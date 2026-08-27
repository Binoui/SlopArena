using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// The self entity's continuously-running ServerSimulation (ADR-0011). Never rebuilt
    /// from a received snapshot — fed the player's true InputState every tick. Corrected by
    /// patching wire-serialized fields onto its own full-fidelity history when the server
    /// packet disagrees, replayed forward only across a Predictable-state suffix (D9) —
    /// a Complex tick anywhere in the replay range means "trust the live sim", never rebuilt.
    ///
    /// Also mirrors other entities' current best-known states in as read-only lookup
    /// targets: ServerSimulation.ProcessTargetLock indexes _states[targetId] directly for
    /// any entity whose input carries a nonzero TargetEntityId (screen-center soft-lock,
    /// set every frame an opponent is near screen center — not attack-only). Without a
    /// mirror, that throws KeyNotFoundException the moment an opponent is on screen.
    /// </summary>
    public sealed class LocalTrack
    {
        private readonly ServerSimulation _sim;
        private readonly ulong _entityId;
        private readonly List<(uint Tick, CharacterState State, InputState Input)> _history = new();
        private readonly HashSet<ulong> _mirrored = new();
        private const int WindowCap = 30;
        private uint _localTick;

        public int CorrectionCount { get; private set; }

        public LocalTrack(ArenaDefinition arena, ulong entityId, IMatchRule? rule = null)
        {
            _sim = new ServerSimulation(arena, rule);
            _entityId = entityId;
        }

        public void RegisterEntity(CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
        {
            _sim.RegisterEntity(_entityId, def, initialState, baked);
            _history.Clear();
            _localTick = 0;
            _history.Add((0, _sim.GetState(_entityId), default));
        }

        /// <summary>Register-or-update a read-only mirror of another entity, purely so
        /// ServerSimulation's target-lock lookups resolve. Never rendered from this track.</summary>
        public void SyncOpponentMirror(ulong id, CharacterDefinition def, CharacterState state)
        {
            if (_mirrored.Add(id))
                _sim.RegisterEntity(id, def, state);
            else
                _sim.SetState(id, state);
        }

        public CharacterState Tick(InputState input)
        {
            _sim.Tick(new Dictionary<ulong, InputState> { { _entityId, input } });
            var state = _sim.GetState(_entityId);
            _localTick++;
            _history.Add((_localTick, state, input));
            if (_history.Count > WindowCap) _history.RemoveAt(0);
            return state;
        }

        /// <summary>Apply a received packet for the self entity (D4). Only actually replays
        /// when every ticked state from the packet's tick to "now" was Predictable — a Complex
        /// tick anywhere in that suffix means the live sim (with its real, never-rebuilt
        /// ability instance) is trusted as-is instead.</summary>
        public void ReconcileWithServer(ServerEntityPacket packet)
        {
            int idx = _history.FindIndex(h => h.Tick == packet.Tick);
            if (idx < 0) return; // outside the window — trust the continuous sim, self-heals next packet

            for (int i = idx; i < _history.Count; i++)
                if (!ActionStateClassifier.IsSnapSafe(_history[i].State.State))
                    return;

            CorrectionCount++;

            var corrected = _history[idx].State;
            packet.State.ApplyTo(ref corrected);
            _sim.SetState(_entityId, corrected);
            _history[idx] = (_history[idx].Tick, corrected, _history[idx].Input);

            for (int i = idx + 1; i < _history.Count; i++)
            {
                _sim.Tick(new Dictionary<ulong, InputState> { { _entityId, _history[i].Input } });
                _history[i] = (_history[i].Tick, _sim.GetState(_entityId), _history[i].Input);
            }
        }

        public CharacterState GetState() => _sim.GetState(_entityId);
        public SpellResolver? Resolver => _sim.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _sim.LastTickHits;

        public IReadOnlyList<TimelinePresentationEvent> DrainPresentationEvents()
            => _sim.GetPresentationEvents(clear: true);
    }
}
