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

        public MoveCandidate(byte slot, float reach, float damage, bool functional,
            bool hasDamage, bool hasMovement, bool requiresAim, bool isRecovery,
            ushort startupTicks)
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

        // A selected move is an execution plan, not a fresh opponent reaction. Keep the
        // captured target until the ordinary simulation accepts the press and any aim hold.
        if (memory.PlanPhase != BotPlanPhase.None)
        {
            if (!self.IsGrounded)
                memory.PlanWasAirborne = true;
            if (PlanInvalidated(self, memory))
            {
                memory.ClearPlan();
                return default;
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

            // The press was accepted, or the action was interrupted before acceptance.
            memory.ClearPlan();
            return default;
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

        if (targetThreatening && canDash && rng.NextDouble() < profile.DodgeChance)
        {
            input.Dash = true;
            input.MoveX = -dx / dist;
            input.MoveY = -dz / dist;
            return input;
        }

        bool combo = candidate.HasValue
            && memory.LastAttackConnected
            && rng.NextDouble() < profile.ComboChance;
        bool punish = candidate.HasValue
            && targetThreatening
            && rng.NextDouble() < profile.PunishChance;
        bool attack = candidate.HasValue
            && (punish || combo || rng.NextDouble() < profile.AttackChance);
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

    private static bool PlanInvalidated(in CharacterState self, BotMemory memory)
        => self.Deaths != memory.PlanDeaths
            || self.HitstunTicks > 0
            || self.LandingLagTicks > 0
            || self.BurstRecoveryTicks > 0
            || (memory.PlanWasAirborne && self.IsGrounded)
            || ((self.State is ActionState.Attacking or ActionState.Aiming)
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
            int startup = int.MaxValue;
            int stageOffset = 0;
            for (int stageIndex = 0; stageIndex < cooked.Timeline.Stages.Count; stageIndex++)
            {
                var stage = cooked.Timeline.Stages[stageIndex];
                var animationNames = stage.AnimationIds.ToArray();
                foreach (var operation in stage.Operations)
                {
                    startup = Math.Min(startup, stageOffset + operation.Tick);
                    switch (operation)
                    {
                        case CookedSpawnHitboxOperation hitbox:
                            functional = true;
                            var evt = ToHitboxEvent(hitbox.Hitbox);
                            hitReach = MathF.Max(hitReach,
                                PoseForwardReach(def, evt, activeSlot, airborne,
                                    animationNames, (byte)stageIndex, baked));
                            hasDamage |= hitbox.Hitbox.Damage > 0f;
                            damage += MathF.Max(0f, hitbox.Hitbox.Damage);
                            break;
                        case CookedSpawnProjectileOperation projectile:
                            functional = true;
                            hasDamage |= projectile.Projectile.Damage > 0f;
                            damage += MathF.Max(0f, projectile.Projectile.Damage);
                            hitReach = MathF.Max(hitReach,
                                MathF.Max(0f, projectile.Projectile.LaunchOffsetZ)
                                + projectile.Projectile.Speed * projectile.Projectile.MaxFlightTicks / 60f
                                + projectile.Projectile.Radius);
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
                cooked.IsRecoveryMove, startup == int.MaxValue ? (ushort)0 : (ushort)Math.Min(startup, ushort.MaxValue));
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
