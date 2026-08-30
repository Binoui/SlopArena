using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Encodings.Web;
using SlopArena.Shared;
using SlopArena.Shared.Abilities;
using Xunit;

namespace SlopArena.Shared.Tests;

internal enum FightGuyTraceInterruption
{
    NaturalCompletion,
    Hitstun,
    Death,
    Burst,
}

internal sealed class FightGuyTraceScenario
{
    public required byte ActiveSlot;
    public required bool Airborne;
    public required FightGuyTraceInterruption Interruption;

    public override string ToString()
        => $"slot={ActiveSlot}, {(Airborne ? "air" : "ground")}, {Interruption}";
}

internal sealed class FightGuyEntitySample
{
    public uint Tick;
    public ulong EntityId;
    public CharacterState State;
}

internal sealed class FightGuyHitboxSnapshot
{
    public float X, Y, Z;
    public float VX, VY, VZ;
    public float Radius;
    public ushort DurationTicks, AgeTicks;
    public HitboxShape Shape;
    public float EndX, EndY, EndZ;
    public float Damage, BaseKnockback, KnockbackGrowth;
    public sbyte KnockbackAngle;
    public ushort StunTicks;
    public ulong OwnerId;
    public bool Active;
    public float Gravity;
    public ProjectileExplosion? Explosion;
    public bool CanHitOwner, FreezesOwner;
    public ushort RehitIntervalTicks;
    public bool HitsMultipleOpponents, IgnoresEntities, TracksBone;
    public HitboxEvent SourceEvent;
    public string[]? AnimationNames;
    public byte AnimIndex, Slot;
    public bool Airborne;
    public ulong[] HitEntities = Array.Empty<ulong>();
}

internal sealed class FightGuyRemovedHitbox
{
    public uint Tick;
    public FightGuyHitboxSnapshot Hitbox = null!;
    public float X, Y, Z;
}

internal sealed class FightGuyHitSnapshot
{
    public ulong TargetEntityId, OwnerEntityId;
    public float Damage, DirX, DirZ;
    public sbyte KnockbackAngle;
    public float BaseKnockback, KnockbackGrowth;
    public ushort StunTicks;
    public bool FreezesOwner;
    public float HitX, HitY, HitZ;
    public float ImpactForce;
    public ushort HitstopTicks;
}

internal sealed class FightGuyLifecycleEvent
{
    public uint Tick;
    public ulong EntityId;
    public string Kind = "";
    public byte ActiveSlot;
    public string? InterruptionReason;
}

internal sealed class FightGuyTickTrace
{
    public uint Tick;
    public List<FightGuyEntitySample> States = new();
    public List<FightGuyHitboxSnapshot> ActiveHitboxes = new();
    public List<FightGuyHitSnapshot> Hits = new();
    public List<TimelinePresentationEvent> PresentationEvents = new();
}

internal sealed class FightGuyTrace
{
    public List<FightGuyTickTrace> Ticks = new();
    public List<FightGuyRemovedHitbox> RemovedHitboxes = new();
    public List<FightGuyLifecycleEvent> Lifecycle = new();
}

internal static class FightGuyBaseline
{
    private const string FixtureRelativePath = "tests/Shared.Tests/Fixtures/FightGuyHotfixedBaseline.json";

    public static string FixturePath
    {
        get
        {
            string repoRoot = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            return Path.Combine(repoRoot, FixtureRelativePath);
        }
    }

    public static CharacterDefinition LoadDefinition()
    {
        var definition = CharacterContentSerializer.LoadFile(FixturePath);
        definition.CookedSlots = FightGuyBaselineOperationCatalog.Slots;
        foreach (var slot in definition.CookedSlots)
        {
            int logicalSlot = slot.Ordinal switch
            {
                0 or 8 => 2,
                1 or 9 => 6,
                2 or 10 => 7,
                3 or 11 => 8,
                4 or 12 => 10,
                5 or 13 => 3,
                6 or 14 => 4,
                7 or 15 => 5,
                _ => -1,
            };
            var ability = logicalSlot >= 0
                ? definition.GetSlotAbility(logicalSlot, slot.IsAir)
                : null;
            if (ability != null)
                ability.AnimationNames = slot.Timeline.Stages.SelectMany(x => x.AnimationIds).ToArray();
        }
        return definition;
    }

