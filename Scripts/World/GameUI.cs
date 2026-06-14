#nullable enable
using Godot;
using System;

/// <summary>
/// Builds and wires all UI elements for the match.
/// Extracted from Main.cs to keep the entry point clean.
/// </summary>
public class GameUI
{
    private readonly CanvasLayer _canvas;
    private readonly Node3D _world;
    private readonly TrainingMatch _match;

    public ActionBarHUD? ActionBar { get; private set; }
    public UnitFrames? UnitFrames { get; private set; }
    public RespawnTimerUI? RespawnTimer { get; private set; }
    public EscapeMenuUI? EscapeMenu { get; private set; }

    public GameUI(CanvasLayer canvas, Node3D world, TrainingMatch match)
    {
        _canvas = canvas;
        _world = world;
        _match = match;
    }

    public void Build(SpellVFXManager spellVFX)
    {
        BuildHUD();
        BuildUnitFrames();
        BuildRespawnTimer();
        BuildActionBar();
        BuildCamera();
        BuildEscapeMenu();
        BuildTargeting();
    }

    private void BuildHUD()
    {
        var label = new Label { Name = "HUD", Size = new Vector2(600f, 200f) };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        _canvas.AddChild(label);

        _match.Player.OnStateUpdated += (px, pz, py, vx, vz) =>
        {
            float speed = MathF.Sqrt((vx * vx) + (vz * vz));
            label.Text = $"DMG: {_match.Player.GetDamagePercent()}%  SPD: {speed:F1}";
        };
    }

    private void BuildUnitFrames()
    {
        UnitFrames = new UnitFrames { Name = "UnitFrames" };
        _canvas.AddChild(UnitFrames);
        UnitFrames.Setup(_match.Player, _match.NPCs);
    }

    private void BuildRespawnTimer()
    {
        RespawnTimer = new RespawnTimerUI { Name = "RespawnTimerUI" };
        _canvas.AddChild(RespawnTimer);

        _match.Player.OnStateUpdated += (_, _, _, _, _) =>
            RespawnTimer?.UpdateTimer(_match.Player.GetRespawnTimeRemaining());
    }

    private void BuildActionBar()
    {
        var hudScene = GD.Load<PackedScene>("res://Scripts/UI/ActionBarHUD.tscn");
        if (hudScene == null) return;

        ActionBar = hudScene.Instantiate<ActionBarHUD>();
        ActionBar.Name = "ActionBarHUD";
        _canvas.AddChild(ActionBar);
        ActionBar.Setup(_match.Player);
        _match.Player.OnAbilityUsed += (slot) => ActionBar?.FlashSlot(slot);
    }

    private void BuildCamera()
    {
        var camScene = GD.Load<PackedScene>("res://scenes/CameraMount.tscn");
        if (camScene == null) return;

        var cameraMount = camScene.Instantiate<CameraMount>();
        cameraMount.Name = "CameraMount";
        cameraMount.Target = _match.Player;
        _world.AddChild(cameraMount);
        _match.Player.SetCamera(cameraMount);
    }

    private void BuildEscapeMenu()
    {
        EscapeMenu = new EscapeMenuUI { Name = "EscapeMenuUI" };
        _canvas.AddChild(EscapeMenu);
        EscapeMenu.Build();
        EscapeMenu.OnExitLobby += () => _match.GetTree()?.ChangeSceneToFile("res://main.tscn");
        EscapeMenu.OnExitGame += () => _match.GetTree()?.Quit();
    }

    private void BuildTargeting()
    {
        _match.Player.OnTargetNextPressed += () =>
        {
            var viewport = _match.GetViewport();
            if (viewport == null) return;
            var camera = viewport.GetCamera3D();
            if (camera == null) return;
            var center = viewport.GetVisibleRect().Size / 2;
            var from = camera.ProjectRayOrigin(center);
            var dir = camera.ProjectRayNormal(center);
            var query = PhysicsRayQueryParameters3D.Create(from, from + (dir * 100f));
            query.CollisionMask = 2;
            var result = _match.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (result.Count > 0 && result.ContainsKey("collider"))
            {
                var collider = (Node)result["collider"];
                while (collider != null)
                {
                    if (collider is not CharacterBody3D) { collider = collider.GetParent(); continue; }
                    string name = collider.Name;
                    if (name.StartsWith("NPC_") && int.TryParse(name.AsSpan("NPC_".Length), out int idx))
                    {
                        if (idx >= 0 && idx < _match.NPCs.Length && _match.NPCs[idx]?.IsNpcAlive() == true)
                            _match.SetTarget((ulong)(100 + idx));
                    }
                    break;
                }
            }
        };
    }

    /// <summary>Handle escape key toggle.</summary>
    public void ToggleEscapeMenu()
    {
        if (EscapeMenu == null || _match.Player == null) return;
        EscapeMenu.Toggle(_match.Player);
        _match.Player.IsEscapeMenuOpen = EscapeMenu.IsOpen();
    }
}
