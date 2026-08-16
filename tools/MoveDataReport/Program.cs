using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SlopArena.Shared;

namespace SlopArena.MoveDataReport;

/// <summary>
/// Move data report: runs the real shared sim (ADR-0019 knockback + flight law)
/// for any character's normal hitboxes at several victim damage percents, and prints
/// authored frame data + simulated trajectories + a derived true-combo matrix.
/// Also emits a lossless JSON report and a self-contained HTML visual report
/// (frame-adv heatmap, knockback-shape gallery, KO/blast-clearance).
///
/// Usage: dotnet run --project tools/MoveDataReport -- [character] [--pcts 0,30,60] [--out path]
///        [--json report.json] [--html report.html] [--combos]
/// Character: fightguy (default) | manki | kistu | nilus.
/// Default markdown output: docs/generated/{character}-move-data.md.
/// Kill % / blast clearance: on a Crossroads-style 60x60 proxy (top +20, sides ±40, bottom -10).
/// </summary>
internal static class Program
{
    private const int MaxTicks = 2400;

    private readonly record struct SlotRef(bool Air, int Slot);
    private sealed record Route(string Label, SlotRef From, int FromHit, SlotRef To, int ToHit);
    private sealed record HitSpec(SlotRef Slot, AbilitySpec Ability, AttackStage Stage, HitboxEvent Hit, int HitIndex,
        sbyte LaunchAngle, float BaseKb, float GrowthKb, bool Adaptive);
    private sealed record RunResult(float Kv, ushort Hitstop, ushort Stun, float Rise, float Drift,
        float Apex, int ActionableTick, int? LandedTick);

    // ── JSON / HTML report model ────────────────────────────────────────────
    private sealed record TrajPoint(int Tick, float Height, float Travel, float Vy, float Vx, char Phase);
    private sealed record TrajectoryData(int Pct, float Kv, ushort Hitstop, ushort Stun, int Adv, int? AdvL,
        float Rise, float Drift, float Apex, float MaxTravel, int ActionableTick, int? LandedTick, string? KillLine, TrajPoint[] Points);
    private sealed record FrameDataData(int Trigger, int ActiveStart, int ActiveEnd, float Damage, int Angle,
        float BaseKb, float Growth, ushort Stun, int Iasa, int LandingLag, int AcBefore, int AcAfter, int Duration,
        string Profile, bool Adaptive);
    private sealed record ClearanceData(float TopFrac, float SideFrac, string Nearest);
    private sealed record MoveData(string Label, string Slot, int HitIndex, string Ability, FrameDataData Frame,
        int? KillPct, string? KillLine, ClearanceData Clearance, TrajectoryData[] Trajectories);
    private sealed record ArenaData(float? KillHeight, float? KillTop, float? KillMinX, float? KillMaxX,
        float? KillMinZ, float? KillMaxZ, string Note);
    private sealed record ReportData(string Character, string GeneratedAt, int[] Percents,
        ArenaData ReportArena, ArenaData KillArena, MoveData[] Moves);

    private static readonly SlotRef G1 = new(false, 1), G2 = new(false, 2), G3 = new(false, 3), G4 = new(false, 4);
    private static readonly SlotRef A1 = new(true, 1), A2 = new(true, 2), A3 = new(true, 3), A4 = new(true, 4);

    /// <summary>Default combo routes — the designed FightGuy juggle/kill links.</summary>
    private static readonly Route[] DefaultRoutes =
    {
        new("g3 Uppercut -> a1 Double Punch",      G3, 0, A1, 0),
        new("g3 Uppercut -> a2 Floating Kick",     G3, 0, A2, 0),
        new("g3 Uppercut -> a3 High Kick",         G3, 0, A3, 0),
        new("g3 Uppercut -> g2 Roundhouse",        G3, 0, G2, 0),
        new("a1 Double Punch h1 -> h2",            A1, 0, A1, 1),
        new("a2 Floating Kick sweet -> weak",      A2, 0, A2, 1),
        new("g1 Low Kick -> g2 Roundhouse",        G1, 0, G2, 0),
        new("g4 Tornado -> a1 Double Punch",       G4, 0, A1, 0),
        new("a2 Floating Kick -> land -> g1 jab",  A2, 1, G1, 0),
    };

    /// <summary>Combo routes per character — authored designed juggle/kill links. Non-FightGuy
    /// characters default to empty until their links are designed; the combo sections
    /// auto-omit when a character has no routes.</summary>
    private static Route[] RoutesFor(string which) => which.ToLowerInvariant() switch
    {
        "fightguy" => DefaultRoutes,
        _ => Array.Empty<Route>(),
    };

    private static CharacterDefinition ResolveCharacter(string which)
    {
        return which.ToLowerInvariant() switch
        {
            "fightguy" => CharacterRegistry.Get(CharacterClass.FightGuy),
            "manki"    => CharacterRegistry.Get(CharacterClass.Manki),
            "kistu"    => CharacterRegistry.Get(CharacterClass.Kistu),
            "nilus"    => CharacterRegistry.Get(CharacterClass.Nilus),
            var c => throw new ArgumentException(
                $"unknown character: {c} (expected one of: fightguy, manki, kistu, nilus)"),
        };
    }

