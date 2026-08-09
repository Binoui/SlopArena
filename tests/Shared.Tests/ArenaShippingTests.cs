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
        Assert.True(ArenaRegistry.Get("colosseum").HasValue);
        Assert.True(ArenaRegistry.Get("training").HasValue);
    }
}
