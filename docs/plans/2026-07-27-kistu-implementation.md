# Kistu — Implementation Plan (code only, no art/anim/model)

> Character design: `docs/characters/kistu.md` (Kistu, The Kitsune Blade — agile katana spacing duelist, launch→juggle payoff).
> This plan covers **code only**: Shared sim registration, all 8 abilities, the two pieces of genuinely new infra, tests, and the *placeholder* client config that makes Kistu playable without art. Art/anim/model/VFX are explicitly deferred.
> All file:line refs verified against the current tree (2026-07-27), not the docs. **`docs/characters/adding-a-new-character.md` and `character-import-checklist.md` are STALE** (they describe an obsolete flat `AttackStage` and a numeric `AbilityTypeId` factory) — author against `AttackData.cs` / `AbilitySpec.cs` / `KnockbackProfile.cs` and the `(CharacterClass, slot, airborne)` factory.

---

## 1. Scope

**In scope (code):**
- New `CharacterClass.Kistu` + registry entry + `KistuData.cs` (8 `AbilitySpec`s).
- 8 abilities wired through `AbilityFactory.CreateKistuAbility`.
- Two new infra pieces: **R charge-stock** (`CharacterState` field + gate/consume/refund) and **Q counter** (`ServerAbility.TryCounter` virtual + target-side interception in `ResolveHits`).
- xUnit tests (golden scenarios for standard abilities, hand-written units for novel infra).
- **Placeholder** client config (reuse FightGuy prefab, empty baked data) so the kit is selectable and every ability fires with a T-pose stand-in.

**Out of scope (deferred art/anim):** real model prefab, `Kistu_AnimConfig.asset`, baked skeleton `.bin`, VFX/foxfire trails, HUD/ability icons, weapon config, and final balance numbers (placeholders only, tuned later against `character-kit-design-principles.md` + existing Manki/FightGuy values).

---

## 2. Architecture grounding (verified)

**Dispatch chain:** `input.ActiveSlot (1-6)` → `ServerSimulation.PreTickAbilities` (`ServerSimulation.cs:327`) → `AbilityFactory.CreateServer(def.Class, slot-1, airborne)` (`AbilityFactory.cs:15`) → **fresh** `ServerAbility` instance per activation → `ActivateAbility` (`:65`, injects `Resolver`/`SimulationStates`/`BakedData`/`CharacterDef`, calls `OnStart`) → `TickAbilities` (`:129`) calls `.Tick` each frame → `EndAbility` (sets `AttackSlot=0`) → deactivate + cooldown (`:181`).

**Slot indices:** `0=LMB, 1=RMB, 2=Q, 3=E, 4=R, 5=F`; `airborne` routes `(0,true)→AirLMB`, `(1,true)→AirRMB` (`CharacterDefinition.GetSlotAbility`, `:147`).

**Base classes to reuse:**
- `StageChainAbility` (`StageChainAbility.cs`) — multi-stage melee combos w/ input-buffered chaining; stages come from `AbilitySpec.Stages[]`. `LmbCombo`/`AirLmbCombo` are character-agnostic subclasses → **reuse verbatim**.
- `ChargeAttackAbility` (`ChargeAttackAbility.cs`) — hold-to-charge, **binary** tap-vs-hold (not analog). `_wasCharged = _chargeTicks >= ChargeHoldTicks`; picks `ChargedStages[0]` if charged else `Stages[1]`. Hooks: `OnChargeStart` (`:35`), `OnAttackStart` (`:38`). `GetChargeHoldTicks(def, fallback)` reads `spec.ChargeHoldTicks`.
- `AirRmbAttack` (`AirRmbAttack.cs`) — shared single-stage aerial spike; reads `Stages[0]`.
- `FightGuyDragonKick` (`FightGuyDragonKick.cs`) — **reference** for homing (scans `SimulationStates` for nearest target, steers `s.VX/VZ`), mid-ability state change from a hit (`OnHitEntity` → phase transition), and multi-hit escalating-damage combos with a final launcher.

**Knockback** (`KnockbackProfile.cs`): `KnockbackProfile` enum + `ProfileTable` `(angle, base, growth)` — `Light(15,2,1.5)`, `Medium(15,8,5)`, `Launcher(25,8,4)`, `Kill(20,18,10)`, `Spike(-45,12,4)`, `Custom`. Authored per hit on `HitboxEvent.Knockback` (`KnockbackData{Profile | Custom + Angle/Base/Growth}.Resolve()`). Applied by `Simulation.ApplyKnockback` (`Simulation.cs:891`): `mag = base + growth*(pct*0.01)`; `KVY>0` sets `IsGrounded=false` (launchers pop off ground). Hitstun derived from `kbMag` capped by `StunTicks`; DI added at hitstun end (`Simulation.cs:454`).

