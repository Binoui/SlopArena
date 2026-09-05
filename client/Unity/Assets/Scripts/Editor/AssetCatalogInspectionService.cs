using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class AssetCatalogInspectionResult
{
    [JsonProperty("success")] public bool Success { get; set; }
    [JsonProperty("worksetPath")] public string WorksetPath { get; set; }
    [JsonProperty("outputPath")] public string OutputPath { get; set; }
    [JsonProperty("reportPath")] public string ReportPath { get; set; }
    [JsonProperty("items")] public List<AssetCatalogInspectionItem> Items { get; set; } = new List<AssetCatalogInspectionItem>();
    [JsonProperty("diagnostics")] public List<AssetCatalogInspectionDiagnostic> Diagnostics { get; set; } = new List<AssetCatalogInspectionDiagnostic>();
    [JsonProperty("contactSheetCells")] public List<AssetCatalogContactSheetCell> ContactSheetCells { get; set; } = new List<AssetCatalogContactSheetCell>();
    [JsonProperty("contactSheetPath")] public string ContactSheetPath { get; set; }

    public AssetCatalogInspectionCompactResult ToCompact()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (AssetCatalogInspectionDiagnostic diagnostic in Diagnostics)
            codes.Add(diagnostic.Code);
        foreach (AssetCatalogInspectionItem item in Items)
        {
            foreach (AssetCatalogInspectionDiagnostic diagnostic in item.TechnicalValidation.Diagnostics)
                codes.Add(diagnostic.Code);
            foreach (AssetCatalogInspectionDiagnostic diagnostic in item.VisualEvidence.Diagnostics)
                codes.Add(diagnostic.Code);
        }
        return new AssetCatalogInspectionCompactResult
        {
            Success = Success,
            SelectedCount = Items.Count(x => x.SelectionStatus == "selected"),
            TechnicalPassCount = Items.Count(x => x.TechnicalValidation.Status == "pass"),
            TechnicalFailCount = Items.Count(x => x.TechnicalValidation.Status == "fail"),
            VisualPassCount = Items.Count(x => x.VisualEvidence.Status == "pass"),
            VisualUnavailableCount = Items.Count(x => x.VisualEvidence.Status == "unavailable"),
            DiagnosticCodes = codes.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            InspectionPath = OutputPath,
            ReportPath = ReportPath,
            ContactSheetPath = ContactSheetPath
        };
    }
}

public sealed class AssetCatalogInspectionCompactResult
{
    [JsonProperty("success")] public bool Success { get; set; }
    [JsonProperty("selectedCount")] public int SelectedCount { get; set; }
    [JsonProperty("technicalPassCount")] public int TechnicalPassCount { get; set; }
    [JsonProperty("technicalFailCount")] public int TechnicalFailCount { get; set; }
    [JsonProperty("visualPassCount")] public int VisualPassCount { get; set; }
    [JsonProperty("visualUnavailableCount")] public int VisualUnavailableCount { get; set; }
    [JsonProperty("diagnosticCodes")] public List<string> DiagnosticCodes { get; set; } = new List<string>();
    [JsonProperty("inspectionPath")] public string InspectionPath { get; set; }
    [JsonProperty("reportPath")] public string ReportPath { get; set; }
    [JsonProperty("contactSheetPath")] public string ContactSheetPath { get; set; }
}

public sealed class AssetCatalogInspectionItem
{
    [JsonProperty("sourcePack")] public string SourcePack { get; set; }
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("path")] public string Path { get; set; }
    [JsonProperty("role")] public string Role { get; set; }
    [JsonProperty("score")] public int Score { get; set; }
    [JsonProperty("reasons")] public List<string> Reasons { get; set; } = new List<string>();
    [JsonProperty("selectionStatus")] public string SelectionStatus { get; set; }
    [JsonProperty("technicalValidation")] public AssetCatalogTechnicalValidation TechnicalValidation { get; set; } = new AssetCatalogTechnicalValidation();
    [JsonProperty("visualEvidence")] public AssetCatalogVisualEvidence VisualEvidence { get; set; } = new AssetCatalogVisualEvidence();
    [JsonProperty("metrics")] public AssetCatalogMetrics Metrics { get; set; }
    [JsonProperty("thumbnailPath")] public string ThumbnailPath { get; set; }
}

