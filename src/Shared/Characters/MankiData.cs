using System;

namespace SlopArena.Shared;

/// <summary>
/// ═══════════════════════════════════════
/// MANKI — Pyromaniac Monkey Bomber
/// ═══════════════════════════════════════
///
/// Data format notes for agents tuning values:
///   - All durations are ushort TICKS (1 tick = 1/60s ≈ 16.6ms)
///     60 ticks = 1 second, 180 ticks = 3 seconds, 600 ticks = 10 seconds
///   - All positions/distances are METERS (Godot world units = meters)
///   - HurtboxBoneScale: Scale factor applied to baked bones. Set to matching character VisualScale (1.0f for Manki, 2.0f for Bunny).
///   - Hitbox Offsets: (OffX, OffY, OffZ) from character center, rotated by facing yaw
///     Positive OffZ = in front, OffY = up from feet
///   - Capsule shape: OffX/OffY/OffZ = start, EndOffX/Y/Z = capsule end (relative to Off)
///   - Damage: flat value, BaseKnockback: minimum horizontal push, KnockbackGrowth: %-scaling knockback component, KnockbackUpward: vertical launch
        ///   - Hitbox BoneName: if set, hitbox follows bone position instead of OffX/Y/Z. BoneOffX/Y/Z = local offset from bone.
