#nullable enable
using Godot;

/// <summary>
/// Fall state — default airborne state when not jumping.
/// No dedicated animation — keeps showing whatever was last playing (jump, run, idle).
/// Transitions to JumpState on double-jump, to run/idle on ground contact.
/// Avoids the jarring fall-pose flash when landing quickly after a jump.
/// </summary>
public sealed partial class FallState : State
{
    /// <summary>Consecutive grounded frames needed to transition out of fall.</summary>
    private const int GroundedThreshold = 3;
    private int _groundedCount;

    public FallState()
    {
        AnimationName = ""; // No animation — keep showing whatever was last playing
    }

    public override void Enter()
    {
        _groundedCount = 0;
        base.Enter();
        Player.SetModelEmission(new Color(0.5f, 0.8f, 1.0f));
    }

    public override void Exit()
    {
        Player.ClearModelEmission();
        base.Exit();
    }

    public override void OnProcess(float delta)
    {
        // Ground detection with threshold to prevent flicker
        if (Movement.IsGrounded)
        {
            _groundedCount++;
            if (_groundedCount >= GroundedThreshold)
            {
                Vector3 vel = Player.Velocity;
                bool moving = (vel.X * vel.X + vel.Z * vel.Z) > 1f;
                Movement.ResetJumps();
                StateMachine.TransitionTo(moving ? "run" : "idle");
            }
        }
        else
        {
            _groundedCount = 0;
        }
    }
}
