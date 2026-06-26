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
///   - HurtboxBoneScale: Mixamo GLB uses cm, so 0.01 = convert cm→m
///   - Hitbox Offsets: (OffX, OffY, OffZ) from character center, rotated by facing yaw
///     Positive OffZ = in front, OffY = up from feet
///   - Capsule shape: OffX/OffY/OffZ = start, EndOffX/Y/Z = capsule end (relative to Off)
///   - Damage: flat value, KnockbackForce: horizontal push, KnockbackUpward: vertical launch
///   - StunTicks: how long the victim is in hitstun (can't act)
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
            CapsuleRadius = 0.6f,
            CapsuleHeight = 1.3f,
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
                JumpDurationTicks = 60,
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
            GlbPath = "res://assets/characters/manki/manki.glb",
            BakedDataPath = "res://data/manki_skeleton.bin",
            VisualScale = 1.0f,
            HurtboxBoneScale = 0.01f,
            ModelSoleOffset = 0.0f,
            AutoModelYOffset = false,
            ModelYOffset = -0.52f,

            // Default animation names match Mixamo: idle, run, dash, jump, fall, small_hit, medium_hit, hard_hit
            // Only ClipOverrides needed for custom timelines
            ClipOverrides = new AnimationClipConfig[]
            {
                new() { Name = "spell_q_loop", LoopMode = ClipLoopMode.PingPong, TimelineLength = 3.0f },
                new() { Name = "spell_q", StartOffset = 0.5f, TimelineLength = 2.0f },
            },

            // ═══ ABILITIES ═══

            LMB = new AbilitySpec
            {
                Name = "Monkey Combo",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 52, ChainWindowTicks = 10, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 36, Radius = 1f, OffX = 0, OffY = 0, OffZ = 0.9f, Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 2f, StunTicks = 10, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 38, ChainWindowTicks = 8, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 22, Radius = 1f, OffX = 0, OffY = 0, OffZ = 0.9f, Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 66, ChainWindowTicks = 0, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 10, DurationTicks = 43, Radius = 1f, OffX = 0, OffY = 0, OffZ = 0.9f, Damage = 10f, KnockbackForce = 16f, KnockbackUpward = 4f, StunTicks = 18, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
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
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 4, DurationTicks = 12, Radius = 0.6f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 6f, KnockbackForce = 8f, KnockbackUpward = 8f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_air" },
            },

            RMB = new AbilitySpec
            {
                Name = "Aerosol + Lighter",
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
                    ["normal_knockback"] = 14f,
                    ["normal_knockback_up"] = 4f,
                    ["normal_stun"] = 24f,
                    ["charged_duration"] = 50f,
                    ["charged_trigger_tick"] = 10f,
                    ["charged_off_z"] = 2.5f,
                    ["charged_end_off_z"] = 4.0f,
                    ["charged_radius"] = 1.0f,
                    ["charged_hitbox_duration"] = 30f,
                    ["charged_damage"] = 14f,
                    ["charged_knockback"] = 24f,
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
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 16, Radius = 0.7f, OffX = 0, OffY = 1.0f, OffZ = 1.8f, Damage = 8f, KnockbackForce = 12f, KnockbackUpward = -8f, StunTicks = 16, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_rmb_air" },
            },

            Q = new AbilitySpec
            {
                Name = "Round Bomb",
                CooldownTicks = 90,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 60, ChainWindowTicks = 0,
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 20f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_q" },
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
                    ["damage"] = 8f,
                    ["knockback_force"] = 10f,
                    ["knockback_upward"] = 6f,
                    ["stun_ticks"] = 14f,
                    ["max_flight_ticks"] = 90f,
                    ["explosion_radius"] = 2.5f,
                    ["explosion_damage"] = 6f,
                    ["explosion_knockback_force"] = 6f,
                    ["explosion_knockback_upward"] = 4f,
                    ["explosion_stun_ticks"] = 10f,
                    ["explosion_duration_ticks"] = 8f,
                },
            },

            E = new ExplosiveMineSpec
            {
                Name = "Dynamite Mine",
                CooldownTicks = 30,
                MineRadius = 0.3f,
                MineDurationTicks = 180,
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
                    KnockbackForce = 5f,
                    KnockbackUpward = 20f,
                    StunTicks = 30,
                    DurationTicks = 8,
                    CanHitOwner = true,
                },
            },

            R = new AbilitySpec
            {
                Name = "Dive Bomb",
                CooldownTicks = 240,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 56, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 14, DurationTicks = 31, Radius = 0.8f, OffX = 0, OffY = 0.5f, OffZ = 1.5f, Damage = 14f, KnockbackForce = 18f, KnockbackUpward = 6f, StunTicks = 18, Interruptible = true } },
                            AttackRange = 6f, WarpRange = 15f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                },
                AnimationNames = new[] { "spell_r" },
                SpecialEffectKeys = new[] { "MankiDiveBomb" },
            },

            F = new AbilitySpec
            {
                Name = "Big Boom",
                CooldownTicks = 600,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 71, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 16, DurationTicks = 41, Radius = 1.2f, OffX = 0, OffY = 0.5f, OffZ = 1.5f, Damage = 20f, KnockbackForce = 22f, KnockbackUpward = 8f, StunTicks = 20, Interruptible = true } },
                            AttackRange = 6f, WarpRange = 15f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                },
                AnimationNames = new[] { "spell_f" },
                SpecialEffectKeys = new[] { "MankiBigBoom" },
            },
        };
    }
}
