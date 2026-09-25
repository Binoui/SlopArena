# Testing and Verification

## Local iteration

Source or asset tuning uses the existing Editor development catalog and the affected
Ability Lab or Training path. A source edit can be compiled in memory without changing
`content-cooked`, generated animation catalogs, or roster pins. Invalid source blocks
preview with diagnostics, not stale fallback. Recompile for changed C#/Shared plugin, or
when Unity requests compilation; do not force a project recompile for prose or every JSON
numeric edit. Read current Unity errors and exercise the changed behavior. Local-only
preview is not persisted package verification.

## Integrated change

Shared edits require focused behavioral coverage while iterating, then `dotnet build
src/Shared/ --nologo` and `dotnet test tests/Shared.Tests/ --nologo` at delivery. Server
edits require `dotnet build src/Server/ --nologo` and `dotnet test tests/Server.Tests/
--nologo`. Unity-facing behavior needs the affected runtime check and current
compile/console evidence, not all unrelated scenes. Accepted character source/asset
changes intended to ship cross the explicit cook/inspect/roster-refresh boundary before
delivery. Documentation/tooling-only edits use the Python checks from the documentation
checker; no Shared build or Unity session is required for this cleanup.

## Distributable demo

Cook changed accepted packages, verify exact roster identities and all required payloads
in fresh publish outputs, then build and exercise the packaged client/server
join-to-rematch path. The server project and release scripts derive required package IDs
from the roster manifest and verify all four payloads for each admitted package.

### Shared build and tests

`src/Shared/` is pure C# `netstandard2.1`. Run after Shared changes:

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
```

Use a focused test filter while iterating, then run the full Shared suite before delivery. Tests should assert observable simulation behavior: state transitions, timing boundaries, collision, damage/Knockback, interruption, deterministic serialization, and catalog identity. Avoid assertions tied only to implementation details or volatile test totals.

Keep mechanic and reporting tests independent of incidental roster tuning. Use explicit
empty-slot fixtures rather than assuming a work-in-progress move remains empty, and assert
facing/lock transitions directly instead of pinning unrelated movement distances. Full-jump
setups must hold through both JumpSquat and the short-hop decision window. Multi-hit tests
should count accepted contacts, not gaps between hitboxes whose active windows may overlap.

### Targeted contract tests

Choose tests that cover the changed boundary:

- movement, jump, Dash, ledge, and air-use behavior;
- hitbox/projectile geometry and collision;
- Hitstun, Hitstop, Knockback, Combo Influence, Clash, and Burst;
- cooked timeline execution, typed operations, capability admission, interruption, and presentation events;
- package compiler diagnostics, deterministic bytes, manifest/hash validation, and Match Content Catalog admission;
- codecs and server/client content requirements.

New observable behavior needs a behavioral test when existing coverage would not fail for a plausible regression. Keep tests in `tests/Shared.Tests/` and use the existing helpers and fixtures.

### Package and cook checks

For a package change, inspect before cooking:

```bash
unity pipeline list --format json
unity command --project-path client/Unity \
  sloparena.character.inspect --target <package> --format json
unity command --project-path client/Unity \
  sloparena.character.cook --target <package> --format json
