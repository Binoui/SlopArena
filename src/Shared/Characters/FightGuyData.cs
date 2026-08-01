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
        return new CharacterDefinition
        {
            Class = CharacterClass.FightGuy,
            DisplayName = "FightGuy",
            CapsuleRadius = 0.35f,
            CapsuleHeight = 1.7f,
            HipHeight = 0.82f,
            HurtboxRadius = 1f,
            Movement = new MovementStats
            {
                WalkSpeed = 10f,
                SprintSpeed = 14f,
                DashSpeed = 32f,
                AirAcceleration = 16f,
                JumpForce = 12f,
                Gravity = 36f,
                AirFloatGravity = 0f,
                DashDurationTicks = 18,
                DashCooldownTicks = 48,
                GroundFriction = 16f,
                AirFriction = 0.5f,
                MaxFallSpeed = 48f,
                MaxJumps = 2,
                JumpSquatTicks = 4,
                FloatWindowTicks = 35,
                FallRampDuration = 10,
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
            HurtboxBoneScale = 1.0f,
            ModelSoleOffset = 0f,
            AutoModelYOffset = true,
            ModelYOffset = 0f,
            ModelResourcePath = "Characters/FightGuy",
            BakedDataPath = "res://data/fightguy_skeleton.bin",

            // ═══ ABILITIES ═══

            LMB = new AbilitySpec
            {
                Name = "Dragon Combo",
                Description = "Fast four-hit kick combo",
                IconName = "lmb",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Stage 1: fast right foot low kick
                    new() { DurationTicks = 40, ChainWindowTicks = 10, LungeForce = 10f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 0.2f, OffZ = 1.0f,
                                    Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 6f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                    // Stage 2: right foot high kick
                    new() { DurationTicks = 32, ChainWindowTicks = 10, LungeForce = 10f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 1.0f, OffZ = 1.2f, 
                                    Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 24, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 6f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                    // Stage 3: double middle kick — left then right
                    new() { DurationTicks = 42, ChainWindowTicks = 10, LungeForce = 14f,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 8, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 0.6f, OffZ = 1.3f, 
                                        Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 16, Interruptible = true },
                                new() { TriggerTick = 17, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 0.6f, OffZ = 1.3f, 
                                        Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 28, Interruptible = true },
                            },
                            AttackRange = 4f, WarpRange = 6f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] {
                                new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f },
                                new BoneTrailDef { BoneName = "mixamorig:LeftFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f },
                            } },
                    // Stage 4: jumping double kick finisher
                    new() { DurationTicks = 56, ChainWindowTicks = 0, LungeForce = 18f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 16, DurationTicks = 8, Radius = 1.0f, OffX = 0, OffY = 0.7f, OffZ = 1.5f,
                            Damage = 10f, Knockback = new() { Profile = KnockbackProfile.Medium }, StunTicks = 36, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 6f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { 
                                new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } ,
                                new BoneTrailDef { BoneName = "mixamorig:LeftFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3", "spell_lmb_4" },
                Params = new() { ["lunge_duration"] = 6f, },
            },

            AirLMB = new AbilitySpec
            {
                Name = "Rising Kick",
                Description = "Two-hit airborne kick combo — rising uppercut into a descending spike punch",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Stage 1: rising two-hit
                    new() { DurationTicks = 28, ChainWindowTicks = 10, LungeForce = 3f,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 6, DurationTicks = 4, Radius = 0.7f, OffX = 0, OffY = 0.9f, OffZ = 1.0f, Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 20, Interruptible = true },
                                new() { TriggerTick = 14, DurationTicks = 6, Radius = 0.8f, OffX = 0, OffY = 0.9f, OffZ = 1.2f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 28, Interruptible = true },
                            },
                            AttackRange = 4f, WarpRange = 6f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { 
                                new BoneTrailDef { BoneName = "mixamorig:LeftHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } ,
                                new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                    // Stage 2: big downward punch spike
                    new() { DurationTicks = 34, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 0.5f, OffZ = 1.3f, Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Spike }, StunTicks = 32, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_lmb_air_1", "spell_lmb_air_2" },
            },

            RMB = new AbilitySpec
            {
                Name = "Uppercut",
                Description = "Charged uppercut — hold to charge, release to strike. More charge = more damage and stun.",
                IconName = "rmb",
                CooldownTicks = 60,
                Behavior = AbilityBehavior.ChargeAttack,
                ChargeHoldTicks = 180,  // 3s at 60Hz
                Stages = new AttackStage[]
                {
                    // Stage 0: charge/hold phase (300 tick safety net = 5s)
                    new() { DurationTicks = 300, ChainWindowTicks = 0,
                            HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 2f, AttackRange = 0f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f ,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } 
                            },
                    // Stage 1: uncharged attack (quick release, less damage/stun)
                    new() { DurationTicks = 35, ChainWindowTicks = 0,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 5, DurationTicks = 4, Radius = 0.7f, OffX = 0, OffY = 0.2f, OffZ = 0.8f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 50, BaseKnockback = 8, KnockbackGrowth = 5 }, StunTicks = 20, Interruptible = true },
                                new() { TriggerTick = 10, DurationTicks = 4, Radius = 0.7f, OffX = 0, OffY = 0.9f, OffZ = 1.0f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 50, BaseKnockback = 8, KnockbackGrowth = 5 }, StunTicks = 20, Interruptible = true },
                                new() { TriggerTick = 15, DurationTicks = 4, Radius = 0.7f, OffX = 0, OffY = 1.6f, OffZ = 0.6f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 50, BaseKnockback = 8, KnockbackGrowth = 5 }, StunTicks = 20, Interruptible = true },
                            },
                            LungeForce = 4f, AttackRange = 3f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } 
                            },
                },
                ChargedStages = new AttackStage[]
                {
                    // Stage 0: charged attack (full charge, bigger hitboxes, more damage/stun/launch)
                    new() { DurationTicks = 35, ChainWindowTicks = 0,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 5, DurationTicks = 4, Radius = 0.9f, OffX = 0, OffY = 0.2f, OffZ = 0.8f, Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 50, BaseKnockback = 14, KnockbackGrowth = 8 }, StunTicks = 36, Interruptible = true },
                                new() { TriggerTick = 10, DurationTicks = 4, Radius = 0.9f, OffX = 0, OffY = 0.9f, OffZ = 1.0f, Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 50, BaseKnockback = 14, KnockbackGrowth = 8 }, StunTicks = 36, Interruptible = true },
                                new() { TriggerTick = 15, DurationTicks = 4, Radius = 0.9f, OffX = 0, OffY = 1.6f, OffZ = 0.6f, Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 50, BaseKnockback = 14, KnockbackGrowth = 8 }, StunTicks = 36, Interruptible = true },
                            },
                            LungeForce = 5f, AttackRange = 3f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } 
                            },
                },
                AnimationNames = new[] { "spell_rmb_loop", "spell_rmb_attack" },
            },

            AirRMB = new AbilitySpec
            {
                Name = "Helicopter",
                Description = "Aerial spinning heel drop that spikes enemies downward",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 25, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 16, Radius = 0.6f, OffX = 0, OffY = 0.5f, OffZ = 1.2f, Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Spike }, StunTicks = 28, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } 
                            },
                },
                AnimationNames = new[] { "spell_air_rmb" },
            },

            Q = new AbilitySpec
            {
                Name = "Ki Shot",
                Description = "Fire a ki projectile that marks the target for bonus damage",
                IconName = "q",
                CooldownTicks = 120,
                Behavior = AbilityBehavior.AimedProjectile,
                AimMode = AimMode.CameraForward3D,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 60, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 16, Radius = 0.5f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 8f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
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

            E = new AbilitySpec
            {
                Name = "Tornado Kick",
                Description = "Dash forward with a rapid spinning kick",
                IconName = "e",
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
                    ["stun_ticks"] = 96f,
                    ["body_y"] = 0.8f,
                    ["side_y"] = 0.3f,
                },
            },

            R = new AbilitySpec
            {
                Name = "Dragon's Kick",
                Description = "Dash forward with a flying kick. On hit: unleash aerial combo. Whiff: recovery.",
                IconName = "r",
                CooldownTicks = 180,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 60,
                            HitboxEvents = new HitboxEvent[]
                            {
                                // Forward-facing capsule: sweeps ahead during dash (loop hurtbox)
                                new() { TriggerTick = 6, DurationTicks = 57, Radius = 0.6f,
                                    OffX = 0, OffY = 0.5f, OffZ = 0.5f,
                                    EndOffX = 0, EndOffY = 0.5f, EndOffZ = 1.5f,
                                    Damage = 5f, Knockback = new() { Profile = KnockbackProfile.Light },
                                    StunTicks = 28, Shape = HitboxShape.Capsule },
                            },
                            AttackRange = 5f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                    new() { DurationTicks = 88 },
                    new() { DurationTicks = 15 },
                },
                AnimationNames = new[] { "spell_r_loop", "spell_r_attack", "spell_r_end" },
                SpecialEffectKeys = new[] { "FightGuyDragonKick" },
                Params = new()
                {
                    ["forward_speed"] = 20f,
                    ["max_flight_ticks"] = 60f,
                    ["min_ticks_before_cancel"] = 10f,
                    ["attack_duration"] = 88f,
                    ["end_duration"] = 15f,
                    ["hit1_tick"] = 4f, ["hit1_damage"] = 6f, ["hit1_stun"] = 32f,
                    ["hit2_tick"] = 10f, ["hit2_damage"] = 8f, ["hit2_stun"] = 40f,
                    ["hit3_tick"] = 26f, ["hit3_damage"] = 16f, ["hit3_stun"] = 48f,
                    ["hit3_base"] = 16f, ["hit3_growth"] = 8f, ["hit3_angle"] = 25f,
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
                    new() { DurationTicks = 12, ChainWindowTicks = 0,
                            HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            AttackRange = 0f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                    // Stage 2: sustained spinning kick AoE
                    new() { DurationTicks = 60, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 0, DurationTicks = 60, Radius = 3.5f, OffX = 0, OffY = 0.5f, OffZ = 0f, Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 6, Interruptible = false } },
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
                    ["launcher_kb_angle"] = 45f,
                    ["launcher_stun_ticks"] = 40f,
                    ["windup_ticks"] = 12f,
                    ["spin_duration_ticks"] = 60f,
                },
            },
        };
    }
}
// __TEST_MARKER_FIGHTGUY_DATA_INCLUDED__
