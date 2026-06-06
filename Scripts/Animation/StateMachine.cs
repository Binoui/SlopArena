#nullable enable
using Godot;
using System.Collections.Generic;

/// <summary>
/// Custom finite state machine that manages State child nodes.
/// Each child State is auto-registered by its Node name (lowercased, 'state' suffix stripped).
///
/// Usage:
///   var fsm = new StateMachine();
///   fsm.Name = "FSM";
///   AddChild(fsm);
///   fsm.AddState(new IdleState());
///   fsm.Initialize(player, movement);
///   fsm.TransitionTo("idle");
/// </summary>
public partial class StateMachine : Node
{
	private readonly Dictionary<string, State> _states = new();
	private AnimationNodeStateMachinePlayback? _animPlayback;
	private PlayerController? _player;
	private MovementComponent? _movement;

	public State? CurrentState { get; private set; }
	public string CurrentStateName => CurrentState?.Name ?? "";

	// ── SETUP ──

	/// <summary>
	/// Must be called after adding all states and finding the AnimationTree.
	/// Binds references to each state and sets up the animation playback.
	/// </summary>
	public void Initialize(PlayerController player, MovementComponent movement)
	{
		_player = player;
		_movement = movement;

		// Find AnimationTree with a StateMachine root (parameters/playback)
		var animTree = FindAnimationTree(player);
		if (animTree != null)
		{
			animTree.Active = true;
			var val = animTree.Get("parameters/playback");
			if (val.Obj is AnimationNodeStateMachinePlayback pb)
				_animPlayback = pb;
			else
				GD.PrintErr("[FSM] AnimationTree root is not a StateMachine — no parameters/playback");
		}
		else
		{
			GD.PrintErr("[FSM] No AnimationTree found on player or model");
		}

		// Register all State children
		_states.Clear();
		foreach (var child in GetChildren())
		{
			if (child is State st)
				RegisterState(st);
		}

		// Bind references
		foreach (var kvp in _states)
			kvp.Value.Setup(this, player, movement, _animPlayback!);
	}

	/// <summary>
	/// Add a state programmatically. Must be called before Initialize().
	/// </summary>
	public void AddState(State state)
	{
		state.Name = SanitizeStateName(state.GetType().Name);
		AddChild(state);
	}

	private void RegisterState(State state)
	{
		string name = SanitizeStateName(state.Name);
		_states[name] = state;
	}

	private static string SanitizeStateName(string raw)
	{
		string name = raw.ToLower();
		if (name.EndsWith("state"))
			name = name[..^5];
		return name;
	}

	private static AnimationTree? FindAnimationTree(Node root)
	{
		// First try direct child
		var at = root.GetNodeOrNull<AnimationTree>("AnimationTree");
		if (at != null) return at;

		// Then search model children
		foreach (var child in root.GetChildren())
		{
			if (child is AnimationTree t) return t;
			var found = FindAnimationTree(child);
			if (found != null) return found;
		}
		return null;
	}

	// ── LIFECYCLE ──

	public override void _Process(double delta)
	{
		CurrentState?.OnProcess((float)delta);
	}

	public override void _PhysicsProcess(double delta)
	{
		CurrentState?.OnPhysicsProcess((float)delta);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		CurrentState?.OnInput(@event);
	}

	// ── TRANSITIONS ──

	/// <summary>
	/// Transition to a state by name (case-insensitive).
	/// </summary>
	public void TransitionTo(string stateName)
	{
		string key = stateName.ToLower();
		if (!_states.TryGetValue(key, out var newState))
		{
			GD.PrintErr($"[FSM] No state '{stateName}' — registered: {string.Join(", ", _states.Keys)}");
			return;
		}

		if (newState == CurrentState)
			return;
		
		GD.Print($"[FSM] state '{CurrentState?.Name}' —> '{newState?.Name}'");

		CurrentState?.Exit();
		CurrentState = newState;
		newState.Enter();
	}

	public bool IsInState(string stateName)
	{
		return CurrentStateName.Equals(stateName, System.StringComparison.OrdinalIgnoreCase);
	}

	public bool CanMove() => CurrentState?.CanMove ?? true;

	/// <summary>
	/// Get a registered state by name. Returns null if not found.
	/// </summary>
	public AttackState? GetAttackState()
	{
		return _states.TryGetValue("attack", out var state) ? state as AttackState : null;
	}
}
