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

    /// <summary>
    /// Complete data-driven definition of a playable character.
    /// All 6 ability slots (0-5) use the same AbilityData struct.
    /// Add new characters by creating a new entry in CharacterRegistry.
    /// </summary>
    public struct CharacterDefinition
    {
        public CharacterClass Class;
        public string DisplayName;
        public MovementStats Movement;

        // Collision shape
        public float CapsuleRadius;
        public float CapsuleHeight;
        public float HurtboxRadius;

        // 8 ability slots: LMB, AirLMB, RMB, AirRMB, Q, E, R, F
        public AbilityData LMB;
        public AbilityData RMB;
        // Air attacks: separate abilities used when airborne
        public AbilityData AirLMB;
        public AbilityData AirRMB;
        public AbilityData Q;
        public AbilityData E;
        public AbilityData R;
        public AbilityData F;

        /// <summary>
        /// Get ability by slot index (0-5).
        /// When airborne, slots 0 (LMB) and 1 (RMB) return air variants.
        /// </summary>
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

    /// <summary>
    /// Registry of all character definitions. Add new characters here.
    /// </summary>
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
        // MANKI — Fire Monkey, agile rushdown/acrobat
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

                // LMB — 3-hit combo: punch, leg sweep, backflip
                // Medium commitment (36-56 ticks), chain window generous (40 ticks)
                LMB = new AbilityData
                {
                    Name = "Monkey Combo",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 2f, LungeForce = 0f, StunTicks = 10, SelfLockTicks = 66, ChainWindowTicks = 80 },
                        new() { Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, LungeForce = 0f, StunTicks = 14, SelfLockTicks = 70, ChainWindowTicks = 80 },
                        new() { Damage = 10f, KnockbackForce = 10f, KnockbackUpward = 8f, LungeForce = 0f, StunTicks = 18, SelfLockTicks = 86, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "melee", "leg_sweep", "backflip" },
                },

                // Air LMB — upward kick for air combos
                // Quick air-to-air, 16 ticks ≈ 267ms
                AirLMB = new AbilityData
                {
                    Name = "Air Kick",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 6f, KnockbackForce = 8f, KnockbackUpward = 8f, LungeForce = 8f, StunTicks = 14, SelfLockTicks = 16, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "attack_air_lmb" },
                },

                // RMB — charged punch (heavy attack)
                // Uncharged: 33f startup + ~20f endlag ≈ 53f total (SelfLockTicks=50)
                // Charged:   45f charge hold + 17f startup + ~20f endlag ≈ 82f total
                RMB = new AbilityData
                {
                    Name = "Fire Fist",
                    CooldownTicks = 30,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 16f, KnockbackUpward = 6f, LungeForce = 18f, StunTicks = 16, SelfLockTicks = 50, ChainWindowTicks = 0 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Damage = 14f, KnockbackForce = 28f, KnockbackUpward = 10f, LungeForce = 28f, StunTicks = 22, SelfLockTicks = 40, ChainWindowTicks = 0 },
                    },
                    ChargeHoldTicks = 45,
                    AnimationNames = new[] { "attack_heavy_charge" },
                },

                // Air RMB — drop kick spike
                // Heavier commitment in air (22 ticks ≈ 367ms)
                AirRMB = new AbilityData
                {
                    Name = "Drop Kick",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 12f, KnockbackUpward = -8f, LungeForce = 14f, StunTicks = 16, SelfLockTicks = 22, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "attack_air_rmb" },
                },

                // Q — Fire Lash: ground kick, slows on hit
                // Ability: medium commitment (30 ticks = 500ms)
                Q = new AbilityData
                {
                    Name = "Fire Lash",
                    CooldownTicks = 90,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 6f, KnockbackForce = 8f, KnockbackUpward = 3f, SelfLockTicks = 30, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "spell_q" },
                    SpecialEffectKeys = new[] { "MankiFireLash" },
                },

                // E — Rising Flame: vertical uppercut, anti-air / recovery
                // Ability: high commitment (35 ticks ≈ 583ms), long cooldown
                E = new AbilityData
                {
                    Name = "Rising Flame",
                    CooldownTicks = 180,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 5f, KnockbackUpward = 12f, LungeForce = 6f, StunTicks = 14, SelfLockTicks = 35, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "spell_e" },
                    SpecialEffectKeys = new[] { "MankiRisingFlame" },
                },

                // R — Ember Burst: AoE explosion around self
                // Strong ability: heavy commitment (42 ticks ≈ 700ms)
                R = new AbilityData
                {
                    Name = "Ember Burst",
                    CooldownTicks = 240,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 10f, KnockbackForce = 20f, KnockbackUpward = 5f, SelfLockTicks = 42, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "spell_r" },
                    SpecialEffectKeys = new[] { "MankiEmberBurst" },
                },

                // F — Inferno Dance (Ult): dash + auto-combo + explosion
                // Ultimate: massive commitment (55 ticks ≈ 917ms), 10s cooldown
                F = new AbilityData
                {
                    Name = "Inferno Dance",
                    CooldownTicks = 600,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 20f, KnockbackForce = 22f, KnockbackUpward = 8f, LungeForce = 30f, StunTicks = 28, SelfLockTicks = 55, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "spell_f" },
                    SpecialEffectKeys = new[] { "MankiInfernoDance" },
                },
            };
        }
    }
}
