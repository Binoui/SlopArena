using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Shared;

public sealed class CharacterPackageAuthoringService
{
    private const string CharacterPackagesRoot = "Assets/CharacterPackages";
    private readonly string _projectRoot;

    public CharacterPackageAuthoringService(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("Project root is required.", nameof(projectRoot));
        _projectRoot = Path.GetFullPath(projectRoot);
    }

    public CharacterPackageInspectionResult Inspect(string target)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (!TryResolve(target, diagnostics, out var package))
            return CharacterPackageInspectionResult.CreateFailure(diagnostics);

        var compilerResult = package.Source == null
            ? null
            : CharacterPackageCompiler.Compile(package.PackageJson, package.CharacterJson, ProfileFor(package.PackageId));
        AddUnique(diagnostics, compilerResult?.Diagnostics);

        string currentSourceHash = null;
        if (package.Source != null && package.Catalog != null && compilerResult?.CookedPackage != null && !compilerResult.HasErrors)
        {
            if (UnityCharacterAssetCooker.TryComputeSourceHash(
                    package.ProjectRelativeRoot,
                    package.Catalog,
                    ProfileFor(package.PackageId),
                    out var computedHash,
                    out var hashDiagnostics))
            {
                currentSourceHash = computedHash;
            }
            AddUnique(diagnostics, hashDiagnostics);
        }

