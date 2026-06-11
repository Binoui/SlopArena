#nullable enable
using Godot;
using SlopArena.Shared;

/// <summary>
/// Generic attack state for all ability slots.
/// PlayerController sets NextAnimName + pending stages before TransitionTo("attack")
/// or calls ChainTo() for combo chaining within the same state.
///
/// Hitbox is NOT spawned immediately — StartupTicks delay before stages resolve.
/// </summary>
public sealed partial class AttackState : State
{
    public AttackState()
    {
        AnimationName = "melee";
        CanMove = false;
    }

    /// <summary>Set by PlayerController before TransitionTo("attack").</summary>
    public string NextAnimName { get; set; } = "";

    /// <summary>
    /// ── Pending stage resolution (startup delay) ──
    /// </summary>
    private AttackStage[]? _pendingStages;
    private int _pendingSlotIndex;
    private bool _pendingCharged;
    private bool _pendingAirborne;
    private ushort _startupTicksRemaining;
    private bool _hasPendingResolve;

    /// <summary>Set pending stages by PlayerController. Resolved after StartupTicks.</summary>
    public void SetPendingResolve(AttackStage[] stages, int slotIndex, bool charged, bool airborne, ushort startupTicks)
    {
        _pendingStages = stages;
        _pendingSlotIndex = slotIndex;
        _pendingCharged = charged;
        _pendingAirborne = airborne;
        _startupTicksRemaining = startupTicks;
        _hasPendingResolve = true;
        // Lock during startup so AttackState.OnProcess doesn't transition away
        // Use ref to modify the struct in-place (CharacterState is a value type)
        ref var state = ref Movement.State;
        state.AnimLockTicks = startupTicks;
    }

    public bool HasPendingResolve => _hasPendingResolve;
    public ushort StartupTicksRemaining => _startupTicksRemaining;

    /// <summary>
	/// Decrement startup timer. When expired, called by PlayerController's tick.
	/// Returns true if startup just completed this frame.
	/// </summary>
	public bool TickStartup()
    {
        if (!_hasPendingResolve) return false;
        if (_startupTicksRemaining > 0)
        {
            _startupTicksRemaining--;
            return false;
        }
        return true; // ready to resolve
    }

    /// <summary>Called by PlayerController after resolving stages.</summary>
    public void ClearPendingResolve()
    {
        _hasPendingResolve = false;
        _pendingStages = null;
    }

    /// <summary>
    /// ── Stage data access for PlayerController ──
    /// </summary>
    public AttackStage[]? PendingStages => _pendingStages;
    public int PendingSlotIndex => _pendingSlotIndex;
    public bool PendingCharged => _pendingCharged;
    public bool PendingAirborne => _pendingAirborne;

    public override void Enter()
    {
        if (!string.IsNullOrEmpty(NextAnimName))
            AnimationName = NextAnimName;
        Player.SetModelEmission(new Color(1.0f, 0.2f, 0.2f), 2.0f); // Red
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
        Player.ClearModelEmission();
        base.Exit();
        _hasPendingResolve = false;
        _pendingStages = null;
        ref var s = ref Movement.State;
        s.BufferedChain = 0;
    }

    public override void OnProcess(float delta)
    {
        if (Movement.State.AnimLockTicks > 0)
            return;

        // Don't leave attack state while stages are pending (startup hasn't resolved yet)
        if (_hasPendingResolve)
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
