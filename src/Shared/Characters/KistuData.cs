namespace SlopArena.Shared;

/// <summary>
/// ═══════════════════════════════════════
/// KISTU — The Kitsune Blade (agile katana spacing duelist)
/// ═══════════════════════════════════════
/// In-your-face spacing duelist: disjointed blade reach in neutral, launch -> air-juggle payoff.
/// Signature R (Rising Slash) is a homing launcher on a refundable charge pool.
///
/// Melee conversion (2026-08-16): LMB/AirLMB and the Q Counter are retired. The normal
/// tier is now keys 1-4 — kistu_g_1..4 grounded, kistu_a_1..4 air variants (FightGuy
/// template). E (Dash Slash), R (Rising Slash), F (Blade Flurry) unchanged.
///
/// Hitboxes are blade-anchored capsules from the baked _weapon_hilt (pommel) to the baked
/// _weapon_tip point (both synthetic points baked into kistu_skeleton.bin from the sword's
/// actual mesh — the tip is the real visual tip, the hilt the pommel). The tip sweetspot on
/// g_4 is opt-in per move — proof of the hilt→tip tracking mechanism. Numbers are
/// first-pass placeholders — tune against character-kit-design-principles.md.
/// </summary>
public static partial class CharacterRegistry
{
    private static CharacterDefinition BuildKistu()
    {
        var def = new CharacterDefinition
        {
            Class = CharacterClass.Kistu,
            DisplayName = "Kistu",
            CapsuleRadius = 0.35f,
            CapsuleHeight = 1.7f,
            HipHeight = 0.82f,
            HurtboxRadius = 1f,
            Movement = new MovementStats
            {
                RunSpeed = 15f,
                RunAccelerationA = 20f,
                RunAccelerationB = 12f,
                DashSpeed = 24f,
                AirSpeedMax = 8.5f,
                AirAccelStick = 18f,
                AirAccelBase = 3.6f,
                JumpForce = 13f,
                ShortHopForce = 7.8f,
                AirJumpVMultiplier = 0.8f,
                AirJumpHMultiplier = 0.85f,
                Gravity = 36f,
                AirFloatGravity = 0f,
                DashDurationTicks = 16,
                DashCooldownTicks = 44,
                GroundFriction = 8f,
                AirFriction = 6f,
                MaxFallSpeed = 48f,
                FastFallSpeed = 58f,
                MaxJumps = 2,
                JumpSquatTicks = 4,
                FloatWindowTicks = 35,
                RushTicks = 10,
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

            // ═══ NORMAL TIER (keys 1-4) — Melee conversion, FightGuy template ═══
            // Hitboxes are blade-anchored capsules sweeping the baked _weapon_hilt (pommel)
            // → _weapon_tip (the exact sword tip, baked from the weapon mesh). Active
            // windows from the anim authoring: g_1 9-13, g_2 6-11 + 22-27, g_3 6-12, g_4 21-26.

            Slot1 = new AbilitySpec
            {
                Name = "Quick Slash",
                Description = "Fast right-hand sword slash — quick poke, not a combo starter (normal tier, key 1)",
                IconName = "1",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Jab: 5-frame startup, 9-frame active, ~24 total. Flat-ish send that
                    // resets neutral — too much KB at low % to link into a combo.
                    new() { DurationTicks = 24, IasaTicks = 21,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 9, DurationTicks = 10, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip",Damage = 4f, Knockback = new() { Profile = KnockbackProfile.Adaptive, Angle = 30, BaseKnockback = 4f, KnockbackGrowth = 20f }, StunTicks = 14, Interruptible = true } },
                            AttackRange = 2.5f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 1f, G = 0.55f, B = 0.1f, A = 1f } } },
                },
                AnimationNames = new[] { "kistu_g_1" },
            },

