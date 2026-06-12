using Godot;
using System;
using System.Diagnostics;
using SlopArena.Shared;

/// <summary>
/// Thin orchestrator: creates MatchManager + UI, starts local server, delegates game loop.
/// </summary>
public partial class Main : Node3D
{
    private Process? _serverProcess;
    private MatchManager _matchManager = null!;
    private CanvasLayer _canvasLayer = null!;
    private ActionBarHUD _actionBarHUD = null!;
    private UnitFrames _unitFrames = null!;
    private RespawnTimerUI _respawnTimerUI = null!;
    private EscapeMenuUI _escapeMenu = null!;
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

        // Start the local server process
        StartLocalServer();

        // Match manager (spawns everything, runs game loop)
        _matchManager = new MatchManager { Name = "MatchManager" };
        AddChild(_matchManager);
        _matchManager.StartMatch(selectedClass, _spellVFX);

        // HUD label (top-right info)
        var label = new Label { Name = "Label", Size = new Vector2(600f, 200f) };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        _canvasLayer.AddChild(label);
        _matchManager.Player.OnStateUpdated += (px, pz, py, vx, vz) =>
        {
            float speed = MathF.Sqrt((vx * vx) + (vz * vz));
            // Simple HUD: damage % + speed
            label.Text = $"DMG: {_matchManager.Player.GetDamagePercent()}%  SPD: {speed:F1}";
        };

        // Unit frames
        _unitFrames = new UnitFrames { Name = "UnitFrames" };
        _canvasLayer.AddChild(_unitFrames);
        _unitFrames.Setup(_matchManager.Player, _matchManager.NPCs);

        // Respawn timer
        _respawnTimerUI = new RespawnTimerUI { Name = "RespawnTimerUI" };
        _canvasLayer.AddChild(_respawnTimerUI);

        // Action bar
        var hudScene = GD.Load<PackedScene>("res://Scripts/UI/ActionBarHUD.tscn");
        if (hudScene != null)
        {
            _actionBarHUD = hudScene.Instantiate<ActionBarHUD>();
            _actionBarHUD.Name = "ActionBarHUD";
            _canvasLayer.AddChild(_actionBarHUD);
            _actionBarHUD.Setup(_matchManager.Player);
            _matchManager.Player.OnAbilityUsed += (slot) => _actionBarHUD?.FlashSlot(slot);
        }

        // Camera
        var camScene = GD.Load<PackedScene>("res://scenes/CameraMount.tscn");
        if (camScene != null)
        {
            var cameraMount = camScene.Instantiate<CameraMount>();
            cameraMount.Name = "CameraMount";
            cameraMount.Target = _matchManager.Player;
            AddChild(cameraMount);
            _matchManager.Player.SetCamera(cameraMount);
        }

        // Escape menu
        _escapeMenu = new EscapeMenuUI { Name = "EscapeMenuUI" };
        _canvasLayer.AddChild(_escapeMenu);
        _escapeMenu.Build();
        _escapeMenu.OnExitLobby += () => GetTree().ChangeSceneToFile("res://main.tscn");
        _escapeMenu.OnExitGame += () => GetTree().Quit();

        // Tab targeting
        _matchManager.Player.OnTargetNextPressed += () =>
        {
            var viewport = GetViewport();
            if (viewport == null) return;
            var camera = viewport.GetCamera3D();
            if (camera == null) return;
            var center = viewport.GetVisibleRect().Size / 2;
            var from = camera.ProjectRayOrigin(center);
            var dir = camera.ProjectRayNormal(center);
            var query = PhysicsRayQueryParameters3D.Create(from, from + (dir * 100f));
            query.CollisionMask = 2;
            var result = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (result.Count > 0 && result.ContainsKey("collider"))
            {
                var collider = (Node)result["collider"];
                while (collider != null)
                {
                    if (collider is not CharacterBody3D) { collider = collider.GetParent(); continue; }
                    string name = collider.Name;
                    if (name.StartsWith("NPC_") && int.TryParse(name.AsSpan("NPC_".Length), out int idx))
                    {
                        if (idx >= 0 && idx < _matchManager.NPCs.Length && _matchManager.NPCs[idx]?.IsNpcAlive() == true)
                            _matchManager.SetTarget((ulong)(100 + idx));
                    }
                    break;
                }
            }
        };

