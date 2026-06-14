using Godot;
using System;
using System.Diagnostics;
using SlopArena.Shared;

/// <summary>
/// Entry point: class select → start match → delegate UI to GameUI.
/// </summary>
public partial class Main : Node3D
{
    private Process _serverProcess;
    private TrainingMatch _matchManager = null!;
    private CanvasLayer _canvasLayer = null!;
    private GameUI _gameUI = null!;
    private DebugHitboxDraw _debugDraw = null!;
    private SpellVFXManager _spellVFX = null!;
    private bool _debugHitboxVisible = true;

    public override async void _Ready()
    {
        GD.Print("SlopArena 3D C# Client Started!");
        SetupInputActions();
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);

        _canvasLayer = new CanvasLayer { Name = "CanvasLayer" };
        AddChild(_canvasLayer);

        // Class select
        var classSelect = new ClassSelectUI();
        _canvasLayer.AddChild(classSelect);
        var tcs = new System.Threading.Tasks.TaskCompletionSource<CharacterClass>();
        classSelect.OnClassConfirmed += (cls) => tcs.TrySetResult(cls);
        var selectedClass = await tcs.Task;

        // Crosshair
        CreateCrosshair();

        // Debug draw
        _debugDraw = new DebugHitboxDraw { Name = "DebugHitboxDraw" };
        AddChild(_debugDraw);

        // Spell VFX
        _spellVFX = new SpellVFXManager { Name = "SpellVFX" };
        AddChild(_spellVFX);

        // Server (editor only)
        StartLocalServer(selectedClass);

        // Match
        _matchManager = new TrainingMatch { Name = "MatchManager" };
        AddChild(_matchManager);
        _matchManager.Start(selectedClass, _spellVFX);

        // UI
        _gameUI = new GameUI(_canvasLayer, this, _matchManager);
        _gameUI.Build(_spellVFX);
    }

    public override void _Process(double delta)
    {
        if (_debugDraw != null && _debugHitboxVisible && _matchManager != null)
        {
            var (hitboxes, entities) = _matchManager.GetDebugData();
            _debugDraw.UpdateHitboxes(hitboxes, entities, entities, Vector3.Zero);
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
                if (_matchManager?.Player != null) _matchManager.Player.DebugEmissionEnabled = _debugHitboxVisible;
                foreach (var npc in _matchManager?.NPCs ?? Array.Empty<PlayerController>())
                    if (npc != null) npc.DebugEmissionEnabled = _debugHitboxVisible;
                GD.Print($"Debug Hitboxes: {(_debugHitboxVisible ? "ON" : "OFF")}");
            }
            else if (key.Keycode == Key.F4 || key.PhysicalKeycode == Key.F4)
            {
                _matchManager?.Player?.DebugYPositions();
                foreach (var npc in _matchManager?.NPCs ?? Array.Empty<PlayerController>())
                    npc?.DebugYPositions();
            }
        }
    }

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

    private void StartLocalServer(CharacterClass playerClass)
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
                    Arguments = $"\"{dllPath}\" \"{playerClass}\"",
                    WorkingDirectory = serverDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                }
            };
            _serverProcess.Start();
            GD.Print($"[Main] Local server started (class={playerClass})");
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
