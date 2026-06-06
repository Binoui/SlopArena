#nullable enable
using Godot;

/// <summary>
/// Jump state — airborne, going upward (velocity.Y > 0).
/// Applies jump force in OnPhysicsProcess (after MovementComponent.Tick).
/// Transitions to Fall when descending (velocity.Y < 0).
/// </summary>
public sealed partial class JumpState : State
{
	public JumpState()
	{
		AnimationName = "Movement/Jump";
	}

	private bool _hasJumped;

	public override void Enter()
	{
		_hasJumped = false;
		base.Enter();
	}

	public override void OnPhysicsProcess(float delta)
	{
		// Apply jump force once (like reference project: in physics, not process)
		if (!_hasJumped && Player.IsOnFloor())
		{
			Player.Velocity = new Vector3(Player.Velocity.X, Player.GetCharacterDef().Movement.JumpForce, Player.Velocity.Z);
			_hasJumped = true;
		}
	}

	public override void OnProcess(float delta)
	{
		// Transition to Fall at the apex
		if (Player.Velocity.Y < 0f && !Player.IsOnFloor())
		{
			StateMachine.TransitionTo("fall");
			return;
		}

		// Instant landing (very short jump, e.g. bumped ceiling)
		if (Player.IsOnFloor() && Player.Velocity.Y <= 0f)
		{
			StateMachine.TransitionTo("landing");
			return;
		}
	}
}
