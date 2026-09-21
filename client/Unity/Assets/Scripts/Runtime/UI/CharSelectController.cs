using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Fighter Select page (issue #219): a fragment mounted into the
    /// FrontendShell hosts — portrait grid and selection brief in the body,
    /// participant cards and CPU editing in the lower-right summary, and the
    /// primary action outside the conversation cell. Training, Solo, and PvP
    /// flows are unchanged:
    /// <list type="bullet">
    /// <item><b>Training</b> — single-player: pick a character, click ENTER TRAINING,
    /// launch the training scene directly.</item>
    /// <item><b>Solo</b> — single-player: assign a player and CPU character,
    /// choose a difficulty, then select a stage.</item>
    /// <item><b>PvP</b> — multiplayer via SignalR: all players pick simultaneously,
    /// lock in, and the host starts the match when everyone is locked in (min 2).</item>
    /// </list>
    /// </summary>
    public class CharSelectController : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        private CharacterClass _selected = CharacterClass.None;
        private bool _selectingCpu;
        private readonly List<Button> _gridButtons = new();
        private Button _btnEditPlayer;
        private Button _btnEditCpu;
        private Label _selectionTarget;

        // PvP state
        private LobbyClient _lobby;
        private VisualElement _rosterPanel;
        private Button _btnLockIn;
        private Button _btnStartMatch;
        private Label _lblPvPStatus;
        private bool _lockedIn;
        private LobbySnapshot _snapshot;

        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            _gridButtons.Clear();
            _selectingCpu = false;
            if (_context == null)
            {
                Debug.LogError("[CharSelectController] No page context was injected; fighter select stays blank.");
                enabled = false;
                return;
            }
            var grid = _context.Q<VisualElement>("char-grid");
            grid?.Clear();

            CharacterClass[] classes = MenuRoster.Classes;
            _selected = classes.Contains(MatchConfig.PlayerClass)
                ? MatchConfig.PlayerClass
                : classes.Length > 0 ? classes[0] : CharacterClass.None;
            if (MatchConfig.Mode == GameMode.Solo &&
                classes.Length > 0 &&
                !classes.Contains(MatchConfig.SoloBotClass))
                MatchConfig.SoloBotClass = classes[0];

            // Build portrait cards in the admitted manifest's authored order.
            if (grid != null)
            {
                foreach (var cls in classes)
                {
                    var capturedCls = cls;
                    var btn = new Button(() => SelectCharacter(capturedCls, _context))
                    {
                        name = $"char-{cls}"
                    };
                    btn.AddToClassList("char-card");

                    var portrait = new VisualElement { name = "char-portrait" };
                    portrait.AddToClassList("char-portrait");
                    var texture = Resources.Load<Texture2D>($"UI/Portraits/{cls}");
                    if (texture != null)
                        portrait.style.backgroundImage = new StyleBackground(texture);

                    var name = new Label(cls.ToString().ToUpperInvariant()) { name = "char-card-name" };
                    name.AddToClassList("char-card-name");
                    var markers = new VisualElement { name = "char-markers" };
                    markers.AddToClassList("char-markers");

                    btn.Add(portrait);
                    btn.Add(name);
                    btn.Add(markers);
                    grid.Add(btn);
                    _gridButtons.Add(btn);
                }
            }

            if (_selected != CharacterClass.None)
                SelectCharacter(_selected, _context);

            if (MatchConfig.Mode == GameMode.PvP)
                InitPvP(_context);
            else if (MatchConfig.Mode == GameMode.Solo)
                InitSolo(_context);
            else
            {
                AddTrainingMarker();
                InitTraining(_context);
            }
        }
        private void InitTraining(FrontendPageContext context)
        {
            SetModeChrome(context, "TRAINING // CHOOSE YOUR FIGHTER", "STEP 1 OF 1  /  FIGHTER");
            var rosterMeta = context.Q<Label>("roster-meta");
            if (rosterMeta != null)
                rosterMeta.text = MenuRoster.Classes.Length == 0
                    ? "NO ADMITTED FIGHTERS"
                    : $"{MenuRoster.Classes.Length} FIGHTERS // TRAINING";
            // Training shows one participant card and the single primary
            // action; the CPU editing column and PvP status stay hidden.
            context.Q<VisualElement>("solo-config")?.style.SetDisplay(false);
            context.Q<VisualElement>("pvp-action-area")?.style.SetDisplay(false);

            var selectButton = context.Q<Button>("btn-select");
            if (selectButton != null)
            {
                selectButton.style.display = DisplayStyle.Flex;
                selectButton.text = "ENTER TRAINING";
                selectButton.SetEnabled(MenuRoster.Classes.Length > 0);
                selectButton.clicked += () =>
                {
                    MatchConfig.PlayerClass = _selected;
                    MatchConfig.ArenaName = "training";
                    SceneManager.LoadScene("Arena_Offline");
                };
            }

            _rosterPanel = context.Q<VisualElement>("roster-panel");
            RenderTrainingRoster();

            var btnBack = context.Q<Button>("btn-back");
            Action back = () => FrontendController.Show(FrontendPage.Home);
            if (btnBack != null)
                btnBack.clicked += back;
            ConfigureNavigation(context, MenuRoster.Classes.Length > 0 ? selectButton : null, btnBack, back);

            if (MenuRoster.Classes.Length == 0)
                ShowRosterUnavailable(context, "NO ADMITTED FIGHTERS", "Cooked fighter content is unavailable. Return to the menu.");
        }

        private void InitSolo(FrontendPageContext context)
        {
            SetModeChrome(context, "SOLO // CHOOSE YOUR FIGHTERS", "STEP 1 OF 2  /  FIGHTERS");
            var rosterMeta = context.Q<Label>("roster-meta");
            if (rosterMeta != null)
                rosterMeta.text = MenuRoster.Classes.Length == 0
                    ? "NO ADMITTED FIGHTERS"
                    : $"{MenuRoster.Classes.Length} FIGHTERS // SOLO";
            // Solo shows both participant cards plus CPU editing; the PvP
            // status column stays hidden.
            context.Q<VisualElement>("pvp-action-area")?.style.SetDisplay(false);

            var selectButton = context.Q<Button>("btn-select");
            if (selectButton != null)
            {
                selectButton.style.display = DisplayStyle.Flex;
                selectButton.text = "SELECT STAGE";
                selectButton.SetEnabled(MenuRoster.Classes.Length > 0);
            }

            _rosterPanel = context.Q<VisualElement>("roster-panel");
            RenderSoloRoster();

            var config = context.Q<VisualElement>("solo-config");
            if (config != null)
                config.style.display = DisplayStyle.Flex;

            _btnEditPlayer = context.Q<Button>("btn-edit-player");
            _btnEditCpu = context.Q<Button>("btn-edit-cpu");
            _selectionTarget = context.Q<Label>("selection-target");
            if (_btnEditPlayer != null)
                _btnEditPlayer.clicked += () => SetSelectionTarget(context, selectingCpu: false);
            if (_btnEditCpu != null)
                _btnEditCpu.clicked += () => SetSelectionTarget(context, selectingCpu: true);
            SetSelectionTarget(context, selectingCpu: false);

            var difficultyLabel = context.Q<Label>("solo-difficulty-label");
            MatchConfig.SoloCpuDifficulty = BotDifficultyProfile.Normalize(MatchConfig.SoloCpuDifficulty);
            if (difficultyLabel != null)
                difficultyLabel.text = $"CPU DIFFICULTY: {BotDifficultyProfile.DisplayName(MatchConfig.SoloCpuDifficulty)}";
            foreach (CpuDifficulty difficulty in (CpuDifficulty[])Enum.GetValues(typeof(CpuDifficulty)))
            {
                var capturedDifficulty = difficulty;
                var button = context.Q<Button>($"btn-cpu-difficulty-{difficulty.ToString().ToLowerInvariant()}");
                if (button == null) continue;
                button.clicked += () =>
                {
                    MatchConfig.SoloCpuDifficulty = capturedDifficulty;
                    if (difficultyLabel != null)
                        difficultyLabel.text = $"CPU DIFFICULTY: {BotDifficultyProfile.DisplayName(capturedDifficulty)}";
                    UpdateDifficultyButtons(context);
                    RenderSoloRoster();
                };
            }
            UpdateDifficultyButtons(context);

            if (selectButton != null)
            {
                selectButton.clicked += () =>
                {
                    if (_selected == CharacterClass.None) return;
                    FrontendController.Show(FrontendPage.StageSelect);
                };
            }

            var btnBack = context.Q<Button>("btn-back");
            Action back = () => FrontendController.Show(FrontendPage.Home);
            if (btnBack != null)
                btnBack.clicked += back;
            ConfigureNavigation(context, MenuRoster.Classes.Length > 0
                ? _btnEditPlayer ?? _gridButtons.FirstOrDefault()
                : null, btnBack, back);

            if (MenuRoster.Classes.Length == 0)
                ShowRosterUnavailable(context, "NO ADMITTED FIGHTERS", "Cooked fighter content is unavailable. Return to the menu.");
        }

        private static void SetModeChrome(FrontendPageContext context, string title, string progress)
        {
            var titleLabel = context.Q<Label>("title");
            if (titleLabel != null)
                titleLabel.text = title;
            var progressLabel = context.Q<Label>("flow-progress");
            if (progressLabel != null)
                progressLabel.text = progress;
        }

        private static void ConfigureNavigation(
            FrontendPageContext context,
            Button initial,
            Button backButton,
            Action back)
        {
            if (backButton != null && initial != null)
                MenuNavigation.Configure(context, initial, back);
            else if (backButton != null)
                MenuNavigation.Configure(context, backButton, back);
        }

        private static void ShowRosterUnavailable(FrontendPageContext context, string heading, string detail)
        {
            var nameLabel = context.Q<Label>("char-name");
            if (nameLabel != null)
                nameLabel.text = heading;
            var roleLabel = context.Q<Label>("char-role");
            if (roleLabel != null)
                roleLabel.text = detail;
            var statusLabel = context.Q<Label>("lbl-pvp-status");
            if (statusLabel != null)
                statusLabel.text = detail;
        }

        private static void UpdateDifficultyButtons(FrontendPageContext context)
        {
            CpuDifficulty current = BotDifficultyProfile.Normalize(MatchConfig.SoloCpuDifficulty);
            foreach (CpuDifficulty difficulty in (CpuDifficulty[])Enum.GetValues(typeof(CpuDifficulty)))
            {
                var button = context.Q<Button>($"btn-cpu-difficulty-{difficulty.ToString().ToLowerInvariant()}");
                button?.EnableInClassList("active", difficulty == current);
            }
        }

        private void SetSelectionTarget(FrontendPageContext context, bool selectingCpu)
        {
            if (MatchConfig.Mode != GameMode.Solo)
                return;

            _selectingCpu = selectingCpu;
            _selected = selectingCpu ? MatchConfig.SoloBotClass : MatchConfig.PlayerClass;
            if (_selectionTarget != null)
            {
                string target = selectingCpu ? "CPU" : "PLAYER";
                string fighter = _selected == CharacterClass.None
                    ? "NONE"
                    : _selected.ToString().ToUpperInvariant();
                _selectionTarget.text = $"EDITING {target}  /  {fighter}";
            }
            _btnEditPlayer?.EnableInClassList("active", !selectingCpu);
            _btnEditCpu?.EnableInClassList("active", selectingCpu);
            var botLabel = context.Q<Label>("solo-bot-label");
            if (botLabel != null)
                botLabel.text = $"CPU CHARACTER: {MatchConfig.SoloBotClass.ToString().ToUpperInvariant()}";
            if (_selected != CharacterClass.None)
                UpdateSelectionVisuals(_selected, context);
            RenderSoloRoster();
        }

        private void UpdateSelectionVisuals(CharacterClass cls, FrontendPageContext context)
        {
            foreach (var btn in _gridButtons)
            {
                btn.RemoveFromClassList("char-card--selected");
                if (btn.name == $"char-{cls}")
                    btn.AddToClassList("char-card--selected");
            }

            var nameLabel = context.Q<Label>("char-name");
            if (nameLabel != null)
                nameLabel.text = cls.ToString().ToUpperInvariant();
            var roleLabel = context.Q<Label>("char-role");
            if (roleLabel != null)
                roleLabel.text = MenuRoster.Description(cls);
        }

        private void RenderSoloRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();
            if (MenuRoster.Classes.Length == 0)
                return;
            _rosterPanel.Add(BuildPlayerCard(
                "P1", "YOU", MatchConfig.PlayerClass, "SELECTED", local: true, host: true));
            _rosterPanel.Add(BuildPlayerCard(
                "P2", "CPU", MatchConfig.SoloBotClass,
                $"CPU {BotDifficultyProfile.DisplayName(MatchConfig.SoloCpuDifficulty)}", local: false, host: false));
        }

        private void InitPvP(FrontendPageContext context)
        {
            SetModeChrome(context, "ONLINE // CHOOSE YOUR FIGHTER", "STEP 1 OF 2  /  FIGHTER");
            // PvP shows the participant cards and the lock-in/status actions;
            // the solo continue action stays hidden.
            context.Q<Button>("btn-select")?.style.SetDisplay(false);
            context.Q<VisualElement>("solo-config")?.style.SetDisplay(false);

            var rosterMeta = context.Q<Label>("roster-meta");
            _snapshot = ClientSession.LobbyRoster;
            if (rosterMeta != null)
                rosterMeta.text = MenuRoster.Classes.Length == 0
                    ? "NO ADMITTED FIGHTERS"
                    : $"{MenuRoster.Classes.Length} FIGHTERS // {_snapshot?.Players.Count ?? 0} PLAYERS";
            _rosterPanel   = context.Q<VisualElement>("roster-panel");
            _btnLockIn     = context.Q<Button>("btn-lockin");
            _btnStartMatch = context.Q<Button>("btn-start-match");
            _lblPvPStatus  = context.Q<Label>("lbl-pvp-status");
            _lockedIn = false;

            bool isHost = IsLocalHost();
            ClientSession.IsLobbyHost = isHost;
            if (_btnLockIn != null)
            {
                _btnLockIn.text = "LOCK IN";
                _btnLockIn.SetEnabled(MenuRoster.Classes.Length > 0);
                _btnLockIn.clicked += OnLockInClicked;
            }

            // Host-only: move everyone to stage select once all players lock in.
            if (_btnStartMatch != null)
            {
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
            }

            var btnBack = context.Q<Button>("btn-back");
            if (btnBack != null)
                btnBack.clicked += OnPvPBackClicked;
            ConfigureNavigation(context, _gridButtons.FirstOrDefault() ?? _btnLockIn, btnBack, OnPvPBackClicked);

            _lobby = ClientSession.ActiveLobby;
            if (_lobby == null)
            {
                if (_lblPvPStatus != null)
                    _lblPvPStatus.text = "No lobby connection. Returning to server browser.";
                FrontendController.Show(FrontendPage.ServerBrowser);
                return;
            }

            _lobby.LobbyUpdated    += OnLobbyUpdated;
            _lobby.CharacterSelected += OnCharacterSelected;
            _lobby.StageSelect      += OnStageSelect;
            _lobby.MatchStarted     += OnMatchStarted;
            _lobby.Error            += OnPvPError;

            if (_lblPvPStatus != null)
                _lblPvPStatus.text = MenuRoster.Classes.Length == 0
                    ? "Fighter content is unavailable. Return to the menu."
                    : "Choose your fighter, then lock in.";
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
            FrontendController.Show(FrontendPage.StageSelect);
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
            FrontendController.Show(FrontendPage.LobbyRoom);
        }

        private void RenderTrainingRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();
            _rosterPanel.Add(BuildPlayerCard(
                "P1", "YOU", _selected, "SELECTED", local: true, host: true));
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
            var role = new Label(MatchConfig.Mode == GameMode.PvP
                ? (host ? "HOST" : "PLAYER")
                : (local ? "PLAYER" : "CPU"));
            role.AddToClassList("player-card__host");
            identity.Add(role);
            card.Add(identity);

            var name = new Label(playerName) { enableRichText = false };
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
            foreach (var button in _gridButtons)
                button.Q<VisualElement>("char-markers")?.Clear();

            var card = _context.Q<Button>($"char-{_selected}");
            var markers = card?.Q<VisualElement>("char-markers");
            if (markers == null) return;

            var marker = new Label("P1 SELECTING");
            marker.AddToClassList("char-marker");
            marker.AddToClassList("char-marker--selecting");
            markers.Add(marker);
        }



        private void SelectCharacter(CharacterClass cls, FrontendPageContext context)
        {
            if (cls == CharacterClass.None)
                return;

            _selected = cls;
            if (MatchConfig.Mode == GameMode.Solo && _selectingCpu)
                MatchConfig.SoloBotClass = cls;
            else
                MatchConfig.PlayerClass = cls;

            UpdateSelectionVisuals(cls, context);
            if (MatchConfig.Mode == GameMode.Training && _rosterPanel != null)
            {
                RenderTrainingRoster();
                AddTrainingMarker();
            }
            else if (MatchConfig.Mode == GameMode.Solo && _rosterPanel != null)
            {
                RenderSoloRoster();
                SetSelectionTarget(context, _selectingCpu);
            }
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

                var card = _context.Q<Button>($"char-{cls}");
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
