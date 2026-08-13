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
                DashDurationTicks = 10,
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

            LMB = new AbilitySpec
            {
                Name = "Dragon Jab",
                Description = "Fast low kick jab — a single committed move (no auto-combo)",
                IconName = "lmb",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Single move: fast right foot low kick
                    new() { DurationTicks = 40, LungeForce = 10f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 6, Radius = 0.7f, OffX = 0, OffY = 0.2f, OffZ = 1.0f,
                                    Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_lmb_1" },
                Params = new() { ["lunge_duration"] = 6f, },
            },

            AirLMB = new AbilitySpec
            {
                Name = "Rising Kick",
                Description = "Rising two-hit airborne uppercut — a single committed move",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Single move: rising two-hit
                    new() { DurationTicks = 28, LungeForce = 3f,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 6, DurationTicks = 4, Radius = 0.55f, OffX = 0, OffY = 0.9f, OffZ = 1.0f, Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 16, Interruptible = true },
                                new() { TriggerTick = 14, DurationTicks = 6, Radius = 0.6f, OffX = 0, OffY = 0.9f, OffZ = 1.2f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 20, Interruptible = true },
                            },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { 
                                new BoneTrailDef { BoneName = "mixamorig:LeftHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } ,
                                new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_lmb_air_1" },
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
                    new() { DurationTicks = 300, 
                            HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 2f, AttackRange = 0f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f ,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } 
                            },
                    // Stage 1: uncharged attack (quick release, less damage/stun)
                    new() { DurationTicks = 35, 
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 5, DurationTicks = 4, Radius = 0.55f, OffX = 0, OffY = 0.2f, OffZ = 0.8f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 8, KnockbackGrowth = 5 }, StunTicks = 16, Interruptible = true },
                                new() { TriggerTick = 10, DurationTicks = 4, Radius = 0.55f, OffX = 0, OffY = 0.9f, OffZ = 1.0f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 8, KnockbackGrowth = 5 }, StunTicks = 16, Interruptible = true },
                                new() { TriggerTick = 15, DurationTicks = 4, Radius = 0.55f, OffX = 0, OffY = 1.6f, OffZ = 0.6f, Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 8, KnockbackGrowth = 5 }, StunTicks = 16, Interruptible = true },
                            },
                            LungeForce = 4f, AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } 
                            },
                },
                ChargedStages = new AttackStage[]
                {
                    // Stage 0: charged attack (full charge, bigger hitboxes, more damage/stun/launch)
                    new() { DurationTicks = 35, 
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 5, DurationTicks = 4, Radius = 0.7f, OffX = 0, OffY = 0.2f, OffZ = 0.8f, Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 14, KnockbackGrowth = 8 }, StunTicks = 24, Interruptible = true },
                                new() { TriggerTick = 10, DurationTicks = 4, Radius = 0.7f, OffX = 0, OffY = 0.9f, OffZ = 1.0f, Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 14, KnockbackGrowth = 8 }, StunTicks = 24, Interruptible = true },
                                new() { TriggerTick = 15, DurationTicks = 4, Radius = 0.7f, OffX = 0, OffY = 1.6f, OffZ = 0.6f, Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 14, KnockbackGrowth = 8 }, StunTicks = 24, Interruptible = true },
                            },
                            LungeForce = 5f, AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } 
                            },
                },
                AnimationNames = new[] { "spell_rmb_loop", "spell_rmb_attack" },
            },

            AirRMB = new AbilitySpec
            {
                Name = "Helicopter",
                Description = "Hold to charge an aerial spinning heel drop; tap = quick spike, charged = heavier heel drop that spikes enemies downward",
                CooldownTicks = 0,
                Behavior = AbilityBehavior.ChargeAttack,
                ChargeHoldTicks = 45,
                Stages = new AttackStage[]
                {
                    // Stage 0: hold/charge phase (no hitboxes; targeting + warp config live here)
                    new() { DurationTicks = 60, 
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                    // Stage 1: tap heel drop (same numbers as the pre-charge air RMB)
                    new() { DurationTicks = 25, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 16, Radius = 0.5f, OffX = 0, OffY = 0.5f, OffZ = 1.2f, Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Spike }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                ChargedStages = new AttackStage[]
                {
                    // Charged: wider heel drop, more damage
                    new() { DurationTicks = 25, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 18, Radius = 0.65f, OffX = 0, OffY = 0.5f, OffZ = 1.2f, Damage = 12f, Knockback = new() { Profile = KnockbackProfile.Spike }, StunTicks = 24, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_rmb_air_loop", "spell_rmb_air_attack" },
            },

            Slot1 = new AbilitySpec
            {
                Name = "Low Kick",
                Description = "Fast low right-foot kick — quick poke, low commitment (normal tier, key 1)",
                IconName = "1",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Melee jab profile: 2-frame startup, 3-frame active, ~17 total. No lunge —
                    // grounded activation stops run momentum; aerials carry theirs (ADR-0015 §2).
                    new() { DurationTicks = 17, IasaTicks = 13,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 2, DurationTicks = 3, Radius = 0.55f, OffX = 0, OffY = 0.3f, OffZ = 1.0f,
                                    Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 14, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_1" },
            },

            AirSlot1 = new AbilitySpec
            {
                Name = "Double Punch",
                Description = "Left then right punch — fast air poke (air variant, key 1)",
                IconName = "1",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Melee uair profile: 5-frame startup, two hitboxes (4 / 13), ~33 total
                    new() { DurationTicks = 33, IasaTicks = 29,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 4, DurationTicks = 4, Radius = 0.5f, OffX = 0, OffY = 0.9f, OffZ = 1.0f, Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = true },
                                new() { TriggerTick = 13, DurationTicks = 5, Radius = 0.55f, OffX = 0, OffY = 0.9f, OffZ = 1.1f, Damage = 5f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 16, Interruptible = true },
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
                Description = "Roundhouse left kick — mid-range spacing normal (normal tier, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Melee ftilt profile: 8-frame startup, 4-frame active, ~29 total. No lunge —
                    // grounded activation stops run momentum; aerials carry theirs (ADR-0015 §2).
                    new() { DurationTicks = 29, IasaTicks = 27,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 4, Radius = 0.65f, OffX = 0, OffY = 0.8f, OffZ = 1.15f,
                                    Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Medium }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:LeftFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_2" },
            },

            AirSlot2 = new AbilitySpec
            {
                Name = "Floating Kick",
                Description = "Long static kick — lingering air hitbox, nair-style spacing (air variant, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Melee nair profile: late startup, long active window, ~42 total; static (no lunge)
                    new() { DurationTicks = 42, IasaTicks = 36,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 12, Radius = 0.6f, OffX = 0, OffY = 0.8f, OffZ = 1.0f,
                                    Damage = 6f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 18, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_a_2" },
            },

            Slot3 = new AbilitySpec
            {
                Name = "Double Uppercut",
                Description = "Low right-hand starter then a rising right uppercut — anti-air launcher (normal tier, key 3)",
                IconName = "3",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Melee utilt profile: two hitboxes (low starter 12, uppercut 20, launch up), ~39 total.
                    // No lunge — grounded activation stops run momentum; aerials carry theirs (ADR-0015 §2).
                    new() { DurationTicks = 39, IasaTicks = 36,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 12, DurationTicks = 4, Radius = 0.55f, OffX = 0, OffY = 0.5f, OffZ = 0.8f, Damage = 5f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 16, Interruptible = true },
                                new() { TriggerTick = 20, DurationTicks = 5, Radius = 0.6f, OffX = 0, OffY = 1.6f, OffZ = 0.5f, Damage = 10f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 75, BaseKnockback = 10, KnockbackGrowth = 5 }, StunTicks = 22, Interruptible = true },
                            },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_3" },
            },

            AirSlot3 = new AbilitySpec
            {
                Name = "High Kick",
                Description = "High-reaching left-foot kick — aerial juggle launcher (air variant, key 3)",
                IconName = "3",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Melee dair-ish profile: 15-frame startup, 5-frame active, ~44 total, upward send
                    new() { DurationTicks = 44, IasaTicks = 41,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 15, DurationTicks = 5, Radius = 0.6f, OffX = 0, OffY = 1.3f, OffZ = 1.0f,
                                    Damage = 9f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 70, BaseKnockback = 8, KnockbackGrowth = 4 }, StunTicks = 20, Interruptible = true } },
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
                    // Melee dsmash profile: 18-frame startup, long 10-frame active ring, ~49 total
                    new() { DurationTicks = 49, IasaTicks = 46,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 18, DurationTicks = 10, Radius = 1.7f, OffX = 0, OffY = 0.6f, OffZ = 0f,
                                    Damage = 12f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 20, BaseKnockback = 12, KnockbackGrowth = 6 }, StunTicks = 22, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.12f, R = 0.3f, G = 0.6f, B = 1f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_g_4" },
            },

            AirSlot4 = new AbilitySpec
            {
                Name = "Air Tornado",
                Description = "Spinning tornado kick — 360° air get-off-me, same as ground key 4 (air variant, key 4)",
                IconName = "4",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Identical frame data to ground Tornado Kick (fightguy_a_4 = same kick)
                    new() { DurationTicks = 49, IasaTicks = 46,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 18, DurationTicks = 10, Radius = 1.7f, OffX = 0, OffY = 0.6f, OffZ = 0f,
                                    Damage = 12f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 20, BaseKnockback = 12, KnockbackGrowth = 6 }, StunTicks = 22, Interruptible = true } },
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
