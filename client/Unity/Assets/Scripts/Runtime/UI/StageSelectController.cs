using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    /// <summary>
    /// Stage select for Training and PvP. The stage registry remains file-driven;
    /// this screen owns only presentation and the existing start-match flow.
    /// </summary>
    public class StageSelectController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private string _selectedArena = "";
        private Button _btnConfirm;
        private Label _lblSelectedStage;
        private Label _lblWaiting;
        private VisualElement _playerCards;
        private LobbyClient _lobby;

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            var grid = root.Q<VisualElement>("stage-grid");
            _btnConfirm = root.Q<Button>("btn-confirm");
            _lblSelectedStage = root.Q<Label>("lbl-selected-stage");
            _lblWaiting = root.Q<Label>("lbl-waiting");
            _playerCards = root.Q<VisualElement>("player-cards-area");
            var btnBack = root.Q<Button>("btn-back");

            bool isHost = MatchConfig.Mode != GameMode.PvP || ClientSession.IsLobbyHost;
            _btnConfirm.style.display = DisplayStyle.None;
            _lblWaiting.style.display = isHost ? DisplayStyle.None : DisplayStyle.Flex;
            root.Q<Label>("lbl-host").text = isHost
                ? "HOST PICKS THE BATTLEGROUND"
                : "WAITING FOR HOST";
            RenderPlayerCards();

            string? arenaDir = BakedContentPaths.ArenaDirectory();
            if (arenaDir != null)
                ArenaRegistry.LoadFromDirectory(arenaDir);

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
                var card = new Button(() => SelectStage(capturedName, root))
                {
                    name = $"stage-{arena.Name}"
                };
                card.AddToClassList("stage-card");

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

            _lobby = MatchConfig.Mode == GameMode.PvP ? ClientSession.ActiveLobby : null;
            if (_lobby != null)
            {
                _lobby.MatchStarted += OnMatchStarted;
                _lobby.Error += OnError;
            }

            _btnConfirm.clicked += OnConfirmClicked;
            btnBack.clicked += () => SceneManager.LoadScene("CharSelect");
        }

        private void RenderPlayerCards()
        {
            if (_playerCards == null) return;
            _playerCards.Clear();

            if (MatchConfig.Mode is GameMode.Training or GameMode.Solo)
            {
                _playerCards.Add(BuildPlayerCard(
                    "P1", "YOU", MatchConfig.PlayerClass, "READY", true, true));
                _playerCards.Add(BuildPlayerCard(
                    "P2", MatchConfig.Mode == GameMode.Solo ? "CPU" : "TRAINING BOT",
                    MatchConfig.Mode == GameMode.Solo ? MatchConfig.SoloBotClass : CharacterClass.FightGuy,
                    MatchConfig.Mode == GameMode.Solo ? $"CPU {MatchConfig.SoloCpuLevel}" : "BOT",
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
            if (string.IsNullOrEmpty(_selectedArena)) return;
            MatchConfig.ArenaName = _selectedArena;

            if (MatchConfig.Mode is GameMode.Training or GameMode.Solo)
            {
                SceneManager.LoadScene("Arena_Offline");
                return;
            }

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
                _lobby.Error -= OnError;
            }
            ClientSession.ApplyMatchStarted(config);
        }

        private void OnError(string message)
        {
            _btnConfirm.SetEnabled(true);
            Debug.LogWarning($"[StageSelect] PvP error: {message}");
        }

        private void Update() => _lobby?.Pump();

        private void OnDisable()
        {
            if (_lobby != null)
            {
                _lobby.MatchStarted -= OnMatchStarted;
                _lobby.Error -= OnError;
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

            _lblSelectedStage.text = $"STAGE: {name.Replace('_', ' ').ToUpperInvariant()}";
            _lblWaiting.style.display = DisplayStyle.None;
            _btnConfirm.style.display = DisplayStyle.Flex;
        }
    }
}
