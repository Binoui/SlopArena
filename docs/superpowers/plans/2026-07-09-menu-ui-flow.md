# Menu & UI Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add MainMenu → Lobby → CharSelect → StageSelect flow; both Training and PvP use the same screens; no more hardcoded Inspector char/arena values.

**Architecture:** `MatchConfig` static class carries selections across scene loads. Four new Unity scenes (MainMenu, Lobby, CharSelect, StageSelect), each with a single `UIDocument` + controller MonoBehaviour. `MatchBase` reads `MatchConfig` instead of `[SerializeField]` for char/arena.

**Tech Stack:** Unity UI Toolkit (`UIDocument` + USS/UXML), `SceneManager.LoadScene`, C# static class for cross-scene state, existing `CharacterRegistry`/`ArenaRegistry`/`AbilitySpec` from `SlopArena.Shared`.

---

## File Map

**New — Shared data:**
- `client/Unity/Assets/Scripts/Runtime/UI/MatchConfig.cs` — static class, cross-scene state

**New — Screens:**
- `client/Unity/Assets/Scripts/Runtime/UI/MainMenuController.cs`
- `client/Unity/Assets/Scripts/Runtime/UI/LobbyController.cs`
- `client/Unity/Assets/Scripts/Runtime/UI/CharSelectController.cs`
- `client/Unity/Assets/Scripts/Runtime/UI/StageSelectController.cs`

**New — UXML layouts:**
- `client/Unity/Assets/UI/MainMenu.uxml`
- `client/Unity/Assets/UI/Lobby.uxml`
- `client/Unity/Assets/UI/CharSelect.uxml`
- `client/Unity/Assets/UI/StageSelect.uxml`

**New — Scenes:**
- `client/Unity/Assets/Scenes/MainMenu.unity`
- `client/Unity/Assets/Scenes/Lobby.unity`
- `client/Unity/Assets/Scenes/CharSelect.unity`
- `client/Unity/Assets/Scenes/StageSelect.unity`

**Modified:**
- `src/Shared/AbilitySpec.cs` — add `Description` string field
- `src/Shared/ArenaDefinition.cs` — add `PreviewColor` string field, fill in `BuildAll()`
- `src/Shared/Characters/MankiData.cs` — fill `Description` on each `AbilitySpec`
- `src/Shared/Characters/BunnyData.cs` — same
- `client/Unity/Assets/Scripts/Runtime/Network/NetworkClient.cs` — add `Connect(string ip, int port)` public method
- `client/Unity/Assets/Scripts/Runtime/World/MatchBase.cs` — remove `_playerClass`/`_arenaName` SerializeFields, read from `MatchConfig`
- `client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs` — call `_networkClient.Connect(MatchConfig.ServerIP, MatchConfig.ServerPort)` in `OnMatchStart`
- `ProjectSettings/EditorBuildSettings.asset` — add 4 new scenes, MainMenu first

---

## Task 1: MatchConfig static class + AbilitySpec.Description + ArenaDefinition.PreviewColor

**Files:**
- Create: `client/Unity/Assets/Scripts/Runtime/UI/MatchConfig.cs`
- Modify: `src/Shared/AbilitySpec.cs`
- Modify: `src/Shared/ArenaDefinition.cs`
- Modify: `src/Shared/Characters/MankiData.cs`
- Modify: `src/Shared/Characters/BunnyData.cs`

- [ ] **Create `MatchConfig.cs`**

```csharp
namespace SlopArena.Client.UI
{
    public enum GameMode { Training, PvP }

    public static class MatchConfig
    {
        public static GameMode Mode = GameMode.Training;
        public static SlopArena.Shared.CharacterClass PlayerClass
            = SlopArena.Shared.CharacterClass.Manki;
        public static SlopArena.Shared.CharacterClass OpponentClass
            = SlopArena.Shared.CharacterClass.Manki;
        public static string ArenaName = "training";
        public static bool IsHost = true;
        public static string ServerIP = "127.0.0.1";
        public static int ServerPort = 9876;

        public static void Reset()
        {
            Mode = GameMode.Training;
            PlayerClass = SlopArena.Shared.CharacterClass.Manki;
            OpponentClass = SlopArena.Shared.CharacterClass.Manki;
            ArenaName = "training";
            IsHost = true;
            ServerIP = "127.0.0.1";
            ServerPort = 9876;
        }
    }
}
```

