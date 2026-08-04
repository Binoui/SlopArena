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
    /// go to StageSelect. Unchanged from the original flow.</item>
    /// <item><b>PvP</b> — multiplayer via SignalR: all players pick simultaneously,
    /// lock in, and the host starts the match when everyone is locked in (min 2).
    /// Uses the shared <see cref="ClientSession.ActiveLobby"/> connection.</item>
    /// </list>
    /// </summary>
    public class CharSelectController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        // Off-screen camera + model for 3D preview
        [SerializeField] private UnityEngine.Camera _previewCamera;
        [SerializeField] private RenderTexture _previewRenderTexture;
        [SerializeField] private Transform _previewModelRoot;

        private CharacterClass _selected = CharacterClass.Manki;
        private readonly List<Button> _gridButtons = new();
        private GameObject _currentModel;

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
            var values = (CharacterClass[])System.Enum.GetValues(typeof(CharacterClass));
            var playable = new System.Collections.Generic.List<CharacterClass>(values.Length);
            foreach (var c in values)
                if (c != CharacterClass.None) playable.Add(c);
            return playable.ToArray();
        }

        // Slot index → key label: Q=2, E=3, R=4, F=5 (matches GetSlotAbility)
        private static readonly string[] AbilitySlots =
            { "ability-q", "ability-e", "ability-r", "ability-f" };

        private void OnEnable()
        {
            _gridButtons.Clear();
            var root = _uiDocument.rootVisualElement;
            var grid = root.Q<VisualElement>("char-grid");
            grid.Clear();

            // Build portrait buttons for each known character class
            foreach (var cls in Classes)
            {
                var capturedCls = cls;
                var btn = new Button(() => SelectCharacter(capturedCls, root))
                {
                    text = cls.ToString().ToUpper(),
                    name = $"char-{cls}"
                };
                btn.AddToClassList("char-card");
                grid.Add(btn);
                _gridButtons.Add(btn);
            }

            // Wire preview camera render texture to model-image element
            if (_previewRenderTexture != null)
            {
                var modelImage = root.Q<VisualElement>("model-image");
                modelImage.style.backgroundImage = Background.FromRenderTexture(_previewRenderTexture);
            }

            SelectCharacter(_selected, root);

            if (MatchConfig.Mode == GameMode.PvP)
                InitPvP(root);
            else
                InitTraining(root);
        }

        // ── Training (single-player, unchanged) ──

        private void InitTraining(VisualElement root)
        {
            root.Q<VisualElement>("pvp-panel").style.display = DisplayStyle.None;

            root.Q<Button>("btn-select").clicked += () =>
            {
                MatchConfig.PlayerClass = _selected;
                SceneManager.LoadScene("StageSelect");
            };

            root.Q<Button>("btn-back").clicked += () =>
            {
                string prev = MatchConfig.Mode == GameMode.Training ? "MainMenu" : "Lobby";
                SceneManager.LoadScene(prev);
            };
        }

        // ── PvP (multiplayer, issue #34) ──

        private void InitPvP(VisualElement root)
        {
            // Hide single-player SELECT button; show the PvP panel.
            root.Q<Button>("btn-select").style.display = DisplayStyle.None;
            root.Q<VisualElement>("pvp-panel").style.display = DisplayStyle.Flex;

            // Seed the roster from the stashed snapshot so the host check and
            // roster render work before the first LobbyUpdated push arrives.
            _snapshot = ClientSession.LobbyRoster;

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

            Debug.Log($"[CharSelect] LobbyUpdated: {DescribePlayers(snapshot)}");
        }

        private void OnCharacterSelected(LobbyPlayerInfo player)
        {
            // The LobbyUpdated that follows carries the same info; just re-render.
            RenderRoster();
            UpdateStartMatchButton();

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

        private void RenderRoster()
        {
            if (_rosterPanel == null) return;
            _rosterPanel.Clear();

            var players = _snapshot?.Players ?? System.Array.Empty<LobbyPlayerInfo>();

            foreach (var p in players)
            {
                var row = new VisualElement();
                row.AddToClassList("roster-row");

                var name = new Label(p.Name) { name = "roster-name" };
                name.AddToClassList("roster-name");

                if (p.IsHost)
                {
                    var badge = new Label("HOST") { name = "roster-host" };
                    badge.AddToClassList("roster-host");
                    row.Add(badge);
                }

                row.Add(name);

                var charLabel = new Label(p.CharacterSelection ?? "—") { name = "roster-char" };
                charLabel.AddToClassList("roster-char");
                if (!string.IsNullOrEmpty(p.CharacterSelection))
                    charLabel.AddToClassList("roster-char--picked");

                row.Add(charLabel);

                var lockBadge = new Label(p.LockedIn ? "LOCKED" : "PICKING")
                    { name = "roster-lock" };
                lockBadge.AddToClassList("roster-lock");
                lockBadge.AddToClassList(p.LockedIn ? "roster-lock--locked" : "roster-lock--picking");
                row.Add(lockBadge);

                _rosterPanel.Add(row);
            }
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

        private void SelectCharacter(CharacterClass cls, VisualElement root)
        {
            _selected = cls;

            // Highlight selected grid button
            foreach (var btn in _gridButtons)
            {
                btn.RemoveFromClassList("char-card--selected");
                if (btn.name == $"char-{cls}")
                    btn.AddToClassList("char-card--selected");
            }

            // Update name
            root.Q<Label>("char-name").text = cls.ToString().ToUpper();

            // Load ability data: Q=slot2, E=slot3, R=slot4, F=slot5
            var def = CharacterRegistry.Get(cls);
            for (int i = 0; i < AbilitySlots.Length; i++)
            {
                var spec = def.GetSlotAbility(i + 2, airborne: false);
                var card = root.Q<VisualElement>(AbilitySlots[i]);
                if (card == null) continue;
                card.Q<Label>($"{AbilitySlots[i]}-name").text = spec?.Name ?? "—";
                card.Q<Label>($"{AbilitySlots[i]}-desc").text = spec?.Description ?? "";
            }

            SwapPreviewModel(def);
        }

        private void SwapPreviewModel(CharacterDefinition def)
        {
            if (_previewModelRoot == null) return;
            if (_currentModel != null) Destroy(_currentModel);

            var prefab = Resources.Load<GameObject>(def.ModelResourcePath);
            if (prefab != null)
            {
                _currentModel = Instantiate(prefab, _previewModelRoot);
                _currentModel.transform.localPosition = Vector3.zero;
                _currentModel.transform.localRotation = Quaternion.identity;
            }
            else
            {
                _currentModel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _currentModel.transform.SetParent(_previewModelRoot, false);
                _currentModel.transform.localPosition = Vector3.zero;
            }

            // Isolate to CharPreview layer so this model only appears in the preview camera
            int layer = LayerMask.NameToLayer("CharPreview");
            if (layer >= 0)
                foreach (var t in _currentModel.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = layer;
        }

        private void Update()
        {
            // Marshal hub events onto the main thread (PvP mode only).
            _lobby?.Pump();
        }

        private void OnDisable()
        {
            if (_currentModel != null) Destroy(_currentModel);

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
