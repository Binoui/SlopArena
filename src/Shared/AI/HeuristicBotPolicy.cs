using System;

namespace SlopArena.Shared.AI;

/// <summary>
/// Deterministic heuristic bot policy v1 (issue #148): approach the opponent, attack when the
/// opponent is within the bot's (seeded-jittered) perceived range, pick from the move set with
/// seeded variation, back off after each swing and after being hit so fights develop spacing,
/// and jump toward an airborne opponent. The seeded jitter makes the bot sometimes attack at
/// the edge of its reach (whiffs) and use a mix of moves — without it, two identical bots
/// trading at guaranteed-connect range produce degenerate telemetry (100% hit rate, no KOs).
///
/// Attack reach is the move's ACTUAL hitbox reach (OffX/OffZ extent + radius + lunge), NOT the
/// authored <c>AttackRange</c> (the sim's auto-dash engage distance, far beyond where the hitbox
/// connects). The sim normalizes <c>MoveX/Y</c> (no analog easing), so the policy approaches
/// outright rather than trying to slow precisely.
///
/// The sim consumes <see cref="InputState.MoveX"/>/<c>MoveY</c> as WORLD-SPACE X/Z and auto-faces
/// on movement input (<c>Atan2</c>); the policy also snaps facing via <c>AimYaw</c> +
/// <c>FaceToCamera</c> (ADR-0017).
///
/// Invariants: <c>MoveX/MoveY</c> magnitude ≤ 1; no action input while
/// hitstun/hitstop/burst-recovery/landing-lag/anim-lock; no dash on cooldown; no slot press on
/// cooldown. Same <c>Random</c> stream → same decisions.
/// </summary>
public sealed class HeuristicBotPolicy
{
    /// <summary>Ground-normal priority pool — ActiveSlot bytes for kit slots 1–4.</summary>
    private static readonly byte[] Slots =
    {
        AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
    };

    /// <summary>Jump toward an airborne opponent once they are this far above the bot (metres).</summary>
    private const float JumpGap = 0.6f;

    /// <summary>Victim hurtbox radius added to the hitbox reach to get the connect distance.</summary>
    private const float VictimRadiusMargin = 0.2f;

    /// <summary>
    /// Choose a slot only if its perceived connect reach is at least this fraction of the
    /// distance. The profile's range error intentionally permits early and late commitments.
    /// </summary>
    private const float SlotReachTolerance = 0.85f;

    public InputState Decide(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory)
    {
        var profile = BotDifficultyProfile.ForLevel(memory.DifficultyLevel);
        if (memory.DecisionTicksRemaining > 0) memory.DecisionTicksRemaining--;
        if (memory.ReactionTicksRemaining > 0) memory.ReactionTicksRemaining--;

        bool movementAllowed = self.HitstunTicks == 0
            && self.HitstopTicks == 0
            && self.BurstRecoveryTicks == 0
            && self.LandingLagTicks == 0;
        bool actionable = movementAllowed
            && self.AnimLockTicks == 0
            && (self.State == ActionState.Idle || self.State == ActionState.Run);

        var input = new InputState();
        if (!actionable)
        {
            memory.WasActionable = false;
            return input;
        }

        float dx = target.PX - self.PX;
        float dz = target.PZ - self.PZ;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        float dy = target.PY - self.PY;
        bool hasTarget = dist > 0.001f;
        bool targetAttacking = target.State is ActionState.Attacking or ActionState.Aiming or ActionState.Warping;
        bool targetThreatening = IsThreatening(target);

        // The runner records the previous pre-tick target state. A rising threat starts the
        // profile's reaction delay; the bot still faces and approaches during that delay.
        if (targetThreatening && !memory.LastTargetWasAttacking)
            memory.ReactionTicksRemaining = Math.Max(memory.ReactionTicksRemaining, profile.ReactionDelayTicks);

        if (hasTarget)
        {
            input.AimYaw = (short)Math.Clamp(
                MathF.Atan2(dx, dz) * (180f / MathF.PI) * 100f, -32768f, 32767f);
            input.FaceToCamera = true;
        }

        if (!hasTarget)
        {
            memory.WasActionable = true;
            return input;
        }

        // Far targets are always approached. Movement remains normalized and uses world X/Z,
        // matching Simulation.ProcessNormalMovement's input contract.
        float rangeScale = 1f + (((float)rng.NextDouble() * 2f) - 1f) * profile.RangeError;
        float perceivedMaxReach = MaxConnectReach(self, def) * rangeScale;
        bool inRange = dist <= perceivedMaxReach;
        if (!inRange)
        {
            input.MoveX = dx / dist;
            input.MoveY = dz / dist;
        }

        bool waiting = memory.DecisionTicksRemaining > 0 || memory.ReactionTicksRemaining > 0;
        if (waiting)
        {
            memory.WasActionable = true;
            return input;
        }

        // Every fresh decision gets a cadence gate, including a no-op/approach decision.
        memory.DecisionTicksRemaining = profile.DecisionIntervalTicks;
        if (!inRange)
        {
            memory.WasActionable = true;
            return input;
        }

        // Hold range by default once inside the perceived band. Individual branches below
        // replace this with a strafe, retreat, or dash vector.
        input.MoveX = 0f;
        input.MoveY = 0f;

        bool targetIsHigherOrAirborne = !target.IsGrounded || dy > JumpGap;
        if (self.IsGrounded && targetIsHigherOrAirborne
            && rng.NextDouble() < profile.JumpChance)
        {
            input.Jump = true;
            input.JumpHeld = true;
            memory.WasActionable = true;
            return input;
        }

        byte? slot = ChooseSlot(self, def, dist, rangeScale, rng);
        bool canDash = self.DashCooldownTicks == 0
            && self.DashDurationTicks == 0
            && self.BurstRecoveryTicks == 0;

        // A dash is a defensive choice only against a visible threat and only when the
        // simulation's cooldown contract says it can start.
        if (targetAttacking && canDash && rng.NextDouble() < profile.DodgeChance)
        {
            input.Dash = true;
            input.MoveX = -dx / dist;
            input.MoveY = -dz / dist;
            memory.WasActionable = true;
            return input;
        }

        // Punishes and confirmed-hit follow-ups are bonuses to the normal attack roll, not
        // unconditional attacks. AttackSlot is never consulted as a hit/start/end signal.
        bool punish = slot.HasValue
            && targetThreatening
            && rng.NextDouble() < profile.PunishChance;
        bool combo = slot.HasValue
            && memory.LastAttackConnected
            && rng.NextDouble() < profile.ComboChance;
        bool attack = slot.HasValue
            && (punish || combo || rng.NextDouble() < profile.AttackChance);
        if (attack)
        {
            input.ActiveSlot = slot!.Value;
            memory.LastPressedSlot = input.ActiveSlot;
            memory.WasActionable = true;
            return input;
        }

        // Retreat only has an explicit reason: an active threat, point-blank spacing without
        // a usable move, or the profile's voluntary retreat roll. A prior attack press alone
        // never creates a backoff window.
        bool pointBlankWithoutAttack = dist <= 0.35f && !slot.HasValue;
        bool retreat = targetThreatening
            || pointBlankWithoutAttack
            || rng.NextDouble() < profile.RetreatChance;
        if (retreat)
        {
            input.MoveX = -dx / dist;
            input.MoveY = -dz / dist;
            memory.WasActionable = true;
            return input;
        }

        if (memory.StrafeDirection == 0)
            memory.StrafeDirection = rng.Next(2) == 0 ? (sbyte)-1 : (sbyte)1;
        input.MoveX = -dz / dist * memory.StrafeDirection;
        input.MoveY = dx / dist * memory.StrafeDirection;
        memory.WasActionable = true;
        return input;
    }

