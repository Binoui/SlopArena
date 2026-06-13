#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Aimed charge state — locks movement, shows a ground-projected cone indicator,
/// plays a looping charge animation, then fires the attack on release.
///
/// Modular: configured via AimedChargeData from the ability definition.
/// Reusable for any ability that needs a charge-aim-release pattern.
///
/// Flow:
///   Enter → charge loop anim + cone visible
///   RMB release → ExecuteSlot → C# FSM transitions to "attack"
///   Exit → hide cone
/// </summary>
public sealed partial class AimedChargeState : State
{
    private AimedChargeData _config;
    private int _slotIndex;
    /// <summary>
    /// direction du triangle (yaw, radians)
    /// </summary>
    private float _aimYaw;
    /// <summary>
    /// hauteur du sol projeté
    /// </summary>
    private float _groundY;
    private int _chargeTicks;
    private bool _attackFired;
    private MeshInstance3D? _coneMesh;
    private bool _airborne;

    public AimedChargeState()
    {
        AnimationName = "rmb_loop"; // default, overridden by Configure
        CanMove = false;
    }

    /// <summary>
    /// Configure with data from the ability definition.
    /// Must be called before this state is entered.
    /// </summary>
    public void Configure(AimedChargeData config, int slotIndex, bool airborne, float aimYaw)
    {
        _config = config;
        _slotIndex = slotIndex;
        _airborne = airborne;
        _aimYaw = aimYaw;
        AnimationName = config.ChargeAnimName;
    }

    public override void Enter()
    {
        _chargeTicks = 0;
        _attackFired = false;
        Player.SetModelEmission(new Color(1.0f, 0.5f, 0.1f)); // Orange
        base.Enter(); // plays charge loop anim
        ShowCone();
    }

    public override void Exit()
    {
        Player.ClearModelEmission();
        base.Exit();
        HideCone();
    }

    public override void OnProcess(float delta)
    {
        if (_attackFired) return;

        _chargeTicks++;

        // Sync charge progress to CharacterState so hitbox can scale
        ref var state = ref Player.GetState();
        state.ChargeTicks = (ushort)_chargeTicks;

        // Clamp charge ticks to MaxChargeTicks for scaling
        if (_config.MaxChargeTicks > 0 && _chargeTicks > _config.MaxChargeTicks)
            _chargeTicks = _config.MaxChargeTicks;

        // Detect RMB release → fire attack
        if (!Input.IsMouseButtonPressed(MouseButton.Right))
        {
            FireAttack();
        }
    }

    private void ShowCone()
    {
        if (_coneMesh != null) return;

        float halfAngle = Mathf.DegToRad(_config.ConeAngle) * 0.5f;
        float range = _config.ConeRange;

        // Find ground Y: raycast from above player straight down
        _groundY = Player.GlobalPosition.Y - 0.5f; // fallback: just below player
        var space = Player.GetWorld3D().DirectSpaceState;
        var query = new PhysicsRayQueryParameters3D
        {
            From = Player.GlobalPosition + (Vector3.Up * 10f),
            To = Player.GlobalPosition + (Vector3.Down * 20f),
            CollideWithAreas = false,
            CollideWithBodies = true,
        };
        var hit = space.IntersectRay(query);
        if (hit.Count > 0 && hit.ContainsKey("position"))
            _groundY = ((Vector3)hit["position"]).Y;
        else
            _groundY = Player.GlobalPosition.Y - 0.5f;

        // Build a simple triangle: tip at player, projected on ground
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Tip at player position
        st.AddVertex(Vector3.Zero);
        // Left edge
        st.AddVertex(new Vector3(-Mathf.Sin(halfAngle) * range, 0f, Mathf.Cos(halfAngle) * range));
        // Right edge
        st.AddVertex(new Vector3(Mathf.Sin(halfAngle) * range, 0f, Mathf.Cos(halfAngle) * range));

        // Single triangle (tip, left, right)
        st.AddIndex(0);
        st.AddIndex(1);
        st.AddIndex(2);

        // Material (semi-transparent orange)
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(1f, 0.5f, 0f, 0.25f);
        mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;

        // Create from surface tool
        var mesh = st.Commit();
        if (mesh != null)
            mesh.SurfaceSetMaterial(0, mat);

        _coneMesh = new MeshInstance3D();
        _coneMesh.Mesh = mesh;
        _coneMesh.Name = "ConeIndicator";

        // Position at player's feet on the ground, facing player direction
        Player.AddChild(_coneMesh);
        UpdateConePosition();
    }

    private void UpdateConePosition()
    {
        if (_coneMesh == null) return;

        // Follow player position at ground level
        Vector3 pos = Player.GlobalPosition;
        pos.Y = _groundY + 0.05f; // slightly above ground to avoid z-fighting
        _coneMesh.GlobalPosition = pos;

        // Face player's aiming direction
        _coneMesh.GlobalRotation = new Vector3(0f, _aimYaw, 0f);
    }

    private void HideCone()
    {
        if (_coneMesh == null) return;
        _coneMesh.QueueFree();
        _coneMesh = null;
    }

    private void FireAttack()
    {
        if (_attackFired) return;
        _attackFired = true;

        // Determine if fully charged
        _ = _config.MaxChargeTicks > 0 && _chargeTicks >= _config.MaxChargeTicks;

        // Signal the sim to start the attack via pending slot press
        // _slotIndex is 0-5, map to 1-6 for ActiveSlot
        Player._pendingSlotPress = (byte)(_slotIndex + 1);
    }
}