```

For a rostered package, require a successful semantic result, valid inspect status,
`dirtyOrStale: false`, and matching source/cooked/package hashes. For a source-only probe,
require successful package resolution plus structured diagnostics and semantic cook failure
only when the probe intentionally has unresolved bindings. A failed cook must preserve the
last valid artifact; with no prior artifact, verify the cooked directory remains absent.
Check `content-cooked/<package>/` and the exact roster requirement only when the package is
built-in.

### Bonk pipeline probe evidence

The Bonk probe is not gameplay coverage. Record:

- inspect `success: true`, `packageId: bonk`, and sixteen canonical slots;
- cook semantic `success: true` with the shared dash/hit bindings;
- cooked payload hashes and preservation of `content-cooked/bonk/` after any later failure;
- package-specific stale tracking after Bonk source/catalog/dependency notifications;
- one queue request for repeated notifications, with unrelated package statuses unchanged;
- Unity recompile status and current console errors.

`KitScenario` golden tests begin only after an approved Bonk kit and successful cooked
package exist. The probe deliberately has no damage, timing, recovery, or capability
contract to golden-test.

The maintained FightGuy check is:

```bash
scripts/verify-fightguy-package.sh
```

### Ability Lab and Unity Training

For integrated Unity-facing changes and accepted package verification:

1. Confirm the Unity Pipeline is reachable, recompile the Editor when code or the
   Shared plugin changed, and inspect current Unity errors.
2. Run `EditorDevelopmentContentSelfTest.Run()` (or the named menu item) when
   content-resolution or cook behavior changed. A valid `character.json` edit must
   change the next Editor Training catalog without changing `content-cooked` or the
   generated animation catalog bytes. An invalid operation must report
   `value.out-of-range` at `character.operation.tick` with its code, path, and message,
   block the local catalog, and never use the old persisted package.
3. Confirm `TryBuildPersistedLocalMatchCatalog` still loads the exact persisted roster
   requirement and hashes. Keep the existing `content-cooked` package/release verification.
4. Open the affected package in Ability Lab, preview a valid persisted cooked draft,
   then open Training and exercise movement, the changed move, collision, interruption,
   and landing behavior. Landing presentation crossfades the current aerial pose into
   idle over the replicated `LandingLagTicks / 60` seconds; ordinary zero-lag landings
   transition directly to locomotion, and hitstun interrupts the blend.

Local iteration uses the transient development catalog and affected runtime path; it
does not require this persisted-catalog self-test for every numerical tuning edit.

The authoritative preview is the cooked Shared path for accepted content. Ability Lab
may show a clearly non-authoritative editing pose for invalid drafts, but Training and
matches must never silently use invalid or stale content.

### Local GameServer/PvP

When the change crosses networking or match composition:

```bash
dotnet build src/Server/ --nologo
dotnet test tests/Server.Tests/ --nologo
```

Exercise a local two-client match where practical. Verify that the GameServer admits the
exact cooked package set, clients receive the same content requirements, attacks resolve
from server state, and respawn/stock flow remains intact. Use [Netcode Architecture](systems/netcode-architecture.md)
for packet and reconciliation details; do not duplicate volatile wire layouts here.

#### Server verification layers

Treat these as separate gates, not interchangeable evidence:

| Layer | Proves | Does not prove |
| --- | --- | --- |
| Shared tests | Deterministic simulation, codecs, content admission | Socket delivery or Unity presentation |
| GameServer tests | Covered server orchestration contracts | A complete live match |
| Master tests (separate repository) | HTTP/SignalR auth, lobby/chat rules, launch requests | PostgreSQL behavior, real GameServer launch, Unity input |
| Local full stack | Actual HTTP/SignalR and UDP lifecycle with real processes | Internet latency/loss, deployment, two-human playability |
| Remote packaged clients | Deployed connectivity and observed human gameplay | Untested failure modes or sustained capacity |

In the Master repository, run:

```bash
dotnet test MasterServer.Tests/MasterServer.Tests.csproj --nologo \
  --logger trx --results-directory /tmp/sloparena-master-results
