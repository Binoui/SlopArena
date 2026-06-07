using System;

namespace SlopArena.Shared
{
    public enum CharacterClass : byte
    {
        Manki
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
        public CharacterClass Class;
        public string DisplayName;
        public MovementStats Movement;

        public float CapsuleRadius;
        public float CapsuleHeight;
        public float HurtboxRadius;

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
                BuildManki(),
            };
        }

        // ═══════════════════════════════════════
        // MANKI — Mad Bomber Monkey
        // ═══════════════════════════════════════
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

                // LMB — 3-hit combo with startup per stage
                LMB = new AbilityData
                {
                    Name = "Monkey Combo",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 2f, StunTicks = 10, SelfLockTicks = 46, ChainWindowTicks = 0, StartupTicks = 6 },
                        new() { Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, StunTicks = 14, SelfLockTicks = 30, ChainWindowTicks = 0, StartupTicks = 8 },
                        new() { Damage = 10f, KnockbackForce = 10f, KnockbackUpward = 8f, StunTicks = 18, SelfLockTicks = 56, ChainWindowTicks = 0, StartupTicks = 10 },
                    },
                    AnimationNames = new[] { "melee", "leg_sweep", "backflip" },
                },

                AirLMB = new AbilityData
                {
                    Name = "Air Punch",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 6f, KnockbackForce = 8f, KnockbackUpward = 8f, LungeForce = 8f, StunTicks = 14, SelfLockTicks = 16, ChainWindowTicks = 0, StartupTicks = 4 },
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
                        new() { Damage = 8f, KnockbackForce = 14f, KnockbackUpward = 4f, StunTicks = 14, SelfLockTicks = 50, ChainWindowTicks = 0, StartupTicks = 8 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Damage = 14f, KnockbackForce = 24f, KnockbackUpward = 8f, StunTicks = 20, SelfLockTicks = 40, ChainWindowTicks = 0, StartupTicks = 10 },
                    },
                    ChargeHoldTicks = 45,
                    AnimationNames = new[] { "rmb_loop" },
                    SpecialEffectKeys = new[] { "MankiAerosolFlame" },
                    AimedCharge = new AimedChargeData
                    {
                        ChargeAnimName = "rmb_loop",
                        AttackAnimName = "rmb_attack",
                        ConeAngle = 60f,
                        ConeRange = 5f,
                        MaxChargeTicks = 45,
                    },
                },

                AirRMB = new AbilityData
                {
                    Name = "Drop Kick",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 12f, KnockbackUpward = -8f, LungeForce = 14f, StunTicks = 16, SelfLockTicks = 22, ChainWindowTicks = 0, StartupTicks = 6 },
                    },
                    AnimationNames = new[] { "attack_air_rmb" },
                },

                Q = new AbilityData
                {
                    Name = "Round Bomb",
                    CooldownTicks = 90,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 10f, KnockbackUpward = 6f, SelfLockTicks = 20, ChainWindowTicks = 0, StartupTicks = 10 },
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
                        new() { Damage = 5f, KnockbackForce = 4f, KnockbackUpward = 4f, SelfLockTicks = 35, ChainWindowTicks = 0, StartupTicks = 12 },
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
                        new() { Damage = 14f, KnockbackForce = 18f, KnockbackUpward = 6f, SelfLockTicks = 42, ChainWindowTicks = 0, StartupTicks = 14 },
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
                        new() { Damage = 20f, KnockbackForce = 22f, KnockbackUpward = 8f, SelfLockTicks = 55, ChainWindowTicks = 0, StartupTicks = 16 },
                    },
                    AnimationNames = new[] { "spell_f" },
                    SpecialEffectKeys = new[] { "MankiBigBoom" },
                },
            };
        }
    }
}