- [ ] **Add `Description` to `AbilitySpec`** — insert after `Name = ""` field (line 34):

```csharp
public string Description = "";
```

- [ ] **Add `PreviewColor` to `ArenaDefinition` struct** — insert after `public string DisplayName;`:

```csharp
/// <summary>Hex color string for stage select card placeholder e.g. "#2a4a2a"</summary>
public string PreviewColor;
```

- [ ] **Fill `PreviewColor` in `ArenaRegistry.BuildAll()`** — add to each `ArenaDefinition` initializer:
  - `pit`: `PreviewColor = "#1a2e1a"`
  - `crossroads` (if present): `PreviewColor = "#1a1a2e"`
  - `training`: `PreviewColor = "#1a1a2e"`

- [ ] **Fill `Description` in `MankiData.cs`** — find each `AbilitySpec` and add one-line description. Example pattern:

```csharp
new AbilitySpec {
    Name = "Grapple",
    Description = "Grab & throw enemy",
    // ... existing fields unchanged
}
```

Fill all 4 of Manki's ability specs (Q/W/E/R). Use the existing `Name` field for reference.

- [ ] **Fill `Description` in `BunnyData.cs`** — same pattern for Bunny's abilities.

- [ ] **Build Shared to verify 0 errors:**
```bash
cd ~/Projects/SlopArena
dotnet build src/Shared/ --nologo
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Commit:**
```bash
git add src/Shared/AbilitySpec.cs src/Shared/ArenaDefinition.cs \
    src/Shared/Characters/MankiData.cs src/Shared/Characters/BunnyData.cs \
    client/Unity/Assets/Scripts/Runtime/UI/MatchConfig.cs
git commit -m "feat: MatchConfig static class, AbilitySpec.Description, ArenaDefinition.PreviewColor"
```

---

## Task 2: NetworkClient.Connect method

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/Network/NetworkClient.cs`

- [ ] **Add public `Connect` method** — insert after `OnDestroy()` (after line 73):

```csharp
/// <summary>
/// Re-point the client at a new server address. Safe to call before first SendInput.
/// Closes the existing socket and opens a fresh one aimed at the new endpoint.
/// </summary>
public void Connect(string ip, int port)
{
    _running = false;
    _receiveThread?.Join(500);
    _udp?.Close();
    _udp = null;
    _connected = false;

    _serverIp = ip;
    _serverPort = port;
    _serverEp = new IPEndPoint(IPAddress.Parse(ip), port);
    CreateSocket();
    _running = true;
    StartReceiveThread();
}
```

- [ ] **Commit:**
```bash
git add client/Unity/Assets/Scripts/Runtime/Network/NetworkClient.cs
git commit -m "feat: NetworkClient.Connect(ip, port) for runtime server address change"
```

---

## Task 3: MatchBase reads MatchConfig

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/World/MatchBase.cs`
- Modify: `client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs`

- [ ] **Remove `_playerClass` and `_arenaName` SerializeFields from `MatchBase`** — delete these two lines:

```csharp
// DELETE both of these:
[SerializeField] protected CharacterClass _playerClass = CharacterClass.Manki;
[SerializeField] protected string _arenaName = "training";
```

- [ ] **Add `using SlopArena.Client.UI;` to `MatchBase.cs`** if not already present.

- [ ] **In `TrainingMatch.OnMatchStart()`** — replace `_arenaName` and `_playerClass` references with `MatchConfig`:

```csharp
// Replace:
string arenaPath = Path.GetFullPath(Path.Combine(
    Application.dataPath, "..", "..", "..", "data", "arenas", _arenaName + ".arena"));
// With:
string arenaPath = Path.GetFullPath(Path.Combine(
    Application.dataPath, "..", "..", "..", "data", "arenas", MatchConfig.ArenaName + ".arena"));

// Replace:
arena = ArenaRegistry.Get(_arenaName);
// With:
arena = ArenaRegistry.Get(MatchConfig.ArenaName);

