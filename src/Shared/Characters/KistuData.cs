namespace SlopArena.Shared;

/// <summary>
/// ═══════════════════════════════════════
/// KISTU — The Kitsune Blade (agile katana spacing duelist)
/// ═══════════════════════════════════════
/// In-your-face spacing duelist: disjointed blade reach in neutral, launch -> air-juggle payoff.
/// Signature R (Rising Slash) is a homing launcher on a refundable charge pool.
/// Placeholder art: reuses the FightGuy prefab + empty baked data (capsule hurtboxes) so the
/// kit is fully playable in sim before its own model/animation assets exist.
/// Numbers are first-pass placeholders — tune against character-kit-design-principles.md.
/// </summary>
public static partial class CharacterRegistry
{
    private static CharacterDefinition BuildKistu()
    {
        return new CharacterDefinition
        {
            Class = CharacterClass.Kistu,
            DisplayName = "Kistu",
            CapsuleRadius = 0.35f,
            CapsuleHeight = 1.7f,
            HipHeight = 0.82f,
            HurtboxRadius = 1f,
            Movement = new MovementStats
            {
                WalkSpeed = 11f,
                SprintSpeed = 15f,
                DashSpeed = 34f,
                AirAcceleration = 18f,
                JumpForce = 13f,
                Gravity = 36f,
                AirFloatGravity = 0f,
                DashDurationTicks = 16,
                DashCooldownTicks = 44,
                GroundFriction = 16f,
                AirFriction = 0.5f,
                MaxFallSpeed = 48f,
                MaxJumps = 2,
                JumpSquatTicks = 4,
                FloatWindowTicks = 35,
                FallRampDuration = 10,
            },

            // Baked skeleton → shared mixamorig bone hurtboxes.
            HurtboxBoneDefs = MixamorigBoneDefs,
            HurtboxCapsules = new HurtboxCapsule[]
            {
                new(0f, 0.2f, 0f, 0f, 0.9f, 0f, 0.3f),
                new(0f, 1.2f, 0f, 0f, 1.2f, 0f, 0.22f),
                new(0.3f, 0.8f, 0f, 0.6f, 0.6f, 0.2f, 0.12f),
                new(-0.3f, 0.8f, 0f, -0.6f, 0.6f, 0.2f, 0.12f),
                new(0.15f, 0f, 0f, 0.15f, -0.8f, 0f, 0.16f),
                new(-0.15f, 0f, 0f, -0.15f, -0.8f, 0f, 0.16f),
            },
            VisualScale = 1f,
            HurtboxBoneScale = 0.85f,
            ModelSoleOffset = 0f,
            AutoModelYOffset = true,
            ModelYOffset = 0f,
            ModelResourcePath = "Characters/Kistu", // Updated to actual prefab path
            BakedDataPath = "res://data/kistu_skeleton.bin",

            // ═══ ABILITIES ═══

            // LMB — Light Slash (single move, reach-y capsule slash)
            LMB = new AbilitySpec
            {
                Name = "Light Slash",
                Description = "Fast katana slash — a single committed move (no auto-combo)",
                IconName = "lmb",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 30, LungeForce = 6f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.4f,
                                    OffX = 0, OffY = 0.7f, OffZ = 0.7f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.8f,
                                    Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 16, Interruptible = true } },
                            AttackRange = 2.5f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f },
                },
                AnimationNames = new[] { "spell_lmb_1" },
                Params = new() { ["lunge_duration"] = 6f },
            },

            // AirLMB — Air Slash (single aerial slash, juggle sustain, near-neutral/up KB)
            AirLMB = new AbilitySpec
            {
                Name = "Air Slash",
                Description = "Aerial slash. Low commit; keeps enemies airborne for juggles.",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 24, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.4f,
                                    OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 1.6f,
                                    Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 14, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f },
                },
                AnimationNames = new[] { "spell_lmb_air_1" },
            },

            // RMB — Charged Spin: tap = horizontal poke, hold = charged kill (big horizontal launch)
            RMB = new AbilitySpec
            {
                Name = "Charged Spin",
                Description = "Hold to charge a spinning slash. Tap = quick poke; charged = blast-zone kill.",
                IconName = "rmb",
                CooldownTicks = 60,
                Behavior = AbilityBehavior.ChargeAttack,
                ChargeHoldTicks = 50,
                Stages = new AttackStage[]
                {
                    // Stage 0: hold/charge phase (safety net 300 ticks)
                    new() { DurationTicks = 300, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            LungeForce = 0f, AttackRange = 0f, WarpRange = 0f },
                    // Stage 1: tap poke (quick, horizontal knockback)
                    new() { DurationTicks = 30, LungeForce = 3f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.45f,
                                    OffX = 0, OffY = 0.7f, OffZ = 0.7f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.9f,
                                    Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Medium }, StunTicks = 18, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f },
                },
                ChargedStages = new AttackStage[]
                {
                    // Charged: big horizontal-launch kill spin
                    new() { DurationTicks = 44, LungeForce = 5f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 12, DurationTicks = 7, Shape = HitboxShape.Capsule, Radius = 0.6f,
                                    OffX = 0, OffY = 0.7f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 2.1f,
                                    Damage = 16f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 15, BaseKnockback = 18, KnockbackGrowth = 10 },
                                    StunTicks = 24, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_rmb_loop", "spell_rmb_attack" },
            },

            // AirRMB — Falling Slash (downward spike; hold to charge)
            AirRMB = new AbilitySpec
            {
                Name = "Falling Slash",
                Description = "Hold to charge a committed downward air slash; tap = quick slash, charged = heavier slash that spikes enemies toward the floor/blast zone.",
                CooldownTicks = 0,
                Behavior = AbilityBehavior.ChargeAttack,
                ChargeHoldTicks = 45,
                Stages = new AttackStage[]
                {
                    // Stage 0: hold/charge phase (no hitboxes; targeting config lives here)
                    new() { DurationTicks = 60, 
                            HitboxEvents = Array.Empty<HitboxEvent>(),
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
                    // Stage 1: tap slash (same numbers as the pre-charge air RMB)
                    new() { DurationTicks = 26, LungeForce = 0f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 14, Shape = HitboxShape.Capsule, Radius = 0.45f,
                                    OffX = 0, OffY = 0.4f, OffZ = 0.6f, EndOffX = 0, EndOffY = -0.6f, EndOffZ = 1.4f,
                                    Damage = 9f, Knockback = new() { Profile = KnockbackProfile.Spike }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
                },
                ChargedStages = new AttackStage[]
                {
                    // Charged: wider blade, more damage
                    new() { DurationTicks = 26, LungeForce = 0f,
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 6, DurationTicks = 16, Shape = HitboxShape.Capsule, Radius = 0.6f,
                                    OffX = 0, OffY = 0.4f, OffZ = 0.6f, EndOffX = 0, EndOffY = -0.6f, EndOffZ = 1.5f,
                                    Damage = 13f, Knockback = new() { Profile = KnockbackProfile.Spike }, StunTicks = 24, Interruptible = true } },
                            AttackRange = 2f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.7f },
                },
                AnimationNames = new[] { "spell_air_rmb", "spell_air_rmb" },
            },

            // Q — Counter (parry window → launch riposte)
            Q = new AbilitySpec
            {
                Name = "Counter",
                Description = "Parry stance. If struck during the window, riposte-launches the attacker.",
                IconName = "q",
                CooldownTicks = 150,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 40, HitboxEvents = System.Array.Empty<HitboxEvent>(),
                            AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_q_parry", "spell_q_riposte" },
                Params = new()
                {
                    ["duration"] = 40f,
                    ["window_start"] = 4f,
                    ["window_end"] = 18f,
                    ["riposte_damage"] = 12f,
                    ["riposte_base"] = 12f,
                    ["riposte_growth"] = 6f,
                    ["riposte_angle"] = 25f,
                    ["riposte_stun"] = 22f,
                },
            },

            // E — Dash Slash (aim the direction on the ground, release to dash a set distance)
            E = new AbilitySpec
            {
                Name = "Dash Slash",
                Description = "Hold to aim the dash direction on the ground (movement stays unlocked), release to dash-slash a set distance.",
                IconName = "e",
                CooldownTicks = 90,
                Behavior = AbilityBehavior.DirectionalDash,
                AimMode = AimMode.GroundVector,
                Stages = new AttackStage[]
                {
                    // Dash stage: the ability spawns a fresh hitbox at the character's position
                    // every dash tick (per-tick sweep along the aim axis, radius covers her sides).
                    // DurationTicks also drives the dash clip playback speed (frameCount / DurationTicks).
                    new() { DurationTicks = 16, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 0, DurationTicks = 1, Shape = HitboxShape.Capsule, Radius = 0.5f,
                                    OffX = 0, OffY = 0.7f, OffZ = 0.5f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.3f,
                                    Damage = 9f, Knockback = new() { Profile = KnockbackProfile.Medium }, StunTicks = 16, Interruptible = true } },
                            AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_e" },
                Params = new()
                {
                    ["dash_distance"] = 5f,          // exact meters travelled on release
                    ["dash_duration_ticks"] = 16f,   // dash length in ticks (matches stage duration)
                    ["max_aim_ticks"] = 180f,        // 3s aim cap, then auto-release
                },
            },

            // R — Rising Slash (signature: homing launcher, refundable charge pool, vertical recovery)
            R = new AbilitySpec
            {
                Name = "Rising Slash",
                Description = "Homing rising slash that launches. Charges refund on hit (juggle) — 2-charge pool.",
                IconName = "r",
                CooldownTicks = 0, // limited by the charge pool, not a flat cooldown
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 24, 
                            HitboxEvents = new[] { new HitboxEvent { TriggerTick = 5, DurationTicks = 8, Shape = HitboxShape.Sphere, Radius = 0.9f,
                                    OffX = 0, OffY = 1.0f, OffZ = 0.6f,
                                    Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 7, KnockbackGrowth = 5 },
                                    StunTicks = 22, Interruptible = true } },
                            AttackRange = 0f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_r" },
                Params = new()
                {
                    ["max_charges"] = 2f,
                    ["charge_regen_ticks"] = 240f, // 4s to recover one charge
                    ["rise_speed"] = 8f,
                    ["rise_ticks"] = 8f,
                    ["homing_range"] = 7f,
                    ["homing_speed"] = 10f,
                },
            },

            // F — Blade Flurry (ult: moving multi-slash → hard launch)
            F = new AbilitySpec
            {
                Name = "Blade Flurry",
                Description = "Committed forward flurry of slashes ending in a hard launch.",
                IconName = "f",
                CooldownTicks = 540,
                Behavior = AbilityBehavior.MeleeCombo,
                AimMode = AimMode.None,
                Stages = new AttackStage[]
                {
                    new() { DurationTicks = 64, 
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 8, DurationTicks = 4, Shape = HitboxShape.Capsule, Radius = 0.5f,
                                        OffX = 0, OffY = 0.7f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.9f,
                                        Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = false },
                                new() { TriggerTick = 16, DurationTicks = 4, Shape = HitboxShape.Capsule, Radius = 0.5f,
                                        OffX = 0, OffY = 0.7f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.9f,
                                        Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = false },
                                new() { TriggerTick = 24, DurationTicks = 4, Shape = HitboxShape.Capsule, Radius = 0.5f,
                                        OffX = 0, OffY = 0.7f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.9f,
                                        Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = false },
                                new() { TriggerTick = 32, DurationTicks = 4, Shape = HitboxShape.Capsule, Radius = 0.5f,
                                        OffX = 0, OffY = 0.7f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.7f, EndOffZ = 1.9f,
                                        Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Light }, StunTicks = 12, Interruptible = false },
                                // Finisher: hard launch
                                new() { TriggerTick = 44, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.6f,
                                        OffX = 0, OffY = 0.8f, OffZ = 0.6f, EndOffX = 0, EndOffY = 0.8f, EndOffZ = 2.0f,
                                        Damage = 12f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 20, BaseKnockback = 14, KnockbackGrowth = 8 },
                                        StunTicks = 24, Interruptible = false },
                            },
                            AttackRange = 4f, WarpRange = 0f },
                },
                AnimationNames = new[] { "spell_f" },
                Params = new()
                {
                    ["forward_speed"] = 7f,
                    ["move_ticks"] = 40f,
                },
            },
        };
    }
}