**Self-movement:** write `s.VX/VY/VZ` directly in `Tick()` (Grapple/Dragon/Cyclone pattern), or `SetVelocityInFacing(ref s, forwardSpeed, vertical)` (`ServerAbility.cs:199`), or `AttackStage.LungeForce` (one-shot burst at stage start). **`AttackStage.MoveX/MoveY/MoveZ` are declared but NEVER consumed** (verified via grep) — do not rely on them; set velocity directly.

**Hitbox authoring** (`AttackData.cs` `HitboxEvent`): `TriggerTick`, `DurationTicks`, `Shape` (Sphere/Capsule), `Radius`, facing-rotated `OffX/Y/Z` (+ `EndOffX/Y/Z` for capsule far end), `Damage`, `Knockback`, `StunTicks`. **Reach-y disjoint sword = `Shape=Capsule` with `EndOff` extended along facing.** For sim-only (no baked data) use `OffX/Y/Z`, **not** `BoneName` (bone-attached hitboxes need baked skeleton data).

**Movement gating:** velocity/movement auto-processed only when `State==Idle`; during `Attacking`, abilities own velocity. All constraints are server-authoritative (client passes `canMove: null`).

---

## 3. Per-slot verdict

| Slot | Ability | Approach | New code? |
|------|---------|----------|-----------|
| LMB (0) | Light Slash Combo (4-hit) | **Reuse `LmbCombo`** — author `Stages[]` only | Data only |
| AirLMB (0 air) | Air Slash (3-hit) | **Reuse `AirLmbCombo`** — author airborne `Stages[]` | Data only |
| RMB (1) | Charged Spin | Subclass `ChargeAttackAbility` → `KistuRmbSpin` (`Stages[1]`=poke, `ChargedStages[0]`=horizontal Kill) | Thin subclass |
| AirRMB (1 air) | Falling Slash | **Reuse `AirRmbAttack`** (Spike KB) — or tiny `KistuAirRmbSpike` if self-plunge | Data (+~10 lines opt) |
| Q (2) | Counter | **NEW `KistuCounter` + `ServerAbility.TryCounter` virtual + `ResolveHits` interception** | **New infra** |
| E (3) | Charged Dash Slash | Subclass `ChargeAttackAbility` → `KistuDashSlash` (dash via `SetVelocityInFacing`, no stun) | Thin subclass |
| R (4) | Rising Slash | **NEW `KistuRisingSlash` + `CharacterState.RChargesLeft` charge-stock** | **New infra** |
| F (5) | Blade Flurry | **NEW `KistuUltFlurry`** (moving multi-hit → launch, DragonKick pattern) | New subclass |

**The only genuinely novel infra:** (1) `CharacterState.RChargesLeft` charge-stock + gate/consume/refund; (2) `ServerAbility.TryCounter` + target-side interception in `ResolveHits`. Everything else is subclasses + data.

---

## 4. Phases

### Phase 0 — Character scaffold (prerequisite for everything)
*Goal: Kistu exists, is selectable, spawns, and every slot dispatches to a placeholder ability. Builds green.*

1. `src/Shared/CharacterDefinition.cs:7` — add `Kistu` to `enum CharacterClass : byte { None, Manki, FightGuy, Kistu }` (ordinal 3).
2. `src/Shared/CharacterDefinition.cs:~181-189` — append `BuildKistu()` to the `BuildRegistry()` array at index 3 (ordinal-aligned; order is load-bearing).
3. **New** `src/Shared/Characters/KistuData.cs` — `public static partial class CharacterRegistry { private static CharacterDefinition BuildKistu() {...} }` mirroring `FightGuyData.cs`. Fill:
   - **Sim-required:** `Class`, `DisplayName`, `MovementStats` (copy FightGuy: walk 10 / sprint 14 / dash 32 / jump 14 / gravity 34 / 2 jumps / dash 8t / dash CD 48t — tune later, agile so maybe faster), `CapsuleRadius`/`CapsuleHeight`, `HurtboxRadius`, `HurtboxCapsules[]` (static fallback so entity is hittable), all 8 `AbilitySpec`s (initially minimal/stub stages).
   - **Art-deferred placeholders:** `ModelResourcePath = "Characters/FightGuy"` (reuse prefab), `BakedDataPath = ""` (→ capsule hurtboxes), copy `VisualScale`/`HipHeight`/`ModelYOffset`/`ModelSoleOffset` from FightGuy, `AnimationNames` = placeholder strings, `HurtboxBoneDefs = null`.