// Replace:
var playerDef = CharacterRegistry.Get(_playerClass);
// With:
var playerDef = CharacterRegistry.Get(MatchConfig.PlayerClass);
```

- [ ] **In `PvPMatch.OnMatchStart()`** — same replacements for `_arenaName` / `_playerClass`, plus add `Connect` call:

```csharp
// After: _bridge = new NetworkSimulationBridge(_networkClient, PlayerEntityId);
// Add:
_networkClient.Connect(MatchConfig.ServerIP, MatchConfig.ServerPort);
```

Also replace:
```csharp
// Replace:
arena = ArenaRegistry.Get(_arenaName);
// With:
arena = ArenaRegistry.Get(MatchConfig.ArenaName);

// Replace:
var playerDef = CharacterRegistry.Get(_playerClass);
// With:
var playerDef = CharacterRegistry.Get(MatchConfig.PlayerClass);
```

- [ ] **Add log in `MatchBase` at the top of `OnMatchStart` for verification** — each subclass should already log their arena name. If not, add:

```csharp
Debug.Log($"[MatchBase] Starting match: mode={MatchConfig.Mode} char={MatchConfig.PlayerClass} arena={MatchConfig.ArenaName}");
```

- [ ] **Commit:**
```bash
git add client/Unity/Assets/Scripts/Runtime/World/MatchBase.cs \
    client/Unity/Assets/Scripts/Runtime/World/TrainingMatch.cs \
    client/Unity/Assets/Scripts/Runtime/World/PvPMatch.cs
git commit -m "refactor: MatchBase/TrainingMatch/PvPMatch read char+arena from MatchConfig"
```

---

## Task 4: MainMenu scene + UXML + controller

**Files:**
- Create: `client/Unity/Assets/UI/MainMenu.uxml`
- Create: `client/Unity/Assets/Scripts/Runtime/UI/MainMenuController.cs`
- Create: `client/Unity/Assets/Scenes/MainMenu.unity` *(in Unity Editor)*

- [ ] **Create `MainMenu.uxml`:**

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement name="root" class="screen">
        <ui:Label name="title" text="SLOPARENA" class="title" />
        <ui:VisualElement name="menu" class="menu-list">
            <ui:Button name="btn-training" text="TRAINING MODE" class="menu-item menu-item--active" />
            <ui:Button name="btn-multiplayer" text="MULTIPLAYER ›" class="menu-item" />
            <ui:VisualElement name="submenu" class="submenu" style="display: none;">
                <ui:Button name="btn-host" text="HOST GAME" class="menu-item menu-item--sub" />
                <ui:VisualElement name="join-row" class="join-row">
                    <ui:Button name="btn-join" text="JOIN GAME" class="menu-item menu-item--sub" />
                    <ui:TextField name="ip-field" value="127.0.0.1" class="ip-input" />
                </ui:VisualElement>
            </ui:VisualElement>
        </ui:VisualElement>
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Create `MainMenuController.cs`:**

```csharp
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Client.UI;

namespace SlopArena.Client.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private void OnEnable()
        {
            MatchConfig.Reset();
            var root = _uiDocument.rootVisualElement;

            var submenu   = root.Q<VisualElement>("submenu");
            var btnTraining    = root.Q<Button>("btn-training");
            var btnMultiplayer = root.Q<Button>("btn-multiplayer");
            var btnHost   = root.Q<Button>("btn-host");
            var btnJoin   = root.Q<Button>("btn-join");
            var ipField   = root.Q<TextField>("ip-field");

            btnTraining.clicked += () =>
            {
                MatchConfig.Mode   = GameMode.Training;
                MatchConfig.IsHost = true;
                SceneManager.LoadScene("CharSelect");
            };

            btnMultiplayer.clicked += () =>
            {
                bool visible = submenu.style.display == DisplayStyle.Flex;
                submenu.style.display = visible ? DisplayStyle.None : DisplayStyle.Flex;
            };

            btnHost.clicked += () =>
            {
                MatchConfig.Mode     = GameMode.PvP;
                MatchConfig.IsHost   = true;
                MatchConfig.ServerIP = "127.0.0.1";
                SceneManager.LoadScene("Lobby");
            };

            btnJoin.clicked += () =>
            {
                string ip = ipField.value.Trim();
                if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
                MatchConfig.Mode     = GameMode.PvP;
                MatchConfig.IsHost   = false;
                MatchConfig.ServerIP = ip;
                SceneManager.LoadScene("Lobby");
            };
        }
    }
}
```

- [ ] **Create `MainMenu.unity` scene in Unity Editor:**
  1. File → New Scene (Basic)
  2. Delete default directional light if present
  3. Add GameObject → UI → UI Document, name it `MainMenu`
  4. Assign `MainMenu.uxml` to the `UIDocument.Source Asset` slot
  5. Add `MainMenuController` component to the same GameObject
  6. Wire the `UIDocument` field on `MainMenuController`
  7. File → Save Scene As → `Assets/Scenes/MainMenu.unity`

- [ ] **Commit:**
```bash
git add client/Unity/Assets/UI/MainMenu.uxml \
    client/Unity/Assets/Scripts/Runtime/UI/MainMenuController.cs \
    client/Unity/Assets/Scenes/MainMenu.unity \
    client/Unity/Assets/Scenes/MainMenu.unity.meta
