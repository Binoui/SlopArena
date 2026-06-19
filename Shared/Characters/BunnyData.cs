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
            CapsuleRadius = 0.6f,
            CapsuleHeight = 1.5f,
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
                JumpDurationTicks = 28,
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
            GlbPath = "res://assets/characters/bunny/bunny.glb",
            BakedDataPath = "res://data/bunny_skeleton.bin",
            VisualScale = 0.022f,
            HurtboxBoneScale = 0.022f,
            ModelSoleOffset = 1.1f,
            AutoModelYOffset = true,

            // Bunny GLB uses "hit_small"/"hit_medium"/"hit_hard" (not Mixamo defaults)
            HitSmallAnim = "hit_small",
            HitMediumAnim = "hit_medium",
            HitHardAnim = "hit_hard",

            // ═══ ABILITIES ═══

            LMB = new AbilitySpec
            {
                Name = "Rabbit Combo",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 50, ChainWindowTicks = 10,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 36, Radius = 0.5f, OffX = 0, OffY = 0.8f, OffZ = 1.2f, Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 1f, StunTicks = 8, Interruptible = true } },
                            AttackRange = 3.5f, WarpRange = 8f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 38, ChainWindowTicks = 8,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 22, Radius = 0.55f, OffX = 0, OffY = 0.8f, OffZ = 1.4f, Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, StunTicks = 10, Interruptible = true } },
                            AttackRange = 3.5f, WarpRange = 8f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    new() { DurationTicks = 60, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 40, Radius = 0.6f, OffX = 0, OffY = 0.9f, OffZ = 1.5f, Damage = 8f, KnockbackForce = 10f, KnockbackUpward = 10f, StunTicks = 14, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                },
                AnimationNames = new[] { "spell_lmb_1", "spell_lmb_2", "spell_lmb_3" },
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
                AnimationNames = new[] { "spell_lmb_air" },
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
                AnimationNames = new[] { "spell_lmb_1" },
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
                AnimationNames = new[] { "spell_rmb_air" },
            },

            Q = new AbilitySpec
            {
                Name = "Whirling Carrot",
                CooldownTicks = 90,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 60, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 16, Radius = 0.5f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 6f, KnockbackForce = 3f, KnockbackUpward = 2f, StunTicks = 6, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 8f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
                },
                AnimationNames = new[] { "spell_q" },
                SpecialEffectKeys = new[] { "BunnyWhirlingCarrot" },
            },

            E = new AbilitySpec
            {
                Name = "Flip Kick",
                CooldownTicks = 75,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 28, Radius = 0.6f, OffX = 0, OffY = 0.8f, OffZ = -1.0f, Damage = 4f, KnockbackForce = 8f, KnockbackUpward = 2f, StunTicks = 8, Interruptible = true } },
                            AttackRange = 4f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                },
                AnimationNames = new[] { "spell_e" },
                SpecialEffectKeys = new[] { "BunnyFlipKick" },
            },

            R = new AbilitySpec
            {
                Name = "Dragon's Kick",
                CooldownTicks = 120,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 45, ChainWindowTicks = 0,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 10, DurationTicks = 28, Radius = 0.7f, OffX = 0, OffY = 0.9f, OffZ = 2.0f, Damage = 12f, KnockbackForce = 20f, KnockbackUpward = 6f, StunTicks = 18, Interruptible = true } },
                            AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                },
                AnimationNames = new[] { "spell_r" },
                SpecialEffectKeys = new[] { "BunnyDragonKick" },
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
            },
        };
    }
}
