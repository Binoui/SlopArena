using System;

namespace SlopArena.Shared
{
    public enum CharacterClass : byte
    {
        Vanguard,
        Wraith,
        Channeler
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

        // 6 ability slots: 0=LMB, 1=RMB, 2=Q, 3=E, 4=R, 5=F
        public AbilityData LMB;
        public AbilityData RMB;
        public AbilityData Q;
        public AbilityData E;
        public AbilityData R;
        public AbilityData F;

        /// <summary>
        /// Get ability by slot index (0-5).
        /// </summary>
        public readonly AbilityData GetSlotAbility(int slotIndex) => slotIndex switch
        {
            0 => LMB,
            1 => RMB,
            2 => Q,
            3 => E,
            4 => R,
            5 => F,
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
                BuildVanguard(),
                BuildWraith(),
                BuildChanneler(),
            };
        }

        // ═══════════════════════════════════════
        // VANGUARD — heavy, slow, tanky
        // ═══════════════════════════════════════
        private static CharacterDefinition BuildVanguard()
        {
            return new CharacterDefinition
            {
                Class = CharacterClass.Vanguard,
                DisplayName = "Vanguard",
                Movement = new MovementStats
                {
                    WalkSpeed = 9f,
                    SprintSpeed = 12f,
                    DashSpeed = 30f,
                    AirAcceleration = 12f,
                    JumpForce = 14f,
                    Gravity = 42f,
                    DashDurationTicks = 10,
                    DashCooldownTicks = 60,
                    GroundFriction = 22f,
                    AirFriction = 0.5f,
                    MaxFallSpeed = 50f,
                    MaxJumps = 2,
                },

                // LMB — 3-hit combo
                LMB = new AbilityData
                {
                    Name = "Battering Combo",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 5f, Range = 2.5f, HitAngleDeg = 45f, KnockbackForce = 3f, KnockbackUpward = 2f, LungeForce = 12f, StunTicks = 12, SelfLockTicks = 8, ChainWindowTicks = 42 },
                        new() { Shape = AttackShape.MeleeCone, Damage = 7f, Range = 3f, HitAngleDeg = 45f, KnockbackForce = 5f, KnockbackUpward = 2f, LungeForce = 18f, StunTicks = 18, SelfLockTicks = 10, ChainWindowTicks = 42 },
                        new() { Shape = AttackShape.MeleeCone, Damage = 12f, Range = 4f, HitAngleDeg = 45f, KnockbackForce = 15f, KnockbackUpward = 5f, LungeForce = 24f, StunTicks = 24, SelfLockTicks = 12, ChainWindowTicks = 0 },
                    }
                },

