using Xunit;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Hitbox.RehitIntervalTicks: 0 = legacy one-hit-then-die, >0 = lingering zone
/// that pulses every N ticks and survives contact.
/// Knockback is zeroed in these fixtures so the target stays inside the zone.
/// </summary>
public class RehitZoneTests
{
    private static ServerSimulation SimWithNpc(out CharacterState npc)
    {
        var sim = TestHelpers.MakeSim();
        var player = TestHelpers.PlayerState();
        player.PY = TestHelpers.GroundPY(TestHelpers.MankiDef);
        TestHelpers.RegisterPlayer(sim, TestHelpers.MankiDef, player);

        npc = TestHelpers.NpcState(0f, 0f);
        npc.PY = TestHelpers.CombatGroundPY;
        TestHelpers.RegisterNpc(sim, TestHelpers.CombatDef, npc);
        return sim;
    }

    /// <summary>
    /// Same as SimWithNpc plus a second NPC standing inside the same zone radius.
    /// TestHelpers.RegisterNpc is hardcoded to entity 100, so the second NPC is
    /// registered directly with id 101.
    /// </summary>
    private static ServerSimulation SimWithTwoNpcs(out CharacterState npc)
    {
        var sim = SimWithNpc(out npc);

        var npc2 = TestHelpers.NpcState(1.5f, 0f);
        npc2.PY = TestHelpers.CombatGroundPY;
        sim.RegisterEntity(101, TestHelpers.CombatDef, npc2);
        return sim;
    }

    private static void Idle(ServerSimulation sim, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            sim.Tick(new() { { 1, default }, { 100, default } });
    }

    private static Hitbox Zone(CharacterState npc, float damage, ushort duration, ushort rehit) => new()
    {
        X = npc.PX, Y = npc.PY, Z = npc.PZ,
        Radius = 2f, Shape = HitboxShape.Sphere,
        EndX = npc.PX, EndY = npc.PY, EndZ = npc.PZ,
        Damage = damage,
        BaseKnockback = 0f, KnockbackGrowth = 0f, KnockbackAngle = 0,
        StunTicks = 0,
        DurationTicks = duration,
        OwnerId = 1,
        RehitIntervalTicks = rehit,
    };

    [Fact]
    public void ZeroInterval_HitsExactlyOnce()
    {
        var sim = SimWithNpc(out var npc);
        sim.Resolver.Spawn(Zone(npc, 5f, duration: 120, rehit: 0));

        Idle(sim, 90);

        Assert.Equal((ushort)5, sim.GetState(100).DamagePercent);
    }

    [Fact]
    public void Interval30_HitsOnEveryPulse()
    {
        var sim = SimWithNpc(out var npc);
        sim.Resolver.Spawn(Zone(npc, 3f, duration: 91, rehit: 30));

        Idle(sim, 91);

        // Pulses at AgeTicks 0, 30, 60, 90 => 4 hits x 3 damage
        Assert.Equal((ushort)12, sim.GetState(100).DamagePercent);
    }

