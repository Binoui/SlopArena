# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

Primary users are friends playing SlopArena online. They need to join a match, understand each fighter quickly, and complete short remote matches without requiring every player to operate hosting or networking infrastructure.

## Product Purpose

SlopArena is an open-source 3D platform fighter built around expressive movement, knockback, compact hero-style kits, and room for unexpected play. The immediate product goal is a playable friends demo that gets real remote feedback on four admitted cooked characters: FightGuy, Manki, Kistu, and Bonk.

Success means friends can enter a match, understand what happened, and have enough readable, expressive interaction to want another round. Package admission is not treated as proof of kit completeness or player acceptance.

## Positioning

SlopArena's distinctive mechanism is a server-authoritative 60 Hz simulation shared by the Unity client and GameServer. The same deterministic gameplay model supports training, prediction, reconciliation, rollback, and online matches while leaving presentation in the client.

The combat identity combines camera-relative 8-direction movement, jump arcs, double jumps, fast-fall, ledges, Dash, damage percent, Knockback, Hitstun, Hitstop, Combo Influence, Clash, Burst, and compact fighter kits with meaningful recovery, zoning, and finisher choices.

## Operating Context

The primary operating context is Unity 6 on desktop, with a local training flow and online matches through a GameServer. A master server manages browser, lobby, character-select, and results flow; it does not simulate matches.

Players move through the MatchFlow: Server Browser, Lobby Room, Character Select, Countdown, Fight, Results, and Lobby Room. Non-technical players should be able to use an OfficialServer; technical players may use HostAndPlay.

Gameplay runs at 60 Hz. The client renders authoritative state and semantic presentation events; the Shared simulation remains the gameplay authority.

## Capabilities and Constraints

- Each character uses a canonical 16-entry grid: grounded and aerial variants of normals `1`, `2`, `3`, `4` and specials `A`, `E`, `R`, `F`.
- Physical controls are input adapters and are not persisted move identities.
- New characters are package-native. Editable packages live under `client/Unity/Assets/CharacterPackages/<package>/` and cook into immutable runtime artifacts under `content-cooked/<package>/`.
- Match Content Catalogs pin exact package IDs, versions, dependencies, capability versions, and hashes.
- Raw authoring JSON is cook input, not the runtime contract.
- Server-side simulation is authoritative. Unity physics, animation callbacks, VFX, and audio are presentation or authoring aids only.
- Shared simulation code remains free of Unity types and engine physics queries.
- Current admitted demo roster: FightGuy, Manki, Kistu, and Bonk. Nilus is legacy compatibility content and is not part of the four-character demo roster.
- The longer-term Workshop direction permits approved deterministic primitives and package-owned assets, not arbitrary simulation code, native plugins, or direct Unity-path dependencies.
- The current implementation targets Unity 6 desktop. SlopArena also has a separate web presentation surface; the Impeccable platform value `adaptive` records that the product spans these first-party surfaces rather than asserting a mobile-native target.

## Brand Commitments

The product name is SlopArena. Existing project guidance commits to an underground fight-poster energy filtered through a playful, self-aware prototype: cool fighting imagery, dry jokes, rigid geometry with controlled imperfection, strong information hierarchy, bold character silhouettes, and a welcoming game made for friends.

The canonical visual-language guide is `docs/design/visual-language.md`. It applies across menus, results, trailers, web pages, Workshop documentation, and future creator tools while adapting composition to each screen's job. The separate `../sloparena-web` reference mentioned during init was not present in the available workspace; the repository's visual-language guide identifies the web surface as the canonical graphic reference.

## Evidence on Hand

- `README.md` contains the current product description, four-character demo status, architecture, development setup, and roster summary.
- `CONTEXT.md` defines the canonical domain vocabulary and settled mechanics.
- `docs/plans/2026-09-05-playable-demo-reset.md` defines the current demo priority.
- `docs/design/visual-language.md` defines the shared presentation grammar.
- `content-cooked/` contains cooked runtime artifacts for the admitted packages.
- `client/Unity/Assets/CharacterPackages/` contains package-native authoring sources and asset catalogs.
- The repository has no confirmed testimonials, press claims, benchmark claims, or external player-acceptance evidence. Future surfaces must not fabricate them.

## Product Principles

1. Make the next player action and the last gameplay event immediately understandable.
2. Preserve deterministic, server-authoritative gameplay across local and online paths.
3. Give each fighter a compact, readable identity with meaningful counterplay.
4. Prefer honest prototype energy over generic polish or unsupported claims.
5. Expand creator content through immutable, reviewable, deterministic packages.

## Accessibility & Inclusion

Gameplay and UI must not rely on color alone for status, player identity, or combat information. Preserve readable contrast, stable HUD information, clear focus and confirmation states, and reduced-motion behavior for non-gameplay presentation where the platform supports it.
