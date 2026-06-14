#nullable enable
using Godot;
using System;

/// <summary>
/// Builds and wires all UI elements for the match.
/// Works with both TrainingMatch and PvPMatch via callbacks.
/// </summary>
public class GameUI
{
    private readonly CanvasLayer _canvas;
    private readonly Node3D _world;
    private readonly Node3D _matchNode;
    private readonly PlayerController _player;
    private readonly PlayerController[] _npcs;
    private readonly Action<ulong> _setTarget;
    private readonly Func<ulong> _getTarget;
    private readonly Func<bool> _hasTarget;

    public ActionBarHUD? ActionBar { get; private set; }
    public UnitFrames? UnitFrames { get; private set; }
    public RespawnTimerUI? RespawnTimer { get; private set; }
    public EscapeMenuUI? EscapeMenu { get; private set; }

    public GameUI(CanvasLayer canvas, Node3D worldNode, PlayerController player, PlayerController[] npcs,
        Action<ulong> setTarget, Func<ulong> getTarget, Func<bool> hasTarget)
    {
        _canvas = canvas;
        _world = worldNode;
        _matchNode = worldNode; // match is a child of world
        _player = player;
        _npcs = npcs;
        _setTarget = setTarget;
        _getTarget = getTarget;
        _hasTarget = hasTarget;
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

        _player.OnStateUpdated += (px, pz, py, vx, vz) =>
        {
            float speed = MathF.Sqrt((vx * vx) + (vz * vz));
            label.Text = $"DMG: {_player.GetDamagePercent()}%  SPD: {speed:F1}";
        };
    }

    private void BuildUnitFrames()
    {
        UnitFrames = new UnitFrames { Name = "UnitFrames" };
        _canvas.AddChild(UnitFrames);
        UnitFrames.Setup(_player, _npcs);
    }

    private void BuildRespawnTimer()
    {
        RespawnTimer = new RespawnTimerUI { Name = "RespawnTimerUI" };
        _canvas.AddChild(RespawnTimer);

        _player.OnStateUpdated += (_, _, _, _, _) =>
            RespawnTimer?.UpdateTimer(_player.GetRespawnTimeRemaining());
    }

    private void BuildActionBar()
    {
        var hudScene = GD.Load<PackedScene>("res://Scripts/UI/ActionBarHUD.tscn");
        if (hudScene == null) return;

        ActionBar = hudScene.Instantiate<ActionBarHUD>();
        ActionBar.Name = "ActionBarHUD";
        _canvas.AddChild(ActionBar);
        ActionBar.Setup(_player);
        _player.OnAbilityUsed += (slot) => ActionBar?.FlashSlot(slot);
    }

    private void BuildCamera()
    {
        var camScene = GD.Load<PackedScene>("res://scenes/CameraMount.tscn");
        if (camScene == null) return;

        var cameraMount = camScene.Instantiate<CameraMount>();
        cameraMount.Name = "CameraMount";
        cameraMount.Target = _player;
        _world.AddChild(cameraMount);
        _player.SetCamera(cameraMount);
    }

    private void BuildEscapeMenu()
    {
        EscapeMenu = new EscapeMenuUI { Name = "EscapeMenuUI" };
        _canvas.AddChild(EscapeMenu);
        EscapeMenu.Build();
        EscapeMenu.OnExitLobby += () => _matchNode.GetTree()?.ChangeSceneToFile("res://main.tscn");
        EscapeMenu.OnExitGame += () => _matchNode.GetTree()?.Quit();
    }

    private void BuildTargeting()
    {
        _player.OnTargetNextPressed += () =>
        {
            var viewport = _matchNode.GetViewport();
            if (viewport == null) return;
            var camera = viewport.GetCamera3D();
            if (camera == null) return;
            var center = viewport.GetVisibleRect().Size / 2;
            var from = camera.ProjectRayOrigin(center);
            var dir = camera.ProjectRayNormal(center);
            var query = PhysicsRayQueryParameters3D.Create(from, from + (dir * 100f));
            query.CollisionMask = 2;
            var result = _matchNode.GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (result.Count > 0 && result.ContainsKey("collider"))
            {
                var collider = (Node)result["collider"];
                while (collider != null)
                {
                    if (collider is not CharacterBody3D) { collider = collider.GetParent(); continue; }
                    string name = collider.Name;
                    if (name.StartsWith("NPC_") && int.TryParse(name.AsSpan("NPC_".Length), out int idx))
                    {
                        if (idx >= 0 && idx < _npcs.Length && _npcs[idx]?.IsNpcAlive() == true)
                            _setTarget((ulong)(100 + idx));
                    }
                    else if (name == "Opponent")
                    {
                        _setTarget(2);
                    }
                    break;
                }
            }
        };
    }

    /// <summary>Handle escape key toggle.</summary>
    public void ToggleEscapeMenu()
    {
        if (EscapeMenu == null || _player == null) return;
        EscapeMenu.Toggle(_player);
        _player.IsEscapeMenuOpen = EscapeMenu.IsOpen();
    }
}