                // RMB — heavy, can charge
                RMB = new AbilityData
                {
                    Name = "Piledriver",
                    CooldownTicks = 20,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 10f, Range = 4f, HitAngleDeg = 45f, KnockbackForce = 20f, KnockbackUpward = 8f, LungeForce = 20f, StunTicks = 18, SelfLockTicks = 18, ChainWindowTicks = 0 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 15f, Range = 5f, HitAngleDeg = 45f, KnockbackForce = 30f, KnockbackUpward = 12f, LungeForce = 28f, StunTicks = 24, SelfLockTicks = 30, ChainWindowTicks = 0 },
                    },
                    ChargeHoldTicks = 18, // 0.3s
                },

                // Q — Shield Bash: melee strike, bonus if target has a status
                Q = new AbilityData
                {
                    Name = "Shield Bash",
                    CooldownTicks = 120,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 8f, Range = 3f, HitAngleDeg = 45f, KnockbackForce = 15f, KnockbackUpward = 5f, LungeForce = 14f, StunTicks = 14, SelfLockTicks = 10, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "VanguardShieldBash" },
                },

                // E — War Cry: self-buff shield + push enemies
                E = new AbilityData
                {
                    Name = "War Cry",
                    CooldownTicks = 240,
                    SpecialEffectKeys = new[] { "VanguardWarCry" },
                },

                // R — Intervene: dash forward + delay AoE slow
                R = new AbilityData
                {
                    Name = "Intervene",
                    CooldownTicks = 180,
                    SpecialEffectKeys = new[] { "VanguardIntervene" },
                },

                // F — Thunderclap: leap + delayed AoE
                F = new AbilityData
                {
                    Name = "Thunderclap",
                    CooldownTicks = 420,
                    SpecialEffectKeys = new[] { "VanguardThunderclap" },
                },
            };
        }

        // ═══════════════════════════════════════
        // WRAITH — fast, light, hit-and-run
        // ═══════════════════════════════════════
        private static CharacterDefinition BuildWraith()
        {
            return new CharacterDefinition
            {
                Class = CharacterClass.Wraith,
                DisplayName = "Wraith",
                Movement = new MovementStats
                {
                    WalkSpeed = 11f,
                    SprintSpeed = 15f,
                    DashSpeed = 35f,
                    AirAcceleration = 16f,
                    JumpForce = 18f,
                    Gravity = 38f,
                    DashDurationTicks = 8,
                    DashCooldownTicks = 55,
                    GroundFriction = 14f,
                    AirFriction = 0.4f,
                    MaxFallSpeed = 55f,
                    MaxJumps = 2,
                },

                LMB = new AbilityData
                {
                    Name = "Shank Combo",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 4f, Range = 2f, HitAngleDeg = 40f, KnockbackForce = 2f, KnockbackUpward = 1f, LungeForce = 10f, StunTicks = 10, SelfLockTicks = 6, ChainWindowTicks = 36 },
                        new() { Shape = AttackShape.MeleeCone, Damage = 6f, Range = 2.5f, HitAngleDeg = 40f, KnockbackForce = 4f, KnockbackUpward = 2f, LungeForce = 16f, StunTicks = 14, SelfLockTicks = 8, ChainWindowTicks = 36 },
                        new() { Shape = AttackShape.MeleeCone, Damage = 10f, Range = 3f, HitAngleDeg = 40f, KnockbackForce = 12f, KnockbackUpward = 4f, LungeForce = 22f, StunTicks = 20, SelfLockTicks = 10, ChainWindowTicks = 0 },
                    }
                },

                RMB = new AbilityData
                {
                    Name = "Cross Strike",
                    CooldownTicks = 15,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 8f, Range = 3.5f, HitAngleDeg = 50f, KnockbackForce = 16f, KnockbackUpward = 6f, LungeForce = 18f, StunTicks = 16, SelfLockTicks = 14, ChainWindowTicks = 0 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 13f, Range = 4.5f, HitAngleDeg = 50f, KnockbackForce = 25f, KnockbackUpward = 10f, LungeForce = 26f, StunTicks = 22, SelfLockTicks = 26, ChainWindowTicks = 0 },
                    },
                    ChargeHoldTicks = 18,
                },

                // Q — Viper Shot: projectile that applies Burn
                Q = new AbilityData
                {
                    Name = "Viper Shot",
                    CooldownTicks = 60,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.Projectile, Damage = 8f, Range = 35f * 2.5f, KnockbackForce = 5f, KnockbackUpward = 3f, Radius = 1.2f, SelfLockTicks = 4, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "WraithViperShot" },
                },

                // E — Shadow Step: directional teleport dash
                E = new AbilityData
                {
                    Name = "Shadow Step",
                    CooldownTicks = 180,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.SelfBuff, SelfLockTicks = 6, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "WraithShadowStep" },
                },

                // R — Rapid Fire: 3 quick projectiles in cone
                R = new AbilityData
                {
                    Name = "Rapid Fire",
                    CooldownTicks = 120,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.Projectile, Damage = 5f, Range = 50f * 1.5f, KnockbackForce = 3f, KnockbackUpward = 2f, Radius = 0.8f, SelfLockTicks = 8, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "WraithRapidFire" },
                },

                // F — Freezing Trap: delayed AoE trap, slows
                F = new AbilityData
                {
                    Name = "Freezing Trap",
                    CooldownTicks = 300,
                    SpecialEffectKeys = new[] { "WraithFreezingTrap" },
                },
            };
        }

        // ═══════════════════════════════════════
        // CHANNELER — ranged, control, zone
        // ═══════════════════════════════════════
        private static CharacterDefinition BuildChanneler()
        {
            return new CharacterDefinition
            {
                Class = CharacterClass.Channeler,
                DisplayName = "Channeler",
                Movement = new MovementStats
                {
                    WalkSpeed = 10f,
                    SprintSpeed = 13f,
                    DashSpeed = 30f,
                    AirAcceleration = 14f,
                    JumpForce = 16f,
                    Gravity = 35f,
                    DashDurationTicks = 9,
                    DashCooldownTicks = 58,
                    GroundFriction = 16f,
                    AirFriction = 0.45f,
                    MaxFallSpeed = 52f,
                    MaxJumps = 2,
                },

                LMB = new AbilityData
                {
                    Name = "Arcane Slash",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 4f, Range = 2.8f, HitAngleDeg = 50f, KnockbackForce = 3f, KnockbackUpward = 2f, LungeForce = 10f, StunTicks = 10, SelfLockTicks = 8, ChainWindowTicks = 36 },
                        new() { Shape = AttackShape.MeleeCone, Damage = 6f, Range = 3.2f, HitAngleDeg = 50f, KnockbackForce = 5f, KnockbackUpward = 3f, LungeForce = 16f, StunTicks = 14, SelfLockTicks = 10, ChainWindowTicks = 36 },
                        new() { Shape = AttackShape.MeleeCone, Damage = 10f, Range = 3.8f, HitAngleDeg = 50f, KnockbackForce = 14f, KnockbackUpward = 5f, LungeForce = 22f, StunTicks = 20, SelfLockTicks = 12, ChainWindowTicks = 0 },
                    }
                },

                RMB = new AbilityData
                {
                    Name = "Force Push",
                    CooldownTicks = 15,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.CircleAOE, Damage = 8f, Range = 0f, HitAngleDeg = 0f, Radius = 3.5f, KnockbackForce = 20f, KnockbackUpward = 8f, LungeForce = 0f, StunTicks = 14, SelfLockTicks = 16, ChainWindowTicks = 0 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.CircleAOE, Damage = 14f, Range = 0f, HitAngleDeg = 0f, Radius = 5f, KnockbackForce = 30f, KnockbackUpward = 12f, LungeForce = 0f, StunTicks = 22, SelfLockTicks = 28, ChainWindowTicks = 0 },
                    },
                    ChargeHoldTicks = 18,
                },

                // Q — Frostbolt: projectile, applies Slow
                Q = new AbilityData
                {
                    Name = "Frostbolt",
                    CooldownTicks = 60,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.Projectile, Damage = 12f, Range = 40f * 2.5f, KnockbackForce = 8f, KnockbackUpward = 3f, Radius = 1.2f, SelfLockTicks = 6, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "ChannelerFrostbolt" },
                },

                // E — Dragon's Breath: cone fire, applies Burn
                E = new AbilityData
                {
                    Name = "Dragon's Breath",
                    CooldownTicks = 180,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.MeleeCone, Damage = 8f, Range = 5f, HitAngleDeg = 60f, KnockbackForce = 3f, KnockbackUpward = 1f, SelfLockTicks = 12, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "ChannelerDragonsBreath" },
                },

                // R — Ice Lance: hitscan beam, consumes Slow for bonus
                R = new AbilityData
                {
                    Name = "Ice Lance",
                    CooldownTicks = 120,
                    Stages = new AttackStage[]
                    {
                        new() { Shape = AttackShape.Beam, Damage = 15f, Range = 12f, KnockbackForce = 12f, KnockbackUpward = 5f, Radius = 2f, SelfLockTicks = 8, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "ChannelerIceLance" },
                },

                // F — Meteor: mark zone, delayed meteor strike, Burn
                F = new AbilityData
                {
                    Name = "Meteor",
                    CooldownTicks = 360,
                    SpecialEffectKeys = new[] { "ChannelerMeteor" },
                },
            };
        }
    }
}
