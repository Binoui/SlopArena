using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using SlopArena.Shared;
using UnityEngine;

namespace SlopArena.Client;

public enum LocalContentMode
{
    Development,
    Player,
}

public sealed class LocalContentResolver
{
    private readonly IReadOnlyList<string> _contentRoots;

    private LocalContentResolver(string projectRoot, IReadOnlyList<string> contentRoots)
    {
        ProjectRoot = projectRoot;
        _contentRoots = contentRoots;
    }

    public static LocalContentResolver CreateDefault()
        => CreateForMode(Application.isEditor ? LocalContentMode.Development : LocalContentMode.Player);

    public static LocalContentResolver CreateForMode(LocalContentMode mode)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string repositoryRoot = Directory.GetParent(projectRoot)?.Parent?.FullName ?? projectRoot;
        string streamingRoot = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "content-cooked"));
        string repositoryCookedRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "content-cooked"));
        string[] roots = mode == LocalContentMode.Development
            ? new[] { repositoryCookedRoot, streamingRoot }
            : new[] { streamingRoot };
        return new LocalContentResolver(
            projectRoot,
            new ReadOnlyCollection<string>(roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    public string ProjectRoot { get; }
    public IReadOnlyList<string> ContentRoots => _contentRoots;

    public LocalContentResolution ResolveRoster()
    {
        foreach (string root in _contentRoots)
        {
            string manifestPath = Path.Combine(root, "roster", CharacterPackageAssembler.ManifestPath);
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var roster = BuiltInRosterManifestCodec.Load(manifestPath);
                if (roster.SchemaVersion != BuiltInRosterManifest.CurrentSchemaVersion)
                    return Failure("content.roster.schema", manifestPath, "Roster manifest schema is not supported.");
                return Success(root, manifestPath, roster, null);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidDataException || ex is FormatException)
            {
                return Failure("content.roster.malformed", manifestPath, ex.Message);
            }
        }

        return Failure("content.roster.missing", Path.Combine(_contentRoots[0], "roster", CharacterPackageAssembler.ManifestPath), "No rooted cooked roster manifest was found.");
    }

    public LocalContentResolution ResolveLegacy(CharacterClass selector)
    {
        if (selector == CharacterClass.None || selector == CharacterClass.FightGuy ||
            (selector != CharacterClass.Kistu && selector != CharacterClass.Nilus))
            return Failure("content.legacy.selector", selector.ToString(), "Selector is not a legacy compatibility character.");

        var rosterResolution = ResolveRoster();
        if (!rosterResolution.Success || rosterResolution.Roster == null)
            return rosterResolution;
        if (!rosterResolution.Roster.TryGetBySelector(selector, out var rosterEntry))
            return Failure("content.legacy.selector", selector.ToString(), "Selector is not available in the rooted roster.");

        var adapter = new LegacyCharacterCatalogAdapter();
        if (!adapter.TrySnapshot(selector, out var legacyEntry, out var diagnostics))
            return Failure(diagnostics);

        return Success(
            rosterResolution.RootPath,
            rosterResolution.ManifestPath,
            rosterResolution.Roster,
            rosterEntry.Requirement,
            legacyEntry);
    }

    public LocalContentResolution ResolveCookedPackage(string packageId)
    {
        if (!MatchContentCatalogBuilder.IsStablePackageId(packageId))
            return Failure("content.package.id-invalid", packageId ?? "packageId", "Package ID must be a stable lowercase identifier.");

        foreach (string root in _contentRoots)
        {
            string packageRoot = Path.Combine(root, packageId);
            string manifestPath = Path.Combine(packageRoot, CharacterPackageAssembler.ManifestPath);
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                var requirement = ReadRequirement(manifestPath);
                if (!string.Equals(requirement.PackageId, packageId, StringComparison.Ordinal))
                    return Failure("content.package.identity-mismatch", manifestPath, "Cooked manifest package ID does not match the requested package ID.");
                return Success(root, manifestPath, null, requirement);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidDataException || ex is FormatException)
            {
                return Failure("content.package.manifest-malformed", manifestPath, ex.Message);
            }
        }

        return Failure("content.package.missing", packageId, $"No rooted cooked package was found for '{packageId}'.");
    }

    private static MatchContentPackageRequirement ReadRequirement(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Cooked manifest must be an object.");

        string packageId = RequiredString(root, "packageId");
        string version = RequiredString(root, "version");
        string cookedContentHash = RequiredString(root, "cookedContentHash");
        string packageHash = RequiredString(root, "packageHash");
        return new MatchContentPackageRequirement(packageId, version, cookedContentHash, packageHash);
    }

    private static string RequiredString(JsonElement root, string property)
        => root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new InvalidDataException($"Manifest property '{property}' is null.")
            : throw new InvalidDataException($"Manifest property '{property}' must be a string.");

    private static LocalContentResolution Success(
        string rootPath,
        string manifestPath,
        BuiltInRosterManifest roster,
        MatchContentPackageRequirement requirement,
        MatchContentEntry? legacyEntry = null)
        => new(true, rootPath, manifestPath, roster, requirement, legacyEntry, Array.Empty<CharacterDiagnostic>());

    private static LocalContentResolution Failure(string code, string path, string message)
        => Failure(new[] { new CharacterDiagnostic(CharacterDiagnosticSeverity.Error, code, path, message) });

    private static LocalContentResolution Failure(IReadOnlyList<CharacterDiagnostic> diagnostics)
        => new(false, "", "", null, null, null, diagnostics);
}

public sealed class LocalContentResolution
{
    public LocalContentResolution(
        bool success,
        string rootPath,
        string manifestPath,
        BuiltInRosterManifest roster,
        MatchContentPackageRequirement requirement,
        MatchContentEntry? legacyEntry,
        IReadOnlyList<CharacterDiagnostic> diagnostics)
    {
        Success = success;
        RootPath = rootPath ?? "";
        ManifestPath = manifestPath ?? "";
        Roster = roster;
        Requirement = requirement;
        LegacyEntry = legacyEntry;
        Diagnostics = new ReadOnlyCollection<CharacterDiagnostic>(
            new List<CharacterDiagnostic>(diagnostics ?? Array.Empty<CharacterDiagnostic>()));
    }

    public bool Success { get; }
    public string RootPath { get; }
    public string ManifestPath { get; }
    public BuiltInRosterManifest Roster { get; }
    public MatchContentPackageRequirement Requirement { get; }
    public MatchContentEntry? LegacyEntry { get; }
    public IReadOnlyList<CharacterDiagnostic> Diagnostics { get; }
}