    private static int Main(string[] args)
    {
        string which = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "fightguy";
        int? trajSlot = ParseTraj(args);
        int[] pcts = ParsePcts(args) ?? (trajSlot.HasValue
            ? new[] { 0, 10, 20, 30, 40, 50, 60 }   // --traj: 10% steps, low-% range
            : new[] { 0, 30, 60, 90, 120, 150 });
        string? outPath = ParseOut(args) ?? $"docs/generated/{which}-move-data.md";

        var def = ResolveCharacter(which);
        var routes = RoutesFor(which);

        var hits = CollectHits(def);

        // --json / --html: lossless structured report + self-contained visual report.
        // Both come from one richer collection (per-tick arcs + KO analysis); markdown path is untouched.
        string? jsonPath = ParseArg(args, "--json");
        string? htmlPath = ParseArg(args, "--html");
        if (jsonPath != null || htmlPath != null)
        {
            var report = BuildReport(def, hits, pcts);
            if (jsonPath != null)
            {
                var dir = Path.GetDirectoryName(jsonPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(jsonPath, ToJson(report));
                Console.Error.WriteLine($"wrote {jsonPath}");
            }
            if (htmlPath != null)
            {
                var dir = Path.GetDirectoryName(htmlPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(htmlPath, ToHtml(report));
                Console.Error.WriteLine($"wrote {htmlPath}");
            }
            return 0;
        }

        // --dll <path>: loads the given SlopArena.Shared.dll file IN ISOLATION (own load
        // context) and runs its Simulation.ApplyKnockback for the g2 formula. Verifies
        // that the DLL file on disk is the build we think it is — the editor can run a
        // stale in-memory copy while the file is fresh.
        string? dllPath = ParseArg(args, "--dll");
        if (dllPath != null)
        {
            ProbeDll(dllPath);
            return 0;
        }

        // --pipe: full-pipeline launch diagnostic — the victim is hit by the REAL hitbox
        // (inputs + baked bones → ResolveHits → queued launch), then the applied KV and
        // travel to landing are printed. Compares the in-game path to the direct
        // ApplyKnockback trajectory rows.
        if (args.Contains("--pipe"))
        {
            PipeLaunch(def);
            return 0;
        }

        // --shape [step]: the knockback feel surface. For EVERY hitbox (normals + aerials)
        // at each %, prints the real sampled arc — height above ground, horizontal travel,
        // vertical/horizontal velocity, and phase (H=hitstun, F=flight, A=apex, G=landed) —
        // sampled every `step` ticks (default 12 ≈ 0.2s) so the shape reads without per-tick
        // noise. This is the report's combo-free view of how far, how high, and at what angle
        // each hit launches at a given %. Also prints a one-line summary (KV, launch angle,
        // stun in seconds) per block.
        string? shapeStepArg = ParseArg(args, "--shape");
        if (shapeStepArg != null)
        {
            int step = int.TryParse(shapeStepArg, out int s) && s > 0 ? s : 12;
            DumpShapes(def, hits, pcts, step);
            return 0;
        }

        // --traj: raw per-tick launch trace only (no report/doc). Verifies the in-game
        // feel against the sim numbers without the summary tables.
        if (trajSlot.HasValue)
        {
            var h = hits.First(x => x.Slot.Air == false && x.Slot.Slot == trajSlot.Value && x.HitIndex == 0);
            DumpTrajectory(def, h, pcts);
            return 0;
        }

        var runs = new Dictionary<(SlotRef, int, int), RunResult>();
        foreach (var h in hits)
        foreach (var p in pcts)
            runs[(h.Slot, h.HitIndex, p)] = RunTrajectory(def, h, p);

        // Pipeline parity: the game launches through ResolveHits → freeze queue, not a
        // direct ApplyKnockback call. Run BOTH paths and compare — a drift here means the
        // game behaves differently from this report (the 2026-08-14 x87 float bug was
        // exactly this divergence, invisible to .NET: every hit took the unscaled force path).
        BakedAnimationData? baked = null;
        string bakedPath = def.BakedDataPath.Replace("res://", "");
        if (File.Exists(bakedPath)) baked = BakedAnimationData.LoadFromBin(File.ReadAllBytes(bakedPath));
        else Console.Error.WriteLine($"warn: {bakedPath} not found — probes run with un-baked hitbox resolution");
        var parity = ComputeParity(def, hits, runs, baked);
        foreach (var p in parity)
            if (!p.Ok)
                Console.Error.WriteLine($"PARITY DIVERGENCE: {p.Label}@{p.Pct}% direct KV {p.DirectKv:F2} vs pipeline {p.PipeKv:F2}");

        if (args.Contains("--parity"))
        {
            PrintParity(parity);
            return 0;
        }
        // Combo matrix + movement probes are opt-in (--combos). They encode designed
        // route *ideas*, not a feel contract — default output is pure knockback data.
        bool combos = args.Contains("--combos");
        var probeRoutes = Array.Empty<Route>();
        var probes = new Dictionary<(SlotRef, SlotRef, int), (ProbeVerdict Verdict, int ConnectTick)>();
        if (combos)
        {
            probeRoutes = routes.Where(r => r.From != r.To).ToArray();
            var starterSpawns = new Dictionary<SlotRef, (float AtkY, float NpcY, float NpcZ)>();
            foreach (var r in probeRoutes)
            {
                if (starterSpawns.ContainsKey(r.From)) continue;
                var spawn = CalibrateStarter(def, r.From, baked);
                if (spawn == null)
                    Console.Error.WriteLine($"probe {r.Label}: no starter placement connects — route will report NO");
                else
                    starterSpawns[r.From] = spawn.Value;
            }
            foreach (var r in probeRoutes)
            foreach (var p in pcts)
            {
                if (!starterSpawns.TryGetValue(r.From, out var spawn))
                    probes[(r.From, r.To, p)] = (ProbeVerdict.None, -1);
                else
                    probes[(r.From, r.To, p)] = RunProbe(def, r, p, baked, spawn);
            }
        }

        string md = BuildMarkdown(def, hits, runs, pcts, routes, probeRoutes, probes, parity, which, combos);
        Console.WriteLine(md);
        if (outPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, md);
            Console.Error.WriteLine($"wrote {outPath}");
        }
        return 0;
    }

    // ── Data collection ──────────────────────────────────────────────────────

    private static List<HitSpec> CollectHits(CharacterDefinition def)
    {
        var hits = new List<HitSpec>();
        foreach (var (slot, air) in new[] { (G1, false), (G2, false), (G3, false), (G4, false),
                                            (A1, true), (A2, true), (A3, true), (A4, true) })
        {
            AbilitySpec ability = air
                ? slot.Slot switch
                {
                    1 => def.AirSlot1, 2 => def.AirSlot2, 3 => def.AirSlot3, _ => def.AirSlot4,
                }
                : slot.Slot switch
                {
                    1 => def.Slot1, 2 => def.Slot2, 3 => def.Slot3, _ => def.Slot4,
                };
            if (ability == null || ability.Stages.Length == 0) continue;
            var stage = ability.Stages[0];
            if (stage.HitboxEvents == null || stage.HitboxEvents.Length == 0)
            {
                Console.Error.WriteLine($"warn: {ability.Name} has no hitbox events — skipped");
                continue;
            }
            for (int i = 0; i < stage.HitboxEvents.Length; i++)
            {
                var hit = stage.HitboxEvents[i];
                // Every move is included regardless of knockback profile. Resolve the launch triple:
                // Custom/Adaptive carry their own base/growth (and Adaptive's authored angle is used as
                // a documented representative — see the report caveat); named profiles resolve from the
                // profile table.
                var kb = hit.Knockback;
                bool adaptive = kb.Profile == KnockbackProfile.Adaptive;
                sbyte angle; float baseKb, growthKb;
                if (adaptive || kb.Profile == KnockbackProfile.Custom)
                {
                    angle = kb.Angle; baseKb = kb.BaseKnockback; growthKb = kb.KnockbackGrowth;
                }
                else
                {
                    var r = kb.Resolve(); angle = r.angle; baseKb = r.baseKB; growthKb = r.growthKB;
                }
                hits.Add(new HitSpec(slot, ability, stage, hit, i, angle, baseKb, growthKb, adaptive));
            }
        }
        return hits;
    }

    /// <summary>
    /// Launch a fresh grounded victim with the hitbox's authored knockback, then step
    /// the real sim until landing. The % column is the victim's damage BEFORE the hit;
    /// the launch itself is computed at pct + damage — the game applies the damage
    /// first, so the queued launch sees the post-hit percent (verified by the pipeline
    /// parity section). No DI/SDI input; hitstop is reported but not simulated.
    /// </summary>
    private static RunResult RunTrajectory(CharacterDefinition def, HitSpec h, int pct)
    {
        var sim = new ServerSimulation(BuildArena());
        float groundY = def.CapsuleHeight * 0.5f;
        var state = new CharacterState
        {
            PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
            State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
        };
        Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
            h.BaseKb, h.GrowthKb,
            h.Hit.Damage, h.Hit.StunTicks, def.Weight);
        float kv = MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
        ushort stun = state.HitstunTicks;
        ushort hitstop = ServerSimulation.ComputeHitstopTicks(h.Hit.Damage, null);
        sim.RegisterEntity(1, def, state);

        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        float maxPy = state.PY;
        int actionable = -1;
        int? landed = null;
        float rise = 0f, drift = 0f;
        for (int t = 0; t < MaxTicks; t++)
        {
            sim.Tick(inputs);
            var s = sim.GetState(1);
            if (s.PY > maxPy) maxPy = s.PY;
            if (actionable < 0 && s.HitstunTicks == 0)
            {
                actionable = t + 1;
                rise = s.PY - groundY;
                drift = s.PZ;
            }
            if (actionable >= 0 && s.IsGrounded)
            {
                landed = t + 1;
                break;
            }
        }
        return new RunResult(kv, hitstop, stun, rise, drift, maxPy - groundY, actionable, landed);
    }

    /// <summary>
    /// Per-tick launch trace: the same real launch (ApplyKnockback + sim.Tick) as
    /// RunTrajectory, but prints every tick so the shape can be compared to in-game
    /// feel. CSV columns: tick, height above ground (m), horizontal travel (m, +Z),
    /// vertical velocity, horizontal velocity, phase flag (H=hitstun, F=flight,
    /// A=apex tick, G=landed).
    /// </summary>
    private static void DumpTrajectory(CharacterDefinition def, HitSpec h, int[] pcts)
    {
        foreach (var pct in pcts)
        {
            var sim = new ServerSimulation(BuildArena());
            float groundY = def.CapsuleHeight * 0.5f;
            var state = new CharacterState
            {
                PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
                State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)pct,
            };
            Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
                h.BaseKb, h.GrowthKb,
                h.Hit.Damage, h.Hit.StunTicks, def.Weight);
            sim.RegisterEntity(1, def, state);
            var inputs = new Dictionary<ulong, InputState> { [1] = default };

