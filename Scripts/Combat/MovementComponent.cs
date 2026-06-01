#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Input state struct for passing player input into MovementComponent.Tick().
/// Booleans for binary inputs, floats for analog directional input.
/// </summary>
public struct InputState
{
    /// <summary>Forward (W/Z) pressed</summary>
    public bool Up;
    /// <summary>Backward (S) pressed</summary>
    public bool Down;
    /// <summary>Left (Q/A) pressed</summary>
    public bool Left;
    /// <summary>Right (D) pressed</summary>
    public bool Right;
    /// <summary>Jump (Space) pressed</summary>
    public bool Jump;
    /// <summary>Dash (Shift) just pressed (edge-triggered for air dodge)</summary>
    public bool Dash;
    /// <summary>Crouch (C) pressed</summary>
    public bool Crouch;
    /// <summary>Attack (LMB) pressed</summary>
    public bool Attack;
    /// <summary>Normalized horizontal input X (-1 to 1)</summary>
    public float MoveX;
    /// <summary>Normalized vertical input Y (-1 to 1)</summary>
    public float MoveY;
}

/// <summary>
/// Pure C# movement component that encapsulates ALL movement physics from PlayerController.
/// Operates on a CharacterBody3D's Velocity and position.
/// Does NOT inherit from Godot.Node — plain class designed for composition.
///
/// Usage:
///   var moveComp = new MovementComponent(body);
///   // On input events:
///   moveComp.StartDash(direction, stats);
///   moveComp.StartAirDodge(direction, stats);
///   moveComp.ApplyJumpForce(stats.JumpForce);
///   moveComp.ApplyKnockback(knockbackForce);
///   // In _PhysicsProcess:
///   moveComp.Tick(delta, def, inputState);
///   // After Tick, body.Velocity is set and MoveAndSlide has been called
///   bool grounded = moveComp.IsGrounded;
/// </summary>
public class MovementComponent
{
    // ==========================================
    // PUBLIC ENUMS & PROPERTIES
    // ==========================================

    public enum MoveState
    {
        Normal,
        Dashing,
        AirDodging
    }

    public MoveState CurrentState { get; private set; } = MoveState.Normal;
    public float DashCooldownRemaining { get; private set; } = 0f;
    public Vector3 KnockbackVelocity { get; set; } = Vector3.Zero;
    public bool IsGrounded { get; private set; } = false;
    public int JumpsLeft { get; private set; } = 2;
    public int AirDodgesLeft { get; private set; } = 1;
    public Vector3 LastInputDirection { get; private set; } = Vector3.Zero;

    // ==========================================
    // PRIVATE STATE
    // ==========================================

    private readonly CharacterBody3D _body;

    // Dash state
    private float _dashTimer = 0f;
    private float _dashCooldownTimer = 0f;
    private Vector3 _dashDirection = Vector3.Zero;

    // Air dodge state
    private float _airDodgeTimer = 0f;
    private Vector3 _airDodgeDirection = Vector3.Zero;

    // Sprint / dash dance state
    private float _dirHoldTime = 0f;
    private bool _isSprinting = false;
    private float _turnaroundTimer = 0f;
    private bool _wasAirborneDuringKnockback = false;

    // Configurable constants (from PlayerController [Export] fields
    // that are NOT in MovementStats but are essential to the movement feel)
    private const float SprintThreshold = 0.2f;
    private const float TurnaroundLag = 0.1f;
    private const float AirDrag = 0.2f;
    private const float AirDodgeSpeed = 35.0f;
    private const float AirDodgeDuration = 0.12f;
    private const float KnockbackDecay = 8.0f;
    private const float MaxAirDodges = 1;

    // ==========================================
    // CONSTRUCTOR
    // ==========================================

    /// <summary>
    /// Create a MovementComponent that operates on the given CharacterBody3D.
    /// </summary>
    public MovementComponent(CharacterBody3D body)
    {
        _body = body ?? throw new System.ArgumentNullException(nameof(body));
    }

    // ==========================================
    // MAIN UPDATE (call from _PhysicsProcess)
    // ==========================================

