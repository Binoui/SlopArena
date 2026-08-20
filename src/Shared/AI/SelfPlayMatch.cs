using System;
using System.Collections.Generic;

namespace SlopArena.Shared.AI;

/// <summary>
/// Runs one deterministic bot-vs-bot match on the real <see cref="ServerSimulation"/>
/// (issue #148). Both entities are driven by <see cref="HeuristicBotPolicy"/> with the same
/// seeded RNG stream; the match terminates via a <see cref="StockMatchRule"/> winner or a
/// deterministic tick cap. Same seed + def + arena → bit-identical match.
/// </summary>
public static class SelfPlayMatch
{
    /// <summary>Safety cap (3 min at 60Hz). A match hitting this is reported as a draw, not a crash.</summary>
    public const int DefaultMaxTicks = 10800;

    /// <summary>Entity IDs for the two bots — player id 1 and the NPC id 100 (existing convention).</summary>
    public const ulong EntityA = 1;
    public const ulong EntityB = 100;

    /// <param name="def">Both bots use the same character definition.</param>
    /// <param name="arena">A bounded arena (KO-able) so the stock rule can end the match.</param>
    /// <param name="baked">Optional baked skeleton data (for BoneName hitboxes); null skips bone-attached hitboxes.</param>
    /// <param name="seed">Determinism seed — same seed reproduces the same match.</param>
    /// <param name="maxTicks">Tick cap; a match reaching it is <see cref="MatchRecord.TimedOut"/>.</param>
    /// <param name="stocks">Stocks per bot for the <see cref="StockMatchRule"/>.</param>
    /// <param name="cpuLevel">Shared CPU level for both bots, clamped to 1..9.</param>
    public static MatchRecord Run(CharacterDefinition def, ArenaDefinition arena, int seed,
        BakedAnimationData? baked = null, int maxTicks = DefaultMaxTicks, int stocks = 3, int cpuLevel = 5)
    {
        var rule = new StockMatchRule((byte)stocks);
        var sim = new ServerSimulation(arena, rule);

        float gpy = def.CapsuleHeight * 0.5f;
        RegisterBot(sim, def, EntityA, -12f, gpy, baked);
        RegisterBot(sim, def, EntityB, 12f, gpy, baked);

        var rng = new Random(seed);
        var memA = new BotMemory();
        var memB = new BotMemory();
        int clampedCpuLevel = Math.Clamp(cpuLevel, 1, 9);
        memA.DifficultyLevel = clampedCpuLevel;
        memB.DifficultyLevel = clampedCpuLevel;
        var policy = new HeuristicBotPolicy();
        var recorder = new MatchRecorder();
        var inputs = new Dictionary<ulong, InputState>();
        int tick = 0;

        for (; tick < maxTicks; tick++)
        {
            var sA = sim.GetState(EntityA);
            var sB = sim.GetState(EntityB);
            bool targetAWasAttacking = IsThreatening(sB);
            bool targetBWasAttacking = IsThreatening(sA);
            inputs[EntityA] = policy.Decide(sA, sB, def, rng, memA);
            inputs[EntityB] = policy.Decide(sB, sA, def, rng, memB);

            recorder.RecordPresses(sim, tick, inputs, def); // swings from the pre-tick presses
            sim.Tick(inputs);
            recorder.RecordTick(sim, tick, inputs, def);     // hits + positions + close swings

            memA.LastAttackConnected = false;
            memB.LastAttackConnected = false;
            foreach (var hit in sim.LastTickHits)
            {
                if (hit.OwnerEntityId == EntityA) memA.LastAttackConnected = true;
                if (hit.OwnerEntityId == EntityB) memB.LastAttackConnected = true;
            }
            memA.LastTargetWasAttacking = targetAWasAttacking;
            memB.LastTargetWasAttacking = targetBWasAttacking;

            var outcome = rule.Evaluate(sim.GetAllStates());
            if (outcome.IsEnded)
                return Finalize(recorder, sim, tick, seed, outcome, timedOut: false);
        }

        // Tick cap reached — draw.
        return Finalize(recorder, sim, tick, seed, default, timedOut: true);
    }

    private static bool IsThreatening(in CharacterState state)
    {
        return state.State is ActionState.Attacking or ActionState.Aiming or ActionState.Warping
            || state.AnimLockTicks > 0
            || state.LandingLagTicks > 0
            || state.BurstRecoveryTicks > 0;
    }

    private static void RegisterBot(ServerSimulation sim, CharacterDefinition def, ulong id, float x, float py, BakedAnimationData? baked)
    {
        var state = new CharacterState
        {
            EntityId = id,
            PX = x, PY = py, PZ = 0f,
            State = ActionState.Idle,
            IsGrounded = true,
            JumpsLeft = def.Movement.MaxJumps,
            AirDodgesLeft = 1,
            FacingYaw = id == EntityA ? 0f : MathF.PI, // A faces +Z, B faces −Z
            MatchState = MatchState.Playing,
        };
        sim.RegisterEntity(id, def, state, baked);
        sim.SetRespawnPosition(id, x, py, 0f, state.FacingYaw);
    }

    private static MatchRecord Finalize(MatchRecorder recorder, ServerSimulation sim, int tick,
        int seed, MatchOutcome outcome, bool timedOut)
    {
        var record = recorder.Finish(tick, seed, outcome);
        record.TimedOut = timedOut;
        var sA = sim.GetState(EntityA);
        var sB = sim.GetState(EntityB);
        record.Entity1Deaths = sA.Deaths;
        record.Entity2Deaths = sB.Deaths;
        record.Entity1Damage = sA.DamagePercent;
        record.Entity2Damage = sB.DamagePercent;
        return record;
    }
}