git commit -m "feat: MainMenu scene with training/multiplayer/host/join navigation"
```

---

## Task 5: Lobby scene + controller

**Files:**
- Create: `client/Unity/Assets/UI/Lobby.uxml`
- Create: `client/Unity/Assets/Scripts/Runtime/UI/LobbyController.cs`
- Create: `client/Unity/Assets/Scenes/Lobby.unity` *(in Unity Editor)*

- [ ] **Create `Lobby.uxml`:**

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement name="root" class="screen">
        <ui:Label name="title" text="LOBBY" class="title" />
        <ui:VisualElement name="players" class="player-list">
            <ui:VisualElement name="p1-row" class="player-row">
                <ui:Label name="p1-label" text="P1" class="player-id" />
                <ui:Label name="p1-status" text="Connected" class="player-status player-status--connected" />
            </ui:VisualElement>
            <ui:VisualElement name="p2-row" class="player-row">
                <ui:Label name="p2-label" text="P2" class="player-id" />
                <ui:Label name="p2-status" text="Waiting..." class="player-status player-status--waiting" />
            </ui:VisualElement>
        </ui:VisualElement>
        <ui:Button name="btn-start" text="START" class="btn-primary" style="display: none;" />
        <ui:Label name="lbl-waiting" text="Waiting for host to start..." class="subtitle" style="display: none;" />
        <ui:Button name="btn-back" text="← BACK" class="btn-back" />
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Create `LobbyController.cs`:**

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Client.UI;
using SlopArena.Client.Network;

namespace SlopArena.Client.UI
{
    public class LobbyController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private NetworkClient _networkClient;

        private Label _p2Status;
        private Button _btnStart;
        private Label _lblWaiting;

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            _p2Status  = root.Q<Label>("p2-status");
            _btnStart  = root.Q<Button>("btn-start");
            _lblWaiting = root.Q<Label>("lbl-waiting");
            var btnBack = root.Q<Button>("btn-back");

            // Training mode skips lobby entirely — this scene is PvP only.
            // But if somehow reached in Training, go straight to CharSelect.
            if (MatchConfig.Mode == GameMode.Training)
            {
                SceneManager.LoadScene("CharSelect");
                return;
            }

            if (MatchConfig.IsHost)
            {
                _btnStart.style.display  = DisplayStyle.Flex;
                _lblWaiting.style.display = DisplayStyle.None;
                _btnStart.SetEnabled(false); // enabled once P2 connects
                _btnStart.clicked += () => SceneManager.LoadScene("CharSelect");
            }
            else
            {
                _btnStart.style.display  = DisplayStyle.None;
                _lblWaiting.style.display = DisplayStyle.Flex;
            }

            btnBack.clicked += () => SceneManager.LoadScene("MainMenu");

            // Connect client to server
            if (_networkClient != null)
                _networkClient.Connect(MatchConfig.ServerIP, MatchConfig.ServerPort);

            StartCoroutine(PollConnection());
        }

        private IEnumerator PollConnection()
        {
            while (true)
            {
                yield return new WaitForSeconds(1f);
                if (_networkClient == null) yield break;

                bool connected = _networkClient.IsServerConnected;
                _p2Status.text = connected ? "Connected" : "Waiting...";
                _p2Status.RemoveFromClassList(connected ? "player-status--waiting" : "player-status--connected");
                _p2Status.AddToClassList(connected ? "player-status--connected" : "player-status--waiting");

                if (MatchConfig.IsHost)
                    _btnStart.SetEnabled(connected);

                // Client: once server is connected, show a "Ready" button to proceed
                if (!MatchConfig.IsHost && connected)
                {
                    _lblWaiting.text = "Server connected — press Ready when host starts";
                    // Show a proceed button for the client
                    var btnReady = new Button(() => SceneManager.LoadScene("CharSelect"))
                    {
                        text = "READY",
                        name  = "btn-ready"
                    };
                    btnReady.AddToClassList("btn-primary");
                    _uiDocument.rootVisualElement.Q<VisualElement>("root").Add(btnReady);
                    yield break; // stop polling once shown
                }
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }
    }
}
```