///   - Interruptible: if true, attacker's hitbox cancels if they get hit during it
///   - ChainWindowTicks: input buffer window after stage ends (0 = final stage)
///   - TriggerTick: when during the animation the hitbox spawns (must be < DurationTicks)
/// </summary>
public static partial class CharacterRegistry
{
    private static CharacterDefinition BuildManki()
    {
        return new CharacterDefinition
        {
            Class = CharacterClass.Manki,
            DisplayName = "Manki",
            CapsuleRadius = 0.3f,
            CapsuleHeight = 1.5f,
            HurtboxRadius = 1.0f,
            Movement = new MovementStats
            {
                WalkSpeed = 9f,
                SprintSpeed = 12f,
                DashSpeed = 30f,
                AirAcceleration = 14f,
                JumpForce = 7f,
                Gravity = 35f,
                AirFloatGravity = 6f,
                DashDurationTicks = 15,
                DashCooldownTicks = 60,
                GroundFriction = 14f,
                AirFriction = 0.4f,
                MaxFallSpeed = 45f,
                MaxJumps = 2,
                JumpSquatTicks = 6,
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
            BakedDataPath = "res://data/manki_skeleton.bin",
            ModelResourcePath = "Characters/Manki",
            VisualScale = 1.0f,
            HurtboxBoneScale = 1.0f,
            ModelSoleOffset = 0.0f,
            AutoModelYOffset = false,
            ModelYOffset = -0.70f,

            // Default animation names match Mixamo: idle, run, dash, jump, fall, small_hit, medium_hit, hard_hit
            // Only ClipOverrides needed for custom timelines
            ClipOverrides = new AnimationClipConfig[]
            {
            },

            // ═══ ABILITIES ═══

            LMB = new AbilitySpec
            {
                Name = "Monkey Combo",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40, ChainWindowTicks = 12, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 4, Radius = 1f, OffX = 0f, OffY = 0.4f, OffZ = 1f, Damage = 4f, BaseKnockback = 1.5f, KnockbackGrowth = 2.5f, KnockbackUpward = 1f, StunTicks = 10, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 35, ChainWindowTicks = 14, LungeForce = 2f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 14, DurationTicks = 4, Radius = 0.7f, OffX = 0f, OffY = 0.4f, OffZ = 1f, Damage = 5f, BaseKnockback = 3f, KnockbackGrowth = 5f, KnockbackUpward = 1.5f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 5f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 45, ChainWindowTicks = 0, LungeForce = 8f,
                            HitboxEvents = new[] {
                                new HitboxEvent { TriggerTick = 16, DurationTicks = 6, Radius = 0.8f, OffX = 0f, OffY = 0.4f, OffZ = 1f, Damage = 11f, BaseKnockback = 7f, KnockbackGrowth = 18f, KnockbackUpward = 6f, StunTicks = 22, Interruptible = true }
                            },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                },
                AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" },
                Params = new()
                {
                    ["lunge_duration"] = 10f,
                },
            },

            AirLMB = new AbilitySpec
            {
                Name = "Air Punch",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 20, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 4, DurationTicks = 12, Radius = 0.6f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 6f, BaseKnockback = 3.2f, KnockbackGrowth = 4.8f, KnockbackUpward = 8f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_air" },
            },

            RMB = new AbilitySpec
            {
                Name = "Aerosol + Lighter",
                Behavior = AbilityBehavior.ChargeAttack,
                CooldownTicks = 30,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 58, ChainWindowTicks = 0,
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 6f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    new() { DurationTicks = 50, ChainWindowTicks = 0,
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 8f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                ChargeHoldTicks = 45,
                AnimationNames = new[] { "spell_rmb_attack", "spell_rmb_charged" },
                SpecialEffectKeys = new[] { "MankiAerosolFlame" },
                Params = new()
                {
                    ["charge_threshold"] = 45f,
                    ["normal_duration"] = 58f,
                    ["normal_trigger_tick"] = 8f,
                    ["normal_off_z"] = 2.0f,
                    ["normal_end_off_z"] = 3.0f,
                    ["normal_radius"] = 0.8f,
                    ["normal_hitbox_duration"] = 38f,
                    ["normal_damage"] = 8f,
                    ["normal_kb_base"] = 5.6f,
                    ["normal_kb_growth"] = 8.4f,
                    ["charged_duration"] = 50f,
                    ["charged_trigger_tick"] = 10f,
                    ["charged_off_z"] = 2.5f,
                    ["charged_end_off_z"] = 4.0f,
                    ["charged_radius"] = 1.0f,
                    ["charged_hitbox_duration"] = 30f,
                    ["charged_damage"] = 14f,
                    ["charged_kb_base"] = 9.6f,
                    ["charged_kb_growth"] = 14.4f,
                    ["charged_knockback_up"] = 8f,
                    ["charged_stun"] = 20f,
                },
            },

            AirRMB = new AbilitySpec
            {
                Name = "Drop Kick",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 28, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 16, Radius = 0.7f, OffX = 0, OffY = 1.0f, OffZ = 1.8f, Damage = 8f, BaseKnockback = 4.8f, KnockbackGrowth = 7.2f, KnockbackUpward = -8f, StunTicks = 16, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_rmb_air" },
            },

            Q = new AbilitySpec
            {
                Name = "Round Bomb",
                Behavior = AbilityBehavior.AimedProjectile,
                CooldownTicks = 300,
                ChargeHoldTicks = 180,         // 3s max aim
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 60, ChainWindowTicks = 0,
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 20f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_q_start", "spell_q_loop", "spell_q_end" },
                SpecialEffectKeys = new[] { "MankiRoundBomb" },
                Params = new()
                {
                    ["throw_duration"] = 60f,
                    ["throw_trigger_tick"] = 10f,
                    ["launch_angle"] = 30f,
                    ["gravity"] = 30f,
                    ["max_range"] = 12f,
                    ["hitbox_radius"] = 0.6f,
                    ["launch_offset_y"] = 1.2f,
                    ["damage"] = 6f,
                    ["knockback_base"] = 4f,
                    ["knockback_growth"] = 6f,
                    ["knockback_upward"] = 6f,
                    ["stun_ticks"] = 14f,
                    ["max_flight_ticks"] = 90f,
                    ["explosion_radius"] = 3.0f,
                    ["explosion_damage"] = 10f,
                    ["explosion_kb_base"] = 2.4f,
                    ["explosion_kb_growth"] = 3.6f,
                    ["explosion_knockback_upward"] = 4f,
                    ["explosion_stun_ticks"] = 10f,
                    ["explosion_duration_ticks"] = 8f,
                },
            },

            E = new ExplosiveMineSpec
            {
                Name = "Dynamite Mine",
                Behavior = AbilityBehavior.AreaDenial,
                CooldownTicks = 120,
                MineRadius = 0.3f,
                MineDurationTicks = 600,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 20, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 0, DurationTicks = 1, Radius = 0.1f } },
                            AttackRange = 0, WarpRange = 0, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_e" },
                SpecialEffectKeys = new[] { "MankiPlaceMine" },
                ExplosionConfig = new ProjectileExplosion
                {
                    Radius = 2.5f,
                    Damage = 5f,
                    BaseKnockback = 2f,
                    KnockbackGrowth = 3f,
                    KnockbackUpward = 20f,
                    StunTicks = 30,
                    DurationTicks = 8,
                    CanHitOwner = true,
                },
            },

            R = new AbilitySpec
            {
                Name = "Bazooka",
                Behavior = AbilityBehavior.AimedProjectile,
                CooldownTicks = 240,
                ChargeHoldTicks = 180,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 100, ChainWindowTicks = 0,
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 20f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_r_start", "spell_r_loop", "spell_r_end" },
                SpecialEffectKeys = new[] { "MankiBazooka" },
                Params = new()
                {
                ["rise_height"] = 5f,
                ["rise_velocity"] = 14f,
                ["min_rise_ticks"] = 8f,
                ["max_aim_range"] = 20f,
                ["charge_hold_ticks"] = 180f,
                ["projectile_speed"] = 35f,
                ["fire_trigger_tick"] = 5f,
                ["damage"] = 15f,
                ["hitbox_radius"] = 1.5f,
                ["knockback_base"] = 8f,
                ["knockback_growth"] = 12f,
                ["knockback_upward"] = 12f,
                ["stun_ticks"] = 25f,
                ["projectile_gravity"] = 10f,
                ["max_flight_ticks"] = 60f,
                ["throw_duration"] = 60f,
                ["explosion_radius"] = 3f,
                ["explosion_damage"] = 25f,
                ["explosion_kb_base"] = 7.2f,
                ["explosion_kb_growth"] = 10.8f,
                ["explosion_knockback_upward"] = 14f,
                ["explosion_stun_ticks"] = 20f,
                ["explosion_duration_ticks"] = 6f,
                },
            },

            F = new AbilitySpec
            {
                Name = "Overclock",
                Behavior = AbilityBehavior.SelfBuff,
                CooldownTicks = 600,
                Stages = System.Array.Empty<AttackStage>(),
                AnimationNames = new[] { "spell_f" },
                SpecialEffectKeys = new[] { "MankiOverclock" },
                Params = new()
                {
                    ["duration_ticks"] = 480f,   // 8s
                },
            },
        };
    }
}
