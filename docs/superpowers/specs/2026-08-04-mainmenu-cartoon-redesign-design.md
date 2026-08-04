# MainMenu Cartoon Redesign — Design Spec (S3-v2)

Date: 2026-08-04
Status: Draft v2 (awaiting user approval)
Scope: **Pilot** — MainMenu only. Shared USS restyle propagates to other screens; per-screen polish is a follow-up phase.

## 1. Goal

Rebuild the MainMenu as a **vibrant cartoon (Smash/Megabonk style)** screen with an **SC2-style overlay composition**: a static scene behind, panels floating over it. Layout locked with the user: **global chat bottom-left, TRAINING MODE as the centerpiece, MULTIPLAYER panel on the right (BROWSE / HOST / JOIN)**.

## 2. Decisions (user-confirmed)

| Question | Answer |
|---|---|
| Style | Vibrant cartoon, Smash/Megabonk — playful, chunky, light humor OK |
| Composition | SC2 overlay style — scene behind, panels on top (S3-v2 layout) |
| Backdrop | **Static, simple** — USS-drawn shapes/textures, NOT a live 3D stage. Seam to swap one baked image later |
| Art pipeline | Code-drawn only (USS), no textures for chrome |
| Chat | Global chat **feature** = separate subsystem (master-server SignalR + persistence, other repo) — own spec later. This spec builds only the **chat panel UI shell** in the menu |
| Scope | Pilot MainMenu, propagate later |
| Font | Bundle free-license display font (Baloo 2 ExtraBold) |

## 3. Design language

### Palette (USS custom properties)

```
--bg-top:    #2f6bff   (sky, top)
--bg-bottom: #3d1ad4   (violet, bottom)
--ink:       #191a3a   (outlines + shadows — near-black navy)
--yellow:    #ffc821   (primary action / TRAINING)
--yellow-hi: #ffd95c   (hover)
--white:     #ffffff   (secondary buttons)
--white-dim: #e8ecff   (hover / sub items)
--red:       #ff4d3d   (JOIN, accents)
--red-hi:    #ff6f5e
--panel:     rgba(19,18,45,.85)  (translucent overlay panels)
--text-muted:#7a7a99
--online:    #4cdd88   (status green)
```

### Font

- **Baloo 2 ExtraBold** (SIL OFL) — titles + buttons. Fallback: Luckiest Guy (Apache 2.0, static TTF) if variable-font import misbehaves.
- Small text (≤12px): Unity default font.
- License file committed next to the TTF.

### Signature treatments

- **Extruded 3D title** — stacked `text-shadow` (6 layers of ink) = cartoon pop-out text. Used for "SLOPARENA" logo.
- **Chunky lip buttons** — 3px ink border (6px on the bottom edge) = pseudo-3D lip, radius 12–14px.
  - Note: `box-shadow` is NOT a USS property in Unity 6000.0.78 — the lip is a thicker bottom border (`border-bottom-width: 6px`).
  - Hover: `translate(0,-2px)` + color shift. Pressed: `translate(0,2px)`.
- **Stripes** — repeating 45° yellow/red bar under the logo (repeating-linear-gradient).

### Motion

- Transitions 0.1–0.15s on background/lip/translate for all interactive states.
- Optional idle title float: only if USS `@keyframes` works on Unity 6000.0.78 — verify at implementation; fallback = drop or C# Experimental.Animations. Not a release blocker.
- Multiplayer panel collapse: instant toggle (existing behavior) — height transitions are unreliable in UI Toolkit; polish later.

## 4. Composition (S3-v2) — element map

```
root.screen (gradient sky)
├── backdrop            (absolute, USS shapes: sun, clouds, hills, platforms)
├── header
│   ├── title           (name kept) — extruded SLOPARENA + stripe bar, top-left
│   └── status area     — lbl-host-status (name kept) + online-count pill, top-right
│                        (online-count shows a static placeholder value in the pilot;
│                         real player count wiring = follow-up, same track as chat)
├── center              — btn-training (name kept) BIG yellow + "SOLO PRACTICE" tagline
├── chat-panel          (NEW) bottom-left — UI shell only (see §6)
├── submenu             (name KEPT) bottom-right — MULTIPLAYER panel
│   ├── btn-multiplayer (name kept) — panel header w/ collapse chevron (toggle logic unchanged)
│   ├── btn-serverbrowser (name kept) — "BROWSE SERVERS"
│   ├── btn-host        (name kept) — "HOST GAME"
│   ├── host-row        (name kept) — host-ip-field (name kept) input
│   └── join-row        (name kept) — btn-join (name kept) + ip-field (name kept)
└── version             — "v0.1 DEMO" pill, bottom-center
```

