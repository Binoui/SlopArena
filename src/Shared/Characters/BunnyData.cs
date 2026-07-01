namespace SlopArena.Shared;

/// <summary>
/// ═══════════════════════════════════════
/// BUNNY — Rabbit Kung-Fu Assassin
/// ═══════════════════════════════════════
/// </summary>
public static partial class CharacterRegistry
{
    private static CharacterDefinition BuildBunny()
    {
        return new CharacterDefinition
        {
            Class = CharacterClass.Bunny,
            DisplayName = "Bunny",
            CapsuleRadius = 0.35f,
            CapsuleHeight = 1.7f,
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
            VisualScale = 2f,
            HurtboxBoneScale = 0.01f,
            ModelSoleOffset = 0.0f,
            AutoModelYOffset = false,
            ModelYOffset = 0f,
            ModelResourcePath = "Characters/Bunny",

            // ═══ ABILITIES ═══

            LMB = new AbilitySpec
            {
                Name = "Rabbit Combo",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 50, ChainWindowTicks = 10, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 36, Radius = 0.5f, OffX = 0, OffY = 0.8f, OffZ = 1.2f, Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 1f, StunTicks = 8, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 38, ChainWindowTicks = 8, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 22, Radius = 0.55f, OffX = 0, OffY = 0.8f, OffZ = 1.4f, Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, StunTicks = 10, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 60, ChainWindowTicks = 0, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 40, Radius = 0.6f, OffX = 0, OffY = 0.9f, OffZ = 1.5f, Damage = 8f, KnockbackForce = 10f, KnockbackUpward = 10f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                },
                AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" },
                Params = new() { ["lunge_duration"] = 8f, },
            },

            AirLMB = new AbilitySpec
            {
                Name = "Rising Bun",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 20, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 4, DurationTicks = 12, Radius = 0.55f, OffX = 0, OffY = 0.9f, OffZ = 1.3f, Damage = 5f, KnockbackForce = 6f, KnockbackUpward = 8f, StunTicks = 12, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_3" },
            },

            RMB = new AbilitySpec
            {
                Name = "Carrot Slam",
                CooldownTicks = 45,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 24, Radius = 1.8f, OffX = 0, OffY = 0.2f, OffZ = 0f, Damage = 8f, KnockbackForce = 10f, KnockbackUpward = 4f, StunTicks = 12, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_rmb" },
                SpecialEffectKeys = new[] { "BunnyCarrotSlam" },
            },

            AirRMB = new AbilitySpec
            {
                Name = "Helicopter",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 25, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 16, Radius = 0.6f, OffX = 0, OffY = 0.5f, OffZ = 1.2f, Damage = 7f, KnockbackForce = 10f, KnockbackUpward = -8f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_air_rmb" },
            },

            Q = new AbilitySpec
            {
                Name = "Whirling Carrot",
                CooldownTicks = 90,
                Behavior = AbilityBehavior.AimedProjectile,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 60, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 16, Radius = 0.5f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 6f, KnockbackForce = 3f, KnockbackUpward = 2f, StunTicks = 6, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 8f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
                },
                AnimationNames = new[] { "spell_q" },
                SpecialEffectKeys = new[] { "BunnyWhirlingCarrot" },
                Params = new()
                {
                    ["charge_hold_ticks"] = 180f,    // 3s max aim
                    ["throw_duration"] = 60f,
                    ["throw_trigger_tick"] = 10f,
                    ["launch_angle"] = 30f,
                    ["gravity"] = 30f,
                    ["max_range"] = 15f,
                    ["hitbox_radius"] = 0.5f,
                    ["damage"] = 6f,
                    ["knockback_force"] = 3f,
                    ["knockback_upward"] = 2f,
                    ["stun_ticks"] = 6f,
                    ["max_flight_ticks"] = 90f,
                    ["mark_duration_ticks"] = 300f,  // 5s
                    ["explosion_radius"] = 2.5f,
                    ["explosion_damage"] = 4f,
                    ["explosion_knockback_force"] = 3f,
                    ["explosion_knockback_upward"] = 2f,
                    ["explosion_stun_ticks"] = 4f,
                    ["explosion_duration_ticks"] = 6f,
                },
            },

            E = new AbilitySpec
            {
                Name = "Tornado Kick",
                CooldownTicks = 90,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 35, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 10, DurationTicks = 18, Radius = 0.7f, OffX = 0, OffY = 0.8f, OffZ = 1.8f, Damage = 6f, KnockbackForce = 6f, KnockbackUpward = 2f, StunTicks = 28, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_e" },
                SpecialEffectKeys = new[] { "BunnyTornadoKick" },
                Params = new()
                {
                    ["forward_speed"] = 14f,
                    ["lunge_duration"] = 10f,
                    ["windup_ticks"] = 8f,
                    ["duration_ticks"] = 35f,
                },
            },

            R = new AbilitySpec
            {
                Name = "Dragon's Kick",
                CooldownTicks = 120,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 345, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 10, DurationTicks = 28, Radius = 0.7f, OffX = 0, OffY = 0.9f, OffZ = 2.0f, Damage = 12f, KnockbackForce = 20f, KnockbackUpward = 6f, StunTicks = 18, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                },
                AnimationNames = new[] { "spell_r_loop", "spell_r_attack", "spell_r_end" },
                SpecialEffectKeys = new[] { "BunnyDragonKick" },
                Params = new()
                {
                    ["mark_multiplier"] = 1.5f,
                    ["forward_speed"] = 20f,
                    ["homing_speed"] = 24f,
                    ["homing_accel"] = 2f,
                    ["max_flight_ticks"] = 180f,    // 3s
                    ["min_ticks_before_cancel"] = 10f,
                    ["impact_aoe_radius"] = 2f,
                    ["impact_aoe_duration"] = 8f,
                    ["impact_aoe_damage"] = 6f,
                    ["impact_aoe_knockback"] = 8f,
                    ["impact_aoe_upward"] = 6f,
                    ["impact_aoe_stun"] = 10f,
                },
            },

            F = new AbilitySpec
            {
                Name = "Jade Hare",
                CooldownTicks = 450,
                Stages = new AttackStage[]
                {
                    // Stage 1: brief windup (no hitbox)
                    new() { DurationTicks = 8, ChainWindowTicks = 0,
                            HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            AttackRange = 0f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                    // Stage 2: sustained spinning kick AoE
                    new() { DurationTicks = 60, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 0, DurationTicks = 60, Radius = 3.5f, OffX = 0, OffY = 0.5f, OffZ = 0f, Damage = 4f, KnockbackForce = 4f, KnockbackUpward = 2f, StunTicks = 4, Interruptible = false } },
                            AttackRange = 4f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_f" },
                SpecialEffectKeys = new[] { "BunnyJadeHare" },
                Params = new()
                {
                    ["pull_radius"] = 3.5f,
                    ["pull_force"] = 3f,
                    ["pull_interval_ticks"] = 10f,
                    ["launcher_damage"] = 8f,
                    ["launcher_knockback"] = 12f,
                    ["launcher_knockback_up"] = 18f,
                    ["launcher_stun_ticks"] = 18f,
                    ["windup_ticks"] = 8f,
                    ["spin_duration_ticks"] = 60f,
                },
            },
        };
    }
}