            AirSlot1 = new AbilitySpec
            {
                Name = "Air Slash",
                Description = "Quick right-hand sword slash in the air — juggle sustain (air variant, key 1)",
                IconName = "1",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Nair: 6-frame startup, 5-frame active, ~26 total. Slight upward send
                    // keeps enemies airborne for the juggle without popping them out.
                    new() { DurationTicks = 26, IasaTicks = 22, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 15,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 6, DurationTicks = 7, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Damage = 5f, Knockback = new() { Profile = KnockbackProfile.Adaptive, Angle = 30, BaseKnockback = 4f, KnockbackGrowth = 20f }, StunTicks = 14, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 1f, G = 0.55f, B = 0.1f, A = 1f } } },
                },
                AnimationNames = new[] { "kistu_a_1" },
            },

            Slot2 = new AbilitySpec
            {
                Name = "Double Slash",
                Description = "Two forward slashes — first hit pops, second hit sends farther (normal tier, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // ftilt: two-hit sword slash. Hit 1 (6-11) is a soft pop, hit 2 (18-23)
                    // sends out — a combo tool that starts neutral and finishes a string.
                    new() { DurationTicks = 34, IasaTicks = 30,
                            HitboxEvents = new HitboxEvent[]
                            {
                                new() { TriggerTick = 6, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Damage = 3f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 25, BaseKnockback = 3f, KnockbackGrowth = 16f }, StunTicks = 10, Interruptible = true },
                                new() { TriggerTick = 18, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 30, BaseKnockback = 5f, KnockbackGrowth = 26f }, StunTicks = 16, Interruptible = true },
                            },
                            AttackRange = 2.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.85f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 1f, G = 0.55f, B = 0.1f, A = 1f } } },
                },
                AnimationNames = new[] { "kistu_g_2" },
            },

            AirSlot2 = new AbilitySpec
            {
                Name = "Reverse Slash",
                Description = "Reverse sword slash in the air — fast, sends out (air variant, key 2)",
                IconName = "2",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Fair: very fast startup (4-8), sends out — spacing aerial, whiffs are cheap.
                    new() { DurationTicks = 24, IasaTicks = 20, LandingLagTicks = 9, AutoCancelBeforeTicks = 4, AutoCancelAfterTicks = 14,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 4, DurationTicks = 5, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Adaptive, Angle = 40, BaseKnockback = 5f, KnockbackGrowth = 24f }, StunTicks = 16, Interruptible = true } },
                            AttackRange = 2.5f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 1f, G = 0.55f, B = 0.1f, A = 1f } } },
                },
                AnimationNames = new[] { "kistu_a_2" },
            },

            Slot3 = new AbilitySpec
            {
                Name = "Up Slash",
                Description = "Quick upwards slash — anti-air / juggle starter (normal tier, key 3)",
                IconName = "3",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Anti-air: single rising slash, sends upright. Fast startup (6-12) to
                    // catch a jumping opponent.
                    new() { DurationTicks = 32, IasaTicks = 28,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 6, DurationTicks = 7, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Damage = 8f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 80, BaseKnockback = 6f, KnockbackGrowth = 24f }, StunTicks = 18, Interruptible = true } },
                            AttackRange = 2.5f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 1f, G = 0.55f, B = 0.1f, A = 1f } } },
                },
                AnimationNames = new[] { "kistu_g_3" },
            },

            AirSlot3 = new AbilitySpec
            {
                Name = "Air Up Slash",
                Description = "Quick upwards slash — aerial version of grounded 3, juggle payoff (air variant, key 3)",
                IconName = "3",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Uair: fastest startup of the air tier (3-8), sends upright — re-launches
                    // juggled enemies. Hard to hit, precise timing is the goal.
                    new() { DurationTicks = 26, IasaTicks = 22, LandingLagTicks = 9, AutoCancelBeforeTicks = 3, AutoCancelAfterTicks = 14,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 3, DurationTicks = 6, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Damage = 7f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 78, BaseKnockback = 5f, KnockbackGrowth = 22f }, StunTicks = 16, Interruptible = true } },
                            AttackRange = 2.25f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 1f, G = 0.55f, B = 0.1f, A = 1f } } },
                },
                AnimationNames = new[] { "kistu_a_3" },
            },

            Slot4 = new AbilitySpec
            {
                Name = "Heavy Down Slash",
                Description = "Heavy double-handed downwards slash — ground kill normal (normal tier, key 4)",
                IconName = "4",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // dtilt/smash-style: telegraphed startup (14-27 active), big damage +
                    // high growth — the grounded kill move. Whiffs are punished.
                    new() { DurationTicks = 44, IasaTicks = 39,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 14, DurationTicks = 14, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Damage = 12f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 8, BaseKnockback = 8f, KnockbackGrowth = 32f }, StunTicks = 22, Interruptible = true } },
                            AttackRange = 2.75f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 1f, G = 0.55f, B = 0.1f, A = 1f } } },
                },
                AnimationNames = new[] { "kistu_g_4" },
            },

            AirSlot4 = new AbilitySpec
            {
                Name = "Air Heavy Down Slash",
                Description = "Heavy double-handed downwards slash — spike, edgeguard finisher (air variant, key 4)",
                IconName = "4",
                CooldownTicks = 0,
                Stages = new AttackStage[]
                {
                    // Dair: slow telegraphed startup (12-18 active), strong DOWNWARD send —
                    // the off-stage kill, deliberately the opposite of the juggle.
                    new() { DurationTicks = 34, IasaTicks = 29, LandingLagTicks = 9, AutoCancelBeforeTicks = 5, AutoCancelAfterTicks = 22,
                            HitboxEvents = new HitboxEvent[] { new() { TriggerTick = 8, DurationTicks = 7, Shape = HitboxShape.Capsule, Radius = 0.25f, OffX = 0f, OffY = 0f, OffZ = 0f, BoneName = "_weapon_hilt", EndBoneName = "_weapon_tip", Damage = 10f, Knockback = new() { Profile = KnockbackProfile.Custom, Angle = -65, BaseKnockback = 6f, KnockbackGrowth = 26f }, StunTicks = 20, Interruptible = true } },
                            AttackRange = 2.5f, WarpRange = 0f, UseTargetLock = true, RotateTowardTarget = true, TrackingStrength = 0.8f,
                            BoneTrails = new[] { new BoneTrailDef { BoneName = "mixamorig:RightHand", Width = 0.12f, R = 1f, G = 0.55f, B = 0.1f, A = 1f } } },
                },
                AnimationNames = new[] { "kistu_a_4" },
            },

            // ═══ SPECIALS (unchanged from the pre-rework kit) ═══

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

        // ── Air variants (issue #117) — ability slots shared, normals have distinct air specs ──
        // Normal tier air variants (AirSlot1-4) are declared inline above (keys 1-4 air pass).
        def.AirE = def.E;   // Dash Slash: same move in the air — aim + dash unchanged
        def.AirR = def.R;   // Rising Slash: works identically in the air
        def.AirF = def.F;   // Blade Flurry: works identically in the air
        return def;
    }
}
