using System;
using System.Collections.Generic;

namespace SlopArena.Shared.Tests;

/// <summary>
/// Sparse per-tick input definition.
/// Unspecified ticks produce <c>default(InputState)</c>.
/// </summary>
public class InputSequence
{
    private readonly Dictionary<int, InputState> _inputs = new();

    /// <summary>Set the input for <paramref name="tick"/>.</summary>
    public InputSequence Set(int tick, InputState input)
    {
        _inputs[tick] = input;
        return this;
    }

    /// <summary>Convenience: press <paramref name="activeSlot"/> on <paramref name="tick"/>.</summary>
    public InputSequence Press(int tick, byte activeSlot)
        => Set(tick, new InputState { ActiveSlot = activeSlot });

    /// <summary>Get the input for <paramref name="tick"/>. Returns default if unset.</summary>
    public InputState ForTick(int tick) =>
        _inputs.TryGetValue(tick, out var v) ? v : default;
}

/// <summary>
/// A self-contained kit scenario: one entity, one input sequence, one assertion.
/// Optionally spawns an NPC dummy target for hit-confirm tests.
/// </summary>
public class KitScenario
{
    /// <summary>Display name for test output.</summary>
    public required string Name { get; init; }

    /// <summary>Character definition to spawn for the primary entity.</summary>
    public required CharacterDefinition Def { get; init; }

    /// <summary>Return the primary entity's initial CharacterState (position, facing, grounded, etc.).</summary>
    public required Func<CharacterState> Setup { get; init; }

    /// <summary>Per-tick inputs for the primary entity.</summary>
    public required InputSequence Inputs { get; init; }

    /// <summary>Assert on the primary entity's final CharacterState. Throws on failure.</summary>
    public required Action<CharacterState> Assert { get; init; }

    /// <summary>
    /// Optional. When set, spawns an NPC dummy (entity 100) with default input.
    /// Returns the NPC's initial CharacterState.
    /// </summary>
    public Func<CharacterState>? NpcSetup { get; init; }

    /// <summary>
    /// Optional. Assert on the NPC's final CharacterState. Only checked when NpcSetup is set.
    /// </summary>
    public Action<CharacterState>? NpcAssert { get; init; }

    /// <summary>NPC character definition. Defaults to Def when NpcSetup is set.</summary>
    public CharacterDefinition? NpcDef { get; init; }

    /// <summary>Total ticks to simulate. Defaults to 200.</summary>
    public int TotalTicks { get; init; } = 200;

    /// <summary>
    /// Optional arena override. Defaults to <c>TestHelpers.TestArena()</c> — override for
    /// scenarios that need custom spawn points (e.g. a void-death respawn that must land
    /// on its feet), kill height, or floor geometry.
    /// </summary>
    public ArenaDefinition? Arena { get; init; }

    /// <summary>
    /// Tick at which the golden snapshot is taken (0-based).
    /// Should point to mid-ability (hitbox active, knockback applied) for rich diffs.
    /// Defaults to TotalTicks - 1 (end state, most values settled to 0).
    /// </summary>
    public int SnapshotTick { get; init; } = -1; // -1 = use TotalTicks-1
}

/// <summary>
/// Runs a KitScenario and returns the final primary entity state
/// (and optionally the NPC state when NpcSetup is configured).
/// </summary>
public static class ScenarioRunner
{
    /// <summary>
    /// Run a self-contained kit scenario.
    /// Returns final states + snapshot states (captured at scenario.SnapshotTick).
    /// NPC assert is called internally when configured.
    /// </summary>
    public static (CharacterState Player, CharacterState? Npc,
                   CharacterState PlayerSnapshot, CharacterState? NpcSnapshot) Run(KitScenario scenario)
    {
        var arena = scenario.Arena ?? TestHelpers.TestArena();
        var sim = TestHelpers.MakeSim(arena);

        // Spawn primary entity
        var state = scenario.Setup();
        sim.RegisterEntity(1, scenario.Def, state, TestHelpers.LoadBakedData(scenario.Def));

        // Spawn NPC dummy when configured
        if (scenario.NpcSetup != null)
        {
            var npcState = scenario.NpcSetup();
            var npcDef = scenario.NpcDef ?? scenario.Def;
            sim.RegisterEntity(100, npcDef, npcState, TestHelpers.LoadBakedData(npcDef));
        }

        int totalTicks = scenario.TotalTicks > 0 ? scenario.TotalTicks : 200;
        int snapTick = scenario.SnapshotTick >= 0 ? scenario.SnapshotTick : totalTicks - 1;

        CharacterState playerSnap = default;
        CharacterState? npcSnap = null;

        for (int tick = 0; tick < totalTicks; tick++)
        {
            var inputs = new Dictionary<ulong, InputState>
            {
                { 1, scenario.Inputs.ForTick(tick) }
            };
            // NPC always gets default input
            if (scenario.NpcSetup != null)
                inputs[100] = default;
            sim.Tick(inputs);

            // Capture snapshot at the designated tick
            if (tick == snapTick)
            {
                playerSnap = sim.GetState(1);
                if (scenario.NpcSetup != null)
                    npcSnap = sim.GetState(100);
            }
        }

        var playerFinal = sim.GetState(1);
        CharacterState? npcFinal = null;

        // Run NPC assert inside Runner
        if (scenario.NpcAssert != null)
        {
            npcFinal = sim.GetState(100);
            scenario.NpcAssert(npcFinal.Value);
        }

        return (playerFinal, npcFinal, playerSnap, npcSnap);
    }
}
