using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client;
using SlopArena.Client.Network;
using SlopArena.Client.UI;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Stage select screen. Two modes:
    /// <list type="bullet">
    /// <item><b>Training</b> — pick a stage, click CONFIRM STAGE, go to Arena_Offline.</item>
    /// <item><b>PvP</b> — reached from CharSelect once all players locked in
    /// (the host's SELECT STAGE button broadcasts <c>StageSelect</c>). The host
    /// picks the stage; non-hosts see a waiting label with disabled cards. The
    /// host's CONFIRM STAGE calls <c>StartMatch(arena)</c> on the master server,
    /// which launches the game server and broadcasts <c>MatchStarted</c>; every
    /// client then loads Arena_PvP via <see cref="ClientSession.ApplyMatchStarted"/>.</item>
    /// </list>
    /// The registry is file-driven (loaded from the arena directory on enable);
    /// a stage is offered iff its baked .arena parses with real collision AND a
    /// visual prefab exists at Resources/Stages/&lt;name&gt;.prefab (issue #77:
    /// hardcoded arenas carried no collision data and players fell through).
    /// </summary>
    public class StageSelectController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private string _selectedArena = "";
        private Button _btnConfirm;
        private LobbyClient _lobby;

        private void OnEnable()
        {
            var root       = _uiDocument.rootVisualElement;
            var grid       = root.Q<VisualElement>("stage-grid");
            _btnConfirm    = root.Q<Button>("btn-confirm");
            var lblWaiting = root.Q<Label>("lbl-waiting");
            var btnBack    = root.Q<Button>("btn-back");

            // Roster-based host check: MatchConfig.IsHost is false for every
            // client on a dedicated server (alfred) — players join via the
            // server browser — but the master still promotes the first joiner
            // to lobby host. CharSelectController stashed the roster-based
            // answer in ClientSession.IsLobbyHost.
            bool isHost = MatchConfig.Mode == GameMode.Training || ClientSession.IsLobbyHost;

            Debug.Log($"[StageSelect] mode={MatchConfig.Mode} isHost={isHost} (lobbyHost={ClientSession.IsLobbyHost})");

            // Host: confirm button hidden until a card is selected; client: show waiting label
            _btnConfirm.style.display = DisplayStyle.None;
            lblWaiting.style.display = isHost ? DisplayStyle.None : DisplayStyle.Flex;

            // File-driven registry: load the baked arenas from disk (issue #77 —
            // the old hardcoded ArenaRegistry list carried no collision data).
            string? arenaDir = BakedContentPaths.ArenaDirectory();
            if (arenaDir != null) ArenaRegistry.LoadFromDirectory(arenaDir);

            // Build stage cards from the loaded registry — a stage is offered iff
            // its baked .arena parses with real collision AND a visual prefab exists
            // (Resources/Stages/<name>.prefab); otherwise the player would fall
            // through the floor or fight an invisible stage.
            foreach (var arena in ArenaRegistry.All)
            {
                string? baked = BakedContentPaths.ResolveArena(arena.Name);
                if (baked == null) continue;
                var arenaOpt = ArenaBinaryFormat.LoadFromFile(baked);
                if (arenaOpt is not ArenaDefinition arenaDef) continue;
                if (arenaDef.CollisionTriangles == null || arenaDef.CollisionTriangles.Length == 0) continue;
                if (Resources.Load<GameObject>($"Stages/{arena.Name}") == null) continue;

                string capturedName = arena.Name;
                var card = new Button(() => SelectStage(capturedName, root))
                {
                    name = $"stage-{arena.Name}"
                };
                card.AddToClassList("stage-card");

                // Color swatch placeholder thumbnail
                var swatch = new VisualElement();
                swatch.AddToClassList("stage-swatch");
                if (!string.IsNullOrEmpty(arena.PreviewColor) &&
                    ColorUtility.TryParseHtmlString(arena.PreviewColor, out var swatchColor))
                    swatch.style.backgroundColor = swatchColor;

                var label = new Label(arena.DisplayName ?? arena.Name.ToUpper());
                label.AddToClassList("stage-name");

                card.Add(swatch);
                card.Add(label);
                card.SetEnabled(isHost);
                grid.Add(card);
            }

            // PvP: the match starts over the lobby connection once the host
            // confirms a stage. Keep the connection alive through the scene
            // transition (issue #34); MatchStarted arrives here while everyone
            // is still on this screen.
            bool isPvP = MatchConfig.Mode == GameMode.PvP;
            _lobby = isPvP ? ClientSession.ActiveLobby : null;
            if (_lobby != null)
            {
                _lobby.MatchStarted += OnMatchStarted;
                _lobby.Error        += OnError;
            }

            _btnConfirm.clicked += OnConfirmClicked;
            btnBack.clicked += () => SceneManager.LoadScene("CharSelect");
        }

        private void OnConfirmClicked()
        {
            if (string.IsNullOrEmpty(_selectedArena)) return;
            MatchConfig.ArenaName = _selectedArena;

            if (MatchConfig.Mode == GameMode.Training)
            {
                SceneManager.LoadScene("Arena_Offline");
                return;
            }

            // PvP: the host confirms the stage -> master launches the match
            // with it and broadcasts MatchStarted (port + arena) to everyone.
            _btnConfirm.SetEnabled(false);
            if (_lobby == null)
            {
                Debug.LogError("[StageSelect] PvP mode but no lobby connection. Returning to server browser.");
                SceneManager.LoadScene("ServerBrowser");
                return;
            }
            _ = _lobby.StartMatchAsync(_selectedArena);
        }

        private void OnMatchStarted(MatchStartedConfig config)
        {
            Debug.Log($"[StageSelect] Match started: {config.Players.Count} players, port={config.MatchPort}, arena={config.ArenaName}.");
            if (_lobby != null)
            {
                _lobby.MatchStarted -= OnMatchStarted;
                _lobby.Error        -= OnError;
            }
            ClientSession.ApplyMatchStarted(config);
        }

        private void OnError(string message)
        {
            _btnConfirm.SetEnabled(true);
            Debug.LogWarning($"[StageSelect] PvP error: {message}");
        }

        private void Update()
        {
            // Marshals hub events onto the main thread (PvP mode only).
            _lobby?.Pump();
        }

        private void OnDisable()
        {
            if (_lobby != null)
            {
                _lobby.MatchStarted -= OnMatchStarted;
                _lobby.Error        -= OnError;
            }
        }

        private void SelectStage(string name, VisualElement root)
        {
            _selectedArena = name;

            foreach (var card in root.Q<VisualElement>("stage-grid").Children())
            {
                card.RemoveFromClassList("stage-card--selected");
                if (card.name == $"stage-{name}")
                    card.AddToClassList("stage-card--selected");
            }

            _btnConfirm.style.display = DisplayStyle.Flex;
        }
    }
}
