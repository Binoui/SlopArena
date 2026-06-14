#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Godot-side wrapper around Shared/Simulation.
/// Owns a CharacterState, delegates movement logic to Simulation.SimulateTick(),
/// then applies the result to the CharacterBody3D for rendering.
///
/// MoveAndSlide() is called for visual collision (arena walls, slopes).
/// The authoritative position comes from CharacterState, not Godot physics.
///
/// Usage:
///   var moveComp = new MovementComponent(body);
///   moveComp.Setup(characterDef, arenaDef);
///   moveComp.StartDash(dirX, dirZ);
///   moveComp.ApplyJump();
///   moveComp.ApplyKnockback(kbX, kbY, kbZ);
///   // In _PhysicsProcess:
///   moveComp.Tick(inputState);
///   bool grounded = moveComp.State.IsGrounded;
/// </summary>
public class MovementComponent
{
    // ── PUBLIC STATE ──

    /// <summary>
    /// The authoritative character state. Read after Tick() for current frame.
    /// </summary>
    public CharacterState State;

    // ── GODOT-SIDE ACCESSORS (bridge to CharacterState) ──

    public ActionState CurrentState => State.State;
    public bool IsGrounded => State.IsGrounded;
    public float DashCooldownRemaining => State.DashCooldownTicks * Simulation.TickDt;
    public int JumpsLeft => State.JumpsLeft;
    public int AirDodgesLeft => State.AirDodgesLeft;
    public ushort DamagePercent => State.DamagePercent;
    public bool IsInvincible => State.InvincibilityTicks > 0;

    // ── PRIVATE ──

    private readonly CharacterBody3D _body;
    private CharacterDefinition _charDef;
    private ArenaDefinition _arenaDef;
    private bool _initialized = false;

    // ── CONSTRUCTOR ──

    public MovementComponent(CharacterBody3D body)
    {
        _body = body ?? throw new System.ArgumentNullException(nameof(body));
    }

    /// <summary>
    /// Initialize with character definition and arena definition.
    /// Must be called before Tick().
    /// </summary>
    public void Setup(CharacterDefinition charDef, ArenaDefinition arenaDef)
    {
        _charDef = charDef;
        _arenaDef = arenaDef;

        // Initialize state from character definition
        ref var s = ref State;
        s.DamagePercent = 0;
        s.JumpsLeft = charDef.Movement.MaxJumps;
        s.AirDodgesLeft = 1;
        s.IsGrounded = false;
        s.EntityId = 1;

        _initialized = true;
    }

    // ── MAIN UPDATE ──

    /// <summary>
    /// Process one tick: delegates to Simulation.SimulateTick(),
    /// then applies velocity to Godot body and calls MoveAndSlide.
    /// </summary>
    public void Tick(InputState input)
    {
        if (!_initialized) return;

        // Sync Godot position into state at start of tick
        ref var s = ref State;
        s.PX = _body.GlobalPosition.X;
        s.PY = _body.GlobalPosition.Y;
        s.PZ = _body.GlobalPosition.Z;
        s.VX = _body.Velocity.X;
        s.VY = _body.Velocity.Y;
        s.VZ = _body.Velocity.Z;

        // Authoritative simulation
        Simulation.SimulateTick(ref State, _charDef, input, _arenaDef);

        // Apply simulation result to Godot body
        _body.Velocity = new Vector3(State.VX, State.VY, State.VZ);

        // Godot collision/rendering step.
        _body.MoveAndSlide();

        // Sync grounded state from Godot
        s = ref State;
        s.IsGrounded = _body.IsOnFloor();

        // Floor safety
        if (_body.GlobalPosition.Y < _arenaDef.FloorHeight - 1f && State.IsGrounded)
        {
            _body.GlobalPosition = new Vector3(_body.GlobalPosition.X, _arenaDef.FloorHeight + 0.5f, _body.GlobalPosition.Z);
            _body.Velocity = new Vector3(_body.Velocity.X, 0f, _body.Velocity.Z);
            s = ref State;
            s.PY = _arenaDef.FloorHeight + 0.5f;
            s.VY = 0f;
        }
    }

    // ── PUBLIC ACTION METHODS ──

    /// <summary>
    /// Start a dash (ground or air). Direction is normalized automatically.
    /// Pass (0, 0) to dash in facing direction.
    /// </summary>
    public void StartDash(float dirX, float dirZ)
    {
        Simulation.StartDash(ref State, _charDef.Movement, dirX, dirZ);
    }

    /// <summary>
    /// Apply an upward jump force.
    /// </summary>
    public void ApplyJump()
    {
        Simulation.ApplyJump(ref State, _charDef.Movement.JumpForce);
    }

    /// <summary>
    /// Apply knockback force (already scaled by damage%).
    /// Interrupts dash/air-dodge.
    /// Also sets body velocity immediately so knockback applies this frame.
    /// </summary>
    public void ApplyKnockback(float kbX, float kbY, float kbZ)
    {
        Simulation.ApplyKnockback(ref State, kbX, kbY, kbZ);
        _body.Velocity = new Vector3(State.KVX, State.KVY, State.KVZ);
    }

    /// <summary>
    /// Apply damage and increase damage percentage.
    /// </summary>
    public void ApplyDamage(float damage)
    {
        Simulation.ApplyDamage(ref State, damage);
    }

    /// <summary>
    /// Tech roll: clears knockback, small burst in last input direction.
    /// </summary>
    public void DoTechRoll()
    {
        Simulation.DoTechRoll(ref State);
    }

    /// <summary>
    /// Set combo stage and attack lock ticks (called by PlayerController after ExecuteSlot).
    /// Note: uses ref to modify the struct in-place (CharacterState is a value type).
    /// </summary>
    public void SetComboState(byte comboStage, ushort chainWindowTicks, ushort selfLockTicks)
    {
        ref var s = ref State;
        s.ComboStage = comboStage;
        s.ComboTimerTicks = chainWindowTicks;
        s.AnimLockTicks = selfLockTicks;
    }

    /// <summary>
    /// Check if the entity is currently in knockback.
    /// </summary>
    public bool IsInKnockback()
    {
        float sq = (State.KVX * State.KVX) + (State.KVY * State.KVY) + (State.KVZ * State.KVZ);
        return sq > 0.0001f;
    }

    /// <summary>
    /// Sync the CharacterState position from a Death/Respawn event.
    /// </summary>
    public void Respawn(Vector3 position)
    {
        ref var s = ref State;
        s.PX = position.X;
        s.PY = position.Y;
        s.PZ = position.Z;
        s.VX = s.VY = s.VZ = 0f;
        s.KVX = s.KVY = s.KVZ = 0f;
        s.State = ActionState.Idle;
        s.StateTicks = 0;
        s.JumpsLeft = _charDef.Movement.MaxJumps;
        s.AirDodgesLeft = 1;
        s.IsGrounded = false;
        s.DamagePercent = 0;
        s.ComboStage = 0;
        s.ComboTimerTicks = 0;
        s.AnimLockTicks = 0;
        s.DashCooldownTicks = 0;
        s.DashDurationTicks = 0;
        s.InvincibilityTicks = 0;
    }

    /// <summary>
    /// Reset jumping resources (called externally when landing is detected).
    /// </summary>
    public void ResetJumps()
    {
        ref var s = ref State;
        s.JumpsLeft = _charDef.Movement.MaxJumps;
        s.AirDodgesLeft = 1;
    }
}
