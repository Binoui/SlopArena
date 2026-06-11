namespace SlopArena.Shared
{
    /// <summary>
    /// One stage of an ability. A simple ability has 1 stage.
    /// A combo ability (like LMB) has N stages chained by ChainWindowTicks.
    /// Range-based: supports attack range, warp range, and target tracking.
    /// </summary>
    public struct AttackStage
    {
        public float Damage;
        public float KnockbackForce;
        public float KnockbackUpward;
        /// <summary>
        /// Forward burst during attack
        /// </summary>
        public float LungeForce;
        /// <summary>
        /// Hitstun duration in ticks
        /// </summary>
        public ushort StunTicks;
        /// <summary>
        /// Self animation lock in ticks
        /// </summary>
        public ushort SelfLockTicks;
        /// <summary>
        /// 0 = final stage / no chain
        /// </summary>
        public ushort ChainWindowTicks;
        /// <summary>
        /// Frames before hitbox activates (startup anim)
        /// </summary>
        public ushort StartupTicks;

        /// <summary>
        /// Range-based range system
        /// </summary>
        public float AttackRange;      // Distance where attack can hit immediately (e.g., 5m)
        /// <summary>
        /// Distance where auto-dash triggers (e.g., 12m)
        /// </summary>
        public float WarpRange;
        /// <summary>
        /// Warp speed now driven by character Movement.SprintSpeed (not per-stage)
        /// </summary>
        public bool UseTargetLock;     // true = use soft lock system for this attack
        /// <summary>
        /// true = auto-rotate toward target during attack
        /// </summary>
        public bool RotateTowardTarget;
        /// <summary>
        /// 0-1: rotation lerp toward target per frame (0.8 = strong tracking)
        /// </summary>
        public float TrackingStrength;
    }

    /// <summary>
    /// Full definition of one ability slot (0-5).
    /// Stages.Length = 1 for single hit, N for combo chains.
    /// SpecialEffectKeys reference methods in AbilityRegistry for
    /// effects that stages can't express (hitbox spawning, teleport, etc.).
    /// AnimationNames per stage: "attack_2h_slice", "spell_cast", etc.
    /// </summary>
    public struct AbilityData
    {
        public string Name;
        /// <summary>
        /// 0 = no cooldown
        /// </summary>
        public ushort CooldownTicks;
        public AttackStage[] Stages;

        /// <summary>Hold-to-charge variant. Triggers after ChargeHoldTicks.</summary>
        public AttackStage[] ChargedStages;
        /// <summary>Ticks to hold before charged version fires.</summary>
        public ushort ChargeHoldTicks;

        /// <summary>Special effects (hitbox spawning, teleport, buff, delayed AoE). Keys in AbilityRegistry.</summary>
        public string[] SpecialEffectKeys;

        /// <summary>Animation name for each stage.
        /// Each character defines their own animation keys.
        /// Example: LMB = ["attack_2h_slice", "attack_2h_chop", "attack_2h_spin"]
        /// </summary>
        public string[] AnimationNames;

        /// <summary>Optional aimed-charge config (e.g., RMB cone flamethrower).</summary>
        public AimedChargeData? AimedCharge;
    }

    /// <summary>
    /// Config for an aimed charge ability.
    /// Player enters a charge state with a ground-projected AoE indicator,
    /// then releases to fire the attack.
    /// </summary>
    public struct AimedChargeData
    {
        /// <summary>Animation to loop during charge.</summary>
        public string ChargeAnimName;
        /// <summary>Animation to play on release.</summary>
        public string AttackAnimName;
        /// <summary>Cone angle in degrees (e.g., 60 = 60° cone).</summary>
        public float ConeAngle;
        /// <summary>Cone length/range in world units.</summary>
        public float ConeRange;
        /// <summary>Max charge ticks for power scaling (0 = no scaling).</summary>
        public ushort MaxChargeTicks;
    }
}
