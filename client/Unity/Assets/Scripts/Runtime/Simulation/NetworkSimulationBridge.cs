using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Client.Network;
using UnityEngine;

namespace SlopArena.Client.Simulation
{
    /// <summary>
    /// ISimulationBridge backed by the UDP NetworkClient.
    /// Sends local input each tick; returns latest server-authoritative states.
    /// No local simulation — one-tick display latency is intentional (Phase 1).
    /// </summary>
    public class NetworkSimulationBridge : ISimulationBridge
    {
        private readonly NetworkClient _client;
        private uint _tick;
        private ulong _localEntityId;
        private readonly Dictionary<ulong, CharacterState> _latestStates = new();

        public NetworkSimulationBridge(NetworkClient client, ulong localEntityId)
        {
            _client = client;
            _localEntityId = localEntityId;
        }

        /// <summary>No-op: server owns entity registration. Stored def handled by caller.</summary>
        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState,
            BakedAnimationData? baked = null) { }

        /// <summary>
        /// Send local player input, advance tick, drain latest server states.
        /// </summary>
        public void Tick(Dictionary<ulong, InputState> inputs)
        {
            if (inputs.TryGetValue(_localEntityId, out var input))
                _client.SendInput(input, _tick);

            _tick++;

            var received = _client.ReceiveStates();
            foreach (var kv in received)
                _latestStates[kv.Key] = kv.Value;
        }

        public CharacterState GetState(ulong id)
            => _latestStates.TryGetValue(id, out var s) ? s : default;

        public Dictionary<ulong, CharacterState> GetAllStates() => _latestStates;

        /// <summary>No local resolver — server owns hitbox collision.</summary>
        public SpellResolver? Resolver => null;
    }
}