        ArtifactSnapshot artifact = ReadArtifact(package.PackageId);
        AddUnique(diagnostics, artifact.Diagnostics);
        var slots = BuildSlotSummaries(package.Source, compilerResult?.CookedPackage);
        bool hasErrors = diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error);
        string status;
        bool dirtyOrStale;
        var staleReasons = new List<CharacterPackageStaleReason>();

        if (hasErrors)
        {
            status = "invalid";
            dirtyOrStale = true;
            if (package.Source == null) staleReasons.Add(new CharacterPackageStaleReason("source-invalid", package.ProjectRelativeRoot + "/character.json", "Character source could not be parsed."));
            if (package.Catalog == null) staleReasons.Add(new CharacterPackageStaleReason("catalog-invalid", package.ProjectRelativeRoot + "/CharacterAssetCatalog.asset", "Character asset catalog is missing or invalid."));
            if (artifact.IsInvalid) staleReasons.Add(new CharacterPackageStaleReason("artifact-invalid", CookedPath(package.PackageId), "Cooked package verification failed."));
            if (staleReasons.Count == 0) staleReasons.Add(new CharacterPackageStaleReason("source-invalid", package.ProjectRelativeRoot, "Character source failed compiler validation."));
        }
        else if (artifact.IsInvalid)
        {
            status = "invalid";
            dirtyOrStale = true;
            staleReasons.Add(new CharacterPackageStaleReason("artifact-invalid", CookedPath(package.PackageId), "Cooked package verification failed."));
        }
        else if (artifact.IsMissing)
        {
            status = "missing";
            dirtyOrStale = false;
            staleReasons.Add(new CharacterPackageStaleReason("cooked-missing", CookedPath(package.PackageId), "No cooked package is present."));
        }
        else if (!StringEquals(currentSourceHash, artifact.SourceHash))
        {
            status = "stale";
            dirtyOrStale = true;
            staleReasons.Add(new CharacterPackageStaleReason("cook-input-changed", package.ProjectRelativeRoot, "Current source or Unity-owned cook dependencies differ from the cooked package."));
        }
        else
        {
            status = "valid";
            dirtyOrStale = false;
        }

        return CharacterPackageInspectionResult.CreateSuccess(
            package.PackageId,
            package.DisplayName,
            package.ProjectRelativeRoot,
            status,
            dirtyOrStale,
            currentSourceHash,
            artifact.SourceHash,
            artifact.CookedContentHash,
            artifact.PackageHash,
            staleReasons,
            slots,
            diagnostics,
            package.Source,
            package.Catalog);
    }

    public CharacterPackageCookResult Cook(string target)
    {
        var diagnostics = new List<CharacterDiagnostic>();
        if (!TryResolve(target, diagnostics, out var package))
            return CharacterPackageCookResult.CreateFailure(null, null, null, null, null, diagnostics, null);

        AddUnique(diagnostics, package.Diagnostics);
        if (package.Source == null || package.Catalog == null || diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error))
            return CharacterPackageCookResult.CreateFailure(package.PackageId, package.ProjectRelativeRoot, CookedPath(package.PackageId), null, null, diagnostics, null);

        byte[] packageBytesBefore;
        byte[] characterBytesBefore;
        try
        {
            packageBytesBefore = File.ReadAllBytes(Path.Combine(package.FullRoot, "package.json"));
            characterBytesBefore = File.ReadAllBytes(Path.Combine(package.FullRoot, "character.json"));
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("source.read.failed", package.ProjectRelativeRoot, ex.Message));
            return CharacterPackageCookResult.CreateFailure(package.PackageId, package.ProjectRelativeRoot, CookedPath(package.PackageId), null, null, diagnostics, null);
        }

        CharacterAssetCookResult cooked;
        try
        {
            cooked = UnityCharacterAssetCooker.Cook(
                package.ProjectRelativeRoot,
                package.Catalog,
                CharacterCookOutput.For(package.PackageId),
                ProfileFor(package.PackageId));
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("asset-catalog.schema", "cook", ex.Message));
            return CharacterPackageCookResult.CreateFailure(package.PackageId, package.ProjectRelativeRoot, CookedPath(package.PackageId), null, null, diagnostics, null);
        }

        AddUnique(diagnostics, cooked.Diagnostics);
        if (cooked.CookedPackage == null || cooked.HasErrors)
            return CharacterPackageCookResult.CreateFailure(package.PackageId, package.ProjectRelativeRoot, CookedPath(package.PackageId), cooked.SourceHash, null, diagnostics, null);

        CharacterPackageAssemblyResult assembly = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(cooked));
        AddUnique(diagnostics, assembly.Diagnostics);
        if (!assembly.IsValid)
            return CharacterPackageCookResult.CreateFailure(package.PackageId, package.ProjectRelativeRoot, CookedPath(package.PackageId), cooked.SourceHash, null, diagnostics, null);

        if (!InputsUnchanged(package, packageBytesBefore, characterBytesBefore, cooked.SourceHash, out var conflictDiagnostics))
        {
            AddUnique(diagnostics, conflictDiagnostics);
            return CharacterPackageCookResult.CreateFailure(package.PackageId, package.ProjectRelativeRoot, CookedPath(package.PackageId), cooked.SourceHash, null, diagnostics, null);
        }

        if (!Publish(package, cooked, assembly, out var publishDiagnostics))
        {
            AddUnique(diagnostics, publishDiagnostics);
            return CharacterPackageCookResult.CreateFailure(package.PackageId, package.ProjectRelativeRoot, CookedPath(package.PackageId), cooked.SourceHash, null, diagnostics, null);
        }

        return CharacterPackageCookResult.CreateSuccess(
            package.PackageId,
            package.ProjectRelativeRoot,
            CookedPath(package.PackageId),
            assembly.SourceHash,
            assembly.CookedContentHash,
            assembly.PackageHash,
            diagnostics,
            assembly);
    }

    public CharacterCookStatus ReadStatus(string packageId)
    {
        if (!MatchContentCatalogBuilder.IsStablePackageId(packageId))
            return new CharacterCookStatus { State = "Unknown", Diagnostics = new List<CharacterCookStatusDiagnostic>() };
        string path = Path.Combine(_projectRoot, CharacterCookOutput.For(packageId).IntermediateDirectory, CharacterCookOutput.For(packageId).StatusFileName);
        if (!File.Exists(path)) return new CharacterCookStatus { State = "Unknown", Diagnostics = new List<CharacterCookStatusDiagnostic>() };
        try
        {
            return JsonUtility.FromJson<CharacterCookStatus>(File.ReadAllText(path)) ?? new CharacterCookStatus();
        }
        catch
        {
            return new CharacterCookStatus { State = "Failed", Diagnostics = new List<CharacterCookStatusDiagnostic>() };
        }
    }

    internal void MarkStale(string packageId)
    {
        CharacterCookStatus status = ReadStatus(packageId);
        if (!StringEquals(status.State, "Valid")) return;
        string packageRoot = PackageRootFor(packageId);
        string catalogPath = packageRoot + "/CharacterAssetCatalog.asset";
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath);
        if (catalog != null && UnityCharacterAssetCooker.TryComputeSourceHash(packageRoot, catalog, ProfileFor(packageId), out string currentHash, out _))
        {
            status.SourceHash = currentHash;
            status.CurrentSourceHash = currentHash;
        }
        else
        {
            status.CurrentSourceHash = status.SourceHash;
        }
        if (string.IsNullOrEmpty(status.CookedSourceHash)) status.CookedSourceHash = status.LastCookedSourceHash;
        if (string.IsNullOrEmpty(status.LastCookedSourceHash)) status.LastCookedSourceHash = status.CookedSourceHash;
        status.State = "Stale";
        status.Diagnostics = new List<CharacterCookStatusDiagnostic>
        {
            new CharacterCookStatusDiagnostic { Severity = "warning", Code = "asset-catalog.stale", Path = "catalog", Message = "A package dependency changed; recook is queued." }
        };
        WriteStatus(status, packageId);
    }

    private bool TryResolve(string target, List<CharacterDiagnostic> diagnostics, out PackageContext package)
    {
        package = null;
        if (string.IsNullOrWhiteSpace(target))
        {
            diagnostics.Add(Error("package.target.missing", "target", "Package ID or package root is required."));
            return false;
        }

        string normalizedTarget = target.Replace('\\', '/').TrimEnd('/');
        bool pathLike = Path.IsPathRooted(target) || normalizedTarget.Contains("/") || normalizedTarget.StartsWith(".", StringComparison.Ordinal);
        string fullRoot = null;
        if (pathLike)
        {
            fullRoot = Path.IsPathRooted(target)
                ? Path.GetFullPath(target)
                : Path.GetFullPath(Path.Combine(_projectRoot, normalizedTarget));
            if (!Directory.Exists(fullRoot))
            {
                diagnostics.Add(Error("package.path.missing", normalizedTarget, "Package root directory does not exist."));
                return false;
            }
            if (!IsInsideCharacterPackages(fullRoot))
            {
                diagnostics.Add(Error("package.path.invalid", normalizedTarget, "Package must be inside Assets/CharacterPackages."));
                return false;
            }
        }
        else
        {
            if (!MatchContentCatalogBuilder.IsStablePackageId(normalizedTarget))
            {
                diagnostics.Add(Error("id.invalid", "target", "Package ID must be a stable lowercase identifier."));
                return false;
            }
            fullRoot = Path.Combine(CharacterPackagesFullRoot(), normalizedTarget);
            if (!Directory.Exists(fullRoot))
            {
                diagnostics.Add(Error("package.missing", normalizedTarget, "Character package was not found."));
                return false;
            }
        }

        package = LoadContext(fullRoot, diagnostics);
        return true;
    }

    private PackageContext LoadContext(string fullRoot, List<CharacterDiagnostic> diagnostics)
    {
        var context = new PackageContext
        {
            FullRoot = Path.GetFullPath(fullRoot),
            ProjectRelativeRoot = ProjectRelative(fullRoot),
            PackageId = new DirectoryInfo(fullRoot).Name,
        };

        if (!MatchContentCatalogBuilder.IsStablePackageId(context.PackageId))
            diagnostics.Add(Error("package.id.invalid", context.ProjectRelativeRoot, "Package folder name must be a stable lowercase identifier."));

        string packagePath = Path.Combine(fullRoot, "package.json");
        string characterPath = Path.Combine(fullRoot, "character.json");
        if (!File.Exists(packagePath)) diagnostics.Add(Error("schema.missing", context.ProjectRelativeRoot + "/package.json", "Package manifest is required."));
        if (!File.Exists(characterPath)) diagnostics.Add(Error("schema.missing", context.ProjectRelativeRoot + "/character.json", "Character document is required."));
        if (File.Exists(packagePath) && File.Exists(characterPath))
        {
            try
            {
                context.PackageJson = File.ReadAllText(packagePath);
                context.CharacterJson = File.ReadAllText(characterPath);
                var loaded = CharacterPackageSourceCodec.Load(context.PackageJson, context.CharacterJson);
                AddUnique(diagnostics, loaded.Diagnostics);
                if (loaded.Source != null)
                {
                    context.Source = loaded.Source;
                    context.PackageId = loaded.Source.Manifest.PackageId;
                    context.DisplayName = loaded.Source.Character.DisplayName;
                    if (!StringEquals(new DirectoryInfo(fullRoot).Name, context.PackageId))
                        diagnostics.Add(Error("package.identity.mismatch", context.ProjectRelativeRoot, "Package folder name does not match manifest package ID."));
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(Error("source.read.failed", context.ProjectRelativeRoot, ex.Message));
            }
        }

        string catalogPath = context.ProjectRelativeRoot + "/CharacterAssetCatalog.asset";
        context.Catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(catalogPath);
        if (context.Catalog == null)
            diagnostics.Add(Error("asset-catalog.missing", catalogPath, "CharacterAssetCatalog.asset is required."));
        else if (!string.IsNullOrEmpty(context.PackageId) && !StringEquals(context.Catalog.PackageId, context.PackageId))
            diagnostics.Add(Error("asset-catalog.schema", catalogPath + ".packageId", "Catalog package ID does not match package manifest ID."));

        context.Diagnostics = diagnostics.ToArray();
        return context;
    }

    private bool InputsUnchanged(
        PackageContext package,
        byte[] packageBytesBefore,
        byte[] characterBytesBefore,
        string cookedSourceHash,
        out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        var result = new List<CharacterDiagnostic>();
        try
        {
            byte[] packageBytesAfter = File.ReadAllBytes(Path.Combine(package.FullRoot, "package.json"));
            byte[] characterBytesAfter = File.ReadAllBytes(Path.Combine(package.FullRoot, "character.json"));
            if (!packageBytesBefore.SequenceEqual(packageBytesAfter) || !characterBytesBefore.SequenceEqual(characterBytesAfter))
                result.Add(Error("source.conflict", package.ProjectRelativeRoot, "Package source changed while cooking."));

            if (result.Count == 0 && package.Catalog != null)
            {
                if (!UnityCharacterAssetCooker.TryComputeSourceHash(
                        package.ProjectRelativeRoot,
                        package.Catalog,
                        ProfileFor(package.PackageId),
                        out string currentHash,
                        out var hashDiagnostics))
                {
                    AddUnique(result, hashDiagnostics);
                }
                else if (!StringEquals(currentHash, cookedSourceHash))
                {
                    result.Add(Error("source.conflict", package.ProjectRelativeRoot, "Package or Unity-owned cook dependencies changed while cooking."));
                }
            }
        }
        catch (Exception ex)
        {
            result.Add(Error("source.conflict", package.ProjectRelativeRoot, "Could not confirm package inputs after cooking: " + ex.Message));
        }

        diagnostics = result;
        return result.Count == 0;
    }

    private bool Publish(
        PackageContext package,
        CharacterAssetCookResult result,
        CharacterPackageAssemblyResult assembled,
        out IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        var errors = new List<CharacterDiagnostic>();
        string projectRoot = _projectRoot;
        string repositoryRoot = RepositoryRoot();
        string rosterPath = Path.Combine(repositoryRoot, "content-cooked", "roster", CharacterPackageAssembler.ManifestPath);
        string rosterTemporary = rosterPath + ".tmp-" + Guid.NewGuid().ToString("N");
        string rosterBackup = rosterPath + ".previous";
        string canonicalDirectory = Path.Combine(repositoryRoot, "content-cooked", package.PackageId);
        string packageParent = Path.GetDirectoryName(canonicalDirectory)!;
        string temporaryDirectory = Path.Combine(packageParent, "." + package.PackageId + ".tmp-" + Guid.NewGuid().ToString("N"));
        string backupDirectory = Path.Combine(packageParent, "." + package.PackageId + ".backup-" + Guid.NewGuid().ToString("N"));
        string finalPose = Path.Combine(projectRoot, CharacterCookOutput.For(package.PackageId).IntermediateDirectory, CharacterCookOutput.For(package.PackageId).PoseFileName);
        string finalBinding = Path.Combine(projectRoot, CharacterCookOutput.For(package.PackageId).IntermediateDirectory, CharacterCookOutput.For(package.PackageId).BindingFileName);
        string generatedAssetPath = CharacterCookOutput.For(package.PackageId).GeneratedAssetPath;
        string temporaryAssetPath = CharacterAnimationCatalogGenerator.TemporaryPath(generatedAssetPath);
        string statusPath = Path.Combine(projectRoot, CharacterCookOutput.For(package.PackageId).IntermediateDirectory, CharacterCookOutput.For(package.PackageId).StatusFileName);
        byte[] previousRoster = Snapshot(rosterPath);
        byte[] previousPose = Snapshot(finalPose);
        byte[] previousBinding = Snapshot(finalBinding);
        byte[] previousStatus = Snapshot(statusPath);
        FileSnapshot previousGenerated = SnapshotFile(generatedAssetPath, projectRoot);
        bool hadCanonical = Directory.Exists(canonicalDirectory);
        bool canonicalInstalled = false;

        try
        {
            Directory.CreateDirectory(packageParent);
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
            if (package.PackageId == "fightguy")
            {
                if (previousRoster == null) throw new InvalidDataException("Cooked roster manifest is missing.");
                WriteDurably(rosterTemporary, UpdateFightGuyRoster(previousRoster, assembled));
            }
            Directory.CreateDirectory(temporaryDirectory);
            WriteDurably(Path.Combine(temporaryDirectory, CharacterPackageAssembler.ManifestPath), assembled.ManifestBytes);
            WriteDurably(Path.Combine(temporaryDirectory, CharacterPackageAssembler.RuntimePath), assembled.RuntimeBytes);
            WriteDurably(Path.Combine(temporaryDirectory, CharacterPackageAssembler.PosePath), assembled.PoseBytes);
            WriteDurably(Path.Combine(temporaryDirectory, CharacterPackageAssembler.BindingPath), assembled.BindingBytes);
            var written = ReadPackageFiles(temporaryDirectory);
            var verification = CharacterPackageAssembler.Verify(written);
            if (!verification.IsValid)
                throw new InvalidDataException(string.Join("\n", verification.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}")));
            if (hadCanonical) Directory.Move(canonicalDirectory, backupDirectory);
            Directory.Move(temporaryDirectory, canonicalDirectory);
            canonicalInstalled = true;
            Directory.CreateDirectory(Path.GetDirectoryName(finalPose)!);
            WriteDurably(finalPose, assembled.PoseBytes);
            WriteDurably(finalBinding, assembled.BindingBytes);
            string generatedTemp = CharacterAnimationCatalogGenerator.Generate(assembled.BindingBytes, generatedAssetPath);
            CharacterAnimationCatalogGenerator.ReplaceTemporary(generatedTemp, generatedAssetPath);
            if (package.PackageId == "fightguy")
            {
                if (File.Exists(rosterPath)) File.Replace(rosterTemporary, rosterPath, rosterBackup, true);
                else File.Move(rosterTemporary, rosterPath);
            }
            WriteSuccessStatus(result, assembled, package.PackageId);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, true);
            if (File.Exists(rosterTemporary)) File.Delete(rosterTemporary);
            if (File.Exists(rosterBackup)) File.Delete(rosterBackup);
            diagnostics = errors;
            return true;
        }
        catch (Exception ex)
        {
            errors.Add(Error("cook.failed", package.ProjectRelativeRoot, ex.Message));
            try
            {
                if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
                string generatedTemporaryFullPath = Path.Combine(projectRoot, temporaryAssetPath);
                if (File.Exists(generatedTemporaryFullPath)) AssetDatabase.DeleteAsset(temporaryAssetPath);
                RestoreFile(finalPose, previousPose);
                RestoreFile(finalBinding, previousBinding);
                RestoreGeneratedAsset(generatedAssetPath, previousGenerated, projectRoot);
                RestoreFile(statusPath, previousStatus);
                if (package.PackageId == "fightguy")
                {
                    RestoreFile(rosterPath, previousRoster);
                    if (File.Exists(rosterTemporary)) File.Delete(rosterTemporary);
                    if (File.Exists(rosterBackup)) File.Delete(rosterBackup);
                }
                if (canonicalInstalled && Directory.Exists(canonicalDirectory)) Directory.Delete(canonicalDirectory, true);
                if (hadCanonical && Directory.Exists(backupDirectory)) Directory.Move(backupDirectory, canonicalDirectory);
                else if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, true);
            }
            catch (Exception rollbackException)
            {
                errors.Add(Error("cook.rollback.failed", package.ProjectRelativeRoot, rollbackException.Message));
            }
            diagnostics = errors;
            return false;
        }
        finally
        {
            if (File.Exists(rosterTemporary)) File.Delete(rosterTemporary);
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }

    private static CharacterPackageSlotSummary[] BuildSlotSummaries(
        CharacterPackageSource source,
        CookedCharacterPackage cooked)
    {
        var result = new List<CharacterPackageSlotSummary>(CharacterPackageCompiler.CanonicalSlotIds.Count);
        var cookedById = cooked?.Definition.Slots.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var explicitSlots = source?.Character.Slots?.Where(x => x != null).GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal)
            ?? new Dictionary<string, CharacterSlotSource>(StringComparer.Ordinal);
        var aliases = source?.Character.Aliases?.Where(x => x != null).GroupBy(x => x.From, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.First().To, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string id in CharacterPackageCompiler.CanonicalSlotIds)
        {
            if (cookedById != null && cookedById.TryGetValue(id, out var cookedSlot))
            {
                result.Add(new CharacterPackageSlotSummary(id, cookedSlot.Name, cookedSlot.Timeline.Stages.Count, true));
                continue;
            }
            CharacterSlotSource slot = ResolveSourceSlot(id, explicitSlots, aliases, new HashSet<string>(StringComparer.Ordinal));
            result.Add(new CharacterPackageSlotSummary(id, slot?.Name, slot?.Timeline?.Stages?.Count ?? 0, slot != null));
        }
        return result.ToArray();
    }

    private static CharacterSlotSource ResolveSourceSlot(
        string id,
        IReadOnlyDictionary<string, CharacterSlotSource> explicitSlots,
        IReadOnlyDictionary<string, string> aliases,
        HashSet<string> visiting)
    {
        if (!visiting.Add(id)) return null;
        if (explicitSlots.TryGetValue(id, out var slot)) return slot;
        if (aliases.TryGetValue(id, out var target)) return ResolveSourceSlot(target, explicitSlots, aliases, visiting);
        return null;
    }

    private ArtifactSnapshot ReadArtifact(string packageId)
    {
        string directory = Path.Combine(RepositoryRoot(), "content-cooked", packageId);
        if (!Directory.Exists(directory)) return ArtifactSnapshot.Missing();
        var diagnostics = new List<CharacterDiagnostic>();
        try
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = path.Substring(directory.Length + 1).Replace('\\', '/');
                files[relative] = File.ReadAllBytes(path);
            }
            var verification = CharacterPackageAssembler.Verify(files);
            AddUnique(diagnostics, verification.Diagnostics);
            if (!verification.IsValid) return ArtifactSnapshot.Invalid(diagnostics);
            using var document = JsonDocument.Parse(files[CharacterPackageAssembler.ManifestPath]);
            var root = document.RootElement;
            string manifestPackageId = root.GetProperty("packageId").GetString() ?? "";
            if (!StringEquals(manifestPackageId, packageId))
                diagnostics.Add(Error("package.identity.mismatch", CharacterPackageAssembler.ManifestPath, "Cooked manifest package ID does not match the package target."));
            if (diagnostics.Any(x => x.Severity == CharacterDiagnosticSeverity.Error)) return ArtifactSnapshot.Invalid(diagnostics);
            return ArtifactSnapshot.Valid(
                root.GetProperty("sourceHash").GetString(),
                root.GetProperty("cookedContentHash").GetString(),
                root.GetProperty("packageHash").GetString(),
                diagnostics);
        }
        catch (Exception ex)
        {
            diagnostics.Add(Error("package.files.invalid", "content-cooked/" + packageId, ex.Message));
            return ArtifactSnapshot.Invalid(diagnostics);
        }
    }

    private CharacterCookProfile ProfileFor(string packageId)
        => packageId == "fightguy" ? CharacterCookProfile.TrustedBuiltIn : CharacterCookProfile.Workshop;

    private string CharacterPackagesFullRoot() => Path.Combine(_projectRoot, CharacterPackagesRoot.Replace('/', Path.DirectorySeparatorChar));
    private string PackageRootFor(string packageId) => CharacterPackagesRoot + "/" + packageId;
    private string CookedPath(string packageId) => "content-cooked/" + packageId;
    private string RepositoryRoot() => Path.GetFullPath(Path.Combine(_projectRoot, "..", ".."));

    private bool IsInsideCharacterPackages(string fullPath)
    {
        string root = Path.GetFullPath(CharacterPackagesFullRoot()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || StringEquals(candidate, root);
    }

    private string ProjectRelative(string fullPath)
    {
        string root = _projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalized = Path.GetFullPath(fullPath).Replace('\\', '/');
        string normalizedRoot = root.Replace('\\', '/');
        return normalized.StartsWith(normalizedRoot, StringComparison.Ordinal)
            ? normalized.Substring(normalizedRoot.Length).TrimEnd('/')
            : normalized;
    }

    private void WriteSuccessStatus(CharacterAssetCookResult result, CharacterPackageAssemblyResult assembled, string packageId)
    {
        var status = new CharacterCookStatus
        {
            State = "Valid",
            SourceHash = assembled.SourceHash,
            CurrentSourceHash = assembled.SourceHash,
            CookedSourceHash = assembled.SourceHash,
            LastCookedSourceHash = assembled.SourceHash,
            CookedContentHash = assembled.CookedContentHash,
            PackageHash = assembled.PackageHash,
            GeneratedSourceHash = assembled.SourceHash,
            Dependencies = result.Dependencies.Select(ToStatusDependency).ToList(),
            Payloads = assembled.Payloads.Select(ToStatusPayload).ToList(),
            Diagnostics = assembled.Diagnostics.Select(ToStatusDiagnostic).ToList(),
        };
        WriteStatus(status, packageId);
    }

    private void WriteStatus(CharacterCookStatus status, string packageId)
    {
        string path = Path.Combine(_projectRoot, CharacterCookOutput.For(packageId).IntermediateDirectory, CharacterCookOutput.For(packageId).StatusFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonUtility.ToJson(status, true));
    }

    private static byte[] UpdateFightGuyRoster(byte[] priorBytes, CharacterPackageAssemblyResult assembled)
    {
        var manifest = BuiltInRosterManifestCodec.ParseCooked(Encoding.UTF8.GetString(priorBytes));
        using var document = JsonDocument.Parse(assembled.ManifestBytes);
        string version = document.RootElement.GetProperty("version").GetString() ?? "";
        var entries = manifest.Entries.Select(x => x.Selector == CharacterClass.FightGuy
            ? new BuiltInRosterEntry(x.Selector, x.PackageId, new MatchContentPackageRequirement(x.PackageId, version, assembled.CookedContentHash, assembled.PackageHash))
            : x).ToArray();
        if (!entries.Any(x => x.Selector == CharacterClass.FightGuy)) throw new InvalidDataException("FightGuy roster requirement is missing.");
        return Encoding.UTF8.GetBytes(BuiltInRosterManifestCodec.Serialize(new BuiltInRosterManifest(manifest.SchemaVersion, entries)));
    }

    private static Dictionary<string, byte[]> ReadPackageFiles(string directory)
        => new[]
        {
            CharacterPackageAssembler.ManifestPath,
            CharacterPackageAssembler.RuntimePath,
            CharacterPackageAssembler.PosePath,
            CharacterPackageAssembler.BindingPath,
        }.ToDictionary(x => x, x => File.ReadAllBytes(Path.Combine(directory, x)), StringComparer.Ordinal);

    private static void WriteDurably(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(true);
    }

    private static void RestoreFile(string path, byte[] bytes)
    {
        if (bytes == null)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private static byte[] Snapshot(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static FileSnapshot SnapshotFile(string projectRelativePath, string projectRoot)
    {
        string fullPath = Path.Combine(projectRoot, projectRelativePath);
        return new FileSnapshot(Snapshot(fullPath), Snapshot(fullPath + ".meta"));
    }

    private static void RestoreGeneratedAsset(string projectRelativePath, FileSnapshot snapshot, string projectRoot)
    {
        string fullPath = Path.Combine(projectRoot, projectRelativePath);
        if (File.Exists(fullPath) || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(projectRelativePath) != null)
            AssetDatabase.DeleteAsset(projectRelativePath);
        if (snapshot.Bytes == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, snapshot.Bytes);
        if (snapshot.MetaBytes != null) File.WriteAllBytes(fullPath + ".meta", snapshot.MetaBytes);
        AssetDatabase.ImportAsset(projectRelativePath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static CharacterCookStatusDependency ToStatusDependency(CharacterCookDependencyRecord record)
        => new CharacterCookStatusDependency
        {
            Kind = record.Kind,
            Identity = record.Identity,
            Guid = record.Guid,
            DependencyHash = record.DependencyHash,
            MetaHash = record.MetaHash,
            ImporterSettings = record.ImporterSettings,
        };

    private static CharacterCookStatusPayload ToStatusPayload(CharacterPackagePayloadInfo payload)
        => new CharacterCookStatusPayload { Path = payload.Path, Sha256 = payload.Sha256, Size = payload.Size };

    private static CharacterCookStatusDiagnostic ToStatusDiagnostic(CharacterDiagnostic diagnostic)
        => new CharacterCookStatusDiagnostic
        {
            Severity = diagnostic.Severity == CharacterDiagnosticSeverity.Error ? "error" : "warning",
            Code = diagnostic.Code,
            Path = diagnostic.Path,
            Message = diagnostic.Message,
        };

    private static CharacterDiagnostic Error(string code, string path, string message)
        => new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message);

    private static void AddUnique(List<CharacterDiagnostic> target, IEnumerable<CharacterDiagnostic> values)
    {
        if (values == null) return;
        foreach (var value in values)
            if (!target.Any(x => x.Severity == value.Severity && x.Code == value.Code && x.Path == value.Path && x.Message == value.Message))
                target.Add(value);
    }

    private static bool StringEquals(string left, string right)
        => !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.Ordinal);

    private sealed class PackageContext
    {
        public string FullRoot = "";
        public string ProjectRelativeRoot = "";
        public string PackageId = "";
        public string DisplayName = "";
        public string PackageJson = "";
        public string CharacterJson = "";
        public CharacterPackageSource Source;
        public CharacterAssetCatalog Catalog;
        public IReadOnlyList<CharacterDiagnostic> Diagnostics = Array.Empty<CharacterDiagnostic>();
    }

    private sealed class ArtifactSnapshot
    {
        public bool IsMissing { get; private set; }
        public bool IsInvalid { get; private set; }
        public string SourceHash { get; private set; }
        public string CookedContentHash { get; private set; }
        public string PackageHash { get; private set; }
        public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; private set; }

        public static ArtifactSnapshot Missing() => new ArtifactSnapshot { IsMissing = true, Diagnostics = Array.Empty<CharacterDiagnostic>() };
        public static ArtifactSnapshot Invalid(IReadOnlyList<CharacterDiagnostic> diagnostics) => new ArtifactSnapshot { IsInvalid = true, Diagnostics = diagnostics };
        public static ArtifactSnapshot Valid(string sourceHash, string cookedContentHash, string packageHash, IReadOnlyList<CharacterDiagnostic> diagnostics)
            => new ArtifactSnapshot { SourceHash = sourceHash, CookedContentHash = cookedContentHash, PackageHash = packageHash, Diagnostics = diagnostics };
    }

    private sealed class FileSnapshot
    {
        public byte[] Bytes { get; }
        public byte[] MetaBytes { get; }
        public FileSnapshot(byte[] bytes, byte[] metaBytes) { Bytes = bytes; MetaBytes = metaBytes; }
    }
}

public sealed class CharacterPackageInspectionResult
{
    [JsonProperty("success", NullValueHandling = NullValueHandling.Include)] public bool Success { get; }
    [JsonProperty("packageId", NullValueHandling = NullValueHandling.Include)] public string PackageId { get; }
    [JsonProperty("displayName", NullValueHandling = NullValueHandling.Include)] public string DisplayName { get; }
    [JsonProperty("sourcePath", NullValueHandling = NullValueHandling.Include)] public string SourcePath { get; }
    [JsonProperty("status", NullValueHandling = NullValueHandling.Include)] public string Status { get; }
    [JsonProperty("dirtyOrStale", NullValueHandling = NullValueHandling.Include)] public bool DirtyOrStale { get; }
    [JsonProperty("sourceHash", NullValueHandling = NullValueHandling.Include)] public string SourceHash { get; }
    [JsonProperty("cookedSourceHash", NullValueHandling = NullValueHandling.Include)] public string CookedSourceHash { get; }
    [JsonProperty("cookedContentHash", NullValueHandling = NullValueHandling.Include)] public string CookedContentHash { get; }
    [JsonProperty("packageHash", NullValueHandling = NullValueHandling.Include)] public string PackageHash { get; }
    [JsonProperty("staleReasons", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<CharacterPackageStaleReason> StaleReasons { get; }
    [JsonProperty("slots", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<CharacterPackageSlotSummary> Slots { get; }
    [JsonProperty("diagnostics", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }
    internal CharacterPackageSource Source { get; }
    internal CharacterAssetCatalog Catalog { get; }
    internal IReadOnlyList<CharacterDiagnostic> RawDiagnostics { get; }

    private CharacterPackageInspectionResult(
        bool success,
        string packageId,
        string displayName,
        string sourcePath,
        string status,
        bool dirtyOrStale,
        string sourceHash,
        string cookedSourceHash,
        string cookedContentHash,
        string packageHash,
        IReadOnlyList<CharacterPackageStaleReason> staleReasons,
        IReadOnlyList<CharacterPackageSlotSummary> slots,
        IReadOnlyList<CharacterPackageDiagnosticResult> diagnostics,
        CharacterPackageSource source,
        CharacterAssetCatalog catalog,
        IReadOnlyList<CharacterDiagnostic> rawDiagnostics)
    {
        Success = success;
        PackageId = packageId;
        DisplayName = displayName;
        SourcePath = sourcePath;
        Status = status;
        DirtyOrStale = dirtyOrStale;
        SourceHash = sourceHash;
        CookedSourceHash = cookedSourceHash;
        CookedContentHash = cookedContentHash;
        PackageHash = packageHash;
        StaleReasons = staleReasons;
        Slots = slots;
        Diagnostics = diagnostics;
        Source = source;
        Catalog = catalog;
        RawDiagnostics = rawDiagnostics;
    }

    internal static CharacterPackageInspectionResult CreateSuccess(
        string packageId,
        string displayName,
        string sourcePath,
        string status,
        bool dirtyOrStale,
        string sourceHash,
        string cookedSourceHash,
        string cookedContentHash,
        string packageHash,
        IReadOnlyList<CharacterPackageStaleReason> staleReasons,
        IReadOnlyList<CharacterPackageSlotSummary> slots,
        IReadOnlyList<CharacterDiagnostic> diagnostics,
        CharacterPackageSource source,
        CharacterAssetCatalog catalog)
        => new CharacterPackageInspectionResult(
            true,
            packageId,
            displayName,
            sourcePath,
            status,
            dirtyOrStale,
            sourceHash,
            cookedSourceHash,
            cookedContentHash,
            packageHash,
            staleReasons.ToArray(),
            slots.ToArray(),
            CharacterPackageDiagnosticResult.From(diagnostics),
            source,
            catalog,
            diagnostics.ToArray());

    internal static CharacterPackageInspectionResult CreateFailure(IReadOnlyList<CharacterDiagnostic> diagnostics)
        => new CharacterPackageInspectionResult(
            false,
            null,
            null,
            null,
            "invalid",
            true,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<CharacterPackageSlotSummary>(),
            CharacterPackageDiagnosticResult.From(diagnostics),
            null,
            null,
            diagnostics?.ToArray() ?? Array.Empty<CharacterDiagnostic>());
}

public sealed class CharacterPackageCookResult
{
    [JsonProperty("success", NullValueHandling = NullValueHandling.Include)] public bool Success { get; }
    [JsonProperty("packageId", NullValueHandling = NullValueHandling.Include)] public string PackageId { get; }
    [JsonProperty("sourcePath", NullValueHandling = NullValueHandling.Include)] public string SourcePath { get; }
    [JsonProperty("cookedPath", NullValueHandling = NullValueHandling.Include)] public string CookedPath { get; }
    [JsonProperty("sourceHash", NullValueHandling = NullValueHandling.Include)] public string SourceHash { get; }
    [JsonProperty("cookedSourceHash", NullValueHandling = NullValueHandling.Include)] public string CookedSourceHash { get; }
    [JsonProperty("cookedContentHash", NullValueHandling = NullValueHandling.Include)] public string CookedContentHash { get; }
    [JsonProperty("packageHash", NullValueHandling = NullValueHandling.Include)] public string PackageHash { get; }
    [JsonProperty("diagnostics", NullValueHandling = NullValueHandling.Include)] public IReadOnlyList<CharacterPackageDiagnosticResult> Diagnostics { get; }
    internal CharacterPackageAssemblyResult Assembly { get; }
    internal IReadOnlyList<CharacterDiagnostic> RawDiagnostics { get; }

    private CharacterPackageCookResult(
        bool success,
        string packageId,
        string sourcePath,
        string cookedPath,
        string sourceHash,
        string cookedSourceHash,
        string cookedContentHash,
        string packageHash,
        IReadOnlyList<CharacterPackageDiagnosticResult> diagnostics,
        CharacterPackageAssemblyResult assembly,
        IReadOnlyList<CharacterDiagnostic> rawDiagnostics)
    {
        Success = success;
        PackageId = packageId;
        SourcePath = sourcePath;
        CookedPath = cookedPath;
        SourceHash = string.IsNullOrEmpty(sourceHash) ? null : sourceHash;
        CookedSourceHash = string.IsNullOrEmpty(cookedSourceHash) ? null : cookedSourceHash;
        CookedContentHash = string.IsNullOrEmpty(cookedContentHash) ? null : cookedContentHash;
        PackageHash = string.IsNullOrEmpty(packageHash) ? null : packageHash;
        Diagnostics = diagnostics;
        Assembly = assembly;
        RawDiagnostics = rawDiagnostics;
    }

    internal static CharacterPackageCookResult CreateSuccess(
        string packageId,
        string sourcePath,
        string cookedPath,
        string sourceHash,
        string cookedContentHash,
        string packageHash,
        IReadOnlyList<CharacterDiagnostic> diagnostics,
        CharacterPackageAssemblyResult assembly)
        => new CharacterPackageCookResult(
            true,
            packageId,
            sourcePath,
            cookedPath,
            sourceHash,
            sourceHash,
            cookedContentHash,
            packageHash,
            CharacterPackageDiagnosticResult.From(diagnostics),
            assembly,
            diagnostics.ToArray());

    internal static CharacterPackageCookResult CreateFailure(
        string packageId,
        string sourcePath,
        string cookedPath,
        string sourceHash,
        string packageHash,
        IReadOnlyList<CharacterDiagnostic> diagnostics,
        CharacterPackageAssemblyResult assembly)
        => new CharacterPackageCookResult(
            false,
            packageId,
            sourcePath,
            cookedPath,
            sourceHash,
            null,
            null,
            packageHash,
            CharacterPackageDiagnosticResult.From(diagnostics),
            assembly,
            diagnostics?.ToArray() ?? Array.Empty<CharacterDiagnostic>());
}

public sealed class CharacterPackageStaleReason
{
    [JsonProperty("code", NullValueHandling = NullValueHandling.Include)] public string Code { get; }
    [JsonProperty("path", NullValueHandling = NullValueHandling.Include)] public string Path { get; }
    [JsonProperty("message", NullValueHandling = NullValueHandling.Include)] public string Message { get; }

    public CharacterPackageStaleReason(string code, string path, string message)
    {
        Code = code;
        Path = path;
        Message = message;
    }
}

public sealed class CharacterPackageSlotSummary
{
    [JsonProperty("id", NullValueHandling = NullValueHandling.Include)] public string Id { get; }
    [JsonProperty("name", NullValueHandling = NullValueHandling.Include)] public string Name { get; }
    [JsonProperty("stageCount", NullValueHandling = NullValueHandling.Include)] public int StageCount { get; }
    [JsonProperty("present", NullValueHandling = NullValueHandling.Include)] public bool Present { get; }

    public CharacterPackageSlotSummary(string id, string name, int stageCount, bool present)
    {
        Id = id;
        Name = name;
        StageCount = stageCount;
        Present = present;
    }
}

public sealed class CharacterPackageDiagnosticResult
{
    [JsonProperty("severity", NullValueHandling = NullValueHandling.Include)] public string Severity { get; }
    [JsonProperty("code", NullValueHandling = NullValueHandling.Include)] public string Code { get; }
    [JsonProperty("path", NullValueHandling = NullValueHandling.Include)] public string Path { get; }
    [JsonProperty("message", NullValueHandling = NullValueHandling.Include)] public string Message { get; }

    private CharacterPackageDiagnosticResult(string severity, string code, string path, string message)
    {
        Severity = severity;
        Code = code;
        Path = path;
        Message = message;
    }

    internal static IReadOnlyList<CharacterPackageDiagnosticResult> From(IEnumerable<CharacterDiagnostic> diagnostics)
        => (diagnostics ?? Array.Empty<CharacterDiagnostic>())
            .Select(x => new CharacterPackageDiagnosticResult(
                x.Severity == CharacterDiagnosticSeverity.Error ? "error" : "warning",
                x.Code,
                x.Path,
                x.Message))
            .ToArray();
}

[Serializable]
public sealed class CharacterCookStatus
{
    public string State = "Unknown";
    public string SourceHash = "";
    public string CurrentSourceHash = "";
    public string CookedSourceHash = "";
    public string LastCookedSourceHash = "";
    public string CookedContentHash = "";
    public string PackageHash = "";
    public string GeneratedSourceHash = "";
    public List<CharacterCookStatusDependency> Dependencies = new List<CharacterCookStatusDependency>();
    public List<CharacterCookStatusPayload> Payloads = new List<CharacterCookStatusPayload>();
    public List<CharacterCookStatusDiagnostic> Diagnostics = new List<CharacterCookStatusDiagnostic>();
}

[Serializable]
public sealed class CharacterCookStatusDependency
{
    public string Kind = "";
    public string Identity = "";
    public string Guid = "";
    public string DependencyHash = "";
    public string MetaHash = "";
    public string ImporterSettings = "";
}

[Serializable]
public sealed class CharacterCookStatusPayload
{
    public string Path = "";
    public string Sha256 = "";
    public long Size;
}

[Serializable]
public sealed class CharacterCookStatusDiagnostic
{
    public string Severity = "warning";
    public string Code = "";
    public string Path = "";
    public string Message = "";
}
