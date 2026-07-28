namespace SlopArena.Shared;

/// <summary>
/// ═══════════════════════════════════════
/// NILUS — The Void Stalker (close/mid-range void controller)
/// ═══════════════════════════════════════
/// In-your-face controller: shortest reach on the roster, blinks in, denies the
/// retreat with a placed rift, kills with ordinary knockback (charged RMB / F).
/// Placeholder art: reuses the FightGuy prefab + empty baked data (capsule
/// hurtboxes) so the kit is fully playable in sim before its own assets exist.
/// Numbers are first-pass — see docs/characters/nilus.md.
/// </summary>
public static partial class CharacterRegistry
{
    private static CharacterDefinition BuildNilus()
    {
        // ── F: Event Horizon lifecycle, declared ONCE ──
        // NilusEventHorizon derives its lifecycle from these two Params (windup + drag) and
        // never reads Stages[0].DurationTicks — but the stage value is NOT dead: SpawnHitbox
        // and ServerSimulation.ResolveBoneAnimFrame use it to map AttackElapsedTicks onto
        // baked animation frames, so a stage duration that disagrees with the ability's real
        // length silently poses the caster's hurtboxes for the wrong frame. Deriving the
        // stage from the Params is what keeps the two from drifting apart under a retune.
        const float fWindupTicks = 72f;         // 1.2s telegraph
        const float fDragDurationTicks = 60f;

        return new CharacterDefinition
        {
            Class = CharacterClass.Nilus,
            DisplayName = "Nilus",
            CapsuleRadius = 0.33f,
            CapsuleHeight = 1.65f,
            HipHeight = 0.8f,
            HurtboxRadius = 1f,
            Movement = new MovementStats
            {
                WalkSpeed = 10f,
                SprintSpeed = 13f,
                DashSpeed = 32f,
                AirAcceleration = 17f,
                JumpForce = 12f,
                Gravity = 34f,
                AirFloatGravity = 0f,
                DashDurationTicks = 15,
                DashCooldownTicks = 48,
                GroundFriction = 15f,
                AirFriction = 0.45f,
                MaxFallSpeed = 46f,
                MaxJumps = 2,
                JumpSquatTicks = 5,
                FloatWindowTicks = 40,
                FallRampDuration = 12,
            },

            // No baked skeleton yet → capsule hurtbox fallback (placeholder).
            HurtboxBoneDefs = null,
            HurtboxCapsules = new HurtboxCapsule[]
            {
                new(0f, 0.2f, 0f, 0f, 0.9f, 0f, 0.3f),
                new(0f, 1.2f, 0f, 0f, 1.2f, 0f, 0.22f),
                new(0.3f, 0.8f, 0f, 0.6f, 0.6f, 0.2f, 0.12f),
                new(-0.3f, 0.8f, 0f, -0.6f, 0.6f, 0.2f, 0.12f),
                new(0.15f, 0f, 0f, 0.15f, -0.8f, 0f, 0.16f),
                new(-0.15f, 0f, 0f, -0.15f, -0.8f, 0f, 0.16f),
            },
            VisualScale = 1f,
            HurtboxBoneScale = 1.0f,
            ModelSoleOffset = 0f,
            AutoModelYOffset = true,
            ModelYOffset = 0f,
            ModelResourcePath = "Characters/FightGuy", // placeholder stand-in prefab
            BakedDataPath = "",                        // empty → capsule hurtboxes

            // ═══ ABILITIES ═══

            // LMB — Rift Claws (3 hits; 1-2 deliberately low base KB = "sticky", 3rd launches)
            LMB = new AbilitySpec
            {
                Name = "Rift Claws",
                Description = "Three-hit claw chain. The first two barely move the target; the third launches.",
                IconName = "lmb",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 28, ChainWindowTicks = 10, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.42f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.4f,
                                    Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 12, BaseKnockback = 1.5f, KnockbackGrowth = 1f },
                                    StunTicks = 16, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                    new() { DurationTicks = 28, ChainWindowTicks = 10, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.42f,
                                    OffX = 0, OffY = 0.7f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.4f,
                                    Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 12, BaseKnockback = 1.5f, KnockbackGrowth = 1f },
                                    StunTicks = 18, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                    new() { DurationTicks = 38, ChainWindowTicks = 0, LungeForce = 7f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 9, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.5f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.6f,
                                    Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Launcher },
                                    StunTicks = 28, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" },
                Params = new() { ["lunge_duration"] = 6f },
            },

            // AirLMB — Void Rake (2-hit juggle glue)
            AirLMB = new AbilitySpec
            {
                Name = "Void Rake",
                Description = "Two-hit aerial claw rake. Keeps enemies airborne for juggles.",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 24, ChainWindowTicks = 9, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.42f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.4f,
                                    Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light },
                                    StunTicks = 16, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                    new() { DurationTicks = 30, ChainWindowTicks = 0, LungeForce = 4f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.48f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.5f,
                                    Damage = 5f, Knockback = new() { Profile = KnockbackProfile.Launcher },
                                    StunTicks = 26, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_air_1", "spell_lmb_air_2" },
            },

            // RMB — Entropy Lance: tap = poke, charged = the kill move
            RMB = new AbilitySpec
            {
                Name = "Entropy Lance",
                Description = "Hold to charge a void spear. Tap = quick poke; charged = blast-zone kill.",
                IconName = "rmb",
                CooldownTicks = 60,
                Behavior = AbilityBehavior.ChargeAttack,
                ChargeHoldTicks = 50,
                Stages = new AttackStage[]
                {
                    // Stage 0: hold/charge phase (safety net)
                    new() { DurationTicks = 300, ChainWindowTicks = 0, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                    // Stage 1: tap poke
                    new() { DurationTicks = 30, ChainWindowTicks = 0, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.45f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 2.2f,
                                    Damage = 9f, Knockback = new() { Profile = KnockbackProfile.Medium },
                                    StunTicks = 22, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    // Charged: long thin void spear (2.2 m capsule), kill-tier knockback.
                    // SINGLE-target: RehitIntervalTicks is unset, so SpellResolver deactivates
                    // the hitbox after its first victim (SpellResolver.cs:250-251). Give it
                    // RehitIntervalTicks = 1 if piercing is ever actually wanted.
                    new() { DurationTicks = 44, ChainWindowTicks = 0, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 7, Shape = HitboxShape.Capsule, Radius = 0.6f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 2.2f,
                                    Damage = 15f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 15, BaseKnockback = 18, KnockbackGrowth = 10 },
                                    StunTicks = 40, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_rmb_loop", "spell_rmb_attack" },
            },

            // AirRMB — Collapse (downward spike)
            AirRMB = new AbilitySpec
            {
                Name = "Collapse",
                Description = "Committed downward void slam. Spikes the target toward the floor.",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 36, ChainWindowTicks = 0, MoveY = -14f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 8, Shape = HitboxShape.Sphere, Radius = 0.8f,
                                    OffX = 0, OffY = 0.1f, OffZ = 0.4f,
                                    Damage = 10f, Knockback = new() { Profile = KnockbackProfile.Spike },
                                    StunTicks = 30, Interruptible = true } },
                            AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_rmb_air" },
            },

            // Q — Void Rift (signature; lobbed seed → grounded lingering rift). Class: NilusVoidRift (Task 3)
            Q = new AbilitySpec
            {
                Name = "Void Rift",
                Description = "Lob a void seed. Where it lands, a rift lingers for 4s, damaging anything inside.",
                IconName = "q",
                CooldownTicks = 600,
                Behavior = AbilityBehavior.AimedProjectile,
                AimMode = AimMode.GroundCursor,
                // 3s max aim, same as Manki's Q. SINGLE source of truth: Simulation.cs:307-314
                // clamps ChargeTicks with this field (which is what makes Q's auto-release
                // reachable) and NilusVoidRift reads the same field, so there is no
                // charge_hold_ticks Param to drift out of step with it.
                ChargeHoldTicks = 180,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40, ChainWindowTicks = 0, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_q", "spell_q", "spell_q" },
                Params = new()
                {
                    ["throw_trigger_tick"] = 10f,
                    ["throw_duration"] = 40f,
                    ["max_range"] = 12f,
                    ["launch_angle"] = 30f,
                    ["gravity"] = 30f,
                    ["launch_offset_y"] = 1.2f,
                    ["hitbox_radius"] = 0.5f,
                    ["seed_damage"] = 0f,          // the seed itself is inert; the rift does the work
                    ["max_flight_ticks"] = 90f,
                    ["rift_radius"] = 3f,
                    ["rift_damage"] = 3f,
                    ["rift_duration_ticks"] = 240f,
                    ["rift_rehit_ticks"] = 30f,
                    ["rift_stun_ticks"] = 6f,
                    ["rift_kb_angle"] = 15f,
                    ["rift_kb_base"] = 2f,
                    ["rift_kb_growth"] = 1f,
                },
            },

            // E — Riftwalk (2-charge blink; primary recovery). Class: NilusRiftwalk (Task 4)
            E = new AbilitySpec
            {
                Name = "Riftwalk",
                Description = "Blink 6m through the void, bursting on arrival. Two charges — also your only recovery.",
                IconName = "e",
                CooldownTicks = 0, // limited by the charge pool, not a flat cooldown
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 8, ChainWindowTicks = 0, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_e" },
                Params = new()
                {
                    ["max_charges"] = 2f,
                    ["charge_regen_ticks"] = 300f,
                    ["blink_distance"] = 6f,
                    ["burst_tick"] = 4f,
                    ["burst_radius"] = 1.6f,
                    ["burst_damage"] = 4f,
                    ["burst_stun_ticks"] = 12f,
                },
            },

            // R — Nether Grasp (aimed claw, yanks target inward). Class: NilusNetherGrasp (Task 5)
            R = new AbilitySpec
            {
                Name = "Nether Grasp",
                Description = "Void claw that seizes a target and drags them to you.",
                IconName = "r",
                CooldownTicks = 480,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 34, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 10, Shape = HitboxShape.Capsule, Radius = 0.6f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 8f,
                                    Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 0f, KnockbackGrowth = 0f },
                                    // INERT as shipped: this HitboxEvent's knockback is all
                                    // zeroes, so ResolveHits' ApplyKnockback produces magnitude 0
                                    // and the `stunTicks > 0 && kbMagnitude > 0.5f` gate
                                    // (Simulation.cs:928) never fires. NilusNetherGrasp.OnHitEntity
                                    // runs afterwards and its own inward knockback decides the
                                    // real hitstun (pull_stun_ticks, itself capped to 12). Changing
                                    // this 20 does nothing until the knockback above is non-zero.
                                    StunTicks = 20, Interruptible = true } },
                            AttackRange = 9f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                },
                AnimationNames = new[] { "spell_r" },
                Params = new()
                {
                    // 9.5 lands the yank at ~4.1 m, which is the spec's "~4 m" contract.
                    // The force→distance curve is nonlinear and steep (measured, 6 m grab on
                    // flat ground: 8 → 3.00 m, 10 → 4.58 m, 12 → 6.28 m, 14 → 8.00 m) because
                    // pull_angle lifts the target off the ground, so ground friction never
                    // brakes the tail — only air drag does. Re-measure after touching either.
                    ["pull_force"] = 9.5f,
                    ["pull_angle"] = 8f,
                    // ApplyKnockback caps hitstun at min(8 + kbMagnitude*0.5, stunTicks)
                    // (Simulation.cs:930-931) = 12 at pull_force 9.5, so any value >= 12 here
                    // is a no-op — INERT as shipped. Lower it below 12 to actually shorten the grab.
                    ["pull_stun_ticks"] = 20f,
                },
            },

            // F — Event Horizon (ult: telegraph → drag → kill detonation). Class: NilusEventHorizon (Task 6)
            F = new AbilitySpec
            {
                Name = "Event Horizon",
                Description = "Tear open a rift that drags everything inward, then detonates.",
                IconName = "f",
                CooldownTicks = 540,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    // Derived, never typed twice — see the constants at the top of BuildNilus.
                    new() { DurationTicks = (ushort)(fWindupTicks + fDragDurationTicks), ChainWindowTicks = 0, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_f", "spell_f" },
                Params = new()
                {
                    ["windup_ticks"] = fWindupTicks,
                    ["drag_duration_ticks"] = fDragDurationTicks,
                    ["drag_radius"] = 6f,
                    ["drag_force"] = 3f,
                    ["drag_interval_ticks"] = 10f,
                    ["drag_damage"] = 3f,
                    ["detonation_damage"] = 18f,
                    ["detonation_kb_angle"] = 40f,
                    ["detonation_kb_base"] = 16f,
                    ["detonation_kb_growth"] = 9f,
                    ["detonation_stun_ticks"] = 40f,
                },
            },
        };
    }
}
