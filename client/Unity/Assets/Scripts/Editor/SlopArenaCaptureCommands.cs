using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SlopArena.EditorTools
{
    /// <summary>
    /// Writes screenshots to non-imported evidence directories without AssetDatabase.Refresh.
    /// Screen capture observes a new Game camera render unless the caller explicitly requests
    /// the last rendered frame; saved PNGs never imply a fresh render on their own.
    /// </summary>
    public static class SlopArenaCaptureCommands
    {
        private static readonly string[] AllowedPrefixes =
        {
            ".impeccable/",
            ".ability-lab-cache/",
            "client/Unity/Temp/",
            "tmp/"
        };

        [CliCommand(
            "sloparena.capture.game-view",
            "Capture a screenshot of Unity menus, HUD or gameplay incl. overlay UI, or a camera/Scene View, without importing assets. Fresh screen capture awaits a render; --last-frame explicitly accepts unverified existing content.",
            MainThreadRequired = true,
            Tags = new[] { "capture", "screenshot", "frontend/ui" })]
        public static async Task<SlopArenaCaptureResult> Capture(
            [CliArg("output", "Repository-relative PNG path below .impeccable/, .ability-lab-cache/, client/Unity/Temp/, or tmp/.", Required = true)] string output,
            [CliArg("source", "'screen' (Play Mode backbuffer incl. overlay UI), 'camera', or 'scene'.")] string source = "screen",
            [CliArg("width", "Output width in pixels (64-4096).")] int width = 1920,
            [CliArg("height", "Output height in pixels (64-4096).")] int height = 1080,
            [CliArg("camera", "Camera name for source=camera; defaults to Camera.main, else the first enabled camera.")] string camera = null,
            [CliArg("step", "Authorize one simulation frame when paused, then await rendering. Screen source only.")] bool step = false,
            [CliArg("last-frame", "Read the existing Game View target without advancing simulation; freshness is explicitly unverified. Screen source only.")] bool lastFrame = false)
        {
            if (width is < 64 or > 4096 || height is < 64 or > 4096)
                throw new ArgumentException("Capture width and height must each be between 64 and 4096.");

            string normalizedSource = (source ?? "screen").Trim().ToLowerInvariant();
            if (normalizedSource is not ("screen" or "camera" or "scene"))
                throw new ArgumentException($"Unknown source '{source}'. Use 'screen', 'camera', or 'scene'.");
            if (step && lastFrame)
                throw new ArgumentException("--step and --last-frame are mutually exclusive.");
            if (normalizedSource != "screen" && (step || lastFrame))
                throw new ArgumentException("--step and --last-frame apply only to source=screen.");
            if (normalizedSource == "screen" && !EditorApplication.isPlaying)
                throw new InvalidOperationException(
                    "source=\"screen\" requires Play Mode (the composited backbuffer exists only at runtime); use source=\"camera\" or \"scene\" in Edit Mode.");

            string absolutePath = ResolveOutput(output, out string normalizedOutput);

            GuardHasGpu();
            bool stepped = false;
            RenderTexture screenTarget = null;
            if (normalizedSource == "screen")
            {
                var view = ResolveGameView();
                if (lastFrame)
                    screenTarget = GetGameViewTargetTexture(view);
                else
                {
                    if (EditorApplication.isPaused && !step)
                        throw new InvalidOperationException(
                            "Fresh screen capture cannot advance a paused session implicitly. Use --step to authorize one frame, " +
                            "or --last-frame to save the existing rendered content with unverified freshness.");
                    stepped = step && EditorApplication.isPaused;
                    screenTarget = await WaitForGameRender(view, stepped);
                }
            }
            byte[] png = normalizedSource switch
            {
                "screen" => RenderScreenToPng(screenTarget, width, height),
                "camera" => RenderCameraToPng(ResolveCamera(camera), width, height),
                _ => RenderCameraToPng(ResolveSceneCamera(), width, height)
            };

            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            try
            {
                using var stream = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(png, 0, png.Length);
            }
            catch (IOException ex)
            {
                throw new IOException($"Could not create '{normalizedOutput}': {ex.Message}", ex);
            }

            return new SlopArenaCaptureResult
            {
                Success = true,
                Source = normalizedSource,
                Output = normalizedOutput,
                AbsolutePath = absolutePath,
                Width = width,
                Height = height,
                Bytes = png.Length,
                Freshness = normalizedSource == "screen"
                    ? (lastFrame ? "last-rendered-unverified" : "observed-game-render")
                    : "camera-render",
                Scene = SceneManager.GetActiveScene().name,
                Frame = Time.frameCount,
                RenderedFrame = Time.renderedFrameCount,
                Stepped = stepped
            };
        }

        // ---- path sandbox ----------------------------------------------------------

        private static string ResolveOutput(string output, out string normalized)
        {
            if (string.IsNullOrWhiteSpace(output))
                throw new ArgumentException("output is required.");
            normalized = output.Trim().Replace('\\', '/');
            if (Path.IsPathRooted(output))
                throw new ArgumentException("output must be repository-relative, for example .impeccable/review/<task>/hud.png.");
            if (normalized.Split('/').Any(segment => segment == ".."))
                throw new ArgumentException("output must not contain '..' traversal.");
            if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("output must be a .png file path.");

            string repoRoot = RepositoryRoot();
            string absolutePath = Path.GetFullPath(Path.Combine(repoRoot, normalized));
            if (!IsUnder(absolutePath, repoRoot))
                throw new ArgumentException("output resolves outside the repository.");

            if (IsUnder(absolutePath, Application.dataPath))
                throw new ArgumentException(
                    "output must not be under client/Unity/Assets: PNGs there are imported and can interrupt a live session. " +
                    "Use one of: " + string.Join(", ", AllowedPrefixes));

            bool allowed = AllowedPrefixes.Any(prefix =>
                IsUnder(absolutePath, Path.Combine(repoRoot, prefix.TrimEnd('/'))));
            if (!allowed)
                throw new ArgumentException("output must be below one of: " + string.Join(", ", AllowedPrefixes));

            if (File.Exists(absolutePath))
                throw new IOException($"Output already exists: {normalized}. Choose a new file; captures never overwrite.");

            for (var directory = Path.GetDirectoryName(absolutePath);
                 directory != null && IsUnder(directory, repoRoot);
                 directory = Path.GetDirectoryName(directory))
            {
                if (Directory.Exists(directory)
                    && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException(
                        $"Output path crosses the symlink '{directory}'; choose a real directory below the repository.");
            }

            return absolutePath;
        }

        /// <summary>Game repository root (the directory that contains client/, content-cooked/, ...).</summary>
        private static string RepositoryRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static bool IsUnder(string path, string root)
        {
            string candidate = Path.GetFullPath(path).TrimEnd('/', '\\');
            string baseDirectory = Path.GetFullPath(root).TrimEnd('/', '\\');
            return candidate.Equals(baseDirectory, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(baseDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(baseDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // ---- render sources --------------------------------------------------------

        private static void GuardHasGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("No GPU available (batchmode/headless); cannot capture.");
        }

        private static Camera ResolveCamera(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var byName = Camera.allCameras.FirstOrDefault(candidate => candidate.name == name)
                    ?? UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .FirstOrDefault(candidate => candidate.name == name);
                if (byName == null)
                    throw new ArgumentException($"No camera named '{name}' found.");
                return byName;
            }
            if (Camera.main != null)
                return Camera.main;
            var first = Camera.allCameras.FirstOrDefault();
            if (first == null)
                throw new InvalidOperationException("No enabled camera found to capture.");
            return first;
        }

        private static Camera ResolveSceneCamera()
        {
            var sceneCamera = SceneView.lastActiveSceneView?.camera;
            if (sceneCamera == null)
                throw new InvalidOperationException("No active Scene View to capture.");
            return sceneCamera;
        }

        private static byte[] RenderCameraToPng(Camera camera, int width, int height)
        {
            var renderTexture = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                return ReadPng(width, height);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static byte[] ReadPng(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                return ImageConversion.EncodeToPNG(texture);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // Unity's PlayModeView.RenderView scopes GetRenderingView() around its own camera
        // rendering. Bind the callback to that exact view/target, not an unrelated Game camera.
        private static async Task<RenderTexture> WaitForGameRender(EditorWindow gameView, bool step)
        {
            var completion = new TaskCompletionSource<RenderTexture>();
            int initialFrame = Time.frameCount;
            int sceneHandle = SceneManager.GetActiveScene().handle;
            bool paused = EditorApplication.isPaused;
            double deadline = EditorApplication.timeSinceStartup + 5d;
            bool rendered = false;
            int updatesAfterRender = 0;
            RenderTexture observedTarget = null;
            var playModeView = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
            var getRenderingView = playModeView?.GetMethod("GetRenderingView",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (getRenderingView == null)
                throw new InvalidOperationException("Unity cannot identify the rendering Game View. Use --last-frame only if an unverified snapshot is intended.");

            void Observe(Camera candidate)
            {
                if (rendered || candidate == null || candidate.cameraType != CameraType.Game
                    || !candidate.isActiveAndEnabled || Time.frameCount <= initialFrame)
                    return;
                if (!ReferenceEquals(getRenderingView.Invoke(null, null), gameView))
                    return;
                observedTarget = GetGameViewTargetTexture(gameView);
                rendered = observedTarget != null;
            }

            void ObservePipeline(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
            {
                foreach (var candidate in cameras)
                    Observe(candidate);
            }

            void Update()
            {
                try
                {
                    if (!EditorApplication.isPlaying || EditorApplication.isPaused != paused
                        || SceneManager.GetActiveScene().handle != sceneHandle || gameView == null)
                        completion.TrySetException(new InvalidOperationException(
                            "Editor mode, pause state, scene or Game View changed during capture. Inspect sloparena.ui.status and retry in a stable session."));
                    else if (ResolveGameView() != gameView)
                        completion.TrySetException(new InvalidOperationException("The main Game View changed during capture. Retry with the intended view selected."));
                    else if (rendered && ++updatesAfterRender >= 2)
                    {
                        if (GetGameViewTargetTexture(gameView) != observedTarget)
                            completion.TrySetException(new InvalidOperationException("The rendered Game View target was replaced during capture. Retry after viewport changes settle."));
                        else
                            completion.TrySetResult(observedTarget);
                    }
                    else if (EditorApplication.timeSinceStartup >= deadline)
                        completion.TrySetException(new InvalidOperationException(
                            "No completed render of the selected Game View was observed within 5 seconds. Make that view visible and retry; " +
                            "use --step for a paused session, or --last-frame only if existing unverified content is intended."));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }

            Camera.onPostRender += Observe;
            RenderPipelineManager.endContextRendering += ObservePipeline;
            EditorApplication.update += Update;
            try
            {
                gameView.Repaint();
                if (step)
                    EditorApplication.Step();
                return await completion.Task;
            }
            finally
            {
                Camera.onPostRender -= Observe;
                RenderPipelineManager.endContextRendering -= ObservePipeline;
                EditorApplication.update -= Update;
            }
        }

        /// <summary>
        /// Reads the composited Game View target. Missing targets fail instead of silently using
        /// ScreenCapture's focus-dependent fallback, which cannot prove the requested surface.
        /// </summary>
        private static byte[] RenderScreenToPng(RenderTexture source, int width, int height)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("source=\"screen\" requires Play Mode.");
            var output = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previousActive = RenderTexture.active;
            if (source == null || !source.IsCreated())
            {
                RenderTexture.ReleaseTemporary(output);
                throw new InvalidOperationException(
                    "No rendered Game View target is available. Open a visible Game View and render a frame before capturing.");
            }
            try
            {
                // Capture waits for an observed render, or explicitly labels existing content.
                if (SystemInfo.graphicsUVStartsAtTop)
                    Graphics.Blit(source, output, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else
                    Graphics.Blit(source, output);
                RenderTexture.active = output;
                return ReadPng(width, height);
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(output);
            }
        }

        private static EditorWindow ResolveGameView()
        {
            var playModeView = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
            var getMain = playModeView?.GetMethod("GetMainPlayModeView",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            var view = getMain?.Invoke(null, null) as EditorWindow;
            if (view == null)
                throw new InvalidOperationException("No main Game View is available. Open the intended Game View before capturing.");
            return view;
        }

        private static RenderTexture GetGameViewTargetTexture(EditorWindow view)
        {
            try
            {
                var playModeView = Type.GetType("UnityEditor.PlayModeView,UnityEditor");
                if (view == null)
                    return null;
                var field = playModeView.GetField("m_TargetTexture",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var renderTexture = field?.GetValue(view) as RenderTexture;
                return renderTexture != null && renderTexture.IsCreated() ? renderTexture : null;
            }
            catch
            {
                return null;
            }
        }

    }

    public sealed class SlopArenaCaptureResult
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("source")] public string Source { get; set; }
        [JsonProperty("output")] public string Output { get; set; }
        [JsonProperty("absolutePath")] public string AbsolutePath { get; set; }
        [JsonProperty("width")] public int Width { get; set; }
        [JsonProperty("height")] public int Height { get; set; }
        [JsonProperty("bytes")] public int Bytes { get; set; }
        [JsonProperty("freshness")] public string Freshness { get; set; }
        [JsonProperty("scene")] public string Scene { get; set; }
        [JsonProperty("frame")] public int Frame { get; set; }
        [JsonProperty("renderedFrame")] public int RenderedFrame { get; set; }
        [JsonProperty("stepped")] public bool Stepped { get; set; }
    }
}
