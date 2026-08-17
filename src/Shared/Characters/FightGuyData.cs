namespace SlopArena.Shared;

/// <summary>
/// ═══════════════════════════════════════
/// FIGHTGUY — Martial Arts Brawler
/// ═══════════════════════════════════════
/// </summary>
public static partial class CharacterRegistry
{
    private static CharacterDefinition BuildFightGuy()
    {
        var def = new CharacterDefinition
        {
            Class = CharacterClass.FightGuy,
            DisplayName = "FightGuy",
            CapsuleRadius = 0.35f,
            CapsuleHeight = 1.7f,
            HipHeight = 0.82f,
            HurtboxRadius = 1f,
            Movement = new MovementStats
            {
                RunSpeed = 14f,
                RunAccelerationA = 20f,
                RunAccelerationB = 12f,
                DashSpeed = 20f,
                AirSpeedMax = 7.5f,
                AirAccelStick = 16f,
                AirAccelBase = 3.2f,
                JumpForce = 12f,
                ShortHopForce = 7.2f,
                AirJumpVMultiplier = 0.8f,
                AirJumpHMultiplier = 0.85f,
                Gravity = 36f,
                AirFloatGravity = 0f,
                DashDurationTicks = 20,
                DashCooldownTicks = 48,
                GroundFriction = 8f,
                AirFriction = 6f,
                MaxFallSpeed = 48f,
                FastFallSpeed = 58f,
                MaxJumps = 2,
                JumpSquatTicks = 4,
                FloatWindowTicks = 35,
                RushTicks = 10,
            },
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
            ModelResourcePath = "Characters/FightGuy",
            BakedDataPath = "res://data/fightguy_skeleton.bin",

            // ═══ ABILITIES ═══

            Slot1 = new AbilitySpec
            {
                Name = "Low Kick",
                Description = "Fast low right-foot kick — quick poke, not a combo starter (normal tier, key 1)",
                IconName = "1",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Poke: 4-frame startup, 5-frame active, ~17 total. Flat-ish send that
                    // resets neutral — too much KB at low % to link into a combo.
                    new() { DurationTicks = 17, IasaTicks = 13,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 4, DurationTicks = 5, Radius = 0.35f, OffX = 0f, OffY = 0f, OffZ = 0.21f, BoneName = "mixamorig:RightFoot", Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 8, BaseKnockback = 4f, KnockbackGrowth = 20f }, StunTicks = 14, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_1" },
            },

            AirSlot1 = new AbilitySpec
            {
                Name = "Double Punch",
                Description = "Left then right punch — hit 1 juggles, hit 2 sends farther (air variant, key 1)",
                IconName = "1",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Two-hit air poke: left (soft juggle pop) then right (sends out farther).
                    new() { DurationTicks = 33, IasaTicks = 29, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 23,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 6, DurationTicks = 5, Radius = 0.3f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "mixamorig:RightHand", Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 55, BaseKnockback = 5f, KnockbackGrowth = 24f }, StunTicks = 12, Interruptible = true },
                                new() { TriggerTick = 16, DurationTicks = 5, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "mixamorig:LeftHand", Damage = 5f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 45, BaseKnockback = 7f, KnockbackGrowth = 30f }, StunTicks = 16, Interruptible = true },
                            },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] {
                                new BoneTrailDef { BoneName = "mixamorig:LeftHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f },
                                new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_a_1" },
            },

            E = new AbilitySpec
            {
                Name = "Rising Dragon",
                Description = "Rising kick — anti-air launcher on the ground, recovery burst in the air (resets the float window)",
                IconName = "e",
                CooldownTicks = 240,
                IsRecoveryMove = true,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 24,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 6, Radius = 0.6f, OffX = 0, OffY = 1.2f, OffZ = 0.6f, Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 75, BaseKnockback = 10, KnockbackGrowth = 6 }, StunTicks = 22, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_r_loop" },
                Params = new() { ["burst_vy"] = 15f, },
            },

            R = new AbilitySpec
            {
                Name = "Cyclone Kick",
                Description = "Dash forward with a rapid spinning kick",
                IconName = "r",
                CooldownTicks = 120,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40 },
                },
                AnimationNames = new[] { "spell_e" },
                AnimSpeed = 2f,
                SpecialEffectKeys = new[] { "FightGuyCycloneKick" },
                Params = new()
                {
                    ["forward_speed"] = 17f,
                    ["windup_ticks"] = 6f,
                    ["hitbox_end_tick"] = 34f,
                    ["duration_ticks"] = 40f,
                    ["body_radius"] = 0.8f,
                    ["side_radius"] = 0.4f,
                    ["side_offset"] = 0.8f,
                    ["damage"] = 7f,
                    ["stun_ticks"] = 20f,
                    ["body_y"] = 0.8f,
                    ["side_y"] = 0.3f,
                },
            },

            F = new AbilitySpec
            {
                Name = "Tempest",
                Description = "Spin and pull nearby enemies inward, then launch them skyward",
                IconName = "f",
                CooldownTicks = 540,
                Stages = new AttackStage[]
                {
                    // Stage 1: brief windup (no hitbox)
                    new() { DurationTicks = 12, 
                            HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            AttackRange = 0f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                    // Stage 2: sustained spinning kick AoE
                    new() { DurationTicks = 60, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 0, DurationTicks = 60, Radius = 2.8f, OffX = 0, OffY = 0.5f, OffZ = 0f, Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 6, Interruptible = false } },
                            AttackRange = 4f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_f" },
                SpecialEffectKeys = new[] { "FightGuyTempest" },
                Params = new()
                {
                    ["pull_radius"] = 3.5f,
                    ["pull_force"] = 3f,
                    ["pull_interval_ticks"] = 10f,
                    ["launcher_damage"] = 12f,
                    ["launcher_kb_base"] = 10f,
                    ["launcher_kb_growth"] = 10f,
                    ["launcher_kb_angle"] = 25f,
                    ["launcher_stun_ticks"] = 24f,
                    ["windup_ticks"] = 12f,
                    ["spin_duration_ticks"] = 60f,
                },
            },

            // ═══ ISSUE #117 — NORMAL TIER (keys 1-4) + Q-SLOT PROJECTILE ═══

            Slot2 = new AbilitySpec
            {
                Name = "Roundhouse",
                Description = "Roundhouse kick — mid-range combo finisher, kills at high % (normal tier, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // ftilt: 7-frame startup, 6-frame active, ~29 total. High growth — a decent
                    // combo finisher that kills late; too slow to open a combo.
                    new() { DurationTicks = 29, IasaTicks = 27,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 7, DurationTicks = 6, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0.21f, BoneName = "mixamorig:RightFoot", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 22, BaseKnockback = 6f, KnockbackGrowth = 32f }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:LeftFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_2" },
            },

            AirSlot2 = new AbilitySpec
            {
                Name = "Floating Kick",
                Description = "Sex kick — strong early, weak late; one leg held out (air variant, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Sex kick (Melee nair): strong sweetspot 7-11 sends out hard; weak window
                    // 12-31 drops base KB to ~0 so it starts combos. One foot only — two
                    // overlapping hitboxes at the same offset double-hit on consecutive ticks.
                    new() { DurationTicks = 42, IasaTicks = 36, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 29,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 7, DurationTicks = 5, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0f, EndOffX = 0.58f, EndOffY = 2.06f, EndOffZ = 1.08f, BoneName = "mixamorig:LeftFoot", Damage = 9f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 15, BaseKnockback = 7f, KnockbackGrowth = 36f }, StunTicks = 18, Interruptible = true },
                                new() { TriggerTick = 12, DurationTicks = 20, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "mixamorig:LeftFoot", Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 15, BaseKnockback = 2f, KnockbackGrowth = 22f }, StunTicks = 12, Interruptible = true },
                                new() { TriggerTick = 6, DurationTicks = 35, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "mixamorig:RightFoot", Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 15, BaseKnockback = 2f, KnockbackGrowth = 22f }, StunTicks = 12, Interruptible = true },
                            },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_a_2" },
            },

            Slot3 = new AbilitySpec
            {
                Name = "Roundhouse",
                Description = "Roundhouse kick — mid-range combo finisher, kills at high % (normal tier, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // ftilt: 7-frame startup, 6-frame active, ~29 total. High growth — a decent
                    // combo finisher that kills late; too slow to open a combo.
                    new() { DurationTicks = 29, IasaTicks = 27,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 7, DurationTicks = 6, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0.21f, BoneName = "mixamorig:RightFoot", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 22, BaseKnockback = 6f, KnockbackGrowth = 32f }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:LeftFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_3" },
            },

            AirSlot3 = new AbilitySpec
            {
                Name = "High Kick",
                Description = "Football kick — slow, hard-to-land kill aerial, sends far out (air variant, key 3)",
                IconName = "3",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Ganon-fair-style kill aerial: slow startup, small precise hitbox, high
                    // damage + high base/growth — kills off the side, whiffs are punished.
                    new() { DurationTicks = 44, IasaTicks = 41, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 30,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 13, DurationTicks = 10, Radius = 0.4f, OffX = 0.43f, OffY = -0.09f, OffZ = -0.16f, BoneName = "mixamorig:RightFoot", Damage = 12f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 12, BaseKnockback = 7f, KnockbackGrowth = 36f }, StunTicks = 22, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:LeftFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_a_3" },
            },

            Slot4 = new AbilitySpec
            {
                Name = "Tornado Kick",
                Description = "Spinning right-foot tornado kick — 360° get-off-me normal (normal tier, key 4)",
                IconName = "4",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Get-off-me: real startup, then one strong 360° ring moment (14-21). High
                    // base / low growth — shoves hard even at 0%, but does not scale into a kill.
                    new() { DurationTicks = 49, IasaTicks = 46,
                               HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 8, DurationTicks = 7, Radius = 0.5f, OffX = 0.05f, OffY = 0f, OffZ = 0.19f, BoneName = "mixamorig:LeftFoot", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 40, BaseKnockback = 4f, KnockbackGrowth = 22f }, StunTicks = 16, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_4" },
            },

            AirSlot4 = new AbilitySpec
            {
                Name = "Air Tornado",
                Description = "Air tornado kick — tatsu-style spin, combo starter when falling (air variant, key 4)",
                IconName = "4",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Air tatsu: faster spin than ground, juggle send instead of shove — a combo
                    // starter off a jump + fast-fall. Hard to land (precise timing is the goal).
                    new() { DurationTicks = 49, IasaTicks = 46, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 34,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 18, DurationTicks = 7, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "mixamorig:RightHand", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 35, BaseKnockback = 4f, KnockbackGrowth = 24f }, StunTicks = 16, Interruptible = true } },

                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_a_4" },
            },

            // Q slot (slot 11) — Ki Shot, moved from key "1" (issue #117). On AZERTY this key
            // is the physical "A" key (QWERTY-Q position = the InputBindings Azerty preset).
            A = new AbilitySpec
            {
                Name = "Ki Shot",
                Description = "Fire a ki projectile that marks the target for bonus damage",
                IconName = "q",
                CooldownTicks = 120,
                Behavior = AbilityBehavior.AimedProjectile,
                AimMode = AimMode.CameraForward3D,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 60, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 16, Radius = 0.5f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 16, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
                },
                AnimationNames = new[] { "spell_q_loop", "spell_q_attack" },
                SpecialEffectKeys = new[] { "FightGuyKiShot" },
                Params = new()
                {
                    ["charge_hold_ticks"] = 180f,    // 3s max aim
                    ["throw_duration"] = 60f,
                    ["throw_trigger_tick"] = 10f,
                    ["projectile_speed"] = 25f,
                    ["gravity"] = 1f,                // ki blast — minimal float
                    ["hitbox_radius"] = 0.5f,
                    ["damage"] = 6f,
                    ["knockback_base"] = 3f,
                    ["knockback_growth"] = 4.5f,
                    ["kb_angle"] = 30f,
                    ["stun_ticks"] = 20f,
                    ["max_flight_ticks"] = 90f,
                    ["mark_duration_ticks"] = 300f,  // 5s
                },
            },

        };

        // ── Air variants (issue #117) — ability slots shared, normals have distinct air specs ──
        // Normal tier air variants (AirSlot1-4) are declared inline above (keys 1-4 air pass).
        def.AirE = def.E;   // Rising Dragon: same move in the air — recovery burst + FloatWindow reset
        def.AirR = def.R;   // Cyclone: works identically in the air
        def.AirA = def.A;   // Ki Shot: works in the air
        return def;
    }
}
// __TEST_MARKER_FIGHTGUY_DATA_INCLUDED__
