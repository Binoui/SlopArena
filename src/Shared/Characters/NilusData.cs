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

        var def = new CharacterDefinition
        {
            Class = CharacterClass.Nilus,
            DisplayName = "Nilus",
            CapsuleRadius = 0.33f,
            CapsuleHeight = 1.65f,
            HipHeight = 0.8f,
            HurtboxRadius = 1f,
            Movement = new MovementStats
            {
                RunSpeed = 13f,
                RunAccelerationA = 20f,
                RunAccelerationB = 12f,
                DashSpeed = 32f,
                AirSpeedMax = 7.0f,
                AirAccelStick = 17f,
                AirAccelBase = 3.4f,
                JumpForce = 12f,
                ShortHopForce = 7.2f,
                AirJumpVMultiplier = 0.8f,
                AirJumpHMultiplier = 0.85f,
                Gravity = 34f,
                AirFloatGravity = 0f,
                DashDurationTicks = 15,
                DashCooldownTicks = 48,
                GroundFriction = 8f,
                AirFriction = 6f,
                MaxFallSpeed = 46f,
                FastFallSpeed = 55f,
                MaxJumps = 2,
                JumpSquatTicks = 5,
                FloatWindowTicks = 40,
                RushTicks = 10,
            },

            // Baked skeleton → shared mixamorig bone hurtboxes.
            HurtboxBoneDefs = MixamorigBoneDefs,
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
            HurtboxBoneScale = 0.85f,
            ModelSoleOffset = 0f,
            AutoModelYOffset = true,
            ModelYOffset = 0f,
            ModelResourcePath = "Characters/Nilus", // Updated to actual prefab path
            BakedDataPath = "res://data/nilus_skeleton.bin",

            // ═══ ABILITIES ═══

            // LMB — Rift Claw (single sticky rake; low base KB = "sticky")
            LMB = new AbilitySpec
            {
                Name = "Rift Claw",
                Description = "Single claw rake that barely moves the target — a committed move (no auto-combo)",
                IconName = "lmb",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 28, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.4f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.4f,
                                    Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 12, BaseKnockback = 1.5f, KnockbackGrowth = 1f },
                                    StunTicks = 14, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                },
                AnimationNames = new[] { "spell_lmb_1" },
                Params = new() { ["lunge_duration"] = 6f },
            },

            // AirLMB — Void Rake (single aerial rake, juggle glue)
            AirLMB = new AbilitySpec
            {
                Name = "Void Rake",
                Description = "Aerial claw rake. Keeps enemies airborne for juggles.",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 24, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.4f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.4f,
                                    Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light },
                                    StunTicks = 14, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_air_1" },
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
                    new() { DurationTicks = 300, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                    // Stage 1: tap poke
                    new() { DurationTicks = 30, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.4f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 2.2f,
                                    Damage = 9f, Knockback = new() { Profile = KnockbackProfile.Medium },
                                    StunTicks = 18, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    // Charged: long thin void spear (2.2 m capsule), kill-tier knockback.
                    // SINGLE-target: RehitIntervalTicks is unset, so SpellResolver deactivates
                    // the hitbox after its first victim (SpellResolver.cs:250-251). Give it
                    // RehitIntervalTicks = 1 if piercing is ever actually wanted.
                    new() { DurationTicks = 44, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 7, Shape = HitboxShape.Capsule, Radius = 0.5f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 2.2f,
                                    Damage = 15f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 15, BaseKnockback = 18, KnockbackGrowth = 10 },
                                    StunTicks = 24, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_rmb_loop", "spell_rmb_attack" },
            },

            // AirRMB — Collapse (downward spike; hold to charge)
            AirRMB = new AbilitySpec
            {
                Name = "Collapse",
                Description = "Hold to charge a committed downward void slam; tap = quick slam, charged = heavier slam that spikes the target toward the floor.",
                CooldownTicks = 0,
                Behavior = AbilityBehavior.ChargeAttack,
                ChargeHoldTicks = 45,
                Stages = new AttackStage[]
                {
                    // Stage 0: hold/charge phase (no hitboxes)
                    new() { DurationTicks = 60, HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 0f, WarpRange = 0f },
                    // Stage 1: tap slam (same numbers as the pre-charge air RMB; drives Nilus down at 14 m/s)
                    new() { DurationTicks = 36, MoveY = -14f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 8, Shape = HitboxShape.Sphere, Radius = 0.7f,
                                    OffX = 0, OffY = 0.1f, OffZ = 0.4f,
                                    Damage = 10f, Knockback = new() { Profile = KnockbackProfile.Spike },
                                    StunTicks = 20, Interruptible = true } },
                            AttackRange = 0f, WarpRange = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    // Charged: bigger rift sphere, faster drop, more damage
                    new() { DurationTicks = 36, MoveY = -18f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 10, Shape = HitboxShape.Sphere, Radius = 0.8f,
                                    OffX = 0, OffY = 0.1f, OffZ = 0.4f,
                                    Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Spike },
                                    StunTicks = 24, Interruptible = true } },
                            AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_rmb_air", "spell_rmb_air" },
            },

            // Q — Void Rift (signature; lobbed seed → grounded lingering rift). Class: NilusVoidRift (Task 3)
            Slot1 = new AbilitySpec
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
                    new() { DurationTicks = 40, HitboxEvents = System.Array.Empty<HitboxEvent>(),
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
                    new() { DurationTicks = 8, HitboxEvents = System.Array.Empty<HitboxEvent>(),
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
                    new() { DurationTicks = 34, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 10, Shape = HitboxShape.Capsule, Radius = 0.55f,
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
                    ["pull_stun_ticks"] = 12f,
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
                    new() { DurationTicks = (ushort)(fWindupTicks + fDragDurationTicks), HitboxEvents = System.Array.Empty<HitboxEvent>(),
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
                    ["detonation_kb_angle"] = 25f,
                    ["detonation_kb_base"] = 16f,
                    ["detonation_kb_growth"] = 9f,
                    ["detonation_stun_ticks"] = 24f,
                },
            },

        };

        // ── Air variants (issue #117 migration — preserve air use of ability slots) ──
        def.AirSlot1 = def.Slot1;
        def.AirE = def.E;
        def.AirR = def.R;
        def.AirF = def.F;
        return def;
    }
}
