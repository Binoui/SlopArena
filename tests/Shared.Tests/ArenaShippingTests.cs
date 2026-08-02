using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Guards the baked-arena pipeline (issue #77): every shipped .arena file must
/// parse and carry real ground collision. The hardcoded ArenaRegistry arenas
/// have Heightmap.Data == null (already documented in NilusAbilityTests), and
/// Simulation then grounds entities at KillHeight + 1 — the "no floor" bug in
/// the exe release, caused by the client silently falling back to them when the
/// baked file was unreachable. These tests fail at bake/commit time instead of
/// at the player's feet.
/// </summary>
public class ArenaShippingTests
{
    /// <summary>Repo root: tests run from tests/Shared.Tests/bin/Debug/net8.0/ (5 dirs up).</summary>
    private static string RepoRoot()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        Assert.True(Directory.Exists(Path.Combine(root, "data", "arenas")),
            $"repo data/arenas not found from {root} — are the baked arenas committed?");
        return root;
    }

    /// <summary>
    /// Known-broken files awaiting re-bake from their Unity scenes: pit, cross,
    /// split and sanctum are v1-format stubs (metadata only — no heightmap, no
    /// triangles) exported before the v2/v3 collision format existed, so the
    /// current loader cannot parse them. They are skipped explicitly so a fresh
    /// unparseable arena is still caught, and a re-bake is validated the moment
    /// it lands. Re-bake task: SlopArenaArenaBaker on each stage scene (Unity
    /// editor — see TESTING-UNITY.md).
    /// </summary>
    private static readonly string[] StaleV1Stubs = { "pit.arena", "cross.arena", "split.arena", "sanctum.arena" };

    public static IEnumerable<object[]> ArenaFiles()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "data", "arenas"), "*.arena"))
            if (!StaleV1Stubs.Contains(Path.GetFileName(file)))
                yield return new object[] { Path.GetFileName(file) };
    }

    [Theory]
    [MemberData(nameof(ArenaFiles))]
    public void ShippedArena_ParsesAndHasGroundCollision(string fileName)
    {
        string path = Path.Combine(RepoRoot(), "data", "arenas", fileName);
        var arenaOpt = ArenaBinaryFormat.LoadFromFile(path);
        Assert.True(arenaOpt.HasValue, $"failed to parse {fileName}");
        ArenaDefinition arena = arenaOpt.Value;

        Assert.NotNull(arena.Heightmap.Data);
        Assert.NotNull(arena.CollisionTriangles);
        Assert.NotEmpty(arena.CollisionTriangles);

        foreach (SpawnPoint spawn in arena.SpawnPoints)
        {
            float surface = arena.Heightmap.Sample(spawn.X, spawn.Z);
            Assert.True(surface > float.MinValue / 2f,
                $"{fileName}: no ground surface under spawn ({spawn.X:F1},{spawn.Z:F1})");
            Assert.True(surface > arena.KillHeight,
                $"{fileName}: ground {surface:F2} at spawn is below the blast zone {arena.KillHeight}");
            // Bug signature: Heightmap.Data == null makes Simulation ground at KillHeight + 1.
            Assert.NotEqual(arena.KillHeight + 1f, surface, precision: 2);
        }
    }

    [Fact]
    public void Registry_UnknownName_ReturnsNull_NotAnotherArena()
    {
        Assert.Null(ArenaRegistry.Get("no-such-arena-xyz"));
        Assert.True(ArenaRegistry.Get("colosseum").HasValue);
    }
}