    public static CharacterDefinition LoadCandidateDefinition()
        => BuiltInContentResolver.Resolve(CharacterClass.FightGuy).Definition;

    public static FightGuyTrace RunTrace(
        CharacterDefinition definition, FightGuyTraceScenario scenario)
    {
        var sim = TestHelpers.MakeSim();
        var state = TestHelpers.PlayerState() with
        {
            PY = scenario.Airborne ? 100f : TestHelpers.GroundPY(definition),
            IsGrounded = !scenario.Airborne,
            JumpsLeft = definition.Movement.MaxJumps,
            VY = 0f,
        };
        sim.RegisterEntity(1, definition, state, TestHelpers.LoadBakedData(definition));

        if (scenario.Interruption == FightGuyTraceInterruption.Hitstun)
        {
            var npc = TestHelpers.NpcState(100f, 100f) with
            {
                PY = TestHelpers.GroundPY(definition),
                IsGrounded = true,
            };
            sim.RegisterEntity(100, definition, npc, TestHelpers.LoadBakedData(definition));
        }

        var trace = new FightGuyTrace();
        var previousStates = sim.GetAllStates()
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var previousAbilities = sim.GetAllStates()
            .Keys.ToDictionary(id => id, id => sim.GetActiveAbility(id));
        var lifecycleRecorded = new HashSet<ulong>();
        uint currentTick = 0;

        RecordStates(trace, 0, sim);
        void OnHitboxRemoved(Hitbox hitbox, float x, float y, float z)
        {
            trace.RemovedHitboxes.Add(new FightGuyRemovedHitbox
            {
                Tick = currentTick,
                Hitbox = Snapshot(hitbox),
                X = x,
                Y = y,
                Z = z,
            });
        }

        sim.Resolver.OnHitboxRemoved += OnHitboxRemoved;
        try
        {
            // Window sized so every populated slot's natural completion AND its
            // hitbox/projectile removal land inside the trace. Ki Shot's aim-hold
            // (release-to-fire) pushes its projectile removal to ~tick 107.
            for (int tick = 0; tick < 120; tick++)
            {
                currentTick = (uint)(tick + 1);

                if (scenario.Interruption == FightGuyTraceInterruption.Death && tick == 1)
                {
                    var doomed = sim.GetState(1);
                    doomed.PX = 1000f;
                    doomed.PY = -100f;
                    sim.SetState(1, doomed);
                }

                if (scenario.Interruption == FightGuyTraceInterruption.Hitstun && tick == 1)
                {
                    var target = sim.GetState(1);
                    sim.Resolver.Spawn(new Hitbox
                    {
                        X = target.PX,
                        Y = target.PY,
                        Z = target.PZ,
                        Radius = 2f,
                        Shape = HitboxShape.Sphere,
                        EndX = target.PX,
                        EndY = target.PY,
                        EndZ = target.PZ,
                        Damage = 7f,
                        BaseKnockback = 3f,
                        KnockbackGrowth = 2f,
                        KnockbackAngle = 30,
                        StunTicks = 20,
                        DurationTicks = 1,
                        OwnerId = 100,
                        CanHitOwner = false,
                        FreezesOwner = false,
                    });
                }

                var input = tick == 0
                    ? new InputState { ActiveSlot = scenario.ActiveSlot }
                    : default;
                if (scenario.Interruption == FightGuyTraceInterruption.Burst && tick == 1)
                    input.Burst = true;
                var inputs = new Dictionary<ulong, InputState> { [1] = input };
                if (scenario.Interruption == FightGuyTraceInterruption.Hitstun)
                    inputs[100] = default;

                var statesBeforeTick = previousStates.ToDictionary(pair => pair.Key, pair => pair.Value);
                var abilitiesBeforeTick = previousAbilities
                    .ToDictionary(pair => pair.Key, pair => (ServerAbility?)pair.Value);
                sim.Tick(inputs);

                RecordLifecycle(trace, sim, statesBeforeTick, abilitiesBeforeTick, lifecycleRecorded);
                var tickTrace = RecordStates(trace, currentTick, sim);
                tickTrace.PresentationEvents = sim.GetPresentationEvents(clear: true).ToList();
                tickTrace.ActiveHitboxes = sim.Resolver.GetActiveHitboxes()
                    .Select(Snapshot)
                    .OrderBy(h => h.OwnerId)
                    .ThenBy(h => h.AgeTicks)
                    .ThenBy(h => h.X)
                    .ThenBy(h => h.Y)
                    .ThenBy(h => h.Z)
                    .ToList();
                tickTrace.Hits = sim.LastTickHits.Select(Snapshot).ToList();

                previousStates = sim.GetAllStates()
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
                previousAbilities = sim.GetAllStates()
                    .Keys.ToDictionary(id => id, id => (ServerAbility?)sim.GetActiveAbility(id));
            }
        }
        finally
        {
            sim.Resolver.OnHitboxRemoved -= OnHitboxRemoved;
        }

        if (sim.GetActiveAbility(1) != null)
            throw new InvalidOperationException($"Covered scenario did not end: {scenario}");
        if (scenario.Interruption != FightGuyTraceInterruption.NaturalCompletion
            && !trace.Lifecycle.Any(e => e.EntityId == 1
                && e.Kind == "Interrupted"
                && e.InterruptionReason == scenario.Interruption.ToString()))
        {
            throw new InvalidOperationException($"Covered scenario lacked {scenario.Interruption}: {scenario}");
        }

        return trace;
    }

