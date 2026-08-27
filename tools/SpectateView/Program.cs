using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using SlopArena.Shared;
using SlopArena.Shared.AI;

namespace SlopArena.SpectateView;

/// <summary>
/// Headless bot-match spectator: runs the real ServerSimulation (same loop as
/// SelfPlayMatch — deterministic bots, seeded RNG) and renders a top-down XZ view
/// in the terminal with ANSI colors. No Unity, no network.
///
/// Usage: dotnet run --project tools/SpectateView -- [--char fightguy|kistu|manki|nilus]
///        [--seed N] [--speed N] [--stocks N] [--max-ticks N]
///   --speed N   ticks per rendered frame (1 = realtime 60fps, 4 = 4x at 15fps). 0 = headless
///               (no render, just the final result — useful for determinism checks).
///   Ctrl+C quits early.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string charName = ParseArg(args, "--char") ?? "fightguy";
        int seed = ParseInt(args, "--seed", 20260817);
        int speed = ParseInt(args, "--speed", 1);
        int maxTicks = ParseInt(args, "--max-ticks", SelfPlayMatch.DefaultMaxTicks);
        int stocks = ParseInt(args, "--stocks", 3);
        string? jsonPath = ParseArg(args, "--json");

        CharacterClass cls = charName.ToLowerInvariant() switch
        {
            "fightguy" or "fg" => CharacterClass.FightGuy,
            "kistu" => CharacterClass.Kistu,
            "manki" => CharacterClass.Manki,
            "nilus" => CharacterClass.Nilus,
            _ => CharacterClass.FightGuy,
        };
        var entry = BuiltInContentResolver.Resolve(cls);
        var def = entry.Definition;
        if (def.Class == CharacterClass.None)
        {
            Console.Error.WriteLine($"Unknown character '{charName}'.");
            return 1;
        }

        var arena = BuildKillArena();
        var baked = LoadBakedData(entry);

        var rule = new StockMatchRule((byte)stocks);
        var sim = new ServerSimulation(arena, rule);
        float gpy = def.CapsuleHeight * 0.5f;
        RegisterBot(sim, def, SelfPlayMatch.EntityA, -12f, gpy, baked);
        RegisterBot(sim, def, SelfPlayMatch.EntityB, 12f, gpy, baked);

        var rng = new Random(seed);
        var memA = new BotMemory();
        var memB = new BotMemory();
        var policy = new HeuristicBotPolicy();
        var inputs = new Dictionary<ulong, InputState>();
        var recorder = new MatchRecorder();
        var frames = new List<FrameSnap>(Math.Min(maxTicks, 10800));

        bool headless = speed <= 0;
        if (!headless) Console.Write("\x1b[2J"); // clear once; frames redraw in place

        int tick = 0;
        for (; tick < maxTicks; tick++)
        {
            inputs[SelfPlayMatch.EntityA] = policy.Decide(
                sim.GetState(SelfPlayMatch.EntityA), sim.GetState(SelfPlayMatch.EntityB), def, rng, memA);
            inputs[SelfPlayMatch.EntityB] = policy.Decide(
                sim.GetState(SelfPlayMatch.EntityB), sim.GetState(SelfPlayMatch.EntityA), def, rng, memB);

            recorder.RecordPresses(sim, tick, inputs, def); // swings from pre-tick presses
            sim.Tick(inputs);
            recorder.RecordTick(sim, tick, inputs, def);     // hits + positions

            if (jsonPath != null)
                frames.Add(new FrameSnap(tick, new[]
                {
                    Snap(sim.GetState(SelfPlayMatch.EntityA)),
                    Snap(sim.GetState(SelfPlayMatch.EntityB)),
                }));

            if (!headless && tick % speed == 0)
                Render(sim, arena, tick, seed, def, speed, stocks, recorder);

            if (rule.Evaluate(sim.GetAllStates()).IsEnded) break;
        }

        var sA = sim.GetState(SelfPlayMatch.EntityA);
        var sB = sim.GetState(SelfPlayMatch.EntityB);
        var outcome = rule.Evaluate(sim.GetAllStates());
        string winner = outcome.IsSharedVictory ? "draw (shared)" :
            outcome.WinnerEntityId == SelfPlayMatch.EntityA ? "A" :
            outcome.WinnerEntityId == SelfPlayMatch.EntityB ? "B" : "draw (no winner)";

        if (!headless) Console.Write("\x1b[0m");
        Console.WriteLine();
        Console.WriteLine($"Match over after {tick + 1} ticks ({(tick + 1) / 60.0:0.0}s) — winner: {winner}");
        Console.WriteLine($"A: {sA.Deaths} deaths, {sA.DamagePercent}%  |  B: {sB.Deaths} deaths, {sB.DamagePercent}%");

        recorder.Finish(tick, seed, outcome);
        var swings = recorder.Record.Swings;
        var hits = recorder.Record.Hits;
        foreach (ulong id in new[] { SelfPlayMatch.EntityA, SelfPlayMatch.EntityB })
        {
            var perMove = swings.Where(s => s.Attacker == id)
                .GroupBy(s => (s.ActiveSlot, s.Air))
                .Select(g => (Label: MoveLabel(def, g.Key.Item1, g.Key.Item2),
                              Swings: g.Count(), Hits: g.Count(s => s.Connected)))
                .OrderByDescending(m => m.Swings);
            string dmg = hits.Where(h => h.Attacker == id).Sum(h => h.Damage).ToString("0");
            Console.WriteLine($"{(id == SelfPlayMatch.EntityA ? "A" : "B")} used: "
                + string.Join("  |  ", perMove.Select(m => $"{m.Label} {m.Swings}× ({m.Hits} hit)"))
                + $"   — dealt {dmg}% total");
        }

        if (jsonPath != null)
        {
            WriteJson(jsonPath, def, arena, seed, stocks, recorder, frames);
            Console.WriteLine($"Wrote match dump: {jsonPath}");
        }
        return 0;
    }

    // ── Rendering ───────────────────────────────────────────────────────────

    private static void Render(ServerSimulation sim, ArenaDefinition arena, int tick,
        int seed, CharacterDefinition def, int speed, int stocks, MatchRecorder recorder)
    {
        var a = sim.GetState(SelfPlayMatch.EntityA);
        var b = sim.GetState(SelfPlayMatch.EntityB);

        int cols = Math.Max(1, (int)MathF.Ceiling(arena.MaxX - arena.MinX)); // 1 char per meter X
        int rows = Math.Max(1, (int)MathF.Ceiling(arena.MaxZ - arena.MinZ)); // 1 char per meter Z

        // Overlay: (col,row) → (glyph, ansi color). Entities + facing arrows.
        var overlay = new Dictionary<(int, int), (char, string)>();
        PlaceEntity(overlay, a, SelfPlayMatch.EntityA, arena, "\x1b[96m");
        PlaceEntity(overlay, b, SelfPlayMatch.EntityB, arena, "\x1b[91m");

        var swings = recorder.Record.Swings;
        var sb = new StringBuilder();
        sb.Append("\x1b[H");
        sb.Append($"\x1b[97m tick {tick} ({(float)tick / 60f:0.0}s)  {def.DisplayName} vs {def.DisplayName}  seed {seed}  {speed}x ");
        sb.Append($" A stocks {Math.Max(0, stocks - a.Deaths)} dmg {a.DamagePercent}% y {a.PY:0.0} {a.State} ");
        sb.Append($"| B stocks {Math.Max(0, stocks - b.Deaths)} dmg {b.DamagePercent}% y {b.PY:0.0} {b.State}\x1b[0m\n");
        sb.Append($"\x1b[96m A moves: {MoveList(swings, SelfPlayMatch.EntityA, def)}\x1b[0m\n");
        sb.Append($"\x1b[91m B moves: {MoveList(swings, SelfPlayMatch.EntityB, def)}\x1b[0m\n");

        string lastColor = "";
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                char ch;
                string color;
                if (overlay.TryGetValue((col, row), out var cell))
                {
                    (ch, color) = cell;
                }
                else
                {
                    float wx = arena.MinX + col + 0.5f;
                    float wz = arena.MinZ + row + 0.5f;
                    ch = HasFloor(arena, wx, wz) ? '.' : ' ';
                    color = ch == '.' ? "\x1b[90m" : "\x1b[0m";
                }
                if (color != lastColor) { sb.Append(color); lastColor = color; }
                sb.Append(ch);
            }
            sb.Append("\x1b[0m\n");
            lastColor = "";
        }
        sb.Append("\x1b[0m");
        Console.Write(sb);
        Thread.Sleep(16); // ~60 rendered frames/s; speed > 1 → fast-forward
    }

    /// <summary>Draw the entity glyph + a facing arrow in the adjacent cell (if free).</summary>
    private static void PlaceEntity(Dictionary<(int, int), (char, string)> overlay,
        CharacterState s, ulong id, ArenaDefinition arena, string color)
    {
        int col = (int)MathF.Floor(s.PX - arena.MinX);
        int row = (int)MathF.Floor(s.PZ - arena.MinZ);
        if (col < 0 || row < 0 || col >= MathF.Ceiling(arena.MaxX - arena.MinX)
            || row >= MathF.Ceiling(arena.MaxZ - arena.MinZ)) return; // off-screen (blasted)

        char glyph = s.IsGrounded ? (id == SelfPlayMatch.EntityA ? 'A' : 'B')
                                  : (id == SelfPlayMatch.EntityA ? 'a' : 'b');
        overlay[(col, row)] = (glyph, color);

        // Facing: yaw 0 = +Z, forward = (sin(yaw), cos(yaw)) in (X, Z).
        float dx = MathF.Sin(s.FacingYaw), dz = MathF.Cos(s.FacingYaw);
        int ac = col + Math.Sign(dx), ar = row + Math.Sign(dz);
        if (ac >= 0 && ar >= 0 && !overlay.ContainsKey((ac, ar)))
            overlay[(ac, ar)] = (ArrowChar(dx, dz), color);
    }

    private static char ArrowChar(float dx, float dz)
    {
        if (dx > 0.7f) return '>';
        if (dx < -0.7f) return '<';
        if (dz > 0.7f) return 'v';
        if (dz < -0.7f) return '^';
        if (dx > 0) return dz > 0 ? '\\' : '/'; // SE / NE
        return dz > 0 ? '/' : '\\';             // SW / NW
    }

    /// <summary>"g1 Fist Jab" — g/a + slot number + ability name (SelfPlayReport convention).</summary>
    private static string MoveLabel(CharacterDefinition def, byte slot, bool air)
        => $"{(air ? "a" : "g")}{SlotOf(slot)} {def.GetSlotAbility(slot - 1, air)?.Name ?? "-"}";

    private static int SlotOf(byte activeSlot) => activeSlot switch
    {
        AbilitySlots.Slot1 => 1, AbilitySlots.Slot2 => 2, AbilitySlots.Slot3 => 3, AbilitySlots.Slot4 => 4,
        AbilitySlots.Slot5 => 5, _ => 0,
    };

    /// <summary>Top-4 moves by usage for one entity, e.g. "g1 Fist Jab ×8  a2 Uppercut ×3".</summary>
    private static string MoveList(List<SwingRecord> swings, ulong id, CharacterDefinition def)
    {
        var counts = new Dictionary<(byte, bool), int>();
        foreach (var sw in swings)
            if (sw.Attacker == id)
                counts[(sw.ActiveSlot, sw.Air)] = counts.GetValueOrDefault((sw.ActiveSlot, sw.Air)) + 1;
        return counts.Count == 0 ? "-"
            : string.Join("  ", counts.OrderByDescending(kv => kv.Value).Take(4)
                .Select(kv => $"{MoveLabel(def, kv.Key.Item1, kv.Key.Item2)} ×{kv.Value}"));
    }

    private static bool HasFloor(in ArenaDefinition arena, float wx, float wz)
    {
        var hm = arena.Heightmap;
        if (hm.Data == null || hm.Data.Length == 0) return false;
        int ix = (int)MathF.Floor((wx - hm.OriginX) / hm.CellSize);
        int iz = (int)MathF.Floor((wz - hm.OriginZ) / hm.CellSize);
        if (ix < 0 || iz < 0 || ix >= hm.Width || iz >= hm.Height) return false;
        return hm.Data[iz * hm.Width + ix] != float.MinValue;
    }

    // ── Match setup (mirrors SelfPlayMatch) ─────────────────────────────────

    /// <summary>Crossroads-style 60×60 flat proxy (top +20, sides ±40, bottom −10) so KOs
    /// actually end matches. Deterministic, self-contained (no stage data dependency).</summary>
    private static ArenaDefinition BuildKillArena()
    {
        const int w = 60, h = 60;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "kill-proxy",
            DisplayName = "Kill Proxy (Crossroads-style 60x60)",
            KillHeight = -10f,
            MinX = -30f, MaxX = 30f, MinZ = -30f, MaxZ = 30f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = -30f, OriginZ = -30f },
        };
    }

    private static void RegisterBot(ServerSimulation sim, CharacterDefinition def, ulong id,
        float x, float py, BakedAnimationData? baked)
    {
        var state = new CharacterState
        {
            EntityId = id,
            PX = x, PY = py, PZ = 0f,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1,
            FacingYaw = id == SelfPlayMatch.EntityA ? 0f : MathF.PI, // A faces +Z, B faces −Z
            MatchState = MatchState.Playing,
        };
        sim.RegisterEntity(id, def, state, baked);
        sim.SetRespawnPosition(id, x, py, 0f, state.FacingYaw);
    }

    private static BakedAnimationData? LoadBakedData(MatchContentEntry entry)
    {
        if (entry.CookedCharacterPackage != null)
            return entry.BakedAnimation;

        var def = entry.Definition;
        if (string.IsNullOrEmpty(def.BakedDataPath)) return null;
        string relative = def.BakedDataPath.Replace("res://", "");
        string path = Path.Combine("data", relative);
        if (!File.Exists(path)) return null;
        try { return BakedAnimationData.LoadFromBin(File.ReadAllBytes(path)); }
        catch { return null; }
    }

    // ── CLI helpers ─────────────────────────────────────────────────────────

    private static string? ParseArg(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int ParseInt(string[] args, string key, int fallback)
    {
        var s = ParseArg(args, key);
        return s != null && int.TryParse(s, out int v) ? v : fallback;
    }

    // ── Match dump (for the browser viewer) ─────────────────────────────────

    private sealed record EntSnap(float x, float y, float z, float fy, string st, ushort dmg, byte deaths, bool g, byte slot);
    private sealed record FrameSnap(int t, EntSnap[] e);
    private sealed record MoveDef(byte slot, bool air, string name, string label);
    private sealed record SwingOut(int t, int e, int m, bool hit);
    private sealed record HitOut(int t, int e, float dmg);

    private static EntSnap Snap(CharacterState s) => new(s.PX, s.PY, s.PZ, s.FacingYaw,
        s.State.ToString(), s.DamagePercent, s.Deaths, s.IsGrounded, s.AttackSlot);

    /// <summary>Lossless-enough dump for playback: per-tick snapshots + move usage + hits.</summary>
    private static void WriteJson(string path, CharacterDefinition def, ArenaDefinition arena,
        int seed, int stocks, MatchRecorder recorder, List<FrameSnap> frames)
    {
        var moveIdx = new Dictionary<(byte, bool), int>();
        var moves = new List<MoveDef>();
        var swingsOut = new List<SwingOut>(recorder.Record.Swings.Count);
        foreach (var sw in recorder.Record.Swings)
        {
            if (!moveIdx.TryGetValue((sw.ActiveSlot, sw.Air), out int i))
            {
                i = moves.Count;
                moveIdx[(sw.ActiveSlot, sw.Air)] = i;
                moves.Add(new MoveDef(sw.ActiveSlot, sw.Air,
                    def.GetSlotAbility(sw.ActiveSlot - 1, sw.Air)?.Name ?? "-",
                    MoveLabel(def, sw.ActiveSlot, sw.Air)));
            }
            swingsOut.Add(new SwingOut(sw.StartTick, sw.Attacker == SelfPlayMatch.EntityA ? 0 : 1, i, sw.Connected));
        }
        var hitsOut = recorder.Record.Hits
            .Select(h => new HitOut(h.Tick, h.Attacker == SelfPlayMatch.EntityA ? 0 : 1, h.Damage))
            .ToArray();
        var lines = ArenaCollision.ResolveBlastLines(arena);
        float floorY = arena.Heightmap.Data is { Length: > 0 } ? arena.Heightmap.Data.Max() : 0f;

        var json = new
        {
            charA = def.DisplayName, charB = def.DisplayName, seed, stocks,
            arena = new
            {
                arena.MinX, arena.MaxX, arena.MinZ, arena.MaxZ,
                KillHeight = lines.KillHeight, KillTop = lines.KillTop,
                KillMinX = lines.KillMinX, KillMaxX = lines.KillMaxX,
                KillMinZ = lines.KillMinZ, KillMaxZ = lines.KillMaxZ,
                FloorY = floorY,
            },
            moves, frames, swings = swingsOut, hits = hitsOut,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(json));
    }
}
