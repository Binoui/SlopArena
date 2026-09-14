using System;
using System.Linq;

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
    /// <summary>
    /// Canonical functional combat slots: grounded/aerial 1, 2, 3, 4, A, E, R, F.
    /// LMB/RMB are physical controls with no canonical package slot identity.
    /// </summary>
    private static readonly byte[] Slots =
    {
        AbilitySlots.Slot1, AbilitySlots.Slot2, AbilitySlots.Slot3, AbilitySlots.Slot4,
        AbilitySlots.A, AbilitySlots.E, AbilitySlots.R, AbilitySlots.F,
    };

    private const float JumpGap = 0.6f;
    private const float VictimRadiusMargin = 0.2f;
    private const float SlotReachTolerance = 0.85f;
    private const ushort AimHoldTicks = 12;

    private readonly struct MoveCandidate
    {
        public readonly byte Slot;
        public readonly float Reach;
        public readonly float Damage;
        public readonly bool Functional;
        public readonly bool HasDamage;
        public readonly bool HasMovement;
        public readonly bool RequiresAim;
        public readonly bool IsRecovery;
        public readonly ushort StartupTicks;
        public readonly ushort TravelTicks;
        public readonly float TravelSpeed;
        public readonly float TravelOffset;

        public MoveCandidate(byte slot, float reach, float damage, bool functional,
            bool hasDamage, bool hasMovement, bool requiresAim, bool isRecovery,
            ushort startupTicks, ushort travelTicks = 0, float travelSpeed = 0f,
            float travelOffset = 0f)
        {
            Slot = slot;
            Reach = MathF.Max(0f, reach);
            Damage = MathF.Max(0f, damage);
            Functional = functional;
            HasDamage = hasDamage;
            HasMovement = hasMovement;
            RequiresAim = requiresAim;
            IsRecovery = isRecovery;
            StartupTicks = startupTicks;
            TravelTicks = travelTicks;
            TravelSpeed = MathF.Max(0f, travelSpeed);
            TravelOffset = MathF.Max(0f, travelOffset);
        }
    }

    private CharacterDefinition? _profileDefinition;
    private BakedAnimationData? _profileBaked;
    private MoveCandidate[] _profile = Array.Empty<MoveCandidate>();
    private MoveCandidate[] _airProfile = Array.Empty<MoveCandidate>();

    public InputState Decide(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory)
        => Decide(self, target, def, rng, memory, baked: null);

    /// <summary>
    /// Convenience entry point for local adapters. The target is sampled before the decision,
    /// and only the delayed observation is read by the policy.
    /// </summary>
    public InputState Decide(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory, BakedAnimationData? baked)
    {
        memory.ObserveOpponent(target);
        return Decide(self, def, rng, memory, baked);
    }

    public InputState Decide(in CharacterState self, CharacterDefinition def,
        Random rng, BotMemory memory, BakedAnimationData? baked = null)
    {
        EnsureProfile(def, baked);

        if (memory.PlanPhase != BotPlanPhase.None)
        {
            if (!self.IsGrounded)
                memory.PlanWasAirborne = true;
            return ContinuePlan(self, def, memory);
        }
        var profile = BotDifficultyProfile.ForDifficulty(memory.Difficulty);
        if (memory.DecisionTicksRemaining > 0)
            memory.DecisionTicksRemaining--;

        bool movementAllowed = self.HitstunTicks == 0
            && self.HitstopTicks == 0
            && self.BurstRecoveryTicks == 0
            && self.LandingLagTicks == 0;
        bool actionable = movementAllowed
            && (self.AnimLockTicks == 0 || Simulation.IsIasaUnlocked(self, def))
            && (self.State == ActionState.Idle
                || self.State == ActionState.Run
                || Simulation.IsIasaUnlocked(self, def));

        var input = new InputState();
        if (!actionable || !memory.TryGetDelayedOpponent(out var target))
            return input;

        if (memory.TryGetNewHit(out var hit))
        {
            QueueFollowUp(self, target, hit, def, profile, rng, memory);
            memory.MarkHitEvaluated(hit);
            if (memory.PlanPhase != BotPlanPhase.None)
                return ContinuePlan(self, def, memory);
        }

        float dx = target.PX - self.PX;
        float dz = target.PZ - self.PZ;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        float dy = target.PY - self.PY;
        bool hasTarget = dist > 0.001f;
        if (!hasTarget)
            return input;

        input.AimYaw = AimYaw(dx, dz);
        input.FaceToCamera = true;

        // Range error is an intentional tier-specific spacing error around real reach.
        // The old constant 2x scale made every move appear twice as long as its resolved
        // hitbox/movement envelope.
        float rangeScale = 1f + (((float)rng.NextDouble() * 2f) - 1f) * profile.RangeError;
        float maxReach = MaxConnectReach(self, rangeScale);
        bool inRange = dist <= maxReach;
        if (!inRange)
        {
            input.MoveX = dx / dist;
            input.MoveY = dz / dist;
        }

        if (memory.DecisionTicksRemaining > 0)
            return input;

        memory.DecisionTicksRemaining = profile.DecisionIntervalTicks;
        if (!inRange)
            return input;

        input.MoveX = 0f;
        input.MoveY = 0f;

        bool targetIsHigherOrAirborne = !target.IsGrounded || dy > JumpGap;
        if (self.IsGrounded && targetIsHigherOrAirborne
            && rng.NextDouble() < profile.JumpChance)
        {
            input.Jump = true;
            input.JumpHeld = true;
            return input;
        }

        bool targetThreatening = target.IsThreatening;
        var candidate = ChooseSlot(self, target, dist, rangeScale, rng);
        bool canDash = self.DashCooldownTicks == 0
            && self.DashDurationTicks == 0
            && self.BurstRecoveryTicks == 0;

        bool punish = candidate.HasValue
            && targetThreatening
            && rng.NextDouble() < profile.PunishChance;
        bool attack = candidate.HasValue
            && (punish || rng.NextDouble() < profile.AttackChance);
        if (attack)
        {
            var selected = candidate.GetValueOrDefault();
            input.ActiveSlot = selected.Slot;
            input.AimPitch = AimPitch(dy, dist);
            input.AimDistance = AimDistance(dist);
            memory.StartPlan(selected.Slot, selected.RequiresAim,
                input.AimYaw, input.AimPitch, input.AimDistance,
                selected.RequiresAim ? AimHoldTicks : (ushort)0,
                self.Deaths, self.IsGrounded);
            if (selected.RequiresAim)
                input.IsAiming = true;
            return input;
        }

        if (targetThreatening && canDash && rng.NextDouble() < profile.DodgeChance)
        {
            input.Dash = true;
            input.MoveX = -dx / dist;
            input.MoveY = -dz / dist;
            return input;
        }

        bool pointBlankWithoutAttack = dist <= 0.35f && !candidate.HasValue;
        bool retreat = targetThreatening
            || pointBlankWithoutAttack
            || rng.NextDouble() < profile.RetreatChance;
        if (retreat)
        {
            input.MoveX = -dx / dist;
            input.MoveY = -dz / dist;
            return input;
        }

        if (memory.StrafeDirection == 0)
            memory.StrafeDirection = rng.Next(2) == 0 ? (sbyte)-1 : (sbyte)1;
        input.MoveX = -dz / dist * memory.StrafeDirection;
        input.MoveY = dx / dist * memory.StrafeDirection;
        return input;
    }

    private InputState ContinuePlan(in CharacterState self, CharacterDefinition def, BotMemory memory)
    {
        if (PlanInvalidated(self, memory))
        {
            memory.ClearPlan();
            return default;
        }

        if (!memory.PlanPressIssued)
        {
            if (!CanPress(self, def, memory.PlanSlot))
                return default;

            memory.PlanPressIssued = true;
            return PlanPressInput(memory);
        }

        if (memory.PlanPhase == BotPlanPhase.AimHold
            && self.State == ActionState.Aiming
            && self.AttackSlot == memory.PlanSlot)
        {
            bool aiming = memory.PlanTicks < memory.PlanHoldTicks;
            var planned = PlanInput(memory, aiming);
            if (aiming)
                memory.PlanTicks++;
            else
                memory.ClearPlan();
            return planned;
        }

        // A non-aim press was accepted, or an aim plan reached its release phase.
        if ((self.State is ActionState.Attacking or ActionState.Aiming)
            && self.AttackSlot == memory.PlanSlot)
        {
            memory.ClearPlan();
            return default;
        }

        // The simulation rejected or interrupted the precommitment. Do not replay it
        // from a fresh current-state read.
        memory.ClearPlan();
        return default;
    }

    private static bool CanPress(in CharacterState self, CharacterDefinition def, byte slot)
        => self.HitstunTicks == 0
            && self.HitstopTicks == 0
            && self.BurstRecoveryTicks == 0
            && self.LandingLagTicks == 0
            && self.AnimLockTicks == 0
            && (self.State == ActionState.Idle || self.State == ActionState.Run)
            && self.GetCooldown(slot) == 0
            && (def.GetCookedSlotAbility(slot, !self.IsGrounded) != null
                || def.GetSlotAbility(slot - 1, !self.IsGrounded) != null);

    private static InputState PlanPressInput(BotMemory memory)
        => new()
        {
            ActiveSlot = memory.PlanSlot,
            IsAiming = memory.PlanPhase == BotPlanPhase.AimHold,
            AimYaw = memory.PlanAimYaw,
            AimPitch = memory.PlanAimPitch,
            AimDistance = memory.PlanAimDistance,
        };

    private void QueueFollowUp(in CharacterState self, in CpuObservation target,
        in CpuHitObservation hit, CharacterDefinition def, BotDifficultyProfile profile,
        Random rng, BotMemory memory)
    {
        float rangeScale = 1f + (((float)rng.NextDouble() * 2f) - 1f) * profile.RangeError;
        var trueCombo = ChooseTrueCombo(self, hit, memory.RemainingHitstun(hit), rangeScale, rng);
        if (trueCombo.HasValue && rng.NextDouble() < profile.ComboChance)
        {
            var selected = trueCombo.GetValueOrDefault();
            short yaw = AimYaw(hit.PX - self.PX, hit.PZ - self.PZ);
            memory.QueueFollowUp(selected.Slot, BotPlanKind.TrueCombo, yaw,
                AimPitch(hit.PY - self.PY, Distance(hit.PX - self.PX, hit.PZ - self.PZ)),
                AimDistance(Distance(hit.PX - self.PX, hit.PZ - self.PZ)),
                selected.RequiresAim ? AimHoldTicks : (ushort)0, self.Deaths, self.IsGrounded);
            return;
        }

        float dx = target.PX - self.PX;
        float dz = target.PZ - self.PZ;
        float dist = Distance(dx, dz);
        var pressure = ChooseSlot(self, target, dist, rangeScale, rng);
        if (!pressure.HasValue || (!target.IsThreatening && memory.RemainingHitstun(hit) == 0))
            return;
        if (!target.IsThreatening && rng.NextDouble() >= profile.AttackChance)
            return;

        var pressureMove = pressure.GetValueOrDefault();
        memory.QueueFollowUp(pressureMove.Slot, BotPlanKind.PressureString,
            AimYaw(dx, dz), AimPitch(target.PY - self.PY, dist), AimDistance(dist),
            pressureMove.RequiresAim ? AimHoldTicks : (ushort)0, self.Deaths, self.IsGrounded);
    }

    private MoveCandidate? ChooseTrueCombo(in CharacterState self, in CpuHitObservation hit,
        int remainingHitstun, float rangeScale, Random rng)
    {
        if (remainingHitstun <= 0)
            return null;

        bool air = !self.IsGrounded;
        Span<MoveCandidate> viable = stackalloc MoveCandidate[Slots.Length];
        int count = 0;
        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (!candidate.Functional || !candidate.HasDamage
                || self.GetCooldown(candidate.Slot) > 0
                || IsChargePoolExhausted(self, candidate.Slot, air))
                continue;

            int timing = candidate.StartupTicks;
            float predictedX = hit.PX + hit.VX * timing * Simulation.TickDt;
            float predictedZ = hit.PZ + hit.VZ * timing * Simulation.TickDt;
            float dist = Distance(predictedX - self.PX, predictedZ - self.PZ);
            if (candidate.TravelSpeed > 0f)
            {
                float flightDistance = MathF.Max(0f, dist - candidate.TravelOffset);
                int travel = (int)MathF.Ceiling(flightDistance / candidate.TravelSpeed
                    * (1f / Simulation.TickDt));
                timing += Math.Min(travel, candidate.TravelTicks);
                predictedX = hit.PX + hit.VX * timing * Simulation.TickDt;
                predictedZ = hit.PZ + hit.VZ * timing * Simulation.TickDt;
                dist = Distance(predictedX - self.PX, predictedZ - self.PZ);
            }
            else
            {
                timing += candidate.TravelTicks;
                predictedX = hit.PX + hit.VX * timing * Simulation.TickDt;
                predictedZ = hit.PZ + hit.VZ * timing * Simulation.TickDt;
                dist = Distance(predictedX - self.PX, predictedZ - self.PZ);
            }
            if (timing <= remainingHitstun
                && dist <= (candidate.Reach + VictimRadiusMargin) * rangeScale)
                viable[count++] = candidate;
        }

        if (count == 0)
            return null;

        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
            bestScore = MathF.Max(bestScore, Score(viable[i], viable[i].Reach, false));
        Span<MoveCandidate> best = stackalloc MoveCandidate[Slots.Length];
        int bestCount = 0;
        for (int i = 0; i < count; i++)
            if (Score(viable[i], viable[i].Reach, false) >= bestScore - 2f)
                best[bestCount++] = viable[i];
        return best[rng.Next(bestCount)];
    }

    private static float Distance(float x, float z)
        => MathF.Sqrt(x * x + z * z);

    private static bool PlanInvalidated(in CharacterState self, BotMemory memory)
        => self.Deaths != memory.PlanDeaths
            || self.HitstunTicks > 0
            || self.LandingLagTicks > 0
            || self.BurstRecoveryTicks > 0
            || (memory.PlanWasAirborne && self.IsGrounded)
            || (memory.PlanPressIssued
                && (self.State is ActionState.Attacking or ActionState.Aiming)
                && self.AttackSlot != memory.PlanSlot);

    private static InputState PlanInput(BotMemory memory, bool aiming)
        => new()
        {
            IsAiming = aiming,
            AimYaw = memory.PlanAimYaw,
            AimPitch = memory.PlanAimPitch,
            AimDistance = memory.PlanAimDistance,
        };

    private void EnsureProfile(CharacterDefinition def, BakedAnimationData? baked)
    {
        if (ReferenceEquals(_profileDefinition, def) && ReferenceEquals(_profileBaked, baked))
            return;

        _profileDefinition = def;
        _profileBaked = baked;
        _profile = new MoveCandidate[Slots.Length];
        _airProfile = new MoveCandidate[Slots.Length];
        for (int i = 0; i < Slots.Length; i++)
        {
            EvaluateMove(def, Slots[i], airborne: false, baked, out _profile[i]);
            EvaluateMove(def, Slots[i], airborne: true, baked, out _airProfile[i]);
        }
    }

    private float MaxConnectReach(in CharacterState self, float rangeScale)
    {
        bool air = !self.IsGrounded;
        float max = 0f;
        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (candidate.Functional)
                max = MathF.Max(max, (candidate.Reach + VictimRadiusMargin) * rangeScale);
        }
        return max;
    }


    private MoveCandidate? ChooseSlot(in CharacterState self, in CpuObservation target,
        float dist, float rangeScale, Random rng)
    {
        bool air = !self.IsGrounded;
        Span<MoveCandidate> viable = stackalloc MoveCandidate[Slots.Length];
        int count = 0;
        int damagingCount = 0;
        float bestDamageReach = 0f;
        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (!candidate.Functional
                || self.GetCooldown(candidate.Slot) > 0
                || IsChargePoolExhausted(self, candidate.Slot, air))
                continue;

            bool inReach = candidate.Reach > 0f
                && (candidate.Reach + VictimRadiusMargin) * rangeScale >= dist * SlotReachTolerance;
            if (inReach && candidate.HasDamage)
            {
                damagingCount++;
                bestDamageReach = MathF.Max(bestDamageReach, candidate.Reach);
            }
        }

        for (int i = 0; i < Slots.Length; i++)
        {
            var candidate = air ? _airProfile[i] : _profile[i];
            if (!candidate.Functional
                || self.GetCooldown(candidate.Slot) > 0
                || IsChargePoolExhausted(self, candidate.Slot, air))
                continue;

            bool inReach = candidate.Reach > 0f
                && (candidate.Reach + VictimRadiusMargin) * rangeScale >= dist * SlotReachTolerance;
            bool usefulMovement = !candidate.HasDamage
                && candidate.HasMovement
                && (dist > bestDamageReach + VictimRadiusMargin || !target.IsGrounded && candidate.IsRecovery);
            if (!inReach || damagingCount > 0 && !candidate.HasDamage)
            {
                if (!usefulMovement) continue;
            }

            viable[count++] = candidate;
        }

        if (count == 0)
            return null;

        // Keep all similarly good options in the seeded tie pool. There is no per-slot memory,
        // so a clearly strong response remains eligible to repeat.
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
            bestScore = MathF.Max(bestScore, Score(viable[i], dist, target.IsThreatening));

        Span<MoveCandidate> best = stackalloc MoveCandidate[Slots.Length];
        int bestCount = 0;
        for (int i = 0; i < count; i++)
            if (Score(viable[i], dist, target.IsThreatening) >= bestScore - 2f)
                best[bestCount++] = viable[i];
        return best[rng.Next(bestCount)];
    }

    private static float Score(in MoveCandidate candidate, float dist, bool threatening)
        => (candidate.HasDamage ? 100f : 10f)
            + (threatening && candidate.HasDamage ? 5f : 0f)
            + candidate.Damage
            - MathF.Abs(candidate.Reach - dist) * 0.2f
            - candidate.StartupTicks * 0.05f;

    private bool IsChargePoolExhausted(in CharacterState state, byte slot, bool air)
    {
        var cooked = _profileDefinition!.GetCookedSlotAbility(slot, air);
        var legacy = _profileDefinition.GetSlotAbility(slot - 1, air);
        int maxCharges = cooked?.ChargePool?.MaxCharges
            ?? (legacy?.Params != null && legacy.Params.TryGetValue("max_charges", out var max)
                ? (int)max : 0);
        return maxCharges > 0 && state.ChargeStockSpent >= maxCharges;
    }

    private static bool EvaluateMove(CharacterDefinition def, byte activeSlot, bool airborne,
        BakedAnimationData? baked, out MoveCandidate candidate)
    {
        var cooked = def.GetCookedSlotAbility(activeSlot, airborne);
        if (cooked != null)
        {
            bool functional = false, hasDamage = false, hasMovement = false;
            float hitReach = 0f, movementReach = 0f, damage = 0f;
            float travelSpeed = 0f, travelOffset = 0f;
            int startup = int.MaxValue;
            int travelTicks = 0;
            int stageOffset = 0;
            for (int stageIndex = 0; stageIndex < cooked.Timeline.Stages.Count; stageIndex++)
            {
                var stage = cooked.Timeline.Stages[stageIndex];
                var animationNames = stage.AnimationIds.ToArray();
                foreach (var operation in stage.Operations)
                {
                    int operationTick = stageOffset + operation.Tick;
                    switch (operation)
                    {
                        case CookedSpawnHitboxOperation hitbox:
                            startup = Math.Min(startup, operationTick);
                            functional = true;
                            var evt = ToHitboxEvent(hitbox.Hitbox);
                            float poseReach = PoseForwardReach(def, evt, activeSlot, airborne,
                                animationNames, (byte)stageIndex, baked);
                            hitReach = MathF.Max(hitReach,
                                poseReach > 0f ? poseReach : DirectForwardReach(evt));
                            hasDamage |= hitbox.Hitbox.Damage > 0f;
                            damage += MathF.Max(0f, hitbox.Hitbox.Damage);
                            break;
                        case CookedSpawnProjectileOperation projectile:
                            startup = Math.Min(startup, operationTick);
                            travelTicks = Math.Max(travelTicks, projectile.Projectile.MaxFlightTicks);
                            travelSpeed = MathF.Max(travelSpeed, projectile.Projectile.Speed);
                            travelOffset = MathF.Max(travelOffset,
                                MathF.Max(0f, projectile.Projectile.LaunchOffsetZ)
                                + projectile.Projectile.Radius);
                            functional = true;
                            hasDamage |= projectile.Projectile.Damage > 0f;
                            damage += MathF.Max(0f, projectile.Projectile.Damage);
                            hitReach = MathF.Max(hitReach,
                                travelOffset + projectile.Projectile.Speed
                                * projectile.Projectile.MaxFlightTicks / 60f);
                            break;
                        case CookedSetVelocityOperation velocity:
                            bool hasVelocity = velocity.X != 0f || velocity.Y != 0f || velocity.Z != 0f;
                            functional |= hasVelocity;
                            hasMovement |= hasVelocity;
                            movementReach = MathF.Max(movementReach,
                                MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z)
                                * MathF.Max(0, stage.DurationTicks - operation.Tick) / 60f);
                            break;
                        case CookedForwardLungeOperation lunge:
                            functional = true;
                            hasMovement = true;
                            movementReach = MathF.Max(movementReach,
                                lunge.Speed * lunge.DurationTicks / 60f);
                            break;
                        case CookedStartCapabilityOperation capability
                            when IsSupportedCapability(capability.Parameters):
                            startup = Math.Min(startup,
                                operationTick + CapabilityFirstActiveTicks(capability.Parameters));
                            CapabilityTravelParameters(capability.Parameters,
                                ref travelTicks, ref travelSpeed, ref travelOffset);
                            functional = true;
                            ApplyCapability(capability.Parameters, ref hitReach, ref movementReach,
                                ref damage, ref hasDamage, ref hasMovement);
                            break;
                    }
                }
                stageOffset += stage.DurationTicks;
            }
            candidate = new MoveCandidate(activeSlot, hitReach + movementReach, damage,
                functional, hasDamage, hasMovement,
                cooked.AimMode != AuthoringAimMode.None
                    || cooked.Behavior is AuthoringAbilityBehavior.AimedProjectile
                    or AuthoringAbilityBehavior.DirectionalDash
                    or AuthoringAbilityBehavior.ChargeAttack,
                cooked.IsRecoveryMove,
                startup == int.MaxValue ? (ushort)0 : (ushort)Math.Min(startup, ushort.MaxValue),
                (ushort)Math.Min(travelTicks, ushort.MaxValue), travelSpeed, travelOffset);
            return functional;
        }

        var spec = def.GetSlotAbility(activeSlot - 1, airborne);
        if (spec?.Stages is not { Length: > 0 })
        {
            candidate = default;
            return false;
        }

        var legacyStage = spec.Stages[0];
        bool legacyFunctional = legacyStage.HitboxEvents is { Length: > 0 }
            || legacyStage.LungeForce != 0f
            || legacyStage.MoveX != 0f || legacyStage.MoveY != 0f || legacyStage.MoveZ != 0f
            || spec.SpecialEffectKeys is { Length: > 0 };
        float legacyReach = 0f, legacyDamage = 0f;
        if (legacyStage.HitboxEvents != null)
            foreach (var evt in legacyStage.HitboxEvents)
            {
                legacyReach = MathF.Max(legacyReach,
                    PoseForwardReach(def, evt, activeSlot, airborne,
                        spec.AnimationNames ?? Array.Empty<string>(), 0, baked));
                legacyDamage += MathF.Max(0f, evt.Damage);
            }
        float lungeDuration = spec.Params != null
            && spec.Params.TryGetValue("lunge_duration", out var duration)
                ? duration : legacyStage.DurationTicks;
        float legacyMovement = legacyStage.LungeForce * MathF.Min(0.3f, lungeDuration / 60f)
            + MathF.Sqrt(legacyStage.MoveX * legacyStage.MoveX + legacyStage.MoveZ * legacyStage.MoveZ)
                * legacyStage.DurationTicks / 60f;
        candidate = new MoveCandidate(activeSlot, legacyReach + MathF.Max(0f, legacyMovement),
            legacyDamage, legacyFunctional, legacyDamage > 0f, legacyMovement != 0f,
            spec.AimMode != AimMode.None || spec.Behavior is AbilityBehavior.AimedProjectile
                or AbilityBehavior.DirectionalDash or AbilityBehavior.ChargeAttack,
            spec.IsRecoveryMove, FirstHitTick(legacyStage));
        return legacyFunctional;
    }


    private static ushort FirstHitTick(AttackStage stage)
    {
        if (stage.HitboxEvents is not { Length: > 0 }) return 0;
        ushort first = ushort.MaxValue;
        foreach (var evt in stage.HitboxEvents) first = Math.Min(first, evt.TriggerTick);
        return first == ushort.MaxValue ? (ushort)0 : first;
    }

    private static float PoseForwardReach(CharacterDefinition def, in HitboxEvent evt,
        byte activeSlot, bool airborne, string[] animationNames, byte animIndex,
        BakedAnimationData? baked)
    {
        var samples = MoveReach.SampleHit(def, evt, (byte)(activeSlot - 1), airborne,
            animationNames, animIndex, baked);
        float reach = 0f;
        foreach (var sample in samples)
            reach = MathF.Max(reach, MathF.Max(sample.Z0, sample.Z1) + sample.Radius);
        return MathF.Max(0f, reach);
    }

    private static HitboxEvent ToHitboxEvent(CookedHitbox hitbox)
        => new()
        {
            Shape = hitbox.Shape == AuthoringHitboxShape.Capsule ? HitboxShape.Capsule : HitboxShape.Sphere,
            Radius = hitbox.Radius,
            OffX = hitbox.OffsetX,
            OffY = hitbox.OffsetY,
            OffZ = hitbox.OffsetZ,
            EndOffX = hitbox.EndOffsetX,
            EndOffY = hitbox.EndOffsetY,
            EndOffZ = hitbox.EndOffsetZ,
            BoneName = hitbox.StartBoneId,
            EndBoneName = hitbox.EndBoneId,

            TriggerTick = 0,
            DurationTicks = hitbox.DurationTicks,
            Damage = hitbox.Damage,
            StunTicks = hitbox.StunTicks,
            Interruptible = hitbox.Interruptible,
            HitGroup = hitbox.HitGroup,
            KnockbackDirection = hitbox.KnockbackDirection,
        };
    private static float DirectForwardReach(in HitboxEvent evt)
        => MathF.Max(evt.OffZ, evt.EndOffZ) + evt.Radius;
    private static bool IsSupportedCapability(CookedCapabilityParameters parameters)
        => parameters is CookedKiShotCapabilityParameters
            or CookedRisingDragonCapabilityParameters
            or CookedCycloneKickCapabilityParameters
            or CookedDragonBeamCapabilityParameters
            or CookedKistuDashSlashCapabilityParameters
            or CookedKistuRisingSlashCapabilityParameters
            or CookedKistuBladeFlurryCapabilityParameters
            or CookedBonkTargetedJumpSlamCapabilityParameters
            or CookedMankiRoundBombCapabilityParameters
            or CookedMankiJetpackBoostCapabilityParameters
            or CookedMankiBazookaCapabilityParameters;

    private static void ApplyCapability(CookedCapabilityParameters parameters,
        ref float hitReach, ref float movementReach, ref float damage,
        ref bool hasDamage, ref bool hasMovement)
    {
        switch (parameters)
        {
            case CookedKiShotCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.ProjectileSpeed * x.MaxFlightTicks / 60f + x.HitboxRadius);
                damage += x.Damage; hasDamage |= x.Damage > 0f; break;
            case CookedRisingDragonCapabilityParameters x:
                hasMovement = true; break;
            case CookedCycloneKickCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.ForwardSpeed * x.DurationTicks / 60f
                    + MathF.Max(x.BodyRadius, x.SideOffset + x.SideRadius));
                damage += x.Damage; hasDamage |= x.Damage > 0f; hasMovement = true; break;
            case CookedDragonBeamCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.BeamRange + x.BeamRadius);
                damage += x.Damage; hasDamage |= x.Damage > 0f; break;
            case CookedKistuDashSlashCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.DashDistance); hasMovement = true; hasDamage = true; break;
            case CookedKistuRisingSlashCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.HomingRange); hasMovement = true; hasDamage = true; break;
            case CookedKistuBladeFlurryCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.ForwardSpeed * x.MoveTicks / 60f);
                hasMovement = true; hasDamage = true; break;
            case CookedBonkTargetedJumpSlamCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.MaxRange + x.SlamRadius);
                damage += x.SlamDamage; hasDamage |= x.SlamDamage > 0f; hasMovement = true; break;
            case CookedMankiRoundBombCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.MaxRange + x.ExplosionRadius);
                damage += x.Damage + x.ExplosionDamage; hasDamage |= x.Damage > 0f || x.ExplosionDamage > 0f; break;
            case CookedMankiJetpackBoostCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.HorizontalSpeed + x.ExplosionRadius);
                damage += x.ExplosionDamage; hasDamage |= x.ExplosionDamage > 0f; hasMovement = true; break;
            case CookedMankiBazookaCapabilityParameters x:
                hitReach = MathF.Max(hitReach, x.ProjectileSpeed * x.MaxFlightTicks / 60f + x.HitboxRadius);
                damage += x.Damage; hasDamage |= x.Damage > 0f; break;
        }
    }

    private static ushort CapabilityFirstActiveTicks(CookedCapabilityParameters parameters)
        => parameters switch
        {
            CookedKiShotCapabilityParameters x => x.StartupTicks,
            CookedRisingDragonCapabilityParameters x => x.RiseDelay,
            CookedCycloneKickCapabilityParameters x => x.WindupTicks,
            CookedDragonBeamCapabilityParameters x => x.FireTick,
            CookedKistuDashSlashCapabilityParameters x => x.MaxAimTicks,
            CookedKistuRisingSlashCapabilityParameters _ => 0,
            CookedBonkTargetedJumpSlamCapabilityParameters x => x.MaxAimTicks,
            CookedMankiRoundBombCapabilityParameters x => x.ThrowTriggerTick,
            CookedMankiJetpackBoostCapabilityParameters x => x.StartupTicks,
            CookedMankiBazookaCapabilityParameters x => x.FireTriggerTick,
            _ => 0,
        };

    private static void CapabilityTravelParameters(CookedCapabilityParameters parameters,
        ref int travelTicks, ref float travelSpeed, ref float travelOffset)
    {
        switch (parameters)
        {
            case CookedKiShotCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.MaxFlightTicks);
                travelSpeed = MathF.Max(travelSpeed, x.ProjectileSpeed);
                travelOffset = MathF.Max(travelOffset, x.HitboxRadius);
                break;
            case CookedMankiRoundBombCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.MaxFlightTicks);
                if (x.MaxFlightTicks > 0)
                    travelSpeed = MathF.Max(travelSpeed,
                        x.MaxRange * (1f / Simulation.TickDt) / x.MaxFlightTicks);
                travelOffset = MathF.Max(travelOffset, x.HitboxRadius);
                break;
            case CookedMankiBazookaCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.MaxFlightTicks);
                travelSpeed = MathF.Max(travelSpeed, x.ProjectileSpeed);
                travelOffset = MathF.Max(travelOffset, x.HitboxRadius);
                break;
            case CookedBonkTargetedJumpSlamCapabilityParameters x:
                travelTicks = Math.Max(travelTicks, x.MaxFlightTicks);
                break;
        }
    }

    public static float ForwardReach(CharacterDefinition def, byte activeSlot, bool airborne)
        => ForwardReach(def, activeSlot, airborne, baked: null);

    /// <summary>
    /// Resolved horizontal envelope for a slot. Cooked timeline hitboxes and typed capabilities
    /// are preferred; legacy specs are only a compatibility fallback.
    /// </summary>
    public static float ForwardReach(CharacterDefinition def, byte activeSlot, bool airborne,
        BakedAnimationData? baked)
        => EvaluateMove(def, activeSlot, airborne, baked, out var candidate)
            ? candidate.Reach : 0f;

    private static short AimYaw(float dx, float dz)
        => (short)Math.Clamp((int)(MathF.Atan2(dx, dz) * (180f / MathF.PI) * 100f),
            -18000, 18000);

    private static short AimPitch(float dy, float dist)
        => (short)Math.Clamp((int)(MathF.Atan2(dy, MathF.Max(dist, 0.001f))
            * (180f / MathF.PI) * 100f), -9000, 9000);

    private static ushort AimDistance(float dist)
        => (ushort)Math.Clamp((int)(dist * 100f), 1, 6500);
}