    private static FightGuyTickTrace RecordStates(
        FightGuyTrace trace, uint tick, ServerSimulation sim)
    {
        var sample = new FightGuyTickTrace { Tick = tick };
        foreach (var pair in sim.GetAllStates().OrderBy(pair => pair.Key))
        {
            sample.States.Add(new FightGuyEntitySample
            {
                Tick = tick,
                EntityId = pair.Key,
                State = pair.Value,
            });
        }
        trace.Ticks.Add(sample);
        return sample;
    }

    private static void RecordLifecycle(
        FightGuyTrace trace,
        ServerSimulation sim,
        Dictionary<ulong, CharacterState> statesBefore,
        Dictionary<ulong, ServerAbility?> abilitiesBefore,
        HashSet<ulong> lifecycleRecorded)
    {
        foreach (var pair in sim.GetAllStates().OrderBy(pair => pair.Key))
        {
            ulong id = pair.Key;
            var current = pair.Value;
            statesBefore.TryGetValue(id, out var priorState);
            abilitiesBefore.TryGetValue(id, out var priorAbility);
            var currentAbility = sim.GetActiveAbility(id);

            if (priorAbility == null && currentAbility != null)
            {
                trace.Lifecycle.Add(new FightGuyLifecycleEvent
                {
                    Tick = (uint)trace.Ticks.Count,
                    EntityId = id,
                    Kind = "Started",
                    ActiveSlot = (byte)(currentAbility!.Slot + 1),
                });
            }

            bool death = priorAbility != null && current.Deaths > priorState.Deaths;
            if (priorAbility == null || lifecycleRecorded.Contains(id)) continue;

            if (death)
            {
                AddInterrupted(trace, id, priorAbility!, FightGuyTraceInterruption.Death);
                lifecycleRecorded.Add(id);
                continue;
            }

            if (currentAbility != null) continue;

            if (current.BurstRecoveryTicks > 0 || priorState.BurstRecoveryTicks > 0)
                AddInterrupted(trace, id, priorAbility!, FightGuyTraceInterruption.Burst);
            else if (current.State == ActionState.Hitstun || current.HitstunTicks > 0
                || current.HitstopTicks > 0 || priorState.State == ActionState.Hitstun
                || priorState.HitstunTicks > 0 || priorState.HitstopTicks > 0)
                AddInterrupted(trace, id, priorAbility!, FightGuyTraceInterruption.Hitstun);
            else if (current.State == ActionState.Idle && current.AttackSlot == 0)
                trace.Lifecycle.Add(new FightGuyLifecycleEvent
                {
                    Tick = (uint)trace.Ticks.Count,
                    EntityId = id,
                    Kind = "Completed",
                    ActiveSlot = (byte)(priorAbility!.Slot + 1),
                    InterruptionReason = FightGuyTraceInterruption.NaturalCompletion.ToString(),
                });
            else
                throw new InvalidOperationException(
                    $"Unclassified FightGuy ability transition for entity {id} at tick {trace.Ticks.Count}.");

            lifecycleRecorded.Add(id);
        }
    }

