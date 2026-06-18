namespace SlopArena.Shared
{
    /// <summary>
    /// Targeted projectile ability spec. Adds a ProjectileConfig
    /// for parabolic-arc throw abilities (e.g., Manki Q).
    /// </summary>
    public class RoundBombSpec : AbilitySpec
    {
        /// <summary>Animation to loop while aiming.</summary>
        public string LoopAnimName = "";
        public ProjectileConfig ProjectileConfig;
    }
}