    /// <summary>
    /// Process one physics tick of ALL movement physics.
    /// Handles knockback decay, dash/air-dodge state maintenance,
    /// ground movement (acceleration, friction, sprint/dash-dance),
    /// air movement (air acceleration, drag, gravity), and jump logic.
    /// Calls MoveAndSlide() internally so IsOnFloor() is fresh.
    /// </summary>
    public void Tick(float delta, CharacterDefinition def, InputState input)
    {
        var stats = def.Movement;
        float dt = delta;

        // --- Timers ---
        TickTimers(dt);

        // --- Knockback overrides everything ---
        if (KnockbackVelocity.LengthSquared() > 0.001f)
        {
            ProcessKnockback(dt);
            return;
        }

        // --- State machine ---
        // Note: order matters — dash/dodge set Velocity, then normal movement
        // can override if state just expired.

        if (CurrentState == MoveState.Dashing)
        {
            ProcessDash(dt, stats, input);
        }
        else if (CurrentState == MoveState.AirDodging)
        {
            ProcessAirDodge(dt);
        }

        if (CurrentState == MoveState.Normal)
        {
            ProcessNormalMovement(dt, stats, input);
        }

        // --- Gravity ---
        ApplyGravity(stats, dt);

        // --- Physics step ---
        _body.MoveAndSlide();
        IsGrounded = _body.IsOnFloor();

        // --- Landing cleanup ---
        if (CurrentState == MoveState.AirDodging && IsGrounded)
        {
            CurrentState = MoveState.Normal;
            _airDodgeTimer = 0f;
        }

        // Reset air dodges on any landing (safety for edge cases)
        if (IsGrounded && CurrentState == MoveState.Dashing)
        {
            AirDodgesLeft = (int)MaxAirDodges;
        }

        // Floor safety
        if (_body.GlobalPosition.Y < 0f && IsGrounded)
        {
            _body.GlobalPosition = new Vector3(_body.GlobalPosition.X, 1f, _body.GlobalPosition.Z);
            _body.Velocity = new Vector3(_body.Velocity.X, 0f, _body.Velocity.Z);
        }

        // Fall death plane
        if (_body.GlobalPosition.Y < -50f)
        {
            GD.Print("Entity fell through the floor! (detected by MovementComponent)");
            _body.Position = new Vector3(100f, 10f, 100f);
            _body.Velocity = Vector3.Zero;
        }
    }

    // ==========================================
    // PUBLIC ACTION METHODS
    // ==========================================

    /// <summary>
    /// Start a ground dash in the given direction.
    /// Updates CurrentState to Dashing and applies initial burst velocity.
    /// </summary>
    public void StartDash(Vector3 direction, MovementStats stats)
    {
        if (DashCooldownRemaining > 0f) return;
        if (CurrentState != MoveState.Normal) return;
        if (KnockbackVelocity.LengthSquared() > 0.001f) return;

        Vector3 dir = direction;
        if (dir.LengthSquared() < 0.01f)
        {
            dir = -_body.Transform.Basis.Z;
            dir.Y = 0;
            dir = dir.Normalized();
        }

        _dashDirection = dir;
        _dashTimer = stats.DashDurationTicks / 60f;
        _dashCooldownTimer = stats.DashCooldownTicks / 60f;
        DashCooldownRemaining = _dashCooldownTimer;
        CurrentState = MoveState.Dashing;

        _body.Velocity = new Vector3(
            dir.X * stats.DashSpeed,
            Mathf.Max(_body.Velocity.Y, 0f),
            dir.Z * stats.DashSpeed
        );
    }

    /// <summary>
    /// Start an air dodge in the given direction.
    /// Only works in the air with air dodges remaining.
    /// </summary>
    public void StartAirDodge(Vector3 direction, MovementStats stats)
    {
        if (AirDodgesLeft <= 0) return;
        if (CurrentState != MoveState.Normal) return;
        if (IsGrounded) return;
        if (KnockbackVelocity.LengthSquared() > 0.001f) return;

        Vector3 dir = direction;
        if (dir.LengthSquared() < 0.01f)
        {
            dir = -_body.Transform.Basis.Z;
            dir.Y = 0;
            dir = dir.Normalized();
        }

        _airDodgeDirection = dir;
        _airDodgeTimer = AirDodgeDuration;
        CurrentState = MoveState.AirDodging;
        AirDodgesLeft--;

        _body.Velocity = new Vector3(
            dir.X * AirDodgeSpeed,
            _body.Velocity.Y,
            dir.Z * AirDodgeSpeed
        );
    }

    /// <summary>
    /// Apply an upward jump force. Consumes one jump.
    /// </summary>
    public void ApplyJumpForce(float jumpForce)
    {
        if (JumpsLeft <= 0) return;
        _body.Velocity = new Vector3(_body.Velocity.X, jumpForce, _body.Velocity.Z);
        JumpsLeft--;
    }

    /// <summary>
    /// Apply knockback force. Interrupts dash/air-dodge and sets state to Normal.
    /// </summary>
    public void ApplyKnockback(Vector3 knockback)
    {
        KnockbackVelocity = knockback;
        CurrentState = MoveState.Normal;
        _dashTimer = 0f;
        _airDodgeTimer = 0f;
        _wasAirborneDuringKnockback = !IsGrounded;
    }