- [ ] **Create `Lobby.unity` scene in Unity Editor:**
  1. File → New Scene (Basic)
  2. Add GameObject → UI → UI Document, name it `Lobby`
  3. Assign `Lobby.uxml` to `UIDocument.Source Asset`
  4. Add `LobbyController` component
  5. Wire `UIDocument` and `NetworkClient` fields (add a `NetworkClient` component to the scene on a separate GameObject named `NetworkClient`, wire it)
  6. File → Save Scene As → `Assets/Scenes/Lobby.unity`

- [ ] **Commit:**
```bash
git add client/Unity/Assets/UI/Lobby.uxml \
    client/Unity/Assets/Scripts/Runtime/UI/LobbyController.cs \
    client/Unity/Assets/Scenes/Lobby.unity \
    client/Unity/Assets/Scenes/Lobby.unity.meta
git commit -m "feat: Lobby scene with connection polling and host start flow"
```

---

## Task 6: CharSelect scene + controller

**Files:**
- Create: `client/Unity/Assets/UI/CharSelect.uxml`
- Create: `client/Unity/Assets/Scripts/Runtime/UI/CharSelectController.cs`
- Create: `client/Unity/Assets/Scenes/CharSelect.unity` *(in Unity Editor)*

- [ ] **Create `CharSelect.uxml`:**

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement name="root" class="screen">
        <ui:Label name="title" text="SELECT CHARACTER" class="title" />

        <!-- Panel 1: character grid -->
        <ui:VisualElement name="char-grid" class="char-grid">
            <!-- populated at runtime by CharSelectController -->
        </ui:VisualElement>

        <!-- Panel 2: preview -->
        <ui:VisualElement name="preview-panel" class="preview-panel">
            <!-- Left: Q and W abilities -->
            <ui:VisualElement name="abilities-left" class="abilities-col">
                <ui:VisualElement name="ability-q" class="ability-card">
                    <ui:Label name="ability-q-key" text="Q" class="ability-key" />
                    <ui:VisualElement class="ability-info">
                        <ui:Label name="ability-q-name" text="—" class="ability-name" />
                        <ui:Label name="ability-q-desc" text="" class="ability-desc" />
                    </ui:VisualElement>
                </ui:VisualElement>
                <ui:VisualElement name="ability-w" class="ability-card">
                    <ui:Label name="ability-w-key" text="W" class="ability-key" />
                    <ui:VisualElement class="ability-info">
                        <ui:Label name="ability-w-name" text="—" class="ability-name" />
                        <ui:Label name="ability-w-desc" text="" class="ability-desc" />
                    </ui:VisualElement>
                </ui:VisualElement>
            </ui:VisualElement>

            <!-- Center: name + 3D render texture + tagline -->
            <ui:VisualElement name="model-col" class="model-col">
                <ui:Label name="char-name" text="—" class="char-name" />
                <ui:VisualElement name="model-viewport" class="model-viewport">
                    <ui:VisualElement name="model-image" class="model-image" />
                </ui:VisualElement>
                <ui:Label name="char-tagline" text="" class="char-tagline" />
            </ui:VisualElement>

            <!-- Right: E and R abilities -->
            <ui:VisualElement name="abilities-right" class="abilities-col">
                <ui:VisualElement name="ability-e" class="ability-card">
                    <ui:Label name="ability-e-key" text="E" class="ability-key" />
                    <ui:VisualElement class="ability-info">
                        <ui:Label name="ability-e-name" text="—" class="ability-name" />
                        <ui:Label name="ability-e-desc" text="" class="ability-desc" />
                    </ui:VisualElement>
                </ui:VisualElement>
                <ui:VisualElement name="ability-r" class="ability-card">
                    <ui:Label name="ability-r-key" text="R" class="ability-key" />
                    <ui:VisualElement class="ability-info">
                        <ui:Label name="ability-r-name" text="—" class="ability-name" />
                        <ui:Label name="ability-r-desc" text="" class="ability-desc" />
                    </ui:VisualElement>
                </ui:VisualElement>
            </ui:VisualElement>
        </ui:VisualElement>

        <ui:Button name="btn-select" text="SELECT" class="btn-primary" />
        <ui:Button name="btn-back" text="← BACK" class="btn-back" />
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Create `CharSelectController.cs`:**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client.UI;

