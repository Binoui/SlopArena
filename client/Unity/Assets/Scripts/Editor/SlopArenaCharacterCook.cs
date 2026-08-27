using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;
using SlopArena.Client.Animation;
using SlopArena.Shared;

public static class SlopArenaCharacterCook
{
    public static IReadOnlyList<CharacterDiagnostic> LastDiagnostics { get; private set; } = Array.Empty<CharacterDiagnostic>();
    public const string PackageRoot = "Assets/CharacterPackages/FightGuy";
    public const string CatalogPath = PackageRoot + "/CharacterAssetCatalog.asset";
    public const string StatusPath = "Library/SlopArena/CharacterCook/FightGuy/cook-status.json";

    public static void CookFightGuy()
    {
        if (!TryRecookFightGuy(out string failure)) throw new InvalidOperationException(failure);
    }

    public static void VerifyCommittedFightGuy()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(CatalogPath);
        if (catalog == null) throw new InvalidOperationException($"Missing catalog: {CatalogPath}");
        CharacterAssetCookResult cooked = UnityCharacterAssetCooker.Cook(PackageRoot, catalog, CharacterCookOutput.FightGuy, CharacterCookProfile.TrustedBuiltIn);
        if (cooked.HasErrors) throw new InvalidOperationException(string.Join("\n", cooked.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}")));
        CharacterPackageAssemblyResult assembled = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(cooked));
        if (!assembled.IsValid) throw new InvalidDataException(string.Join("\n", assembled.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}")));
        string directory = Path.Combine(RepositoryRoot(), "content-cooked", "fightguy");
        if (!Directory.Exists(directory)) throw new InvalidDataException("Committed FightGuy package directory is missing.");
        var expectedNames = new HashSet<string>(new[] { CharacterPackageAssembler.ManifestPath, CharacterPackageAssembler.RuntimePath, CharacterPackageAssembler.PosePath, CharacterPackageAssembler.BindingPath }, StringComparer.Ordinal);
        var actualNames = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(x => x.Substring(directory.Length + 1).Replace('\\', '/')).ToArray();
        if (!actualNames.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(expectedNames.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException("Committed FightGuy package file set is not exactly canonical.");
        var committed = ReadPackageFiles(directory);
        CharacterPackageVerificationResult verification = CharacterPackageAssembler.Verify(committed);
        if (!verification.IsValid) throw new InvalidDataException(string.Join("\n", verification.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}")));
        var generated = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [CharacterPackageAssembler.ManifestPath] = assembled.ManifestBytes,
            [CharacterPackageAssembler.RuntimePath] = assembled.RuntimeBytes,
            [CharacterPackageAssembler.PosePath] = assembled.PoseBytes,
            [CharacterPackageAssembler.BindingPath] = assembled.BindingBytes,
        };
        foreach (string name in generated.Keys)
            if (!generated[name].SequenceEqual(committed[name]))
                throw new InvalidDataException($"Committed FightGuy file differs: {name}");
        Debug.Log($"[SlopArena] Verified committed FightGuy package: packageHash={assembled.PackageHash}");
    }

    internal static bool TryRecookFightGuy(out string failure)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(CatalogPath);
        if (catalog == null)
        {
            failure = $"Missing catalog: {CatalogPath}";
            WriteFailureStatus(new[] { UnityCharacterAssetCooker.Error("asset-catalog.schema", "catalog", failure) }, "", "fightguy");
            return false;
        }
        return TryRecookPackage(PackageRoot, catalog, CharacterCookOutput.FightGuy, CharacterCookProfile.TrustedBuiltIn, out _, out failure);
    }

    public static bool TryRecookPackage(
        string packageRoot,
        CharacterAssetCatalog catalog,
        CharacterCookOutput output,
        CharacterCookProfile profile,
        out CharacterPackageAssemblyResult assembly,
        out string failure)
    {
        assembly = null!;
        failure = "";
        CharacterAssetCookResult result = null!;
        CharacterPackageAssemblyResult assembled;
        try
        {
            result = UnityCharacterAssetCooker.Cook(packageRoot, catalog, output, profile);
            LastDiagnostics = result.Diagnostics;
            if (result.HasErrors)
            {
                failure = string.Join("\n", result.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}"));
                LastDiagnostics = result.Diagnostics;
                WriteFailureStatus(result.Diagnostics, result.SourceHash, output.PackageId);
                return false;
            }
            assembled = CharacterPackageAssembler.Assemble(UnityCharacterAssetCooker.BuildPackageInput(result));
            if (!assembled.IsValid)
            {
                failure = string.Join("\n", assembled.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}"));
                LastDiagnostics = assembled.Diagnostics;
                WriteFailureStatus(assembled.Diagnostics, result.SourceHash, output.PackageId);
                return false;
            }
        }
        catch (Exception ex)
        {
            failure = ex.ToString();
            LastDiagnostics = new[] { UnityCharacterAssetCooker.Error("asset-catalog.schema", "cook", ex.Message) };
            WriteFailureStatus(new[] { UnityCharacterAssetCooker.Error("asset-catalog.schema", "cook", ex.Message) }, "", output.PackageId);
            return false;
        }

        string projectRoot = UnityCharacterAssetCooker.ProjectRoot();
        string repositoryRoot = RepositoryRoot();
        string rosterPath = Path.Combine(repositoryRoot, "content-cooked", "roster", CharacterPackageAssembler.ManifestPath);
        string rosterTemporary = rosterPath + ".tmp-" + Guid.NewGuid().ToString("N");
        string rosterBackup = rosterPath + ".previous";
        byte[] previousRoster = output.PackageId == "fightguy" && File.Exists(rosterPath) ? File.ReadAllBytes(rosterPath) : null;
        string canonicalDirectory = Path.Combine(repositoryRoot, "content-cooked", output.PackageId);
        string packageParent = Path.GetDirectoryName(canonicalDirectory)!;
        string temporaryDirectory = Path.Combine(packageParent, "." + output.PackageId + ".tmp-" + Guid.NewGuid().ToString("N"));
        string backupDirectory = Path.Combine(packageParent, "." + output.PackageId + ".backup-" + Guid.NewGuid().ToString("N"));
        string finalPose = Path.Combine(projectRoot, output.IntermediateDirectory, output.PoseFileName);
        string finalBinding = Path.Combine(projectRoot, output.IntermediateDirectory, output.BindingFileName);
        string temporaryAsset = CharacterAnimationCatalogGenerator.TemporaryPath(output.GeneratedAssetPath);
        byte[] previousPose = File.Exists(finalPose) ? File.ReadAllBytes(finalPose) : null;
        byte[] previousBinding = File.Exists(finalBinding) ? File.ReadAllBytes(finalBinding) : null;
        bool hadCanonical = Directory.Exists(canonicalDirectory);
        bool canonicalInstalled = false;
        try
        {
            Directory.CreateDirectory(packageParent);
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
            if (output.PackageId == "fightguy")
            {
                WriteDurably(rosterTemporary, UpdateFightGuyRoster(previousRoster, assembled));
            }
            Directory.CreateDirectory(temporaryDirectory);
            WriteDurably(Path.Combine(temporaryDirectory, CharacterPackageAssembler.ManifestPath), assembled.ManifestBytes);
            WriteDurably(Path.Combine(temporaryDirectory, CharacterPackageAssembler.RuntimePath), assembled.RuntimeBytes);
            WriteDurably(Path.Combine(temporaryDirectory, CharacterPackageAssembler.PosePath), assembled.PoseBytes);
            WriteDurably(Path.Combine(temporaryDirectory, CharacterPackageAssembler.BindingPath), assembled.BindingBytes);
            var written = ReadPackageFiles(temporaryDirectory);
            CharacterPackageVerificationResult verification = CharacterPackageAssembler.Verify(written);
            if (!verification.IsValid)
                throw new InvalidDataException(string.Join("\n", verification.Diagnostics.Select(x => $"{x.Code} {x.Path}: {x.Message}")));
            if (hadCanonical) Directory.Move(canonicalDirectory, backupDirectory);
            Directory.Move(temporaryDirectory, canonicalDirectory);
            canonicalInstalled = true;
            Directory.CreateDirectory(Path.GetDirectoryName(finalPose)!);
            WriteDurably(finalPose, assembled.PoseBytes);
            WriteDurably(finalBinding, assembled.BindingBytes);
            string generatedTemp = CharacterAnimationCatalogGenerator.Generate(assembled.BindingBytes, output.GeneratedAssetPath);
            CharacterAnimationCatalogGenerator.ReplaceTemporary(generatedTemp, output.GeneratedAssetPath);
            if (output.PackageId == "fightguy")
            {
                if (File.Exists(rosterPath)) File.Replace(rosterTemporary, rosterPath, rosterBackup, true);
                else File.Move(rosterTemporary, rosterPath);
            }
            WriteSuccessStatus(result, assembled, output.PackageId);
            if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, true);
            if (File.Exists(rosterTemporary)) File.Delete(rosterTemporary);
            if (File.Exists(rosterBackup)) File.Delete(rosterBackup);
            LastDiagnostics = assembled.Diagnostics;
            assembly = assembled;
            Debug.Log($"[SlopArena] Cooked {output.PackageId}: {result.Animations.Count} animations, sourceHash={result.SourceHash}, packageHash={assembled.PackageHash}");
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.ToString();
            LastDiagnostics = new[] { UnityCharacterAssetCooker.Error("asset-catalog.schema", "cook", ex.Message) };
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
            string generatedTemporaryFullPath = Path.Combine(projectRoot, temporaryAsset);

            if (File.Exists(generatedTemporaryFullPath)) AssetDatabase.DeleteAsset(temporaryAsset);
            RestoreFile(finalPose, previousPose);
            RestoreFile(finalBinding, previousBinding);
            if (output.PackageId == "fightguy")
            {
                RestoreFile(rosterPath, previousRoster);
                if (File.Exists(rosterTemporary)) File.Delete(rosterTemporary);
                if (File.Exists(rosterBackup)) File.Delete(rosterBackup);
            }
            if (canonicalInstalled && Directory.Exists(canonicalDirectory)) Directory.Delete(canonicalDirectory, true);
            if (hadCanonical && Directory.Exists(backupDirectory)) Directory.Move(backupDirectory, canonicalDirectory);
            else if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, true);
            WriteFailureStatus(new[] { UnityCharacterAssetCooker.Error("asset-catalog.schema", "cook", ex.Message) }, result.SourceHash, output.PackageId);
            return false;
        }
    }

    private static byte[] UpdateFightGuyRoster(byte[] priorBytes, CharacterPackageAssemblyResult assembled)
    {
        if (priorBytes == null) throw new InvalidDataException("Cooked roster manifest is missing.");
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

    internal static void MarkStale()
    {
        CharacterCookStatus status = ReadStatus();
        status.State = "Stale";
        var catalog = AssetDatabase.LoadAssetAtPath<CharacterAssetCatalog>(CatalogPath);
        if (catalog != null && UnityCharacterAssetCooker.TryComputeSourceHash(PackageRoot, catalog, CharacterCookProfile.TrustedBuiltIn, out string currentHash, out _))
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
        status.Diagnostics = new List<CharacterCookStatusDiagnostic>
        {
            new CharacterCookStatusDiagnostic { Severity = "warning", Code = "asset-catalog.stale", Path = "catalog", Message = "A package dependency changed; recook is queued." }
        };
        WriteStatus(status);
    }

    internal static CharacterCookStatus ReadStatus() => ReadStatus("fightguy");

    internal static CharacterCookStatus ReadStatus(string packageId)
    {
        string path = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), CharacterCookOutput.For(packageId).IntermediateDirectory, CharacterCookOutput.For(packageId).StatusFileName);
        if (!File.Exists(path)) return new CharacterCookStatus { State = "Unknown", Diagnostics = new List<CharacterCookStatusDiagnostic>() };
        try { return JsonUtility.FromJson<CharacterCookStatus>(File.ReadAllText(path)) ?? new CharacterCookStatus(); }
        catch { return new CharacterCookStatus { State = "Failed", Diagnostics = new List<CharacterCookStatusDiagnostic>() }; }
    }

    private static void WriteSuccessStatus(CharacterAssetCookResult result, CharacterPackageAssemblyResult assembled, string packageId)
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

    private static void WriteFailureStatus(IReadOnlyList<SlopArena.Shared.CharacterDiagnostic> diagnostics, string sourceHash, string packageId)
    {
        CharacterCookStatus prior = ReadStatus(packageId);
        prior.State = "Failed";
        prior.SourceHash = sourceHash;
        prior.CurrentSourceHash = sourceHash;
        prior.Diagnostics = diagnostics.Select(ToStatusDiagnostic).ToList();
        WriteStatus(prior, packageId);
    }

    private static void WriteStatus(CharacterCookStatus status) => WriteStatus(status, "fightguy");

    private static void WriteStatus(CharacterCookStatus status, string packageId)
    {
        CharacterCookOutput output = CharacterCookOutput.For(packageId);
        string path = Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), output.IntermediateDirectory, output.StatusFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonUtility.ToJson(status, true));
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

    private static CharacterCookStatusDiagnostic ToStatusDiagnostic(SlopArena.Shared.CharacterDiagnostic diagnostic)
        => new CharacterCookStatusDiagnostic { Severity = diagnostic.Severity == CharacterDiagnosticSeverity.Error ? "error" : "warning", Code = diagnostic.Code, Path = diagnostic.Path, Message = diagnostic.Message };

    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(UnityCharacterAssetCooker.ProjectRoot(), "..", ".."));

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
