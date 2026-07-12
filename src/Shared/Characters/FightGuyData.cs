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
                JumpForce = 14f,
                Gravity = 34f,
                AirFloatGravity = 6f,
                DashDurationTicks = 8,
                DashCooldownTicks = 48,
                GroundFriction = 16f,
                AirFriction = 0.5f,
                MaxFallSpeed = 48f,
                MaxJumps = 2,
                JumpSquatTicks = 4,
            },
            HurtboxBoneDefs = new HurtboxBoneDef[]
            {
                new("mixamorig:Head", 0, 0, 0, 0.25f),
                new("mixamorig:Spine2", 0, 0, 0, 0.3f),
                new("mixamorig:Hips", 0, 0, 0, 0.3f),
                new("mixamorig:RightHand", 0, 0, 0, 0.14f),
                new("mixamorig:LeftHand", 0, 0, 0, 0.14f),
                new("mixamorig:RightFoot", 0, 0, 0, 0.18f),
                new("mixamorig:LeftFoot", 0, 0, 0, 0.18f),
            },

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
                    // Stage 1: fast right foot low kick (anim: 30f @30fps, sped 1.50× → 40 ticks)
                    new() { DurationTicks = 40, ChainWindowTicks = 10, LungeForce = 6f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 0.2f, OffZ = 1.0f, Damage = 4f, BaseKnockback = 6f, KnockbackGrowth = 2f, KnockbackUpward = 1f, StunTicks = 10, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    // Stage 2: left foot high kick (anim: 24f @30fps, sped 1.50× → 32 ticks)
                    new() { DurationTicks = 32, ChainWindowTicks = 10, LungeForce = 4f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 7, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 1.0f, OffZ = 1.2f, Damage = 5f, BaseKnockback = 8f, KnockbackGrowth = 2.5f, KnockbackUpward = 3f, StunTicks = 12, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    // Stage 3: double middle kick — left then right (anim: 30f @30fps, sped 1.43× → 42 ticks)
                    new() { DurationTicks = 42, ChainWindowTicks = 10, LungeForce = 3f,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 8, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 0.6f, OffZ = 1.3f, Damage = 5f, BaseKnockback = 5f, KnockbackGrowth = 2f, KnockbackUpward = 3f, StunTicks = 8, Interruptible = true },
                                new() { TriggerTick = 17, DurationTicks = 6, Radius = 0.9f, OffX = 0, OffY = 0.6f, OffZ = 1.3f, Damage = 7f, BaseKnockback = 10f, KnockbackGrowth = 4f, KnockbackUpward = 5f, StunTicks = 14, Interruptible = true },
                            },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                    // Stage 4: jumping double kick finisher (anim: 51f @30fps, sped 1.82× → 56 ticks)
                    new() { DurationTicks = 56, ChainWindowTicks = 0, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 16, DurationTicks = 8, Radius = 1.0f, OffX = 0, OffY = 0.7f, OffZ = 1.5f, Damage = 10f, BaseKnockback = 18f, KnockbackGrowth = 7f, KnockbackUpward = 14f, StunTicks = 18, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                },
                AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3", "spell_lmb_4" },
                Params = new() { ["lunge_duration"] = 6f, },
            },

            AirLMB = new AbilitySpec
            {
                Name = "Rising Kick",
                Description = "Two-hit airborne kick combo that launches enemies upward",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 18, ChainWindowTicks = 10, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 6, Radius = 0.7f, OffX = 0, OffY = 0.9f, OffZ = 1.0f, Damage = 4f, BaseKnockback = 6f, KnockbackGrowth = 2f, KnockbackUpward = 5f, StunTicks = 12, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                    new() { DurationTicks = 20, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 8, Radius = 0.8f, OffX = 0, OffY = 0.9f, OffZ = 1.2f, Damage = 6f, BaseKnockback = 10f, KnockbackGrowth = 3f, KnockbackUpward = 8f, StunTicks = 16, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_3", "spell_lmb_3" },
            },

            RMB = new AbilitySpec
            {
                Name = "Heavy Strike",
                Description = "Slam the ground with a ki-infused strike — shockwave hits nearby enemies",
                IconName = "rmb",
                CooldownTicks = 90,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 50, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 24, Radius = 2.0f, OffX = 0, OffY = 0.2f, OffZ = 0f, Damage = 10f, BaseKnockback = 10f, KnockbackGrowth = 8f, KnockbackUpward = 5f, StunTicks = 16, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_rmb" },
                SpecialEffectKeys = new[] { "FightGuyHeavySlam" },
            },

            AirRMB = new AbilitySpec
            {
                Name = "Helicopter",
                Description = "Aerial spinning heel drop that spikes enemies downward",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 25, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 16, Radius = 0.6f, OffX = 0, OffY = 0.5f, OffZ = 1.2f, Damage = 7f, BaseKnockback = 4f, KnockbackGrowth = 6f, KnockbackUpward = -8f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
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
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 16, Radius = 0.5f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 6f, BaseKnockback = 3f, KnockbackGrowth = 4.5f, KnockbackUpward = 3f, StunTicks = 10, Interruptible = true } },
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
                    ["knockback_upward"] = 3f,
                    ["stun_ticks"] = 10f,
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
                    ["stun_ticks"] = 48f,
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
                                new() { TriggerTick = 3, DurationTicks = 57, Radius = 0.6f,
                                    OffX = 0, OffY = 0.5f, OffZ = 0.5f,
                                    EndOffX = 0, EndOffY = 0.5f, EndOffZ = 1.5f,
                                    Damage = 5f, BaseKnockback = 0f, KnockbackGrowth = 0f, KnockbackUpward = 0f,
                                    StunTicks = 14, Shape = HitboxShape.Capsule },
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
                    ["hit1_tick"] = 4f, ["hit1_damage"] = 6f, ["hit1_stun"] = 16f,
                    ["hit2_tick"] = 10f, ["hit2_damage"] = 8f, ["hit2_stun"] = 20f,
                    ["hit3_tick"] = 26f, ["hit3_damage"] = 16f, ["hit3_stun"] = 24f,
                    ["hit3_base"] = 16f, ["hit3_growth"] = 18f, ["hit3_upward"] = 8f,
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
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 0, DurationTicks = 60, Radius = 3.5f, OffX = 0, OffY = 0.5f, OffZ = 0f, Damage = 3f, BaseKnockback = 3f, KnockbackGrowth = 3f, KnockbackUpward = 2f, StunTicks = 3, Interruptible = false } },
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
                    ["launcher_knockback_up"] = 20f,
                    ["launcher_stun_ticks"] = 20f,
                    ["windup_ticks"] = 12f,
                    ["spin_duration_ticks"] = 60f,
                },
            },
        };
    }
}