namespace SlopArena.Client.UI
{
    public class CharSelectController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        // Off-screen camera + model for 3D preview
        [SerializeField] private Camera _previewCamera;
        [SerializeField] private RenderTexture _previewRenderTexture;
        [SerializeField] private Transform _previewModelRoot;

        private CharacterClass _selected = CharacterClass.Manki;
        private readonly List<Button> _gridButtons = new();
        private GameObject _currentModel;

        private static readonly CharacterClass[] Classes = (CharacterClass[])System.Enum.GetValues(typeof(CharacterClass));
        // Slot index → key label (slots 3–6 = Q, W, E, R)
        private static readonly string[] AbilityKeys = { "Q", "W", "E", "R" };
        private static readonly string[] AbilitySlots = { "ability-q", "ability-w", "ability-e", "ability-r" };

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            var grid = root.Q<VisualElement>("char-grid");

            // Build portrait buttons
            foreach (var cls in Classes)
            {
                var btn = new Button(() => SelectCharacter(cls, root))
                {
                    text = cls.ToString().ToUpper(),
                    name = $"char-{cls}"
                };
                btn.AddToClassList("char-card");
                grid.Add(btn);
                _gridButtons.Add(btn);
            }

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

            // Wire preview camera render texture to model-image element
            if (_previewRenderTexture != null)
            {
                var modelImage = root.Q<VisualElement>("model-image");
                modelImage.style.backgroundImage = Background.FromRenderTexture(_previewRenderTexture);
            }

            SelectCharacter(_selected, root);
        }

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

            // Update name + tagline
            root.Q<Label>("char-name").text = cls.ToString().ToUpper();

            // Load ability data (slots 3–6 = Q W E R in CharacterDefinition)
            var def = CharacterRegistry.Get(cls);
            var slotNames = new[] { "ability-q", "ability-w", "ability-e", "ability-r" };
            int[] slotIndices = { 3, 4, 5, 6 }; // Q=3, W=4(if exists), E=4 or 5, R=5 or 6
            // Use slots 3,4,5,6 — match what's defined in the character def
            for (int i = 0; i < 4; i++)
            {
                var spec = def.GetSlotAbility(i + 3, airborne: false);
                var card = root.Q<VisualElement>(slotNames[i]);
                if (card == null) continue;
                card.Q<Label>($"{slotNames[i]}-name").text = spec?.Name ?? "—";
                card.Q<Label>($"{slotNames[i]}-desc").text = spec?.Description ?? "";
            }

            // Swap 3D model
            SwapPreviewModel(cls, def);
        }

        private void SwapPreviewModel(CharacterClass cls, CharacterDefinition def)
        {
            if (_previewModelRoot == null) return;
            if (_currentModel != null) Destroy(_currentModel);

            // Load model prefab from Resources/Characters/{ClassName}/Model
            // Falls back to a default capsule if not found
            var prefab = Resources.Load<GameObject>($"Characters/{cls}/Model");
            if (prefab != null)
            {
                _currentModel = Instantiate(prefab, _previewModelRoot);
                _currentModel.transform.localPosition = Vector3.zero;
                _currentModel.transform.localRotation = Quaternion.identity;
            }
            else
            {
                // Placeholder: create a primitive capsule
                _currentModel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _currentModel.transform.SetParent(_previewModelRoot, false);
                _currentModel.transform.localPosition = Vector3.zero;
            }
        }

        private void OnDisable()
        {
            if (_currentModel != null) Destroy(_currentModel);
        }
    }
}
```

- [ ] **Create `CharSelect.unity` scene in Unity Editor:**
  1. File → New Scene (Basic)
  2. Add UI Document GameObject → assign `CharSelect.uxml`
  3. Add `CharSelectController` component, wire UIDocument
  4. Create an off-screen `Camera` GameObject at position `(100, 0, 0)`, set Culling Mask to a dedicated layer (e.g. `CharPreview` — create this layer in Project Settings → Tags and Layers)
  5. Create a `RenderTexture` asset (`Assets/UI/CharPreviewRT.renderTexture`, 512×512, Depth 24)
  6. Assign `RenderTexture` to the preview camera's `Target Texture`
  7. Create an empty `PreviewModelRoot` GameObject at `(100, 0, -3)` (in front of preview camera)
  8. Wire `_previewCamera`, `_previewRenderTexture`, `_previewModelRoot` on the controller
  9. File → Save Scene As → `Assets/Scenes/CharSelect.unity`

- [ ] **Commit:**
```bash
git add client/Unity/Assets/UI/CharSelect.uxml \
    client/Unity/Assets/Scripts/Runtime/UI/CharSelectController.cs \
    client/Unity/Assets/Scenes/CharSelect.unity \
    client/Unity/Assets/Scenes/CharSelect.unity.meta