public sealed class AssetCatalogTechnicalValidation
{
    [JsonProperty("status")] public string Status { get; set; } = "fail";
    [JsonProperty("diagnostics")] public List<AssetCatalogInspectionDiagnostic> Diagnostics { get; set; } = new List<AssetCatalogInspectionDiagnostic>();
}

public sealed class AssetCatalogVisualEvidence
{
    [JsonProperty("status")] public string Status { get; set; } = "unavailable";
    [JsonProperty("diagnostics")] public List<AssetCatalogInspectionDiagnostic> Diagnostics { get; set; } = new List<AssetCatalogInspectionDiagnostic>();
}

public sealed class AssetCatalogInspectionDiagnostic
{
    [JsonProperty("code")] public string Code { get; set; }
    [JsonProperty("message")] public string Message { get; set; }
}

public sealed class AssetCatalogMetrics
{
    [JsonProperty("boundsCenter")] public AssetCatalogVector3 BoundsCenter { get; set; }
    [JsonProperty("boundsDimensions")] public AssetCatalogVector3 BoundsDimensions { get; set; }
    [JsonProperty("rendererCount")] public int RendererCount { get; set; }
    [JsonProperty("colliderCount")] public int ColliderCount { get; set; }
    [JsonProperty("enabledColliderCount")] public int EnabledColliderCount { get; set; }
    [JsonProperty("uniqueMaterialCount")] public int UniqueMaterialCount { get; set; }
    [JsonProperty("materialSlotCount")] public int MaterialSlotCount { get; set; }
    [JsonProperty("supportedShaders")] public List<string> SupportedShaders { get; set; } = new List<string>();
    [JsonProperty("unsupportedShaders")] public List<string> UnsupportedShaders { get; set; } = new List<string>();
    [JsonProperty("missingShaderCount")] public int MissingShaderCount { get; set; }
    [JsonProperty("highestDetailTriangleCount")] public int HighestDetailTriangleCount { get; set; }
    [JsonProperty("lodGroupCount")] public int LodGroupCount { get; set; }
    [JsonProperty("lodLevels")] public List<AssetCatalogLodLevel> LodLevels { get; set; } = new List<AssetCatalogLodLevel>();
    [JsonProperty("missingMeshReferenceCount")] public int MissingMeshReferenceCount { get; set; }
    [JsonProperty("missingMaterialReferenceCount")] public int MissingMaterialReferenceCount { get; set; }
}

public sealed class AssetCatalogVector3
{
    [JsonProperty("x")] public float X { get; set; }
    [JsonProperty("y")] public float Y { get; set; }
    [JsonProperty("z")] public float Z { get; set; }
    public AssetCatalogVector3() { }
    public AssetCatalogVector3(Vector3 value) { X = value.x; Y = value.y; Z = value.z; }
}

public sealed class AssetCatalogLodLevel
{
    [JsonProperty("groupIndex")] public int GroupIndex { get; set; }
    [JsonProperty("levelIndex")] public int LevelIndex { get; set; }
    [JsonProperty("transitionHeight")] public float TransitionHeight { get; set; }
    [JsonProperty("rendererCount")] public int RendererCount { get; set; }
    [JsonProperty("triangleCount")] public int TriangleCount { get; set; }
}

public sealed class AssetCatalogContactSheetCell
{
    [JsonProperty("cellIndex")] public int CellIndex { get; set; }
    [JsonProperty("sourcePack")] public string SourcePack { get; set; }
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("role")] public string Role { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("identityLabel")] public string IdentityLabel { get; set; }
}

public sealed class AssetCatalogWorkset
{
    [JsonProperty("schemaVersion")] public int SchemaVersion { get; set; }
    [JsonProperty("concept")] public string Concept { get; set; }
    [JsonProperty("shortlist")] public List<AssetCatalogWorksetItem> Shortlist { get; set; } = new List<AssetCatalogWorksetItem>();
}

public sealed class AssetCatalogWorksetItem
{
    [JsonProperty("sourcePack")] public string SourcePack { get; set; }
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("path")] public string Path { get; set; }
    [JsonProperty("role")] public string Role { get; set; }
    [JsonProperty("score")] public int Score { get; set; }
    [JsonProperty("reasons")] public List<string> Reasons { get; set; } = new List<string>();
    [JsonProperty("selectionStatus")] public string SelectionStatus { get; set; }
}

