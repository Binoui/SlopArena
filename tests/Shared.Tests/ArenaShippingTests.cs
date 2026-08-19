using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Guards the baked-arena pipeline (issue #77): every shipped .arena file must
/// parse and carry real ground collision, and the file-driven ArenaRegistry
/// must serve them (no hardcoded fallback — Simulation grounded entities at
/// KillHeight + 1 when a stage had no baked heightmap). These tests fail at
/// bake/commit time instead of at the player's feet.
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

    public static IEnumerable<object[]> ArenaFiles()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "data", "arenas"), "*.arena"))
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
    public void Registry_AfterLoadFromDirectory_ServesBakedArenas()
    {
        ArenaRegistry.LoadFromDirectory(Path.Combine(RepoRoot(), "data", "arenas"));
        Assert.Null(ArenaRegistry.Get("no-such-arena-xyz"));
        Assert.True(ArenaRegistry.Get("slop_court").HasValue);
        Assert.True(ArenaRegistry.Get("splash_deck").HasValue);
        Assert.True(ArenaRegistry.Get("after_hours").HasValue);
        Assert.True(ArenaRegistry.Get("rec_center_roof").HasValue);
        Assert.True(ArenaRegistry.Get("picnic_panic").HasValue);
        Assert.True(ArenaRegistry.Get("training").HasValue);
        Assert.Null(ArenaRegistry.Get("square"));
        Assert.Null(ArenaRegistry.Get("steps"));
        Assert.Null(ArenaRegistry.Get("colosseum"));
    }

    [Theory]
    [MemberData(nameof(ArenaFiles))]
    public void ShippedArena_ResolvesSideAndTopBlastLines(string fileName)
    {
        // Every shipped stage must have real side/top kill lines (issue: side/top blast
        // zones were missing — only void death). Baked arenas carry meaningful bounds,
        // so the auto-derivation must produce finite lines strictly beyond the mesh.
        string path = Path.Combine(RepoRoot(), "data", "arenas", fileName);
        var arenaOpt = ArenaBinaryFormat.LoadFromFile(path);
        Assert.True(arenaOpt.HasValue, $"failed to parse {fileName}");
        ArenaDefinition arena = arenaOpt.Value;

        var lines = ArenaCollision.ResolveBlastLines(in arena);

        Assert.True(lines.KillTop < float.PositiveInfinity,
            $"{fileName}: top blast line is inactive");
        Assert.True(lines.KillTop > arena.Heightmap.Data.Max(),
            $"{fileName}: top line {lines.KillTop} not above highest surface {arena.Heightmap.Data.Max()}");
        Assert.True(lines.KillMinX < arena.MinX,
            $"{fileName}: min X line {lines.KillMinX} not beyond mesh {arena.MinX}");
        Assert.True(lines.KillMaxX > arena.MaxX,
            $"{fileName}: max X line {lines.KillMaxX} not beyond mesh {arena.MaxX}");
        Assert.True(lines.KillMinZ < arena.MinZ,
            $"{fileName}: min Z line {lines.KillMinZ} not beyond mesh {arena.MinZ}");
        Assert.True(lines.KillMaxZ > arena.MaxZ,
            $"{fileName}: max Z line {lines.KillMaxZ} not beyond mesh {arena.MaxZ}");
    }
}