    /// <summary>
    /// Reset jumps to the character's maximum. Called on ground contact.
    /// </summary>
    public void ResetJumps()
    {
        JumpsLeft = 2; // Will be overridden by MaxJumps from stats on actual ground frame
    }

    // ==========================================
    // TECH / UTILITY
    // ==========================================

    /// <summary>
    /// Tech roll: clears knockback and sets state to Normal.
    /// Called externally by PlayerController when tech input is detected
    /// during knockback landing.
    /// </summary>
    public void DoTechRoll()
    {
        KnockbackVelocity = Vector3.Zero;
        CurrentState = MoveState.Normal;

        // Small burst in last input direction (or forward)
        Vector3 rollDir = LastInputDirection;
        if (rollDir.LengthSquared() < 0.01f)
        {
            rollDir = -_body.Transform.Basis.Z;
            rollDir.Y = 0;
            rollDir = rollDir.Normalized();
        }
        _body.Velocity = new Vector3(rollDir.X * 10f, 0f, rollDir.Z * 10f);
    }

    /// <summary>
    /// Check if the entity is currently in knockback.
    /// </summary>
    public bool IsInKnockback()
    {
        return KnockbackVelocity.LengthSquared() > 0.001f;
    }

    // ==========================================
    // PRIVATE: Timer bookkeeping
    // ==========================================

    private void TickTimers(float dt)
    {
        if (_dashCooldownTimer > 0f)
        {
            _dashCooldownTimer -= dt;
            if (_dashCooldownTimer <= 0f)
            {
                _dashCooldownTimer = 0f;
                DashCooldownRemaining = 0f;
            }
            else
            {
                DashCooldownRemaining = _dashCooldownTimer;
            }
        }

        if (_dashTimer > 0f)
            _dashTimer -= dt;

        if (_airDodgeTimer > 0f)
            _airDodgeTimer -= dt;

        if (_turnaroundTimer > 0f)
            _turnaroundTimer -= dt;
    }

    // ==========================================
    // PRIVATE: Knockback processing
    // ==========================================

    private void ProcessKnockback(float dt)
    {
        // Decay knockback velocity
        KnockbackVelocity = KnockbackVelocity.Lerp(Vector3.Zero, KnockbackDecay * dt);
        _body.Velocity = KnockbackVelocity;

        // Apply minimal gravity during knockback
        if (!_body.IsOnFloor())
        {
            _body.Velocity -= new Vector3(0f, 9.8f * dt, 0f);
        }

        bool wasAirborne = !_body.IsOnFloor();
        _body.MoveAndSlide();
        bool nowGrounded = _body.IsOnFloor();
        IsGrounded = nowGrounded;

        if (wasAirborne && nowGrounded)
        {
            // Natural landing: clear knockback (tech roll handled externally)
            KnockbackVelocity = Vector3.Zero;
            CurrentState = MoveState.Normal;
            AirDodgesLeft = (int)MaxAirDodges;
        }
    }

    // ==========================================
    // PRIVATE: Dash state processing
    // ==========================================

    private void ProcessDash(float dt, MovementStats stats, InputState input)
    {
        if (_dashTimer > 0f)
        {
            // Dash can be canceled by jump
            if (input.Jump && IsGrounded)
            {
                CurrentState = MoveState.Normal;
                _body.Velocity = new Vector3(_body.Velocity.X, stats.JumpForce, _body.Velocity.Z);
            }
            else
            {
                _body.Velocity = new Vector3(
                    _dashDirection.X * stats.DashSpeed,
                    Mathf.Max(_body.Velocity.Y, 0f),
                    _dashDirection.Z * stats.DashSpeed
                );
            }
        }
        else
        {
            CurrentState = MoveState.Normal;
        }
    }

    // ==========================================
    // PRIVATE: Air dodge state processing
    // ==========================================

    private void ProcessAirDodge(float dt)
    {
        if (_airDodgeTimer > 0f)
        {
            _body.Velocity = new Vector3(
                _airDodgeDirection.X * AirDodgeSpeed,
                _body.Velocity.Y,
                _airDodgeDirection.Z * AirDodgeSpeed
            );
        }
        else
        {
            CurrentState = MoveState.Normal;
        }
    }

    // ==========================================
    // PRIVATE: Normal movement processing
    // ==========================================

    private void ProcessNormalMovement(float dt, MovementStats stats, InputState input)
    {
        bool grounded = _body.IsOnFloor();
        Vector3 inputDir = GetInputDirection(input);
        LastInputDirection = inputDir;

        if (grounded)
        {
            ProcessGroundMovement(dt, stats, input, inputDir);
        }
        else
        {
            ProcessAirMovement(dt, stats, input, inputDir);
        }
    }

