#nullable enable
using Godot;

/// <summary>
/// Air state — replaces both JumpState and FallState.
/// Uses a BlendSpace1D in the AnimationTree (Movement/air) driven by velocity.Y.
/// Applies jump force in OnPhysicsProcess (like reference project).
/// Transitions to Landing when grounded.
/// </summary>
public sealed partial class AirState : State
{
    public AirState()
    {
        AnimationName = "Movement/air";
    }

    private bool _hasJumped;

    public override void Enter()
    {
        _hasJumped = false;
        base.Enter();
    }

    public override void OnPhysicsProcess(float delta)
    {
        // Apply jump force once on first physics frame after entering
        if (!_hasJumped && Player.IsOnFloor())
        {
            Player.Velocity = new Vector3(
                Player.Velocity.X,
                Player.GetCharacterDef().Movement.JumpForce,
                Player.Velocity.Z);
            _hasJumped = true;
        }
    }

    public override void OnProcess(float delta)
    {
        // Drive the BlendSpace1D parameter: -1 = up (jump), 0 = apex, +1 = down (fall)
        float maxSpeed = Player.GetCharacterDef().Movement.JumpForce;
        float normalized = Mathf.Clamp(Player.Velocity.Y / maxSpeed, -1f, 1f);
        StateMachine.SetAnimParameter("parameters/Movement/air/blend_position", normalized);

        // Transition to Landing when grounded
        if (Player.IsOnFloor() && Player.Velocity.Y <= 0f)
        {
            StateMachine.TransitionTo("landing");
            return;
        }
    }
}