git commit -m "feat: CharSelect scene with 3D preview and ability cards"
```

---

## Task 7: StageSelect scene + controller

**Files:**
- Create: `client/Unity/Assets/UI/StageSelect.uxml`
- Create: `client/Unity/Assets/Scripts/Runtime/UI/StageSelectController.cs`
- Create: `client/Unity/Assets/Scenes/StageSelect.unity` *(in Unity Editor)*

- [ ] **Create `StageSelect.uxml`:**

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:VisualElement name="root" class="screen">
        <ui:Label name="title" text="SELECT STAGE" class="title" />
        <ui:VisualElement name="stage-grid" class="stage-grid">
            <!-- populated at runtime -->
        </ui:VisualElement>
        <ui:Button name="btn-confirm" text="CONFIRM STAGE" class="btn-primary" style="display: none;" />
        <ui:Label name="lbl-waiting" text="Waiting for host to select stage..." class="subtitle" style="display: none;" />
        <ui:Button name="btn-back" text="← BACK" class="btn-back" />
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Create `StageSelectController.cs`:**

```csharp
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using SlopArena.Shared;
using SlopArena.Client.UI;

namespace SlopArena.Client.UI
{
    public class StageSelectController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        private string _selectedArena = "";

        private void OnEnable()
        {
            var root = _uiDocument.rootVisualElement;
            var grid = root.Q<VisualElement>("stage-grid");
            var btnConfirm = root.Q<Button>("btn-confirm");
            var lblWaiting = root.Q<Label>("lbl-waiting");
            var btnBack    = root.Q<Button>("btn-back");

            bool isHost = MatchConfig.Mode == GameMode.Training || MatchConfig.IsHost;

            if (isHost)
            {
                btnConfirm.style.display = DisplayStyle.None; // shown after selection
                lblWaiting.style.display = DisplayStyle.None;
            }
            else
            {
                lblWaiting.style.display = DisplayStyle.Flex;
            }

            // Build stage cards from ArenaRegistry
            foreach (var arena in ArenaRegistry.All)
            {
                string arenaName = arena.Name;
                var card = new Button(() => SelectStage(arenaName, root, btnConfirm))
                {
                    name = $"stage-{arena.Name}"
                };
                card.AddToClassList("stage-card");

                // Color swatch as placeholder thumbnail
                var swatch = new VisualElement();
                swatch.AddToClassList("stage-swatch");
                if (!string.IsNullOrEmpty(arena.PreviewColor))
                    swatch.style.backgroundColor = ParseHex(arena.PreviewColor);

                var label = new Label(arena.DisplayName ?? arena.Name.ToUpper());
                label.AddToClassList("stage-name");

                card.Add(swatch);
                card.Add(label);

                if (!isHost) card.SetEnabled(false);
                grid.Add(card);
            }

            btnConfirm.clicked += () =>
            {
                if (string.IsNullOrEmpty(_selectedArena)) return;
                MatchConfig.ArenaName = _selectedArena;
                string scene = MatchConfig.Mode == GameMode.Training ? "Arena_Offline" : "Arena_PvP";
                SceneManager.LoadScene(scene);
            };

            btnBack.clicked += () => SceneManager.LoadScene("CharSelect");
        }