    private void ProcessGroundMovement(float dt, MovementStats stats, InputState input, Vector3 inputDir)
    {
        // Reset resources on landing
        AirDodgesLeft = (int)MaxAirDodges;
        JumpsLeft = stats.MaxJumps;
        IsGrounded = true;

        bool hasInput = inputDir.LengthSquared() > 0.01f;

        if (hasInput)
        {
            // Detect direction change (dot < 0.5 means significant change)
            bool dirChanged = LastInputDirection.LengthSquared() > 0.01f
                && inputDir.Dot(LastInputDirection) < 0.5f;

            if (dirChanged)
            {
                _dirHoldTime = 0f;
                if (_isSprinting)
                {
                    _turnaroundTimer = TurnaroundLag;
                    _isSprinting = false;
                }
            }
            else
            {
                // Same direction or initial press
                _dirHoldTime += dt;
                if (_dirHoldTime >= SprintThreshold && !_isSprinting)
                    _isSprinting = true;
            }

            LastInputDirection = inputDir;

            if (_turnaroundTimer > 0f)
            {
                // Turnaround lag: decelerate, can't move in new direction yet
                float friction = stats.GroundFriction * dt;
                _body.Velocity = new Vector3(
                    Mathf.MoveToward(_body.Velocity.X, 0f, Mathf.Abs(_body.Velocity.X) * friction),
                    _body.Velocity.Y,
                    Mathf.MoveToward(_body.Velocity.Z, 0f, Mathf.Abs(_body.Velocity.Z) * friction)
                );
            }
            else
            {
                // Walk or sprint: instant speed
                float speed = _isSprinting ? stats.SprintSpeed : stats.WalkSpeed;
                _body.Velocity = new Vector3(inputDir.X * speed, _body.Velocity.Y, inputDir.Z * speed);
            }

            // Jump (ground or double jump)
            if (input.Jump && JumpsLeft > 0)
            {
                _body.Velocity = new Vector3(_body.Velocity.X, stats.JumpForce, _body.Velocity.Z);
                JumpsLeft--;
            }
        }
        else
        {
            // No input: reset sprint, decelerate via friction
            _dirHoldTime = 0f;
            _isSprinting = false;
            LastInputDirection = Vector3.Zero;

            float friction = stats.GroundFriction * dt;
            _body.Velocity = new Vector3(
                Mathf.MoveToward(_body.Velocity.X, 0f, Mathf.Abs(_body.Velocity.X) * friction),
                _body.Velocity.Y,
                Mathf.MoveToward(_body.Velocity.Z, 0f, Mathf.Abs(_body.Velocity.Z) * friction)
            );
        }
    }

    private void ProcessAirMovement(float dt, MovementStats stats, InputState input, Vector3 inputDir)
    {
        IsGrounded = false;

        // Air acceleration toward input direction
        Vector3 targetVel = inputDir * stats.WalkSpeed;
        float airAccel = stats.AirAcceleration * dt;
        float newX = Mathf.MoveToward(_body.Velocity.X, targetVel.X, airAccel);
        float newZ = Mathf.MoveToward(_body.Velocity.Z, targetVel.Z, airAccel);
        _body.Velocity = new Vector3(newX, _body.Velocity.Y, newZ);

        // Air drag (gentle slowdown)
        float drag = AirDrag * dt;
        _body.Velocity = new Vector3(
            _body.Velocity.X * (1f - drag),
            _body.Velocity.Y,
            _body.Velocity.Z * (1f - drag)
        );

        // Air dodge initiation (dash in air = air dodge)
        if (input.Dash && AirDodgesLeft > 0)
        {
            StartAirDodge(inputDir, stats);
        }
    }

    // ==========================================
    // PRIVATE: Gravity
    // ==========================================

    private void ApplyGravity(MovementStats stats, float dt)
    {
        if (!_body.IsOnFloor())
        {
            _body.Velocity -= new Vector3(0f, stats.Gravity * dt, 0f);

            // Hard cap on fall speed
            if (_body.Velocity.Y < -stats.MaxFallSpeed)
            {
                _body.Velocity = new Vector3(_body.Velocity.X, -stats.MaxFallSpeed, _body.Velocity.Z);
            }
        }
    }

    // ==========================================
    // PRIVATE: Input direction helper
    // ==========================================

    private static Vector3 GetInputDirection(InputState input)
    {
        Vector3 dir = Vector3.Zero;
        if (input.Up)    dir += Vector3.Back;  // Forward in Godot is -Z
        if (input.Down)  dir += Vector3.Forward;
        if (input.Left)  dir -= Vector3.Right;
        if (input.Right) dir += Vector3.Right;

        if (dir.LengthSquared() > 0f)
            dir = dir.Normalized();

        return dir;
    }
}
