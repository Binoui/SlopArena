using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SlopArena.Shared;

/// <summary>
/// Entry point: screen navigation controller for SlopArena menus.
/// Manages screen stack and transitions through game flow:
///   MainMenu → (Training | Join | Host) → CharacterSelect → MapSelect → Match
/// </summary>
public partial class Main : Node3D
{
    private Process _serverProcess;
    private PvPMatch _pvpMatch;
    private TrainingMatch _matchManager;
    private CanvasLayer _canvasLayer;
    private GameUI _gameUI;
    private DebugHitboxDraw _debugDraw;
    private SpellVFXManager _spellVFX;
    private bool _debugHitboxVisible = true;

    // Screen navigation
    private readonly Stack<Control> _screenStack = new();

    // Cached packed scenes
    private PackedScene _mainMenuScene = null!;
    private PackedScene _joinServerScene = null!;
    private PackedScene _hostLobbyScene = null!;
    private PackedScene _charSelectScene = null!;
    private PackedScene _mapSelectScene = null!;

    // Flow state
    private string _flowMode; // "training", "join", "host"
    private string _serverIp;
    private CharacterClass _selectedClass;

    public override async void _Ready()
    {
        GD.Print("SlopArena 3D C# Client Started!");
        SetupInputActions();
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);

        // Preload screen scenes
        _mainMenuScene = GD.Load<PackedScene>("res://Scripts/UI/main_menu.tscn");
        _joinServerScene = GD.Load<PackedScene>("res://Scripts/UI/join_server.tscn");
        _hostLobbyScene = GD.Load<PackedScene>("res://Scripts/UI/host_lobby.tscn");
        _charSelectScene = GD.Load<PackedScene>("res://Scripts/UI/character_select.tscn");
        _mapSelectScene = GD.Load<PackedScene>("res://Scripts/UI/map_select.tscn");

        _canvasLayer = new CanvasLayer { Name = "CanvasLayer" };
        AddChild(_canvasLayer);

        // Debug draw
        _debugDraw = new DebugHitboxDraw { Name = "DebugHitboxDraw" };
        AddChild(_debugDraw);

        // Spell VFX
        _spellVFX = new SpellVFXManager { Name = "SpellVFX" };
        AddChild(_spellVFX);