```

The maintained Master README documents startup, environment variables, and hub
contracts. Its integration tests use real ASP.NET handlers and SignalR clients over
TestServer long polling, isolated EF InMemory databases, and a substituted external
match launcher. Passing them does not prove PostgreSQL transactions or TCP/WebSocket
delivery. Use an isolated PostgreSQL database for the full-stack persistence gate;
do not suppress transaction warnings in production to make an InMemory fixture pass.

#### Local full-stack setup

1. Start a **local Master** using its README, an isolated development database, and
   development-only signing credentials. Record the actual listening URL. Never
   reuse production credentials or point this test at production by accident.
2. Build the GameServer, then make a temporary copy of `src/Server/server.json`.
   Select `deploymentProfile: development` explicitly. Set `masterServerUrl` to
   that listener, `publicIp` to `127.0.0.1` for a same-machine run, an unused base
   `port`, and `arenaDataDir` to the intended baked arenas. TCP control uses the
   base port; UDP match ports span `port` through
   `port + maxConcurrentMatches - 1`. Use reachable public addresses for remote
   clients; do not advertise loopback.
3. Launch the real executable with that explicit configuration:

   ```bash
   dotnet src/Server/bin/Debug/net8.0/SlopArena.Server.dll /absolute/path/to/test-server.json
   ```

   Confirm successful registration and heartbeats in the logs, not merely a
   listening control port. An unavailable Master causes bounded retries; bad
   host credentials or configuration fail startup. Check the selected arena and
   admitted package hashes rather than accepting fallback content as evidence.
4. Configure both clients' Master endpoint **before launch authentication**. Use two
   packaged clients for the remote acceptance pass. One Unity client plus a real
   protocol peer is useful for diagnosis, but is not two-client UI or human-feel proof.
   Use the [Unity CLI workflow](contributing/unity-cli.md) for Editor operations.
5. Confirm distinct player IDs/tags, matching admitted content, and Unity Fixed Timestep
   of 1/60 second. Record the registration/server ID, match ID, and assigned UDP port.

There is currently no maintained one-command full-stack chat smoke harness. The
2026-09-17 pass used a temporary local Master/UDP-peer fixture that was removed after
verification; do not refer to it as an available repository command.

#### Match, membership, and chat acceptance

Run the normal lifecycle first, then repeat it with controlled failures:

| Scenario | Required observation |
| --- | --- |
| Join → character/stage selection → match | Both clients receive the same admission content and reach authoritative gameplay; lobby slots are released without losing Server chat |
| Combat → stocks/respawn → completion | Damage and outcomes agree with server state; result reporting succeeds at the Master and Results opens on both clients |
| Results → rematch | A new match starts; launch identities, conversations, and drafts survive |
| Full waiting roster | An additional admitted chat member can use Server chat without taking a waiting slot |
| Explicit Leave / server switch | Old Server chat becomes read-only and stops delivering; Global and Direct remain usable |
| Hub disconnect/reconnect during a match | Same identity/tag; previously admitted Server membership resumes without adding a waiting player or duplicating history/sends |
| Offline/rate-rejected send | Draft retained, readable feedback, no automatic replay; uncertain delivery is not presented as confirmed failure |
| Direct, rename, mute | Direct stays private; recipient identity is ID-based; rename does not retarget a conversation; local mute filters the sender |
| Compose during combat | Movement/attacks/camera shortcuts suppressed, simulation and damage continue, Escape preserves draft, held controls require release |
| Invalid or mismatched content | Admission fails explicitly; neither client proceeds with stale/substituted content |

Then exercise the boundaries independently:

- Interrupt **SignalR and UDP separately**. A restored chat connection is not proof of
  recovered gameplay. Record both transports' state and the exact recovery behavior.
- Disconnect the host during selection and disconnect a player during a match. Check
  roster/host changes, match cleanup, and whether the remaining client can continue.
- Restart Master and GameServer separately. Record lost in-memory state and surfaced
  errors; do not assume either operation transparently resumes a match.
- Run simultaneous matches and repeated join/leave/rematch cycles. Check isolation,
  port/slot reuse, result association, and absence of cross-match state or Server chat leaks.
- On an isolated test network, introduce latency, jitter, loss, and a brief outage.
  Record the actual impairment settings and remove them afterward. No network-fault
  tool is assumed installed; do not alter the development machine's shared route blindly.
- Exercise real credential renewal and expiry without silently creating another guest.
  Keep tokens and signing secrets out of saved evidence.

#### Evidence and next-pass priorities

Capture server/client logs with timestamps, player/server/match IDs, package hashes,
transport state, authoritative and local ticks, observed action/phase, result-report
HTTP status, and Unity console errors. Keep TRX files and screenshots outside Unity
`Assets`. Distinguish process startup, successful admission, actual packet receipt,
and successful UI transition. Do not force a client into Results to claim lifecycle
success. Restore test preferences, Editor scene/view settings, processes, and temporary
configuration when finished.

The 2026-09-17 chat pass completed a real local match → Results → rematch and explicit
Leave using Unity plus a protocol peer. It also repaired accidental Training ownership
in the PvP scene and corrected Unity's 50 Hz fixed loop to 60 Hz. This was not a broad
server reliability sign-off: local prediction still lagged authoritative state
(one sample: local Countdown at tick 5507 versus server GO at tick 5597), and the full
Shared suite still had gameplay/content failures. The next pass should prioritize:

1. Reproduce and diagnose prediction/phase divergence with both clients' tick evidence.
2. Establish the full Shared failure baseline; do not label unclassified failures pre-existing.
3. Verify PostgreSQL result persistence and the complete lifecycle with two packaged clients.
4. Exercise reconnects, restarts, impaired networks, and concurrent match isolation.

The gitignored root `TESTING-UNITY.md` records the session-specific checks. Temporary
evidence under `/tmp/sloparena-chat-evidence/` is local and disposable, not durable CI
coverage or a prerequisite for running this procedure.

### Verification evidence

Record:

- commands run and whether they passed;
- package/cook status and hashes when content changed;
- Unity console status and the Training/Ability Lab surface exercised;
- the local server/client path exercised when applicable;
- any unexercised runtime surface.

Do not claim a live runtime result from a build or static inspection alone.
