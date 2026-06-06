#nullable enable
using Godot;

/// <summary>
/// Grounded idle state — player stands still.
/// Transitions to: Run (on movement input), Jump (on jump press), Fall (off edge).
/// </summary>
public sealed partial class IdleState : State
{
	public IdleState()
	{
		AnimationName = "Idle";
	}

	public override void Enter()
	{
		Movement.State.ComboStage = 0;
		Movement.ResetJumps();
		base.Enter();
	}

	public override void OnProcess(float delta)
	{
		// Jump
		if (Input.IsActionJustPressed("jump") && Player.IsOnFloor())
		{
			StateMachine.TransitionTo("jump");
			return;
		}

		// Run if moving (input from camera-relative direction or velocity)
		if (!Player.IsOnFloor())
		{
			StateMachine.TransitionTo("fall");
			return;
		}

		// If the simulation produced horizontal velocity from friction, check against threshold
		var hVel = new Vector3(Player.Velocity.X, 0f, Player.Velocity.Z);
		if (hVel.Length() > 0.5f && Player.IsOnFloor())
		{
			StateMachine.TransitionTo("run");
			return;
		}
	}
}
