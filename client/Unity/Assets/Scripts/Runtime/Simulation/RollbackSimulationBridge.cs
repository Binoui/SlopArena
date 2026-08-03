using System.Collections.Generic;
using SlopArena.Shared;
using SlopArena.Shared.Rollback;
using SlopArena.Client.Network;

namespace SlopArena.Client.Simulation
{
    /// <summary>
    /// ISimulationBridge backed by RollbackSimulator (ADR-0011): the self entity predicts
    /// continuously (LocalTrack); opponents predict while in a Predictable ActionState
    /// (PredictedTrack) and render raw from the server otherwise (RawTrack). Replaces
    /// NetworkSimulationBridge for PvPMatch — Training keeps LocalSimulationBridge.
    /// </summary>
    public class RollbackSimulationBridge : ISimulationBridge
    {
        private readonly RollbackSimulator _core;
        private readonly NetworkClient _client;
        private readonly ulong _selfId;
        private uint _tick;

        public RollbackSimulationBridge(ArenaDefinition arena, NetworkClient client, ulong selfEntityId, IMatchRule? rule = null)
        {
            _core = new RollbackSimulator(arena, selfEntityId, rule);
            _client = client;
            _selfId = selfEntityId;
        }

        /// <summary>Debug overlay data (Task 10) — not part of ISimulationBridge.</summary>
        public int CorrectionCount => _core.CorrectionCount;
        public uint LastFrontierTicks => _core.LastFrontierTicks;

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => _core.RegisterEntity(id, def, initialState, baked);

        public void Tick(Dictionary<ulong, InputState> inputs)
        {
            if (inputs.TryGetValue(_selfId, out var input))
                _client.SendInput(input, _tick);
            _tick++;

            _core.Tick(inputs);

            var packets = _client.ReceiveEntityPackets();
            if (packets.Count == 0) return;

            var opponentBatch = new List<ServerEntityPacket>(packets.Count);
            foreach (var packet in packets)
            {
                if (packet.EntityId == _selfId)
                    _core.ReconcileSelf(packet);
                else
                    opponentBatch.Add(packet);
            }
            if (opponentBatch.Count > 0)
                _core.IngestOpponentBatch(opponentBatch);
        }

        public CharacterState GetState(ulong id) => _core.GetState(id);
        public Dictionary<ulong, CharacterState> GetAllStates() => _core.GetAllStates();
        public SpellResolver? Resolver => _core.Resolver;
    }
}
