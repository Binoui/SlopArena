namespace SlopArena.Shared.AI;

/// <summary>
/// A deterministic policy that reads sim state and produces a valid <see cref="InputState"/>
/// for one tick (issue #148). The reuse seam for in-game bots: a bot entity's inputs dict is
/// filled from <see cref="Decide"/> each tick, exactly like a client would fill it. Pure C#,
/// no engine types, no wall clock — a seeded <see cref="Random"/> is the only non-determinism
/// source and it is caller-supplied so the same seed reproduces the same decisions.
/// </summary>
public interface IBotPolicy
{
    /// <summary>
    /// Decide the input for <paramref name="self"/> this tick against <paramref name="target"/>.
    /// Callers feed the result into <c>ServerSimulation.Tick(Dictionary&lt;ulong, InputState&gt;)</c>.
    /// </summary>
    /// <param name="self">The bot's current sim state.</param>
    /// <param name="target">The opponent's current sim state (the bot's chosen engagement).</param>
    /// <param name="def">The bot's character definition (move specs, movement, weight).</param>
    /// <param name="rng">Seeded RNG for deterministic tie-breaks / decision jitter.</param>
    /// <param name="memory">Persistent per-entity bot state (cooldowns, swing tracking).</param>
    InputState Decide(in CharacterState self, in CharacterState target,
        CharacterDefinition def, Random rng, BotMemory memory);
}