    private static void AddInterrupted(
        FightGuyTrace trace, ulong entityId, ServerAbility ability,
        FightGuyTraceInterruption reason)
    {
        trace.Lifecycle.Add(new FightGuyLifecycleEvent
        {
            Tick = (uint)trace.Ticks.Count,
            EntityId = entityId,
            Kind = "Interrupted",
            ActiveSlot = (byte)(ability.Slot + 1),
            InterruptionReason = reason.ToString(),
        });
    }

    private static FightGuyHitboxSnapshot Snapshot(Hitbox hitbox)
        => new()
        {
            X = hitbox.X, Y = hitbox.Y, Z = hitbox.Z,
            VX = hitbox.VX, VY = hitbox.VY, VZ = hitbox.VZ,
            Radius = hitbox.Radius,
            DurationTicks = hitbox.DurationTicks,
            AgeTicks = hitbox.AgeTicks,
            Shape = hitbox.Shape,
            EndX = hitbox.EndX, EndY = hitbox.EndY, EndZ = hitbox.EndZ,
            Damage = hitbox.Damage,
            BaseKnockback = hitbox.BaseKnockback,
            KnockbackGrowth = hitbox.KnockbackGrowth,
            KnockbackAngle = hitbox.KnockbackAngle,
            StunTicks = hitbox.StunTicks,
            OwnerId = hitbox.OwnerId,
            Active = hitbox.Active,
            Gravity = hitbox.Gravity,
            Explosion = hitbox.Explosion,
            CanHitOwner = hitbox.CanHitOwner,
            FreezesOwner = hitbox.FreezesOwner,
            RehitIntervalTicks = hitbox.RehitIntervalTicks,
            HitsMultipleOpponents = hitbox.HitsMultipleOpponents,
            IgnoresEntities = hitbox.IgnoresEntities,
            TracksBone = hitbox.TracksBone,
            SourceEvent = hitbox.SourceEvent,
            AnimationNames = hitbox.AnimationNames?.ToArray(),
            AnimIndex = hitbox.AnimIndex,
            Slot = hitbox.Slot,
            Airborne = hitbox.Airborne,
            HitEntities = hitbox.HitEntities?.OrderBy(id => id).ToArray() ?? Array.Empty<ulong>(),
        };

    private static FightGuyHitSnapshot Snapshot(SpellResolver.HitResult hit)
        => new()
        {
            TargetEntityId = hit.TargetEntityId,
            OwnerEntityId = hit.OwnerEntityId,
            Damage = hit.Damage,
            DirX = hit.DirX,
            DirZ = hit.DirZ,
            KnockbackAngle = hit.KnockbackAngle,
            BaseKnockback = hit.BaseKnockback,
            KnockbackGrowth = hit.KnockbackGrowth,
            StunTicks = hit.StunTicks,
            FreezesOwner = hit.FreezesOwner,
            HitX = hit.HitX,
            HitY = hit.HitY,
            HitZ = hit.HitZ,
            ImpactForce = hit.ImpactForce,
            HitstopTicks = hit.HitstopTicks,
        };

    public static string SerializeTrace(FightGuyTrace trace)
        => JsonSerializer.Serialize(trace, new JsonSerializerOptions
        {
            IncludeFields = true,
            Encoder = JavaScriptEncoder.Default,
            WriteIndented = false,
        });
}

