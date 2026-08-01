namespace SlopArena.Client.UI
{
    public enum GameMode { Training, PvP }

    public static class MatchConfig
    {
        public static GameMode Mode = GameMode.Training;
        public static SlopArena.Shared.CharacterClass PlayerClass
            = SlopArena.Shared.CharacterClass.FightGuy;
        public static SlopArena.Shared.CharacterClass OpponentClass
            = SlopArena.Shared.CharacterClass.Manki;
        public static string ArenaName = "training";
        public static bool IsHost = true;
        public static string ServerIP = "127.0.0.1";
        public static int ServerPort = 9876;

        // Per-match entity IDs assigned by the master server at match start
        // (issue #35). The local player drives LocalEntityId; the rendered
        // opponent is OpponentEntityId. Default to the legacy 1/2 so training
        // and the old join-by-IP path keep working unchanged.
        public static ulong LocalEntityId = 1;
        public static ulong OpponentEntityId = 2;

        public static void Reset()
        {
            Mode = GameMode.Training;
            PlayerClass = SlopArena.Shared.CharacterClass.FightGuy;
            OpponentClass = SlopArena.Shared.CharacterClass.Manki;
            ArenaName = "training";
            IsHost = true;
            ServerIP = "127.0.0.1";
            ServerPort = 9876;
            LocalEntityId = 1;
            OpponentEntityId = 2;
        }
    }
}