        // Respawn timer in _Process
        var respawnTimer = 0f;
        SetProcess(true);
        // Override _Process via a simple timer approach isn't great, use a handler
        _matchManager.Player.OnStateUpdated += (_, _, _, _, _) =>
        {
            if (_respawnTimerUI != null)
                _respawnTimerUI.UpdateTimer(_matchManager.Player.GetRespawnTimeRemaining());
        };
    }

    public override void _Process(double delta)
    {
        // Debug hitbox visualization disabled with network server
        // (hitboxes are on the server process, not the client)
        if (_debugDraw != null && _debugHitboxVisible)
        {
            // Show empty data for now
            _debugDraw.UpdateHitboxes(
                new System.Collections.Generic.List<Hitbox>(),
                new System.Collections.Generic.List<(float, float, float, float, float, float, float, bool)>(),
                Vector3.Zero);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape)
            {
                if (_escapeMenu != null && _matchManager?.Player != null)
                {
                    _escapeMenu.Toggle(_matchManager.Player);
                    if (_escapeMenu.IsOpen())
                        _matchManager.Player.IsEscapeMenuOpen = true;
                    else
                        _matchManager.Player.IsEscapeMenuOpen = false;
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (key.Keycode == Key.F3 || key.PhysicalKeycode == Key.F3)
            {
                _debugHitboxVisible = !_debugHitboxVisible;
                if (_debugDraw != null) _debugDraw.Visible = _debugHitboxVisible;
                if (_matchManager?.Player != null) _matchManager.Player.DebugEmissionEnabled = _debugHitboxVisible;
                foreach (var npc in _matchManager?.NPCs ?? Array.Empty<PlayerController>())
                {
                    if (npc != null) npc.DebugEmissionEnabled = _debugHitboxVisible;
                }
                GD.Print($"Debug Hitboxes: {(_debugHitboxVisible ? "ON" : "OFF")}");
            }
        }
    }

    private void SetupInputActions()
    {
        void Add(string n, InputEventKey k) { if (!InputMap.HasAction(n)) InputMap.AddAction(n); InputMap.ActionAddEvent(n, k); }
        Add("move_forward", new InputEventKey { PhysicalKeycode = Key.W });
        Add("move_back", new InputEventKey { PhysicalKeycode = Key.S });
        Add("move_left", new InputEventKey { PhysicalKeycode = Key.A });
        Add("move_right", new InputEventKey { PhysicalKeycode = Key.D });
        Add("jump", new InputEventKey { PhysicalKeycode = Key.Space });
        Add("dash", new InputEventKey { PhysicalKeycode = Key.Shift });
        Add("crouch", new InputEventKey { PhysicalKeycode = Key.C });
        Add("ability_q", new InputEventKey { PhysicalKeycode = Key.Q });
        Add("ability_e", new InputEventKey { PhysicalKeycode = Key.E });
        Add("ability_r", new InputEventKey { PhysicalKeycode = Key.R });
        Add("ability_f", new InputEventKey { PhysicalKeycode = Key.F });
        Add("spellbook_toggle", new InputEventKey { Keycode = Key.B });
        Add("ui_cancel", new InputEventKey { Keycode = Key.Escape });
        Add("trinket", new InputEventKey { Keycode = Key.G });
        Add("tech", new InputEventKey { Keycode = Key.T });
        SettingsUI.LoadBindings();
    }

    private void CreateCrosshair()
    {
        var crosshair = new ColorRect();
        crosshair.Name = "Crosshair";
        crosshair.Size = new Vector2(8f, 8f);
        crosshair.Color = new Color(1f, 1f, 1f, 0.6f);
        crosshair.MouseFilter = Control.MouseFilterEnum.Ignore;
        crosshair.Position = GetViewport().GetVisibleRect().Size / 2 - new Vector2(4f, 4f);
        _canvasLayer.AddChild(crosshair);
    }

    private void StartLocalServer()
    {
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
                    Arguments = $"\"{dllPath}\"",
                    WorkingDirectory = serverDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                }
            };
            _serverProcess.Start();
            GD.Print("[Main] Local server started");
        }
        else
        {
            GD.PrintErr($"[Main] Server DLL not found at {dllPath}");
        }
    }

    public override void _ExitTree()
    {
        _serverProcess?.Kill();
        _serverProcess?.Dispose();
    }
}
