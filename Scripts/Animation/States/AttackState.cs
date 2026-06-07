#nullable enable
using Godot;

/// <summary>
/// Generic attack state for all ability slots.
/// PlayerController sets NextAnimName before TransitionTo("attack")
/// or calls ChainTo() for combo chaining within the same state.
/// </summary>
public sealed partial class AttackState : State
{
	public AttackState()
	{
		AnimationName = "melee";
	}

	/// <summary>
	/// Set by PlayerController before TransitionTo("attack").
	/// </summary>
	public string NextAnimName { get; set; } = "";

	public override void Enter()
	{
		if (!string.IsNullOrEmpty(NextAnimName))
			AnimationName = NextAnimName;
		base.Enter();
	}

	/// <summary>
	/// Chain to the next combo stage without leaving the state.
	/// </summary>
	public void ChainTo(string animName)
	{
		NextAnimName = animName;
		AnimationName = animName;
		if (AnimPlayback != null)
			AnimPlayback.Travel(animName);
	}

	public override void Exit()
	{
		base.Exit();
		// Clear queued chains when leaving the state (avoids leftover LMB1)
		Movement.State.BufferedChain = 0;
	}

	public override void OnProcess(float delta)
	{
		if (Movement.State.AnimLockTicks > 0)
			return;

		if (Movement.State.ComboTimerTicks > 0)
			return;

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
