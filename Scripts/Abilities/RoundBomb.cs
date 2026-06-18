#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Q — Round Bomb: hold to aim, release to throw a projectile along a parabolic arc.
/// Plays LoopAnimName while aiming, then AnimationNames[0] on throw.
/// Wraps a RoundBombSpec from the character definition.
/// </summary>
public class RoundBomb : Ability
{
    public override string Name => "Round Bomb";
    public override byte SlotNumber { get; set; } = 3;

    private RoundBombSpec Spec => (RoundBombSpec)Data;

    // Draw helpers
    private readonly GroundCircle _circle = new();
    private readonly ArcDrawer _arc = new();

    // Aiming state
    private float _aimYaw;
    private float _targetDistance;
    private float _groundY;
    private bool _fired;
    private Node? _sceneRoot;

    private const float MouseSensitivity = 0.005f;
    private const float DistanceSensitivity = 0.1f;

    public override void OnActivate(PlayerController player)
    {
        _fired = false;

        // Self-setup: camera yaw + π offset (camera looks -Z, server AimYaw=0 means +Z)
        _aimYaw = player.GetCameraYaw() + Mathf.Pi;
        _targetDistance = Spec.ProjectileConfig.MaxRange;
        _sceneRoot = player.GetParent();

        // Transition FSM to aimed_charge for aiming loop animation
        var fsm = player.GetFSM();
        if (fsm != null)
        {
            var chargeState = fsm.GetState<AimedChargeState>("aimed_charge");
            if (chargeState != null)
            {
                chargeState.Configure(Spec.LoopAnimName);
                fsm.TransitionTo("aimed_charge");
            }
        }

        // Find ground Y
        _groundY = player.GlobalPosition.Y - 0.5f;
        var space = player.GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid> { player.GetRid() };
        var query = new PhysicsRayQueryParameters3D
        {
            From = player.GlobalPosition + (Vector3.Up * 10f),
            To = player.GlobalPosition + (Vector3.Down * 20f),
            Exclude = exclude,
            CollideWithAreas = false,
            CollideWithBodies = true,
        };
        var hit = space.IntersectRay(query);
        if (hit.Count > 0 && hit.ContainsKey("position"))
            _groundY = ((Vector3)hit["position"]).Y;
        else
            _groundY = player.GlobalPosition.Y - 0.65f;

        // Show indicators
        Node root = _sceneRoot ?? player;
        _circle.Show(root);
        _arc.Show(root);
        UpdateIndicators(player);

        player.SetModelEmission(new Color(1.0f, 0.5f, 0.1f));
    }

    public override AbilityInputState? Tick(PlayerController player, float delta)
    {
        if (_fired) return null;

        if (!Input.IsActionPressed("ability_q"))
        {
            _fired = true;
            UpdateIndicators(player);
            TriggerEffects(player);
            return new AbilityInputState
            {
                ActiveSlot = SlotNumber,
                AimYaw = _aimYaw,
                AimDistance = (ushort)Mathf.Clamp(_targetDistance * 100f, 0f, 6500f),
            };
        }

        UpdateIndicators(player);
        return new AbilityInputState
        {
            AimYaw = _aimYaw,
            AimDistance = (ushort)Mathf.Clamp(_targetDistance * 100f, 0f, 6500f),
        };
    }

    public override void OnDeactivate(PlayerController player)
    {
        _circle.Hide();
        _arc.Hide();
        player.ClearModelEmission();
        // FSM transition handled by _PhysicsProcess reacting to sim's ActionState.Attacking
    }

    public override void OnInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm)
        {
            _aimYaw -= mm.Relative.X * MouseSensitivity;
            var pc = Spec.ProjectileConfig;
            _targetDistance = Mathf.Clamp(
                _targetDistance - mm.Relative.Y * DistanceSensitivity,
                1f, pc.MaxRange);
        }
    }

    private void UpdateIndicators(PlayerController player)
    {
        float aimSin = Mathf.Sin(_aimYaw);
        float aimCos = Mathf.Cos(_aimYaw);

        Vector3 targetPos = player.GlobalPosition;
        targetPos.X += aimSin * _targetDistance;
        targetPos.Z += aimCos * _targetDistance;
        targetPos.Y = _groundY;

        _circle.SetPosition(targetPos);

        Vector3 launchPos = player.GlobalPosition + (Vector3.Up * 1.2f);
        var pc = Spec.ProjectileConfig;
        _arc.Draw(_sceneRoot ?? player, launchPos, targetPos, pc.LaunchAngleDeg, pc.Gravity);
    }
}
