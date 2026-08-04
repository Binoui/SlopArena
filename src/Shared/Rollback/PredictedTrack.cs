using System.Collections.Generic;

namespace SlopArena.Shared.Rollback
{
    /// <summary>
    /// Rebuild-and-replay for opponent entities currently in a Predictable ActionState (D9).
    /// Owns ONE shared ServerSimulation for every tracked opponent, matching how the real
    /// server also sims everyone together — so hurtbox/collision checks between two
    /// tracked opponents behave consistently. Callers (RollbackSimulator, Task 5) must
    /// route Complex-state packets elsewhere (RawTrack) — this class only ever sees
    /// Predictable-state packets and calls StopTracking when an entity leaves that partition.
    /// </summary>
    public sealed class PredictedTrack
    {
        private readonly ServerSimulation _sim;
        private readonly Dictionary<ulong, InputState> _lastKnownInput = new();
        private readonly HashSet<ulong> _registered = new();
        private const uint WindowCap = 30;

        public uint LastFrontierTicks { get; private set; }

        public PredictedTrack(ArenaDefinition arena, IMatchRule? rule = null) => _sim = new ServerSimulation(arena, rule);

        public bool IsTracking(ulong id) => _registered.Contains(id);

        /// <summary>
        /// Apply one network drain's worth of Predictable-state packets, then replay the
        /// frontier (ConfirmedTick..currentLocalTick) using held-last inputs (D5), capped at
        /// WindowCap ticks as a desync guard.
        /// </summary>
        public void ApplyBatch(IReadOnlyList<ServerEntityPacket> packets, uint currentLocalTick,
            IReadOnlyDictionary<ulong, CharacterDefinition> defs,
            IReadOnlyDictionary<ulong, BakedAnimationData?> baked)
        {
            if (packets.Count == 0) { LastFrontierTicks = 0; return; }

            uint maxConfirmedTick = 0;
            foreach (var packet in packets)
            {
                var confirmedState = packet.State.ToState();
                confirmedState.EntityId = packet.EntityId;

                bool firstRegister = _registered.Add(packet.EntityId);
                if (!defs.TryGetValue(packet.EntityId, out var def))
                {
                    // No definition — cannot simulate this entity. Roll back the
                    // _registered mark so a later SetState can't create an unpaired
                    // _states key (missing def → SimulateMovement KeyNotFound).
                    _registered.Remove(packet.EntityId);
                    continue;
                }
                if (firstRegister)
                    _sim.RegisterEntity(packet.EntityId, def, confirmedState,
                        baked.TryGetValue(packet.EntityId, out var b) ? b : null);
                else
                    _sim.SetState(packet.EntityId, confirmedState);

                _lastKnownInput[packet.EntityId] = packet.HasInput ? packet.Input : default;
                if (packet.Tick > maxConfirmedTick) maxConfirmedTick = packet.Tick;
            }

            uint frontierTicks = currentLocalTick > maxConfirmedTick ? currentLocalTick - maxConfirmedTick : 0;
            if (frontierTicks > WindowCap) frontierTicks = WindowCap;
            LastFrontierTicks = frontierTicks;

            for (uint i = 0; i < frontierTicks; i++)
            {
                var inputs = new Dictionary<ulong, InputState>(_registered.Count);
                foreach (var id in _registered)
                    if (_lastKnownInput.TryGetValue(id, out var input))
                        inputs[id] = input;
                _sim.Tick(inputs);
            }
        }

        public void StopTracking(ulong id)
        {
            if (_registered.Remove(id))
            {
                _sim.RemoveEntity(id);
                _lastKnownInput.Remove(id);
            }
        }

        public CharacterState GetState(ulong id) => _sim.GetState(id);
    }
}
