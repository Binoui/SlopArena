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
    /// <summary>Max ticks between same-(attacker,target) hits to count as one combo.</summary>
    public const int ComboGapTicks = 90;

    private readonly MatchRecord _record = new();
    private readonly Dictionary<ulong, List<SwingRecord>> _openSwings = new();
    private ComboLink? _currentCombo;

    public MatchRecord Record => _record;

    /// <summary>Finalize the record after the match loop. Returns the record.</summary>
    public MatchRecord Finish(int durationTicks, int seed, MatchOutcome outcome)
    {
        _record.DurationTicks = durationTicks;
        _record.Seed = seed;
        _record.WinnerEntityId = outcome.WinnerEntityId;
        _record.SharedVictory = outcome.IsSharedVictory;
        if (_currentCombo is { Hits: >= 2 })
            _record.Combos.Add(_currentCombo);
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

        foreach (var hit in sim.LastTickHits)
        {
            _record.Hits.Add(new HitEvent
            {
                Attacker = hit.OwnerEntityId, Target = hit.TargetEntityId, Damage = hit.Damage, Tick = tick,
            });
            if (_openSwings.TryGetValue(hit.OwnerEntityId, out var swings))
                foreach (var sw in swings) sw.Connected = true;

            if (_currentCombo != null
                && _currentCombo.Attacker == hit.OwnerEntityId
                && _currentCombo.Target == hit.TargetEntityId
                && tick - _currentCombo.EndTick <= ComboGapTicks)
            {
                _currentCombo.Hits++;
                _currentCombo.EndTick = tick;
            }
            else
            {
                if (_currentCombo is { Hits: >= 2 })
                    _record.Combos.Add(_currentCombo);
                _currentCombo = new ComboLink
                {
                    Attacker = hit.OwnerEntityId, Target = hit.TargetEntityId, Hits = 1, StartTick = tick, EndTick = tick,
                };
            }
        }

        foreach (var id in _openSwings.Keys.ToArray())
        {
            var list = _openSwings[id];
            list.RemoveAll(sw => tick > sw.StartTick + sw.WindowTicks);
            if (list.Count == 0) _openSwings.Remove(id);
        }
    }

    /// <summary>Active window (ticks) of a slot's first stage = max(trigger + duration) across its hitboxes.</summary>
    private static int ActiveWindowTicks(CharacterDefinition def, byte activeSlot, bool airborne)
    {
        var spec = def.GetSlotAbility(activeSlot - 1, airborne);
        if (spec == null || spec.Stages == null || spec.Stages.Length == 0) return 0;
        int max = 0;
        if (spec.Stages[0].HitboxEvents != null)
            foreach (var evt in spec.Stages[0].HitboxEvents)
                max = Math.Max(max, evt.TriggerTick + evt.DurationTicks);
        return max;
    }
}
