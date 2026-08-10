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
///   - HurtboxBoneScale: Scale factor applied to baked bones. Set to matching character VisualScale (1.0f for Manki, 2.0f for FightGuy).
///   - Hitbox Offsets: (OffX, OffY, OffZ) from character center, rotated by facing yaw
///     Positive OffZ = in front, OffY = up from feet
///   - Capsule shape: OffX/OffY/OffZ = start, EndOffX/Y/Z = capsule end (relative to Off)
///   - Damage: flat value, BaseKnockback: minimum horizontal push, KnockbackGrowth: %-scaling knockback component, KnockbackUpward: vertical launch
        ///   - Hitbox BoneName: if set, hitbox follows bone position instead of OffX/Y/Z. BoneOffX/Y/Z = local offset from bone.
///   - Interruptible: if true, attacker's hitbox cancels if they get hit during it
///   - No chain windows (issue #115): every slot is a single move — one stage, one press
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
            HipHeight = 0.50f,
            HurtboxRadius = 1.0f,
            Movement = new MovementStats
            {
                WalkSpeed = 9f,
                SprintSpeed = 12f,
                DashSpeed = 30f,
                AirAcceleration = 14f,
                JumpForce = 10f,
                Gravity = 35f,
                AirFloatGravity = 0f,
                DashDurationTicks = 15,
                DashCooldownTicks = 60,
                GroundFriction = 14f,
                AirFriction = 0.4f,
                MaxFallSpeed = 45f,
                MaxJumps = 2,
                JumpSquatTicks = 6,
                FloatWindowTicks = 30,
                FallRampDuration = 15,
            },

            HurtboxBoneDefs = MixamorigBoneDefs,
            BakedDataPath = "res://data/manki_skeleton.bin",
            ModelResourcePath = "Characters/Manki",
            VisualScale = 1.0f,
            HurtboxBoneScale = 0.85f,
            ModelSoleOffset = 0.0f,
            AutoModelYOffset = true,
            ModelYOffset = -0.2f,

            // Default animation names match Mixamo: idle, run, dash, jump, fall, small_hit, medium_hit, hard_hit
            // Only ClipOverrides needed for custom timelines
            ClipOverrides = new AnimationClipConfig[]
            {
                new AnimationClipConfig { Name = "spell_q_loop", Extrapolation = ExtrapolationMode.Continuous },
            },

            // ═══ ABILITIES ═══

            LMB = new AbilitySpec
            {
                Name = "Monkey Punch",
                Description = "Right punch with a target-locking lunge — a single committed move (no auto-combo)",
                IconName = "lmb",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Single move: right punch
                    new() { DurationTicks = 40, LungeForce = 8f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 8, Radius = 0.8f, OffX = 0f, OffY = 0.4f, OffZ = 1f, Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.15f, R = 1f, G = 0.6f, B = 0f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_lmb_1" },
                Params = new()
                {
                    ["lunge_duration"] = 10f,
                },
            },

            AirLMB = new AbilitySpec
            {
                Name = "Air Kick",
                Description = "Quick aerial kick that lunges toward the target",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Single move: air kick
                    new() { DurationTicks = 28, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 6, Radius = 0.55f, OffX = 0, OffY = 0.4f, OffZ = 1.0f, Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 18, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f, LungeForce = 3f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.15f, R = 1f, G = 0.6f, B = 0f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_lmb_air_1" },
            },

            RMB = new AbilitySpec
            {
                Name = "Aerosol + Lighter",
                Description = "Charge aerosol and ignite — hold to release a larger flame burst",
                IconName = "rmb",
                Behavior = AbilityBehavior.ChargeAttack,
                CooldownTicks = 30,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 128,     // Stage 0: charge phase
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 6f, WarpRange = 0f, UseTargetLock = false,
                            RotateTowardTarget = false, TrackingStrength = 0f },
                    new() { DurationTicks = 58,      // Stage 1: normal attack
                        HitboxEvents = new[]
                        {
                            new HitboxEvent
                            {
                                TriggerTick = 8, DurationTicks = 38,
                                Shape = HitboxShape.Capsule, Radius = 0.7f,
                                OffX = 0, OffY = 1.0f, OffZ = 2.0f,
                                EndOffX = 0, EndOffY = 0, EndOffZ = 1.0f,
                                Damage = 8f,
                                Knockback = new() { Profile = KnockbackProfile.Medium },
                                StunTicks = 22, Interruptible = true,
                            },
                        },
                            AttackRange = 6f, WarpRange = 0f, UseTargetLock = false,
                            RotateTowardTarget = false, TrackingStrength = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    new() { DurationTicks = 50, 
                            HitboxEvents = new[]
                            {
                                new HitboxEvent
                                {
                                    TriggerTick = 10, DurationTicks = 30,
                                    Shape = HitboxShape.Capsule, Radius = 0.8f,
                                    OffX = 0, OffY = 1.0f, OffZ = 2.5f,
                                    EndOffX = 0, EndOffY = 0, EndOffZ = 1.5f,
                                    Damage = 14f,
                                    Knockback = new() { Profile = KnockbackProfile.Medium }, StunTicks = 22, Interruptible = true,
                                },
                            },
                            AttackRange = 8f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                ChargeHoldTicks = 45,
                AnimationNames = new[] { "spell_rmb_loop", "spell_rmb_attack" },
            },

            AirRMB = new AbilitySpec
            {
                Name = "Knuckle Spike",
                Description = "Hold to charge a downward spike punch; tap = quick spike, charged = heavier spike that launches enemies straight down",
                CooldownTicks = 0,
                Behavior = AbilityBehavior.ChargeAttack,
                ChargeHoldTicks = 45,
                Stages = new AttackStage[]
                {
                    // Stage 0: hold/charge phase (no hitboxes; targeting + warp config live here)
                    new() { DurationTicks = 60, 
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.3f },
                    // Stage 1: tap spike (same numbers as the pre-charge air RMB)
                    new() { DurationTicks = 30, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 16, DurationTicks = 8, Radius = 0.7f, Shape = HitboxShape.Capsule, OffX = 0, OffY = -0.5f, OffZ = 0, EndOffX = 0, EndOffY = -1.5f, EndOffZ = 0, Damage = 10f, Knockback = new() { Profile = KnockbackProfile.Spike }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.3f },
                },
                ChargedStages = new AttackStage[]
                {
                    // Charged: bigger knuckle, longer reach, more damage
                    new() { DurationTicks = 30, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 14, DurationTicks = 10, Radius = 0.8f, Shape = HitboxShape.Capsule, OffX = 0, OffY = -0.5f, OffZ = 0, EndOffX = 0, EndOffY = -1.6f, EndOffZ = 0, Damage = 14f, Knockback = new() { Profile = KnockbackProfile.Spike }, StunTicks = 22, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.3f },
                },
                AnimationNames = new[] { "spell_rmb_air_loop", "spell_rmb_air_attack" },
            },

            Q = new AbilitySpec
            {
                Name = "Round Bomb",
                Description = "Throw a timed explosive round bomb at a ground target",
                IconName = "q",
                Behavior = AbilityBehavior.AimedProjectile,
                AimMode = AimMode.GroundCursor,
                CooldownTicks = 300,
                ChargeHoldTicks = 180,         // 3s max aim
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 60, 
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 20f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_q_loop", "spell_q_attack" },
                AnimSpeed = 1f,        // loop plays at native rate during the variable-length aim hold
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
                    ["stun_ticks"] = 22f,
                    ["max_flight_ticks"] = 90f,
                    ["kb_angle"] = 30f,
                    ["explosion_damage"] = 10f,
                    ["explosion_kb_base"] = 2.4f,
                    ["explosion_kb_growth"] = 3.6f,
                    ["explosion_stun_ticks"] = 18f,
                    ["explosion_duration_ticks"] = 8f,
                    ["explosion_kb_angle"] = 30f,
                },
            },

            E = new AbilitySpec
            {
                Name = "Grapple Gun",
                Description = "Fire a grapple hook that pulls Manki toward the hit point",
                IconName = "e",
                Behavior = AbilityBehavior.Projectile,
                AimMode = AimMode.CameraForward3D,
                CooldownTicks = 210,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 30, 
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 15f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_e_loop", "spell_e_attack" },
                AnimSpeed = 1f,        // loop plays at native rate during the variable-length aim hold
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
                    ["kb_angle"] = 0f,
                    ["cast_duration"] = 30f,
                },
            },

            R = new AbilitySpec
            {
                Name = "Bazooka",
                Description = "Launch a rocket that explodes on impact",
                IconName = "r",
                Behavior = AbilityBehavior.Projectile,
                AimMode = AimMode.CameraForward3D,
                CooldownTicks = 240,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 35, 
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 40f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_r_loop", "spell_r_attack" },
                AnimSpeed = 1f,        // loop plays at native rate during the variable-length aim hold
                Params = new()
                {
                    ["fire_trigger_tick"] = 6f,
                    ["projectile_speed"] = 40f,
                    ["hitbox_radius"] = 0.6f,
                    ["damage"] = 15f,
                    ["gravity"] = 15f,
                    ["max_flight_ticks"] = 45f,
                    ["stun_ticks"] = 24f,
                    ["explosion_radius"] = 3f,
                    ["kb_angle"] = 25f,
                    ["explosion_kb_base"] = 6f,
                    ["explosion_kb_growth"] = 9f,
                    ["explosion_stun_ticks"] = 22f,
                    ["explosion_duration_ticks"] = 6f,
                    ["explosion_kb_angle"] = 25f,
                    ["cast_duration"] = 20f,
                    ["recovery_duration"] = 15f,
                },
            },

            F = new AbilitySpec
            {
                Name = "Overclock",
                Description = "Briefly overclock speed and attack power",
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