4. `src/Shared/Abilities/AbilityFactory.cs:17` — add `CharacterClass.Kistu => CreateKistuAbility(slot, airborne)` and a `CreateKistuAbility(byte slot, bool airborne)` method:
   ```
   (0,false)=>new LmbCombo(), (0,true)=>new AirLmbCombo(),
   (1,false)=>new KistuRmbSpin(), (1,true)=>new AirRmbAttack(),
   (2,_)=>new KistuCounter(), (3,_)=>new KistuDashSlash(),
   (4,_)=>new KistuRisingSlash(), (5,_)=>new KistuUltFlurry()
   ```
   (Stub the new classes in Phase 0 as trivial `EndAbility`-after-N-ticks placeholders so it compiles; flesh out in later phases.)
5. `tests/Shared.Tests/TestHelpers.cs` — add `KistuDef` accessor (`CharacterRegistry.Get(CharacterClass.Kistu)`) + `KistuGroundPY`; `tests/Shared.Tests/KitScenarioTests.cs` — add `KistuGpy`.

**Verify:** `dotnet build src/Shared/ --nologo` green; a smoke test that spawns Kistu and ticks idle without throwing; `AbilityLifecycleTests`-style test that each of the 8 slots activates.

---

### Phase 1 — Data-only abilities: LMB, AirLMB, AirRMB
*Reuse existing base classes; author stage data + hitbox geometry only.*

- **LMB** (`def.LMB.Stages`, 4 stages, reuse `LmbCombo`): each stage `DurationTicks` + `ChainWindowTicks>0` (last stage 0) + one `HitboxEvent` (Capsule, reach-y `EndOff` along facing, modest `Damage`). Finisher stage: bigger KB (`Launcher` for the juggle entry, or `Medium` — respecting the *predictable-KB, no scripted-combo* pillar, keep it a plain readable value, not a wired combo hook). Optional small `LungeForce` per stage.
- **AirLMB** (`def.AirLMB.Stages`, 3 stages, reuse `AirLmbCombo`): fast, low commit, near-neutral/slight-up KB (juggle-sustain + fall-stall). `ChainWindowTicks` for chaining.
- **AirRMB** (`def.AirRMB.Stages[0]`, reuse `AirRmbAttack`): single committed slash, `HitboxEvent.Knockback = Spike` (angle −45..−70) for the downward spike. If Kistu should also plunge, add ~10-line `KistuAirRmbSpike : ServerAbility` (copy `AirRmbAttack`, add `SetVelocity(ref s,0,-diveSpeed,0)` in `OnStart`) and swap the factory `(1,true)` entry.

**Verify:** golden scenarios (`KitScenario` + `AssertGoldenScenario`) for LMB chain (all 4 stages, damage/KB), AirLMB (3-stage chain), AirRMB (downward KB direction on the dummy). Snapshot mid-ability tick where hitbox is active.

---

### Phase 2 — Charge abilities: RMB (kill spin), E (dash slash)
*Subclass `ChargeAttackAbility`; charge is binary tap-vs-hold.*

- **`KistuRmbSpin : ChargeAttackAbility`** — `Stages[1]` = quick horizontal poke (`Medium` KB, fast), `ChargedStages[0]` = charged spin = **kill move** (`Kill` KB, horizontal-biased angle ~10-20°, slow startup, high growth). `ChargeHoldTicks` ~45-60. `Behavior = ChargeAttack`, `AimMode = None`.
- **`KistuDashSlash : ChargeAttackAbility`** — override `OnAttackStart` → `SetVelocityInFacing(ref s, dashSpeed, 0)` with `dashSpeed` short if `!_wasCharged` else full (two-tier gap-close; "distance scales with charge" becomes tap=short / hold=full, acceptable given binary charge). Add a `Tick` override re-applying forward velocity for the dash duration (DragonKick loop pattern) so it travels, not just an initial burst. Hitbox with **no** (or tiny) `StunTicks` — **no stun** (differentiates from FightGuy Cyclone Kick, preserves zero-hard-CC). Serves as horizontal recovery. `Behavior = ChargeAttack`, `AimMode = None`.

**Verify:** hand-written unit tests (`ChargeAttackAbility` charge needs a **manual** `sim.Tick` loop holding `IsAiming` — `TestHelpers.TickN` drops held input after tick 0):
- RMB tap (release before threshold) → uses `Stages[1]`, lower damage; RMB hold past threshold → `ChargedStages[0]`, kill KB.
- E tap → short PX delta; E hold → larger PX delta; target hit takes no hitstun (no stun).

---

