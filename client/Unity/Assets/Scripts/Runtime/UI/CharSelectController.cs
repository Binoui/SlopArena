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
            // Host-only: show Start Match button (enabled when all locked in, min 2)
            bool isHost = IsLocalHost();
            if (isHost)
            {
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
            _lobby.MatchStarted     += OnMatchStarted;
            _lobby.Error            += OnPvPError;

            _lblPvPStatus.text = "Select your character...";
            RenderRoster();
            UpdateStartMatchButton();
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
            _ = _lobby.SelectCharacterAsync(_selected.ToString());
        }

        private void OnStartMatchClicked()
        {
            _btnStartMatch.SetEnabled(false);
            _lblPvPStatus.text = "Starting match...";
            _ = _lobby.StartMatchAsync();
        }

        private void OnLobbyUpdated(LobbySnapshot snapshot)
        {
            _snapshot = snapshot;
            RenderRoster();
            UpdateStartMatchButton();
        }

        private void OnCharacterSelected(LobbyPlayerInfo player)
        {
            // The LobbyUpdated that follows carries the same info; just re-render.
            RenderRoster();
            UpdateStartMatchButton();
        }

        private async void OnMatchStarted(MatchStartedConfig config)
        {
            Debug.Log($"[CharSelect] Match started: {config.Players.Count} players, port={config.MatchPort}, arena={config.ArenaName}.");

            // Find the local player in the roster (by SteamId). The master
            // server assigned entity IDs 1..N by join order (issue #35); the
            // game server spawns each with the roster's character class, so
            // every client renders the right chars (issue #36).
            var players = config.Players;
            LobbyPlayerInfo? local = null;
            foreach (var p in players)
            {
                if (p.SteamId == ClientSession.SteamId)
                    local = p;
            }

            if (local == null)
            {
                _lblPvPStatus.text = "Match started but you are not in the roster. Returning to server browser.";
                Debug.LogError("[CharSelect] Local player missing from MatchStarted roster.");
                SceneManager.LoadScene("ServerBrowser");
                return;
            }

            // Stash the match config the PvP scene reads on start.
            MatchConfig.Mode = GameMode.PvP;
            MatchConfig.ArenaName = string.IsNullOrEmpty(config.ArenaName) ? "split" : config.ArenaName;
            MatchConfig.ServerPort = config.MatchPort > 0 ? config.MatchPort : MatchConfig.ServerPort;
            // ServerIP is already set (host: localhost, joiner: server browser IP).
            MatchConfig.PlayerClass = ParseClass(local.CharacterSelection, _selected);
            MatchConfig.LocalEntityId = (ulong)(local.EntityId > 0 ? local.EntityId : 1);
            // Every non-local rostered player is an opponent (issue #36).
            // entityId <= 0 means the master never assigned it, so the game
            // server never spawned the entity — skip it.
            MatchConfig.Opponents.Clear();
            foreach (var p in players)
            {
                if (p.SteamId == ClientSession.SteamId) continue;
                if (p.EntityId <= 0) continue;
                MatchConfig.Opponents.Add(new MatchConfig.OpponentInfo(
                    (ulong)p.EntityId,
                    ParseClass(p.CharacterSelection, CharacterClass.Manki)));
            }

            // Tear down the lobby connection — the match is now handed off to
            // the game server (UDP). Leaving the SignalR lobby connected would
            // keep server-side lobby membership alive past match start.
            if (_lobby != null)
            {
                _lobby.LobbyUpdated    -= OnLobbyUpdated;
                _lobby.CharacterSelected -= OnCharacterSelected;
                _lobby.MatchStarted     -= OnMatchStarted;
                _lobby.Error            -= OnPvPError;
                try { await _lobby.LeaveLobbyAsync(); } catch { /* best effort */ }
                await _lobby.DisconnectAsync();
            }
            ClientSession.ActiveLobby = null;
            ClientSession.LobbyRoster = null;

            // Go straight to the PvP arena — the master server already picked
            // the arena and assigned the port, so StageSelect is skipped for
            // online matches (issue #35).
            SceneManager.LoadScene("Arena_PvP");
        }

        private static CharacterClass ParseClass(string? name, CharacterClass fallback)
        {
            if (string.IsNullOrEmpty(name))
                return fallback;
            return System.Enum.TryParse<CharacterClass>(name, ignoreCase: true, out var c) && c != CharacterClass.None
                ? c
                : fallback;
        }

        private void OnPvPError(string message)
        {
            _lblPvPStatus.text = message;
            // Re-enable lock-in on error (e.g. rejected selection)
            _btnLockIn.SetEnabled(!_lockedIn);
        }

        private void OnPvPBackClicked()
        {
            if (_lobby != null)
            {
                _lobby.LobbyUpdated    -= OnLobbyUpdated;
                _lobby.CharacterSelected -= OnCharacterSelected;
                _lobby.MatchStarted     -= OnMatchStarted;
                _lobby.Error            -= OnPvPError;
            }
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
                _lblPvPStatus.text = "All players locked in. Host can start.";
            }
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
                _lobby.MatchStarted     -= OnMatchStarted;
                _lobby.Error            -= OnPvPError;
            }
        }
    }
}
