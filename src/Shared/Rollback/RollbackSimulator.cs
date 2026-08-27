using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// Composes LocalTrack (self), PredictedTrack (opponents in a Predictable ActionState),
    /// and RawTrack (opponents in a Complex ActionState — just the latest received state,
    /// no simulation) into one entity-addressable surface (ADR-0011). Shape matches
    /// ISimulationBridge deliberately — RollbackSimulationBridge (Task 7) is a thin wrapper.
    /// </summary>
    public sealed class RollbackSimulator
    {
        private readonly LocalTrack _local;
        private readonly PredictedTrack _predicted;
        private readonly Dictionary<ulong, CharacterState> _rawTrackLatest = new();
        private readonly Dictionary<ulong, CharacterDefinition> _defs = new();
        private readonly Dictionary<ulong, BakedAnimationData?> _baked = new();
        private readonly ulong _selfId;
        private uint _localTick;
        private readonly List<TimelinePresentationEvent> _acceptedPresentationEvents = new();
        private readonly HashSet<PresentationEventKey> _seenPresentationEvents = new();

        public RollbackSimulator(ArenaDefinition arena, ulong selfEntityId, IMatchRule? rule = null)
        {
            _selfId = selfEntityId;
            _local = new LocalTrack(arena, selfEntityId, rule);
            _predicted = new PredictedTrack(arena, rule);
        }

        public int CorrectionCount => _local.CorrectionCount;
        public uint LastFrontierTicks => _predicted.LastFrontierTicks;

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
        {
            _defs[id] = def;
            _baked[id] = baked;
            if (id == _selfId)
                _local.RegisterEntity(def, initialState, baked);
            else
                _rawTrackLatest[id] = initialState; // opponents start on RawTrack until their first packet
        }

        /// <summary>Advance the self entity one tick. Mirrors every other known entity's
        /// current best-known state into LocalTrack first (target-lock crash fix, Task 3).</summary>
        public void Tick(Dictionary<ulong, InputState> inputs)
        {
            foreach (var id in _defs.Keys)
                if (id != _selfId)
                    _local.SyncOpponentMirror(id, _defs[id], GetState(id));

            var input = inputs.TryGetValue(_selfId, out var i) ? i : default;
            _local.Tick(input);
            _localTick++;
            Publish(_local.DrainPresentationEvents());

        }

        /// <summary>Feed one network drain's worth of opponent packets. Splits by ActionState
        /// (D9): Predictable entities go to PredictedTrack, Complex entities go to RawTrack.</summary>
        public void IngestOpponentBatch(IReadOnlyList<ServerEntityPacket> packets)
        {
            var predictable = new List<ServerEntityPacket>();
            foreach (var packet in packets)
            {
                var state = packet.State.ToState();
                if (ActionStateClassifier.IsPredictable(state.State) && _defs.ContainsKey(packet.EntityId))
                {
                    predictable.Add(packet);
                    _rawTrackLatest.Remove(packet.EntityId);
                }
                else
                {
                    // Unknown def (entity never registered) or Complex state: RawTrack —
                    // render as received, never simulate an entity we have no definition for.
                    _predicted.StopTracking(packet.EntityId);
                    state.EntityId = packet.EntityId;
                    _rawTrackLatest[packet.EntityId] = state;
                }
            }
            if (predictable.Count > 0)
            {
                _predicted.ApplyBatch(predictable, _localTick, _defs, _baked);
                Publish(_predicted.DrainPresentationEvents());
            }

        }

        /// <summary>Feed the self entity's own received packet (LocalTrack correction, D4).</summary>
        public void ReconcileSelf(ServerEntityPacket packet) => _local.ReconcileWithServer(packet);

        public CharacterState GetState(ulong id)
        {
            if (id == _selfId) return _local.GetState();
            if (_predicted.IsTracking(id)) return _predicted.GetState(id);
            return _rawTrackLatest.TryGetValue(id, out var s) ? s : default;
        }

        public Dictionary<ulong, CharacterState> GetAllStates()
        {
            var result = new Dictionary<ulong, CharacterState> { [_selfId] = _local.GetState() };
            foreach (var id in _defs.Keys)
                if (id != _selfId) result[id] = GetState(id);
            return result;
        }

        public void IngestPresentationEvent(TimelinePresentationEvent value)
        {
            if (_seenPresentationEvents.Add(value.Key))
                _acceptedPresentationEvents.Add(value);
        }

        public void IngestPresentationEvents(IReadOnlyList<TimelinePresentationEvent> values)
        {
            foreach (var value in values)
                IngestPresentationEvent(value);
        }

        public List<TimelinePresentationEvent> DrainPresentationEvents()
        {
            var result = new List<TimelinePresentationEvent>(_acceptedPresentationEvents);
            _acceptedPresentationEvents.Clear();
            return result;
        }

        private void Publish(IReadOnlyList<TimelinePresentationEvent> values)
            => IngestPresentationEvents(values);

        public SpellResolver? Resolver => _local.Resolver;
        public IReadOnlyList<SpellResolver.HitResult> LastTickHits => _local.LastTickHits;
    }
}
