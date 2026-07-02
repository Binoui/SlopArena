using Xunit;

namespace SlopArena.Shared.Tests;

public class SpellResolverTests
{
    private static SpellResolver.EntityData MakeEntity(ulong id, float x, float y, float z, float radius = 0.5f)
    {
        return new SpellResolver.EntityData
        {
            Id = id,
            PosX = x, PosY = y, PosZ = z,
            Radius = radius,
            Shape = HitboxShape.Sphere,
            EndX = x, EndY = y, EndZ = z,
            Active = true,
        };
    }

    private static Hitbox MakeHitbox(float x, float y, float z, float radius = 0.5f, ulong ownerId = 1)
    {
        return new Hitbox
        {
            X = x, Y = y, Z = z,
            Radius = radius,
            Shape = HitboxShape.Sphere,
            EndX = x, EndY = y, EndZ = z,
            Damage = 10,
            BaseKnockback = 25f,
            KnockbackGrowth = 25f,
            KnockbackUpward = 20,
            StunTicks = 10,
            DurationTicks = 10,
            OwnerId = ownerId,
            Active = true,
            CanHitOwner = false,
        };
    }

    // ── Sphere-sphere collision ──

    [Fact]
    public void Tick_SphereTouchingEntity_ReturnsHit()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0));
        var entities = new List<SpellResolver.EntityData> { MakeEntity(2, 0.6f, 0, 0) };

        var hits = resolver.Tick(entities);

        Assert.Single(hits);
        Assert.Equal(2UL, hits[0].TargetEntityId);
        Assert.Equal(1UL, hits[0].OwnerEntityId);
        Assert.Equal(10, hits[0].Damage);
    }

    [Fact]
    public void Tick_SphereFarFromEntity_NoHit()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0));
        var entities = new List<SpellResolver.EntityData> { MakeEntity(2, 10, 0, 0) };

        var hits = resolver.Tick(entities);

        Assert.Empty(hits);
    }

    // ── Capsule-sphere collision ──

    [Fact]
    public void Tick_CapsuleHitboxVsSphereEntity_ReturnsHit()
    {
        var resolver = new SpellResolver();
        var capsule = MakeHitbox(0, 0, 0);
        capsule.Shape = HitboxShape.Capsule;
        capsule.EndX = 2; capsule.EndY = 0; capsule.EndZ = 0;
        capsule.Radius = 0.3f;
        resolver.Spawn(capsule);

        // Entity near the capsule segment's midpoint
        var entities = new List<SpellResolver.EntityData> { MakeEntity(2, 1, 0, 0.5f, 0.5f) };

        var hits = resolver.Tick(entities);

        Assert.Single(hits);
        Assert.Equal(2UL, hits[0].TargetEntityId);
    }

    // ── CanHitOwner ──

    [Fact]
    public void Tick_CanHitOwnerFalse_SkipsOwner()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0, ownerId: 1));
        var entities = new List<SpellResolver.EntityData> { MakeEntity(1, 0.4f, 0, 0) }; // same ID as owner

        var hits = resolver.Tick(entities);

        Assert.Empty(hits);
    }

    [Fact]
    public void Tick_CanHitOwnerTrue_HitsOwner()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, 0, 0, ownerId: 1);
        hb.CanHitOwner = true;
        resolver.Spawn(hb);
        var entities = new List<SpellResolver.EntityData> { MakeEntity(1, 0.4f, 0, 0) };

        var hits = resolver.Tick(entities);

        Assert.Single(hits);
    }

    // ── One hit per entity per tick ──

    [Fact]
    public void Tick_TwoHitboxesSameEntity_OneHit()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0));
        resolver.Spawn(MakeHitbox(0.1f, 0, 0)); // second overlapping
        var entities = new List<SpellResolver.EntityData> { MakeEntity(2, 0.5f, 0, 0) };

        var hits = resolver.Tick(entities);

        Assert.Single(hits); // only first hitbox connects
    }

    [Fact]
    public void Tick_TwoHitboxesTwoEntities_BothHit()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(MakeHitbox(0, 0, 0, radius: 5f));
        resolver.Spawn(MakeHitbox(0, 0, 1, radius: 5f));
        var entities = new List<SpellResolver.EntityData>
        {
            MakeEntity(2, 1, 0, 0),
            MakeEntity(3, 0, 0, 1),
        };

        var hits = resolver.Tick(entities);

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.TargetEntityId == 2);
        Assert.Contains(hits, h => h.TargetEntityId == 3);
    }
    [Fact]
    public void Tick_ProjectileExpiresWithExplosion_PendingExplosion()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(10, 5, 0);
        hb.DurationTicks = 30;
        hb.Explosion = new ProjectileExplosion
        {
            Radius = 3, Damage = 25, BaseKnockback = 40f, KnockbackGrowth = 40f,
            KnockbackUpward = 30, StunTicks = 15, DurationTicks = 5,
        };
        resolver.Spawn(hb);

        // Tick until expiry (no entities to collide with)
        var entities = new List<SpellResolver.EntityData>();
        for (int i = 0; i < 30; i++)
            resolver.Tick(entities);

        var explosions = resolver.DrainPendingExplosions();
        Assert.Single(explosions);
        // Position should be near the last position (pre-deactivation)
        Assert.Equal(10, explosions[0].x, 1);
        Assert.Equal(5, explosions[0].y, 1);
    }

    // ── Gravity on projectile ──

    [Fact]
    public void Tick_GravityOnProjectile_VYDecreases()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, 10, 0);
        hb.VY = 5f;
        hb.Gravity = 20f; // m/s²
        hb.DurationTicks = 60;
        resolver.Spawn(hb);
        var entities = new List<SpellResolver.EntityData>();

        resolver.Tick(entities);

        // After one tick (1/60s): VY = 5 - 20 * (1/60) ≈ 4.667
        float expectedVy = 5f - (20f * (1f / 60f));
        var hitboxes = resolver.GetActiveHitboxes();
        var projectile = Assert.Single(hitboxes);
        Assert.Equal(expectedVy, projectile.VY, 4);
    }

    // ── CheckGroundCollision ──

    [Fact]
    public void CheckGroundCollision_ProjectileBelowGround_QueuesExplosion()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, -1, 0); // below ground
        hb.Gravity = 20f;
        hb.Explosion = new ProjectileExplosion { Radius = 2, Damage = 15, BaseKnockback = 30f, KnockbackGrowth = 30f, KnockbackUpward = 20, StunTicks = 10, DurationTicks = 5 };
        resolver.Spawn(hb);

        var arena = TestHelpers.TestArena();
        resolver.CheckGroundCollision(arena);

        var explosions = resolver.DrainPendingExplosions();
        Assert.Single(explosions);
        Assert.Equal(0, explosions[0].y, 4); // explosion at floor Y
    }

    [Fact]
    public void CheckGroundCollision_ProjectileAboveGround_NoExplosion()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, 5, 0); // above ground
        hb.Gravity = 20f;
        hb.Explosion = new ProjectileExplosion
        {
            Radius = 2, Damage = 15, BaseKnockback = 30f, KnockbackGrowth = 30f,
            KnockbackUpward = 20, StunTicks = 10, DurationTicks = 5,
        };
        resolver.Spawn(hb);

        resolver.Spawn(hb);
        var arena = TestHelpers.TestArena();
        resolver.CheckGroundCollision(arena);
    }

    [Fact]
    public void CheckGroundCollision_NoGravity_NoExplosion()
    {
        var resolver = new SpellResolver();
        var hb = MakeHitbox(0, -1, 0); // below ground
        hb.Gravity = 0;                 // zero gravity = melee hitbox, not a projectile
        hb.Explosion = new ProjectileExplosion
        {
            Radius = 2, Damage = 15, BaseKnockback = 30f, KnockbackGrowth = 30f,
            KnockbackUpward = 20, StunTicks = 10, DurationTicks = 5,
        };
        resolver.Spawn(hb);
        var arena = TestHelpers.TestArena();
        resolver.CheckGroundCollision(arena);

        Assert.Empty(resolver.DrainPendingExplosions());
    }
}
