#nullable enable
using Godot;

/// <summary>
/// Jump state — rising phase of a jump (single or double jump, duration-based).
/// Uses the dedicated "jump" AnimationTree node.
/// Auto-transitions to FallState after JumpDurationTicks (if still airborne),
/// or directly to Run/Idle if grounded (avoids one frame of fall pose when landing quickly).
/// </summary>
public sealed partial class JumpState : State
{
    private const int GroundedThreshold = 10;
    private ushort _ticksRemaining;
    private int _groundedCount;

    public JumpState()
    {
        AnimationName = "jump";
    }

    public override void Enter()
    {
        _ticksRemaining = Player.CharDef.Movement.JumpDurationTicks;
        _groundedCount = 0;
        base.Enter();
        Player.SetModelEmission(new Color(0.5f, 0.8f, 1.0f)); // Light blue
    }

    public override void Exit()
    {
        Player.ClearModelEmission();
        base.Exit();
    }

    public override void OnPhysicsProcess(float delta)
    {
        // Ground detection — if we land during the jump, skip fall entirely
        if (Movement.IsGrounded)
        {
            _groundedCount++;
            if (_groundedCount >= GroundedThreshold)
            {
                Vector3 vel = Player.Velocity;
                bool moving = (vel.X * vel.X + vel.Z * vel.Z) > 1f;
                Movement.ResetJumps();
                StateMachine.TransitionTo(moving ? "run" : "idle");
                return;
            }
        }
        else
        {
            _groundedCount = 0;
        }

        // Tick counter — transition to fall when rising phase ends (if still airborne)
        if (_ticksRemaining > 0)
        {
            _ticksRemaining--;
            if (_ticksRemaining == 0 && !Movement.IsGrounded)
                StateMachine.TransitionTo("fall");
        }
    }
}