    private static bool IsThreatening(in CharacterState state)
    {
        return state.State is ActionState.Attacking or ActionState.Aiming or ActionState.Warping
            || state.AnimLockTicks > 0
            || state.LandingLagTicks > 0
            || state.BurstRecoveryTicks > 0;
    }

    /// <summary>Max connect range across the slots for the current state (metres).</summary>
    private static float MaxConnectReach(in CharacterState self, CharacterDefinition def)
    {
        bool air = !self.IsGrounded;
        float max = 0f;
        foreach (byte slot in Slots)
            max = Math.Max(max, ForwardReach(def, slot, air) + VictimRadiusMargin);
        return max;
    }

    /// <summary>
    /// Pick a slot whose perceived connect reach is within tolerance of the distance. Skips
    /// cooldown / data-less slots and uses only the caller's seeded RNG for tie-breaking.
    /// </summary>
    private static byte? ChooseSlot(in CharacterState self, CharacterDefinition def, float dist,
        float rangeScale, Random rng)
    {
        bool air = !self.IsGrounded;
        var viable = new byte[Slots.Length];
        int n = 0;
        foreach (byte slot in Slots)
        {
            if (self.GetCooldown(slot) > 0) continue;
            float reach = (ForwardReach(def, slot, air) + VictimRadiusMargin) * rangeScale;
            if (reach > 0f && reach >= dist * SlotReachTolerance)
                viable[n++] = slot;
        }
        if (n == 0) return null;
        return viable[rng.Next(n)];
    }

    /// <summary>
    /// Forward reach of a slot's hitboxes in the facing frame (metres): max forward extent
    /// (OffZ + radius) over the first-stage hitboxes, plus lunge travel. This is the actual
    /// hitbox reach, deliberately not the authored auto-dash AttackRange.
    /// </summary>
    public static float ForwardReach(CharacterDefinition def, byte activeSlot, bool airborne)
    {
        var spec = def.GetSlotAbility(activeSlot - 1, airborne);
        if (spec == null || spec.Stages == null || spec.Stages.Length == 0) return 0f;
        var stage = spec.Stages[0];

        float reach = 0f;
        if (stage.HitboxEvents != null)
        {
            foreach (var evt in stage.HitboxEvents)
            {
                float extent = MathF.Max(0f, evt.OffZ + evt.Radius);
                if (extent > reach) reach = extent;
            }
        }
        if (stage.LungeForce > 0f)
            reach += stage.LungeForce * MathF.Min(0.3f, stage.DurationTicks / 60f);
        return reach;
    }
}