    [Fact]
    public void Zone_ExpiresAfterDuration()
    {
        var sim = SimWithNpc(out var npc);
        sim.Resolver.Spawn(Zone(npc, 3f, duration: 31, rehit: 30));

        // Stop immediately after the second pulse and expiry. A longer run lets the dummy
        // fall out of the finite test arena and respawn, resetting its damage.
        Idle(sim, 31);

        Assert.Equal((ushort)6, sim.GetState(100).DamagePercent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    [Fact]
    public void Zone_HitsEveryOverlappingEntityOnTheSamePulse()
    {
        var sim = SimWithTwoNpcs(out var npc);
        sim.Resolver.Spawn(Zone(npc, 3f, duration: 91, rehit: 30));

        Idle(sim, 91);

        // Pulses at AgeTicks 0, 30, 60, 90 => 4 hits x 3 damage, for BOTH NPCs.
        // A zone that stopped scanning after its first hit would leave one at 0.
        Assert.Equal((ushort)12, sim.GetState(100).DamagePercent);
        Assert.Equal((ushort)12, sim.GetState(101).DamagePercent);
    }

    [Fact]
    public void IntervalLongerThanDuration_HitsExactlyOnce()
    {
        var sim = SimWithNpc(out var npc);
        sim.Resolver.Spawn(Zone(npc, 5f, duration: 30, rehit: 100));

        Idle(sim, 60);

        // Pulses at AgeTicks 0, then expires at 30 — long before the next pulse at 100.
        Assert.Equal((ushort)5, sim.GetState(100).DamagePercent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }
    /// <summary>
    /// A persistent one-hit capsule must not consume its target eligibility while
    /// Dash invincibility is active. Once the four invincible resolver ticks end,
    /// it accepts exactly one hit before the authored lifetime expires.
    /// </summary>
    [Fact]
    public void PersistentHitbox_DashInvincibilityDoesNotConsumeEligibility()
    {
        var resolver = new SpellResolver();
        resolver.Spawn(new Hitbox
        {
            X = 0f, Y = 0f, Z = 0f,
            EndX = 0f, EndY = 1f, EndZ = 0f,
            Radius = 0.5f,
            Shape = HitboxShape.Capsule,
            Damage = 5f,
            DurationTicks = 8,
            OwnerId = 1,
            HitsMultipleOpponents = true,
        });

        var entities = new System.Collections.Generic.List<SpellResolver.EntityData>
        {
            new() { Id = 1, Active = true, PosY = 0.5f, EndY = 0.5f, Radius = 0.3f },
            new()
            {
                Id = 2, Active = true, PosY = 0.5f, EndY = 0.5f, Radius = 0.3f,
                InvincibilityTicks = 4,
            },
        };

        for (int i = 0; i < 4; i++)
            Assert.Empty(resolver.Tick(entities));

        var target = entities[1];
        target.InvincibilityTicks = 0;
        entities[1] = target;

        Assert.Single(resolver.Tick(entities));
        for (int i = 0; i < 3; i++)
            Assert.Empty(resolver.Tick(entities));
    }


    [Fact]
    public void Interval1_HitsEveryTick()
    {
        var sim = SimWithNpc(out var npc);
        sim.Resolver.Spawn(Zone(npc, 1f, duration: 10, rehit: 1));

        Idle(sim, 40);

        // AgeTicks % 1 == 0 always => one hit per tick of the zone's 10-tick life.
        Assert.Equal((ushort)10, sim.GetState(100).DamagePercent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    /// <summary>
    /// <c>RehitIntervalTicks == DurationTicks</c> is the shape Nilus' F detonation uses
    /// (5 and 5): the age gate only ever matches at 0, so the blast resolves exactly once
    /// while still scanning every body on that pulse. Pinned separately from
    /// <see cref="IntervalLongerThanDuration_HitsExactlyOnce"/> because the boundary case
    /// (<c>AgeTicks</c> reaching the interval on the same tick it expires) is the one a
    /// reader would have to re-derive.
    /// </summary>
    [Fact]
    public void IntervalEqualToDuration_PulsesOnlyOnce_ForEveryTarget()
    {
        var sim = SimWithTwoNpcs(out var npc);
        sim.Resolver.Spawn(Zone(npc, 18f, duration: 5, rehit: 5));

        Idle(sim, 30);

        Assert.Equal((ushort)18, sim.GetState(100).DamagePercent);
        Assert.Equal((ushort)18, sim.GetState(101).DamagePercent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    /// <summary>
    /// <see cref="Hitbox.IgnoresEntities"/> must skip the body scan outright, not merely deal
    /// zero damage. Both halves matter: no <c>HitResult</c> (so no zero-magnitude
    /// ApplyKnockback forcing the victim to Idle), and no <c>Active = false</c>, so the hitbox
    /// survives contact and lives out its full <c>DurationTicks</c> instead of expiring the
    /// instant it overlaps someone.
    /// </summary>
    [Fact]
    public void IgnoresEntities_NeitherDamagesNorDiesOnContact()
    {
        var sim = SimWithNpc(out var npc);
        var hb = Zone(npc, 5f, duration: 20, rehit: 0);
        hb.IgnoresEntities = true;
        sim.Resolver.Spawn(hb);

        // The NPC is standing at the zone's centre — a normal one-hit hitbox would connect
        // and be removed on the very first tick.
        Idle(sim, 10);
        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        Assert.Equal(ActionState.Idle, sim.GetState(100).State);
        Assert.Single(sim.Resolver.GetActiveHitboxes());

        Idle(sim, 15);
        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);
        Assert.Empty(sim.Resolver.GetActiveHitboxes());
    }

    /// <summary>
    /// The flag opts out of the ENTITY scan only. Aging, expiry and explosion queueing are
    /// untouched, which is what lets Nilus' Q seed carry the rift through a body and still
    /// deliver it — the payload arrives, the seed just never touches anyone on the way.
    /// </summary>
    [Fact]
    public void IgnoresEntities_StillQueuesItsExplosionOnExpiry()
    {
        var sim = SimWithNpc(out var npc);
        var hb = Zone(npc, 5f, duration: 10, rehit: 0);
        hb.IgnoresEntities = true;
        hb.Explosion = new ProjectileExplosion
        {
            Radius = 2f,
            Damage = 7f,
            Knockback = new() { Profile = KnockbackProfile.Custom, Angle = 0, BaseKnockback = 0f, KnockbackGrowth = 0f },
            StunTicks = 0,
            DurationTicks = 2,
        };
        sim.Resolver.Spawn(hb);

        Idle(sim, 9);
        Assert.Equal((ushort)0, sim.GetState(100).DamagePercent);

        Idle(sim, 5);
        Assert.Equal((ushort)7, sim.GetState(100).DamagePercent);
    }
}
