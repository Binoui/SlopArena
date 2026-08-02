using System.Collections.Generic;

namespace SlopArena.Client.UI
{
    public enum GameMode { Training, PvP }

    public static class MatchConfig
    {
        public static GameMode Mode = GameMode.Training;
        public static SlopArena.Shared.CharacterClass PlayerClass
            = SlopArena.Shared.CharacterClass.FightGuy;
        public static string ArenaName = "training";
        public static bool IsHost = true;
        public static string ServerIP = "127.0.0.1";
        public static int ServerPort = 9876;

        /// <summary>One opponent in the match (issue #36): entity ID + class.</summary>
        public sealed class OpponentInfo
        {
            public ulong EntityId { get; }
            public SlopArena.Shared.CharacterClass Class { get; }

            public OpponentInfo(ulong entityId, SlopArena.Shared.CharacterClass @class)
            {
                EntityId = entityId;
                Class = @class;
            }
        }

        // Per-match entity IDs assigned by the master server at match start
        // (issue #35). The local player drives LocalEntityId; the opponents
        // come from Opponents (issue #36). Defaults keep training and the old
        // join-by-IP path working unchanged.
        public static ulong LocalEntityId = 1;

        /// <summary>All non-local players, in master-server roster order.</summary>
        public static List<OpponentInfo> Opponents = new();

        public static void Reset()
        {
            Mode = GameMode.Training;
            PlayerClass = SlopArena.Shared.CharacterClass.FightGuy;
            ArenaName = "training";
            IsHost = true;
            ServerIP = "127.0.0.1";
            ServerPort = 9876;
            LocalEntityId = 1;
            Opponents.Clear();
        }
    }
}
