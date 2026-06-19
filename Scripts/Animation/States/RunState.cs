#nullable enable
using Godot;

/// <summary>
/// Grounded running state — player has horizontal velocity.
/// Transitions to: idle (stopping), air (jump or off edge).
/// </summary>
public sealed partial class RunState : State
{
	public RunState()
	{
		AnimationName = "Run";
	}

	public override void Enter()
	{
		Movement.ResetJumps();
		Player.SetModelEmission(new Color(0.2f, 0.5f, 1.0f)); // Blue
		base.Enter();
	}

	public override void OnProcess(float delta)
	{
		// Ran off edge — go to fall (no jump animation)
		if (!Movement.IsGrounded && Player.Velocity.Y < 0f)
		{
			StateMachine.TransitionTo("fall");
			return;
		}

		// Idle when stopping
		var hVel = new Vector3(Player.Velocity.X, 0f, Player.Velocity.Z);
		if (hVel.Length() < 0.5f)
		{
			StateMachine.TransitionTo("idle");
			return;
		}
	}
}
