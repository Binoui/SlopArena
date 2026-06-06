#nullable enable
using Godot;

/// <summary>
/// Abstract base class for a player state.
/// Each state is a child node of StateMachine.
///
/// Lifecycle: Enter() → OnProcess(dt) [every frame] → Exit()
///
/// SlopArena uses a tick-based simulation (MovementComponent handles physics).
/// States do NOT call MoveAndSlide() — they only handle animation and transition logic.
/// </summary>
public abstract partial class State : Node
{
	[Export]
	public string AnimationName { get; set; } = "";

	[Export]
	public bool CanMove { get; set; } = true;

	protected StateMachine StateMachine { get; private set; } = null!;
	protected PlayerController Player { get; private set; } = null!;
	protected MovementComponent Movement { get; private set; } = null!;
	protected AnimationNodeStateMachinePlayback AnimPlayback { get; private set; } = null!;

	/// <summary>
	/// Called by StateMachine after setting references. Don't call directly.
	/// </summary>
	public void Setup(StateMachine stateMachine, PlayerController player,
		MovementComponent movement, AnimationNodeStateMachinePlayback animPlayback)
	{
		StateMachine = stateMachine;
		Player = player;
		Movement = movement;
		AnimPlayback = animPlayback;
	}

	/// <summary>
	/// Called when entering this state. Plays the associated animation.
	/// </summary>
	public virtual void Enter()
	{
		if (!string.IsNullOrEmpty(AnimationName) && AnimPlayback != null)
			AnimPlayback.Travel(AnimationName);
	}

	/// <summary>
	/// Called when leaving this state.
	/// </summary>
	public virtual void Exit()
	{
	}

	/// <summary>
	/// Called every frame (process). Use for transition checks.
	/// </summary>
	public virtual void OnProcess(float delta)
	{
	}

	/// <summary>
	/// Called for unhandled input events.
	/// </summary>
	public virtual void OnInput(InputEvent @event)
	{
	}
}
