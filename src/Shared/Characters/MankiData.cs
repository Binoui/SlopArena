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
                JumpForce = 10f,
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
                IconName = "lmb",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40, ChainWindowTicks = 12, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 8, Radius = 1f, OffX = 0f, OffY = 0.4f, OffZ = 1f, Damage = 4f, BaseKnockback = 15f, KnockbackGrowth = 2f, KnockbackUpward = 3f, StunTicks = 16, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 35, ChainWindowTicks = 14, LungeForce = 2f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 14, DurationTicks = 8, Radius = 1f, OffX = 0f, OffY = 0.4f, OffZ = 1f, Damage = 5f, BaseKnockback = 15f, KnockbackGrowth = 3f, KnockbackUpward = 5f, StunTicks = 20, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 5f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 45, ChainWindowTicks = 0, LungeForce = 10f,
                            HitboxEvents = new[] {
                                new HitboxEvent { TriggerTick = 16, DurationTicks = 12, Radius = 1.5f, OffX = 0f, OffY = 0.4f, OffZ = 1f, Damage = 8f, BaseKnockback = 21f, KnockbackGrowth = 5f, KnockbackUpward = 10f, StunTicks = 22, Interruptible = true }
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
                Name = "Air Kick",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 16, ChainWindowTicks = 10,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 6, Radius = 0.7f, OffX = 0, OffY = 0.4f, OffZ = 1.0f, Damage = 4f, BaseKnockback = 8f, KnockbackGrowth = 2f, KnockbackUpward = 5f, StunTicks = 12, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 5f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f, LungeForce = 3f },
                    new() { DurationTicks = 18, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 8, Radius = 0.8f, OffX = 0, OffY = 0.4f, OffZ = 1.2f, Damage = 6f, BaseKnockback = 12f, KnockbackGrowth = 3f, KnockbackUpward = 8f, StunTicks = 16, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_air", "spell_lmb_air" },
            },

            RMB = new AbilitySpec
            {
                Name = "Aerosol + Lighter",
                IconName = "rmb",
                Behavior = AbilityBehavior.ChargeAttack,
                CooldownTicks = 30,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 128, ChainWindowTicks = 0,     // Stage 0: charge phase
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 6f, WarpRange = 0f, UseTargetLock = false,
                            RotateTowardTarget = false, TrackingStrength = 0f },
                    new() { DurationTicks = 58, ChainWindowTicks = 0,      // Stage 1: normal attack
                        HitboxEvents = new[]
                        {
                            new HitboxEvent
                            {
                                TriggerTick = 8, DurationTicks = 38,
                                Shape = HitboxShape.Capsule, Radius = 0.8f,
                                OffX = 0, OffY = 1.0f, OffZ = 2.0f,
                                EndOffX = 0, EndOffY = 0, EndOffZ = 1.0f,
                                Damage = 8f,
                                BaseKnockback = 5.6f, KnockbackGrowth = 8.4f,
                                KnockbackUpward = 8f,
                                StunTicks = 20, Interruptible = true,
                            },
                        },
                            AttackRange = 6f, WarpRange = 0f, UseTargetLock = false,
                            RotateTowardTarget = false, TrackingStrength = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    new() { DurationTicks = 50, ChainWindowTicks = 0,
                            HitboxEvents = new[]
                            {
                                new HitboxEvent
                                {
                                    TriggerTick = 10, DurationTicks = 30,
                                    Shape = HitboxShape.Capsule, Radius = 1.0f,
                                    OffX = 0, OffY = 1.0f, OffZ = 2.5f,
                                    EndOffX = 0, EndOffY = 0, EndOffZ = 1.5f,
                                    Damage = 14f,
                                    BaseKnockback = 9.6f, KnockbackGrowth = 14.4f,
                                    KnockbackUpward = 8f,
                                    StunTicks = 20, Interruptible = true,
                                },
                            },
                            AttackRange = 8f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                ChargeHoldTicks = 45,
                AnimationNames = new[] { "spell_rmb_charged", "spell_rmb_attack" },
            },

            AirRMB = new AbilitySpec
            {
                Name = "Knuckle Spike",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 30, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 16, DurationTicks = 8, Radius = 0.8f, Shape = HitboxShape.Capsule, OffX = 0, OffY = -0.5f, OffZ = 0, EndOffX = 0, EndOffY = -1.5f, EndOffZ = 0, Damage = 10f, BaseKnockback = 20f, KnockbackGrowth = 5f, KnockbackUpward = -12f, StunTicks = 16, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.3f },
                },
                AnimationNames = new[] { "spell_rmb_air" },
            },

            Q = new AbilitySpec
            {
                Name = "Round Bomb",
                IconName = "q",
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

            E = new AbilitySpec
            {
                Name = "Grapple Gun",
                IconName = "e",
                Behavior = AbilityBehavior.Projectile,
                CooldownTicks = 210,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 30, ChainWindowTicks = 0,
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 15f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_e" },
                Params = new()
                {
                    ["fire_trigger_tick"] = 8f,
                    ["tether_speed"] = 40f,
                    ["hitbox_radius"] = 0.3f,
                    ["max_flight_ticks"] = 30f,
                    ["max_range"] = 15f,
                    ["reel_speed"] = 25f,
                    ["arrival_threshold"] = 0.5f,
                    ["damage"] = 3f,
                    ["stun_ticks"] = 0f,
                    ["knockback_base"] = 0f,
                    ["knockback_growth"] = 0f,
                    ["knockback_upward"] = 0f,
                    ["cast_duration"] = 30f,
                },
            },

            R = new AbilitySpec
            {
                Name = "Bazooka",
                IconName = "r",
                Behavior = AbilityBehavior.Projectile,
                CooldownTicks = 240,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 35, ChainWindowTicks = 0,
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 40f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_r" },
                Params = new()
                {
                    ["fire_trigger_tick"] = 6f,
                    ["projectile_speed"] = 40f,
                    ["hitbox_radius"] = 0.6f,
                    ["damage"] = 15f,
                    ["gravity"] = 15f,
                    ["max_flight_ticks"] = 45f,
                    ["knockback_base"] = 6f,
                    ["knockback_growth"] = 9f,
                    ["knockback_upward"] = 12f,
                    ["stun_ticks"] = 25f,
                    ["explosion_radius"] = 3f,
                    ["explosion_damage"] = 10f,
                    ["explosion_kb_base"] = 6f,
                    ["explosion_kb_growth"] = 9f,
                    ["explosion_knockback_upward"] = 14f,
                    ["explosion_stun_ticks"] = 20f,
                    ["explosion_duration_ticks"] = 6f,
                    ["self_damage"] = 4f,
                    ["cast_duration"] = 20f,
                    ["recovery_duration"] = 15f,
                },
            },

            F = new AbilitySpec
            {
                Name = "Overclock",
                IconName = "f",
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
