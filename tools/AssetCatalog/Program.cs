using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SlopArena.AssetCatalog;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal sealed class QueryProfile
    {
        public int SchemaVersion { get; set; }
        public string Concept { get; set; } = "";
        public string[] PreferredTags { get; set; } = Array.Empty<string>();
        public string[] Terms { get; set; } = Array.Empty<string>();
        public string[] ExcludedTags { get; set; } = Array.Empty<string>();
        public int CandidateLimit { get; set; }
        public int PerFamilyLimit { get; set; }
        public RoleProfile[] Roles { get; set; } = Array.Empty<RoleProfile>();
    }

    internal sealed class RoleProfile
    {
        public string Name { get; set; } = "";
        public int Quota { get; set; }
        public string[] Categories { get; set; } = Array.Empty<string>();
        public string[] StageTags { get; set; } = Array.Empty<string>();
        public string[] Terms { get; set; } = Array.Empty<string>();
    }

    internal sealed class CatalogManifest
    {
        public int SchemaVersion { get; set; }
        public string EntriesFile { get; set; } = "";
        public CatalogPack[] Packs { get; set; } = Array.Empty<CatalogPack>();
    }

    internal sealed class CatalogPack
    {
        public string SourcePack { get; set; } = "";
        public string SourceArchive { get; set; } = "";
        public int PrefabCount { get; set; }
    }

    internal sealed class CatalogEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string SourcePack { get; set; } = "";
        public string Category { get; set; } = "";
        public string[] Tags { get; set; } = Array.Empty<string>();
        public string[] StageTags { get; set; } = Array.Empty<string>();
    }

    internal sealed class Workset
    {
        public int SchemaVersion { get; set; } = 1;
        public string Concept { get; set; } = "";
        public int CatalogSchemaVersion { get; set; }
        public List<WorksetCandidate> Candidates { get; set; } = new();
        public List<ShortlistItem> Shortlist { get; set; } = new();
        public List<RequiredPack> RequiredPacks { get; set; } = new();
        public List<Diagnostic> Diagnostics { get; set; } = new();
    }

    internal class WorksetCandidate
    {
        public string SourcePack { get; set; } = "";
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Category { get; set; } = "";
        public string[] Tags { get; set; } = Array.Empty<string>();
        public string[] StageTags { get; set; } = Array.Empty<string>();
        public int Score { get; set; }
        public List<string> Reasons { get; set; } = new();
    }

    internal sealed class ShortlistItem : WorksetCandidate
    {
        public string Role { get; set; } = "";
        public int RoleScore { get; set; }
        public string SelectionStatus { get; set; } = "selected";
    }

    internal sealed class RequiredPack
    {
        public string SourcePack { get; set; } = "";
        public string SourceArchive { get; set; } = "";
    }

    internal sealed class Diagnostic
    {
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
    }

    internal sealed class MaterializeResult
    {
        public bool Success { get; set; }
        public List<MaterializePackResult> Packs { get; set; } = new();
        public List<Diagnostic> Diagnostics { get; set; } = new();
    }

    internal sealed class MaterializePackResult
    {
        public string SourcePack { get; set; } = "";
        public int Copied { get; set; }
        public int Unchanged { get; set; }
        public int PrefabPathsExpected { get; set; }
    }

    private sealed class StagedPack
    {
        public RequiredPack Pack { get; init; } = new();
        public string Root { get; init; } = "";
        public List<StagedFile> Files { get; } = new();
        public int PrefabPathsExpected { get; set; }
    }

    private sealed class StagedFile
    {
        public string RelativePath { get; init; } = "";
        public string StagedPath { get; init; } = "";
        public string DestinationPath { get; init; } = "";
    }

    internal static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return Fail("missing verb (query, probe, or materialize)");
            return args[0] switch
            {
                "query" => RunQuery(args),
                "probe" => RunProbe(args),
                "materialize" => RunMaterialize(args),
                _ => Fail($"unknown verb '{args[0]}'")
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException or IOException)
        {
            return Fail(ex.Message);
        }
    }

    private static int RunQuery(string[] args)
    {
        string profilePath = RequiredArg(args, "--profile");
        string outputPath = RequiredArg(args, "--out");
        string catalogPath = OptionalArg(args, "--catalog") ?? "docs/assets/catalog/index.json";
        EnsureFile(profilePath, "profile");
        EnsureFile(catalogPath, "catalog");

        QueryProfile profile = Deserialize<QueryProfile>(File.ReadAllText(profilePath), "profile");
        ValidateProfile(profile);
        CatalogManifest manifest = Deserialize<CatalogManifest>(File.ReadAllText(catalogPath), "catalog");
        ValidateManifest(manifest);
        string entriesPath = ResolvePath(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!, manifest.EntriesFile);
        EnsureFile(entriesPath, "catalog entries");

        Workset workset = BuildWorkset(profile, manifest, ReadEntries(entriesPath));
        WriteJson(outputPath, workset);
        Console.Error.WriteLine($"wrote {outputPath} ({workset.Candidates.Count} candidates, {workset.Shortlist.Count} shortlist items)");
        return 0;
    }
    private static int RunProbe(string[] args)
    {
        string profilePath = RequiredArg(args, "--profile");
        string outputPath = RequiredArg(args, "--out");
        string catalogPath = OptionalArg(args, "--catalog") ?? "docs/assets/catalog/index.json";
        int perRole = 1;
        string? perRoleArgument = OptionalArg(args, "--per-role");
        if (perRoleArgument != null && (!int.TryParse(perRoleArgument, out perRole) || perRole is < 1 or > 3))
            throw new ArgumentException("--per-role must be 1-3");
        EnsureFile(profilePath, "profile");
        EnsureFile(catalogPath, "catalog");

        QueryProfile profile = Deserialize<QueryProfile>(File.ReadAllText(profilePath), "profile");
        ValidateProfile(profile);
        CatalogManifest manifest = Deserialize<CatalogManifest>(File.ReadAllText(catalogPath), "catalog");
        ValidateManifest(manifest);
        string entriesPath = ResolvePath(Path.GetDirectoryName(Path.GetFullPath(catalogPath))!, manifest.EntriesFile);
        EnsureFile(entriesPath, "catalog entries");

        Workset workset = BuildProbeWorkset(profile, manifest, ReadEntries(entriesPath), perRole);
        WriteJson(outputPath, workset);
        Console.Error.WriteLine($"wrote probe {outputPath} ({workset.Candidates.Count} candidates, {workset.Shortlist.Count} shortlist items)");
        return 0;
    }

    private static int RunMaterialize(string[] args)
    {
        string worksetPath = RequiredArg(args, "--workset");
        string unityProject = OptionalArg(args, "--unity-project") ?? "client/Unity";
        string staging = OptionalArg(args, "--staging") ?? ".asset-catalog-cache/materialize";
        EnsureFile(worksetPath, "workset");
        EnsureDirectory(unityProject, "Unity project");
        Workset workset = Deserialize<Workset>(File.ReadAllText(worksetPath), "workset");
        MaterializeResult result = Materialize(workset, Path.GetFullPath(unityProject), Path.GetFullPath(staging));
        string status = result.Success ? "success" : "failed";
        Console.Error.WriteLine($"materialize {status}: {result.Packs.Sum(x => x.Copied)} copied, {result.Packs.Sum(x => x.Unchanged)} unchanged");
        foreach (Diagnostic diagnostic in result.Diagnostics)
            Console.Error.WriteLine($"{diagnostic.Code}: {diagnostic.Message}");
        return result.Success ? 0 : 1;
    }

    internal static Workset BuildWorkset(QueryProfile profile, CatalogManifest manifest, IEnumerable<CatalogEntry> entries, int? maxPerRole = null)
    {
        ValidateProfile(profile);
        ValidateManifest(manifest);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ranked = new List<WorksetCandidate>();
        foreach (CatalogEntry entry in entries)
        {
            ValidateEntry(entry);
            string identity = entry.SourcePack + "\0" + entry.Id;
            if (!seen.Add(identity)) throw new InvalidDataException($"duplicate catalog record ({entry.SourcePack},{entry.Id})");
            ranked.Add(ScoreEntry(profile, entry));
        }

        ranked = ranked
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.SourcePack, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
        List<WorksetCandidate> candidates = ranked.Take(profile.CandidateLimit).ToList();

        var shortlist = new List<ShortlistItem>();
        var roleCounts = profile.Roles.ToDictionary(x => Normalize(x.Name), _ => 0, StringComparer.Ordinal);
        var familyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new List<(WorksetCandidate Candidate, RoleProfile Role, int Score)>();
        foreach (WorksetCandidate candidate in ranked)
        foreach (RoleProfile role in profile.Roles)
        {
            int score = RoleScore(candidate, role, out bool matched);
            if (matched) pairs.Add((candidate, role, score));
        }

        foreach (var pair in pairs
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Candidate.Score)
            .ThenBy(x => x.Role.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Candidate.SourcePack, StringComparer.Ordinal)
            .ThenBy(x => x.Candidate.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Candidate.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Candidate.Id, StringComparer.Ordinal))
        {
            string roleKey = Normalize(pair.Role.Name);
            string identity = pair.Candidate.SourcePack + "\0" + pair.Candidate.Id;
            string family = FamilyKey(pair.Candidate);
            int roleQuota = maxPerRole.HasValue ? Math.Min(pair.Role.Quota, maxPerRole.Value) : pair.Role.Quota;
            if (roleCounts[roleKey] >= roleQuota || used.Contains(identity)) continue;
            familyCounts.TryGetValue(family, out int familyCount);
            if (familyCount >= profile.PerFamilyLimit) continue;
            roleCounts[roleKey]++;
            familyCounts[family] = familyCount + 1;
            used.Add(identity);
            shortlist.Add(new ShortlistItem
            {
                SourcePack = pair.Candidate.SourcePack,
                Id = pair.Candidate.Id,
                Name = pair.Candidate.Name,
                Path = pair.Candidate.Path,
                Category = pair.Candidate.Category,
                Tags = pair.Candidate.Tags,
                StageTags = pair.Candidate.StageTags,
                Score = pair.Candidate.Score,
                Reasons = pair.Candidate.Reasons,
                Role = pair.Role.Name,
                RoleScore = pair.Score,
                SelectionStatus = "selected"
            });
        }


        var diagnostics = new List<Diagnostic>();
        foreach (RoleProfile role in profile.Roles)
        {
            int assigned = roleCounts[Normalize(role.Name)];
            int roleQuota = role.Quota;
            if (assigned < roleQuota)
                diagnostics.Add(new Diagnostic { Code = "ROLE_UNDERFILLED", Message = $"Role '{role.Name}' assigned {assigned} of {roleQuota}." });
        }

        var packMap = manifest.Packs.ToDictionary(x => x.SourcePack, StringComparer.Ordinal);
        var required = shortlist.Select(x => x.SourcePack).Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(packName =>
            {
                if (!packMap.TryGetValue(packName, out CatalogPack? pack))
                    throw new InvalidDataException($"shortlist references unknown source pack '{packName}'");
                string archive = ToUnityArchivePath(NormalizeArchivePath(pack.SourceArchive));
                return new RequiredPack { SourcePack = packName, SourceArchive = archive };
            }).ToList();

        return new Workset
        {
            Concept = profile.Concept,
            CatalogSchemaVersion = manifest.SchemaVersion,
            Candidates = candidates,
            Shortlist = shortlist,
            RequiredPacks = required,
            Diagnostics = diagnostics
        };
    }

    internal static Workset BuildProbeWorkset(QueryProfile profile, CatalogManifest manifest, IEnumerable<CatalogEntry> entries, int perRole)
    {
        if (perRole is < 1 or > 3) throw new ArgumentException("probe per-role must be 1-3");
        List<CatalogEntry> materializedEntries = entries.ToList();
        Workset full = BuildWorkset(profile, manifest, materializedEntries);
        Workset probe = BuildWorkset(profile, manifest, materializedEntries, perRole);
        probe.Diagnostics = full.Diagnostics;
        return probe;
    }

    internal static MaterializeResult Materialize(Workset workset, string unityProjectRoot, string stagingRoot)
    {
        if (workset.Shortlist == null || workset.Shortlist.Count == 0)
            throw new InvalidDataException("workset shortlist must not be empty");
        string projectRoot = Path.GetFullPath(unityProjectRoot);
        string staging = Path.GetFullPath(stagingRoot);
        string allowedArchiveRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets", "LowPolyMegaBundle"));
        if (!Directory.Exists(projectRoot)) throw new DirectoryNotFoundException($"Unity project does not exist: {unityProjectRoot}");
        Directory.CreateDirectory(staging);
        foreach (string existing in Directory.GetDirectories(staging)) Directory.Delete(existing, true);

        var result = new MaterializeResult { Success = false };
        var staged = new List<StagedPack>();
        var globalDestinations = new HashSet<string>(StringComparer.Ordinal);
        foreach (RequiredPack pack in workset.RequiredPacks.OrderBy(x => x.SourcePack, StringComparer.Ordinal))
        {
            string archiveRelative = NormalizeArchivePath(pack.SourceArchive);
            string archive = Path.GetFullPath(Path.Combine(projectRoot, archiveRelative));
            if (!IsUnder(archive, allowedArchiveRoot) || !archive.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
            {
                result.Diagnostics.Add(new Diagnostic { Code = "INVALID_SOURCE_ARCHIVE", Message = $"Archive for '{pack.SourcePack}' is outside LowPolyMegaBundle or is not a .unitypackage." });
                continue;
            }
            if (!File.Exists(archive))
            {
                result.Diagnostics.Add(new Diagnostic { Code = "SOURCE_ARCHIVE_MISSING", Message = archiveRelative });
                continue;
            }
            try
            {
                StagedPack one = StagePack(pack, archive, projectRoot, staging, workset, globalDestinations);
                staged.Add(one);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                result.Diagnostics.Add(new Diagnostic { Code = "INVALID_ARCHIVE", Message = $"{pack.SourcePack}: {ex.Message}" });
            }
        }

        if (result.Diagnostics.Count != 0) return result;
        foreach (StagedPack pack in staged)
        {
            var packResult = new MaterializePackResult { SourcePack = pack.Pack.SourcePack, PrefabPathsExpected = pack.PrefabPathsExpected };
            foreach (StagedFile file in pack.Files)
            {
                if (File.Exists(file.DestinationPath))
                {
                    if (!FilesEqual(file.StagedPath, file.DestinationPath))
                    {
                        result.Diagnostics.Add(new Diagnostic { Code = "IMPORT_CONFLICT", Message = file.RelativePath });
                        break;
                    }
                    packResult.Unchanged++;
                }
                else packResult.Copied++;
            }
            result.Packs.Add(packResult);
            if (result.Diagnostics.Count != 0) return result;
        }

        foreach (StagedPack pack in staged)
        foreach (StagedFile file in pack.Files)
        {
            if (File.Exists(file.DestinationPath)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file.DestinationPath)!);
            File.Copy(file.StagedPath, file.DestinationPath, false);
        }
        result.Success = true;
        return result;
    }

    private static StagedPack StagePack(RequiredPack pack, string archive, string projectRoot,
        string stagingRoot, Workset workset, HashSet<string> globalDestinations)
    {
        var pathnames = new Dictionary<string, string>(StringComparer.Ordinal);
        using (TarReader reader = OpenArchive(archive))
        {
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) != null)
            {
                string[] pieces = entry.Name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (pieces.Length == 2 && pieces[1] == "pathname" && pieces[0].Length > 0)
                {
                    Stream stream = entry.DataStream ?? throw new InvalidDataException($"missing pathname payload for {pieces[0]}");
                    using var text = new StreamReader(stream, leaveOpen: true);
                    string pathname = text.ReadToEnd().Trim();
                    pathnames.Add(pieces[0], NormalizeUnityPath(pathname));
                }
            }
        }

        string packRoot = Path.Combine(stagingRoot, SanitizeSegment(pack.SourcePack));
        Directory.CreateDirectory(packRoot);
        var stagedPack = new StagedPack { Pack = pack, Root = packRoot };
        var payloads = new Dictionary<string, (byte[]? Asset, byte[]? Meta)>(StringComparer.Ordinal);
        using (TarReader reader = OpenArchive(archive))
        {
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) != null)
            {
                string[] pieces = entry.Name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (pieces.Length != 2 || !pathnames.ContainsKey(pieces[0])) continue;
                if (pieces[1] != "asset" && pieces[1] != "asset.meta") continue;
                Stream stream = entry.DataStream ?? throw new InvalidDataException($"missing payload for {pieces[0]}");
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                if (!payloads.TryGetValue(pieces[0], out var payload)) payload = (null, null);
                if (pieces[1] == "asset")
                {
                    if (payload.Asset != null) throw new InvalidDataException($"duplicate asset payload for {pieces[0]}");
                    payload.Asset = buffer.ToArray();
                }
                else
                {
                    if (payload.Meta != null) throw new InvalidDataException($"duplicate asset.meta payload for {pieces[0]}");
                    payload.Meta = buffer.ToArray();
                }
                payloads[pieces[0]] = payload;
            }
        }

        var expected = workset.Shortlist.Where(x => x.SourcePack == pack.SourcePack).Select(x => NormalizeUnityPath(x.Path)).ToHashSet(StringComparer.Ordinal);
        stagedPack.PrefabPathsExpected = expected.Count;
        foreach (var mapping in pathnames.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (!payloads.TryGetValue(mapping.Key, out var payload) || payload.Meta == null)
                throw new InvalidDataException($"missing asset.meta payload for {mapping.Key}");
            if (payload.Asset != null)
                AddStagedFile(stagedPack, mapping.Value, payload.Asset, projectRoot, globalDestinations);
            AddStagedFile(stagedPack, mapping.Value + ".meta", payload.Meta, projectRoot, globalDestinations);
        }
        return stagedPack;
    }

    private static void AddStagedFile(StagedPack pack, string relativePath, byte[] bytes, string projectRoot,
        HashSet<string> globalDestinations)
    {
        string normalized = NormalizeUnityPath(relativePath);
        string stagedPath = Path.GetFullPath(Path.Combine(pack.Root, normalized.Substring("Assets/".Length)));
        string destination = Path.GetFullPath(Path.Combine(projectRoot, normalized));
        if (!IsUnder(stagedPath, pack.Root) || !IsUnder(destination, projectRoot) || !globalDestinations.Add(destination))
            throw new InvalidDataException($"destination escapes root or is duplicated: {normalized}");
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
        File.WriteAllBytes(stagedPath, bytes);
        pack.Files.Add(new StagedFile { RelativePath = normalized, StagedPath = stagedPath, DestinationPath = destination });
    }

    private static WorksetCandidate ScoreEntry(QueryProfile profile, CatalogEntry entry)
    {
        string name = Normalize(entry.Name);
        string path = Normalize(entry.Path);
        var tags = entry.Tags.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var stageTags = entry.StageTags.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var reasons = new List<string>();
        int score = 0;
        foreach (string tag in DistinctNormalized(profile.PreferredTags))
            if (tags.Contains(tag)) { score += 12; reasons.Add($"tag:{tag} (+12)"); }
        foreach (string term in DistinctNormalized(profile.Terms))
        {
            if (name.Contains(term, StringComparison.Ordinal)) { score += 10; reasons.Add($"name:{term} (+10)"); }
            else if (path.Contains(term, StringComparison.Ordinal)) { score += 4; reasons.Add($"path:{term} (+4)"); }
        }
        if (profile.Roles.Any(role => role.Categories.Select(Normalize).Contains(Normalize(entry.Category), StringComparer.Ordinal)))
        {
            score += 6; reasons.Add($"category:{Normalize(entry.Category)} (+6)");
        }
        foreach (string stageTag in profile.Roles.SelectMany(x => x.StageTags).Select(Normalize).Distinct(StringComparer.Ordinal))
            if (stageTags.Contains(stageTag)) { score += 5; reasons.Add($"stage-tag:{stageTag} (+5)"); }
        foreach (string tag in DistinctNormalized(profile.ExcludedTags))
            if (tags.Contains(tag)) { score -= 12; reasons.Add($"excluded-tag:{tag} (-12)"); }
        return ToCandidate(entry, score, reasons);
    }

    private static int RoleScore(WorksetCandidate candidate, RoleProfile role, out bool matched)
    {
        string category = Normalize(candidate.Category);
        var stageTags = candidate.StageTags.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        string name = Normalize(candidate.Name);
        string path = Normalize(candidate.Path);
        int score = candidate.Score;
        bool categoryMatch = role.Categories.Select(Normalize).Contains(category, StringComparer.Ordinal);
        if (categoryMatch) score += 7;
        int stageMatches = DistinctNormalized(role.StageTags).Count(stageTags.Contains);
        score += stageMatches * 5;
        int termMatches = DistinctNormalized(role.Terms).Count(term => name.Contains(term, StringComparison.Ordinal) || path.Contains(term, StringComparison.Ordinal));
        score += termMatches * 6;
        matched = categoryMatch || stageMatches > 0 || termMatches > 0;
        return score;
    }

    private static WorksetCandidate ToCandidate(CatalogEntry entry, int score, List<string> reasons) => new()
    {
        SourcePack = entry.SourcePack,
        Id = entry.Id,
        Name = entry.Name,
        Path = entry.Path,
        Category = entry.Category,
        Tags = entry.Tags.ToArray(),
        StageTags = entry.StageTags.ToArray(),
        Score = score,
        Reasons = reasons
    };

    internal static IEnumerable<CatalogEntry> ReadEntries(string path)
    {
        using var reader = new StreamReader(path);
        string? line;
        int lineNumber = 0;
        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            CatalogEntry? entry;
            try { entry = JsonSerializer.Deserialize<CatalogEntry>(line, JsonOptions); }
            catch (JsonException ex) { throw new InvalidDataException($"malformed catalog JSONL at line {lineNumber}: {ex.Message}"); }
            if (entry == null) throw new InvalidDataException($"malformed catalog JSONL at line {lineNumber}");
            try { ValidateEntry(entry); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
            { throw new InvalidDataException($"invalid catalog record at line {lineNumber}: {ex.Message}"); }
            yield return entry;
        }
    }

    private static void ValidateProfile(QueryProfile profile)
    {
        if (profile.SchemaVersion != 1) throw new InvalidDataException("unsupported profile schema version");
        if (string.IsNullOrWhiteSpace(profile.Concept)) throw new InvalidDataException("profile concept is blank");
        if (profile.CandidateLimit is < 1 or > 500) throw new InvalidDataException("candidateLimit must be 1-500");
        if (profile.PerFamilyLimit is < 1 or > 20) throw new InvalidDataException("perFamilyLimit must be 1-20");
        if (profile.Roles == null || profile.Roles.Length == 0) throw new InvalidDataException("profile roles are required");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (RoleProfile role in profile.Roles)
        {
            if (string.IsNullOrWhiteSpace(role.Name) || !names.Add(Normalize(role.Name))) throw new InvalidDataException("role names must be unique and nonblank");
            if (role.Quota is < 1 or > 40) throw new InvalidDataException($"role '{role.Name}' quota must be 1-40");
        }
    }

    private static void ValidateManifest(CatalogManifest manifest)
    {
        if (manifest.SchemaVersion != 2) throw new InvalidDataException("unsupported catalog schema version");
        if (string.IsNullOrWhiteSpace(manifest.EntriesFile)) throw new InvalidDataException("catalog entriesFile is blank");
        if (manifest.Packs == null) throw new InvalidDataException("catalog packs are missing");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (CatalogPack pack in manifest.Packs)
            if (string.IsNullOrWhiteSpace(pack.SourcePack) || !names.Add(pack.SourcePack)) throw new InvalidDataException("catalog pack names must be unique and nonblank");
    }

    private static void ValidateEntry(CatalogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.Path) || string.IsNullOrWhiteSpace(entry.SourcePack))
            throw new InvalidDataException("catalog record has a blank identity or path");
        if (entry.Tags == null || entry.StageTags == null) throw new InvalidDataException("catalog record has null tag arrays");
    }

    private static string FamilyKey(WorksetCandidate candidate)
    {
        string name = Normalize(candidate.Name);
        name = Regex.Replace(name, @"(?:[_\-\s]+(?:lod)?\d+)+$", "", RegexOptions.CultureInvariant);
        return Normalize(candidate.SourcePack) + ":" + name;
    }

    private static string Normalize(string value) => (value ?? "").Trim().ToLowerInvariant();
    private static IEnumerable<string> DistinctNormalized(IEnumerable<string> values) => (values ?? Array.Empty<string>()).Select(Normalize).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal);

    private static string NormalizeUnityPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("pathname is blank");
        string normalized = value.Replace('\\', '/').Trim();
        if (normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(normalized)) throw new InvalidDataException($"absolute pathname: {value}");
        string[] parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "Assets" || parts.Any(x => x == ".." || x == ".")) throw new InvalidDataException($"pathname must be below Assets/: {value}");
        return string.Join("/", parts);
    }

    private static string NormalizeArchivePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("source archive path is blank");
        string normalized = value.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(x => x == "..")) throw new InvalidDataException($"invalid source archive path: {value}");
        return normalized;
    }
    private static string ToUnityArchivePath(string repositoryPath)
    {
        const string prefix = "client/Unity/";
        return repositoryPath.StartsWith(prefix, StringComparison.Ordinal)
            ? repositoryPath.Substring("client/Unity/".Length)
            : repositoryPath;
    }

    private static bool IsUnder(string path, string root)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static TarReader OpenArchive(string path)
    {
        var file = File.OpenRead(path);
        var gzip = new GZipStream(file, CompressionMode.Decompress);
        return new TarReader(new OwnedStream(gzip, file));
    }

    private sealed class OwnedStream : Stream
    {
        private readonly Stream _inner;
        private readonly Stream _owner;
        public OwnedStream(Stream inner, Stream owner) { _inner = inner; _owner = owner; }
        public override bool CanRead => _inner.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { _inner.Dispose(); _owner.Dispose(); } base.Dispose(disposing); }
    }

    private static bool FilesEqual(string a, string b)
    {
        var left = new FileInfo(a); var right = new FileInfo(b);
        if (left.Length != right.Length) return false;
        using var x = File.OpenRead(a); using var y = File.OpenRead(b);
        int bx, by;
        do { bx = x.ReadByte(); by = y.ReadByte(); if (bx != by) return false; } while (bx >= 0);
        return true;
    }

    private static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException($"invalid source pack name: {value}");
        return value;
    }

    private static string ResolvePath(string root, string path) => Path.GetFullPath(Path.Combine(root, path));
    private static void EnsureFile(string path, string label) { if (!File.Exists(path)) throw new FileNotFoundException($"{label} does not exist: {path}"); }
    private static void EnsureDirectory(string path, string label) { if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"{label} does not exist: {path}"); }

    private static T Deserialize<T>(string json, string label)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidDataException($"{label} is empty"); }
        catch (JsonException ex) { throw new InvalidDataException($"malformed {label} JSON: {ex.Message}"); }
    }

    private static void WriteJson(string path, object value)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private static string RequiredArg(string[] args, string name) => OptionalArg(args, name) ?? throw new ArgumentException($"missing required argument {name}");
    private static string? OptionalArg(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"missing value for {name}");
        return args[index + 1];
    }
    private static int Fail(string message) { Console.Error.WriteLine($"error: {message}"); return 1; }
}
