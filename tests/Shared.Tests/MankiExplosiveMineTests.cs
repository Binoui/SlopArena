using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Tests for Manki's E — ExplosiveMineSpec.
/// Mine placement, manual detonation, auto-detonation, and per-owner isolation.
/// </summary>
public class MankiExplosiveMineTests
{
    private static readonly CharacterDefinition Def = TestHelpers.MankiDef!;

    private static (CharacterState state, ExplosiveMineSpec spec, HitboxEvent evt, SpellResolver resolver) Setup()
    {
        var spec = (ExplosiveMineSpec)Def.E;
        var evt = spec.Stages![0].HitboxEvents![0];
        var state = TestHelpers.PlayerState();
        state.PY = 1.3f; // standing height
        var resolver = new SpellResolver();
        return (state, spec, evt, resolver);
    }

    [Fact]
    public void Place_SpawnsMineHitbox()
    {
        var (state, spec, evt, resolver) = Setup();
        spec.SpawnHitbox(evt, state, Def, resolver, 1);

        var hitboxes = resolver.GetActiveHitboxes();
        Assert.Single(hitboxes);
        var mine = hitboxes[0];
        Assert.Equal(0f, mine.Gravity);
        Assert.Equal(spec.MineDurationTicks, mine.DurationTicks);
        Assert.Equal((ulong)1, mine.OwnerId);
        Assert.True(mine.Explosion.HasValue);
    }

    [Fact]
    public void Place_MineRadiusMatchesSpec()
    {
        var (state, spec, evt, resolver) = Setup();
        spec.SpawnHitbox(evt, state, Def, resolver, 1);

        var mine = resolver.GetActiveHitboxes()[0];
        Assert.Equal(spec.MineRadius, mine.Radius);
    }

    [Fact]
    public void Place_MinePositionAtFeet()
    {
        var (state, spec, evt, resolver) = Setup();
        spec.SpawnHitbox(evt, state, Def, resolver, 1);

        var mine = resolver.GetActiveHitboxes()[0];
        Assert.Equal(state.PX, mine.X);
        Assert.Equal(state.PY, mine.Y);
        Assert.Equal(state.PZ, mine.Z);
    }

    [Fact]
    public void Place_ReturnsTrue()
    {
        var (state, spec, evt, resolver) = Setup();
        bool result = spec.SpawnHitbox(evt, state, Def, resolver, 1);
        Assert.True(result);
    }

    [Fact]
    public void Detonate_RemovesExistingMine()
    {
        var (state, spec, evt, resolver) = Setup();

        bool placeResult = spec.SpawnHitbox(evt, state, Def, resolver, 1);
        Assert.True(placeResult);
        Assert.Single(resolver.GetActiveHitboxes());

        bool detonateResult = spec.SpawnHitbox(evt, state, Def, resolver, 1);
        Assert.True(detonateResult);
        Assert.Empty(resolver.GetActiveHitboxes());
    }

    [Fact]
    public void Detonate_QueuesExplosion()
    {
        var (state, spec, evt, resolver) = Setup();

        spec.SpawnHitbox(evt, state, Def, resolver, 1);
        spec.SpawnHitbox(evt, state, Def, resolver, 1);

        var explosions = resolver.DrainPendingExplosions();
        Assert.Single(explosions);
        Assert.True(explosions[0].explosion.CanHitOwner);
    }

    [Fact]
    public void AutoDetonate_AfterDuration()
    {
        var (state, spec, evt, resolver) = Setup();

        spec.SpawnHitbox(evt, state, Def, resolver, 1);
        Assert.Single(resolver.GetActiveHitboxes());

        int ticks = spec.MineDurationTicks;
        var emptyEntities = new List<SpellResolver.EntityData>();
        for (int i = 0; i < ticks; i++)
            resolver.Tick(emptyEntities);

        Assert.Empty(resolver.GetActiveHitboxes());
        var explosions = resolver.DrainPendingExplosions();
        Assert.Single(explosions);
    }

    [Fact]
    public void TwoSeparateOwners_EachGetsOwnMine()
    {
        var (state, spec, evt, resolver) = Setup();

        var state2 = TestHelpers.NpcState(x: 5f);
        state2.PY = 1.3f;

        spec.SpawnHitbox(evt, state, Def, resolver, 1);
        spec.SpawnHitbox(evt, state2, Def, resolver, 2);

        var hitboxes = resolver.GetActiveHitboxes();
        Assert.Equal(2, hitboxes.Count);

        spec.SpawnHitbox(evt, state, Def, resolver, 1);

        hitboxes = resolver.GetActiveHitboxes();
        Assert.Single(hitboxes);
        Assert.Equal((ulong)2, hitboxes[0].OwnerId);
    }

    // ══════════════════════════════════════════════════════════════════
    // ── Overclock buff + mine explosion ──
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Place_DuringOverclock_BuffsExplosionDamage()
    {
        var (state, spec, evt, resolver) = Setup();
        state.BuffActiveFlags = (byte)BuffType.Overclock;
        state.BuffRemainingTicks = 400;

        spec.SpawnHitbox(evt, state, Def, resolver, 1);

        var mine = resolver.GetActiveHitboxes()[0];
        Assert.True(mine.Explosion.HasValue);
        Assert.Equal(8f, mine.Explosion.Value.Damage);   // 5 + 3
        Assert.Equal(3f, mine.Explosion.Value.Radius);   // 2.5 + 0.5
    }

    [Fact]
    public void Place_WithoutOverclock_NormalExplosionDamage()
    {
        var (state, spec, evt, resolver) = Setup();

        spec.SpawnHitbox(evt, state, Def, resolver, 1);

        var mine = resolver.GetActiveHitboxes()[0];
        Assert.True(mine.Explosion.HasValue);
        Assert.Equal(5f, mine.Explosion.Value.Damage);   // base 5
        Assert.Equal(2.5f, mine.Explosion.Value.Radius); // base 2.5
    }
}
