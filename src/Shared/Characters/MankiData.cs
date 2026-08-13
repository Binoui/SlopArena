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
        var def = new CharacterDefinition
        {
            Class = CharacterClass.Manki,
            DisplayName = "Manki",
            CapsuleRadius = 0.3f,
            CapsuleHeight = 1.5f,
            HipHeight = 0.50f,
            HurtboxRadius = 1.0f,
            Movement = new MovementStats
            {
                RunSpeed = 12f,
                RunAccelerationA = 20f,
                RunAccelerationB = 12f,
                DashSpeed = 30f,
                AirSpeedMax = 6.5f,
                AirAccelStick = 14f,
                AirAccelBase = 2.8f,
                JumpForce = 10f,
                ShortHopForce = 6.0f,
                AirJumpVMultiplier = 0.8f,
                AirJumpHMultiplier = 0.85f,
                Gravity = 35f,
                AirFloatGravity = 0f,
                DashDurationTicks = 15,
                DashCooldownTicks = 60,
                GroundFriction = 8f,
                AirFriction = 6f,
                MaxFallSpeed = 45f,
                FastFallSpeed = 54f,
                MaxJumps = 2,
                JumpSquatTicks = 6,
                FloatWindowTicks = 30,
                RushTicks = 10,
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
                    new() { DurationTicks = 40, IasaTicks = 36, LungeForce = 8f,
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
                    new() { DurationTicks = 28, IasaTicks = 24, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 19, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 6, Radius = 0.55f, OffX = 0, OffY = 0.4f, OffZ = 1.0f, Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 18, Interruptible = true } },
                            AttackRange = 1.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f, LungeForce = 3f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightFoot", Width = 0.15f, R = 1f, G = 0.6f, B = 0f, A = 1f } } },
                },
                AnimationNames = new[] { "spell_lmb_air_1" },
            },

            Slot1 = new AbilitySpec
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

        // ── Air variants (issue #117 migration — preserve air use of ability slots) ──
        def.AirSlot1 = def.Slot1;
        def.AirE = def.E;
        def.AirR = def.R;
        def.AirF = def.F;
        return def;
    }
}