public sealed class FightGuyBaselineHarnessTests
{
    private static readonly byte[] PopulatedSlots = { 3, 4, 5, 6, 7, 8, 9, 11 };

    [Fact]
    public void Fixture_LoadsAndCookedCandidateResolvesFromBuiltInCatalog()
    {
        var baseline = FightGuyBaseline.LoadDefinition();
        var candidate = FightGuyBaseline.LoadCandidateDefinition();

        Assert.Equal(CharacterClass.FightGuy, baseline.Class);
        Assert.Equal(CharacterClass.FightGuy, candidate.Class);
        Assert.Same(baseline.E, baseline.AirE);
        Assert.Same(baseline.R, baseline.AirR);
        Assert.Same(baseline.F, baseline.AirF);
        Assert.Same(baseline.A, baseline.AirA);
        Assert.NotNull(candidate.CookedSlots);
        Assert.Equal("", candidate.BakedDataPath);
        Assert.Equal("Characters/FightGuy", candidate.ModelResourcePath);
    }

    [Fact]
    public void Fixture_AirAliasesAreSharedAndNormalsAreDistinct()
    {
        var definition = FightGuyBaseline.LoadDefinition();

        Assert.Same(definition.E, definition.AirE);
        Assert.Same(definition.R, definition.AirR);
        Assert.Same(definition.F, definition.AirF);
        Assert.Same(definition.A, definition.AirA);
        Assert.NotSame(definition.Slot1, definition.AirSlot1);
        Assert.NotSame(definition.Slot2, definition.AirSlot2);
        Assert.NotSame(definition.Slot3, definition.AirSlot3);
        Assert.NotSame(definition.Slot4, definition.AirSlot4);
    }

    [Fact]
    public void EmptyCanonicalSlotsAreRejectedOnGroundAndInAir()
    {
        var definition = FightGuyBaseline.LoadDefinition();
        foreach (byte activeSlot in new byte[] { 1, 2, 10 })
        {
            Assert.Null(definition.GetSlotAbility(activeSlot - 1, false));
            Assert.Null(definition.GetSlotAbility(activeSlot - 1, true));

            foreach (bool airborne in new[] { false, true })
            {
                var sim = TestHelpers.MakeSim();
                var state = TestHelpers.PlayerState() with
                {
                    PY = airborne ? 100f : TestHelpers.GroundPY(definition),
                    IsGrounded = !airborne,
                };
                sim.RegisterEntity(1, definition, state, TestHelpers.LoadBakedData(definition));
                sim.Tick(new Dictionary<ulong, InputState>
                {
                    [1] = new InputState { ActiveSlot = activeSlot },
                });
                Assert.Null(sim.GetActiveAbility(1));
                Assert.Equal((byte)0, sim.GetState(1).AttackSlot);
            }
        }
    }

    [Fact]
    public void NaturalCompletion_CoversEveryGroundAndAirSlot()
    {
        var definition = FightGuyBaseline.LoadDefinition();
        foreach (byte activeSlot in PopulatedSlots)
        foreach (bool airborne in new[] { false, true })
        {
            var scenario = new FightGuyTraceScenario
            {
                ActiveSlot = activeSlot,
                Airborne = airborne,
                Interruption = FightGuyTraceInterruption.NaturalCompletion,
            };
            var trace = FightGuyBaseline.RunTrace(definition, scenario);
            AssertNaturalTrace(trace, definition, scenario);
        }
    }

