#nullable enable
using Godot;

/// <summary>
/// Grounded idle state — player stands still.
/// Transitions to: air (on jump or off edge), run (when moving).
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
		Player.ClearModelEmission(); // Debug: normal
		base.Enter();
	}

	public override void OnProcess(float delta)
	{
		// Jump — transition immediately when action is pressed
		if (InputCtrl.JumpJustPressed)
		{
			StateMachine.TransitionTo("air");
			return;
		}

		// Walked off edge — go to air (blend handles jump→fall naturally)
		if (!Movement.IsGrounded && Player.Velocity.Y < 0f)
		{
			StateMachine.TransitionTo("air");
			return;
		}

		// Run when moving
		var hVel = new Vector3(Player.Velocity.X, 0f, Player.Velocity.Z);
		if (hVel.Length() > 0.5f && Movement.IsGrounded)
		{
			StateMachine.TransitionTo("run");
			return;
		}
	}
}
