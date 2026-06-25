namespace SlopArena.Shared
{
    /// <summary>
    /// Placeable mine ability spec. The server spawns a static hitbox with
    /// zero velocity and gravity. After DurationTicks (or manual detonation),
    /// an explosion hitbox spawns at the mine's position.
    /// Self-spawns/detonates via SpawnHitbox override.
    /// </summary>
    public class ExplosiveMineSpec : AbilitySpec
    {
        public ProjectileExplosion ExplosionConfig;
        /// <summary>Radius of the mine hitbox (and visual).</summary>
        public float MineRadius = 0.3f;
        /// <summary>Duration in ticks before auto-detonation.</summary>
        public ushort MineDurationTicks = 180;

        public override bool SpawnHitbox(HitboxEvent evt, CharacterState state, CharacterDefinition def, SpellResolver resolver, ulong ownerId)
        {
            bool detonated = resolver.RemoveHitbox(ownerId, hb => hb.Gravity == 0f && hb.Explosion.HasValue);
            if (!detonated)
            {
                resolver.Spawn(new Hitbox
                {
                    X = state.PX, Y = state.PY, Z = state.PZ,
                    Radius = MineRadius, Shape = HitboxShape.Sphere,
                    DurationTicks = MineDurationTicks, OwnerId = ownerId,
                    Gravity = 0,
                    Explosion = ExplosionConfig,
                });
            }
            return true;
        }
    }
}
