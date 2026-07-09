using System.Collections.Generic;
using SlopArena.Shared;

namespace SlopArena.Client.Simulation
{
    public class LocalSimulationBridge : ISimulationBridge
    {
        private readonly ServerSimulation _server;
        private readonly ArenaDefinition _arena;

        public LocalSimulationBridge(ArenaDefinition arena)
        {
            _arena = arena;
            _server = new ServerSimulation(arena);
        }

        public void RegisterEntity(ulong id, CharacterDefinition def, CharacterState initialState, BakedAnimationData? baked = null)
            => _server.RegisterEntity(id, def, initialState, baked);

        public void Tick(Dictionary<ulong, InputState> inputs)
            => _server.Tick(inputs);

        public CharacterState GetState(ulong id) => _server.GetState(id);
        public Dictionary<ulong, CharacterState> GetAllStates() => _server.GetAllStates();
        public SpellResolver? Resolver => _server.Resolver;
        public ServerSimulation InternalSim => _server;
        public void SetRespawnPosition(ulong id, float x, float y, float z)
            => _server.SetRespawnPosition(id, x, y, z);
    }
}
