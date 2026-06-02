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
        State.HP = 100f;
        State.MaxHP = 100f;
        State.JumpsLeft = charDef.Movement.MaxJumps;
        State.AirDodgesLeft = 1;
        State.IsGrounded = false;
        State.EntityId = 1;

        _initialized = true;
    }

    // ── MAIN UPDATE ──

    /// <summary>
    /// Process one tick: delegates to Simulation.SimulateTick(),
    /// then applies velocity to Godot body and calls MoveAndSlide.
    /// </summary>
    public void Tick(SlopArena.Shared.InputState input)
    {
        if (!_initialized) return;

        // Sync Godot position into state at start of tick
        State.PX = _body.GlobalPosition.X;
        State.PY = _body.GlobalPosition.Y;
        State.PZ = _body.GlobalPosition.Z;
        State.VX = _body.Velocity.X;
        State.VY = _body.Velocity.Y;
        State.VZ = _body.Velocity.Z;
        GD.Print($"  PreSim: body_v=({_body.Velocity.X:F1},{_body.Velocity.Y:F1},{_body.Velocity.Z:F1}) state_v=({State.VX:F1},{State.VY:F1},{State.VZ:F1})");

        // Authoritative simulation
        Simulation.SimulateTick(ref State, _charDef, input, _arenaDef);
        GD.Print($"  PostSim: v=({State.VX:F1},{State.VY:F1},{State.VZ:F1}) g={State.IsGrounded} s={State.State} lastDir=({State.LastDirX:F2},{State.LastDirZ:F2})");

        // Apply simulation result to Godot body
        _body.Velocity = new Vector3(State.VX, State.VY, State.VZ);
        GD.Print($"  Tick: vel=({State.VX:F1},{State.VY:F1},{State.VZ:F1}) grounded={State.IsGrounded} state={State.State}");

        // Godot collision/rendering step.
        _body.MoveAndSlide();
        bool godotGrounded = _body.IsOnFloor();
        GD.Print($"  PostSlide: body_vel=({_body.Velocity.X:F1},{_body.Velocity.Y:F1},{_body.Velocity.Z:F1}) floor={godotGrounded}");

        // Sync grounded state back (Godot's collision is more accurate for ground detection)
        State.IsGrounded = godotGrounded || State.IsGrounded;

        // Floor safety (catching edge cases)
        if (_body.GlobalPosition.Y < Simulation.FloorHeight - 1f && State.IsGrounded)
        {
            _body.GlobalPosition = new Vector3(_body.GlobalPosition.X, Simulation.FloorHeight + 0.5f, _body.GlobalPosition.Z);
            _body.Velocity = new Vector3(_body.Velocity.X, 0f, _body.Velocity.Z);
            State.PY = Simulation.FloorHeight + 0.5f;
            State.VY = 0f;
        }
    }

    // ── PUBLIC ACTION METHODS ──

    /// <summary>
    /// Start a ground dash. Direction is normalized automatically.
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
    /// Apply knockback force. Interrupts dash/air-dodge.
    /// Also sets body velocity immediately so knockback applies this frame.
    /// </summary>
    public void ApplyKnockback(float kbX, float kbY, float kbZ)
    {
        Simulation.ApplyKnockback(ref State, kbX, kbY, kbZ);
        _body.Velocity = new Vector3(kbX, kbY, kbZ);
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
    /// </summary>
    public void SetComboState(byte comboStage, ushort chainWindowTicks, ushort selfLockTicks)
    {
        State.ComboStage = comboStage;
        State.ComboTimerTicks = chainWindowTicks;
        State.AnimLockTicks = selfLockTicks;
    }

    /// <summary>
    /// Get remaining cooldown ticks for a slot (0-5).
    /// </summary>
    public ushort GetSlotCooldown(int slot)
    {
        return slot switch
        {
            0 => State.Cooldown0,
            1 => State.Cooldown1,
            2 => State.Cooldown2,
            3 => State.Cooldown3,
            4 => State.Cooldown4,
            5 => State.Cooldown5,
            _ => 0
        };
    }

    /// <summary>
    /// Check if the entity is currently in knockback.
    /// </summary>
    public bool IsInKnockback()
    {
        float sq = State.KVX * State.KVX + State.KVY * State.KVY + State.KVZ * State.KVZ;
        return sq > 0.0001f;
    }

    /// <summary>
    /// Sync the CharacterState position from a Death/Respawn event.
    /// Called by PlayerController when HP reaches 0.
    /// </summary>
    public void Respawn(Vector3 position)
    {
        State.PX = position.X;
        State.PY = position.Y;
        State.PZ = position.Z;
        State.VX = State.VY = State.VZ = 0f;
        State.KVX = State.KVY = State.KVZ = 0f;
        State.State = ActionState.Idle;
        State.StateTicks = 0;
        State.JumpsLeft = _charDef.Movement.MaxJumps;
        State.AirDodgesLeft = 1;
        State.IsGrounded = false;
        State.HP = State.MaxHP;
        State.ComboStage = 0;
        State.ComboTimerTicks = 0;
        State.AnimLockTicks = 0;
        State.DashCooldownTicks = 0;
        State.DashDurationTicks = 0;
    }

    /// <summary>
    /// Reset jumping resources (called externally when landing is detected).
    /// Now handled internally by Simulation, but kept for external callers.
    /// </summary>
    public void ResetJumps()
    {
        State.JumpsLeft = _charDef.Movement.MaxJumps;
        State.AirDodgesLeft = 1;
    }
}
