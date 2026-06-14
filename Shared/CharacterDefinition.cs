using System;

#nullable enable

namespace SlopArena.Shared
{
    public enum CharacterClass : byte
    {
        Manki,
        Bunny
    }

    [Serializable]
    public struct MovementStats
    {
        public float WalkSpeed;
        public float SprintSpeed;
        public float DashSpeed;
        public float AirAcceleration;
        public float JumpForce;
        public float Gravity;
        public ushort DashDurationTicks;
        public ushort DashCooldownTicks;
        public float GroundFriction;
        public float AirFriction;
        public float MaxFallSpeed;
        public byte MaxJumps;
    }

    public struct CharacterDefinition
    {
        public CharacterDefinition() { HurtboxBoneScale = 1.0f; }

        public CharacterClass Class;
        public string DisplayName;
        public MovementStats Movement;

        public float CapsuleRadius;
        public float CapsuleHeight;
        public float HurtboxRadius;

        /// <summary>
        /// World-space offset from character position (legacy, used when no skeleton)
        /// </summary>
        public HurtboxCapsule[] HurtboxCapsules;

        /// <summary>
        /// Bone-attached hurtboxes (ServerSkeleton-based). Replaces HurtboxCapsules when loaded.
        /// Each entry defines a sphere at a bone position with an offset.
        /// </summary>
        public HurtboxBoneDef[] HurtboxBoneDefs;
        /// <summary>Path to the GLB file containing the skeleton model.</summary>
        public string GlbPath;
        /// <summary>Path to the baked skeleton .bin file (pre-computed bone positions per frame).</summary>
        public string BakedDataPath;
        /// <summary>
        /// Scale factor for baked bone positions. Mixamo GLBs are in cm (0.01),
        /// Blender/Maya exports with meters are 1.0. Default: 1.0
        /// </summary>
        public float HurtboxBoneScale;
        /// <summary>
        /// Y offset for the visual model relative to capsule center.
        /// Aligns the model's feet with the capsule bottom.
        /// Calculated as: -(footY * HurtboxBoneScale + CapsuleHeight * 0.5f)
        /// For Mixamo: ≈ -0.52 (Manki), adjust per character.
        /// 0 = model origin at capsule center (if model is already centered).
        /// </summary>
        public float ModelYOffset;
        /// <summary>
        /// Additional downward offset for the sole of the foot (below the lowest bone).
        /// Bones are inside the mesh; this accounts for sole thickness.
        /// Typical: 0.04-0.06m for humanoids, 0 for robots/mechs.
        /// </summary>
        public float ModelSoleOffset;
        /// <summary>
        /// If true, ModelYOffset is computed from the baked skeleton data
        /// (lowest bone position at idle frame 0) instead of using the manual value.
        /// </summary>
        public bool AutoModelYOffset;

        public AbilityData LMB;
        public AbilityData RMB;
        public AbilityData AirLMB;
        public AbilityData AirRMB;
        public AbilityData Q;
        public AbilityData E;
        public AbilityData R;
        public AbilityData F;

        public readonly AbilityData GetSlotAbility(int slotIndex, bool airborne = false) => (slotIndex, airborne) switch
        {
            (0, true) => AirLMB,
            (1, true) => AirRMB,
            (0, _) => LMB,
            (1, _) => RMB,
            (2, _) => Q,
            (3, _) => E,
            (4, _) => R,
            (5, _) => F,
            _ => throw new ArgumentOutOfRangeException(nameof(slotIndex))
        };
    }

    public static class CharacterRegistry
    {
        private static CharacterDefinition[]? _definitions;

        public static CharacterDefinition[] All
        {
            get
            {
                if (_definitions == null)
                    _definitions = BuildRegistry();
                return _definitions;
            }
        }

        public static CharacterDefinition Get(CharacterClass c)
        {
            return All[(int)c];
        }

        private static CharacterDefinition[] BuildRegistry()
        {
            return new CharacterDefinition[]
            {
                BuildManki(),      // index 0
                BuildBunny(),      // index 1
            };
        }

        /// <summary>
        /// ═══════════════════════════════════════
        /// MANKI — Mad Bomber Monkey
        /// ═══════════════════════════════════════
        /// </summary>
        /// <returns></returns>
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

