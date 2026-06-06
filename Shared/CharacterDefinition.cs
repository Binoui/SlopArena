using System;

namespace SlopArena.Shared
{
    public enum CharacterClass : byte
    {
        Vanguard,
        Wraith,
        Manki,
        Knight
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
                BuildVanguard(),
                BuildWraith(),
                BuildManki(),
                BuildKnight(),
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
                    Name = "Battering Combo", AnimationNames = new[] { "great_sword_slash", "great_sword_slash", "great_sword_high_spin_attack" },
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 5f, KnockbackForce = 3f, KnockbackUpward = 2f, LungeForce = 12f, StunTicks = 12, SelfLockTicks = 8, ChainWindowTicks = 42 },
                        new() { Damage = 7f, KnockbackForce = 5f, KnockbackUpward = 2f, LungeForce = 18f, StunTicks = 18, SelfLockTicks = 10, ChainWindowTicks = 42 },
                        new() { Damage = 12f, KnockbackForce = 15f, KnockbackUpward = 5f, LungeForce = 24f, StunTicks = 24, SelfLockTicks = 12, ChainWindowTicks = 0 },
                    }
                },

                // RMB — heavy, can charge
                RMB = new AbilityData
                {
                    Name = "Piledriver", AnimationNames = new[] { "great_sword_attack" },
                    CooldownTicks = 20,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 10f, KnockbackForce = 20f, KnockbackUpward = 8f, LungeForce = 20f, StunTicks = 18, SelfLockTicks = 18, ChainWindowTicks = 0 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Damage = 15f, KnockbackForce = 30f, KnockbackUpward = 12f, LungeForce = 28f, StunTicks = 24, SelfLockTicks = 30, ChainWindowTicks = 0 },
                    },
                    ChargeHoldTicks = 18, // 0.3s
                },

                // Q — Shield Bash: melee strike, bonus if target has a status
                Q = new AbilityData
                {
                    Name = "Shield Bash", AnimationNames = new[] { "great_sword_blocking" },
                    CooldownTicks = 120,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 15f, KnockbackUpward = 5f, LungeForce = 14f, StunTicks = 14, SelfLockTicks = 10, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "VanguardShieldBash" },
                },

                // E — War Cry: self-buff shield + push enemies
                E = new AbilityData
                {
                    Name = "War Cry", AnimationNames = new[] { "great_sword_power_up" },
                    CooldownTicks = 240,
                    SpecialEffectKeys = new[] { "VanguardWarCry" },
                },

                // R — Intervene: dash forward + delay AoE slow
                R = new AbilityData
                {
                    Name = "Intervene", AnimationNames = new[] { "great_sword_slide_attack" },
                    CooldownTicks = 180,
                    SpecialEffectKeys = new[] { "VanguardIntervene" },
                },

                // F — Thunderclap: leap + delayed AoE
                F = new AbilityData
                {
                    Name = "Thunderclap", AnimationNames = new[] { "great_sword_jump_attack" },
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
                        new() { Damage = 4f, KnockbackForce = 2f, KnockbackUpward = 1f, LungeForce = 10f, StunTicks = 10, SelfLockTicks = 6, ChainWindowTicks = 36 },
                        new() { Damage = 6f, KnockbackForce = 4f, KnockbackUpward = 2f, LungeForce = 16f, StunTicks = 14, SelfLockTicks = 8, ChainWindowTicks = 36 },
                        new() { Damage = 10f, KnockbackForce = 12f, KnockbackUpward = 4f, LungeForce = 22f, StunTicks = 20, SelfLockTicks = 10, ChainWindowTicks = 0 },
                    }
                },

                RMB = new AbilityData
                {
                    Name = "Cross Strike",
                    CooldownTicks = 15,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 16f, KnockbackUpward = 6f, LungeForce = 18f, StunTicks = 16, SelfLockTicks = 14, ChainWindowTicks = 0 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Damage = 13f, KnockbackForce = 25f, KnockbackUpward = 10f, LungeForce = 26f, StunTicks = 22, SelfLockTicks = 26, ChainWindowTicks = 0 },
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
                        new() { Damage = 8f, KnockbackForce = 5f, KnockbackUpward = 3f, SelfLockTicks = 4, ChainWindowTicks = 0 },
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
                        new() { SelfLockTicks = 6, ChainWindowTicks = 0 },
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
                        new() { Damage = 5f, KnockbackForce = 3f, KnockbackUpward = 2f, SelfLockTicks = 8, ChainWindowTicks = 0 },
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
        // MANKI — Fire Monkey, agile rushdown/acrobat
        // ═══════════════════════════════════════
        private static CharacterDefinition BuildManki()
        {
            return new CharacterDefinition
            {
                Class = CharacterClass.Manki,
                DisplayName = "Manki",
                Movement = new MovementStats
                {
                    WalkSpeed = 11f,
                    SprintSpeed = 14f,
                    DashSpeed = 34f,
                    AirAcceleration = 16f,
                    JumpForce = 17f,
                    Gravity = 37f,
                    DashDurationTicks = 8,
                    DashCooldownTicks = 56,
                    GroundFriction = 16f,
                    AirFriction = 0.45f,
                    MaxFallSpeed = 53f,
                    MaxJumps = 2,
                },

                // LMB — 3-hit combo: punch, leg sweep, backflip
                LMB = new AbilityData
                {
                    Name = "Monkey Combo",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 4f, KnockbackForce = 3f, KnockbackUpward = 2f, LungeForce = 0f, StunTicks = 10, SelfLockTicks = 8, ChainWindowTicks = 40 },
                        new() { Damage = 5f, KnockbackForce = 5f, KnockbackUpward = 2f, LungeForce = 0f, StunTicks = 14, SelfLockTicks = 10, ChainWindowTicks = 40 },
                        new() { Damage = 10f, KnockbackForce = 10f, KnockbackUpward = 8f, LungeForce = 0f, StunTicks = 18, SelfLockTicks = 12, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "melee", "leg_sweep", "backflip" },
                },

                // Air LMB — upward kick for air combos
                AirLMB = new AbilityData
                {
                    Name = "Air Kick",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 6f, KnockbackForce = 8f, KnockbackUpward = 8f, LungeForce = 8f, StunTicks = 14, SelfLockTicks = 8, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "attack_air_lmb" },
                },

                // RMB — charged punch
                RMB = new AbilityData
                {
                    Name = "Fire Fist",
                    CooldownTicks = 15,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 16f, KnockbackUpward = 6f, LungeForce = 18f, StunTicks = 16, SelfLockTicks = 14, ChainWindowTicks = 0 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Damage = 14f, KnockbackForce = 28f, KnockbackUpward = 10f, LungeForce = 28f, StunTicks = 22, SelfLockTicks = 26, ChainWindowTicks = 0 },
                    },
                    ChargeHoldTicks = 18,
                    AnimationNames = new[] { "attack_heavy_charge" },
                },

                // Air RMB — drop kick spike
                AirRMB = new AbilityData
                {
                    Name = "Drop Kick",
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 12f, KnockbackUpward = -8f, LungeForce = 14f, StunTicks = 16, SelfLockTicks = 10, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "attack_air_rmb" },
                },

                // Q — Fire Lash: ground kick, slows on hit
                Q = new AbilityData
                {
                    Name = "Fire Lash",
                    CooldownTicks = 60,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 6f, KnockbackForce = 8f, KnockbackUpward = 3f, SelfLockTicks = 10, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "spell_q" },
                    SpecialEffectKeys = new[] { "MankiFireLash" },
                },

                // E — Rising Flame: vertical uppercut, anti-air / recovery
                E = new AbilityData
                {
                    Name = "Rising Flame",
                    CooldownTicks = 120,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 5f, KnockbackUpward = 12f, LungeForce = 6f, StunTicks = 14, SelfLockTicks = 12, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "spell_e" },
                    SpecialEffectKeys = new[] { "MankiRisingFlame" },
                },

                // R — Ember Burst: AoE explosion around self
                R = new AbilityData
                {
                    Name = "Ember Burst",
                    CooldownTicks = 150,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 10f, KnockbackForce = 20f, KnockbackUpward = 5f, SelfLockTicks = 12, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "spell_r" },
                    SpecialEffectKeys = new[] { "MankiEmberBurst" },
                },

                // F — Inferno Dance (Ult): dash + auto-combo + explosion
                F = new AbilityData
                {
                    Name = "Inferno Dance",
                    CooldownTicks = 420,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 20f, KnockbackForce = 22f, KnockbackUpward = 8f, LungeForce = 30f, StunTicks = 28, SelfLockTicks = 24, ChainWindowTicks = 0 },
                    },
                    AnimationNames = new[] { "spell_f" },
                    SpecialEffectKeys = new[] { "MankiInfernoDance" },
                },
            };
        }

    // KNIGHT — medium, balanced, based on King Arthur (DKO)
    // ═══════════════════════════════════════
    private static CharacterDefinition BuildKnight()
        {
            return new CharacterDefinition
            {
                Class = CharacterClass.Knight,
                DisplayName = "Knight",
                Movement = new MovementStats
                {
                    WalkSpeed = 10f,
                    SprintSpeed = 14f,
                    DashSpeed = 32f,
                    AirAcceleration = 14f,
                    JumpForce = 16f,
                    Gravity = 38f,
                    DashDurationTicks = 10,
                    DashCooldownTicks = 58,
                    GroundFriction = 18f,
                    AirFriction = 0.45f,
                    MaxFallSpeed = 52f,
                    MaxJumps = 2,
                },

                // LMB — Royal Combo: 3-hit sword combo
                LMB = new AbilityData
                {
                    Name = "Royal Combo",
                    AnimationNames = new[] { "attack_2h_slice", "attack_2h_chop", "attack_2h_spin" },
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 6f, KnockbackForce = 3f, KnockbackUpward = 2f, LungeForce = 12f, StunTicks = 12, SelfLockTicks = 60, ChainWindowTicks = 80 },
                        new() { Damage = 6f, KnockbackForce = 5f, KnockbackUpward = 2f, LungeForce = 18f, StunTicks = 18, SelfLockTicks = 60, ChainWindowTicks = 80 },
                        new() { Damage = 12f, KnockbackForce = 15f, KnockbackUpward = 5f, LungeForce = 24f, StunTicks = 24, SelfLockTicks = 75, ChainWindowTicks = 90 },
                    }
                },

                // Air LMB — Rising Slash: upward launch for juggle follow-ups
                AirLMB = new AbilityData
                {
                    Name = "Rising Slash",
                    AnimationNames = new[] { "attack_2h_slice" },
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 6f, KnockbackForce = 8f, KnockbackUpward = 8f, LungeForce = 10f, StunTicks = 14, SelfLockTicks = 8, ChainWindowTicks = 0 },
                    }
                },

                // RMB — Heavy Sunder: overhead strike (hold for charged)
                RMB = new AbilityData
                {
                    Name = "Heavy Sunder",
                    AnimationNames = new[] { "attack_2h_chop" },
                    CooldownTicks = 15,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 10f, KnockbackForce = 20f, KnockbackUpward = 8f, LungeForce = 20f, StunTicks = 18, SelfLockTicks = 18, ChainWindowTicks = 0 },
                    },
                    ChargedStages = new AttackStage[]
                    {
                        new() { Damage = 15f, KnockbackForce = 30f, KnockbackUpward = 12f, LungeForce = 28f, StunTicks = 24, SelfLockTicks = 30, ChainWindowTicks = 0 },
                    },
                    ChargeHoldTicks = 18,
                },

                // Air RMB — Aerial Slam: spike enemies downward
                AirRMB = new AbilityData
                {
                    Name = "Aerial Slam",
                    AnimationNames = new[] { "attack_2h_chop" },
                    CooldownTicks = 0,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 8f, KnockbackForce = 15f, KnockbackUpward = -8f, LungeForce = 15f, StunTicks = 16, SelfLockTicks = 10, ChainWindowTicks = 0 },
                    }
                },

                // Q — Blinding Light: frontal cone stun
                Q = new AbilityData
                {
                    Name = "Blinding Light",
                    AnimationNames = new[] { "attack_2h_spin" },
                    CooldownTicks = 180,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 12f, KnockbackForce = 10f, KnockbackUpward = 5f, LungeForce = 0f, StunTicks = 75, SelfLockTicks = 14, ChainWindowTicks = 0 },
                    }
                },

                // E — Lion's Advance: gap closer lunge strike
                E = new AbilityData
                {
                    Name = "Lion's Advance",
                    AnimationNames = new[] { "attack_2h_stab" },
                    CooldownTicks = 120,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 10f, KnockbackForce = 15f, KnockbackUpward = 5f, LungeForce = 35f, StunTicks = 16, SelfLockTicks = 14, ChainWindowTicks = 0 },
                    }
                },

                // R — Knight's Resolve: parry stance, counter-attack if struck
                R = new AbilityData
                {
                    Name = "Knight's Resolve",
                    AnimationNames = new[] { "block_idle" },
                    CooldownTicks = 240,
                    Stages = new AttackStage[]
                    {
                        new() { SelfLockTicks = 6, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "KnightKnightsResolve" },
                },

                // F — Might of Excalibur (Ult): ground slam + buff duration
                F = new AbilityData
                {
                    Name = "Might of Excalibur",
                    AnimationNames = new[] { "attack_2h_chop" },
                    CooldownTicks = 420,
                    Stages = new AttackStage[]
                    {
                        new() { Damage = 20f, KnockbackForce = 25f, KnockbackUpward = 10f, LungeForce = 0f, StunTicks = 30, SelfLockTicks = 20, ChainWindowTicks = 0 },
                    },
                    SpecialEffectKeys = new[] { "KnightMightOfExcalibur" },
                },
            };
        }
    }
}
