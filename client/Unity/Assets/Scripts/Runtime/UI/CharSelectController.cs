using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Character select screen (issue #34). Two modes:
    /// <list type="bullet">
    /// <item><b>Training</b> — single-player: pick a character, click SELECT,
    /// launch the training scene directly.</item>
    /// <item><b>PvP</b> — multiplayer via SignalR: all players pick simultaneously,
    /// lock in, and the host starts the match when everyone is locked in (min 2).
    /// Uses the shared <see cref="ClientSession.ActiveLobby"/> connection.</item>
    /// </list>
    /// </summary>
    public class CharSelectController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private CharacterClass _selected = CharacterClass.Manki;
        private readonly List<Button> _gridButtons = new();

        // PvP state
        private LobbyClient _lobby;
        private VisualElement _rosterPanel;
        private Button _btnLockIn;
        private Button _btnStartMatch;
        private Label _lblPvPStatus;
        private bool _lockedIn;
        private LobbySnapshot _snapshot;

        private static readonly CharacterClass[] Classes = GetPlayableClasses();

        private static CharacterClass[] GetPlayableClasses()
        {
            try
            {
                string[] roots =
                {
                    "content-cooked",
                    System.IO.Path.Combine(Application.dataPath, "../../../content-cooked"),
                    System.IO.Path.Combine(Application.streamingAssetsPath, "content-cooked"),
                };
                foreach (string root in roots)
                {
                    string path = System.IO.Path.Combine(root, "roster", "manifest.json");
                    if (System.IO.File.Exists(path))
                        return BuiltInRosterManifestCodec.Load(path).Entries.Select(x => x.Selector).ToArray();
                }
                throw new System.IO.FileNotFoundException("Cooked roster manifest is missing.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharSelect] Built-in roster unavailable: {ex.Message}");
                return System.Array.Empty<CharacterClass>();
            }
        }

        private static readonly System.Collections.Generic.Dictionary<CharacterClass, string> RolePhrases = new()
        {
            { CharacterClass.FightGuy, "DISCIPLINE / BLUE-WHITE KI" },
            { CharacterClass.Manki, "MISCHIEF / EXPLOSIVES" },
            { CharacterClass.Kistu, "BLADE / PRECISION" },
            { CharacterClass.Bonk, "BLADE / HEAVY IMPACT" },
            { CharacterClass.Nilus, "NATURE / CONTROL" },
        };

        private void OnEnable()
        {
            _gridButtons.Clear();
            var root = _uiDocument.rootVisualElement;
            var grid = root.Q<VisualElement>("char-grid");
            grid.Clear();

            // Build large portrait cards from the playable character roster.
            foreach (var cls in Classes)
            {
                var capturedCls = cls;
                var btn = new Button(() => SelectCharacter(capturedCls, root))
                {
                    name = $"char-{cls}"
                };
                btn.AddToClassList("char-card");

                var portrait = new VisualElement { name = "char-portrait" };
                portrait.AddToClassList("char-portrait");
                var texture = Resources.Load<Texture2D>($"UI/Portraits/{cls}");
                if (texture != null)
                    portrait.style.backgroundImage = new StyleBackground(texture);

                var name = new Label(cls.ToString().ToUpper()) { name = "char-card-name" };
                name.AddToClassList("char-card-name");
                var markers = new VisualElement { name = "char-markers" };
                markers.AddToClassList("char-markers");

                btn.Add(portrait);
                btn.Add(name);
                btn.Add(markers);
                grid.Add(btn);
                _gridButtons.Add(btn);
            }

            SelectCharacter(_selected, root);

            if (MatchConfig.Mode == GameMode.PvP)
                InitPvP(root);
            else if (MatchConfig.Mode == GameMode.Solo)
                InitSolo(root);
            else
            {
                AddTrainingMarker();
                InitTraining(root);
            }
        }
        private void InitTraining(VisualElement root)
        {
            root.Q<Label>("roster-meta").text = $"{Classes.Length} FIGHTERS // TRAINING";
            root.Q<VisualElement>("pvp-panel").style.display = DisplayStyle.Flex;
            root.Q<VisualElement>("pvp-action-area").style.display = DisplayStyle.None;
            root.Q<Button>("btn-select").style.display = DisplayStyle.Flex;

            _rosterPanel = root.Q<VisualElement>("roster-panel");
            RenderTrainingRoster();

            root.Q<Button>("btn-select").clicked += () =>
            {
                MatchConfig.PlayerClass = _selected;
                MatchConfig.ArenaName = "training";
                SceneManager.LoadScene("Arena_Offline");
            };

            root.Q<Button>("btn-back").clicked += () =>
            {
                string prev = MatchConfig.Mode == GameMode.Training ? "MainMenu" : "Lobby";
                SceneManager.LoadScene(prev);
            };
        }
        private void InitSolo(VisualElement root)
        {
            root.Q<Label>("roster-meta").text = $"{Classes.Length} FIGHTERS // SOLO";
            root.Q<VisualElement>("pvp-panel").style.display = DisplayStyle.Flex;
            root.Q<VisualElement>("pvp-action-area").style.display = DisplayStyle.None;
            var selectButton = root.Q<Button>("btn-select");
            selectButton.style.display = DisplayStyle.Flex;
            selectButton.text = "START SOLO";

            _rosterPanel = root.Q<VisualElement>("roster-panel");
            RenderSoloRoster();

            var config = root.Q<VisualElement>("solo-config");
            config.style.display = DisplayStyle.Flex;

            var botLabel = root.Q<Label>("solo-bot-label");
            root.Q<Button>("btn-assign-cpu").clicked += () =>
            {
                MatchConfig.SoloBotClass = _selected;
                RenderSoloRoster();
                botLabel.text = $"CPU CHARACTER: {MatchConfig.SoloBotClass.ToString().ToUpperInvariant()}";
            };

            var levelLabel = root.Q<Label>("solo-level-label");
            for (int level = 1; level <= 9; level++)
            {
                int capturedLevel = level;
                root.Q<Button>($"btn-cpu-level-{level}").clicked += () =>
                {
                    MatchConfig.SoloCpuLevel = capturedLevel;
                    levelLabel.text = $"CPU LEVEL: {capturedLevel}";
                };
            }

            selectButton.clicked += () =>
            {
                MatchConfig.PlayerClass = _selected;
                SceneManager.LoadScene("StageSelect");
            };
            root.Q<Button>("btn-back").clicked += () => SceneManager.LoadScene("MainMenu");
        }

        private void RenderSoloRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();
            _rosterPanel.Add(BuildPlayerCard(
                "P1", "YOU", _selected, "SELECTED", local: true, host: true));
            _rosterPanel.Add(BuildPlayerCard(
                "P2", "CPU", MatchConfig.SoloBotClass,
                $"CPU {MatchConfig.SoloCpuLevel}", local: false, host: false));
        }

        private void InitPvP(VisualElement root)
        {
            // Hide single-player SELECT button; show the PvP panel.
            root.Q<Button>("btn-select").style.display = DisplayStyle.None;
            root.Q<VisualElement>("pvp-panel").style.display = DisplayStyle.Flex;

            _snapshot = ClientSession.LobbyRoster;
            root.Q<Label>("roster-meta").text =
                $"{Classes.Length} FIGHTERS // {_snapshot?.Players.Count ?? 0} PLAYERS";
            _rosterPanel   = root.Q<VisualElement>("roster-panel");
            _btnLockIn     = root.Q<Button>("btn-lockin");
            _btnStartMatch = root.Q<Button>("btn-start-match");
            _lblPvPStatus  = root.Q<Label>("lbl-pvp-status");

            _btnLockIn.text = "LOCK IN";
            // Host-only: show SELECT STAGE button (enabled when all locked in, min 2).
            // Clicking it moves everyone to the stage select screen; the host
            // picks the arena there and the match starts from StageSelect.
            bool isHost = IsLocalHost();
            // Roster-based host flag for downstream screens (StageSelect):
            // MatchConfig.IsHost is false for everyone on a dedicated server.
            ClientSession.IsLobbyHost = isHost;
            if (isHost)
            {
                _btnStartMatch.text = "SELECT STAGE";
                _btnStartMatch.style.display = DisplayStyle.Flex;
                _btnStartMatch.SetEnabled(false);
                _btnStartMatch.clicked += OnStartMatchClicked;
            }
            else
            {
                _btnStartMatch.style.display = DisplayStyle.None;
            }

            _btnLockIn.clicked += OnLockInClicked;

            root.Q<Button>("btn-back").clicked += OnPvPBackClicked;

            // Reuse the persistent lobby connection
            _lobby = ClientSession.ActiveLobby;
            if (_lobby == null)
            {
                _lblPvPStatus.text = "No lobby connection. Returning to server browser.";
                SceneManager.LoadScene("ServerBrowser");
                return;
            }

            _lobby.LobbyUpdated    += OnLobbyUpdated;
            _lobby.CharacterSelected += OnCharacterSelected;
            _lobby.StageSelect      += OnStageSelect;
            _lobby.MatchStarted     += OnMatchStarted;
            _lobby.Error            += OnPvPError;

            _lblPvPStatus.text = "Select your character...";
            RenderRoster();
            UpdateStartMatchButton();

            RenderCardMarkers();
            Debug.Log($"[CharSelect] InitPvP: isHost={isHost}, snapshot={_snapshot?.Players.Count ?? 0} players, " +
                $"steamId={ClientSession.SteamId}");
        }

        private bool IsLocalHost()
        {
            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            foreach (var p in players)
            {
                if (p.SteamId == ClientSession.SteamId && p.IsHost)
                    return true;
            }
            return false;
        }

        private void OnLockInClicked()
        {
            if (_lockedIn) return;
            _btnLockIn.SetEnabled(false);
            _btnLockIn.text = "LOCKED";
            Debug.Log($"[CharSelect] Locking in {_selected}");
            _ = _lobby.SelectCharacterAsync(_selected.ToString());
        }

        private void OnStartMatchClicked()
        {
            _btnStartMatch.SetEnabled(false);
            _lblPvPStatus.text = "Selecting stage...";
            _ = _lobby.StartStageSelectAsync();
        }

        private void OnStageSelect(MatchStartingConfig config)
        {
            // Everyone moves to the stage select screen; the host picks the
            // arena there, then the match starts from StageSelect.
            SceneManager.LoadScene("StageSelect");
        }

        private void OnLobbyUpdated(LobbySnapshot snapshot)
        {
            _snapshot = snapshot;
            RenderRoster();
            UpdateStartMatchButton();

            RenderCardMarkers();
            Debug.Log($"[CharSelect] LobbyUpdated: {DescribePlayers(snapshot)}");
        }

        private void OnCharacterSelected(LobbyPlayerInfo player)
        {
            // The LobbyUpdated that follows carries the same info; just re-render.
            RenderRoster();
            UpdateStartMatchButton();

            RenderCardMarkers();
            Debug.Log($"[CharSelect] CharacterSelected: {player.Name} locked={player.LockedIn} char={player.CharacterSelection} host={player.IsHost}");
        }

        private void OnMatchStarted(MatchStartedConfig config)
        {
            Debug.Log($"[CharSelect] Match started: {config.Players.Count} players, port={config.MatchPort}, arena={config.ArenaName}.");

            // Keep the lobby connection alive through the match (issue #40): the
            // results screen + lobby return rely on it. Just unsubscribe the
            // event handlers so nothing fires while in Arena_PvP.
            if (_lobby != null)
            {
                _lobby.LobbyUpdated    -= OnLobbyUpdated;
                _lobby.CharacterSelected -= OnCharacterSelected;
                _lobby.StageSelect      -= OnStageSelect;
                _lobby.MatchStarted     -= OnMatchStarted;
                _lobby.Error            -= OnPvPError;
            }

            ClientSession.ApplyMatchStarted(config);
        }

        private void OnPvPError(string message)
        {
            _lblPvPStatus.text = message;
            Debug.LogWarning($"[CharSelect] PvP error: {message}");
            // Re-enable lock-in on error (e.g. rejected selection)
            _btnLockIn.SetEnabled(!_lockedIn);
            // Re-enable the stage-select button on a rejected transition.
            UpdateStartMatchButton();
        }

        private void OnPvPBackClicked()
        {
            if (_lobby != null)
            {
                _lobby.LobbyUpdated    -= OnLobbyUpdated;
                _lobby.CharacterSelected -= OnCharacterSelected;
                _lobby.StageSelect      -= OnStageSelect;
                _lobby.MatchStarted     -= OnMatchStarted;
                _lobby.Error            -= OnPvPError;
            }
            // The host owns the embedded server subprocess (ADR-0005): backing
            // out of char-select must stop it, or the orphaned server keeps
            // running and stays registered (issue #48). Non-hosts never touch
            // it — MatchConfig.IsHost is the authoritative flag, not the roster.
            if (MatchConfig.IsHost)
                ServerHost.Instance?.Stop();

            // Return to lobby room (connection still alive)
            SceneManager.LoadScene("LobbyRoom");
        }

        private void RenderTrainingRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();
            _rosterPanel.Add(BuildPlayerCard(
                "P1", "YOU", _selected, "SELECTED", local: true, host: true));
            _rosterPanel.Add(BuildPlayerCard(
                "P2", "TRAINING BOT", CharacterClass.FightGuy, "BOT", local: false, host: false));
        }

        private void RenderRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();

            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                CharacterClass selectedClass = CharacterClass.None;
                bool hasCharacter = System.Enum.TryParse(
                    player.CharacterSelection, true, out selectedClass);
                _rosterPanel.Add(BuildPlayerCard(
                    $"P{i + 1}",
                    player.Name,
                    hasCharacter ? selectedClass : (CharacterClass?)null,
                    hasCharacter ? (player.LockedIn ? "LOCKED" : "PICKING") : "WAITING",
                    player.SteamId == ClientSession.SteamId,
                    player.IsHost));
            }
        }

        private VisualElement BuildPlayerCard(
            string playerNumber,
            string playerName,
            CharacterClass? selectedClass,
            string statusText,
            bool local,
            bool host)
        {
            var card = new VisualElement();
            card.AddToClassList("player-card");
            if (local) card.AddToClassList("player-card--local");

            var identity = new VisualElement();
            identity.AddToClassList("player-card__identity");
            var number = new Label(playerNumber);
            number.AddToClassList("player-card__number");
            identity.Add(number);
            var role = new Label(host ? "HOST" : "PLAYER");
            role.AddToClassList("player-card__host");
            identity.Add(role);
            card.Add(identity);

            var name = new Label(playerName);
            name.AddToClassList("player-card__name");
            card.Add(name);

            if (selectedClass.HasValue)
            {
                var portrait = new VisualElement();
                portrait.AddToClassList("player-card__portrait");
                var texture = Resources.Load<Texture2D>($"UI/Portraits/{selectedClass.Value}");
                if (texture != null)
                    portrait.style.backgroundImage = new StyleBackground(texture);
                card.Add(portrait);

                var character = new Label(selectedClass.Value.ToString().ToUpper());
                character.AddToClassList("player-card__character");
                card.Add(character);
            }
            else
            {
                var waiting = new Label("WAITING FOR PLAYER");
                waiting.AddToClassList("player-card__waiting");
                card.Add(waiting);
            }

            var status = new Label(statusText);
            status.AddToClassList("player-card__status");
            status.AddToClassList(statusText == "LOCKED" || statusText == "BOT"
                ? "player-card__status--locked"
                : "player-card__status--picking");
            card.Add(status);
            return card;
        }

        private void UpdateStartMatchButton()
        {
            if (_btnStartMatch == null || _btnStartMatch.style.display == DisplayStyle.None)
                return;

            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            bool canStart = players.Count >= 2 && players.All(p => p.LockedIn);
            _btnStartMatch.SetEnabled(canStart);

            Debug.Log($"[CharSelect] StartMatch check: count={players.Count} locked={string.Join(",", players.Select(p => $"{p.Name}:{p.LockedIn}"))} -> canStart={canStart}");

            if (!canStart)
            {
                int locked = 0;
                foreach (var p in players) if (p.LockedIn) locked++;
                _lblPvPStatus.text = players.Count < 2
                    ? "Waiting for players..."
                    : $"Waiting for locks ({locked}/{players.Count})...";
            }
            else
            {
                _lblPvPStatus.text = "All players locked in. Host can select the stage.";
            }
        }

        private static string DescribePlayers(LobbySnapshot snapshot)
        {
            var parts = snapshot?.Players.Select(p =>
                $"{p.Name}(locked={p.LockedIn},char={p.CharacterSelection ?? "?"},host={p.IsHost},steam={p.SteamId})");
            return $"server={snapshot?.ServerId}, players=[{string.Join(", ", parts ?? System.Array.Empty<string>())}]";
        }

        // ── Shared ──
        private void AddTrainingMarker()
        {
            var card = _uiDocument.rootVisualElement.Q<Button>($"char-{_selected}");
            var markers = card?.Q<VisualElement>("char-markers");
            if (markers == null) return;

            var marker = new Label("P1 SELECTING");
            marker.AddToClassList("char-marker");
            marker.AddToClassList("char-marker--selecting");
            markers.Add(marker);
        }



        private void SelectCharacter(CharacterClass cls, VisualElement root)
        {
            _selected = cls;

            foreach (var btn in _gridButtons)
            {
                btn.RemoveFromClassList("char-card--selected");
                if (btn.name == $"char-{cls}")
                    btn.AddToClassList("char-card--selected");
            }

            root.Q<Label>("char-name").text = cls.ToString().ToUpper();
            root.Q<Label>("char-role").text =
                RolePhrases.TryGetValue(cls, out var role) ? role : "FIGHTER / UNKNOWN";
            if (MatchConfig.Mode == GameMode.Training && _rosterPanel != null)
                RenderTrainingRoster();
            else if (MatchConfig.Mode == GameMode.Solo && _rosterPanel != null)
                RenderSoloRoster();

            }

        private void RenderCardMarkers()
        {
            foreach (var btn in _gridButtons)
                btn.Q<VisualElement>("char-markers")?.Clear();

            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!System.Enum.TryParse<CharacterClass>(player.CharacterSelection, true, out var cls))
                    continue;

                var card = _uiDocument.rootVisualElement.Q<Button>($"char-{cls}");
                var markers = card?.Q<VisualElement>("char-markers");
                if (markers == null) continue;

                var marker = new Label($"P{i + 1} {(player.LockedIn ? "READY" : "SELECTING")}");
                marker.AddToClassList("char-marker");
                marker.AddToClassList(player.LockedIn
                    ? "char-marker--ready"
                    : "char-marker--selecting");
                markers.Add(marker);
            }
        }

        private void Update()
        {
            // Marshal hub events onto the main thread (PvP mode only).
            _lobby?.Pump();
        }

        private void OnDisable()
        {
            if (_lobby != null)
            {
                _lobby.LobbyUpdated    -= OnLobbyUpdated;
                _lobby.CharacterSelected -= OnCharacterSelected;
                _lobby.StageSelect      -= OnStageSelect;
                _lobby.MatchStarted     -= OnMatchStarted;
                _lobby.Error            -= OnPvPError;
            }
        }

    }
}