    [Fact]
    public void InterruptionMatrices_CoverHitstunDeathAndBurstForEverySlot()
    {
        var definition = FightGuyBaseline.LoadDefinition();
        foreach (FightGuyTraceInterruption interruption in new[]
            {
                FightGuyTraceInterruption.Hitstun,
                FightGuyTraceInterruption.Death,
                FightGuyTraceInterruption.Burst,
            })
        foreach (byte activeSlot in PopulatedSlots)
        foreach (bool airborne in new[] { false, true })
        {
            var scenario = new FightGuyTraceScenario
            {
                ActiveSlot = activeSlot,
                Airborne = airborne,
                Interruption = interruption,
            };
            var trace = FightGuyBaseline.RunTrace(definition, scenario);
            var interruptionEvent = Assert.Single(trace.Lifecycle,
                e => e.EntityId == 1 && e.Kind == "Interrupted");
            Assert.Equal(activeSlot, interruptionEvent.ActiveSlot);
            Assert.Equal(interruption.ToString(), interruptionEvent.InterruptionReason);

            if (interruption == FightGuyTraceInterruption.Hitstun)
            {
                var hit = Assert.Single(trace.Ticks.SelectMany(t => t.Hits),
                    h => h.OwnerEntityId == 100 && h.TargetEntityId == 1);
                Assert.Equal(7f, hit.Damage);
                Assert.Equal(3f, hit.BaseKnockback);
                Assert.Equal(2f, hit.KnockbackGrowth);
                Assert.Equal((sbyte)30, hit.KnockbackAngle);
                Assert.Equal((ushort)20, hit.StunTicks);
                Assert.False(hit.FreezesOwner);
                Assert.True(hit.DirX != 0f || hit.DirZ != 0f);
                Assert.True(hit.ImpactForce > 0f);
                Assert.True(hit.HitstopTicks > 0);
                var hitTick = trace.Ticks.Single(t => t.Hits.Contains(hit));
                var targetState = hitTick.States.Single(s => s.EntityId == 1).State;
                Assert.Equal(MathF.Atan2(-hit.DirX, -hit.DirZ), targetState.FacingYaw);
                Assert.Contains(trace.RemovedHitboxes, h => h.Hitbox.OwnerId == 100);
            }
            else if (interruption == FightGuyTraceInterruption.Death)
            {
                var final = trace.Ticks[^1].States.Single(s => s.EntityId == 1).State;
                Assert.Equal((byte)1, final.Deaths);
                Assert.Equal(0f, final.PX);
                Assert.Equal(TestHelpers.GroundPY(definition), final.PY);
                Assert.Equal(0f, final.PZ);
            }
            else
            {
                Assert.Contains(trace.Ticks, t => t.States.Any(s => s.EntityId == 1
                    && s.State.BurstRecoveryTicks > 0));
            }
        }
    }

    [Fact]
    public void Traces_AreByteIdenticalBetweenBaselineAndCookedCandidate()
    {
        var baselineDefinition = FightGuyBaseline.LoadDefinition();
        var candidateDefinition = FightGuyBaseline.LoadCandidateDefinition();
        var scenarios = PopulatedSlots.SelectMany(slot => new[] { false, true }
            .SelectMany(airborne => Enum.GetValues<FightGuyTraceInterruption>()
                .Select(interruption => new FightGuyTraceScenario
                {
                    ActiveSlot = slot,
                    Airborne = airborne,
                    Interruption = interruption,
                }))).ToArray();

        foreach (var scenario in scenarios)
        {
            var baselineTrace = FightGuyBaseline.RunTrace(baselineDefinition, scenario);
            var candidateTrace = FightGuyBaseline.RunTrace(candidateDefinition, scenario);
            string baseline = FightGuyBaseline.SerializeTrace(baselineTrace);
            string candidate = FightGuyBaseline.SerializeTrace(candidateTrace);
            if (baseline != candidate)
            {
                int tick = Enumerable.Range(0, Math.Min(baselineTrace.Ticks.Count, candidateTrace.Ticks.Count))
                    .First(i => baselineTrace.Ticks[i].ActiveHitboxes.Count != candidateTrace.Ticks[i].ActiveHitboxes.Count ||
                                (baselineTrace.Ticks[i].ActiveHitboxes.Count > 0 && candidateTrace.Ticks[i].ActiveHitboxes.Count > 0 &&
                                 (baselineTrace.Ticks[i].ActiveHitboxes[0].X != candidateTrace.Ticks[i].ActiveHitboxes[0].X ||
                                  baselineTrace.Ticks[i].ActiveHitboxes[0].Y != candidateTrace.Ticks[i].ActiveHitboxes[0].Y)));
                var b = baselineTrace.Ticks[tick];
                var c = candidateTrace.Ticks[tick];
                var bs = b.States.First(x => x.EntityId == 1).State;
                var cs = c.States.First(x => x.EntityId == 1).State;
                var bh = b.ActiveHitboxes.FirstOrDefault();
                var ch = c.ActiveHitboxes.FirstOrDefault();
                throw new Xunit.Sdk.XunitException($"{scenario} firstTick={tick} baselineState=(elapsed={bs.AttackElapsedTicks},anim={bs.AnimIndex},slot={bs.AttackSlot}) candidateState=(elapsed={cs.AttackElapsedTicks},anim={cs.AnimIndex},slot={cs.AttackSlot}) baselineHitbox={(bh == null ? "none" : $"({bh.X},{bh.Y},{bh.Z}) anim={bh.AnimIndex} slot={bh.Slot} names={string.Join(",", bh.AnimationNames ?? Array.Empty<string>())}")} candidateHitbox={(ch == null ? "none" : $"({ch.X},{ch.Y},{ch.Z}) anim={ch.AnimIndex} slot={ch.Slot} names={string.Join(",", ch.AnimationNames ?? Array.Empty<string>())}")}");
            }
        }
    }

