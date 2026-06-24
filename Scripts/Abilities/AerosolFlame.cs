#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// RMB — Aerosol + Lighter: hold to charge, release to fire cone flamethrower.
/// Shows a ground-projected cone indicator while held.
/// Reads params from AbilitySpec.Params.
/// </summary>
public class AerosolFlame : Ability
{
    public override string Name => "Aerosol + Lighter";
    public override byte SlotNumber { get; set; } = 2;

    private readonly GroundCone _cone = new();
    private float _aimYaw;
    private float _groundY;
    private int _chargeTicks;
    private bool _fired;

    public override void OnActivate(PlayerController player)
    {
        _fired = false;
        _chargeTicks = 0;
        _aimYaw = player.GlobalRotation.Y;

        // Transition FSM to aimed_charge (blocks movement, plays charge animation)
        var fsm = player.GetFSM();
        if (fsm != null)
        {
            var chargeState = fsm.GetState<AimedChargeState>("aimed_charge");
            if (chargeState != null)
            {
                chargeState.Configure("spell_rmb_charge");
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

        float coneAngle = 60f; // default cone angle
        float coneRange = 5f;  // default cone range
        _cone.Show(player, coneAngle, coneRange, _groundY);
        player.SetModelEmission(new Color(1.0f, 0.5f, 0.1f));
    }

    public override AbilityInputState? Tick(PlayerController player, float delta)
    {
        if (_fired) return null;

        _chargeTicks++;
        int maxCharge = (int)(Data?.Params?.TryGetValue("charge_threshold", out float mc) == true ? mc : 45f);
        if (maxCharge > 0 && _chargeTicks > maxCharge)
            _chargeTicks = maxCharge;

        // Sync charge progress to sim
        ref var state = ref player.GetState();
        state.ChargeTicks = (ushort)_chargeTicks;

        // Update cone position
        _cone.SetPosition(player.GlobalPosition, _aimYaw);

        // Detect RMB release
        if (!Input.IsMouseButtonPressed(MouseButton.Right))
        {
            _fired = true;
            TriggerEffects(player);
            return new AbilityInputState { ActiveSlot = SlotNumber };
        }

        return null; // still charging, no input to send
    }

    public override void OnDeactivate(PlayerController player)
    {
        _cone.Hide();
        player.ClearModelEmission();
        // FSM transition handled by _PhysicsProcess reacting to sim's ActionState.Attacking
    }

    public override void OnInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm)
        {
            _aimYaw -= mm.Relative.X * 0.003f;
        }
    }
}