        // Show main menu
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        ShowMainMenu();
    }

    // ═══ SCREEN NAVIGATION ═══

    private void PushScreen(Control screen)
    {
        _screenStack.Push(screen);
        _canvasLayer.AddChild(screen);
    }

    private void PopScreen()
    {
        if (_screenStack.Count > 0)
        {
            var screen = _screenStack.Pop();
            screen.QueueFree();
        }
    }

    private void ClearScreens()
    {
        while (_screenStack.Count > 0)
            PopScreen();
    }

    private void TransitionTo(Control screen)
    {
        ClearScreens();
        PushScreen(screen);
    }

    // ═══ SCREEN CONSTRUCTORS ═══

    private void ShowMainMenu()
    {
        var menu = _mainMenuScene.Instantiate<MainMenuUI>();
        menu.OnTrainingMode += () =>
        {
            _flowMode = "training";
            ShowCharacterSelect();
        };
        menu.OnJoinServer += () => ShowJoinServer();
        menu.OnHostServer += () =>
        {
            _flowMode = "host";
            StartLocalServer(null);
            ShowHostLobby();
        };
        menu.OnQuit += () => GetTree().Quit();
        TransitionTo(menu);
    }

    private void ShowJoinServer()
    {
        var join = _joinServerScene.Instantiate<JoinServerUI>();
        join.OnConnect += (ip) =>
        {
            _flowMode = "join";
            _serverIp = ip;
            ShowCharacterSelect();
        };
        join.OnBack += () => ShowMainMenu();
        TransitionTo(join);
    }

    private void ShowHostLobby()
    {
        var lobby = _hostLobbyScene.Instantiate<HostLobbyUI>();
        lobby.OnStartGame += () => ShowCharacterSelect();
        lobby.OnBack += () =>
        {
            _serverProcess?.Kill();
            _serverProcess = null;
            ShowMainMenu();
        };
        TransitionTo(lobby);
    }

    private void ShowCharacterSelect()
    {
        var charSelect = _charSelectScene.Instantiate<CharacterSelectUI>();
        charSelect.OnCharacterConfirmed += (cls) =>
        {
            _selectedClass = cls;
            ShowMapSelect();
        };
        charSelect.OnBack += () =>
        {
            if (_flowMode == "training")
                ShowMainMenu();
            else if (_flowMode == "join")
                ShowJoinServer();
            else if (_flowMode == "host")
                ShowHostLobby();
        };
        TransitionTo(charSelect);
    }

    private void ShowMapSelect()
    {
        var mapSelect = _mapSelectScene.Instantiate<MapSelectUI>();
        mapSelect.OnMapConfirmed += (arenaName) => StartMatch(arenaName);
        mapSelect.OnBack += () => ShowCharacterSelect();
        TransitionTo(mapSelect);
    }

    // ═══ MATCH START ═══

    private void StartMatch(string arenaName)
    {
        ClearScreens();

        // Crosshair
        CreateCrosshair();

        if (_flowMode == "training")
        {
            _matchManager = new TrainingMatch { Name = "MatchManager" };
            AddChild(_matchManager);
            _matchManager.Start(_selectedClass, _spellVFX);
            _gameUI = new GameUI(_canvasLayer, this, _matchManager.Player, _matchManager.NPCs,
                id => _matchManager.SetTarget(id), () => _matchManager.GetTarget(), () => _matchManager.HasTarget());
            _gameUI.Build(_spellVFX);
        }
        else if (_flowMode == "join" || _flowMode == "host")
        {
            string ip = _flowMode == "join" ? _serverIp : "127.0.0.1";
            int port = 9876;

            var pvp = new PvPMatch { Name = "MatchManager" };
            _pvpMatch = pvp;
            AddChild(pvp);
            pvp.Start(_selectedClass, _spellVFX, ip, port);
            _gameUI = new GameUI(_canvasLayer, this, pvp.Player, Array.Empty<PlayerController>(),
                id => pvp.SetTarget(id), () => pvp.GetTarget(), () => pvp.HasTarget());
            _gameUI.Build(_spellVFX);
        }
    }

    // ═══ INPUT HANDLING ═══

    public override void _Process(double delta)
    {
        if (_debugDraw == null || !_debugHitboxVisible) return;

        if (_matchManager != null)
        {
            var (hitboxes, entities) = _matchManager.GetDebugData();
            _debugDraw.UpdateHitboxes(hitboxes, entities, entities, Vector3.Zero);
        }
        else if (_pvpMatch != null)
        {
            var (hitboxes, local, server) = _pvpMatch.GetDebugData();
            _debugDraw.UpdateHitboxes(hitboxes, local, server, Vector3.Zero);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape)
            {
                _gameUI?.ToggleEscapeMenu();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode == Key.F3 || key.PhysicalKeycode == Key.F3)
            {
                _debugHitboxVisible = !_debugHitboxVisible;
                if (_debugDraw != null) _debugDraw.Visible = _debugHitboxVisible;
                var player = _matchManager?.Player ?? _pvpMatch?.Player;
                if (player != null) player.DebugEmissionEnabled = _debugHitboxVisible;
                if (_matchManager?.NPCs != null)
                    foreach (var npc in _matchManager.NPCs)
                        if (npc != null) npc.DebugEmissionEnabled = _debugHitboxVisible;
                if (_pvpMatch?.Opponent != null)
                    _pvpMatch.Opponent.DebugEmissionEnabled = _debugHitboxVisible;
                GD.Print($"Debug Hitboxes: {(_debugHitboxVisible ? "ON" : "OFF")}");
            }
            else if (key.Keycode == Key.F4 || key.PhysicalKeycode == Key.F4)
            {
                _matchManager?.Player?.DebugYPositions();
                _pvpMatch?.Player?.DebugYPositions();
                if (_matchManager?.NPCs != null)
                    foreach (var npc in _matchManager.NPCs) npc?.DebugYPositions();
                _pvpMatch?.Opponent?.DebugYPositions();
            }
        }
    }

    // ═══ SETUP ═══

    private void SetupInputActions()
    {
        void Add(string n, InputEvent k) { if (!InputMap.HasAction(n)) InputMap.AddAction(n); InputMap.ActionAddEvent(n, k); }
        Add("move_forward", new InputEventKey { PhysicalKeycode = Key.W });
        Add("move_back", new InputEventKey { PhysicalKeycode = Key.S });
        Add("move_left", new InputEventKey { PhysicalKeycode = Key.A });
        Add("move_right", new InputEventKey { PhysicalKeycode = Key.D });
        Add("jump", new InputEventKey { PhysicalKeycode = Key.Space });
        Add("dash", new InputEventKey { PhysicalKeycode = Key.Shift });
        Add("crouch", new InputEventKey { PhysicalKeycode = Key.Ctrl });
        Add("action_lmb", new InputEventMouseButton { ButtonIndex = MouseButton.Left });
        Add("action_rmb", new InputEventMouseButton { ButtonIndex = MouseButton.Right });
        Add("action_q", new InputEventKey { PhysicalKeycode = Key.Q });
        Add("action_e", new InputEventKey { PhysicalKeycode = Key.E });
        Add("action_r", new InputEventKey { PhysicalKeycode = Key.R });
        Add("action_f", new InputEventKey { PhysicalKeycode = Key.F });
        Add("target_next", new InputEventKey { PhysicalKeycode = Key.Tab });
        GD.Print("[Main] Input map initialized");
    }

    private void CreateCrosshair()
    {
        var cross = new ColorRect { Name = "Crosshair", Size = new Vector2(4f, 4f), Color = new Color(1f, 1f, 1f, 0.7f) };
        cross.Position = DisplayServer.WindowGetSize() / 2 - (cross.Size / 2);
        _canvasLayer.AddChild(cross);
        GetTree().Root.SizeChanged += () =>
            cross.Position = DisplayServer.WindowGetSize() / 2 - (cross.Size / 2);
    }

    private void StartLocalServer(CharacterClass? playerClass)
    {
        if (!OS.HasFeature("editor")) return;

        var projectDir = ProjectSettings.GlobalizePath("res://");
        var serverDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, "ServerApp"));
        var dllPath = System.IO.Path.Combine(serverDir, "bin", "Debug", "net8.0", "ServerApp.dll");
        if (System.IO.File.Exists(dllPath))
        {
            _serverProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{dllPath}\" \"{playerClass ?? CharacterClass.Manki}\"",
                    WorkingDirectory = serverDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                }
            };
            _serverProcess.Start();
            GD.Print($"[Main] Local server started (class={playerClass ?? CharacterClass.Manki})");
        }
        else
        {
            GD.PrintErr($"[Main] Server DLL not found at {dllPath}");
        }
    }

    public override void _ExitTree()
    {
        _serverProcess?.Kill();
    }
}
