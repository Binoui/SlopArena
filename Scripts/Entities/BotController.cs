#nullable enable
using Godot;

/// <summary>
/// Simple AI controller for NPC bots.
/// Provides basic movement and combat behavior for Smash-style fighting.
///
/// Behaviors:
/// - Wander randomly when far from player
/// - Approach and circle player when in range
/// - Use basic attacks when close
/// - Jump occasionally for mobility
/// - Dash to approach or escape
///
/// This is a STARTER AI - expand with more complex behaviors later
/// (combos, ability usage, threat assessment, etc.)
/// </summary>
public partial class BotController : Node
{
    private PlayerController? _npc;
    /// <summary>
    /// Usually the human player
    /// </summary>
    private PlayerController? _target;

    private float _actionTimer = 0f;
    private float _nextActionTime = 1f;

    private float _attackTimer = 0f;
    private float _nextAttackTime = 0.5f;

    private float _dashTimer = 0f;
    private float _nextDashTime = 3f;

    private Vector3 _wanderTarget = Vector3.Zero;
    private float _wanderTimer = 0f;

    /// <summary>
    /// Behavior parameters
    /// </summary>
    private const float EngageRange = 15f;    // Start approaching player
    /// <summary>
    /// Start attacking
    /// </summary>
    private const float AttackRange = 3f;
    /// <summary>
    /// Circle player at this distance
    /// </summary>
    private const float CircleRadius = 5f;
    /// <summary>
    /// Random wander area
    /// </summary>
    private const float WanderRadius = 10f;

    private float _circleAngle = 0f;

    /// <summary>
    /// One-shot action flags (cleared each frame after injection)
    /// </summary>
    private bool _shouldJump = false;
    private bool _shouldDash = false;

    public void Setup(PlayerController npc, PlayerController target)
    {
        _npc = npc;
        _target = target;

        // Process before parent (PlayerController) so AI input is ready when parent reads it
        ProcessPriority = -1;

        // Start with random wander position
        _wanderTarget = _npc.GlobalPosition + new Vector3(
            (GD.Randf() * WanderRadius) - (WanderRadius / 2),
            0f,
            (GD.Randf() * WanderRadius) - (WanderRadius / 2)
        );
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_npc == null || _target == null) return;
        if (!_npc.IsNpcAlive()) return; // Don't act while dead

        float dt = (float)delta;

        _actionTimer += dt;
        _attackTimer += dt;
        _dashTimer += dt;
        _wanderTimer += dt;

        // Calculate distance to player
        Vector3 toTarget = _target.GlobalPosition - _npc.GlobalPosition;
        float distance = toTarget.Length();

        // Decide behavior based on distance
        if (distance < EngageRange)
        {
            // Combat mode: approach, circle, and attack
            DoCombatBehavior(toTarget, distance, dt);
        }
        else
        {
            // Wander mode: move randomly
            DoWanderBehavior();
        }

        // Build and inject input for the NPC
        InjectInput();
    }

    private void DoCombatBehavior(Vector3 toTarget, float distance, float dt)
    {
        if (_npc == null || _target == null) return;

        // Attack if in range
        if (distance < AttackRange && _attackTimer >= _nextAttackTime)
        {
            // Basic attack (LMB)
            ExecuteAttack();
            _attackTimer = 0f;
            _nextAttackTime = (GD.Randf() * 1f) + 0.5f; // 0.5-1.5s between attacks
        }

        // Circle strafe around player
        if (distance > AttackRange * 0.7f && distance < CircleRadius)
        {
            // Circle the player
            _circleAngle += dt * (GD.Randf() > 0.5f ? 1f : -1f);

            Vector3 circleOffset = new Vector3(
                Mathf.Sin(_circleAngle) * CircleRadius,
                0f,
                Mathf.Cos(_circleAngle) * CircleRadius
            );

            _wanderTarget = _target.GlobalPosition + circleOffset;
        }
        else if (distance >= CircleRadius)
        {
            // Too far: approach player
            _wanderTarget = _target.GlobalPosition;
        }
        else
        {
            // Too close: back away slightly
            Vector3 away = -toTarget.Normalized();
            _wanderTarget = _npc.GlobalPosition + (away * 2f);
        }

        // Dash occasionally to close distance or escape
        if (_dashTimer >= _nextDashTime)
        {
            if (distance > AttackRange * 1.5f && distance < EngageRange)
            {
                // Dash toward player
                ExecuteDash();
                _dashTimer = 0f;
                _nextDashTime = (GD.Randf() * 3f) + 2f; // 2-5s between dashes
            }
        }

        // Jump occasionally for mobility
        if (GD.Randf() < 0.01f) // 1% chance per frame (~0.6 times per second at 60fps)
        {
            ExecuteJump();
        }
    }

    private void DoWanderBehavior()
    {
        if (_npc == null) return;

        // Pick new wander target every few seconds
        if (_wanderTimer >= 3f)
        {
            _wanderTarget = _npc.GlobalPosition + new Vector3(
                (GD.Randf() * WanderRadius) - (WanderRadius / 2),
                0f,
                (GD.Randf() * WanderRadius) - (WanderRadius / 2)
            );
            _wanderTimer = 0f;
        }

        // Occasionally jump while wandering
        if (GD.Randf() < 0.005f)
        {
            ExecuteJump();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // INPUT INJECTION (proper AI → PlayerController interface)
    // ══════════════════════════════════════════════════════════════

    private void InjectInput()
    {
        if (_npc == null) return;

        // Calculate direction to wander target
        Vector3 toWander = _wanderTarget - _npc.GlobalPosition;
        toWander.Y = 0f; // Flatten to horizontal plane
        float distToWander = toWander.Length();

        // Build input state
        var input = new SlopArena.Shared.InputState();

        if (distToWander > 0.5f) // Only move if not at target
        {
            Vector3 direction = toWander.Normalized();

            // Convert world direction to input axes
            // World space: X = right, Z = forward (camera-relative is handled by PlayerController)
            input.MoveX = direction.X;
            input.MoveY = direction.Z;
        }
        else
        {
            // Stop moving
            input.MoveX = 0f;
            input.MoveY = 0f;
        }

        // One-shot actions (cleared after injection)
        input.Jump = _shouldJump;
        input.Dash = _shouldDash;

        // Inject the input into PlayerController
        _npc.InjectInput(input);

        // Clear one-shot flags
        _shouldJump = false;
        _shouldDash = false;
    }

    private void ExecuteAttack()
    {
        if (_npc == null) return;
        _npc.UseAbility(0); // Slot 0 = LMB attack
    }

    private void ExecuteJump()
    {
        _shouldJump = true; // Will be injected in next InjectInput() call
    }

    private void ExecuteDash()
    {
        _shouldDash = true; // Will be injected in next InjectInput() call
    }
}
