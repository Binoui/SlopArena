using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Shared.AI;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Stage select page (issue #221): a fragment mounted into the
    /// FrontendShell hosts — the stage grid in an intentional scroll region,
    /// the participant summary in the lower-right summary cell, and the
    /// selected-stage label plus start action outside the conversation cell.
    /// The stage registry remains file-driven; this screen owns only
    /// presentation and the existing start-match flow. Training does not
    /// route through stage select (issue #211).
    /// </summary>
    public class StageSelectController : MonoBehaviour, IFrontendPageController
    {
        // Not serialized: the shell injects the per-activation context at
        // mount time (issue #219).
        private FrontendPageContext _context = null!;

        private string _selectedArena = "";
        private Button _btnConfirm;
        private Label _lblSelectedStage;
        private Label _lblWaiting;
        private VisualElement _playerCards;
        private VisualElement _grid;
        private LobbyClient _lobby;

        public void InjectPageContext(FrontendPageContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        private void OnEnable()
        {
            _selectedArena = "";
            _grid = _context.Q<VisualElement>("stage-grid");
            _grid?.Clear();
            _btnConfirm = _context.Q<Button>("btn-confirm");
            _lblSelectedStage = _context.Q<Label>("lbl-selected-stage");
            _lblWaiting = _context.Q<Label>("lbl-waiting");
            _playerCards = _context.Q<VisualElement>("player-cards-area");

            bool isOnline = MatchConfig.Mode == GameMode.PvP;
            bool isHost = !isOnline || ClientSession.IsLobbyHost;
            SetModeChrome(isOnline ? "ONLINE // SELECT STAGE" : "SOLO // SELECT STAGE",
                "STEP 2 OF 2  /  STAGE");
            _context.Q<Label>("subtitle").text = isHost
                ? "CHOOSE YOUR BATTLEGROUND"
                : "THE HOST WILL CHOOSE THE BATTLEGROUND";
            _context.Q<Label>("lbl-host").text = isOnline
                ? (isHost ? "HOST CHOOSES THE STAGE" : "WAITING FOR HOST")
                : "YOU CHOOSE THE STAGE";

            if (_btnConfirm != null)
            {
                _btnConfirm.style.display = DisplayStyle.None;
                _btnConfirm.text = isOnline ? "START MATCH" : "START SOLO";
            }
            if (_lblWaiting != null)
            {
                _lblWaiting.style.display = isHost ? DisplayStyle.None : DisplayStyle.Flex;
                if (!isHost)
                    _lblWaiting.text = "WAITING FOR HOST TO PICK A STAGE";
            }
            RenderPlayerCards();

            string? arenaDir = BakedContentPaths.ArenaDirectory();
            if (arenaDir != null)
                ArenaRegistry.LoadFromDirectory(arenaDir);

            Button firstStageButton = null;
            int stageCount = 0;
            foreach (var arena in ArenaRegistry.All)
            {
                if (arena.Name == "training") continue;
                string? baked = BakedContentPaths.ResolveArena(arena.Name);
                if (baked == null) continue;
                var arenaOpt = ArenaBinaryFormat.LoadFromFile(baked);
                if (arenaOpt is not ArenaDefinition arenaDef) continue;
                if (arenaDef.CollisionTriangles == null || arenaDef.CollisionTriangles.Length == 0) continue;
                if (Resources.Load<GameObject>($"Stages/{arena.Name}") == null) continue;

                string capturedName = arena.Name;
                var card = new Button(() => SelectStage(capturedName))
                {
                    name = $"stage-{arena.Name}"
                };
                card.AddToClassList("stage-card");

                var swatch = new VisualElement();
                swatch.AddToClassList("stage-swatch");
                var preview = Resources.Load<Texture2D>($"UI/Stages/{arena.Name}");
                if (preview != null)
                    swatch.style.backgroundImage = new StyleBackground(preview);
                else if (!string.IsNullOrEmpty(arena.PreviewColor) &&
                    ColorUtility.TryParseHtmlString(arena.PreviewColor, out var swatchColor))
                    swatch.style.backgroundColor = swatchColor;

                var label = new Label(DisplayName(arena));
                label.AddToClassList("stage-name");
                card.Add(swatch);
                card.Add(label);
                card.SetEnabled(isHost);
                _grid?.Add(card);
                firstStageButton ??= card;
                stageCount++;
            }

            if (stageCount == 0)
            {
                _selectedArena = "";
                if (_lblSelectedStage != null)
                    _lblSelectedStage.text = "NO STAGES AVAILABLE";
                if (_lblWaiting != null)
                {
                    _lblWaiting.text = isHost
                        ? "No admitted stages are available. Return to the menu."
                        : "Waiting for host. No stage list is available.";
                    _lblWaiting.style.display = DisplayStyle.Flex;
                }
                _btnConfirm?.SetEnabled(false);
            }
            // Solo keeps its in-flow stage choice through backward navigation;
            // online stage selection derives from server state, never from a
            // restored preference left by a previous flow (issue #213).
            else if (MatchConfig.Mode == GameMode.Solo && !string.IsNullOrEmpty(MatchConfig.ArenaName) &&
                MatchConfig.ArenaName != "training" &&
                _grid?.Q<VisualElement>($"stage-{MatchConfig.ArenaName}") != null)
            {
                SelectStage(MatchConfig.ArenaName);
            }

            var btnBack = _context.Q<Button>("btn-back");
            Action back = BackToFighterSelect;
            if (btnBack != null)
                btnBack.clicked += back;
            Button initial = isHost && firstStageButton != null ? firstStageButton : btnBack;
            if (initial != null)
                MenuNavigation.Configure(_context, initial, back);

            _lobby = isOnline ? ClientSession.ActiveLobby : null;
            if (_lobby != null)
            {
                _lobby.MatchStarted += OnMatchStarted;
                _lobby.Error += OnError;
            }

            if (_btnConfirm != null)
                _btnConfirm.clicked += OnConfirmClicked;
        }


        private void OnDisable()
        {
            if (_lobby != null)
            {
                _lobby.MatchStarted -= OnMatchStarted;
                _lobby.Error -= OnError;
                _lobby = null;
            }
            if (_btnConfirm != null) _btnConfirm.clicked -= OnConfirmClicked;
        }

        private static void SetModeChrome(string title, string progress)
        {
            var titleLabel = FrontendController.CurrentContext?.Q<Label>("title");
            if (titleLabel != null)
                titleLabel.text = title;
            var progressLabel = FrontendController.CurrentContext?.Q<Label>("flow-progress");
            if (progressLabel != null)
                progressLabel.text = progress;
        }

        private static string DisplayName(ArenaDefinition arena)
        {
            if (!string.IsNullOrWhiteSpace(arena.DisplayName))
                return arena.DisplayName;
            if (string.IsNullOrWhiteSpace(arena.Name))
                return "UNKNOWN STAGE";

            string[] words = arena.Name.Split('_');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }
            return string.Join(" ", words);
        }

        private static void BackToFighterSelect()
            => FrontendController.Show(FrontendPage.FighterSelect);

        private void RenderPlayerCards()
        {
            if (_playerCards == null) return;
            _playerCards.Clear();

            if (MatchConfig.Mode == GameMode.Solo)
            {
                _playerCards.Add(BuildPlayerCard(
                    "P1", "YOU", MatchConfig.PlayerClass, "READY", true, true));
                _playerCards.Add(BuildPlayerCard(
                    "P2", "CPU", MatchConfig.SoloBotClass,
                    $"CPU {BotDifficultyProfile.DisplayName(MatchConfig.SoloCpuDifficulty)}",
                    false, false));
                return;
            }

            var players = ClientSession.LobbyRoster?.Players;
            if (players == null) return;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                CharacterClass selectedClass;
                bool hasCharacter = System.Enum.TryParse(player.CharacterSelection, true, out selectedClass);
                _playerCards.Add(BuildPlayerCard(
                    $"P{i + 1}",
                    player.Name,
                    hasCharacter ? selectedClass : (CharacterClass?)null,
                    hasCharacter && player.LockedIn ? "LOCKED" : "WAITING",
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

            var status = new Label(statusText);
            status.AddToClassList("player-card__status");
            status.AddToClassList(statusText == "LOCKED" || statusText == "BOT" || statusText == "READY"
                ? "player-card__status--locked"
                : "player-card__status--picking");
            card.Add(status);
            return card;
        }

        private void OnConfirmClicked()
        {
            if (string.IsNullOrEmpty(_selectedArena))
            {
                if (_lblWaiting != null)
                {
                    _lblWaiting.text = "Choose a stage before starting.";
                    _lblWaiting.style.display = DisplayStyle.Flex;
                }
                return;
            }

            MatchConfig.ArenaName = _selectedArena;
            if (MatchConfig.Mode == GameMode.Solo)
            {
                SceneManager.LoadScene("Arena_Offline");
                return;
            }

            _btnConfirm?.SetEnabled(false);
            if (_lblWaiting != null)
            {
                _lblWaiting.text = "STARTING MATCH…";
                _lblWaiting.style.display = DisplayStyle.Flex;
            }
            if (_lobby == null)
            {
                Debug.LogError("[StageSelect] PvP mode but no lobby connection. Returning to server browser.");
                if (_lblWaiting != null)
                    _lblWaiting.text = "No lobby connection. Returning to server browser.";
                FrontendController.Show(FrontendPage.ServerBrowser);
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
                _lobby.Error -= OnError;
            }
            ClientSession.ApplyMatchStarted(config);
        }

        private void OnError(string message)
        {
            _btnConfirm?.SetEnabled(true);
            if (_lblWaiting != null)
            {
                _lblWaiting.text = $"COULD NOT START MATCH: {message}";
                _lblWaiting.style.display = DisplayStyle.Flex;
            }
            Debug.LogWarning($"[StageSelect] PvP error: {message}");
        }
        private void SelectStage(string name)
        {
            if (MatchConfig.Mode == GameMode.PvP && !ClientSession.IsLobbyHost)
                return;

            _selectedArena = name;
            MatchConfig.ArenaName = name;
            var grid = _grid;
            if (grid != null)
            {
                foreach (var card in grid.Children())
                {
                    card.RemoveFromClassList("stage-card--selected");
                    if (card.name == $"stage-{name}")
                        card.AddToClassList("stage-card--selected");
                }
            }

            ArenaDefinition? arena = ArenaRegistry.Get(name);
            string label = arena.HasValue ? DisplayName(arena.Value) : name.Replace('_', ' ');
            if (_lblSelectedStage != null)
                _lblSelectedStage.text = $"STAGE SELECTED: {label.ToUpperInvariant()}";
            if (_lblWaiting != null)
                _lblWaiting.style.display = DisplayStyle.None;
            if (_btnConfirm != null)
            {
                _btnConfirm.style.display = DisplayStyle.Flex;
                _btnConfirm.SetEnabled(true);
            }
        }
    }
}
