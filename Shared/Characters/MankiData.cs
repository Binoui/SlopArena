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
                DashDurationTicks = 8,
                DashCooldownTicks = 56,
                GroundFriction = 14f,
                AirFriction = 0.4f,
                MaxFallSpeed = 45f,
                MaxJumps = 2,
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
            AutoModelYOffset = true,

            // ═══ ABILITIES ═══

            LMB = new AbilityData
            {
                Name = "Monkey Combo",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 52, ChainWindowTicks = 10,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 36, Radius = 0.5f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 2f, StunTicks = 10, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 38, ChainWindowTicks = 8,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 22, Radius = 0.6f, OffX = 0, OffY = 1.0f, OffZ = 1.8f, Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 66, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 10, DurationTicks = 43, Radius = 0.7f, OffX = 0, OffY = 1.0f, OffZ = 2.0f, Damage = 10f, KnockbackForce = 16f, KnockbackUpward = 4f, StunTicks = 18, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                },
                AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" },
            },

            AirLMB = new AbilityData
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

            // RMB — charged cone flamethrower
            RMB = new AbilityData
            {
                Name = "Aerosol + Lighter",
                CooldownTicks = 30,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 58, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 38, Shape = HitboxShape.Capsule, Radius = 0.8f, OffX = 0, OffY = 1.0f, OffZ = 2.0f, EndOffZ = 3.0f, Damage = 8f, KnockbackForce = 14f, KnockbackUpward = 4f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 6f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    new() { DurationTicks = 50, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 10, DurationTicks = 30, Shape = HitboxShape.Capsule, Radius = 1.0f, OffX = 0, OffY = 1.0f, OffZ = 2.5f, EndOffZ = 4.0f, Damage = 14f, KnockbackForce = 24f, KnockbackUpward = 8f, StunTicks = 20, Interruptible = true } },
                            AttackRange = 8f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                ChargeHoldTicks = 45,
                AnimationNames = new[] { "spell_rmb_loop" },
                SpecialEffectKeys = new[] { "MankiAerosolFlame" },
                AimedCharge = new AimedChargeData
                {
                    ChargeAnimName = "spell_rmb_loop",
                    AttackAnimName = "spell_rmb_attack",
                    ConeAngle = 60f,
                    ConeRange = 15f,
                    MaxChargeTicks = 45,
                },
            },

            AirRMB = new AbilityData
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

            Q = new AbilityData
            {
                Name = "Round Bomb",
                CooldownTicks = 90,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 30, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 10, DurationTicks = 14, Radius = 0.6f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 8f, KnockbackForce = 10f, KnockbackUpward = 6f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
                },
                AnimationNames = new[] { "spell_q" },
                SpecialEffectKeys = new[] { "MankiRoundBomb" },
            },

            E = new AbilityData
            {
                Name = "Dynamite Jump",
                CooldownTicks = 180,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 47, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 25, Radius = 0.6f, OffX = 0, OffY = 0f, OffZ = 0f, Damage = 5f, KnockbackForce = 4f, KnockbackUpward = 4f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 3f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_e" },
                SpecialEffectKeys = new[] { "MankiDynamiteJump" },
            },

            R = new AbilityData
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

            F = new AbilityData
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
