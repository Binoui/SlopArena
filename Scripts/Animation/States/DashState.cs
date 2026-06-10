#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Dash state — plays dash animation, movement handled by Simulation.ProcessDash.
/// Transition out when dash duration expires.
/// </summary>
public sealed partial class DashState : State
{
	private float _dirX, _dirZ;

	public DashState()
	{
		AnimationName = "dash";
		CanMove = false;
	}

	/// <summary>
	/// Set dash direction before TransitionTo("dash").
	/// </summary>
	public void SetDirection(float dirX, float dirZ)
	{
		_dirX = dirX;
		_dirZ = dirZ;
	}

	public override void Enter()
	{
		base.Enter(); // plays dash anim
		Movement.StartDash(_dirX, _dirZ);
		Player.SetModelEmission(new Color(0.3f, 0.9f, 1.0f)); // Cyan
	}

	public override void Exit()
	{
		Player.ClearModelEmission();
		base.Exit();
	}

	public override void OnProcess(float delta)
	{
		// Wait for dash to finish (Simulation decrements DashDurationTicks)
		if (Movement.State.DashDurationTicks > 0)
			return;

		// Dash ended — transition to appropriate state
		if (Player.IsOnFloor())
		{
			StateMachine.TransitionTo(
				Player.MoveDirection.LengthSquared() > 0.001f ? "run" : "idle");
		}
		else
		{
			StateMachine.TransitionTo("air");
		}
	}
}