**Critical invariant:** every element name queried by `MainMenuController` is preserved verbatim (verified against `client/Unity/Assets/Scripts/Runtime/UI/MainMenuController.cs`):
`submenu`, `btn-training`, `btn-multiplayer`, `btn-host`, `btn-join`, `ip-field`, `host-ip-field`, `btn-serverbrowser`, `lbl-host-status`, `title`.
The existing toggle semantics also survive: `btn-multiplayer` (panel header) still toggles `submenu`'s display. **Result: zero C# changes.**

## 5. Backdrop (static, USS-only)

- Base: `.screen` vertical gradient (`--bg-top` → `--bg-bottom`).
- Shape layers (absolute-positioned VisualElements in `backdrop`):
  - Sun (yellow circle + glow), 2–3 clouds (white rounded blobs, ~55% opacity)
  - 2 hills (large rounded shapes, darker blue) at the bottom edge
  - 2–3 floating platforms (light rounded bars w/ ink border + lip) + 2–3 idle-fighter silhouettes (simple rounded rects) — static, no animation
  - Subtle texture overlay: low-opacity repeating gradient (pinstripes or dot grid)
- **Swap seam:** `backdrop` exposes one `background-image` slot; a single baked image (AI-gen or stage screenshot) can replace the shape layers later without touching layout. Not needed now.

## 6. Chat panel (UI shell only)

- Bottom-left panel (`chat-panel`): header `# GLOBAL CHAT` + online count, message feed (placeholder rows), input field.
- **No network in this phase.** Input echoes locally at most; messages are placeholders. The feature (master-server SignalR hub, persistence, moderation, rate limiting) is spec'd separately, in the master-server repo.
- Interface seam: panel elements get stable names (`chat-feed`, `chat-input`, `chat-send`) so the future controller connects without UXML churn.

## 7. USS architecture

- Single shared `SlopArena.uss` (existing convention).
- **No CSS custom properties** (`var()`/`:root`): Unity 6000.0.x UI Toolkit has a `StylePropertyReader` bug (ArgumentOutOfRangeException per frame) with custom properties — the palette is written as literal values, matching the file's original convention. Revisit tokens on a Unity upgrade.
- **No `box-shadow`**: not a USS property in 6000.0.78 — lips are thicker bottom borders (`border-bottom-width: 6px`).
- Shared-class restyle (`.screen`, `.title`, `.menu-item*`, `.btn-primary`, `.btn-back`, `.ip-input`) means Lobby / ServerBrowser / LobbyRoom / CharSelect / StageSelect / Results inherit the language automatically. Per-screen polish = follow-up phase after pilot approval.

## 8. Files touched

| File | Change |
|---|---|
| `client/Unity/Assets/UI/SlopArena.uss` | tokens + restyle + new component classes (backdrop shapes, chat panel, multiplayer panel) |
| `client/Unity/Assets/UI/MainMenu.uxml` | new structure per §4 element map; **all controller-queried names unchanged** |
| `client/Unity/Assets/Fonts/Baloo2-ExtraBold.ttf` | new |
| `client/Unity/Assets/Fonts/OFL.txt` | new (license) |
| `TESTING-UNITY.md` | new, gitignored — Unity playtest checklist (AGENTS.md convention) |

**No C# changes. No Shared/Server changes** (nothing to rebuild with dotnet).

## 9. Verification

1. `grep` UXML for all 10 controller-queried names — all present exactly once.
2. Unity: open MainMenu scene, visual check against acceptance list:
   - Backdrop: gradient, sun/clouds/hills/platforms visible; static
   - Extruded logo + stripes; status pills; version pill
   - TRAINING big + centered; hover lift / press squish works
   - Multiplayer panel: header toggles collapse; BROWSE / HOST / JOIN visible; host-IP + join-IP fields styled
   - Chat panel rendered (shell)
3. Playtest flows: TRAINING → CharSelect; HOST GAME (host-and-play flow starts, status label appears); JOIN GAME with IP; SERVER BROWSER scene loads.
4. Optional compile gate: `"$UNITY_EDITOR" -batchmode -quit -projectPath client/Unity` if Unity isn't already open.

## 10. Non-goals (this phase)

- No live 3D scene behind the menu (static backdrop only).
- No global-chat feature (backend) — separate spec in master-server repo.
- No other screens' UXML changes (shared styles inherit; polish later).
- No HUD restyle (separate USS, later).
- No C# changes.

## 11. Open items

- Baloo 2 variable-TTF import behavior in Unity → fallback Luckiest Guy if needed.
- `@keyframes` title float: verify on Unity 6000.0.78.
- Exact backdrop shapes/positions tuned visually in-editor (user will move things around).
