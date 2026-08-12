# SlopArena Project Context

## About the Dev

- French solo dev, English only communication.
- Uses Unity 6 C# with .NET 8. Shared code (`src/Shared/`) targets netstandard2.1, compiled as a DLL imported by Unity via `client/Unity/Assets/Plugins/SlopArena.Shared/`.
- Uses Blender 5.1 (detected from `~/.config/blender/5.1/extensions/`).
- Server-authoritative architecture: the server simulation is always the source of truth, never use client-side hacks (position overrides, state checks) for gameplay mechanics.
- Preference: `dotnet build src/Shared/ --nologo` after every Shared change (auto-copies DLL to Unity Plugins).
- Squash commits, then push.
- NEVER install anything without asking.
- "Stop saying no to my choices" was a direct correction. Implement numeric choices without arguing. Suggest once only if correctness issue, then implement their value.
- Follows the project CLAUDE.md workflow: think before coding, state assumptions explicitly, explain before editing, present tradeoffs. For multi-step changes state a brief plan first.
- Never change files without explaining the plan first. The flow is: state the problem → describe the fix (what files, why) → wait for "vas y" / "go ahead" → implement. See Debugging Protocol below.

## Project Overview

SlopArena is a 3D platform fighter (Smash/DKO-style) with a server-authoritative 60Hz UDP model with client-side prediction + rollback reconciliation.

### Architecture

Three server-like things (see `CONTEXT.md` "Disambiguation: server"):
- **Master server** — separate repo (`SlopArena-MasterServer`), ASP.NET Core + SignalR + PostgreSQL. Matchmaking, lobby, char-select, results. Never runs simulation. See `docs/systems/master-server.md`.
- **Game server** — this repo's `src/Server/`, .NET console. Registers with master, receives match-start commands via HTTP, runs 2-4 player matches over UDP. See `docs/systems/netcode-architecture.md`.
- **ServerSimulation** — `src/Shared/`, pure C# tick loop. Runs identically on client (prediction) and game server (authority).

```
Master Server (SignalR/REST)          Game Server (src/Server, .NET console)
  Lobby / Char Select / Results         MatchControlServer (TCP :base_port)
         │                                    │ POST /match/start
         │ POST /match/start                  ▼
         └────────────────────►  MultiMatchOrchestrator (port allocation)
                                      │ spawns MatchInstance (one per match)
                                      │
  Unity Client ◄──── UDP ─────── MatchInstance (60Hz sim, dedicated port)
  Unity Client ◄──── SignalR ──── Master Server (lobby/meta)
```

