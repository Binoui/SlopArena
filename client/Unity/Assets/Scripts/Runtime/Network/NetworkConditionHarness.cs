using System;
using System.Collections.Generic;
using SlopArena.Shared;

namespace SlopArena.Client.Network
{
    /// <summary>
    /// Dev-only seam for exercising rollback under simulated bad network conditions before
    /// the friends playtest (docs/plans/2026-08-02-rollback-netcode.md, Delay/loss harness).
    /// Not wired into NetworkClient by default — a developer wraps ReceiveEntityPackets()
    /// output through this manually, behind a DEVELOPMENT_BUILD/editor-only toggle, when
    /// testing. Deterministic given a seed, so behavior is reproducible across runs.
    /// </summary>
    public sealed class NetworkConditionHarness
    {
        private readonly Random _random;
        private readonly float _dropChance;
        private readonly float _duplicateChance;
        private readonly uint _extraDelayTicks;
        private readonly List<(uint AvailableAtTick, ServerEntityPacket Packet)> _delayed = new();

        public NetworkConditionHarness(float dropChance = 0f, float duplicateChance = 0f, uint extraDelayTicks = 0, int seed = 0)
        {
            _dropChance = dropChance;
            _duplicateChance = duplicateChance;
            _extraDelayTicks = extraDelayTicks;
            _random = new Random(seed);
        }

        /// <summary>Feed freshly-received packets in; get back what the client should actually
        /// "receive" this tick, after simulated drop, duplication, and injected RTT delay.</summary>
        public List<ServerEntityPacket> Process(List<ServerEntityPacket> incoming, uint currentTick)
        {
            foreach (var packet in incoming)
            {
                if (_random.NextDouble() < _dropChance) continue;
                _delayed.Add((currentTick + _extraDelayTicks, packet));
                if (_random.NextDouble() < _duplicateChance)
                    _delayed.Add((currentTick + _extraDelayTicks, packet));
            }

            var due = new List<ServerEntityPacket>();
            for (int i = _delayed.Count - 1; i >= 0; i--)
            {
                if (_delayed[i].AvailableAtTick > currentTick) continue;
                due.Add(_delayed[i].Packet);
                _delayed.RemoveAt(i);
            }
            return due;
        }
    }
}
