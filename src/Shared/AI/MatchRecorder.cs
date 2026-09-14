using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopArena.Shared.AI;

/// <summary>
/// Accumulates telemetry during a self-play match: per-tick positions, hit events, combo
/// links, and swing records (slot presses with their active window, connect/whiff, and — on
/// whiff — the opponent's position relative to the attacker in the facing frame).
///
/// Swings are detected from the bot's PRE-TICK press (<c>RecordPresses</c>, called before the
/// sim consumes the input). <c>CharacterState.AttackSlot</c> is NOT a reliable start signal —
/// it persists as the "last used slot" and never returns to 0, so 0→nonzero transitions fire
/// once per entity. A swing is a whiff iff no hit from that attacker lands within its window.
/// </summary>
public sealed class MatchRecorder
{
    /// <summary>Maximum age of a same-pair hit link before it is a new exchange.</summary>
    public const int ComboGapTicks = 90;

    private readonly MatchRecord _record = new();
    private readonly Dictionary<ulong, List<SwingRecord>> _openSwings = new();
    private readonly Dictionary<(ulong Attacker, ulong Target), ComboLink> _openCombos = new();
    private readonly Dictionary<(ulong Attacker, ulong Target), bool> _actionableSinceHit = new();

    public MatchRecord Record => _record;

    /// <summary>Finalize the record after the match loop. Returns the record.</summary>
    public MatchRecord Finish(int durationTicks, int seed, MatchOutcome outcome)
    {
        _record.DurationTicks = durationTicks;
        _record.Seed = seed;
        _record.WinnerEntityId = outcome.WinnerEntityId;
        _record.SharedVictory = outcome.IsSharedVictory;
        foreach (var combo in _openCombos.Values)
            if (combo.Hits >= 2)
                _record.Combos.Add(combo);
        _openCombos.Clear();
        return _record;
    }

    /// <summary>Open swings from the tick's presses. Call BEFORE sim.Tick (inputs not yet consumed).</summary>
    public void RecordPresses(ServerSimulation sim, int tick, IReadOnlyDictionary<ulong, InputState> inputs, CharacterDefinition def)
    {
        var states = sim.GetAllStates();
        foreach (var (id, input) in inputs)
        {
            if (input.ActiveSlot == 0) continue;
            if (!states.TryGetValue(id, out var st)) continue;

            bool air = !st.IsGrounded;
            int window = ActiveWindowTicks(def, input.ActiveSlot, air);
            ulong targetId = st.TargetEntityId;
            float side = 0f, fwd = 0f, dy = 0f;
            if (targetId > 0 && states.TryGetValue(targetId, out var target))
            {
                float dx = target.PX - st.PX;
                float dz = target.PZ - st.PZ;
                dy = target.PY - st.PY;
                (side, fwd) = FacingMath.ToFacingFrame(dx, dz, st.FacingYaw);
            }
            var swing = new SwingRecord
            {
                Attacker = id, Target = targetId, ActiveSlot = input.ActiveSlot, Air = air,
                StartTick = tick, WindowTicks = window,
                RelSide = side, RelForward = fwd, RelHeight = dy,
            };
            if (!_openSwings.TryGetValue(id, out var list)) { list = new(); _openSwings[id] = list; }
            list.Add(swing);
            _record.Swings.Add(swing);
        }
    }

