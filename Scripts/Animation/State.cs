#nullable enable
using Godot;

/// <summary>
/// Abstract base class for a player state.
/// Each state is a child node of StateMachine.
///
/// Lifecycle: Enter() → OnProcess(dt) → OnPhysicsProcess(dt) → Exit()
///
/// Physics flow:
///   1. PlayerController._PhysicsProcess → MovementComponent.Tick() (sim only)
///   2. StateMachine._PhysicsProcess → CurrentState.OnPhysicsProcess()
///      States can override velocity for state-specific forces (jump, landing).
///      MovementComponent already called MoveAndSlide — states do NOT call it.
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
	protected InputController InputCtrl { get; private set; } = null!;

	/// <summary>
	/// Called by StateMachine after setting references. Don't call directly.
	/// </summary>
	public void Setup(StateMachine stateMachine, PlayerController player,
		MovementComponent movement, AnimationNodeStateMachinePlayback animPlayback,
		InputController inputCtrl)
	{
		StateMachine = stateMachine;
		Player = player;
		Movement = movement;
		AnimPlayback = animPlayback;
		InputCtrl = inputCtrl;
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
	/// Called every physics frame, AFTER MovementComponent.Tick().
	/// States override velocity for state-specific forces (jump, landing dampening).
	/// Do NOT call MoveAndSlide() — already done by MovementComponent.
	/// </summary>
	public virtual void OnPhysicsProcess(float delta)
	{
	}

	/// <summary>
	/// Called for unhandled input events.
	/// </summary>
	public virtual void OnInput(InputEvent @event)
	{
	}
}