### Phase 3 — R signature: charge-stock + homing + rising launcher + capped recovery
*The one persistent-resource build. Charge pool must live on `CharacterState` (ability instances are recreated per activation).*

1. `src/Shared/CharacterState.cs` — add `public byte RChargesLeft;` (+ optional `public ushort RChargeRegenTicks;`). Initialize to max (~2) on spawn/registration.
2. `src/Shared/ServerSimulation.cs:PreTickAbilities` (gate near `:345`/`:407`) — when the activating slot is R (index 4) and `state.RChargesLeft == 0`, skip activation (treat as on cooldown). On successful R activation, decrement `RChargesLeft` (or decrement in `KistuRisingSlash.OnStart`). Optional: regen one charge every N ticks in the main sim loop.
3. **New** `src/Shared/Abilities/KistuRisingSlash.cs : ServerAbility` — multi-phase (model on `FightGuyDragonKick`):
   - **Rise/home phase:** `SetVelocity` upward (`s.VY = riseSpeed`); if a target is near, steer `s.VX/VZ` toward it — read `s.TargetEntityId` (client `PickScreenTarget` soft-locks nearest ≤20m) with a `SimulationStates` nearest-enemy scan fallback (DragonKick `:66-80`).
   - **Hit window:** spawn upward `Launcher` hitbox (steep angle ~60-80° for a rising launch) via `SpawnHitbox`.
   - **Recovery cap:** if it whiffs (no hit) in empty air, cap total upward `VY`/duration so it grants only limited height → *honestly exploitable recovery* (the designed flaw).
   - **Refund:** override `OnHitEntity` → `RChargesLeft = min(max, RChargesLeft+1)` (refund on connect → sustains the juggle only while actually hitting). `AimMode = None`.
