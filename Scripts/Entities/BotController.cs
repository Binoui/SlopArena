#nullable enable
using Godot;
using System;

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
    private PlayerController? _target; // Usually the human player

    private float _actionTimer = 0f;
    private float _nextActionTime = 1f;

    private float _attackTimer = 0f;
    private float _nextAttackTime = 0.5f;

    private float _dashTimer = 0f;
    private float _nextDashTime = 3f;

    private Vector3 _wanderTarget = Vector3.Zero;
    private float _wanderTimer = 0f;

    // Behavior parameters
    private const float EngageRange = 15f;    // Start approaching player
    private const float AttackRange = 3f;     // Start attacking
    private const float CircleRadius = 5f;    // Circle player at this distance
    private const float WanderRadius = 10f;   // Random wander area

    private float _circleAngle = 0f;

    public void Setup(PlayerController npc, PlayerController target)
    {
        _npc = npc;
        _target = target;

        // Start with random wander position
        _wanderTarget = _npc.GlobalPosition + new Vector3(
            GD.Randf() * WanderRadius - WanderRadius / 2,
            0f,
            GD.Randf() * WanderRadius - WanderRadius / 2
        );
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_npc == null || _target == null) return;
        if (!_npc.IsAlive()) return; // Don't act while dead

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
            DoWanderBehavior(dt);
        }

        // Simulate input for the NPC (this drives PlayerController)
        SimulateInput(dt);
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
            _nextAttackTime = GD.Randf() * 1f + 0.5f; // 0.5-1.5s between attacks
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
            _wanderTarget = _npc.GlobalPosition + away * 2f;
        }

        // Dash occasionally to close distance or escape
        if (_dashTimer >= _nextDashTime)
        {
            if (distance > AttackRange * 1.5f && distance < EngageRange)
            {
                // Dash toward player
                ExecuteDash();
                _dashTimer = 0f;
                _nextDashTime = GD.Randf() * 3f + 2f; // 2-5s between dashes
            }
        }

        // Jump occasionally for mobility
        if (GD.Randf() < 0.01f) // 1% chance per frame (~0.6 times per second at 60fps)
        {
            ExecuteJump();
        }
    }

    private void DoWanderBehavior(float dt)
    {
        if (_npc == null) return;

        // Pick new wander target every few seconds
        if (_wanderTimer >= 3f)
        {
            _wanderTarget = _npc.GlobalPosition + new Vector3(
                GD.Randf() * WanderRadius - WanderRadius / 2,
                0f,
                GD.Randf() * WanderRadius - WanderRadius / 2
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
    // INPUT SIMULATION
    // ══════════════════════════════════════════════════════════════

    private void SimulateInput(float dt)
    {
        if (_npc == null) return;

        // Calculate direction to wander target
        Vector3 toWander = _wanderTarget - _npc.GlobalPosition;
        toWander.Y = 0f; // Flatten to horizontal plane
        float distToWander = toWander.Length();

        if (distToWander > 0.5f) // Only move if not at target
        {
            Vector3 direction = toWander.Normalized();

            // Convert world direction to ZQSD input (relative to fixed camera direction)
            // Assuming camera looks down -Z axis (north)
            // Z = forward (negative Z), Q = left (negative X), S = back (positive Z), D = right (positive X)

            float inputZ = -direction.Z; // Forward/backward (Z/S keys)
            float inputX = direction.X;   // Left/right (Q/D keys)

            // Normalize to prevent diagonal speed boost
            float len = Mathf.Sqrt(inputX * inputX + inputZ * inputZ);
            if (len > 0.01f)
            {
                inputX /= len;
                inputZ /= len;
            }

            // Set movement input
            SetMovementInput(inputX, inputZ);
        }
        else
        {
            // Stop moving
            SetMovementInput(0f, 0f);
        }
    }

    private void SetMovementInput(float x, float z)
    {
        if (_npc == null) return;

        // This would need to interface with PlayerController's input system
        // For now, we directly manipulate velocity (hack for MVP)
        // TODO: Add proper input injection API to PlayerController

        float speed = 9f; // Match character walk speed
        Vector3 velocity = new Vector3(x * speed, _npc.Velocity.Y, z * speed);
        _npc.Velocity = velocity;
    }

    private void ExecuteAttack()
    {
        // TODO: Call PlayerController.ExecuteSlot(0) for LMB
        // Needs public API on PlayerController
    }

    private void ExecuteJump()
    {
        if (_npc == null) return;

        // Check if grounded and apply jump
        if (_npc.IsOnFloor())
        {
            Vector3 vel = _npc.Velocity;
            vel.Y = 10f; // Jump force
            _npc.Velocity = vel;
        }
    }

    private void ExecuteDash()
    {
        // TODO: Call PlayerController dash ability
        // Needs public API
    }
}
