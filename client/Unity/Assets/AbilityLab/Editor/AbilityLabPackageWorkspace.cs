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
    private string LoadedCatalogFingerprint { get; set; } = "";
    public bool IsDirty { get; private set; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics => _diagnostics;
    public string CookedSourceHash { get; private set; } = "";
    public string CookedContentHash { get; private set; } = "";
    public string PackageHash { get; private set; } = "";
    public CharacterAssetCatalog Catalog { get; private set; }
    public CharacterPackageAssemblyResult LastValidAssembly { get; private set; }
    public AbilityLabPackagePreviewResult Preview { get; private set; }
    public CookedCharacterPackage? LiveDraftPackage { get; private set; }
    public bool LiveDraftInvalid { get; private set; }
    public string Status { get; private set; } = "Unknown";
    private const int MaxUndoDepth = 50;
    private readonly Stack<WorkspaceSnapshot> _undo = new();
    private readonly Stack<WorkspaceSnapshot> _redo = new();

    private readonly List<CharacterDiagnostic> _diagnostics = new();
    public event Action? StatusChanged;

    public bool HasPackage => !string.IsNullOrEmpty(PackageRoot) && Manifest != null && Draft != null;
    public string PackageId => Manifest?.PackageId ?? "";

    public bool NewPackage(string packageId, string displayName, string creator = "Binoui", string license = "MIT", string attribution = "SlopArena")
    {
        var service = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot());
        CharacterPackageCreateResult result = service.NewPackage(packageId, displayName, creator, license, attribution);
        if (!result.Success)
        {
            SetDiagnostics((result.Diagnostics ?? Array.Empty<CharacterPackageDiagnosticResult>())
                .Select(x => new CharacterDiagnostic(
                    x.Severity == "error" ? CharacterDiagnosticSeverity.Error : CharacterDiagnosticSeverity.Warning,
                    x.Code, x.Path, x.Message)), "Failed");
            return false;
        }
        return OpenPackage(result.SourcePath);

    }
    public bool OpenPackage(string packageRoot)
    {
        LiveDraftPackage = null;
        LiveDraftInvalid = false;
        var inspection = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Inspect(packageRoot);
        if (!inspection.Success || inspection.Source == null || inspection.Catalog == null)
        {
            SetDiagnostics(inspection.RawDiagnostics, "Failed");
            Preview = new AbilityLabPackagePreviewResult(
                false, null, null, null, null, null, Array.Empty<SlotAddress>(), inspection.RawDiagnostics);
            AbilityLab.Instance?.ApplyPreviewUnavailable(Preview.Diagnostics);
            return false;
        }

        PackageRoot = inspection.SourcePath;
        Manifest = inspection.Source.Manifest;
        Draft = inspection.Source.Character;
        Catalog = inspection.Catalog;
        _undo.Clear();
        _redo.Clear();
        AbilityLab.Instance?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft));
        AbilityLab.Instance?.SetPresentationBindings(Catalog.Presentations);
        LoadedDiskHash = ComputeDiskHash();
        LoadedCatalogFingerprint = ComputeCatalogFingerprint();
        IsDirty = false;
        CookedSourceHash = inspection.CookedSourceHash ?? "";
        CookedContentHash = inspection.CookedContentHash ?? "";
        PackageHash = inspection.PackageHash ?? "";
        var inspectionDiagnostics = new List<CharacterDiagnostic>(inspection.RawDiagnostics);
        if (inspection.Status == "stale")
            inspectionDiagnostics.AddRange((inspection.StaleReasons ?? Array.Empty<CharacterPackageStaleReason>())
                .Select(reason => new CharacterDiagnostic(CharacterDiagnosticSeverity.Warning, reason.Code, reason.Path, reason.Message)));
        SetDiagnostics(inspectionDiagnostics, inspection.Status == "valid" ? "Valid" : inspection.Status == "stale" ? "Stale" : "Failed");
        Preview = AbilityLabPackagePreviewLoader.Load(Manifest.PackageId);
        if (Preview.IsAvailable)
            AbilityLab.Instance?.ApplyPackagePreview(Preview);
        else
            AbilityLab.Instance?.ApplyPreviewUnavailable(Preview.Diagnostics);

        if (!Preview.IsAvailable)
        {
            var previewDiagnostics = _diagnostics.Concat(Preview.Diagnostics).ToArray();
            SetDiagnostics(previewDiagnostics, Status);
        }
        return true;
    }

    public bool ReloadPackage() => HasPackage && OpenPackage(PackageRoot);
    public bool SavePackage()
    {
        if (ComputeDiskHash() != LoadedDiskHash)
            return Fail("workspace.conflict", PackageRoot, "Package source changed externally; reload before saving.");
        if (ComputeCatalogFingerprint() != LoadedCatalogFingerprint)
            return Fail("workspace.conflict", PackageRoot + "/CharacterAssetCatalog.asset", "Character asset catalog changed externally; reload before saving.");
        string packagePath = ToFull(PackageRoot + "/package.json"); string characterPath = ToFull(PackageRoot + "/character.json");
        try
        {
            string packageJson = CharacterPackageSourceCodec.SerializeManifest(Manifest);
            string characterJson = CharacterPackageSourceCodec.SerializeCharacter(Draft);
            ReplaceSources(packagePath, characterPath, Encoding.UTF8.GetBytes(packageJson), Encoding.UTF8.GetBytes(characterJson));
            LoadedDiskHash = ComputeDiskHash();
            IsDirty = false;
            SetDiagnostics(Array.Empty<CharacterDiagnostic>(), "Cooking");
            var cook = new CharacterPackageAuthoringService(UnityCharacterAssetCooker.ProjectRoot()).Cook(PackageRoot);
            if (!cook.Success || cook.Assembly == null)
            {
                SetDiagnostics(cook.RawDiagnostics, "Failed");
                return false;
            }

            var loadedAssembly = CookedCharacterPackageLoader.LoadAssembly(cook.Assembly);
            if (!loadedAssembly.IsValid)
            {
                SetDiagnostics(loadedAssembly.Diagnostics, "Failed");
                return false;
            }

            var candidatePreview = AbilityLabPackagePreviewLoader.Load(PackageId);
            if (!candidatePreview.IsAvailable)
            {
                SetDiagnostics(loadedAssembly.Diagnostics.Concat(candidatePreview.Diagnostics), "Failed");
                return false;
            }

            LastValidAssembly = cook.Assembly;
            CookedSourceHash = cook.SourceHash;
            CookedContentHash = cook.CookedContentHash;
            PackageHash = cook.PackageHash;
            Preview = candidatePreview;
            LiveDraftPackage = null;
            LiveDraftInvalid = false;
            SetDiagnostics(loadedAssembly.Diagnostics.Concat(candidatePreview.Diagnostics), "Valid");
            AbilityLab.Instance?.ApplyPackagePreview(candidatePreview);
            return true;
        }
        catch (Exception ex)
        {
            SetDiagnostics(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "workspace.save.failed", PackageRoot, ex.Message) }, "Failed");
            return false;
        }
    }

    public bool RenameSemanticId(string oldId, string newId)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        var snapshots = (Catalog.Bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .Where(x => x != null)
            .Select(x => new CharacterAssetCatalogBindingSnapshot(x.SemanticId ?? "", x.PoseTrackId ?? ""))
            .Concat((Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                .Where(x => x != null)
                .Select(x => new CharacterAssetCatalogBindingSnapshot(x.SemanticId ?? "", "")))
            .ToArray();
        var result = CharacterPackageSourceCodec.RenameSemanticId(new CharacterPackageSource(Manifest, Draft), oldId, newId, snapshots);
        if (!result.IsValid || result.Source == null) { SetDiagnosticsWithoutNotify(result.Diagnostics, "Failed"); return false; }
        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Rename semantic ID");
            Manifest = result.Source.Manifest;
            Draft = result.Source.Character;
            Catalog.Bindings = CloneBindings(Catalog.Bindings)
                .Select(x => x == null ? null : new CharacterAssetCatalog.AnimationBinding
                {
                    SemanticId = x.SemanticId == oldId ? newId : x.SemanticId,
                    PoseTrackId = x.PoseTrackId == oldId ? newId : x.PoseTrackId,
                    Clip = x.Clip,
                    Extrapolation = x.Extrapolation,
                }).ToArray();
            Catalog.Presentations = ClonePresentations(Catalog.Presentations)
                .Select(x => x == null ? null : new CharacterAssetCatalog.PresentationBinding
                {
                    SemanticId = x.SemanticId == oldId ? newId : x.SemanticId,
                    Prefab = x.Prefab,
                }).ToArray();
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            PushUndo(prior);
            AbilityLab.Instance?.SetSourceDocument(result.Source);
            AbilityLab.Instance?.SetPresentationBindings(Catalog.Presentations);
            IsDirty = true;
            LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            SetDiagnosticsWithoutNotify(result.Diagnostics, "Stale");
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(prior, false);
            SetDiagnosticsWithoutNotify(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "rename.failed", "catalog", ex.Message) }, "Failed");
            return false;
        }
    }

    public bool ReplaceCatalogRig(GameObject rig)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Replace character catalog rig");
            Catalog.Rig = rig;
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            PushUndo(prior);
            MarkCatalogEdited();
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(prior, false);
            return Fail("edit.catalog.failed", "catalog.rig", ex.Message);
        }
    }

    public bool ReplaceCatalogBinding(string semanticId, AnimationClip clip, ExtrapolationMode extrapolation)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        WorkspaceSnapshot prior = CaptureSnapshot();
        CharacterPackageAuthoringService service = new(UnityCharacterAssetCooker.ProjectRoot());
        CharacterPackageBindingResult result = clip == null
            ? service.Unbind(PackageRoot, semanticId)
            : service.Bind(PackageRoot, semanticId, AssetDatabase.GetAssetPath(clip), extrapolation);
        if (!result.Success)
        {
            SetDiagnostics((result.Diagnostics ?? Array.Empty<CharacterPackageDiagnosticResult>())
                .Select(x => new CharacterDiagnostic(
                    x.Severity == "error" ? CharacterDiagnosticSeverity.Error : CharacterDiagnosticSeverity.Warning,
                    x.Code, x.Path, x.Message)), "Failed");
            return false;
        }
        Catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(PackageRoot + "/CharacterAssetCatalog.asset");
        PushUndo(prior);
        MarkCatalogEdited();
        SetDiagnosticsWithoutNotify(Array.Empty<CharacterDiagnostic>(), "Stale");
        SceneView.RepaintAll();
        return true;
    }
    public bool AddPresentationAsset(string semanticId, GameObject prefab)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        semanticId = semanticId?.Trim() ?? "";
        if (string.IsNullOrEmpty(semanticId))
            return Fail("asset-catalog.presentation-id.missing", "presentationId", "Presentation ID is required.");
        if (prefab == null)
            return Fail("asset-catalog.presentation.prefab.missing", semanticId, "Presentation prefab is required.");
        if ((Draft.PresentationIds ?? Array.Empty<string>()).Contains(semanticId, StringComparer.Ordinal) ||
            (Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                .Any(binding => binding != null && binding.SemanticId == semanticId))
            return Fail("asset-catalog.presentation.duplicate", semanticId, "Presentation ID already exists.");

        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Add presentation asset");
            Draft = Draft with
            {
                PresentationIds = (Draft.PresentationIds ?? Array.Empty<string>()).Concat(new[] { semanticId }).ToArray(),
            };
            Catalog.Presentations = (Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
                .Concat(new[]
                {
                    new CharacterAssetCatalog.PresentationBinding { SemanticId = semanticId, Prefab = prefab },
                }).ToArray();
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            PushUndo(prior);
            LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            IsDirty = true;
            AbilityLab.Instance?.SetSourceDocument(new CharacterPackageSource(Manifest, Draft));
            AbilityLab.Instance?.SetPresentationBindings(Catalog.Presentations);
            RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(prior, false);
            return Fail("edit.catalog.presentation.failed", semanticId, ex.Message);
        }
    }

    public bool ReplaceCatalogPresentation(string semanticId, GameObject prefab)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        var existing = (Catalog.Presentations ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
            .FirstOrDefault(binding => binding != null && binding.SemanticId == semanticId);
        if (existing == null)
            return Fail("asset-catalog.presentation.missing", semanticId, "Presentation binding does not exist.");

        WorkspaceSnapshot prior = CaptureSnapshot();
        try
        {
            UnityEditor.Undo.RecordObject(Catalog, "Replace presentation asset");
            Catalog.Presentations = ClonePresentations(Catalog.Presentations)
                .Select(binding => binding != null && binding.SemanticId == semanticId
                    ? new CharacterAssetCatalog.PresentationBinding { SemanticId = semanticId, Prefab = prefab }
                    : binding)
                .ToArray();
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            PushUndo(prior);
            MarkCatalogEdited();
            SceneView.RepaintAll();
            return true;
        }
        catch (Exception ex)
        {
            RestoreSnapshot(prior, false);
            return Fail("edit.catalog.presentation.failed", semanticId, ex.Message);
        }
    }


    private void MarkCatalogEdited()
    {
        LoadedCatalogFingerprint = CharacterPackageAuthoringService.ComputeCatalogFingerprint(Catalog);
        IsDirty = true;
        SetDiagnosticsWithoutNotify(_diagnostics, "Stale");
        AbilityLab.Instance?.SetPresentationBindings(Catalog?.Presentations);
    }
    public bool TryResolveCanonicalSlot(string canonicalSlotId, out int explicitSourceSlotIndex, out CharacterSlotSource sourceSlot)
    {
        explicitSourceSlotIndex = -1;
        sourceSlot = null!;
        if (!HasPackage || !CanonicalSlotProjection.TryGet(canonicalSlotId, out _)) return false;

        var explicitById = Draft.Slots
            .Select((slot, index) => (slot, index))
            .Where(item => item.slot != null)
            .ToDictionary(item => item.slot.Id, item => item.index, StringComparer.Ordinal);
        var aliases = (Draft.Aliases ?? Array.Empty<CharacterAliasSource>())
            .Where(alias => alias != null)
            .ToDictionary(alias => alias.From, alias => alias.To, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string current = canonicalSlotId;
        while (true)
        {
            if (!visited.Add(current)) return false;
            if (explicitById.TryGetValue(current, out explicitSourceSlotIndex))
            {
                sourceSlot = Draft.Slots[explicitSourceSlotIndex];
                return true;
            }
            if (!aliases.TryGetValue(current, out string target)) return false;
            current = target;
        }
    }

    public bool ReplaceGeneral(string displayName, float weight, float capsuleRadius, float capsuleHeight, float hipHeight, float hurtboxRadius)
        => !HasPackage
            ? Fail("workspace.missing", "workspace", "No package is open.")
            : ApplyEdit(CharacterPackageSourceCodec.ReplaceGeneral(
                new CharacterPackageSource(Manifest, Draft), displayName, weight, capsuleRadius, capsuleHeight, hipHeight, hurtboxRadius));

    public bool ReplaceMovement(CharacterMovementSource value)
        => !HasPackage
            ? Fail("workspace.missing", "workspace", "No package is open.")
            : ApplyEdit(CharacterPackageSourceCodec.ReplaceMovement(new CharacterPackageSource(Manifest, Draft), value));

    public bool ReplacePresentation(CharacterPresentationSource value)
        => !HasPackage
            ? Fail("workspace.missing", "workspace", "No package is open.")
            : ApplyEdit(CharacterPackageSourceCodec.ReplacePresentation(new CharacterPackageSource(Manifest, Draft), value));

    public bool ReplaceStage(string canonicalSlotId, int stageIndex, CharacterStageSource stage)
    {
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ReplaceStage(slotIndex, stageIndex, stage);
    }
    public bool ReplaceOperationTick(string canonicalSlotId, int stageIndex, int operationIndex, int tick)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ApplyEdit(CharacterPackageSourceCodec.ReplaceOperationTick(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, tick));
    }

    public bool ReplaceHitboxDuration(string canonicalSlotId, int stageIndex, int operationIndex, int durationTicks)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out _))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        return ApplyEdit(CharacterPackageSourceCodec.ReplaceHitboxDuration(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, durationTicks));
    }


    public bool ReplaceHitbox(string canonicalSlotId, int stageIndex, int operationIndex, HitboxSource hitbox)
    {
        if (hitbox == null) return Fail("edit.source.missing", "hitbox", "Hitbox is required.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        var operations = sourceSlot.Timeline.Stages[stageIndex].Operations;
        if (operationIndex < 0 || operationIndex >= operations.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}].operations[{operationIndex}]", "Operation index is out of range.");
        if (operations[operationIndex] is not SpawnHitboxOperationSource original)
            return Fail("edit.operation.type", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}].operations[{operationIndex}]", "Selected operation is not a hitbox operation.");
        return ReplaceOperation(slotIndex, stageIndex, operationIndex, original with { Hitbox = hitbox });
    }
    public bool AddHitbox(string canonicalSlotId, int stageIndex)
    {
        if (!HasPackage) return Fail("workspace.missing", "workspace", "No package is open.");
        if (!TryResolveCanonicalSlot(canonicalSlotId, out int slotIndex, out var sourceSlot))
            return Fail("edit.slot.unresolved", canonicalSlotId, "Canonical slot does not resolve to an explicit source slot.");
        if (stageIndex < 0 || stageIndex >= sourceSlot.Timeline.Stages.Count)
            return Fail("edit.index.out-of-range", $"character.slots[{slotIndex}].timeline.stages[{stageIndex}]", "Stage index is out of range.");
        var hitbox = new HitboxSource(
            AuthoringHitboxShape.Sphere, 0.5f,
            0f, 0f, 0f, 0f, 0f, 0f,
            "bone.hips", null,
            1f, 45f, 5f, 80f, 8, 1, false, 0);
        return AddOperation(slotIndex, stageIndex, new SpawnHitboxOperationSource(0, AuthoringUnit.Meters, hitbox));
    }

    public bool ReplaceStage(int slotIndex, int stageIndex, CharacterStageSource stage)
    {
        var result = CharacterPackageSourceCodec.ReplaceStage(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, stage);
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        IsDirty = true;
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
    }

    public bool ReplaceOperation(int slotIndex, int stageIndex, int operationIndex, CharacterTimelineOperationSource operation)
    {
        var result = CharacterPackageSourceCodec.ReplaceOperation(new CharacterPackageSource(Manifest, Draft), slotIndex, stageIndex, operationIndex, operation);
        if (!result.IsValid || result.Source == null) { SetDiagnostics(result.Diagnostics, "Failed"); return false; }
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        IsDirty = true;
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
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
        PushUndo();
        Manifest = result.Source.Manifest;
        Draft = result.Source.Character;
        IsDirty = true;
        RefreshLiveDraft(result.Source, result.Diagnostics);
        return true;
    }

    private void RefreshLiveDraft(CharacterPackageSource source, IReadOnlyList<CharacterDiagnostic> sourceDiagnostics)
    {
        AbilityLab.Instance?.SetSourceDocument(source);
        var compiled = CharacterPackageCompiler.Compile(source, CharacterPackageAuthoringService.ProfileFor(PackageId));
        var diagnostics = new List<CharacterDiagnostic>(sourceDiagnostics ?? Array.Empty<CharacterDiagnostic>());
        diagnostics.AddRange(compiled.Diagnostics);
        if (!compiled.HasErrors && compiled.CookedPackage != null)
        {
            LiveDraftPackage = compiled.CookedPackage;
            LiveDraftInvalid = false;
            SetDiagnostics(diagnostics, "Stale");
            return;
        }

        LiveDraftPackage = null;
        LiveDraftInvalid = true;
        if (diagnostics.Count == 0)
            diagnostics.Add(new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, "preview.compile.failed", "character", "Live package preview compilation failed."));
        AbilityLab.Instance?.MarkPackageDraftInvalid();
        SetDiagnostics(diagnostics, "Failed");
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(CaptureSnapshot());
        RestoreSnapshot(_undo.Pop(), true);
    }
    public void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Push(CaptureSnapshot());
        RestoreSnapshot(_redo.Pop(), true);
    }

    public void SetDraft(CharacterAuthoringDocument draft)
    {
        PushUndo();
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        IsDirty = true;
        RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
    }

    public void SetManifest(PackageManifestSource manifest)
    {
        PushUndo();
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        IsDirty = true;
        RefreshLiveDraft(new CharacterPackageSource(Manifest, Draft), Array.Empty<CharacterDiagnostic>());
    }
    public void RevertDraft() => ReloadPackage();

    private void RestoreSnapshot(WorkspaceSnapshot snapshot, bool recordUnityUndo)
    {
        if (recordUnityUndo && Catalog != null)
            UnityEditor.Undo.RecordObject(Catalog, "Ability Lab workspace undo");
        Manifest = snapshot.Source.Manifest;
        Draft = snapshot.Source.Character;
        if (Catalog != null && snapshot.Catalog != null)
        {
            Catalog.Rig = snapshot.Catalog.Rig;
            Catalog.Bindings = CloneBindings(snapshot.Catalog.Bindings);
            Catalog.Presentations = ClonePresentations(snapshot.Catalog.Presentations);
            EditorUtility.SetDirty(Catalog);
            AssetDatabase.SaveAssets();
            LoadedCatalogFingerprint = ComputeCatalogFingerprint();
            AbilityLab.Instance?.SetPresentationBindings(Catalog.Presentations);
        }
        IsDirty = true;
        RefreshLiveDraft(snapshot.Source, Array.Empty<CharacterDiagnostic>());
        SceneView.RepaintAll();
    }

    private WorkspaceSnapshot CaptureSnapshot()
        => new(new CharacterPackageSource(Manifest, Draft), Catalog == null
            ? null
            : new CatalogSnapshot(Catalog.Rig, CloneBindings(Catalog.Bindings), ClonePresentations(Catalog.Presentations)));

    private void PushUndo() => PushUndo(CaptureSnapshot());

    private void PushUndo(WorkspaceSnapshot snapshot)
    {
        _undo.Push(snapshot);
        if (_undo.Count > MaxUndoDepth) _undo.Pop();
        _redo.Clear();
    }

    private static CharacterAssetCatalog.AnimationBinding[] CloneBindings(IEnumerable<CharacterAssetCatalog.AnimationBinding> bindings)
        => (bindings ?? Array.Empty<CharacterAssetCatalog.AnimationBinding>())
            .Select(x => x == null ? null : new CharacterAssetCatalog.AnimationBinding
            {
                SemanticId = x.SemanticId,
                PoseTrackId = x.PoseTrackId,
                Clip = x.Clip,
                Extrapolation = x.Extrapolation,
            }).ToArray();
    private static CharacterAssetCatalog.PresentationBinding[] ClonePresentations(IEnumerable<CharacterAssetCatalog.PresentationBinding> bindings)
        => (bindings ?? Array.Empty<CharacterAssetCatalog.PresentationBinding>())
            .Select(x => x == null ? null : new CharacterAssetCatalog.PresentationBinding
            {
                SemanticId = x.SemanticId,
                Prefab = x.Prefab,
            }).ToArray();


    private sealed class WorkspaceSnapshot
    {
        public WorkspaceSnapshot(CharacterPackageSource source, CatalogSnapshot catalog)
        {
            Source = source;
            Catalog = catalog;
        }

        public CharacterPackageSource Source { get; }
        public CatalogSnapshot Catalog { get; }
    }

    private sealed class CatalogSnapshot
    {
        public CatalogSnapshot(
            GameObject rig,
            CharacterAssetCatalog.AnimationBinding[] bindings,
            CharacterAssetCatalog.PresentationBinding[] presentations)
        {
            Rig = rig;
            Bindings = bindings;
            Presentations = presentations;
        }

        public GameObject Rig { get; }
        public CharacterAssetCatalog.AnimationBinding[] Bindings { get; }
        public CharacterAssetCatalog.PresentationBinding[] Presentations { get; }
    }

    private bool Fail(string code, string path, string message) => SetFailure(code, path, message);
    private bool SetFailure(string code, string path, string message)
    {
        SetDiagnostics(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message) }, "Failed");
        return false;
    }
    private void SetDiagnostics(IEnumerable<CharacterDiagnostic> diagnostics, string status)
    {
        var values = (diagnostics ?? Array.Empty<CharacterDiagnostic>()).ToArray();
        _diagnostics.Clear();
        _diagnostics.AddRange(values);
        Status = status;
        StatusChanged?.Invoke();
    }
    private void SetDiagnosticsWithoutNotify(IEnumerable<CharacterDiagnostic> diagnostics, string status)
    {
        var values = (diagnostics ?? Array.Empty<CharacterDiagnostic>()).ToArray();
        _diagnostics.Clear();
        _diagnostics.AddRange(values);
        Status = status;
    }
    private string ToFull(string path) => Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), path);
    private string ComputeDiskHash() => HashFiles(File.ReadAllBytes(ToFull(PackageRoot + "/package.json")), File.ReadAllBytes(ToFull(PackageRoot + "/character.json")));
    private string ComputeCatalogFingerprint() => CharacterPackageAuthoringService.ComputeCatalogFingerprint(Catalog);
    internal static string HashFiles(byte[] package, byte[] character)
    {
        using var hash = SHA256.Create();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(package.Length);
        writer.Write(package);
        writer.Write(character.Length);
        writer.Write(character);
        writer.Flush();
        return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
    }
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