public sealed class AssetCatalogInspectionService
{
    private const int ThumbnailSize = 384;
    private const int ContactSheetColumns = 5;
    private const int PixelDistanceThreshold = 24;
    private static readonly Color PreviewBackground = new Color(0.16f, 0.16f, 0.16f, 1f);
    private readonly string _projectRoot;
    private readonly string _repositoryRoot;
    private readonly string _cacheRoot;

    private enum PreviewFraming
    {
        PerspectiveIsometric,
        OrthographicIsometric,
        PerspectiveFront,
        PerspectiveSide
    }

    private sealed class PixelClassification
    {
        public bool Blank;
        public bool TouchesEdge;
    }

    public AssetCatalogInspectionService(string projectRoot)
    {
        _projectRoot = Path.GetFullPath(projectRoot);
        _repositoryRoot = Directory.GetParent(_projectRoot).Parent.FullName;
        _cacheRoot = Path.Combine(_repositoryRoot, ".asset-catalog-cache");
    }

    public AssetCatalogInspectionResult Inspect(string workset, string output, bool renderThumbnails)
    {
        string worksetPath = ResolveCachePath(workset, "workset");
        string outputPath = ResolveCachePath(output, "output");
        AssetCatalogWorkset source = JsonConvert.DeserializeObject<AssetCatalogWorkset>(File.ReadAllText(worksetPath));
        if (source == null || source.SchemaVersion != 1)
            throw new InvalidDataException("unsupported workset schema version");
        if (source.Shortlist == null || source.Shortlist.Count == 0)
            throw new InvalidDataException("workset shortlist must not be empty");

        CleanVisualArtifacts(outputPath);
        var result = new AssetCatalogInspectionResult
        {
            Success = true,
            WorksetPath = RelativeToRepository(worksetPath),
            OutputPath = RelativeToRepository(outputPath)
        };
        foreach (AssetCatalogWorksetItem item in source.Shortlist)
            result.Items.Add(InspectItem(item));

        if (renderThumbnails)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                result.Diagnostics.Add(new AssetCatalogInspectionDiagnostic
                {
                    Code = "THUMBNAIL_GPU_UNAVAILABLE",
                    Message = "Thumbnail rendering requires a GPU-backed Unity Editor."
                });
                MarkVisualUnavailable(result.Items, "THUMBNAIL_GPU_UNAVAILABLE", "GPU-backed thumbnail rendering is unavailable.");
            }
            else
            {
                RenderThumbnails(result, outputPath);
            }
            result.ReportPath = WriteReport(result, outputPath);
        }
        else
        {
            MarkVisualUnavailable(result.Items, "THUMBNAIL_NOT_REQUESTED", "Thumbnail rendering was not requested.");
        }

        result.Success = result.Items.All(x => x.TechnicalValidation.Status == "pass");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, JsonConvert.SerializeObject(result, Formatting.Indented));
        AssetDatabase.Refresh();
        return result;
    }

    internal AssetCatalogInspectionItem InspectSingleForSelfTest(string prefabPath, string sourcePack, string id)
    {
        string guid = AssetDatabase.AssetPathToGUID(prefabPath);
        if (!string.Equals(guid, id, StringComparison.Ordinal))
            throw new InvalidOperationException("Self-test prefab GUID did not match its catalog identity.");
        return InspectItem(new AssetCatalogWorksetItem
        {
            SourcePack = sourcePack,
            Id = id,
            Name = Path.GetFileNameWithoutExtension(prefabPath),
            Path = prefabPath,
            Role = "self-test",
            SelectionStatus = "selected"
        });
    }

    private AssetCatalogInspectionItem InspectItem(AssetCatalogWorksetItem item)
    {
        var output = new AssetCatalogInspectionItem
        {
            SourcePack = item.SourcePack,
            Id = item.Id,
            Name = item.Name,
            Path = item.Path,
            Role = item.Role,
            Score = item.Score,
            Reasons = item.Reasons == null ? new List<string>() : new List<string>(item.Reasons),
            SelectionStatus = item.SelectionStatus,
            TechnicalValidation = new AssetCatalogTechnicalValidation(),
            VisualEvidence = new AssetCatalogVisualEvidence()
        };
        try
        {
            string path = NormalizeAssetPath(item.Path);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                AddTechnicalFailure(output, "ASSET_NOT_FOUND", "Shortlist path is not a prefab.");
                return output;
            }
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                AddTechnicalFailure(output, "ASSET_NOT_FOUND", path);
                return output;
            }
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.Equals(guid, item.Id, StringComparison.Ordinal))
            {
                AddTechnicalFailure(output, "IDENTITY_MISMATCH", $"Catalog id {item.Id} does not match AssetDatabase GUID {guid}.");
                return output;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                contents.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                contents.transform.localScale = Vector3.one;
                output.Metrics = Measure(contents, output);
                if (output.TechnicalValidation.Diagnostics.Count == 0)
                    output.TechnicalValidation.Status = "pass";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }
        catch (Exception ex)
        {
            output.TechnicalValidation.Status = "fail";
            output.TechnicalValidation.Diagnostics.Clear();
            AddTechnicalFailure(output, "INSPECTION_EXCEPTION", ex.Message);
        }
        return output;
    }

    private static AssetCatalogMetrics Measure(GameObject root, AssetCatalogInspectionItem output)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        var metrics = new AssetCatalogMetrics { RendererCount = renderers.Length };
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        metrics.ColliderCount = colliders.Length;
        metrics.EnabledColliderCount = colliders.Count(x => x.enabled);

        Bounds bounds = new Bounds();
        bool hasBounds = false;
        foreach (Renderer renderer in renderers)
        {
            Bounds rendererBounds = renderer.bounds;
            Vector3 center = root.transform.InverseTransformPoint(rendererBounds.center);
            Vector3 extents = rendererBounds.extents;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                if (!IsFinite(corner)) AddTechnicalFailure(output, "INVALID_BOUNDS", "Renderer bounds contain a non-finite value.");
                if (!hasBounds) { bounds = new Bounds(corner, Vector3.zero); hasBounds = true; }
                else bounds.Encapsulate(corner);
            }
            Material[] materials = renderer.sharedMaterials;
            metrics.MaterialSlotCount += materials.Length;
            foreach (Material material in materials)
            {
                if (material == null) { metrics.MissingMaterialReferenceCount++; continue; }
                if (!material.shader) { metrics.MissingShaderCount++; continue; }
                if (material.shader.isSupported) AddUnique(metrics.SupportedShaders, material.shader.name);
                else AddUnique(metrics.UnsupportedShaders, material.shader.name);
            }
        }
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh == null) metrics.MissingMeshReferenceCount++;
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (renderer.sharedMesh == null) metrics.MissingMeshReferenceCount++;

        var uniqueMaterials = new HashSet<Material>();
        foreach (Renderer renderer in renderers)
            foreach (Material material in renderer.sharedMaterials)
                if (material != null) uniqueMaterials.Add(material);
        metrics.UniqueMaterialCount = uniqueMaterials.Count;
        if (!hasBounds) AddTechnicalFailure(output, "NO_RENDERERS", "Prefab contains no renderers.");
        else if (!IsFinite(bounds.center) || !IsFinite(bounds.size) || bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
            AddTechnicalFailure(output, "INVALID_BOUNDS", "Prefab renderer bounds are non-finite or zero.");
        else
        {
            metrics.BoundsCenter = new AssetCatalogVector3(bounds.center);
            metrics.BoundsDimensions = new AssetCatalogVector3(bounds.size);
        }
        if (metrics.MissingMeshReferenceCount != 0 || metrics.MissingMaterialReferenceCount != 0)
            AddTechnicalFailure(output, "MISSING_REFERENCE", "Prefab contains missing mesh or material references.");
        if (metrics.UnsupportedShaders.Count != 0 || metrics.MissingShaderCount != 0)
            AddTechnicalFailure(output, "UNSUPPORTED_SHADER", "Prefab contains unsupported or missing shaders.");

        var lodRenderers = new HashSet<Renderer>();
        LODGroup[] groups = root.GetComponentsInChildren<LODGroup>(true);
        metrics.LodGroupCount = groups.Length;
        for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            LOD[] lods = groups[groupIndex].GetLODs();
            for (int levelIndex = 0; levelIndex < lods.Length; levelIndex++)
            {
                Renderer[] levelRenderers = lods[levelIndex].renderers ?? new Renderer[0];
                foreach (Renderer renderer in levelRenderers) lodRenderers.Add(renderer);
                int triangles = levelIndex == 0 ? levelRenderers.Sum(TriangleCount) : 0;
                metrics.LodLevels.Add(new AssetCatalogLodLevel
                {
                    GroupIndex = groupIndex,
                    LevelIndex = levelIndex,
                    TransitionHeight = lods[levelIndex].screenRelativeTransitionHeight,
                    RendererCount = levelRenderers.Length,
                    TriangleCount = triangles
                });
                if (levelIndex == 0) metrics.HighestDetailTriangleCount += triangles;
            }
        }
        foreach (Renderer renderer in renderers)
            if (!lodRenderers.Contains(renderer)) metrics.HighestDetailTriangleCount += TriangleCount(renderer);
        return metrics;
    }

    private void RenderThumbnails(AssetCatalogInspectionResult result, string outputPath)
    {
        string thumbnailDirectory = Path.Combine(Path.GetDirectoryName(outputPath), "thumbnails");
        Directory.CreateDirectory(thumbnailDirectory);
        List<AssetCatalogInspectionItem> successful = result.Items
            .Where(x => x.TechnicalValidation.Status == "pass")
            .ToList();
        for (int index = 0; index < successful.Count; index++)
        {
            AssetCatalogInspectionItem item = successful[index];
            string fileName = Sanitize(item.SourcePack) + "--" + Sanitize(item.Id) + ".png";
            string fullPath = Path.Combine(thumbnailDirectory, fileName);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NormalizeAssetPath(item.Path));
            if (prefab == null)
            {
                AddVisualFailure(item, "THUMBNAIL_RENDER_EXCEPTION", "Prefab disappeared before thumbnail rendering.");
                continue;
            }
            if (TryRenderOne(prefab, item.Metrics, fullPath, item))
            {
                item.ThumbnailPath = RelativeToRepository(fullPath);
                item.VisualEvidence.Status = "pass";
            }
            else
            {
                item.VisualEvidence.Status = "unavailable";
            }
        }

        List<AssetCatalogInspectionItem> rendered = successful.Where(x => x.VisualEvidence.Status == "pass").ToList();
        if (rendered.Count == 0) return;
        int rows = (rendered.Count + ContactSheetColumns - 1) / ContactSheetColumns;
        var sheet = new Texture2D(ContactSheetColumns * ThumbnailSize, rows * ThumbnailSize, TextureFormat.RGBA32, false);
        sheet.SetPixels(Enumerable.Repeat(PreviewBackground, ContactSheetColumns * ThumbnailSize * rows * ThumbnailSize).ToArray());
        try
        {
            for (int index = 0; index < rendered.Count; index++)
            {
                AssetCatalogInspectionItem item = rendered[index];
                string thumbnail = Path.Combine(thumbnailDirectory, Sanitize(item.SourcePack) + "--" + Sanitize(item.Id) + ".png");
                var image = new Texture2D(2, 2);
                try
                {
                    image.LoadImage(File.ReadAllBytes(thumbnail));
                    sheet.SetPixels((index % ContactSheetColumns) * ThumbnailSize,
                        rows * ThumbnailSize - (index / ContactSheetColumns + 1) * ThumbnailSize,
                        ThumbnailSize, ThumbnailSize, image.GetPixels());
                }
                finally { UnityEngine.Object.DestroyImmediate(image); }
                result.ContactSheetCells.Add(new AssetCatalogContactSheetCell
                {
                    CellIndex = index,
                    SourcePack = item.SourcePack,
                    Id = item.Id,
                    Role = item.Role,
                    Name = item.Name,
                    IdentityLabel = IdentityLabel(item)
                });
            }
            sheet.Apply();
            string contactSheet = Path.Combine(Path.GetDirectoryName(outputPath), "contact-sheet.png");
            File.WriteAllBytes(contactSheet, sheet.EncodeToPNG());
            result.ContactSheetPath = RelativeToRepository(contactSheet);
        }
        finally { UnityEngine.Object.DestroyImmediate(sheet); }
    }

    private static bool TryRenderOne(GameObject prefab, AssetCatalogMetrics metrics, string path, AssetCatalogInspectionItem item)
    {
        PreviewFraming[] framings =
        {
            PreviewFraming.PerspectiveIsometric,
            PreviewFraming.OrthographicIsometric,
            PreviewFraming.PerspectiveFront,
            PreviewFraming.PerspectiveSide
        };
        foreach (PreviewFraming framing in framings)
        {
            Texture2D image = null;
            try
            {
                image = RenderAttempt(prefab, metrics, framing);
                PixelClassification pixels = ClassifyPixels(image);
                if (pixels.Blank)
                {
                    AddVisualFailure(item, "THUMBNAIL_BLANK", "Preview contains no foreground pixels.");
                    continue;
                }
                if (pixels.TouchesEdge)
                {
                    AddVisualFailure(item, "THUMBNAIL_CLIPPED", "Preview foreground touches the image edge; trying fallback framing.");
                    continue;
                }
                File.WriteAllBytes(path, image.EncodeToPNG());
                return true;
            }
            catch (Exception ex)
            {
                AddVisualFailure(item, "THUMBNAIL_RENDER_EXCEPTION", ex.Message);
            }
            finally
            {
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
        }
        return false;
    }

    private static Texture2D RenderAttempt(GameObject prefab, AssetCatalogMetrics metrics, PreviewFraming framing)
    {
        PreviewRenderUtility utility = new PreviewRenderUtility(true);
        Material previewMaterial = null;
        GameObject instance = null;
        Texture preview = null;
        Texture2D image = null;
        try
        {
            utility.cameraFieldOfView = 30f;
            utility.camera.clearFlags = CameraClearFlags.Color;
            utility.camera.backgroundColor = PreviewBackground;
            utility.lights[0].type = LightType.Directional;
            utility.lights[0].intensity = 1.2f;
            utility.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            utility.lights[1].type = LightType.Directional;
            utility.lights[1].intensity = 0.7f;
            utility.lights[1].transform.rotation = Quaternion.Euler(-25f, 145f, 0f);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No preview shader is available.");
            previewMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Color previewColor = new Color(0.55f, 0.58f, 0.62f, 1f);
            if (previewMaterial.HasProperty("_BaseColor")) previewMaterial.SetColor("_BaseColor", previewColor);
            if (previewMaterial.HasProperty("_Color")) previewMaterial.SetColor("_Color", previewColor);
            instance = UnityEngine.Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                int materialCount = Mathf.Max(renderer.sharedMaterials.Length, 1);
                renderer.sharedMaterials = Enumerable.Repeat(previewMaterial, materialCount).ToArray();
            }
            utility.AddSingleGO(instance);
            ConfigureCamera(utility.camera, metrics, framing);
            utility.BeginPreview(new Rect(0, 0, ThumbnailSize, ThumbnailSize), GUIStyle.none);
            utility.camera.Render();
            preview = utility.EndPreview();
            image = new Texture2D(ThumbnailSize, ThumbnailSize, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (preview is RenderTexture renderTexture)
                {
                    RenderTexture.active = renderTexture;
                    image.ReadPixels(new Rect(0, 0, ThumbnailSize, ThumbnailSize), 0, 0);
                    image.Apply();
                }
                else if (preview is Texture2D texture)
                {
                    Graphics.CopyTexture(texture, image);
                }
                else throw new InvalidOperationException("PreviewRenderUtility returned an unsupported preview texture.");
            }
            finally { RenderTexture.active = previous; }
            return image;
        }
        catch
        {
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            throw;
        }
        finally
        {
            if (preview != null) UnityEngine.Object.DestroyImmediate(preview);
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (previewMaterial != null) UnityEngine.Object.DestroyImmediate(previewMaterial);
            utility.Cleanup();
        }
    }

    private static void ConfigureCamera(Camera camera, AssetCatalogMetrics metrics, PreviewFraming framing)
    {
        Vector3 center = new Vector3(metrics.BoundsCenter.X, metrics.BoundsCenter.Y, metrics.BoundsCenter.Z);
        Vector3 dimensions = new Vector3(metrics.BoundsDimensions.X, metrics.BoundsDimensions.Y, metrics.BoundsDimensions.Z);
        float radius = Mathf.Max(dimensions.magnitude * 0.5f, 0.01f);
        Vector3 direction = framing == PreviewFraming.PerspectiveSide ? Vector3.right :
            framing == PreviewFraming.PerspectiveFront ? Vector3.back : new Vector3(1f, 0.75f, -1f).normalized;
        camera.transform.position = center + direction * (radius / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.25f);
        camera.transform.LookAt(center);
        if (framing == PreviewFraming.OrthographicIsometric)
        {
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(dimensions.magnitude * 0.58f, 0.01f);
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = Mathf.Max(radius * 4f, 1f);
        }
        else
        {
            camera.orthographic = false;
            float distance = Vector3.Distance(camera.transform.position, center);
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 1.35f);
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.1f, distance + radius * 1.35f);
        }
    }

    private static PixelClassification ClassifyPixels(Texture2D image)
    {
        Color32 background = PreviewBackground;
        Color32[] pixels = image.GetPixels32();
        int minX = image.width;
        int minY = image.height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < image.height; y++)
        for (int x = 0; x < image.width; x++)
        {
            Color32 pixel = pixels[y * image.width + x];
            int distance = Mathf.Abs(pixel.r - background.r) + Mathf.Abs(pixel.g - background.g) + Mathf.Abs(pixel.b - background.b);
            if (pixel.a > 5 && distance > PixelDistanceThreshold)
            {
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }
        return new PixelClassification
        {
            Blank = maxX < 0,
            TouchesEdge = maxX == image.width - 1 || minX == 0 || maxY == image.height - 1 || minY == 0
        };
    }

    private string WriteReport(AssetCatalogInspectionResult result, string outputPath)
    {
        string reportPath = Path.Combine(Path.GetDirectoryName(outputPath), "report.html");
        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Asset inspection report</title><style>");
        html.Append("body{font:14px sans-serif;background:#17191c;color:#eee;margin:24px}h1{margin-top:0}.grid{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:12px}.card{background:#25292e;border:1px solid #3b424a;border-radius:6px;padding:8px}.card img,.blank{width:100%;aspect-ratio:1;background:#292929;object-fit:contain}.label{font-weight:600;margin-top:6px}.muted{color:#aab2bb;font-size:12px}.status{margin-top:4px}.pass{color:#7ee787}.unavailable{color:#f2cc60}.fail{color:#ff7b72}.summary{display:flex;gap:10px;flex-wrap:wrap}.summary .card{min-width:130px}.diagnostics{color:#ff7b72}</style></head><body>");
        html.Append("<h1>Asset inspection report</h1><div class=\"grid\">");
        foreach (AssetCatalogInspectionItem item in result.Items)
        {
            html.Append("<article class=\"card\">");
            string thumbnailPath = item.ThumbnailPath == null ? null : Path.Combine(_repositoryRoot, item.ThumbnailPath.Replace('/', Path.DirectorySeparatorChar));
            if (thumbnailPath != null && File.Exists(thumbnailPath))
                html.Append("<img alt=\"").Append(Escape(item.Name)).Append("\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(File.ReadAllBytes(thumbnailPath))).Append("\">");
            else
                html.Append("<div class=\"blank\"></div>");
            html.Append("<div class=\"label\">").Append(Escape(IdentityLabel(item))).Append("</div>");
            html.Append("<div class=\"muted\">").Append(Escape(item.Role)).Append(" · ").Append(Escape(item.SourcePack)).Append("</div>");
            html.Append("<div class=\"status ").Append(Escape(item.TechnicalValidation.Status)).Append("\">technical: ").Append(Escape(item.TechnicalValidation.Status)).Append("</div>");
            html.Append("<div class=\"status ").Append(Escape(item.VisualEvidence.Status)).Append("\">visual: ").Append(Escape(item.VisualEvidence.Status)).Append("</div>");
            html.Append("<div class=\"status\">selection: ").Append(Escape(item.SelectionStatus)).Append("</div>");
            AppendDiagnosticCodes(html, item.TechnicalValidation.Diagnostics.Concat(item.VisualEvidence.Diagnostics));
            html.Append("<details><summary>identity</summary><code>").Append(Escape(item.SourcePack + "/" + item.Id)).Append("</code></details></article>");
        }
        html.Append("</div><h2>Summary</h2><div class=\"summary\">");
        html.Append("<div class=\"card\">selected: ").Append(result.Items.Count(x => x.SelectionStatus == "selected")).Append("</div>");
        html.Append("<div class=\"card\">technical pass: ").Append(result.Items.Count(x => x.TechnicalValidation.Status == "pass")).Append("</div>");
        html.Append("<div class=\"card\">visual pass: ").Append(result.Items.Count(x => x.VisualEvidence.Status == "pass")).Append("</div></div>");
        html.Append("<h2>Role counts</h2><div class=\"summary\">");
        foreach (var role in result.Items.GroupBy(x => x.Role).OrderBy(x => x.Key, StringComparer.Ordinal))
            html.Append("<div class=\"card\">").Append(Escape(role.Key)).Append(": ").Append(role.Count()).Append("</div>");
        html.Append("</div><h2>Contact sheet</h2>");
        if (result.ContactSheetPath != null)
        {
            string contactSheet = Path.Combine(_repositoryRoot, result.ContactSheetPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(contactSheet))
                html.Append("<img alt=\"contact sheet\" style=\"max-width:100%\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(File.ReadAllBytes(contactSheet))).Append("\">");
        }
        html.Append("<h2>Diagnostics</h2>");
        AppendDiagnosticCodes(html, result.Diagnostics.Concat(result.Items.SelectMany(x => x.TechnicalValidation.Diagnostics.Concat(x.VisualEvidence.Diagnostics))));
        html.Append("</body></html>");
        File.WriteAllText(reportPath, html.ToString());
        return RelativeToRepository(reportPath);
    }

    private static void AppendDiagnosticCodes(StringBuilder html, IEnumerable<AssetCatalogInspectionDiagnostic> diagnostics)
    {
        string[] codes = diagnostics.Select(x => x.Code).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (codes.Length == 0) return;
        html.Append("<div class=\"diagnostics\">diagnostics: ").Append(Escape(string.Join(", ", codes))).Append("</div>");
    }

    private void CleanVisualArtifacts(string outputPath)
    {
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (outputDirectory == null) throw new InvalidDataException("output path has no directory");
        string thumbnailDirectory = Path.Combine(outputDirectory, "thumbnails");
        if (Directory.Exists(thumbnailDirectory)) Directory.Delete(thumbnailDirectory, true);
        foreach (string name in new[] { "contact-sheet.png", "report.html" })
        {
            string path = Path.Combine(outputDirectory, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void MarkVisualUnavailable(IEnumerable<AssetCatalogInspectionItem> items, string code, string message)
    {
        foreach (AssetCatalogInspectionItem item in items)
        {
            item.VisualEvidence.Status = "unavailable";
            AddVisualFailure(item, code, message);
        }
    }

    private static void AddTechnicalFailure(AssetCatalogInspectionItem item, string code, string message)
    {
        if (!item.TechnicalValidation.Diagnostics.Any(x => x.Code == code))
            item.TechnicalValidation.Diagnostics.Add(new AssetCatalogInspectionDiagnostic { Code = code, Message = message });
        item.TechnicalValidation.Status = "fail";
    }

    private static void AddVisualFailure(AssetCatalogInspectionItem item, string code, string message)
    {
        if (!item.VisualEvidence.Diagnostics.Any(x => x.Code == code))
            item.VisualEvidence.Diagnostics.Add(new AssetCatalogInspectionDiagnostic { Code = code, Message = message });
    }

    private string ResolveCachePath(string path, string label)
    {
        string full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_repositoryRoot, path));
        if (!IsUnder(full, _cacheRoot)) throw new InvalidDataException($"{label} path must stay under .asset-catalog-cache");
        return full;
    }

    private string NormalizeAssetPath(string path)
    {
        string normalized = (path ?? "").Replace('\\', '/');
        if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) || normalized.Contains("..")) throw new InvalidDataException("asset path must be below Assets/");
        return normalized;
    }

    private string RelativeToRepository(string path) => Path.GetRelativePath(_repositoryRoot, path).Replace('\\', '/');
    private static string IdentityLabel(AssetCatalogInspectionItem item) => (item.Name ?? "asset") + " · " + ShortIdentity(item.Id);
    private static string ShortIdentity(string id) => string.IsNullOrEmpty(id) ? "unknown" : id.Substring(0, Mathf.Min(8, id.Length));
    private static string Escape(string value) => (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    private static string Sanitize(string value) => string.Concat((value ?? "").Select(x => char.IsLetterOrDigit(x) || x == '-' || x == '_' ? x : '_'));
    private static bool IsUnder(string path, string root) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool IsFinite(Vector3 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    private static int TriangleCount(Renderer renderer)
    {
        Mesh mesh = renderer is SkinnedMeshRenderer ? ((SkinnedMeshRenderer)renderer).sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
        return mesh == null ? 0 : mesh.triangles.Length / 3;
    }
    private static void AddUnique(List<string> values, string value) { if (!values.Contains(value, StringComparer.Ordinal)) values.Add(value); }
}