                HurtboxCapsules = new HurtboxCapsule[]
                {
                    // Torso: hips → upper chest
                    new(0f, 0.2f, 0f, 0f, 1.0f, 0f, 0.35f),
                    // Head: sphere at neck level
                    new(0f, 1.4f, 0f, 0f, 1.4f, 0f, 0.25f),
                    // Right arm: shoulder → hand
                    new(0.35f, 0.9f, 0f, 0.7f, 0.7f, 0.2f, 0.14f),
                    // Left arm: shoulder → hand
                    new(-0.35f, 0.9f, 0f, -0.7f, 0.7f, 0.2f, 0.14f),
                    // Right leg: hip → foot
                    new(0.15f, 0f, 0f, 0.15f, -0.9f, 0f, 0.18f),
                    // Left leg: hip → foot
                    new(-0.15f, 0f, 0f, -0.15f, -0.9f, 0f, 0.18f),
                },

                // ── Bone-attached hurtboxes (ServerSkeleton-based) ──
                // Follows same pattern as BoneHurtboxSetup.DefaultHumanoid()
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
                HurtboxBoneScale = 0.01f,
                ModelSoleOffset = 0.47f,
                AutoModelYOffset = true,
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
                    AnimationNames = new[] { "melee", "leg_sweep", "backflip" },
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
                    AnimationNames = new[] { "attack_air_lmb" },
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
                    AnimationNames = new[] { "rmb_loop" },
                    SpecialEffectKeys = new[] { "MankiAerosolFlame" },
                    AimedCharge = new AimedChargeData
                    {
                        ChargeAnimName = "rmb_loop",
                        AttackAnimName = "rmb_attack",
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
                    AnimationNames = new[] { "attack_air_rmb" },
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
                    SpecialEffectKeys = new[] { "MankiBigBoom" },
                },
            };
        }

        /// <summary>
        /// ═══════════════════════════════════════
        /// BUNNY — Rabbit Kung-Fu Assassin
        /// ═══════════════════════════════════════
        /// </summary>
        private static CharacterDefinition BuildBunny()
        {
            return new CharacterDefinition
            {
                Class = CharacterClass.Bunny,
                DisplayName = "Bunny",
                CapsuleRadius = 0.5f,
                CapsuleHeight = 1.2f,
                HurtboxRadius = 0.9f,
                Movement = new MovementStats
                {
                    WalkSpeed = 10f,
                    SprintSpeed = 14f,
                    DashSpeed = 32f,
                    AirAcceleration = 16f,
                    JumpForce = 14f,
                    Gravity = 34f,
                    DashDurationTicks = 8,
                    DashCooldownTicks = 48,
                    GroundFriction = 16f,
                    AirFriction = 0.5f,
                    MaxFallSpeed = 48f,
                    MaxJumps = 2,
                },

                // Fallback capsules (manual) until baked skeleton is generated
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
                    new("mixamorig:Head", 0, 0, 0, 0.22f),
                    new("mixamorig:Spine2", 0, 0, 0, 0.28f),
                    new("mixamorig:Hips", 0, 0, 0, 0.28f),
                    new("mixamorig:RightHand", 0, 0, 0, 0.12f),
                    new("mixamorig:LeftHand", 0, 0, 0, 0.12f),
                    new("mixamorig:RightFoot", 0, 0, 0, 0.16f),
                    new("mixamorig:LeftFoot", 0, 0, 0, 0.16f),
                },
                GlbPath = "res://assets/characters/bunny/bunny.tscn",
                BakedDataPath = "res://data/bunny_skeleton.bin",
                HurtboxBoneScale = 0.01f,
                ModelSoleOffset = 0.47f,
                AutoModelYOffset = true,

                LMB = new AbilityData
                {
                    Name = "Rabbit Combo",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { DurationTicks = 10, ChainWindowTicks = 8,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 4, DurationTicks = 4, Radius = 0.5f, OffX = 0, OffY = 0.8f, OffZ = 1.2f, Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 1f, StunTicks = 8, Interruptible = true } },
                                AttackRange = 3.5f, WarpRange = 8f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                        new() { DurationTicks = 10, ChainWindowTicks = 8,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 4, DurationTicks = 5, Radius = 0.55f, OffX = 0, OffY = 0.8f, OffZ = 1.4f, Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, StunTicks = 10, Interruptible = true } },
                                AttackRange = 3.5f, WarpRange = 8f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                        new() { DurationTicks = 16, ChainWindowTicks = 0,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 8, Radius = 0.6f, OffX = 0, OffY = 0.9f, OffZ = 1.5f, Damage = 8f, KnockbackForce = 10f, KnockbackUpward = 10f, StunTicks = 14, Interruptible = true } },
                                AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                    },
                    AnimationNames = new[] { "kick_low", "kick_medium", "kick_high" },
                },

                AirLMB = new AbilityData
                {
                    Name = "Rising Bun",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { DurationTicks = 14, ChainWindowTicks = 0,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 4, DurationTicks = 8, Radius = 0.55f, OffX = 0, OffY = 0.9f, OffZ = 1.3f, Damage = 5f, KnockbackForce = 6f, KnockbackUpward = 8f, StunTicks = 12, Interruptible = true } },
                                AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                    },
                    AnimationNames = new[] { "kick_high" },
                },

                RMB = new AbilityData
                {
                    Name = "Carrot Slam",
                    CooldownTicks = 45,
                    Stages = new AttackStage[]
                    {
                        new() { DurationTicks = 20, ChainWindowTicks = 0,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 8, Radius = 1.8f, OffX = 0, OffY = 0.2f, OffZ = 0f, Damage = 8f, KnockbackForce = 10f, KnockbackUpward = 4f, StunTicks = 12, Interruptible = true } },
                                AttackRange = 3f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                    },
                    AnimationNames = new[] { "punch_down" },
                    SpecialEffectKeys = new[] { "BunnyCarrotSlam" },
                },

                AirRMB = new AbilityData
                {
                    Name = "Helicopter",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { DurationTicks = 18, ChainWindowTicks = 0,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 4, DurationTicks = 10, Radius = 0.6f, OffX = 0, OffY = 0.5f, OffZ = 1.2f, Damage = 7f, KnockbackForce = 10f, KnockbackUpward = -8f, StunTicks = 14, Interruptible = true } },
                                AttackRange = 4f, WarpRange = 10f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                    },
                    AnimationNames = new[] { "kick_wheel" },
                },

                Q = new AbilityData
                {
                    Name = "Whirling Carrot",
                    CooldownTicks = 90,
                    Stages = new AttackStage[]
                    {
                        new() { DurationTicks = 20, ChainWindowTicks = 0,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 8, Radius = 0.5f, OffX = 0, OffY = 1.0f, OffZ = 1.5f, Damage = 6f, KnockbackForce = 3f, KnockbackUpward = 2f, StunTicks = 6, Interruptible = true } },
                                AttackRange = 4f, WarpRange = 8f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
                    },
                    AnimationNames = new[] { "throw" },
                    SpecialEffectKeys = new[] { "BunnyWhirlingCarrot" },
                },

                E = new AbilityData
                {
                    Name = "Flip Kick",
                    CooldownTicks = 75,
                    Stages = new AttackStage[]
                    {
                        new() { DurationTicks = 16, ChainWindowTicks = 0,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 4, DurationTicks = 8, Radius = 0.6f, OffX = 0, OffY = 0.8f, OffZ = -1.0f, Damage = 4f, KnockbackForce = 8f, KnockbackUpward = 2f, StunTicks = 8, Interruptible = true } },
                                AttackRange = 4f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                    },
                    AnimationNames = new[] { "backflip" },
                    SpecialEffectKeys = new[] { "BunnyFlipKick" },
                },

                R = new AbilityData
                {
                    Name = "Dragon's Kick",
                    CooldownTicks = 120,
                    Stages = new AttackStage[]
                    {
                        new() { DurationTicks = 24, ChainWindowTicks = 0,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 8, DurationTicks = 12, Radius = 0.7f, OffX = 0, OffY = 0.9f, OffZ = 2.0f, Damage = 12f, KnockbackForce = 20f, KnockbackUpward = 6f, StunTicks = 18, Interruptible = true } },
                                AttackRange = 5f, WarpRange = 12f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.9f },
                    },
                    AnimationNames = new[] { "kick_high_full" },
                    SpecialEffectKeys = new[] { "BunnyDragonKick" },
                },

                F = new AbilityData
                {
                    Name = "Jade Hare",
                    CooldownTicks = 450,
                    Stages = new AttackStage[]
                    {
                        new() { DurationTicks = 8, ChainWindowTicks = 0,
                                HitboxEvents = Array.Empty<HitboxEvent>(),
                                AttackRange = 0f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                        new() { DurationTicks = 60, ChainWindowTicks = 0,
                                HitboxEvents = new[] { new HitboxEvent { TriggerTick = 0, DurationTicks = 60, Radius = 3.5f, OffX = 0, OffY = 0.5f, OffZ = 0f, Damage = 4f, KnockbackForce = 4f, KnockbackUpward = 2f, StunTicks = 4, Interruptible = false } },
                                AttackRange = 4f, WarpRange = 0f, UseTargetLock = false, RotateTowardTarget = false, TrackingStrength = 0f },
                    },
                    AnimationNames = new[] { "kick_high_full" },
                    SpecialEffectKeys = new[] { "BunnyJadeHare" },
                },
            };
        }
    }
}
