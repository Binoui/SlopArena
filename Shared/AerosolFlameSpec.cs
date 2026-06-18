namespace SlopArena.Shared
{
    /// <summary>
    /// Hold-to-charge ability spec. All charge-related data lives here directly.
    /// </summary>
    public class AerosolFlameSpec : AbilitySpec
    {
        /// <summary>Animation to loop during charge.</summary>
        public string ChargeAnimName = "";
        /// <summary>Cone angle in degrees (e.g., 60 = 60° cone).</summary>
        public float ConeAngle;
        /// <summary>Cone length in world units.</summary>
        public float ConeRange;
        /// <summary>Max charge ticks for power scaling (0 = no scaling).</summary>
        public ushort MaxChargeTicks;
    }
}
