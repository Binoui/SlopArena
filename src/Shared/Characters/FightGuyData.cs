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

            Slot2 = new AbilitySpec
            {
                Name = "Straight Punch",
                Description = "Straight right punch — fast mid-range forward check (normal tier, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Forward punch: 5-tick startup, 5-tick active, 25 total. The hand reaches
                    // full extension at this window; it checks approach but does not kill.
                    new() { DurationTicks = 25, IasaTicks = 22,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 5, DurationTicks = 5, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0.21f, BoneName = "mixamorig:RightHand", Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 25, BaseKnockback = 5f, KnockbackGrowth = 26f }, StunTicks = 18, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_2" },
            },

            AirSlot2 = new AbilitySpec
            {
                Name = "Floating Kick",
                Description = "Sex kick — strong early, weak late; one body-covering leg capsule (air variant, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Sex kick: the early sweetspot and late sourspot are sequential. Each
                    // capsule spans the planted body to the extended foot, so it covers the
                    // fighter without overlapping independent events that can double-hit.
                    new() { DurationTicks = 42, IasaTicks = 36, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 29,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 7, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.35f, BoneName = "mixamorig:LeftFoot", EndBoneName = "mixamorig:Hips", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 25, BaseKnockback = 5f, KnockbackGrowth = 26f }, StunTicks = 18, Interruptible = true, HitGroup = 1 },
                                new() { TriggerTick = 12, DurationTicks = 20, Shape = HitboxShape.Capsule, Radius = 0.35f, BoneName = "mixamorig:LeftFoot", EndBoneName = "mixamorig:Hips", Damage = 5f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 20, BaseKnockback = 3f, KnockbackGrowth = 16f }, StunTicks = 12, Interruptible = true, HitGroup = 1 },
                            },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:LeftFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_a_2" },
            },

            Slot3 = new AbilitySpec
            {
                Name = "Sweeping Kick",
                Description = "Wide right-foot sweep — side-check that lifts rather than sends far (normal tier, key 3)",
                IconName = "3",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Wide lateral sweep: a higher, softer launch differentiates it from the
                    // forward punch and checks opponents moving across FightGuy's front.
                    new() { DurationTicks = 29, IasaTicks = 25,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 7, DurationTicks = 6, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0.21f, BoneName = "mixamorig:RightFoot", Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 55, BaseKnockback = 5f, KnockbackGrowth = 24f }, StunTicks = 18, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_3" },
            },

            AirSlot3 = new AbilitySpec
            {
                Name = "High Kick",
                Description = "High rising kick — deliberate anti-air aerial that sends up, not out (air variant, key 3)",
                IconName = "3",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // The foot reaches its forward/high peak at this tighter window. This is
                    // FightGuy's aerial anti-air, leaving horizontal kills to Air Smash.
                    new() { DurationTicks = 44, IasaTicks = 41, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 30,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 14, DurationTicks = 6, Radius = 0.35f, OffX = 0f, OffY = 0f, OffZ = 0.14f, BoneName = "mixamorig:RightFoot", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 65, BaseKnockback = 5f, KnockbackGrowth = 26f }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_a_3" },
            },

            Slot4 = new AbilitySpec
            {
                Name = "Double Kick",
                Description = "Two-foot forward heavy kick — slow grounded kill read (normal tier, key 4)",
                IconName = "4",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Forward heavy: slowed so the two feet reach full extension at t10. One
                    // capsule spans both feet, making the visual double kick one heavy hit.
                    new() { DurationTicks = 60, IasaTicks = 56,
                               HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 10, DurationTicks = 7, Shape = HitboxShape.Capsule, Radius = 0.42f, BoneName = "mixamorig:LeftFoot", EndBoneName = "mixamorig:RightFoot", Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 28, BaseKnockback = 9f, KnockbackGrowth = 42f }, StunTicks = 26, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] {
                                new BoneTrailDef { BoneName = "mixamorig:LeftFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f },
                                new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_4" },
            },

            AirSlot4 = new AbilitySpec
            {
                Name = "Air Smash",
                Description = "Heavy forward-air strike — slow, precise horizontal kill read (air variant, key 4)",
                IconName = "4",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Ganon-fair role: the right hand reaches full extension late in the clip.
                    // Slow startup, a tight strike, and substantial landing commitment buy its kill reward.
                    new() { DurationTicks = 54, IasaTicks = 50, LandingLagTicks = 12, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 38,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 20, DurationTicks = 7, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0.24f, BoneName = "mixamorig:RightHand", Damage = 13f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 25, BaseKnockback = 8f, KnockbackGrowth = 42f }, StunTicks = 26, Interruptible = true } },

                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_a_4" },
            },

            // Q slot (slot 11) — Ki Shot, moved from key "1" (issue #117). On AZERTY this key
            // is the physical "A" key (QWERTY-Q position = the InputBindings Azerty preset).
            A = new AbilitySpec
            {
                Name = "Ki Shot",
                Description = "Fire a camera-directed ki projectile",
                IconName = "a",
                CooldownTicks = 120,
                Behavior = AbilityBehavior.Projectile,
                AimMode = AimMode.CameraForward3D,
                Stages = new AttackStage[]
                {
                    new()
                    {
                        DurationTicks = 24,
                        HitboxEvents = System.Array.Empty<HitboxEvent>(),
                        AttackRange = 0f,
                        WarpRange = 0f,
                        UseTargetLock = false,
                        RotateTowardTarget = false,
                        TrackingStrength = 0f,
                    },
                },
                AnimationNames = new[] { "spell_q_loop", "spell_q_attack" },
                SpecialEffectKeys = new[] { "FightGuyKiShot" },
                Params = new()
                {
                    ["startup_ticks"] = 8f,
                    ["duration_ticks"] = 24f,
                    ["launch_offset_y"] = 1.2f,
                    ["projectile_speed"] = 25f,
                    ["gravity"] = 1f,
                    ["hitbox_radius"] = 0.5f,
                    ["damage"] = 6f,
                    ["knockback_base"] = 3f,
                    ["knockback_growth"] = 4.5f,
                    ["kb_angle"] = 30f,
                    ["stun_ticks"] = 12f,
                    ["max_flight_ticks"] = 90f,
                },
            },

            E = new AbilitySpec
            {
                Name = "Rising Dragon",
                Description = "Rising punch — anti-air launcher on the ground, recovery burst in the air (resets the float window)",
                IconName = "e",
                CooldownTicks = 240,
                IsRecoveryMove = true,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 34,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 6, DurationTicks = 25, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0.23f, BoneName = "mixamorig:RightHand", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 75, BaseKnockback = 30f, KnockbackGrowth = 6f }, StunTicks = 22, Interruptible = true },
                                new() { TriggerTick = 6, DurationTicks = 25, Radius = 0.3f, OffX = 0f, OffY = 0.18f, OffZ = 0f, BoneName = "mixamorig:Head", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 75, BaseKnockback = 30f, KnockbackGrowth = 6f }, StunTicks = 22, Interruptible = true },
                                new() { TriggerTick = 10, DurationTicks = 5, Radius = 0.4f, OffX = 0f, OffY = 0f, OffZ = 0.63f, BoneName = "mixamorig:Hips", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 75, BaseKnockback = 30f, KnockbackGrowth = 6f }, StunTicks = 22, Interruptible = true },
                            },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_e" },
                Params = new() { ["rise_speed"] = 11f, ["rise_ticks"] = 12f, ["rise_delay"] = 8f, },
            },

            R = new AbilitySpec
            {
                Name = "Cyclone Kick",
                Description = "Dash forward with a spinning engage kick",
                IconName = "r",
                CooldownTicks = 360,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40 },
                },
                AnimationNames = new[] { "spell_r" },
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
                    ["knockback_angle"] = 15f,
                    ["knockback_base"] = 8f,
                    ["knockback_growth"] = 5f,
                    ["stun_ticks"] = 6f,
                    ["body_y"] = 0.8f,
                    ["side_y"] = 0.3f,
                },
            },

            F = new AbilitySpec
            {
                Name = "Dragon Beam",
                Description = "Fire a camera-directed beam that launches each target once",
                IconName = "f",
                CooldownTicks = 1200,
                Behavior = AbilityBehavior.Projectile,
                AimMode = AimMode.CameraForward3D,
                Stages = new AttackStage[]
                {
                    new()
                    {
                        DurationTicks = 28,
                        HitboxEvents = System.Array.Empty<HitboxEvent>(),
                        AttackRange = 0f,
                        WarpRange = 0f,
                        UseTargetLock = false,
                        RotateTowardTarget = false,
                        TrackingStrength = 0f,
                    },
                },
                AnimationNames = new[] { "spell_f" },
                SpecialEffectKeys = new[] { "FightGuyDragonBeam" },
                Params = new()
                {
                    ["duration_ticks"] = 28f,
                    ["fire_tick"] = 24f,
                    ["launch_offset_y"] = 1.2f,
                    ["beam_range"] = 18f,
                    ["beam_radius"] = 0.45f,
                    ["damage"] = 14f,
                    ["knockback_angle"] = 20f,
                    ["knockback_base"] = 18f,
                    ["knockback_growth"] = 10f,
                    ["stun_ticks"] = 24f,
                    ["hitbox_duration_ticks"] = 2f,
                },
            },




        };

        // ── Air variants (issue #117) — ability slots shared, normals have distinct air specs ──
        // Normal tier air variants (AirSlot1-4) are declared inline above (keys 1-4 air pass).
        def.AirE = def.E;   // Rising Dragon: same move in the air — recovery burst + FloatWindow reset
        def.AirR = def.R;   // Cyclone: works identically in the air
        def.AirA = def.A;   // Ki Shot: works in the air
        def.AirF = def.F;   // Dragon Beam: works in the air
        return def;
    }
}
// __TEST_MARKER_FIGHTGUY_DATA_INCLUDED__