- `Shared/` is pure C# with zero Unity dependencies. No `UnityEngine.*` imports.
- All tick durations use `ushort` (max 65535 ticks = ~18 minutes).
- Packet serialization uses `System.Buffers.Binary.BinaryPrimitives` (little-endian).
- Client → Server: `entityId(8) + tick(4) + InputState(20)` = 32 bytes, 60Hz (JumpHeld bit, ADR-0016; FaceToCamera bit, ADR-0017; ToggleLock bit, ADR-0018).
- Server → Client: `entityId(8) + tick(4) + CharacterStatePacket(113) + hasInput(1) + InputState(20)` = up to 146 bytes per entity (input relay, issue #80; hitstop freeze field, ADR-0012; short-hop field, ADR-0016; LockOn flag, ADR-0018).
- Match flow: Server Browser → Lobby Room → Character Select → Countdown → Fight → Results → Lobby Room (ADR-0008). Master server (SignalR) manages lobby/char-select/results; game server (UDP) manages countdown/fight only.

## Key Conventions

### Project Structure
- `src/Shared/` — canonical shared code (netstandard2.1). Real .cs files, single source of truth.
- `src/Server/` — game server (.NET console). Multi-match orchestrator, match instances, master server registration. Standalone project — build with `dotnet build src/Server/`.
- `client/Unity/Assets/Plugins/SlopArena.Shared/` — compiled DLL, auto-copied via post-build.
- `dotnet build src/Shared/` → rebuilds DLL and copies to Unity Plugins.
- `client/Unity/Assets/Scripts/Runtime/` — Unity MonoBehaviour scripts (Input, Renderer, Camera, UI, Network, World).
- `tests/Shared.Tests/` — xUnit tests for simulation + codecs (LobbyPayloadCodec, MatchStartRequestCodec).

### Unity Conventions
- Use `MonoBehaviour.Update/FixedUpdate`, not Godot `_Process`/`_PhysicsProcess`.
- AnimancerComponent plays clips directly. No AnimatorController.
- Input via Unity InputSystem (`Keyboard.current`, `Mouse.current`).
- Button text color = White.

### Movement
- LungeForce on AttackStage implements forward burst.
- No normal movement processing during Attacking state.
- Warp is server-side via Simulation.ProcessWarp.
- Camera is a world sibling (instantiated by TrainingMatch), absolute yaw — mouse only.
- Double jump for all classes, dash replaces old air-dodge.

### Combat
- Smash-style % system (no HP). DamagePercent 0-999.
- Knockback scales: `kbScale = 1 + (DamagePercent * 0.01)`.
- ServerAbility lifecycle (OnStart/Tick/OnEnd) for complex abilities; data-driven HitboxEvents for simple attacks.
- Hit detection via pure math (CombatMath.cs, SpellResolver.cs) — no Unity physics queries on server.

### Input
- InputController.Poll() + BuildInputState() → SlopArena.Shared.InputState.
- Sim handles input buffering via InputBufferWindow=6 ticks.
- Entity IDs: player=1, NPCs=100-104.

### Animation
- **Animancer** (third-party) — `PlayerRenderer` calls `_animancer.Play(clip)` directly. No AnimatorController, no triggers.
- `CharacterAnimationConfig` ScriptableObject maps name → `AnimationClip`. Auto-loaded from `Resources/AnimationConfigs/{Class}_AnimConfig`.
- Clip playback speed modulated per-stage: `animSpeed = frameCount / DurationTicks` (server timing).
- Per-clip overrides (`AnimationClipConfig` on `CharacterDefinition`) for loop/extrapolation settings.
- Idle/run crossfade (no blend tree). Jump/Fall/Dash/Hitstun play once. Ability clips per `AnimationNames[]`.
- Mixamo FBX scale: cm (0.01 factor). Blender 5.1 uses layered actions API.


### Git
- Commit convention: Conventional Commits, one squash commit per branch — `<type>(<scope>): <imperative summary> (issue #N)`. Full rules in `docs/contributing/conventions.md` § Git & Commits.
- Squash + PR flow lives in `.omp/skills/sloparena-finish-branch`.

### Agent Verification Protocol (worktree agents)
- **Scope note:** `### Debugging Protocol` below (explain → wait for "vas y" → implement) governs the interactive main session. Worktree agents are AFK: they STOP for approval only on architecture-level changes (design doc + go-ahead); everything else proceeds after the in-session plan.
- **Unity is main-repo-only.** The Unity Editor and the MCP bridge (`:26356`) serve the main checkout — worktree agents must NOT run `scripts/mcp-*.sh`; they'd hit the main repo's Editor and see none of their own changes.
- Headless verification, mandatory before finishing a slice:
  - `dotnet build src/Shared/ --nologo` after any Shared change (auto-copies the DLL to Unity Plugins).
  - `dotnet test tests/Shared.Tests/` — filtered during development, full suite at the end.
  - `dotnet build src/Server/` when server code changed.
- Optional stronger gate: `"$UNITY_EDITOR" -batchmode -quit -projectPath <worktree>/client/Unity` once per worktree — catches Unity-script compile errors. First run builds a fresh `Library/` (slow, multi-GB); afterwards fast. `$UNITY_EDITOR` = Unity 6000.0.78f1 install path. **Gitignored local packages don't travel to worktrees** (`Packages/com.kybernetik.animancer` is paid and never committed): run `scripts/setup-worktree-unity-packages.sh` first or the gate fails on Animancer type errors.
- **Handoff:** if the slice touches Unity-facing code (`client/Unity/Assets/Scripts/`, prefabs, animation, input), write a short "Test in Unity" checklist (what to playtest, what to look for) to `TESTING-UNITY.md` at the repo root (gitignored — never committed). `sloparena-finish-branch` picks it up into the PR body.

### Debugging Protocol
1. State the problem (1-2 sentences)
2. Describe the fix (2-3 sentences): what, which files, why
3. Wait for confirmation before coding — "vas y", "go ahead"
4. For architecture-level changes: write design doc in `docs/<topic>.md` first, present options with pros/cons
5. One file change at a time for complex edits

### Docs worth reading before system-level work
- `docs/architecture-overview.md` — directory map, data flow, pitfall list
- `docs/systems/netcode-architecture.md` — UDP protocol, rollback, packet layout, match-control topology
- `docs/systems/master-server.md` — master server (separate repo) endpoints, deployment, DB schema
- `docs/systems/combat-systems.md` — universal combat mechanics
- `docs/systems/hitbox-system.md` — hit detection, hurtboxes, collision math
- `docs/systems/animation-system.md` — Animancer clip playback, extrapolation, speed modulation
- `docs/adr/0008-lobby-room-match-flow.md` — match flow decision (lobby → char select → fight → results)
- `CONTEXT.md` — canonical domain vocabulary (GameServer, MatchControlServer, MatchInstance, Roster, etc.)
- `docs/contributing/conventions.md` — art direction, naming, pipeline
- `docs/plans/` — active refactor plans (ability refactor, AnimationTree builder, online PvP roadmap)