4. Regression coverage for the new state field: add `RChargesLeft` to `tests/Shared.Tests/GoldenSnapshot.cs` `EntitySnapshot.FromState` **and** `KitScenarioTests.AssertEntityEqual` (else it won't be diffed).

**Verify:** unit tests — R launches a grounded dummy upward (`KVY>0`, dummy `State=Hitstun`); homing steers toward off-axis target; hitting a target refunds a charge (fire R twice with a hit between → second activation allowed with only ~2 base); whiff in air caps self height below the on-hit height; `RChargesLeft==0` blocks activation.

---

### Phase 4 — Q Counter: new target-side interception infra
*No counter/parry/damage-interception exists anywhere today. This is the deepest new build.*

1. `src/Shared/Abilities/ServerAbility.cs` (~`:43`) — add a target-side virtual:
   ```csharp
   public virtual bool TryCounter(ref CharacterState defender, ref CharacterState attacker,
       CharacterDefinition attackerDef, ref float damage, ref float knockback) => false;
   ```
2. `src/Shared/ServerSimulation.cs:ResolveHits` (`:654`) — at the **top** of the per-hit loop, **before** `ApplyKnockback` (`:681`): look up `_activeAbilities[hit.TargetEntityId]`; if it exists and `TryCounter(...)` returns true, **skip** the normal damage + `ApplyKnockback` on the defender for this hit and apply the riposte (launch the attacker via `Simulation.ApplyKnockback` on the attacker state, or spawn a riposte hitbox owned by the defender). Guard against self/owner and multi-hit double-counter (consume the window on first successful counter).
3. **New** `src/Shared/Abilities/KistuCounter.cs : ServerAbility` — `OnStart` enters a parry stance and sets an instance `_windowTicks` (e.g. active frames `startup..startup+window`); `Tick` counts down and, if the window closes with no counter, plays out recovery endlag then `EndAbility` (whiff punish). `TryCounter` returns true only while within the active window; on success set a flag so `Tick` transitions to the riposte/recovery and applies the attacker launch (`Launcher`/`Kill` KB — no lingering stun, consistent with the zero-hard-CC pillar). Reachable via `_activeAbilities[targetId]` because it's an active ability on the defender.

**Verify:** unit tests (two entities) — attacker hits Kistu **during** the window → attacker launched (`KVY>0`/`KVX`), Kistu takes 0 damage & no hitstun; hit **outside** the window → normal damage/knockback to Kistu, no riposte; window expires with no incoming hit → Kistu in recovery/vulnerable; counter fires **once** even against a multi-hit attack.

---

### Phase 5 — F ult: Blade Flurry (moving multi-slash → launch)
*New subclass, no new infra beyond it.*

- **New** `src/Shared/Abilities/KistuUltFlurry.cs : ServerAbility` — model on `FightGuyDragonKick.TickAttack` (`:126`): multi-phase moving loop that applies forward/slight-rising self-velocity and spawns a sequence of flurry hitboxes (modest per-hit damage), ending in a final **big-launch** hitbox (`Kill` or `Custom` high base, ~45° angle). Telegraphed startup, high `CooldownTicks` (ult range). `AimMode = None`.

**Verify:** golden/unit — fires the flurry sequence (multiple hits land on a dummy), final hit launches hard, self-moves during the flurry, high cooldown applied on end.

---

### Phase 6 — Placeholder client config + smoke test
*Zero client C# changes — confirm the data-driven path.*

- Placeholder art fields already set in `KistuData.cs` (Phase 0): `ModelResourcePath = "Characters/FightGuy"`, `BakedDataPath = ""`. `CharSelectController.GetPlayableClasses` auto-enumerates the enum → Kistu appears with no UI edit; `SwapPreviewModel` falls back to a capsule if the prefab is absent.
- Boot straight into Kistu for testing: set `TrainingMatch._playerClassOverride = Kistu` in the `Arena_Offline` scene Inspector, **or** change `MatchConfig.PlayerClass` default (`MatchConfig.cs:8`), **or** pick Kistu in char-select.
- **Smoke test:** launch server + Unity client, select Kistu (T-pose stand-in), and exercise the full loop: LMB chain, RMB tap/hold, AirLMB/AirRMB in air, E dash, R rising launch + juggle sustain (refund-on-hit), Q counter vs an NPC attack, F flurry. Confirm the juggle loop (hit → R → chase → R → RMB kill) works with readable knockback.
  *(If Unity is unavailable this session, substitute a scripted Shared-sim integration run driving the 8 slots against an NPC and asserting the juggle sequence — the sim is the authority; the client only renders.)*

---

### Phase 7 — Full verification + cleanup
- `dotnet build src/Shared/ --nologo` (auto-copies DLL to Unity Plugins) then `dotnet test tests/Shared.Tests/ --nologo` (<3s, ~151 tests + new ones) — all green.
- Regenerate goldens for new standard-ability scenarios: `REGENERATE_GOLDENS=1 dotnet test ...`, then re-run to confirm byte-stable.
- Docs cleanup: add a **Kistu** column to the slot table in `docs/systems/ability-architecture.md`; note the counter as a new pattern; optionally promote the *predictable-KB / emergent-combos* pillar into `docs/systems/combat-systems.md` (flagged in the design doc). Flip `docs/characters/kistu.md` status from "Design" to "Implemented (sim; art pending)".

---

## 5. Dependency graph / parallelization

```
Phase 0 (scaffold)  ── prerequisite for all ──┐
                                              ├─ Phase 1 (LMB/AirLMB/AirRMB data)   ┐
                                              ├─ Phase 2 (RMB/E charge subclasses)  │  independent
                                              ├─ Phase 3 (R: CharacterState + gate) │  (Phase 3 & 4 touch
                                              ├─ Phase 4 (Q: ServerAbility + ResolveHits) │  different files)
                                              └─ Phase 5 (F flurry)                 ┘
Phases 1-5 ──> Phase 6 (client placeholder + smoke) ──> Phase 7 (test pass + cleanup)
```

Phase 0 is a hard prerequisite (everything imports the enum/registry/factory). Phases 1-5 are mutually independent and parallelizable after 0 (Phase 3 edits `CharacterState.cs`+`PreTickAbilities`; Phase 4 edits `ServerAbility.cs`+`ResolveHits` — distinct hunks). Phase 6 needs all abilities present; Phase 7 is the final gate.

## 6. Risks / open notes
- **Homing target source:** relies on `s.TargetEntityId` being populated server-side for the player (client feeds `PickScreenTarget` each tick). Confirm the server path sets it; the `SimulationStates` nearest-enemy scan is the fallback (DragonKick already does this).
- **Binary charge:** E "distance scales with charge" is realized as two tiers (tap=short, hold=full), since `ChargeAttackAbility` charge is binary, not analog. Acceptable; note if analog is later wanted it's a base-class change.
- **`AttackStage.MoveX/Y/Z` dead:** never consumed — all per-tick movement must be direct `s.VX/VY/VZ` writes or `LungeForce`.
- **Numbers are placeholders:** damage / KB base+growth / charge counts / CDs / windows / refund cap tuned in a later balance pass against `character-kit-design-principles.md` and existing kits.
- **New `CharacterState` field cost:** `RChargesLeft` adds to the struct; ensure it's included in any state serialization/rollback snapshot paths (netcode) and in golden `EntitySnapshot` — check `CharacterStatePacket` (44 bytes/entity) if the field must cross the wire, otherwise it's server-only sim state.