    [Fact]
    public void TraceComparator_DetectsSlot1DurationMutation()
    {
        var baselineDefinition = FightGuyBaseline.LoadDefinition();
        var candidateDefinition = FightGuyBaseline.LoadDefinition();
        candidateDefinition.Movement.Gravity += 1f;
        var scenario = new FightGuyTraceScenario
        {
            ActiveSlot = 3,
            Airborne = true,
            Interruption = FightGuyTraceInterruption.NaturalCompletion,
        };

        string baseline = FightGuyBaseline.SerializeTrace(
            FightGuyBaseline.RunTrace(baselineDefinition, scenario));
        string candidate = FightGuyBaseline.SerializeTrace(
            FightGuyBaseline.RunTrace(candidateDefinition, scenario));
        Assert.NotEqual(baseline, candidate);
    }

    private static void AssertNaturalTrace(
        FightGuyTrace trace, CharacterDefinition definition, FightGuyTraceScenario scenario)
    {
        Assert.Equal(121, trace.Ticks.Count);
        Assert.Equal(121, trace.Ticks.Count(t => t.States.Any(s => s.EntityId == 1)));
        Assert.Contains(trace.Ticks, t => t.ActiveHitboxes.Count > 0);
        Assert.Contains(trace.RemovedHitboxes, h => h.Hitbox.OwnerId == 1);

        var started = Assert.Single(trace.Lifecycle,
            e => e.EntityId == 1 && e.Kind == "Started");
        Assert.Equal(scenario.ActiveSlot, started.ActiveSlot);
        var completed = Assert.Single(trace.Lifecycle,
            e => e.EntityId == 1 && e.Kind == "Completed");
        Assert.Equal(scenario.ActiveSlot, completed.ActiveSlot);
        Assert.Equal("NaturalCompletion", completed.InterruptionReason);

        var completionState = trace.Ticks[(int)completed.Tick].States
            .Single(s => s.EntityId == 1).State;
        Assert.Equal(ActionState.Idle, completionState.State);
        Assert.Equal((byte)0, completionState.AttackSlot);
        var spec = definition.GetSlotAbility(scenario.ActiveSlot - 1, scenario.Airborne)!;
        Assert.Equal(spec.CooldownTicks, completionState.GetCooldown(scenario.ActiveSlot));
        if (scenario.ActiveSlot == 5)
        {
            var presentation = Assert.Single(trace.Ticks.SelectMany(t => t.PresentationEvents));
            Assert.Equal(new PresentationEventKey(1, 1, scenario.Airborne ? 24 : 10), presentation.Key);
            Assert.Equal("presentation.cyclone-kick.start", presentation.PresentationId);
        }
    }
}
