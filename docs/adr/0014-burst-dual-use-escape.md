# ADR-0014: Burst — Dual-Use Escape and Combo Extender

**Status:** Proposed — 2026-08-07
**Deciders:** @Binoui

## Context

The macro read layer: something that turns every string into "is their out up?" without Smash-style perpendicular DI. Started as the WoW-PvP trinket idea (CC-break on a long cooldown, baited for a big punish); Guilty Gear Burst is the direct inspiration. Design requirement from discussion: the same tool must also be an **offensive** option — a single-purpose escape gets hoarded and feels dead. Precedents: GG Burst (defensive, baitable, gauge visible to both), DBFZ Sparking and GG Roman Cancel (dual-use — exactly the shape wanted).

## Decision

**Burst: one tool, one long per-entity cooldown, two uses.**

- **Defensive (get-out-of-jail):** usable during Hitstun or knockback drift — and during Hitstop, which is the decision window (ADR-0012). Clears hitstun + knockback, grants brief invulnerability during startup, pushes the attacker with a small fixed knockback to create space (no hitstun on the attacker — it shoves, it doesn't start a punish). The user then enters **Burst Recovery**: cannot act ~20–30 ticks, no invulnerability — the punish window when baited.
- **Offensive (combo extender):** usable during your own attack Duration Lock (`AnimLockTicks`). Cancels the lock (ability ends) and spawns a forward hitbox with **fixed knockback** — constant launch, zero damage scaling (growth = 0), so it resets position but can never kill. Enables an immediate follow-up ability. The defensive cost: your escape is now down.
- **Cooldown:** long, per-entity, **persists through KO/respawn — death never resets it.** Spending it offensively stays a commitment across stocks: a freshly respawned opponent with a live burst (respawn invincibility is 60 ticks, `ServerSimulation.cs:26`) can pressure the spent player while their out is down. Start at ~60 s, tune so a 3-stock match sees roughly 2–4 uses.
- **Readability is the balance:** startup telegraph (~8–12 ticks, character flash), visible Burst Recovery after, and the cooldown shown on the HUD for **both** players (the GG burst gauge). "Burst bait" = attacker fakes the commit, defender wastes the out, attacker punishes the recovery.
- **Input:** a dedicated button — new input flag (pattern exists: `ClientInputPacket` bit fields); physical key C, free since the crouch mapping is deprecated. Wire format change.
- **LMB chain allowed:** burst can cancel a chain stage; the chain resets to stage 1 after (LMB1 LMB2 Burst LMB1 LMB2 LMB3 is legal — spending the escape to extend a low-value string is the commitment cost).
- **Fixed knockback start values (tune in spec):** magnitude ~10 m/s, angle ~20° up, small stun (~6–8 ticks) — a reset that permits a near-true follow-up; zero damage scaling so it never kills.

## Considered Options

- **Single-use trinket (WoW-style escape only)** — rejected by the design requirement: hoarded, feels dead.
- **Dodge meter (Multiversus / Brawlhalla)** — a passive resource escape with no offensive side and a weaker read layer: the attacker has nothing to bait beyond timing.
- **Parry as the universal tool** — rejected: already character-specific (Kistu counter); a universal parry flattens the roster.

## Consequences

- New wire fields (input bit + cooldown for HUD).
- Hitstop synergy: the freeze is the escape decision window.
- Burst is the explicit exception to Model-A hitstun — the only way to act inside the no-input lock.
- Must NOT reset on respawn, or death dodges the cost and the "abuse the spent burst" window collapses.
- New tuning axis and HUD element.