            Console.WriteLine($"\n=== {Label(h)} at {pct}% ===");
            Console.WriteLine("tick,height(m),travel(m),vY,vX,phase");
            float maxPy = state.PY;
            bool apexMarked = false;
            for (int t = 0; t < MaxTicks; t++)
            {
                sim.Tick(inputs);
                var s = sim.GetState(1);
                if (s.PY > maxPy) maxPy = s.PY;
                else if (!apexMarked && s.HitstunTicks == 0 && !s.IsGrounded)
                {
                    Console.WriteLine($"{t + 1},{s.PY - groundY:F2},{s.PZ:F2},{s.VY:F2},{s.VX:F2},A");
                    apexMarked = true;
                }
                char phase = s.IsGrounded ? 'G'
                    : s.HitstunTicks > 0 ? 'H'
                    : 'F';
                float vy = s.HitstunTicks > 0 ? s.KVY : s.VY;
                float vz = s.HitstunTicks > 0 ? s.KVZ : s.VZ;
                Console.WriteLine($"{t + 1},{s.PY - groundY:F2},{s.PZ:F2},{vy:F2},{vz:F2},{phase}");
                if (phase == 'G') break;
            }
        }
    }

    /// <summary>
    /// Knockback feel surface for every hitbox (normals + aerials) at each %: prints the
    /// real sampled arc — height above ground, horizontal travel, V/H velocity, phase
    /// (H=hitstun, F=flight, A=apex, G=landed) — sampled every `step` ticks (default 12 ≈
    /// 0.2s). Uses the post-hit % (pct + damage) so rows match the Per-hit trajectories.
    /// This is the combo-free view: how far, how high, and at what angle each hit sends.
    /// </summary>
    private static void DumpShapes(CharacterDefinition def, List<HitSpec> hits, int[] pcts, int step)
    {
        foreach (var h in hits)
        foreach (var pct in pcts)
        {
            var sim = new ServerSimulation(BuildArena());
            float groundY = def.CapsuleHeight * 0.5f;
            var state = new CharacterState
            {
                PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
                State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
            };
            Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
                h.BaseKb, h.GrowthKb,
                h.Hit.Damage, h.Hit.StunTicks, def.Weight);
            float kv = MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
            ushort stun = state.HitstunTicks;
            sim.RegisterEntity(1, def, state);
            var inputs = new Dictionary<ulong, InputState> { [1] = default };

            Console.WriteLine($"\n=== {Label(h)} hit {h.HitIndex + 1} at {pct}% ===");
            Console.WriteLine($"launch {kv:F2} m/s  stun {stun} ticks ({stun / 60f:F2}s)  hitstop {ServerSimulation.ComputeHitstopTicks(h.Hit.Damage, null)}");
            Console.WriteLine("tick,height(m),travel(m),vY,vX,phase");
            float maxPy = state.PY;
            bool apexMarked = false;
            int apexTick = -1;
            for (int t = 0; t < MaxTicks; t++)
            {
                sim.Tick(inputs);
                var s = sim.GetState(1);
                // Apex = the tick after height stops increasing (post-hitstun flight).
                if (!apexMarked && t > 0 && s.PY <= maxPy && !s.IsGrounded && s.HitstunTicks == 0)
                {
                    apexMarked = true;
                    apexTick = t + 1;
                }
                if (s.PY > maxPy) maxPy = s.PY;
                if (s.IsGrounded)
                {
                    Console.WriteLine($"{t + 1},{s.PY - groundY:F2},{s.PZ:F2},0.00,0.00,G");
                    break;
                }
                bool atApex = t + 1 == apexTick;
                // Print the apex tick and every `step`-th tick (plus tick 0-ish via the loop start).
                if (!atApex && t % step != 0) continue;
                char phase = s.HitstunTicks > 0 ? 'H' : atApex ? 'A' : 'F';
                float vy = s.HitstunTicks > 0 ? s.KVY : s.VY;
                float vz = s.HitstunTicks > 0 ? s.KVZ : s.VZ;
                Console.WriteLine($"{t + 1},{s.PY - groundY:F2},{s.PZ:F2},{vy:F2},{vz:F2},{phase}");
            }
        }
    }

    private sealed record ParityResult(string Label, int Pct, float DirectKv, float PipeKv,
        int DirectStun, int PipeStun, float DirectApex, float PipeApex, bool Ok);
    /// <summary>
    /// Runs the REAL launch path (input → hitbox → ResolveHits → freeze queue → queued
    /// launch) for each slot's first hitbox at 0% and the last requested %, and compares
    /// the applied launch against the direct-ApplyKnockback trajectory rows. The game
    /// must match the tool here; divergence means the report doesn't describe the game.
    /// </summary>
    private static List<ParityResult> ComputeParity(CharacterDefinition def, List<HitSpec> hits,
        Dictionary<(SlotRef, int, int), RunResult> runs, BakedAnimationData? baked)
    {
        var result = new List<ParityResult>();
        foreach (var h in hits.Where(x => x.HitIndex == 0 && !x.Adaptive))
        foreach (var pct in new[] { 0, pctsDefault[^1] })
        {
            var direct = runs[(h.Slot, 0, pct)];
            var spawn = CalibrateStarter(def, h.Slot, baked);
            if (spawn == null)
            {
                result.Add(new ParityResult(Label(h), pct, direct.Kv, float.NaN, direct.Stun, -1, direct.Apex, float.NaN, false));
                continue;
            }
            var pipe = RunPipelineLaunch(def, h.Slot, pct, baked, spawn.Value);
            if (pipe == null)
            {
                result.Add(new ParityResult(Label(h), pct, direct.Kv, float.NaN, direct.Stun, -1, direct.Apex, float.NaN, false));
                continue;
            }
            bool ok = MathF.Abs(pipe.Value.KvMag - direct.Kv) <= 0.01f * direct.Kv + 0.02f
                && pipe.Value.Hitstun == direct.Stun
                && MathF.Abs(pipe.Value.Apex - direct.Apex) <= 0.03f * direct.Apex + 0.05f;
            result.Add(new ParityResult(Label(h), pct, direct.Kv, pipe.Value.KvMag, direct.Stun, pipe.Value.Hitstun,
                direct.Apex, pipe.Value.Apex, ok));
        }
        return result;
    }

    private static void PrintParity(List<ParityResult> parity)
    {
        Console.WriteLine("pipeline parity (real hit path vs direct formula):");
        foreach (var p in parity)
            Console.WriteLine($"{(p.Ok ? "OK " : "DIVERGE")} {p.Label,-22} {p.Pct,4}%  direct KV {p.DirectKv,6:F2}  pipe KV {p.PipeKv,6:F2}  " +
                $"stun {p.DirectStun,2}/{p.PipeStun,2}  apex {p.DirectApex,5:F1}/{p.PipeApex,5:F1}");
    }

    /// <summary>
    /// Full-pipeline launch: the slot's starter connects through the real hitbox path
    /// (inputs + baked bones → ResolveHits → hitstop queue → queued launch). Returns the
    /// applied KV magnitude, hitstun, hitstop and apex, or null when the starter whiffs.
    /// </summary>
    private static (float KvMag, int Hitstun, int Hitstop, float Apex)? RunPipelineLaunch(
        CharacterDefinition def, SlotRef slot, int pct, BakedAnimationData? baked, (float AtkY, float NpcY, float NpcZ) spawn)
    {
        var sim = new ServerSimulation(BuildArena());
        bool air = slot.Air;
        var attacker = new CharacterState
        {
            PX = 0f, PY = spawn.AtkY, PZ = 0f, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = 0f, JumpsLeft = (byte)(air ? 1 : 2),
        };
        var victim = new CharacterState
        {
            PX = 0f, PY = spawn.NpcY, PZ = spawn.NpcZ, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = MathF.PI, DamagePercent = (ushort)pct,
        };
        sim.RegisterEntity(1, def, attacker, baked);
        sim.RegisterEntity(100, def, victim, baked);
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = SlotByte(slot.Slot) }, [100] = default };
        bool hit = false, launched = false;
        float kvMag = 0f, apex = victim.PY, launchPy = victim.PY;
        int hitstun = 0, hitstop = 0;
        for (int t = 0; t < 400; t++)
        {
            sim.Tick(inputs);
            var v = sim.GetState(100);
            if (!hit && v.DamagePercent > pct) hit = true;
            if (hit && !launched && v.HitstunTicks > 0)
            {
                launched = true;
                kvMag = MathF.Sqrt(v.KVX * v.KVX + v.KVY * v.KVY + v.KVZ * v.KVZ);
                hitstun = v.HitstunTicks;
                hitstop = v.HitstopTicks;
                launchPy = v.PY;
            }
            if (v.PY > apex) apex = v.PY;
            if (launched && v.HitstunTicks == 0 && v.HitstopTicks == 0 && v.IsGrounded)
                return (kvMag, hitstun, hitstop, apex - launchPy); // apex GAIN — comparable to the ground-launch rows
        }
        return null;
    }

    private static readonly int[] pctsDefault = { 0, 30, 60, 90, 120, 150 };

    private static string? ParseArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>
    /// Loads a SlopArena.Shared.dll file in a dedicated context and reflectively invokes
    /// Simulation.ApplyKnockback with the g2 parameters (base 14, growth 26, dmg 8,
    /// angle 28, stun 20, weight 100, 0%). Prints the KV + hitstun the FILE produces —
    /// independent of whatever the Unity editor has loaded.
    /// </summary>
    private static void ProbeDll(string dllPath)
    {
        var alc = new System.Runtime.Loader.AssemblyLoadContext("probe", isCollectible: true);
        try
        {
            var asm = alc.LoadFromAssemblyPath(Path.GetFullPath(dllPath));
            var simType = asm.GetType("SlopArena.Shared.Simulation")!;
            Console.WriteLine($"file={Path.GetFullPath(dllPath)}");
            Console.WriteLine($"MVID={asm.ManifestModule.ModuleVersionId}");
            var scaleField = simType.GetField("KbScaleFactor");
            var gField = simType.GetField("FlightGravity");
            Console.WriteLine($"KbScaleFactor={(scaleField?.GetValue(null)?.ToString() ?? "MISSING")} " +
                $"FlightGravity={(gField?.GetValue(null)?.ToString() ?? "MISSING")}");
            var stateType = asm.GetType("SlopArena.Shared.CharacterState")!;
            var state = Activator.CreateInstance(stateType)!;
            Set(stateType, state, "PX", 0f); Set(stateType, state, "PY", 0.85f); Set(stateType, state, "PZ", 0f);
            Set(stateType, state, "IsGrounded", true); Set(stateType, state, "DamagePercent", (ushort)0);
            var apply = simType.GetMethods()
                .FirstOrDefault(m => m.Name == "ApplyKnockback" && m.GetParameters().Length == 10);
            if (apply == null) { Console.WriteLine("ERR: ApplyKnockback(10 params) not found"); return; }
            Console.WriteLine($"sig={string.Join(", ", apply.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}");
            var args = new object[] { state, 0f, 1f, (sbyte)28, 14f, 26f, 8f, (ushort)20, 100f, true };
            apply.Invoke(null, args);
            state = (object)args[0];
            float kvx = (float)Get(stateType, state, "KVX")!;
            float kvy = (float)Get(stateType, state, "KVY")!;
            float kvz = (float)Get(stateType, state, "KVZ")!;
            int hstun = Convert.ToInt32(Get(stateType, state, "HitstunTicks"));
            Console.WriteLine($"KV=({kvx:F2},{kvy:F2},{kvz:F2}) mag={MathF.Sqrt(kvx * kvx + kvy * kvy + kvz * kvz):F2} hitstun={hstun}");
        }
        catch (Exception e) { Console.WriteLine($"ERR {e.GetType().Name}: {e.Message}"); }
        finally { alc.Unload(); }
    }

    private static void Set(Type t, object o, string name, object value)
        => t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.SetValue(o, value);
    private static object? Get(Type t, object o, string name)
        => t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.GetValue(o);

    private static int? ParseTraj(string[] args)
    {
        int i = Array.IndexOf(args, "--traj");
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int slot) ? slot : null;
    }

    /// <summary>Kit slot 1-4 → ActiveSlot byte (factory indices: key1=3, key2=7, key3=8, key4=9).</summary>
    private static byte SlotByte(int slot) => (byte)(slot switch { 1 => 3, 2 => 7, 3 => 8, _ => 9 });

    /// <summary>
    /// Full-pipeline launch: the g2 starter connects through the REAL hitbox path
    /// (inputs + baked bones → ResolveHits → hitstop queue → queued launch). Prints the
    /// victim's applied KV + hitstun + travel to landing — the number the GAME produces,
    /// vs the direct-ApplyKnockback trajectory rows.
    /// </summary>
    private static void PipeLaunch(CharacterDefinition def)
    {
        BakedAnimationData? baked = null;
        string bakedPath = def.BakedDataPath.Replace("res://", "");
        if (File.Exists(bakedPath)) baked = BakedAnimationData.LoadFromBin(File.ReadAllBytes(bakedPath));
        else Console.Error.WriteLine($"warn: {bakedPath} not found — un-baked hitbox resolution");
        float gpy = def.CapsuleHeight * 0.5f;
        foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f, 2.0f })
        {
            var sim = new ServerSimulation(BuildArena());
            var atk = new CharacterState
            {
                PX = 0f, PY = gpy, PZ = 0f, IsGrounded = true,
                State = ActionState.Idle, FacingYaw = 0f, JumpsLeft = 2, DamagePercent = 0,
            };
            var vic = new CharacterState
            {
                PX = 0f, PY = gpy, PZ = z, IsGrounded = true,
                State = ActionState.Idle, FacingYaw = MathF.PI, JumpsLeft = 2, DamagePercent = 0,
            };
            sim.RegisterEntity(1, def, atk, baked);
            sim.RegisterEntity(100, def, vic, baked);
            var inputs = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = SlotByte(2) }, [100] = default };
            bool hit = false, launched = false;
            for (int t = 0; t < 400; t++)
            {
                sim.Tick(inputs);
                var v = sim.GetState(100);
                if (!hit && v.DamagePercent > 0)
                {
                    hit = true;
                    Console.WriteLine($"z={z} HIT t={t} dmg={v.DamagePercent} hstun={v.HitstunTicks} hitstop={v.HitstopTicks}");
                }
                if (hit && !launched && v.HitstunTicks > 0)
                {
                    launched = true;
                    float kv = MathF.Sqrt(v.KVX * v.KVX + v.KVY * v.KVY + v.KVZ * v.KVZ);
                    Console.WriteLine($"z={z} LAUNCH t={t} hstun={v.HitstunTicks} " +
                        $"KV=({v.KVX:F2},{v.KVY:F2},{v.KVZ:F2}) mag={kv:F2}");
                }
                if (launched && v.HitstunTicks == 0 && v.HitstopTicks == 0 && v.IsGrounded)
                {
                    Console.WriteLine($"z={z} LANDED t={t} P=({v.PX:F2},{v.PY:F2},{v.PZ:F2})");
                    return;
                }
            }
            if (!hit) Console.WriteLine($"z={z} no connect");
        }
    }

    /// <summary>
    /// Find a starter placement where the move's hitbox actually connects (bone pose +
    /// radius reach varies per move and changes with retunes — don't trust stale test
    /// positions). Tries a small grid, returns the first working (attackerY, victimY, victimZ).
    /// </summary>
    private static (float AtkY, float NpcY, float NpcZ)? CalibrateStarter(CharacterDefinition def, SlotRef from, BakedAnimationData? baked)
    {
        float gpy = def.CapsuleHeight * 0.5f;
        var candidates = new List<(float, float, float)>();
        if (!from.Air)
        {
            foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f, 2.0f })
                candidates.Add((gpy, gpy, z));
        }
        else
        {
            foreach (var (ay, ny) in new[] { (2f, 3f), (2.5f, 3.5f), (2f, 3.5f), (2.5f, 3f), (1.5f, 2.5f), (2f, 2.5f) })
            foreach (var z in new[] { 0.8f, 1.0f, 1.2f, 1.5f, 2.0f })
                candidates.Add((ay, ny, z));
        }
        foreach (var c in candidates)
            if (StarterConnects(def, SlotByte(from.Slot), c, baked, from.Air)) return c;
        return null;
    }

    private static bool StarterConnects(CharacterDefinition def, byte slotByte,
        (float AtkY, float NpcY, float NpcZ) p, BakedAnimationData? baked, bool air)
    {
        var sim = new ServerSimulation(BuildArena());
        var attacker = new CharacterState
        {
            PX = 0f, PY = p.AtkY, PZ = 0f, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = 0f, JumpsLeft = (byte)(air ? 1 : 2),
        };
        var victim = new CharacterState
        {
            PX = 0f, PY = p.NpcY, PZ = p.NpcZ, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = MathF.PI,
        };
        sim.RegisterEntity(1, def, attacker, baked);
        sim.RegisterEntity(100, def, victim, baked);
        var inputs = new Dictionary<ulong, InputState> { [1] = new() { ActiveSlot = slotByte }, [100] = default };
        for (int t = 0; t < 80; t++)
        {
            sim.Tick(inputs);
            if (sim.GetState(100).DamagePercent > 0) return true;
        }
        return false;
    }

    /// <summary>Probe verdict: Airborne = follow-up connected while the victim was in flight
    /// (a real juggle); Grounded = connected only after the victim landed (neutral hit on a
    /// passive target — opponent is fully actionable); None = never connected.</summary>
    private enum ProbeVerdict { None, Airborne, Grounded }

    /// <summary>
    /// Movement probe: the attacker performs the starter with real inputs (hit must
    /// connect through the actual hit resolution, bone hitboxes included), then plays
    /// a greedy chase policy — run toward the victim, jump when they're above, buffer
    /// the follow-up when in range. Victim is passive (no DI/SDI). Returns whether the
    /// follow-up connected while the victim was airborne (juggle) or only after landing.
    /// </summary>
    private static (ProbeVerdict Verdict, int ConnectTick) RunProbe(CharacterDefinition def, Route r, int pct, BakedAnimationData? baked,
        (float AtkY, float NpcY, float NpcZ) spawn)
    {
        var sim = new ServerSimulation(BuildArena());

        // Follow-up startup — the press is led by this many ticks (predictive timing).
        int toTriggerTicks = def.GetSlotAbility(SlotByte(r.To.Slot) - 1, r.To.Air)?.Stages[0]
            .HitboxEvents?[0].TriggerTick ?? 0;

        bool air = r.From.Air;
        var attacker = new CharacterState
        {
            PX = 0f, PY = spawn.AtkY, PZ = 0f, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = 0f, JumpsLeft = (byte)(air ? 1 : 2),
        };
        var victim = new CharacterState
        {
            PX = 0f, PY = spawn.NpcY, PZ = spawn.NpcZ, IsGrounded = !air,
            State = ActionState.Idle, FacingYaw = MathF.PI, DamagePercent = (ushort)pct,
        };
        sim.RegisterEntity(1, def, attacker, baked);
        sim.RegisterEntity(100, def, victim, baked);

        var inputs = new Dictionary<ulong, InputState>();
        int starterConnectedAt = -1;
        bool fired = false;
        int firedAt = -1;
        ushort damageAtFire = 0;

        for (int t = 0; t < MaxTicks; t++)
        {
            var a = sim.GetState(1);
            var v = sim.GetState(100);
            var input = new InputState();

            if (starterConnectedAt < 0)
            {
                if (t == 0) input.ActiveSlot = SlotByte(r.From.Slot);
            }
            else if (t > starterConnectedAt)
            {
                bool squatting = a.State == ActionState.JumpSquat;
                bool canAct = a.AnimLockTicks == 0 && a.HitstunTicks == 0 && a.HitstopTicks == 0
                    && a.LandingLagTicks == 0 && a.BurstRecoveryTicks == 0
                    && (a.State == ActionState.Idle || a.State == ActionState.Run);
                if (squatting)
                {
                    // hold through the squat for a full jump
                    input.Jump = true;
                    input.JumpHeld = true;
                }
                else if (canAct)
                {
                    float dx = v.PX - a.PX, dz = v.PZ - a.PZ;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    float dy = v.PY - a.PY;
                    // Aerials have short reach — get in close; ground moves lunge forward, so
                    // stop at kick distance and let the lunge close the gap.
                    float stopDist = r.To.Air ? 0.6f : 1.4f;
                    if (dist > stopDist) { input.MoveX = dx / dist; input.MoveY = dz / dist; }

                    // Stop at attack range, not point-blank: ground moves lunge forward, so a
                    // press at 0 m carries the hitbox PAST the target (whiff-lunge loop). A
                    // player stands at kick distance and lets the lunge close the gap.
                    float speed = MathF.Sqrt(a.VX * a.VX + a.VZ * a.VZ);

                    // Press with lead: predict where the victim will be at the follow-up's
                    // trigger tick (constant velocity) and fire when the predicted position
                    // is inside the real reach (~1.3m; bone pose + hitbox radius). The press
                    // repeats every tick in range — the sim gates it, so this is a retry.
                    int trig = toTriggerTicks;
                    float pdx = (v.PX + v.VX * trig / 60f) - a.PX;
                    float pdz = (v.PZ + v.VZ * trig / 60f) - a.PZ;
                    float pdy = (v.PY + v.VY * trig / 60f) - a.PY;
                    float pdist = MathF.Sqrt(pdx * pdx + pdz * pdz);
                    if (r.To.Air)
                    {
                        // jump only when the victim is close to falling into reach
                        if (a.IsGrounded && dy > 1.2f && dy < 8f && a.JumpsLeft > 0 && dist < 7f)
                        { input.Jump = true; input.JumpHeld = true; }
                        else if (!a.IsGrounded && dy > 0.2f && dy < 8f && a.JumpsLeft > 0 && a.VY < -1f)
                        { input.Jump = true; }
                        if (speed < 1f && pdist < 1.1f && pdy > -0.8f && pdy < 2.2f)
                        { input.ActiveSlot = SlotByte(r.To.Slot); if (!fired) { fired = true; firedAt = t; damageAtFire = v.DamagePercent; } }
                    }
                    else
                    {
                        // ground follow-up: stay grounded, hit them on the way down or after landing
                        if (speed < 1f && pdist > 0.9f && pdist < 1.8f && pdy > -0.6f && pdy < 1.8f)
                        { input.ActiveSlot = SlotByte(r.To.Slot); if (!fired) { fired = true; firedAt = t; damageAtFire = v.DamagePercent; } }
                    }
                }
            }

            inputs[1] = input;
            inputs[100] = default;
            sim.Tick(inputs);

            var v2 = sim.GetState(100);
            if (starterConnectedAt < 0 && v2.DamagePercent > pct) starterConnectedAt = t;
            if (fired && v2.DamagePercent > damageAtFire)
                return (v2.IsGrounded ? ProbeVerdict.Grounded : ProbeVerdict.Airborne, t);
            if (starterConnectedAt < 0 && t > 500) return (ProbeVerdict.None, -1);
        }
        return (ProbeVerdict.None, -1);
    }

    private static ArenaDefinition BuildArena()
    {
        const int w = 200, h = 200;
        var data = new float[w * h];
        return new ArenaDefinition
        {
            Name = "test",
            DisplayName = "Test Arena",
            KillHeight = -20f,
            SpawnPoints = new[] { new SpawnPoint { X = 0, Y = 0, Z = 0, Yaw = 0 } },
            Heightmap = new ArenaHeightmap
            {
                Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f,
            },
        };
    }

    // ── Report ───────────────────────────────────────────────────────────────

    private static string BuildMarkdown(CharacterDefinition def, List<HitSpec> hits,
        Dictionary<(SlotRef, int, int), RunResult> runs, int[] pcts,
        Route[] routes, Route[] probeRoutes, Dictionary<(SlotRef, SlotRef, int), (ProbeVerdict Verdict, int ConnectTick)> probes,
        List<ParityResult> parity, string which, bool combos)
    {
        var sb = new System.Text.StringBuilder();
        string charName = def.DisplayName;
        sb.AppendLine($"# {charName} move data report");
        sb.AppendLine();
        sb.AppendLine($"> Generated by `tools/MoveDataReport` ({DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC) — runs the real shared sim.");
        sb.AppendLine($"> Method: each trajectory row launches a fresh grounded victim at the given % with the hitbox's authored");
        sb.AppendLine("> knockback via `Simulation.ApplyKnockback`, then steps the sim until landing (cap 2400 ticks).");
        sb.AppendLine("> No DI/SDI input. Hitstop is reported (in-code `ComputeHitstopTicks`, ADR-0019: min(12, dmg/3 + 6))");
        sb.AppendLine("> but not simulated — flight starts at launch.");
        sb.AppendLine("> Flight law (current sim): KV holds constant through hitstun (single integration per tick); at stun expiry KV is copied");
        sb.AppendLine($"> to V and the victim enters post-hitstun flight (ADR-0019 §6): gravity {Simulation.FlightGravity:0} m/s2 + horizontal friction {Simulation.FlightFriction:0} until");
        sb.AppendLine("> landing or any action (jump/ability clears the regime). The AirTime float window no longer applies to launches.");
        sb.AppendLine("> Combo matrix uses the no-travel bound: `TA = (IASA - trigger) [+ landing lag] [+ jump squat] + follow-up trigger`;");
        sb.AppendLine("> `T` = TRUE combo (TA < stun at that %), `-` = not true. Travel and hitstop make reality harder than this bound.");
        sb.AppendLine();

        sb.AppendLine("## Frame data (authored)");
        sb.AppendLine();
        sb.AppendLine("| move | hit | trigger | active | dmg | angle | base | growth | stun gate | IASA | landlag | AC bef | AC aft | total |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var h in hits)
        {
            sb.AppendLine($"| {Label(h)}{(h.Adaptive ? " (adaptive)" : "")} | {h.HitIndex + 1} | {h.Hit.TriggerTick} | {h.Hit.TriggerTick}-{h.Hit.TriggerTick + h.Hit.DurationTicks - 1} | " +
                          $"{h.Hit.Damage} | {h.LaunchAngle} | {h.BaseKb} | {h.GrowthKb} | {h.Hit.StunTicks} | " +
                          $"{h.Stage.IasaTicks} | {h.Stage.LandingLagTicks} | {h.Stage.AutoCancelBeforeTicks} | {h.Stage.AutoCancelAfterTicks} | {h.Stage.DurationTicks} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Per-hit trajectories (simulated)");
        sb.AppendLine();
        sb.AppendLine("| move | hit | % | KV m/s | hitstop | stun | adv | advL | rise@stun m | drift@stun m | apex m | actionable tick | landed tick |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var h in hits)
        foreach (var p in pcts)
        {
            var r = runs[(h.Slot, h.HitIndex, p)];
            // Frame advantage on hit (ticks): victim stun minus the attacker's remaining
            // recovery after the hit connects on its first active frame. Hitstop freezes
            // both players so it cancels out. unlock = IASA early-out, or stage end when
            // no IASA is authored (full commitment).
            int unlock = h.Stage.IasaTicks > 0 ? h.Stage.IasaTicks : h.Stage.DurationTicks;
            int recovery = Math.Max(0, unlock - h.Hit.TriggerTick);
            int adv = r.Stun - recovery;
            // advL: landed follow-up after an aerial pays landing lag (unless the landing
            // frame sits in an AC window, or the attacker continues with an aerial at IASA).
            string advL = h.Slot.Air ? (adv - h.Stage.LandingLagTicks).ToString() : "—";
            sb.AppendLine($"| {Label(h)} | {h.HitIndex + 1} | {p} | {r.Kv:0.0} | {r.Hitstop} | {r.Stun} | {adv} | {advL} | " +
                          $"{r.Rise:0.00} | {r.Drift:0.00} | {r.Apex:0.0} | {r.ActionableTick} | {r.LandedTick?.ToString() ?? ">2400"} |");
        }
        sb.AppendLine();
        sb.AppendLine("> **adv** = on-hit frame advantage (ticks): `stun − (IASA − trigger)`; positive = the attacker acts before the");
        sb.AppendLine("> victim leaves hitstun (can press/continue), negative = the victim recovers first. Hitstop freezes both, so it");
        sb.AppendLine("> cancels out. Assumes the hit connects on the first active frame — later connects only improve advantage.");
        sb.AppendLine("> **advL** (aerials): the landed follow-up pays `LandingLagTicks` on top (SHFFL-style); land inside an AC window");
        sb.AppendLine("> (`≤ AC bef` / `≥ AC aft`) or chase with an aerial at IASA and the lag is skipped — the true landed number sits");
        sb.AppendLine("> between `advL` and `adv`. Grounded moves land free (no lag), so `advL` is `—`.");

        sb.AppendLine("## Pipeline parity (real hit path vs direct formula)");
        sb.AppendLine();
        sb.AppendLine("The game launches through ResolveHits → hitstop queue, the rows above through a direct");
        sb.AppendLine("`ApplyKnockback` call. This section runs the REAL path (input → hitbox → resolve → queued launch)");
        sb.AppendLine("for each slot's first hitbox and compares the applied launch to the direct rows. Any `DIVERGE`");
        sb.AppendLine("means the game behaves differently from this report — investigate before trusting feel tuning");
        sb.AppendLine("(the 2026-08-14 x87 float comparison bug showed exactly this: every hit took the unscaled");
        sb.AppendLine("force path while .NET tests were green).");
        sb.AppendLine();
        sb.AppendLine("| move | % | direct KV m/s | pipeline KV m/s | direct stun | pipeline stun | direct apex | pipeline apex | status |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var p in parity)
            sb.AppendLine($"| {p.Label} | {p.Pct} | {p.DirectKv:0.00} | {p.PipeKv:0.00} | {p.DirectStun} | {p.PipeStun} | " +
                          $"{p.DirectApex:0.0} | {p.PipeApex:0.0} | {(p.Ok ? "OK" : "**DIVERGE**")} |");
        sb.AppendLine();

        if (combos)
        {
            sb.AppendLine("## Combo matrix (no-travel bound)");
            sb.AppendLine();
            sb.AppendLine("T = follow-up connects before stun ends (attacker recovers at IASA, jumpsquat when follow-up is aerial,");
            sb.AppendLine("landing lag when the route lands between hits). Most routes SHOULD be `-` at low % and some at all % —");
            sb.AppendLine("reads and movement are what make combos happen (Melee-style), TRUE everywhere = flowchart bait.");
            sb.AppendLine();
            sb.AppendLine("| route (TA ticks) | " + string.Join(" | ", pcts.Select(p => p.ToString())) + " |");
            sb.AppendLine("|---" + string.Join("", pcts.Select(_ => "|---")) + "|");
            foreach (var r in routes)
            {
                int ta = AttackerBudget(def, hits, r);
                string cells = string.Join(" | ", pcts.Select(p =>
                {
                    var stun = runs[(r.From, r.FromHit, p)].Stun;
                    return ta < stun ? "**T**" : "-";
                }));
                sb.AppendLine($"| {r.Label} ({ta}) | {cells} |");
            }
            sb.AppendLine();
        }

        if (combos && probeRoutes.Length > 0)
        {
            sb.AppendLine("## Combo probes (movement chase, real sim)");
            sb.AppendLine();
        sb.AppendLine("T = frame-true (matrix above). C = the chase policy connected while the victim was airborne — a real");
        sb.AppendLine("juggle/read; the victim is likely actionable by then, so these are not true combos. L = connected only");
        sb.AppendLine("after the victim landed (neutral hit on a passive target — opponent fully actionable, so not a combo).");
        sb.AppendLine("- = no connect within 2400 ticks. Policy: run toward the victim, jump when they are above, press the");
        sb.AppendLine("follow-up with a trigger-tick lead when the predicted position is in reach; victim is passive (no DI/SDI).");
        sb.AppendLine("The starter is performed for real (bone hitboxes) with an auto-calibrated placement — a route can fail");
        sb.AppendLine("because the starter itself is hard to land.");
        sb.AppendLine();
        sb.AppendLine("| route | " + string.Join(" | ", pcts.Select(p => p.ToString())) + " |");
        sb.AppendLine("|---" + string.Join("", pcts.Select(_ => "|---")) + "|");
        foreach (var r in probeRoutes)
        {
            int ta = AttackerBudget(def, hits, r);
            string cells = string.Join(" | ", pcts.Select(p =>
            {
                var stun = runs[(r.From, r.FromHit, p)].Stun;
                if (ta < stun) return "**T**";
                var probe = probes[(r.From, r.To, p)];
                return probe.Verdict == ProbeVerdict.Airborne ? "**C**"
                    : probe.Verdict == ProbeVerdict.Grounded ? "L" : "-";
            }));
            sb.AppendLine($"| {r.Label} | {cells} |");
        }
        sb.AppendLine();
        sb.AppendLine("Connect ticks (starter hit -> follow-up damage tick; verdict letter per cell):");
        sb.AppendLine();
        sb.AppendLine("```");
        foreach (var r in probeRoutes)
        {
            var parts = pcts.Select(p =>
            {
                var probe = probes[(r.From, r.To, p)];
                string tag = probe.Verdict switch
                {
                    ProbeVerdict.Airborne => "C",
                    ProbeVerdict.Grounded => "L",
                    _ => "-",
                };
                return probe.Verdict == ProbeVerdict.None ? $"{p}%:-" : $"{p}%:{tag}@{probe.ConnectTick}";
            });
            sb.AppendLine($"{r.Label}: {string.Join("  ", parts)}");
        }
        sb.AppendLine("```");
        sb.AppendLine();
        }

        sb.AppendLine($"_Weight: {def.Weight}. Jump squat: {def.Movement.JumpSquatTicks} ticks. Dash startup: not authored (0) — revisit later._");
        return sb.ToString();
    }

    /// <summary>Attacker time from hit-1 connect to hit-2 connect, in state-machine ticks (shared hitstop cancels).</summary>
    private static int AttackerBudget(CharacterDefinition def, List<HitSpec> hits, Route r)
    {
        var from = hits.First(h => h.Slot == r.From && h.HitIndex == r.FromHit);
        var to = hits.First(h => h.Slot == r.To && h.HitIndex == r.ToHit);
        if (r.From == r.To)
            return to.Hit.TriggerTick - from.Hit.TriggerTick; // same-move chain: no recovery between hitboxes
        int recovery = Math.Max(0, from.Stage.IasaTicks - from.Hit.TriggerTick);
        int landLag = r.From.Air && !r.To.Air ? from.Stage.LandingLagTicks : 0;
        int jumpSquat = r.To.Air && !r.From.Air ? def.Movement.JumpSquatTicks : 0;
        return recovery + landLag + jumpSquat + to.Hit.TriggerTick;
    }

    private static string Label(HitSpec h)
        => $"{(h.Slot.Air ? "a" : "g")}{h.Slot.Slot} {h.Ability.Name}";

    // ── JSON / HTML report ──────────────────────────────────────────────────

    private static string SlotName(SlotRef s) => $"{(s.Air ? "a" : "g")}{s.Slot}";

    /// <summary>A spike (negative launch angle) sent from the ground lands instantly and shows no arc.
    /// The report launches spike victims from this altitude (off-stage / airborne scenario) so the
    /// downward send is visible. Kill-% analysis stays grounded (a grounded spike does not KO).</summary>
    private const float SpikeLaunchAltitude = 3f;

    /// <summary>Kill-% proxy arena: flat 60x60, KillHeight -10 (Crossroads-style). Sides auto-derive
    /// from bounds &plusmn;10 &rarr; &plusmn;40; top auto = floor + 20 = +20.</summary>
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
            Heightmap = new ArenaHeightmap { Data = data, Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginZ = 0f },
        };
    }

    /// <summary>Clone an arena with blast lines pushed out so the sim never respawns — lets the run
    /// detect a real blast-line crossing itself and keep recording the full arc.</summary>
    private static ArenaDefinition NoRespawn(ArenaDefinition a)
    {
        a.KillHeight = -1e6f; a.KillTop = 1e6f;
        a.KillMinX = -1e6f; a.KillMaxX = 1e6f; a.KillMinZ = -1e6f; a.KillMaxZ = 1e6f;
        return a;
    }

    private static string? BlastLine(CharacterState s, in ArenaCollision.BlastLines b)
    {
        if (s.PY < b.KillHeight) return "bottom";
        if (s.PY > b.KillTop) return "top";
        if (s.PX < b.KillMinX || s.PX > b.KillMaxX) return "side";
        if (s.PZ < b.KillMinZ || s.PZ > b.KillMaxZ) return "side";
        return null;
    }

    private static TrajPoint PointAt(CharacterState s, int tick, float groundY)
    {
        float vy = s.HitstunTicks > 0 ? s.KVY : s.VY;
        float vz = s.HitstunTicks > 0 ? s.KVZ : s.VZ;
        char phase = s.IsGrounded ? 'G' : s.HitstunTicks > 0 ? 'H' : 'F';
        return new TrajPoint(tick, s.PY - groundY, s.PZ, vy, vz, phase);
    }

    /// <summary>Launch a grounded victim with the hitbox's knockback, step the sim until it lands OR
    /// crosses a real blast line, recording the per-tick arc. Runs on a no-respawn arena so the flight
    /// is captured in full and the crossing is detected against <paramref name="detect"/>.</summary>
    private static TrajectoryData RunTrajectoryFull(CharacterDefinition def, HitSpec h, int pct,
        ArenaDefinition phys, in ArenaCollision.BlastLines detect)
    {
        var sim = new ServerSimulation(phys);
        float groundY = def.CapsuleHeight * 0.5f;
        bool spike = h.LaunchAngle < 0;
        float startY = spike ? groundY + SpikeLaunchAltitude : groundY;
        var state = new CharacterState
        {
            PX = 0f, PY = startY, PZ = 0f,
            IsGrounded = !spike,
            State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
        };
        Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
            h.BaseKb, h.GrowthKb,
            h.Hit.Damage, h.Hit.StunTicks, def.Weight);
        float kv = MathF.Sqrt(state.KVX * state.KVX + state.KVY * state.KVY + state.KVZ * state.KVZ);
        ushort stun = state.HitstunTicks;
        ushort hitstop = ServerSimulation.ComputeHitstopTicks(h.Hit.Damage, null);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };

        float maxPy = state.PY;
        float maxTravel = 0f;
        int actionable = -1;
        int? landed = null; string? killLine = null;
        float rise = 0f, drift = 0f;
        var pts = new List<TrajPoint>();
        for (int t = 0; t < MaxTicks; t++)
        {
            sim.Tick(inputs);
            var s = sim.GetState(1);
            killLine = BlastLine(s, detect);
            if (killLine != null) { pts.Add(PointAt(s, t + 1, groundY)); break; }
            if (s.PY > maxPy) maxPy = s.PY;
            if (s.PZ > maxTravel) maxTravel = s.PZ;
            if (actionable < 0 && s.HitstunTicks == 0) { actionable = t + 1; rise = s.PY - groundY; drift = s.PZ; }
            if (actionable >= 0 && s.IsGrounded) { pts.Add(PointAt(s, t + 1, groundY)); landed = t + 1; break; }
            pts.Add(PointAt(s, t + 1, groundY));
            if (pts.Count >= 1200) break; // safety cap
        }
        int unlock = h.Stage.IasaTicks > 0 ? h.Stage.IasaTicks : h.Stage.DurationTicks;
        int adv = stun - Math.Max(0, unlock - h.Hit.TriggerTick);
        int? advL = h.Slot.Air ? adv - h.Stage.LandingLagTicks : null;
        return new TrajectoryData(pct, kv, hitstop, stun, adv, advL, rise, drift,
            maxPy - groundY, maxTravel, actionable, landed, killLine, pts.ToArray());
    }

    private static (bool Killed, string Line) RunKillProbe(CharacterDefinition def, HitSpec h, int pct,
        ArenaDefinition phys, in ArenaCollision.BlastLines detect)
    {
        var sim = new ServerSimulation(phys);
        float groundY = def.CapsuleHeight * 0.5f;
        var state = new CharacterState
        {
            PX = 0f, PY = groundY, PZ = 0f, IsGrounded = true,
            State = ActionState.Idle, FacingYaw = 0f, DamagePercent = (ushort)(pct + (int)h.Hit.Damage),
        };
        Simulation.ApplyKnockback(ref state, 0f, 1f, h.LaunchAngle,
            h.BaseKb, h.GrowthKb,
            h.Hit.Damage, h.Hit.StunTicks, def.Weight);
        sim.RegisterEntity(1, def, state);
        var inputs = new Dictionary<ulong, InputState> { [1] = default };
        for (int t = 0; t < MaxTicks; t++)
        {
            sim.Tick(inputs);
            var s = sim.GetState(1);
            string? line = BlastLine(s, detect);
            if (line != null) return (true, line);
            if (s.IsGrounded) return (false, "");
        }
        return (false, "");
    }

    /// <summary>Lowest victim % (pre-hit) at which the launch crosses a blast line before landing.
    /// Binary search — knockback grows monotonically with damage. null = no KO by 250%.</summary>
    private static (int Pct, string Line)? KillPctFor(CharacterDefinition def, HitSpec h,
        ArenaDefinition phys, in ArenaCollision.BlastLines detect)
    {
        const int MAX = 250;
        if (!RunKillProbe(def, h, MAX, phys, detect).Killed) return null;
        int lo = 0, hi = MAX, found = MAX; string line = "";
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            var r = RunKillProbe(def, h, mid, phys, detect);
            if (r.Killed) { found = mid; line = r.Line; hi = mid - 1; }
            else lo = mid + 1;
        }
        return (found, line);
    }

    private static FrameDataData BuildFrameData(HitSpec h) => new(
        h.Hit.TriggerTick, h.Hit.TriggerTick, h.Hit.TriggerTick + h.Hit.DurationTicks - 1,
        h.Hit.Damage, h.LaunchAngle, h.BaseKb, h.GrowthKb,
        h.Hit.StunTicks, h.Stage.IasaTicks, h.Stage.LandingLagTicks, h.Stage.AutoCancelBeforeTicks,
        h.Stage.AutoCancelAfterTicks, h.Stage.DurationTicks,
        h.Hit.Knockback.Profile.ToString(), h.Adaptive);

    private static ReportData BuildReport(CharacterDefinition def, List<HitSpec> hits, int[] pcts)
    {
        var reportArena = BuildArena();
        var reportDetect = ArenaCollision.ResolveBlastLines(in reportArena);
        var reportPhys = NoRespawn(reportArena);

        var killArena = BuildKillArena();
        var killDetect = ArenaCollision.ResolveBlastLines(in killArena);
        var killPhys = NoRespawn(killArena);

        var moves = new List<MoveData>();
        foreach (var h in hits)
        {
            var trajs = new List<TrajectoryData>();
            foreach (var p in pcts)
                trajs.Add(RunTrajectoryFull(def, h, p, reportPhys, reportDetect));
            var kill = KillPctFor(def, h, killPhys, killDetect);
            moves.Add(new MoveData(Label(h), SlotName(h.Slot), h.HitIndex, h.Ability.Name,
                BuildFrameData(h), kill?.Pct, kill?.Line, ClearanceOf(def, trajs[^1], killDetect), trajs.ToArray()));
        }
        return new ReportData(def.DisplayName, DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            pcts, ToArenaData(reportDetect, "trajectory / landing arena (flat 200x200)"),
            ToArenaData(killDetect, "kill-% proxy (flat 60x60, Crossroads-style, -10)"), moves.ToArray());
    }

    /// <summary>How close the move gets to the top / side blast lines at the highest simulated % —
    /// fraction of the way there (0..1). Always populated even when it never kills, so tuning can
    /// see "up-slash apex is 47% of the way to top blast".</summary>
    private static ClearanceData ClearanceOf(CharacterDefinition def, TrajectoryData tr, in ArenaCollision.BlastLines kill)
    {
        float groundY = def.CapsuleHeight * 0.5f;
        float topFrac = kill.KillTop - groundY > 0.01f ? Math.Clamp(tr.Apex / (kill.KillTop - groundY), 0f, 1f) : 0f;
        float sideFrac = kill.KillMaxZ - kill.KillMinZ > 0.01f ? Math.Clamp(tr.MaxTravel / kill.KillMaxZ, 0f, 1f) : 0f;
        return new ClearanceData(topFrac, sideFrac, topFrac >= sideFrac ? "top" : "side");
    }

    private static ArenaData ToArenaData(in ArenaCollision.BlastLines b, string note)
    {
        static float? Inf(float v) => float.IsInfinity(v) ? null : v;
        return new ArenaData(Inf(b.KillHeight), Inf(b.KillTop), Inf(b.KillMinX), Inf(b.KillMaxX),
            Inf(b.KillMinZ), Inf(b.KillMaxZ), note);
    }

    private static string ToJson(ReportData r) => JsonSerializer.Serialize(r,
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    // ── HTML renderer (self-contained, no external deps) ────────────────────

    private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Fi(float v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    private static string Escape(string s) => System.Net.WebUtility.HtmlEncode(s);
    private static string Blast(float? v) => v == null ? "&infin;" : F(v.Value);
    private static string Bar(int pct) => $"<span class=\"barwrap\"><span class=\"barfill\" style=\"width:{pct}%\"></span></span>{pct}%";
    private static string MoveTag(MoveData m) => m.Frame.Adaptive ? " <span class=\"tag\">adaptive</span>" : "";

    private static string AdvColor(int adv)
    {
        int a = Math.Clamp(adv, -40, 40);
        int m = (int)(Math.Abs(a) * 5.5f);
        return a >= 0 ? $"rgb({255 - m},255,{255 - m})" : $"rgb(255,{255 - m},{255 - m})";
    }

    private static string ArcSvg(TrajectoryData tr, float maxTravel, float maxHeight)
    {
        const int W = 150, H = 110, PAD = 8;
        float tw = maxTravel > 0.01f ? maxTravel : 1f;
        float th = maxHeight > 0.01f ? maxHeight : 1f;
        float sx = (W - 2 * PAD) / tw, sy = (H - 2 * PAD) / th;
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W} {H}\" class=\"arc\">");
        sb.Append($"<line x1=\"{PAD}\" y1=\"{H - PAD}\" x2=\"{W - PAD}\" y2=\"{H - PAD}\" stroke=\"#888\" stroke-width=\"1\"/>");
        sb.Append("<polyline fill=\"none\" stroke=\"#1a5fd0\" stroke-width=\"1.6\" points=\"");
        foreach (var p in tr.Points)
            sb.Append($"{Fi(PAD + p.Travel * sx)},{Fi(H - PAD - p.Height * sy)} ");
        sb.Append("\"/>");
        if (tr.Points.Length > 0)
        {
            var apex = tr.Points.OrderByDescending(p => p.Height).First();
            sb.Append($"<circle cx=\"{Fi(PAD + apex.Travel * sx)}\" cy=\"{Fi(H - PAD - apex.Height * sy)}\" r=\"2.5\" fill=\"#d23\"/>");
        }
        if (tr.KillLine != null && tr.Points.Length > 0)
        {
            var last = tr.Points[tr.Points.Length - 1];
            sb.Append($"<line x1=\"{Fi(PAD + last.Travel * sx)}\" y1=\"{PAD}\" x2=\"{Fi(PAD + last.Travel * sx)}\" y2=\"{H - PAD}\" stroke=\"#d23\" stroke-width=\"1\" stroke-dasharray=\"2,2\"/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string ToHtml(ReportData r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{Escape(r.Character)} move data</title><style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;margin:24px;color:#1c1e21;}h1{font-size:22px;}h2{margin-top:32px;border-bottom:1px solid #ddd;padding-bottom:4px;}.meta{color:#666;font-size:13px;margin-bottom:16px;max-width:880px;line-height:1.4;}");
        sb.AppendLine("table.heat{border-collapse:collapse;font-size:13px;}table.heat td,table.heat th{border:1px solid #ccc;padding:3px 7px;text-align:center;}table.heat td.move,table.heat th.move{text-align:left;white-space:nowrap;}table.heat th{cursor:pointer;user-select:none;background:#f2f3f5;}table.heat td.kill{min-width:56px;}");
        sb.AppendLine(".legend{font-size:12px;color:#444;margin:6px 0 18px;max-width:880px;}.arcs{display:flex;flex-wrap:wrap;gap:12px;margin:6px 0 20px;}.move{margin-bottom:20px;}figure{margin:0;text-align:center;font-size:11px;color:#555;width:150px;}svg.arc{border:1px solid #eee;background:#fafbfc;display:block;}");
        sb.AppendLine(".killbadge{font-size:11px;padding:1px 6px;border-radius:9px;background:#fdecea;color:#c0392b;white-space:nowrap;}");
        sb.AppendLine(".barwrap{width:84px;background:#eee;border-radius:6px;height:10px;display:inline-block;vertical-align:middle;margin-right:5px;overflow:hidden;}.barfill{display:block;height:10px;border-radius:6px;background:#e74c3c;}td.clr{font-size:11px;color:#555;white-space:nowrap;}");
        sb.AppendLine(".tag{font-size:10px;color:#8a6d1a;background:#fcf3d4;padding:0 5px;border-radius:8px;margin-left:5px;font-weight:normal;}.note{background:#fff7e6;border-left:3px solid #e6a23c;padding:6px 10px;font-size:12px;color:#6b5b1f;margin:8px 0 14px;max-width:880px;}");
        sb.AppendLine("</style></head><body>");

        sb.AppendLine($"<h1>{Escape(r.Character)} move data</h1>");
        sb.AppendLine($"<div class=\"meta\">Generated {Escape(r.GeneratedAt)} &middot; real ServerSimulation (ADR-0019). " +
            "No DI/SDI input, hitstop reported not simulated, hit connects on first active frame.<br>" +
            $"Trajectories: {Escape(r.ReportArena.Note)}. " +
            $"Kill %: {Escape(r.KillArena.Note)} &mdash; top {Blast(r.KillArena.KillTop)}, side &plusmn;{Blast(r.KillArena.KillMaxX)}, " +
            $"bottom {Blast(r.KillArena.KillHeight)}. Center launch, victim passive.</div>");

        // A — frame-advantage heatmap
        sb.AppendLine("<h2>Frame advantage</h2>");
        if (r.Moves.Any(m => m.Frame.Adaptive))
            sb.AppendLine("<div class=\"note\">Moves tagged <b>adaptive</b> use the Melee-361 auto-angle (the real launch angle varies with hit position); their trajectory here uses the <b>authored angle as a representative</b>. Frame data (active / duration / IASA / stun) is exact regardless.</div>");
        sb.AppendLine("<div class=\"legend\">Cells = on-hit frame advantage in ticks (stun &minus; recovery). Green = attacker acts first (+), red = victim recovers first (&minus;). Click a column header to sort; hover a cell for KV / stun / apex.</div>");
        sb.AppendLine("<table id=\"adv\" class=\"heat\"><thead><tr><th class=\"move\" onclick=\"sortT(0,false)\">move</th>");
        for (int i = 0; i < r.Percents.Length; i++) sb.AppendLine($"<th onclick=\"sortT({i + 1},true)\">{r.Percents[i]}%</th>");
        sb.AppendLine($"<th class=\"kill\" onclick=\"sortT({r.Percents.Length + 1},true)\">kill%</th></tr></thead><tbody>");
        foreach (var m in r.Moves)
        {
            sb.AppendLine($"<tr><td class=\"move\">{Escape(m.Label)}{MoveTag(m)}</td>");
            foreach (var tr in m.Trajectories)
                sb.AppendLine($"<td style=\"background:{AdvColor(tr.Adv)}\" title=\"KV {F(tr.Kv)} m/s &middot; stun {tr.Stun} &middot; apex {F(tr.Apex)} m &middot; hitstop {tr.Hitstop}\">{tr.Adv}</td>");
            sb.AppendLine($"<td class=\"kill\">{(m.KillPct != null ? $"<span class=\"killbadge\">{m.KillPct}% {Escape(m.KillLine!)}</span>" : "&mdash;")}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");

        // B — knockback shape gallery (COMMON scale across the whole character so absolute sizes compare)
        float gMaxTravel = 1f, gMaxHeight = 1f;
        foreach (var m in r.Moves)
            foreach (var tr in m.Trajectories)
            {
                if (tr.MaxTravel > gMaxTravel) gMaxTravel = tr.MaxTravel;
                if (tr.Apex > gMaxHeight) gMaxHeight = tr.Apex;
            }
        sb.AppendLine("<h2>Knockback shape</h2>");
        sb.AppendLine($"<div class=\"legend\">Arc = height (m) vs horizontal travel (m). <b>Common scale across the character</b> — axes span {F(gMaxTravel)} m wide &times; {F(gMaxHeight)} m tall, so absolute size is comparable move-to-move. Red dot = apex; red dashed line = blast crossing (KO). <b>Spike moves (negative angle) launch from {F(SpikeLaunchAltitude)} m up</b> (off-stage scenario) so the downward send is visible.</div>");
        foreach (var m in r.Moves)
        {
            sb.AppendLine($"<div class=\"move\"><strong>{Escape(m.Label)}{MoveTag(m)}</strong><div class=\"arcs\">");
            foreach (var tr in m.Trajectories)
                sb.AppendLine($"<figure>{ArcSvg(tr, gMaxTravel, gMaxHeight)}<figcaption>{tr.Pct}% &middot; KV {F(tr.Kv)} &middot; apex {F(tr.Apex)}m &middot; stun {tr.Stun}</figcaption></figure>");
            sb.AppendLine("</div></div>");
        }

        // C — KO + blast clearance
        sb.AppendLine("<h2>KO &amp; blast clearance</h2>");
        sb.AppendLine("<div class=\"legend\">kill % = lowest victim % at which the launch crosses a blast line before landing (&mdash; = no KO by 250%). Clearance = fraction of the way to the top / side blast line at the highest simulated %, so a move that can't kill still shows how close it gets.</div>");
        sb.AppendLine("<table class=\"heat\"><thead><tr><th class=\"move\">move</th><th>kill %</th><th>top</th><th>side</th></tr></thead><tbody>");
        foreach (var m in r.Moves)
        {
            int tp = (int)Math.Round(m.Clearance.TopFrac * 100), sp = (int)Math.Round(m.Clearance.SideFrac * 100);
            sb.AppendLine($"<tr><td class=\"move\">{Escape(m.Label)}{MoveTag(m)}</td>");
            sb.AppendLine($"<td>{(m.KillPct != null ? $"{m.KillPct}% {Escape(m.KillLine!)}" : "&mdash;")}</td>");
            sb.AppendLine($"<td class=\"clr\">{Bar(tp)}</td><td class=\"clr\">{Bar(sp)}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");

        sb.AppendLine("<script>function sortT(n,nums){var tb=document.getElementById('adv').tBodies[0],rows=[].slice.call(tb.rows);rows.sort(function(a,b){var av=a.cells[n].textContent,bv=b.cells[n].textContent;if(nums){av=parseInt(av)||-9999;bv=parseInt(bv)||-9999;return av-bv;}return av.localeCompare(bv);});rows.forEach(function(r){tb.appendChild(r);});}</script>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    // ── CLI ──────────────────────────────────────────────────────────────────

    private static int[]? ParsePcts(string[] args)
    {
        int i = Array.IndexOf(args, "--pcts");
        if (i < 0 || i + 1 >= args.Length) return null;
        return args[i + 1].Split(',').Select(int.Parse).ToArray();
    }

    private static string? ParseOut(string[] args)
    {
        int i = Array.IndexOf(args, "--out");
        if (i < 0 || i + 1 >= args.Length) return null;
        return args[i + 1];
    }
}