    /// <summary>Accumulate hits, positions, and close expired swings. Call AFTER sim.Tick.</summary>
    public void RecordTick(ServerSimulation sim, int tick, IReadOnlyDictionary<ulong, InputState> inputs, CharacterDefinition def)
    {
        var states = sim.GetAllStates();

        foreach (var (id, st) in states)
            _record.Samples.Add(new TickSample { Tick = tick, EntityId = id, PX = st.PX, PY = st.PY, PZ = st.PZ });

        foreach (var pair in _openCombos.Keys.ToArray())
            if (states.TryGetValue(pair.Target, out var target) && IsActionable(target))
                _actionableSinceHit[pair] = true;

        var hitPairs = new HashSet<(ulong Attacker, ulong Target)>();
        foreach (var hit in sim.LastTickHits)
        {
            _record.Hits.Add(new HitEvent
            {
                Attacker = hit.OwnerEntityId,
                Target = hit.TargetEntityId,
                AttackSlot = hit.AttackSlot,
                Damage = hit.Damage,
                Tick = tick,
            });
            if (_openSwings.TryGetValue(hit.OwnerEntityId, out var swings))
                foreach (var sw in swings) sw.Connected = true;

            var pair = (hit.OwnerEntityId, hit.TargetEntityId);
            bool hadActionableWindow = _actionableSinceHit.TryGetValue(pair, out var actionable)
                && actionable;
            ComboLink? combo = _openCombos.TryGetValue(pair, out var existing) ? existing : null;
            if (hadActionableWindow && combo is { Hits: 1 }
                && tick - combo.EndTick <= ComboGapTicks)
            {
                combo.Hits = 2;
                combo.EndTick = tick;
                combo.IsTrueCombo = false;
                combo.IsPressureString = true;
            }
            else if (combo == null || tick - combo.EndTick > ComboGapTicks || hadActionableWindow)
            {
                if (combo is { Hits: >= 2 })
                    _record.Combos.Add(combo);
                combo = new ComboLink
                {
                    Attacker = hit.OwnerEntityId,
                    Target = hit.TargetEntityId,
                    Hits = 1,
                    StartTick = tick,
                    EndTick = tick,
                    IsTrueCombo = !hadActionableWindow,
                    IsPressureString = hadActionableWindow,
                };
                _openCombos[pair] = combo;
            }
            else
            {
                combo.Hits++;
                combo.EndTick = tick;
            }

            _actionableSinceHit[pair] = false;
            hitPairs.Add(pair);
        }

        foreach (var pair in _openCombos.Keys.ToArray())
        {
            if (hitPairs.Contains(pair)
                || !states.TryGetValue(pair.Item2, out var target))
                continue;
            if (IsActionable(target))
                _actionableSinceHit[pair] = true;
        }


        foreach (var id in _openSwings.Keys.ToArray())
        {
            var list = _openSwings[id];
            list.RemoveAll(sw => tick > sw.StartTick + sw.WindowTicks);
            if (list.Count == 0) _openSwings.Remove(id);
        }
    }
    private static bool IsActionable(in CharacterState state)
        => state.HitstunTicks == 0
            && state.HitstopTicks == 0
            && state.AnimLockTicks == 0
            && state.LandingLagTicks == 0
            && state.State is ActionState.Idle or ActionState.Run;

    /// <summary>Active window for the resolved slot's timeline, or legacy first-stage hitboxes.</summary>
    private static int ActiveWindowTicks(CharacterDefinition def, byte activeSlot, bool airborne)
    {
        var cooked = def.GetCookedSlotAbility(activeSlot, airborne);
        if (cooked != null)
        {
            int total = 0;
            int effectEnd = 0;
            foreach (var stage in cooked.Timeline.Stages)
            {
                foreach (var operation in stage.Operations)
                {
                    int end = operation.Tick;
                    if (operation is CookedSpawnProjectileOperation projectile)
                        end += projectile.Projectile.MaxFlightTicks;
                    else if (operation is CookedStartCapabilityOperation capability)
                        end += CapabilityWindowTicks(capability.Parameters);
                    effectEnd = Math.Max(effectEnd, total + end);
                }
                total += stage.DurationTicks;
            }
            return Math.Max(total, effectEnd);
        }

        var spec = def.GetSlotAbility(activeSlot - 1, airborne);
        if (spec == null || spec.Stages == null || spec.Stages.Length == 0) return 0;
        int max = 0;
        if (spec.Stages[0].HitboxEvents != null)
            foreach (var evt in spec.Stages[0].HitboxEvents)
                max = Math.Max(max, evt.TriggerTick + evt.DurationTicks);
        return max;
    }
    private static int CapabilityWindowTicks(CookedCapabilityParameters parameters)
        => parameters switch
        {
            CookedKiShotCapabilityParameters x => x.StartupTicks + x.DurationTicks + x.MaxFlightTicks,
            CookedRisingDragonCapabilityParameters x => x.RiseDelay + x.RiseTicks,
            CookedCycloneKickCapabilityParameters x => x.DurationTicks,
            CookedDragonBeamCapabilityParameters x => x.DurationTicks + x.HitboxDurationTicks,
            CookedKistuDashSlashCapabilityParameters x => x.MaxAimTicks + x.DashDurationTicks,
            CookedKistuRisingSlashCapabilityParameters x => x.RiseTicks,
            CookedKistuBladeFlurryCapabilityParameters x => x.MoveTicks,
            CookedBonkTargetedJumpSlamCapabilityParameters x => x.MaxAimTicks + x.MaxFlightTicks + x.SlamDurationTicks,
            CookedMankiRoundBombCapabilityParameters x => x.ThrowTriggerTick + x.MaxFlightTicks + x.ExplosionDurationTicks,
            CookedMankiJetpackBoostCapabilityParameters x => x.StartupTicks + x.ExplosionDurationTicks,
            CookedMankiBazookaCapabilityParameters x => x.FireTriggerTick + x.MaxFlightTicks + x.RecoveryDuration,
            _ => 0,
        };
}
