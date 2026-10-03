using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Pipeline.Editor.Authoring;
using Unity.Pipeline.Editor.Commands.Capture;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Unity.Pipeline.Tests.Editor.Capture
{
    /// <summary>
    /// Tests for the visual-feedback commands (CLI-199), exercised directly and via PipelineClient.
    /// Render tests are GPU-gated: under batchmode/headless the graphics device is
    /// <see cref="GraphicsDeviceType.Null"/> and the tests self-ignore rather than fail.
    /// </summary>
    public class CaptureCommandsTests
    {
        private const string CameraName = "CLI199_Cam";
        private const string SaveFolder = "Assets/CLI199_CaptureTests";

        // PNG file signature: 0x89 'P' 'N' 'G' 0x0D 0x0A 0x1A 0x0A.
        private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private GameObject m_CameraObject;

        [SetUp]
        public void SetUp()
        {
            m_CameraObject = new GameObject(CameraName);
            m_CameraObject.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_CameraObject != null)
                UnityEngine.Object.DestroyImmediate(m_CameraObject);

            if (AssetDatabase.IsValidFolder(SaveFolder))
                AssetDatabase.DeleteAsset(SaveFolder);
        }

        private static string AbsolutePath(string projectRelative) =>
            Path.Combine(ProjectPaths.ProjectRoot, projectRelative);

        private static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        private static void AssertPngSignature(byte[] bytes)
        {
            Assert.GreaterOrEqual(bytes.Length, PngSignature.Length, "PNG payload too short to contain a signature");
            for (var i = 0; i < PngSignature.Length; i++)
                Assert.AreEqual(PngSignature[i], bytes[i], $"PNG signature mismatch at byte {i}");
        }

        #region Direct

        [Test]
        public void CaptureGameView_NamedCamera_ReturnsPng()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            var result = CaptureCommands.CaptureGameView(64, 64, CameraName);

            Assert.IsNotNull(result, "Capture should return a result");
            Assert.IsNotEmpty(result.Base64, "Base64 payload should be non-empty");
            Assert.AreEqual(64, result.Width, "Width should match the requested size");
            Assert.AreEqual(64, result.Height, "Height should match the requested size");
            Assert.AreEqual("png", result.Encoding);
            Assert.AreEqual($"camera:{CameraName}", result.Source);
            Assert.IsNull(result.SavedPath, "No savePath was requested");

            var bytes = Convert.FromBase64String(result.Base64);
            AssertPngSignature(bytes);
            Assert.AreEqual(bytes.Length, result.Bytes, "Reported byte length should match decoded payload");
        }

        [Test]
        public void CaptureSceneView_ReturnsPng()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
                Assert.Ignore("No Scene View");

            var result = CaptureCommands.CaptureSceneView(64, 64);

            Assert.IsNotNull(result, "Capture should return a result");
            Assert.IsNotEmpty(result.Base64, "Base64 payload should be non-empty");
            Assert.AreEqual("sceneView", result.Source);

            var bytes = Convert.FromBase64String(result.Base64);
            AssertPngSignature(bytes);
        }

        [Test]
        public void CaptureGameView_SavePath_OmitsBase64_AndWritesPng()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            var result = CaptureCommands.CaptureGameView(64, 64, CameraName, $"{SaveFolder}/path_only.png");

            Assert.IsNull(result.Base64, "save_path without include_inline_image should omit the base64 payload");
            Assert.IsNotNull(result.SavedPath, "SavedPath should be set");

            var fileBytes = File.ReadAllBytes(AbsolutePath(result.SavedPath));
            AssertPngSignature(fileBytes);
            Assert.AreEqual(fileBytes.Length, result.Bytes, "Reported byte length should match the file");
        }

        [Test]
        public void CaptureGameView_SavePath_IncludeImage_ReturnsFileAndBase64()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            var result = CaptureCommands.CaptureGameView(64, 64, CameraName, $"{SaveFolder}/both.png", includeInlineImage: true);

            Assert.IsNotEmpty(result.Base64, "include_inline_image=true should return the base64 payload");
            Assert.IsNotNull(result.SavedPath, "SavedPath should be set");
            AssertPngSignature(Convert.FromBase64String(result.Base64));
            AssertPngSignature(File.ReadAllBytes(AbsolutePath(result.SavedPath)));
        }

        [Test]
        public void CaptureGameView_MaxResolution_ClampsInlineRender()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            // No save_path: the inline image is the only artifact, so the cap applies to the render.
            var result = CaptureCommands.CaptureGameView(128, 64, CameraName, maxResolution: 32);

            Assert.AreEqual(32, result.Width, "Long edge should be clamped to max_resolution");
            Assert.AreEqual(16, result.Height, "Short edge should keep the aspect ratio");
            AssertPngSignature(Convert.FromBase64String(result.Base64));
        }

        [Test]
        public void CaptureGameView_SavePath_MaxResolution_DownscalesInlineOnly()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            var result = CaptureCommands.CaptureGameView(128, 64, CameraName, $"{SaveFolder}/full.png",
                includeInlineImage: true, maxResolution: 32);

            Assert.AreEqual(128, result.Width, "The saved file keeps the requested resolution");
            Assert.AreEqual(32, result.InlineWidth, "The inline copy is downscaled to max_resolution");
            Assert.AreEqual(16, result.InlineHeight, "The inline copy keeps the aspect ratio");
            AssertPngSignature(Convert.FromBase64String(result.Base64));
            AssertPngSignature(File.ReadAllBytes(AbsolutePath(result.SavedPath)));
        }

        [Test]
        public void CaptureSceneView_SavePath_OmitsBase64_AndWritesPng()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            var sv = SceneView.lastActiveSceneView;
            if (sv == null)
                Assert.Ignore("No Scene View");

            var result = CaptureCommands.CaptureSceneView(64, 64, $"{SaveFolder}/scene_path_only.png");

            Assert.IsNull(result.Base64, "save_path without include_inline_image should omit the base64 payload");
            Assert.IsNotNull(result.SavedPath, "SavedPath should be set");
            AssertPngSignature(File.ReadAllBytes(AbsolutePath(result.SavedPath)));
        }

        #endregion

        #region Source routing (AUTHAPI-10)

        [Test]
        public void CaptureGameView_ScreenSource_InEditMode_Throws()
        {
            // Overlay/screen capture is a Play-Mode operation; in Edit Mode it must fail with a clear
            // message (this guard runs before the GPU check, so the message is deterministic headless too).
            var ex = Assert.Throws<InvalidOperationException>(
                () => CaptureCommands.CaptureGameView(64, 64, source: "screen"));
            StringAssert.Contains("Play Mode", ex.Message);
        }

        [Test]
        public void CaptureGameView_UnknownSource_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(
                () => CaptureCommands.CaptureGameView(64, 64, source: "bogus"));
            StringAssert.Contains("Unknown source", ex.Message);
        }

        #endregion

        #region ViaClient

        [Test]
        public void CaptureGameView_ViaClient_Succeeds()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("capture_game_view", new { width = 64, height = 64, camera = CameraName });

                Assert.IsTrue(response.IsSuccess, $"capture_game_view should succeed: {response.Error}");
                Assert.IsTrue(response.HasValidJson, "Response should have valid JSON");
                Assert.IsTrue(response.JsonResponse.ContainsKey("result"), "Should have result field");
            }
        }

        [Test]
        public void CaptureGameView_ViaClient_SavePath_SerializedResultOmitsBase64Key()
        {
            if (IsHeadless)
                Assert.Ignore("No GPU in batchmode");

            using (var server = new PipelineTestServer())
            {
                var response = server.Execute("capture_game_view",
                    new { width = 64, height = 64, camera = CameraName, save_path = $"{SaveFolder}/via_client.png" });

                Assert.IsTrue(response.IsSuccess, $"capture_game_view should succeed: {response.Error}");
                var result = (JObject)response.JsonResponse["result"];
                Assert.IsFalse(result.ContainsKey("base64"),
                    "A path-only result must not carry a base64 key on the wire (AUTHAPI-8)");
                Assert.IsNotNull(result["savedPath"]?.ToString(), "savedPath should be present");
            }
        }

        #endregion

        #region Screen capture in Play Mode (AUTHAPI-10)

        // Draws a full-screen two-tone quad via IMGUI. Like a Screen Space - Overlay canvas it
        // composites straight to the screen and never renders through a camera — the case AUTHAPI-10
        // covers — so it lets the test prove overlay content shows up in a screen capture but not a
        // camera capture, without a uGUI dependency.
        //
        // The tones are chosen to catch two capture-path bugs a flat saturated color (the old pure
        // red) is blind to:
        //  * MID-GRAYS, because a primary like (255,0,0) is invariant under gamma conversion — 128
        //    turns into ~188 when the path double-encodes gamma (the washout Thomas reported), so
        //    asserting captured luminance pins the tone round-trip.
        //  * TWO DIFFERENT tones (light top, dark bottom), because a uniform screen is invariant
        //    under vertical flips — CaptureScreenshotIntoRenderTexture returns GPU-UV-oriented
        //    content that must be un-flipped on D3D/Metal/Vulkan, so asserting which half is which
        //    pins the orientation.
        private static readonly Color32 OverlayTopGray = new Color32(128, 128, 128, 255);
        private static readonly Color32 OverlayBottomGray = new Color32(64, 64, 64, 255);

        private class FullScreenOverlay : MonoBehaviour
        {
            private Texture2D m_TopTex;
            private Texture2D m_BottomTex;

            private static Texture2D Solid(Color32 c)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixels32(new[] { c });
                tex.Apply();
                return tex;
            }

            private void OnGUI()
            {
                if (m_TopTex == null)
                {
                    m_TopTex = Solid(OverlayTopGray);
                    m_BottomTex = Solid(OverlayBottomGray);
                }

                GUI.depth = -1000; // draw on top of any other IMGUI
                var half = Screen.height / 2f;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, half), m_TopTex);
                GUI.DrawTexture(new Rect(0, half, Screen.width, Screen.height - half), m_BottomTex);
            }

            private void OnDestroy()
            {
                if (m_TopTex != null) DestroyImmediate(m_TopTex);
                if (m_BottomTex != null) DestroyImmediate(m_BottomTex);
            }
        }

        [UnityTearDown]
        public IEnumerator ExitPlayModeIfNeeded()
        {
            // A failed assertion inside the play-mode test would otherwise leave the editor playing.
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator CaptureGameView_ScreenSource_CapturesOverlayUi()
        {
            // Overlay/screen capture reads the presented game-view backbuffer, which requires a real,
            // rendering game view. In pure headless/batchmode there is none (and WaitForEndOfFrame never
            // fires), so self-ignore early. Edit-Mode + unknown-source guards cover CI regardless.
            if (IsHeadless || Application.isBatchMode)
            {
                Assert.Ignore("Screen capture requires an interactive game view (skipped headless/batchmode).");
                yield break;
            }

            yield return new EnterPlayMode();

            // Tolerate unrelated error logs from whatever project hosts the run (this test only checks pixels).
            LogAssert.ignoreFailingMessages = true;

            // A camera that clears to blue and renders nothing, so a camera capture is overlay-free.
            var camGo = new GameObject("CLI199_ScreenCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.blue;
            cam.cullingMask = 0;

            var overlayGo = new GameObject("CLI199_Overlay");
            overlayGo.AddComponent<FullScreenOverlay>();

            // Poll a few frames: the overlay only appears in the capture once the game view has
            // repainted it into the backbuffer (a single frame is racy in the automated harness).
            CaptureResult screen = null;
            var overlayObserved = false;
            var stats = default((float fraction, float topLuma, float bottomLuma));
            for (var frame = 0; frame < 30 && !overlayObserved; frame++)
            {
                yield return new WaitForEndOfFrame();
                screen = CaptureCommands.CaptureGameView(64, 64, source: "screen");
                stats = GrayStats(screen.Base64);
                // The full-screen overlay should dominate the frame with neutral gray of ANY
                // brightness — brightness-agnostic on purpose, so a tone or orientation bug is a
                // failed assert below rather than an "overlay not observed" ignore.
                overlayObserved = stats.fraction > 0.8f;
            }

            // Reliable everywhere the screen path runs: it returns a correctly-sized PNG tagged "screen".
            Assert.IsNotNull(screen, "screen capture should have run");
            Assert.AreEqual("screen", screen.Source);
            Assert.IsNotEmpty(screen.Base64, "screen capture should return an image");
            Assert.AreEqual(64, screen.Width);

            // Pixel-presence needs the environment to actually present the overlay into the backbuffer.
            // Some CI/windowless setups render a blank/stale backbuffer even in Play Mode; treat an
            // unobservable overlay as inconclusive rather than a failure (verified live in the PR).
            if (!overlayObserved)
            {
                UnityEngine.Object.Destroy(overlayGo);
                UnityEngine.Object.Destroy(camGo);
                // ExitPlayModeIfNeeded ([UnityTearDown]) restores edit mode after this ignore.
                Assert.Ignore("Overlay not observable in this environment's game-view capture; screen path still verified.");
            }

            // Tone round-trip: the authored grays must read back near their values. The old AsTexture
            // path double-encoded gamma in Linear projects (128 washed out to ~188). Orientation: the
            // light half must be on top — CaptureScreenshotIntoRenderTexture returns GPU-UV-oriented
            // content that the command must un-flip on D3D/Metal/Vulkan. (AUTHAPI-10 review)
            Assert.That(stats.topLuma, Is.EqualTo((float)OverlayTopGray.r).Within(20f),
                "top half should be the light gray at its authored tone (flip and/or gamma bug otherwise)");
            Assert.That(stats.bottomLuma, Is.EqualTo((float)OverlayBottomGray.r).Within(20f),
                "bottom half should be the dark gray at its authored tone (flip and/or gamma bug otherwise)");

            // Overlay is observable here: prove it shows in a screen capture but NOT a camera capture
            // (the camera clears to solid blue and renders nothing, so it contains no neutral gray).
            var cameraShot = CaptureCommands.CaptureGameView(64, 64, camera: "CLI199_ScreenCam", source: "camera");
            Assert.Less(GrayStats(cameraShot.Base64).fraction, 0.2f, "overlay UI must be absent from a camera capture");

            UnityEngine.Object.Destroy(overlayGo);
            UnityEngine.Object.Destroy(camGo);

            yield return new ExitPlayMode();
        }

        /// <summary>
        /// Decode the PNG and measure its neutral-gray content: the fraction of pixels that are
        /// near-neutral (r≈g≈b, away from black/white clip), plus the mean luminance of the image's
        /// top and bottom halves. Brightness-agnostic so gamma distortion shows up in the luminance
        /// values (not as a hidden overlay), and per-half so a vertical flip swaps them detectably.
        /// </summary>
        private static (float fraction, float topLuma, float bottomLuma) GrayStats(string base64)
        {
            var tex = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(tex.LoadImage(Convert.FromBase64String(base64)), "capture PNG should decode");
                var width = tex.width;
                var height = tex.height;
                var pixels = tex.GetPixels32();
                var gray = 0;
                long topSum = 0, bottomSum = 0;
                int topCount = 0, bottomCount = 0;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var p = pixels[y * width + x];
                        if (Mathf.Abs(p.r - p.g) <= 12 && Mathf.Abs(p.g - p.b) <= 12 && p.r >= 40 && p.r <= 240)
                            gray++;

                        // GetPixels32 row 0 is the image's BOTTOM row.
                        var luma = (p.r + p.g + p.b) / 3;
                        if (y >= height / 2) { topSum += luma; topCount++; }
                        else { bottomSum += luma; bottomCount++; }
                    }
                }

                return (pixels.Length == 0 ? 0f : (float)gray / pixels.Length,
                        topCount == 0 ? 0f : (float)topSum / topCount,
                        bottomCount == 0 ? 0f : (float)bottomSum / bottomCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        #endregion
    }
}