        private void SelectStage(string name, VisualElement root, Button btnConfirm)
        {
            _selectedArena = name;

            // Update selected styling
            foreach (var card in root.Q<VisualElement>("stage-grid").Children())
            {
                card.RemoveFromClassList("stage-card--selected");
                if (card.name == $"stage-{name}")
                    card.AddToClassList("stage-card--selected");
            }

            btnConfirm.style.display = DisplayStyle.Flex;
        }

        private static UnityEngine.Color ParseHex(string hex)
        {
            hex = hex.TrimStart('#');
            if (hex.Length != 6) return UnityEngine.Color.gray;
            float r = System.Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
            float g = System.Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
            float b = System.Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
            return new UnityEngine.Color(r, g, b);
        }
    }
}
```

- [ ] **Create `StageSelect.unity` scene in Unity Editor:**
  1. File → New Scene (Basic)
  2. Add UI Document GameObject → assign `StageSelect.uxml`
  3. Add `StageSelectController`, wire UIDocument
  4. File → Save Scene As → `Assets/Scenes/StageSelect.unity`

- [ ] **Commit:**
```bash
git add client/Unity/Assets/UI/StageSelect.uxml \
    client/Unity/Assets/Scripts/Runtime/UI/StageSelectController.cs \
    client/Unity/Assets/Scenes/StageSelect.unity \
    client/Unity/Assets/Scenes/StageSelect.unity.meta
git commit -m "feat: StageSelect scene — host picks arena, loads match"
```

---

## Task 8: Register scenes in Build Settings

**Files:**
- Modify: `ProjectSettings/EditorBuildSettings.asset`

- [ ] **In Unity Editor** — File → Build Settings → add scenes in order:
  1. `Scenes/MainMenu` (index 0)
  2. `Scenes/Lobby` (index 1)
  3. `Scenes/CharSelect` (index 2)
  4. `Scenes/StageSelect` (index 3)
  5. `Scenes/Arena_Offline` (index 4)
  6. `Scenes/Arena_PvP` (index 5)
  7. `Scenes/Arena` (existing, keep it)

- [ ] **Commit:**
```bash
git add ProjectSettings/EditorBuildSettings.asset
git commit -m "build: register MainMenu/Lobby/CharSelect/StageSelect in build settings"
```

---

## Task 9: Smoke test

- [ ] **Training path** — Press Play from `MainMenu`:
  - Click `TRAINING MODE`
  - `CharSelect` opens: click Manki, click SELECT
  - `StageSelect` opens: click The Pit, click CONFIRM STAGE
  - `Arena_Offline` loads
  - Console shows: `[MatchBase] Starting match: mode=Training char=Manki arena=pit`
  - Player spawns, moves, NPC attacks back ✓

- [ ] **PvP Host path** — Press Play from `MainMenu`:
  - Click `MULTIPLAYER ›` → `HOST GAME`
  - `Lobby` opens, P2 shows "Waiting..."
  - (Start the `.NET` server: `dotnet run --project src/Server/`)
  - P2 shows "Connected", START button enables
  - Click START → `CharSelect` → `StageSelect` → `Arena_PvP` loads
  - Console shows `[MatchBase] Starting match: mode=PvP char=Manki arena=<selected>` ✓

- [ ] **PvP Join path** — Press Play from `MainMenu`:
  - Click `MULTIPLAYER ›` → enter IP → `JOIN GAME`
  - `Lobby` opens, server connecting
  - Once connected: "Ready" button appears → click → `CharSelect` → `StageSelect` → `Arena_PvP` ✓

- [ ] **Commit final:**
```bash
git add -A
git commit -m "feat: complete menu UI flow — MainMenu/Lobby/CharSelect/StageSelect"
git push origin main
```
