using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Client.Tools;
using SlopArena.Shared;

namespace SlopArena.EditorTools;

public sealed class AbilityLabPackageWorkspace
{
    public string PackageRoot { get; private set; } = "";
    public PackageManifestSource Manifest { get; private set; } = null!;
    public CharacterAuthoringDocument Draft { get; private set; } = null!;
    public string LoadedDiskHash { get; private set; } = "";
    public bool IsDirty { get; private set; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics => _diagnostics;
    public string CookedSourceHash { get; private set; } = "";
    public string CookedContentHash { get; private set; } = "";
    public string PackageHash { get; private set; } = "";
    public CharacterAssetCatalog Catalog { get; private set; }
    public CharacterPackageAssemblyResult LastValidAssembly { get; private set; }
    public string Status { get; private set; } = "Unknown";
    private const int MaxUndoDepth = 50;
    private readonly Stack<CharacterPackageSource> _undo = new();
    private readonly Stack<CharacterPackageSource> _redo = new();

    private readonly List<CharacterDiagnostic> _diagnostics = new();

    public bool HasPackage => !string.IsNullOrEmpty(PackageRoot) && Manifest != null && Draft != null;
    public string PackageId => Manifest?.PackageId ?? "";

    public bool NewPackage(string packageId, string displayName, string creator = "Binoui", string license = "MIT", string attribution = "SlopArena")
    {
        if (!MatchContentCatalogBuilder.IsStablePackageId(packageId)) return Fail("id.invalid", "packageId", "Package ID must be a stable lowercase identifier.");
        string root = "Assets/CharacterPackages/" + packageId;
        string full = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), root);
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any()) return Fail("package.exists", root, "Package folder already exists and is not empty.");
        try
        {
            Directory.CreateDirectory(full);
            var source = CharacterPackageSourceCodec.CreateMinimal(packageId, displayName, creator, license, attribution);
            WriteAtomic(Path.Combine(full, "package.json"), Encoding.UTF8.GetBytes(CharacterPackageSourceCodec.SerializeManifest(source.Manifest)));
            WriteAtomic(Path.Combine(full, "character.json"), Encoding.UTF8.GetBytes(CharacterPackageSourceCodec.SerializeCharacter(source.Character)));
            var catalog = ScriptableObject.CreateInstance<CharacterAssetCatalog>();
            catalog.PackageId = packageId; catalog.CatalogSchemaVersion = CharacterAssetCatalog.SchemaVersion; catalog.SampleRate = UnityCharacterAssetCooker.SampleRate; catalog.Rig = null; catalog.Bindings = Array.Empty<CharacterAssetCatalog.AnimationBinding>();
            AssetDatabase.CreateAsset(catalog, root + "/CharacterAssetCatalog.asset");
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            return OpenPackage(root);
        }
        catch (Exception ex)
        {
            AssetDatabase.DeleteAsset(root + "/CharacterAssetCatalog.asset");
            if (Directory.Exists(full)) Directory.Delete(full, true);
            _diagnostics.Clear(); _diagnostics.Add(UnityCharacterAssetCooker.Error("package.create.failed", root, ex.Message));
            Status = "Failed"; return false;
        }
    }

    public bool OpenPackage(string packageRoot)
    {
        var inspection = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(packageRoot);
        if (!inspection.Success || inspection.Source == null || inspection.Catalog == null)
        {
            SetDiagnostics(inspection.RawDiagnostics, "Failed");
            return false;
        }
        PackageRoot = inspection.SourcePath;
        Manifest = inspection.Source.Manifest;
        Draft = inspection.Source.Character;
        Catalog = inspection.Catalog;
        AbilityLab.Instance?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft), true);
        LoadedDiskHash = ComputeDiskHash();
        IsDirty = false;
        SetDiagnostics(inspection.RawDiagnostics, inspection.Status == "valid" ? "Valid" : inspection.Status == "stale" ? "Stale" : "Failed");
        return true;
    }

    public bool ReloadPackage() => HasPackage && OpenPackage(PackageRoot);

    public bool SavePackage()
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (ComputeDiskHash() != LoadedDiskHash) return Fail("workspace.conflict", PackageRoot, "Package source changed externally; reload before saving.");
        string packagePath = ToFull(PackageRoot + "/package.json"); string characterPath = ToFull(PackageRoot + "/character.json");
        try
        {
            string packageJson = CharacterPackageSourceCodec.SerializeManifest(Manifest);
            string characterJson = CharacterPackageSourceCodec.SerializeCharacter(Draft);
            ReplaceSources(packagePath, characterPath, Encoding.UTF8.GetBytes(packageJson), Encoding.UTF8.GetBytes(characterJson));
            LoadedDiskHash = ComputeDiskHash(); IsDirty = false;
            var cook = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook(PackageRoot);
            if (!cook.Success)
            {
                SetDiagnostics(cook.RawDiagnostics, "Failed");
                AbilityLab.Instance?.MarkPreviewNonAuthoritative();
                return false;
            }
            LastValidAssembly = cook.Assembly;
            CookedSourceHash = cook.SourceHash;
            CookedContentHash = cook.CookedContentHash;
            PackageHash = cook.PackageHash;
            var loadedAssembly = CookedCharacterPackageLoader.LoadAssembly(cook.Assembly);
            SetDiagnostics(loadedAssembly.Diagnostics, loadedAssembly.IsValid ? "Valid" : "Failed");
            if (!loadedAssembly.IsValid) AbilityLab.Instance?.MarkPreviewNonAuthoritative();
            if (loadedAssembly.IsValid && loadedAssembly.Package != null)
            {
                var generated = CharacterAnimationCatalogGenerator.Create(cook.Assembly.BindingBytes);
                AbilityLab.Instance?.ApplyCookedPackagePreview(loadedAssembly.Package, loadedAssembly.BakedAnimation, generated, Catalog.Rig, PackageId == "fightguy" ? CharacterClass.FightGuy : CharacterClass.None, true);
            }
            return loadedAssembly.IsValid;
        }
        catch (Exception ex) { AbilityLab.Instance?.MarkPreviewNonAuthoritative(); SetDiagnostics(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "workspace.save.failed", PackageRoot, ex.Message) }, "Failed"); return false; }
    }

    public bool RenameSemanticId(string oldId, string newId)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        var snapshots = (Catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .Where(x => x != null)
            .Select(x => new CharacterAssetCatalogBindingSnapshot(x.SemanticId ?? "", x.PoseTrackId ?? ""))
            .ToArray();
        var result = CharacterPackageSourceCodec.RenameSemanticId(new CharacterPackageSource(Manifest, Draft), oldId, newId, snapshots);
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        var prior = Catalog.Bindings.Select(x => new CharacterAssetCatalog.AnimationBinding { SemanticId = x.SemanticId, PoseTrackId = x.PoseTrackId, Clip = x.Clip, Extrapolation = x.Extrapolation }).ToArray();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Rename semantic ID");
            PushUndo();
            Manifest = result.Source.Manifest; Draft = result.Source.Character; AbilityLab.Instance?.SetSourceDocument(result.Source);
            Catalog.Bindings = Catalog.Bindings.Select(x => x == null ? null : new CharacterAssetCatalog.AnimationBinding { SemanticId = x.SemanticId == oldId ? newId : x.SemanticId, PoseTrackId = x.PoseTrackId == oldId ? newId : x.PoseTrackId, Clip = x.Clip, Extrapolation = x.Extrapolation }).ToArray();
            EditorUtility.SetDirty(Catalog); IsDirty = true; SetDiagnostics(result.Diagnostics, "Valid"); return true;
        }
        catch (Exception ex)
        {
            Catalog.Bindings = prior; SetDiagnostics(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "rename.failed", "catalog", ex.Message) }, "Failed"); return false;
        }
    }
    public bool ReplaceStage(int slotIndex, int stageIndex, CharacterStageSource stage)
    {
        var result = CharacterPackageSourceCodec.ReplaceStage(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, stage);
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Draft = result.Source.Character; AbilityLab.Instance?.SetSourceDocument(result.Source); IsDirty = true; SetDiagnostics(result.Diagnostics, "Stale"); return true;
    }

    public bool ReplaceOperation(int slotIndex, int stageIndex, int operationIndex, CharacterTimelineOperationSource operation)
    {
        var result = CharacterPackageSourceCodec.ReplaceOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, operation);
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Draft = result.Source.Character; AbilityLab.Instance?.SetSourceDocument(result.Source); IsDirty = true; SetDiagnostics(result.Diagnostics, "Stale"); return true;
    }

    public bool AddStage(int slotIndex, CharacterStageSource stage)
        => ApplyEdit(CharacterPackageSourceCodec.AddStage(new CharacterPackageSource(Manifest, Draft), slotIndex, stage));
    public bool RemoveStage(int slotIndex, int stageIndex)
        => ApplyEdit(CharacterPackageSourceCodec.RemoveStage(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex));

    public bool AddOperation(int slotIndex, int stageIndex, CharacterTimelineOperationSource operation)
        => ApplyEdit(CharacterPackageSourceCodec.AddOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operation));
    public bool RemoveOperation(int slotIndex, int stageIndex, int operationIndex)
        => ApplyEdit(CharacterPackageSourceCodec.RemoveOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex));
    public bool MoveOperation(int slotIndex, int stageIndex, int operationIndex, int destinationIndex)
        => ApplyEdit(CharacterPackageSourceCodec.MoveOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, destinationIndex));
    private bool ApplyEdit(CharacterSourceEditResult result)
    {
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo(); Manifest = result.Source.Manifest; Draft = result.Source.Character;
        AbilityLab.Instance?.SetSourceDocument(result.Source); IsDirty = true; SetDiagnostics(result.Diagnostics, "Stale"); return true;
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Undo()
    {
        if (_undo.Count == 0) return;
        if (Manifest != null && Draft != null) _redo.Push(new CharacterPackageSource(Manifest, Draft));
        var source = _undo.Pop(); Manifest = source.Manifest; Draft = source.Character; AbilityLab.Instance?.SetSourceDocument(source, true); IsDirty = true; Status = "Stale";
    }
    public void Redo()
    {
        if (_redo.Count == 0) return;
        if (Manifest != null && Draft != null) _undo.Push(new CharacterPackageSource(Manifest, Draft));
        var source = _redo.Pop(); Manifest = source.Manifest; Draft = source.Character; AbilityLab.Instance?.SetSourceDocument(source, true); IsDirty = true; Status = "Stale";
    }


    public void SetDraft(CharacterAuthoringDocument draft) { PushUndo(); Draft = draft ?? throw new ArgumentNullException(nameof(draft)); if (Manifest != null) AbilityLab.Instance?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft)); IsDirty = true; }
    public void SetManifest(PackageManifestSource manifest) { PushUndo(); Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest)); if (Draft != null) AbilityLab.Instance?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft)); IsDirty = true; }
    public void RevertDraft() => ReloadPackage();

    private void PushUndo()
    {
        if (Manifest == null || Draft == null) return;
        _undo.Push(new CharacterPackageSource(Manifest, Draft));
        if (_undo.Count > MaxUndoDepth) _undo.Pop();
        _redo.Clear();
    }

    private bool Fail(string code, string path, string message) { SetDiagnostics(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message) }, "Failed"); return false; }
    private void SetDiagnostics(IEnumerable<CharacterDiagnostic> diagnostics, string status) { _diagnostics.Clear(); _diagnostics.AddRange(diagnostics ?? Array.Empty<CharacterDiagnostic>()); Status = status; }
    private string ToFull(string path) => Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path);
    private string ComputeDiskHash() => HashFiles(File.ReadAllBytes(ToFull(PackageRoot + "/package.json")), File.ReadAllBytes(ToFull(PackageRoot + "/character.json")));
    internal static string HashFiles(byte[] package, byte[] character)
    {
        using var hash = SHA256.Create(); using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(package.Length); writer.Write(package); writer.Write(character.Length); writer.Write(character); writer.Flush(); return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
    }
    private static void WriteAtomic(string path, byte[] bytes) { string temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllBytes(temp, bytes); if (File.Exists(path)) File.Replace(temp, path, path + ".previous", true); else File.Move(temp, path); }
    private static void ReplaceSources(string packagePath, string characterPath, byte[] package, byte[] character)
    {
        string packageTemp = packagePath + ".tmp-" + Guid.NewGuid().ToString("N");
        string characterTemp = characterPath + ".tmp-" + Guid.NewGuid().ToString("N");
        byte[] priorPackage = File.Exists(packagePath) ? File.ReadAllBytes(packagePath) : null;
        byte[] priorCharacter = File.Exists(characterPath) ? File.ReadAllBytes(characterPath) : null;
        File.WriteAllBytes(packageTemp, package); File.WriteAllBytes(characterTemp, character);
        try
        {
            if (File.Exists(packagePath)) File.Replace(packageTemp, packagePath, null, true); else File.Move(packageTemp, packagePath);
            if (File.Exists(characterPath)) File.Replace(characterTemp, characterPath, null, true); else File.Move(characterTemp, characterPath);
        }
        catch
        {
            if (priorPackage == null) { if (File.Exists(packagePath)) File.Delete(packagePath); } else File.WriteAllBytes(packagePath, priorPackage);
            if (priorCharacter == null) { if (File.Exists(characterPath)) File.Delete(characterPath); } else File.WriteAllBytes(characterPath, priorCharacter);
            throw;
        }
        finally
        {
            if (File.Exists(packageTemp)) File.Delete(packageTemp);
            if (File.Exists(characterTemp)) File.Delete(characterTemp);
        }
    }
}
