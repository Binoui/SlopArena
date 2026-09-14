using System;

namespace SlopArena.Shared.AI;

/// <summary>
/// Immutable opponent information made available to one CPU after its reaction delay.
/// It contains simulation observations only; input commands are never captured.
/// </summary>
public readonly struct CpuObservation
{
    public readonly float PX, PY, PZ;
    public readonly float VX, VY, VZ;
    public readonly ActionState State;
    public readonly ushort StateTicks;
    public readonly bool IsGrounded;
    public readonly ushort DamagePercent;
    public readonly ushort AttackElapsedTicks;
    public readonly byte AttackSlot, ComboStage;
    public readonly ushort ComboTimerTicks, AnimLockTicks, LandingLagTicks, ChargeTicks;
    public readonly ushort HitstunTicks, HitstopTicks, BurstRecoveryTicks;
    public readonly bool WasHit;

    private CpuObservation(in CharacterState state, bool wasHit)
    {
        PX = state.PX;
        PY = state.PY;
        PZ = state.PZ;
        VX = state.VX;
        VY = state.VY;
        VZ = state.VZ;
        State = state.State;
        StateTicks = state.StateTicks;
        IsGrounded = state.IsGrounded;
        DamagePercent = state.DamagePercent;
        AttackElapsedTicks = state.AttackElapsedTicks;
        AttackSlot = state.AttackSlot;
        ComboStage = state.ComboStage;
        AnimLockTicks = state.AnimLockTicks;
        ChargeTicks = state.ChargeTicks;
        HitstunTicks = state.HitstunTicks;
        HitstopTicks = state.HitstopTicks;
        BurstRecoveryTicks = state.BurstRecoveryTicks;
        WasHit = wasHit;
    }

    internal static CpuObservation Capture(in CharacterState state, bool wasHit)
        => new(state, wasHit);

    public bool IsThreatening
        => State is ActionState.Attacking or ActionState.Aiming or ActionState.Warping
            || AnimLockTicks > 0
            || LandingLagTicks > 0
            || BurstRecoveryTicks > 0;
}

/// <summary>Execution phase for one selected CPU action.</summary>
internal enum BotPlanPhase : byte
{
    None,
    PendingPress,
    AimHold,
}

/// <summary>
/// Persistent per-entity bot state that spans ticks. Held by the runner/controller — never
/// written into <see cref="CharacterState"/>, so the prediction wire is untouched.
/// </summary>
public sealed class BotMemory
{
    private const int MaximumReactionDelayTicks = 24;
    private readonly CpuObservation[] _opponentHistory = new CpuObservation[MaximumReactionDelayTicks + 1];
    private int _opponentHistoryCount;
    private int _opponentHistoryWriteIndex;
    private bool _opponentHitPending;

    /// <summary>Named CPU difficulty selected for this bot.</summary>
    public CpuDifficulty Difficulty = CpuDifficulty.Normal;

    /// <summary>Ticks remaining before a fresh attack/pressure decision.</summary>
    public int DecisionTicksRemaining;

    /// <summary>True for the previous tick when this bot's hitbox connected.</summary>
    public bool LastAttackConnected;

    /// <summary>Stable lateral direction for range-holding: -1 or +1 once selected.</summary>
    public sbyte StrafeDirection;

    /// <summary>
    /// One ordinary input plan selected by the policy. It carries both the initial press and
    /// any required aim/hold/release sequence without entering CharacterState.
    /// </summary>
    internal BotPlanPhase PlanPhase;
    internal byte PlanSlot;
    internal ushort PlanTicks;
    internal ushort PlanHoldTicks;
    internal short PlanAimYaw;
    internal short PlanAimPitch;
    internal ushort PlanAimDistance;
    internal byte PlanDeaths;
    internal bool PlanWasAirborne;

    /// <summary>
    /// Record the opponent's current simulation state. The policy can only retrieve it after
    /// the selected difficulty's delay has elapsed.
    /// </summary>
    public void ObserveOpponent(in CharacterState state)
    {
        _opponentHistory[_opponentHistoryWriteIndex] =
            CpuObservation.Capture(state, _opponentHitPending);
        _opponentHitPending = false;
        _opponentHistoryWriteIndex = (_opponentHistoryWriteIndex + 1) % _opponentHistory.Length;
        if (_opponentHistoryCount < _opponentHistory.Length)
            _opponentHistoryCount++;
    }

    /// <summary>Record an authoritative resolver outcome for the next opponent observation.</summary>
    public void RecordOpponentHit() => _opponentHitPending = true;

    internal bool TryGetDelayedOpponent(out CpuObservation observation)
    {
        int delay = BotDifficultyProfile.ForDifficulty(Difficulty).ReactionDelayTicks;
        if (_opponentHistoryCount <= delay)
        {
            observation = default;
            return false;
        }

        int index = _opponentHistoryWriteIndex - 1 - delay;
        if (index < 0) index += _opponentHistory.Length;
        observation = _opponentHistory[index];
        return true;
    }

    /// <summary>
    /// Reset runner-owned transient state before a match or CPU life starts. The selected
    /// difficulty is intentionally preserved across respawn and life resets.
    /// </summary>
    public void Reset()
    {
        DecisionTicksRemaining = 0;
        LastAttackConnected = false;
        StrafeDirection = 0;
        ClearPlan();
        _opponentHistoryCount = 0;
        _opponentHistoryWriteIndex = 0;
        _opponentHitPending = false;
    }

    internal void StartPlan(byte slot, bool requiresAim, short aimYaw, short aimPitch,
        ushort aimDistance, ushort holdTicks, byte deaths, bool wasGrounded)
    {
        PlanPhase = requiresAim ? BotPlanPhase.AimHold : BotPlanPhase.PendingPress;
        PlanSlot = slot;
        PlanTicks = 0;
        PlanHoldTicks = holdTicks;
        PlanAimYaw = aimYaw;
        PlanAimPitch = aimPitch;
        PlanAimDistance = aimDistance;
        PlanDeaths = deaths;
        PlanWasAirborne = !wasGrounded;
    }

    internal void ClearPlan()
    {
        PlanPhase = BotPlanPhase.None;
        PlanSlot = 0;
        PlanTicks = 0;
        PlanHoldTicks = 0;
        PlanAimYaw = 0;
        PlanAimPitch = 0;
        PlanAimDistance = 0;
        PlanDeaths = 0;
        PlanWasAirborne = false;
    }
}
