# MainMenu Cartoon Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the MainMenu as a vibrant cartoon (Smash/Megabonk style) screen with an SC2-style overlay composition — static USS backdrop, chat panel bottom-left, TRAINING centerpiece, MULTIPLAYER panel right — with zero C# changes.

**Architecture:** Pure UI Toolkit restyle. One shared stylesheet (`SlopArena.uss`) gets design tokens (CSS custom properties) + a cartoon restyle of shared classes; `MainMenu.uxml` is restructured into the locked S3-v2 layout while preserving every element name queried by `MainMenuController` (verified against the controller — the existing `submenu` toggle semantics survive unchanged). A bundled display font (Baloo 2 ExtraBold, OFL) drives titles/buttons. The chat panel is a UI shell only (no backend in this phase).

**Tech Stack:** Unity 6 (6000.0.78f1) UI Toolkit (UXML/USS), no C# changes, no Shared/Server changes. Fonts from the google/fonts GitHub repo (raw.githubusercontent.com).

## Global Constraints

- **Zero C# changes**: `MainMenuController` is untouched. All 10 queried names must exist exactly once in the new UXML: `submenu`, `btn-training`, `btn-multiplayer`, `btn-host`, `btn-join`, `ip-field`, `host-ip-field`, `btn-serverbrowser`, `lbl-host-status`, `title`.
- **Toggle semantics preserved**: `btn-multiplayer` must still toggle `submenu`'s display. New behavior: `submenu` starts **visible** (no `display: none`), header click collapses it.
- **Element names from spec §6** (chat shell seam): `chat-feed`, `chat-input`, `chat-send`.
- Palette (must match spec): `--bg-top:#2f6bff`, `--bg-bottom:#3d1ad4`, `--ink:#191a3a`, `--yellow:#ffc821`, `--yellow-hi:#ffd95c`, `--white:#ffffff`, `--white-dim:#e8ecff`, `--red:#ff4d3d`, `--red-hi:#ff6f5e`, `--panel:rgba(19,18,45,.85)`, `--text-muted:#7a7a99`, `--online:#4cdd88`.
- Backdrop is **static** (no animation, no 3D scene). No textures/images — all USS shapes.
- Font license file MUST be committed next to the TTF.
- No dotnet builds needed (no C#/Shared changes). Unity Editor is the verification surface.
- The `TESTING-UNITY.md` checklist (AGENTS.md convention) is gitignored — never committed.

---

## File Structure

| File | Responsibility | Action |
|---|---|---|
| `client/Unity/Assets/Fonts/Baloo2-Variable.ttf` | Display font (titles + buttons) | Create (download) |
| `client/Unity/Assets/Fonts/OFL.txt` | Font license | Create (download) |
| `client/Unity/Assets/UI/SlopArena.uss` | Design tokens + shared cartoon restyle + new component classes | Modify (replace top chunk) |
| `client/Unity/Assets/UI/MainMenu.uxml` | New S3-v2 layout, names preserved | Overwrite |
| `TESTING-UNITY.md` (repo root) | Unity playtest checklist (gitignored) | Create |

Task order is strictly sequential: font → USS → UXML → in-editor pass → playtest. Later tasks depend on earlier ones' artifacts (font path, class names, element names).

---

### Task 1: Bundle the display font

**Files:**
- Create: `client/Unity/Assets/Fonts/Baloo2-Variable.ttf`
- Create: `client/Unity/Assets/Fonts/OFL.txt`

**Interfaces:**
- Consumes: nothing (network fetch from google/fonts).
- Produces: `Assets/Fonts/Baloo2-Variable.ttf` — the exact path referenced by every `-unity-font: url(...)` in Task 2.

- [ ] **Step 1: Create the Fonts directory**

Run:
```bash
mkdir -p client/Unity/Assets/Fonts
```
Expected: no error; directory exists.

- [ ] **Step 2: Download Baloo 2 variable TTF + OFL license**

Run:
```bash
curl -L -o client/Unity/Assets/Fonts/Baloo2-Variable.ttf \
  "https://raw.githubusercontent.com/google/fonts/main/ofl/baloo2/Baloo2%5Bwght%5D.ttf"
curl -L -o client/Unity/Assets/Fonts/OFL.txt \
  "https://raw.githubusercontent.com/google/fonts/main/ofl/baloo2/OFL.txt"
```
Expected: both files exist, non-empty.

- [ ] **Step 3: Verify downloads**

Run:
```bash
ls -la client/Unity/Assets/Fonts/
head -3 client/Unity/Assets/Fonts/OFL.txt
```
Expected: TTF ~100–300 KB; OFL.txt starts with the SIL Open Font License text ("Copyright ... SIL Open Font License").

- [ ] **Step 4: Commit**

```bash
git add client/Unity/Assets/Fonts/
git commit -m "chore(ui): bundle Baloo 2 display font (OFL)"
```
(Unity generates `.meta` files on first import — if `.meta` files appear later, commit them in Task 4's commit.)

---

### Task 2: USS design tokens + shared cartoon restyle

**Files:**
- Modify: `client/Unity/Assets/UI/SlopArena.uss` — replace the entire top chunk (from the `/* ── Screen root` comment through the end of the `.btn-back:hover` rule) with the block below. The chunks after (`/* ── Lobby ── */` onward) stay untouched this task.

**Interfaces:**
- Consumes: `Assets/Fonts/Baloo2-Variable.ttf` (Task 1).
- Produces: the CSS class contract Task 3's UXML depends on — `.screen`, `.backdrop`, `.bkg-*`, `.header`, `.title-wrap`, `.title`/`.title--face`/`.title--shadow1..3`, `.title-stripes`, `.status-area`, `.status-pill`/`--online`/`--host`, `.center-col`, `.btn-hero`, `.hero-tagline`, `.chat-panel`, `.chat-header`, `.chat-title`, `.chat-online`, `.chat-feed`, `.chat-msg`, `.chat-input-row`, `.chat-send`, `.mp-panel`, `.mp-header`, `.mp-body`, `.menu-item`/`--join`, `.join-row`, `.ip-input`, `.version-pill`, plus restyled `.btn-primary`, `.btn-secondary`, `.btn-back`.

- [ ] **Step 1: Replace the top chunk of SlopArena.uss**

The chunk to replace starts at the line `/* ── Screen root ─────────────────────────────────────────── */` and ends at the `.btn-back:hover { ... }` rule (inclusive) — everything before `/* ── Lobby ───────────────────────────────────────────────── */`. Replace it with exactly:

```css
/* ── Screen root ─────────────────────────────────────────── */

.screen {
    flex-grow: 1;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    position: relative;
    overflow: hidden;
    background-color: #191a3a;
    background-image: linear-gradient(180deg, var(--bg-top) 0%, var(--bg-bottom) 100%);
    padding: 40px;
}

/* ── Design tokens ─────────────────────────────────────────
   Palette locked in spec v2. If `:root` fails to resolve
   (USS parse warning), move this block to `.screen`.       */

:root {
    --bg-top:       #2f6bff;
    --bg-bottom:    #3d1ad4;
    --ink:          #191a3a;
    --yellow:       #ffc821;
    --yellow-hi:    #ffd95c;
    --white:        #ffffff;
    --white-dim:    #e8ecff;
    --red:          #ff4d3d;
    --red-hi:       #ff6f5e;
    --panel:        rgba(19, 18, 45, 0.85);
    --text-muted:   #7a7a99;
    --online:       #4cdd88;
}

/* ── Display font (shared by all chunky text) ────────────── */

.title,
.btn-hero,
.menu-item,
.mp-header,
.chat-title,
.version-pill,
.btn-primary,
.btn-secondary {
    -unity-font: url("project://database/Assets/Fonts/Baloo2-Variable.ttf");
}

/* ── Backdrop — static scene (spec §5) ───────────────────── */

.backdrop {
    position: absolute;
    left: 0;
    right: 0;
    top: 0;
    bottom: 0;
    overflow: hidden;
}

.bkg-sun {
    position: absolute;
    top: 48px;
    right: 12%;
    width: 120px;
    height: 120px;
    border-radius: 50%;
    background-color: var(--yellow);
    box-shadow: 0 0 60px rgba(255, 200, 33, 0.7);
}

.bkg-cloud {
    position: absolute;
    border-radius: 40px;
    background-color: rgba(255, 255, 255, 0.55);
}

.bkg-cloud--1 { top: 64px;  left: 10%; width: 200px; height: 44px; }
.bkg-cloud--2 { top: 140px; left: 34%; width: 140px; height: 34px; }

.bkg-hill {
    position: absolute;
    border-radius: 50% 50% 0 0;
}

.bkg-hill--left  { bottom: 0; left: -140px; width: 620px; height: 320px; background-color: #1d4fbf; }
.bkg-hill--right { bottom: 0; right: -180px; width: 760px; height: 400px; background-color: #1a44a8; }

.bkg-platform {
    position: absolute;
    background-color: var(--white-dim);
    border-width: 3px;
    border-color: var(--ink);
    border-radius: 14px;
    box-shadow: 0 5px 0 var(--ink), inset 0 -8px 0 rgba(25, 26, 58, 0.12);
}

.bkg-platform--main  { bottom: 96px;  left: 50%; margin-left: -230px; width: 460px; height: 34px; }
.bkg-platform--left  { bottom: 180px; left: 8%;  width: 120px; height: 22px; transform: rotate(-6deg); }
.bkg-platform--right { bottom: 180px; right: 8%; width: 120px; height: 22px; transform: rotate(6deg); }

.bkg-fighter {
    position: absolute;
    bottom: 130px;
    width: 26px;
    height: 34px;
    border-width: 3px;
    border-color: var(--ink);
    border-radius: 8px 8px 4px 4px;
}

.bkg-fighter--1 { background-color: var(--red);    left: 50%; margin-left: -46px; }
.bkg-fighter--2 { background-color: #2f6bff;       left: 50%; margin-left: -8px;  }
.bkg-fighter--3 { background-color: var(--yellow); left: 50%; margin-left: 30px;  }

.bkg-texture {
    position: absolute;
    left: 0;
    right: 0;
    top: 0;
    bottom: 0;
    opacity: 0.06;
    background-image: repeating-linear-gradient(45deg, #ffffff 0 2px, transparent 2px 12px);
}

/* ── Header: logo + status ───────────────────────────────── */

.header {
    position: absolute;
    left: 0;
    right: 0;
    top: 0;
    padding: 28px 40px 0 40px;
    flex-direction: row;
    justify-content: space-between;
    align-items: flex-start;
}

.title-wrap {
    position: relative;
}

.title {
    position: absolute;
    left: 0;
    top: 0;
    font-size: 64px;
    letter-spacing: 2px;
    color: var(--white);
    -unity-text-align: middle-left;
}

.title--shadow3 { color: var(--ink); transform: translate(6px, 6px); }
.title--shadow2 { color: var(--ink); transform: translate(4px, 4px); }
.title--shadow1 { color: var(--ink); transform: translate(2px, 2px); }
.title--face    { position: relative; }

.title-stripes {
    width: 260px;
    height: 10px;
    margin-top: 12px;
    border-radius: 5px;
    border-width: 2px;
    border-color: var(--ink);
    background-image: repeating-linear-gradient(45deg, var(--yellow) 0 14px, var(--red) 14px 28px);
}

.status-area {
    flex-direction: row;
    align-items: center;
}

.status-pill {
    background-color: var(--panel);
    border-width: 2px;
    border-color: var(--ink);
    border-radius: 10px;
    padding: 6px 14px;
    color: var(--white-dim);
    font-size: 11px;
    letter-spacing: 1px;
    -unity-text-align: middle-center;
    margin-left: 10px;
}

.status-pill--online { color: var(--online); }
.status-pill--host   { color: var(--white-dim); }

/* ── Center: TRAINING hero ───────────────────────────────── */

.center-col {
    position: absolute;
    left: 50%;
    top: 44%;
    width: 340px;
    margin-left: -170px;
    flex-direction: column;
    align-items: center;
}

.btn-hero {
    width: 320px;
    height: 64px;
    border-radius: 16px;
    border-width: 3px;
    border-color: var(--ink);
    background-color: var(--yellow);
    color: var(--ink);
    font-size: 20px;
    letter-spacing: 4px;
    -unity-text-align: middle-center;
    box-shadow: 0 5px 0 var(--ink);
    transition-duration: 0.12s;
    transition-property: background-color, box-shadow, translate;
}

.btn-hero:hover  { background-color: var(--yellow-hi); translate: 0 -2px; box-shadow: 0 7px 0 var(--ink); }
.btn-hero:active { translate: 0 3px; box-shadow: 0 2px 0 var(--ink); }

.hero-tagline {
    margin-top: 14px;
    color: rgba(255, 255, 255, 0.85);
    font-size: 12px;
    letter-spacing: 2px;
    -unity-text-align: middle-center;
}

/* ── Overlay panels: chat (left) + multiplayer (right) ───── */

.chat-panel,
.mp-panel {
    position: absolute;
    bottom: 36px;
    flex-direction: column;
    background-color: var(--panel);
    border-width: 3px;
    border-color: var(--ink);
    border-radius: 14px;
    padding: 14px;
    box-shadow: 0 5px 0 rgba(0, 0, 0, 0.35);
}

.chat-panel { left: 36px; width: 320px; }
.mp-panel   { right: 36px; width: 300px; }

.chat-header {
    flex-direction: row;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 8px;
}

.chat-title {
    color: var(--yellow);
    font-size: 13px;
    letter-spacing: 2px;
}

.chat-online {
    color: var(--text-muted);
    font-size: 10px;
    letter-spacing: 1px;
}

.chat-feed {
    height: 120px;
    overflow: hidden;
    background-color: rgba(255, 255, 255, 0.07);
    border-radius: 8px;
    padding: 8px;
    flex-direction: column;
}

.chat-msg {
    font-size: 11px;
    color: var(--white-dim);
    white-space: normal;
    margin-bottom: 4px;
}

.chat-input-row {
    flex-direction: row;
    align-items: center;
    margin-top: 8px;
}

.chat-input-row .ip-input {
    flex-grow: 1;
    margin-right: 8px;
}

.chat-send {
    width: 44px;
    height: 36px;
    border-radius: 8px;
    border-width: 2px;
    border-color: var(--ink);
    background-color: var(--yellow);
    color: var(--ink);
    font-size: 14px;
    -unity-text-align: middle-center;
}

.chat-send:hover { background-color: var(--yellow-hi); }

.mp-header {
    height: 36px;
    background-color: rgba(255, 255, 255, 0.06);
    border-radius: 8px;
    border-width: 0;
    color: var(--yellow);
    font-size: 14px;
    letter-spacing: 2px;
    -unity-text-align: middle-center;
}

.mp-header:hover { background-color: rgba(255, 255, 255, 0.12); }

.mp-body {
    flex-direction: column;
    margin-top: 10px;
}

.menu-item {
    width: 100%;
    height: 44px;
    margin-top: 4px;
    margin-bottom: 4px;
    border-radius: 12px;
    border-width: 3px;
    border-color: var(--ink);
    background-color: var(--white);
    color: var(--ink);
    font-size: 13px;
    letter-spacing: 2px;
    -unity-text-align: middle-center;
    box-shadow: 0 4px 0 var(--ink);
    transition-duration: 0.12s;
    transition-property: background-color, box-shadow, translate;
}

.menu-item:hover  { background-color: var(--white-dim); translate: 0 -2px; box-shadow: 0 6px 0 var(--ink); }
.menu-item:active { translate: 0 2px; box-shadow: 0 2px 0 var(--ink); }

.menu-item--join {
    background-color: var(--red);
    color: var(--white);
}

.menu-item--join:hover { background-color: var(--red-hi); }

.join-row {
    flex-direction: row;
    align-items: center;
    margin-top: 6px;
}

.join-row .menu-item--join {
    flex-grow: 1;
    margin-right: 8px;
}

.join-row .ip-input {
    flex-grow: 1;
}

.ip-input {
    height: 36px;
    border-radius: 8px;
    border-width: 2px;
    border-color: var(--ink);
    background-color: var(--white);
    color: var(--ink);
    font-size: 11px;
    padding-left: 10px;
    padding-right: 10px;
}

.ip-input:focus {
    border-color: var(--yellow);
    box-shadow: 0 0 0 3px rgba(255, 200, 33, 0.4);
}

/* ── Version pill ────────────────────────────────────────── */

.version-pill {
    position: absolute;
    left: 50%;
    bottom: 36px;
    margin-left: -50px;
    width: 100px;
    background-color: var(--ink);
    color: var(--yellow);
    font-size: 10px;
    letter-spacing: 2px;
    padding: 6px 0;
    border-radius: 12px;
    -unity-text-align: middle-center;
}

/* ── Shared action buttons (other screens inherit) ───────── */

.btn-primary {
    width: 240px;
    height: 52px;
    margin-top: 24px;
    border-radius: 14px;
    border-width: 3px;
    border-color: var(--ink);
    background-color: var(--yellow);
    color: var(--ink);
    font-size: 14px;
    letter-spacing: 3px;
    -unity-text-align: middle-center;
    box-shadow: 0 4px 0 var(--ink);
    transition-duration: 0.12s;
    transition-property: background-color, box-shadow, translate;
}

.btn-primary:hover  { background-color: var(--yellow-hi); translate: 0 -2px; box-shadow: 0 6px 0 var(--ink); }
.btn-primary:active { translate: 0 2px; box-shadow: 0 2px 0 var(--ink); }

.btn-primary:disabled {
    background-color: #4a4a66;
    border-color: #4a4a66;
    color: var(--text-muted);
    box-shadow: none;
}

.btn-secondary {
    width: 240px;
    height: 52px;
    border-radius: 14px;
    border-width: 3px;
    border-color: var(--ink);
    background-color: var(--white);
    color: var(--ink);
    font-size: 14px;
    letter-spacing: 3px;
    -unity-text-align: middle-center;
    box-shadow: 0 4px 0 var(--ink);
    transition-duration: 0.12s;
    transition-property: background-color, box-shadow, translate;
}

.btn-secondary:hover  { background-color: var(--white-dim); translate: 0 -2px; box-shadow: 0 6px 0 var(--ink); }
.btn-secondary:active { translate: 0 2px; box-shadow: 0 2px 0 var(--ink); }

.btn-back {
    position: absolute;
    top: 24px;
    left: 24px;
    width: 100px;
    height: 36px;
    border-radius: 10px;
    border-width: 2px;
    border-color: rgba(255, 255, 255, 0.6);
    background-color: rgba(0, 0, 0, 0);
    color: var(--white-dim);
    font-size: 12px;
    -unity-text-align: middle-center;
}

.btn-back:hover {
    background-color: var(--white);
    color: var(--ink);
}
```

Notes: the old `.menu-list`, `.submenu` (class), `.menu-item--active`, `.menu-item--sub`, and `.subtitle` rules are intentionally deleted (only MainMenu used them; the new UXML uses the new classes). If `gap`/multi-`box-shadow`/`translate` transitions warn in the Unity console, they degrade gracefully — leave them.

- [ ] **Step 2: Verify the file structure**

Run:
```bash
grep -c ":root" client/Unity/Assets/UI/SlopArena.uss
grep -n "Baloo2-Variable" client/Unity/Assets/UI/SlopArena.uss
grep -c "var(--ink)" client/Unity/Assets/UI/SlopArena.uss
```
Expected: `1` for `:root`; 8 `Baloo2-Variable` occurrences (title, btn-hero, menu-item, mp-header, chat-title, version-pill, btn-primary, btn-secondary); `var(--ink)` appears many times.

- [ ] **Step 3: Optional compile gate (only if the Unity Editor is closed)**

Run:
```bash
"$UNITY_EDITOR" -batchmode -quit -projectPath client/Unity -logFile - | grep -iE "error|UXML|USS" || echo "no import errors"
```
Expected: no `error` lines mentioning UXML/USS. Skip if the Editor is open (Library lock) — in-editor verification in Task 4 covers it.

- [ ] **Step 4: Commit**

```bash
git add client/Unity/Assets/UI/SlopArena.uss
git commit -m "feat(ui): restyle shared USS to cartoon language (S3-v2)"
```

---

### Task 3: Rebuild MainMenu.uxml (S3-v2 layout)

**Files:**
- Overwrite: `client/Unity/Assets/UI/MainMenu.uxml`

**Interfaces:**
- Consumes: the class contract from Task 2; font path from Task 1.
- Produces: the element-name contract `MainMenuController` binds to (unchanged list) — do NOT rename anything.

- [ ] **Step 1: Overwrite MainMenu.uxml with the complete file**

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
    <ui:Style src="SlopArena.uss" />
    <ui:VisualElement name="root" class="screen">

        <!-- Static backdrop scene (spec §5) -->
        <ui:VisualElement name="backdrop" class="backdrop">
            <ui:VisualElement class="bkg-sun" />
            <ui:VisualElement class="bkg-cloud bkg-cloud--1" />
            <ui:VisualElement class="bkg-cloud bkg-cloud--2" />
            <ui:VisualElement class="bkg-hill bkg-hill--left" />
            <ui:VisualElement class="bkg-hill bkg-hill--right" />
            <ui:VisualElement class="bkg-platform bkg-platform--main" />
            <ui:VisualElement class="bkg-platform bkg-platform--left" />
            <ui:VisualElement class="bkg-platform bkg-platform--right" />
            <ui:VisualElement class="bkg-fighter bkg-fighter--1" />
            <ui:VisualElement class="bkg-fighter bkg-fighter--2" />
            <ui:VisualElement class="bkg-fighter bkg-fighter--3" />
            <ui:VisualElement class="bkg-texture" />
        </ui:VisualElement>

        <!-- Header: extruded logo + status pills -->
        <ui:VisualElement name="header" class="header">
            <ui:VisualElement name="title-wrap" class="title-wrap">
                <ui:Label text="SLOPARENA" class="title title--shadow3" />
                <ui:Label text="SLOPARENA" class="title title--shadow2" />
                <ui:Label text="SLOPARENA" class="title title--shadow1" />
                <ui:Label name="title" text="SLOPARENA" class="title title--face" />
                <ui:VisualElement class="title-stripes" />
            </ui:VisualElement>
            <ui:VisualElement name="status-area" class="status-area">
                <ui:Label name="online-count" text="● 1,204 ONLINE" class="status-pill status-pill--online" />
                <ui:Label name="lbl-host-status" text="" class="status-pill status-pill--host" style="display: none;" />
            </ui:VisualElement>
        </ui:VisualElement>

        <!-- Center: TRAINING hero -->
        <ui:VisualElement name="center" class="center-col">
            <ui:Button name="btn-training" text="TRAINING MODE" class="btn-hero" />
            <ui:Label text="SOLO PRACTICE · ANY STAGE · NO PRESSURE" class="hero-tagline" />
        </ui:VisualElement>

        <!-- Chat panel — UI shell only (spec §6) -->
        <ui:VisualElement name="chat-panel" class="chat-panel">
            <ui:VisualElement class="chat-header">
                <ui:Label text="# GLOBAL CHAT" class="chat-title" />
                <ui:Label name="chat-online" text="1,204 online" class="chat-online" />
            </ui:VisualElement>
            <ui:VisualElement name="chat-feed" class="chat-feed">
                <ui:Label text="BonkLord: gg wp anyone up for 2v2?" class="chat-msg" />
                <ui:Label text="slopqueen: ranked when??" class="chat-msg" />
                <ui:Label text="dude420: host is up, join fast" class="chat-msg" />
            </ui:VisualElement>
            <ui:VisualElement class="chat-input-row">
                <ui:TextField name="chat-input" value="" placeholdertext="type here…" class="ip-input" />
                <ui:Button name="chat-send" text="➜" class="chat-send" />
            </ui:VisualElement>
        </ui:VisualElement>

        <!-- Multiplayer panel — element name "submenu" kept so the
             controller's collapse toggle keeps working. Starts VISIBLE
             (behavior change vs old hidden submenu). -->
        <ui:VisualElement class="mp-panel">
            <ui:Button name="btn-multiplayer" text="MULTIPLAYER" class="mp-header" />
            <ui:VisualElement name="submenu" class="mp-body">
                <ui:Button name="btn-serverbrowser" text="BROWSE SERVERS" class="menu-item" />
                <ui:Button name="btn-host" text="HOST GAME" class="menu-item" />
                <ui:VisualElement name="host-row" class="join-row">
                    <ui:TextField name="host-ip-field" value="" placeholdertext="Public IP or domain (empty = auto)" class="ip-input" />
                </ui:VisualElement>
                <ui:VisualElement name="join-row" class="join-row">
                    <ui:Button name="btn-join" text="JOIN GAME" class="menu-item menu-item--join" />
                    <ui:TextField name="ip-field" value="127.0.0.1" class="ip-input" />
                </ui:VisualElement>
            </ui:VisualElement>
        </ui:VisualElement>

        <!-- Version -->
        <ui:Label text="v0.1 DEMO" class="version-pill" />
    </ui:VisualElement>
</ui:UXML>
```

- [ ] **Step 2: Verify every controller-queried name exists exactly once**

Run:
```bash
for n in submenu btn-training btn-multiplayer btn-host btn-join ip-field host-ip-field btn-serverbrowser lbl-host-status title; do
  c=$(grep -o "name=\"$n\"" client/Unity/Assets/UI/MainMenu.uxml | wc -l)
  echo "$n: $c"
done
```
Expected: each name prints `1`. Any `0` or `>1` is a bug — fix before continuing.

Also verify the chat-seam names:
```bash
grep -o 'name="chat-[a-z]*"' client/Unity/Assets/UI/MainMenu.uxml
```
Expected: `chat-panel`, `chat-feed`, `chat-input`, `chat-send` (plus `chat-online`).

- [ ] **Step 3: Optional compile gate (only if Editor is closed)**

Same command as Task 2 Step 3. Expected: no UXML/USS errors.

- [ ] **Step 4: Commit**

```bash
git add client/Unity/Assets/UI/MainMenu.uxml
git commit -m "feat(ui): rebuild main menu layout (S3-v2 composition)"
```

---

### Task 4: In-editor visual pass

**Files:**
- Modify (only if needed): `client/Unity/Assets/UI/SlopArena.uss` (font swap or shape tuning), `client/Unity/Assets/UI/MainMenu.uxml` (layout tuning the user requests)
- Commit: `client/Unity/Assets/Fonts/*.meta` if Unity generated them

**Interfaces:**
- Consumes: everything from Tasks 1–3.

- [ ] **Step 1: Open the MainMenu in Unity**

Open the Unity Editor (main repo), load the MainMenu scene (the scene hosting the MainMenu UIDocument — find it via the controller's `_uiDocument` reference; usually the menu/bootstrap scene), enter Play mode (or use the UI Toolkit runtime preview). Check the Unity Console (via the Editor or `Unity_GetConsoleLogs` through the gamedev MCP at `localhost:26356/mcp`) for USS/UXML warnings.

Expected: no errors; warning-free import of `SlopArena.uss`.

- [ ] **Step 2: Acceptance checklist (spec §9)**

Verify visually:
- Backdrop renders: gradient sky, sun, clouds, hills, platforms, 3 fighter silhouettes, subtle texture. Static (no motion).
- Extruded "SLOPARENA": white face with ink layers below (3 offset layers) + yellow/red stripes bar.
- Status pills top-right (online green + hidden host label), version pill bottom-center.
- TRAINING MODE: big yellow hero, centered; hover lifts (translate up + lip grows), press squishes (lip shrinks).
- Multiplayer panel right: header toggles collapse (click MULTIPLAYER header → body hides/shows); BROWSE SERVERS / HOST GAME / JOIN GAME (+IP field) visible; host-IP field under HOST GAME.
- Chat panel left: header, 3 placeholder messages, input + send button.
- Other screens (Lobby, ServerBrowser, CharSelect, StageSelect) still load without breaking (they inherit the restyled `.screen`/buttons — layout changes are expected and acceptable; pixel polish is follow-up scope).

- [ ] **Step 3: Font weight check (variable-font risk)**

If the title/buttons render at the wrong weight (e.g., Regular instead of ExtraBold, thin look), swap to the static fallback (spec §3):

```bash
curl -L -o client/Unity/Assets/Fonts/LuckiestGuy-Regular.ttf \
  "https://raw.githubusercontent.com/google/fonts/main/ofl/luckiestguy/LuckiestGuy-Regular.ttf"
curl -L -o client/Unity/Assets/Fonts/LuckiestGuy-LICENSE.txt \
  "https://raw.githubusercontent.com/google/fonts/main/ofl/luckiestguy/LICENSE" || true
sed -i 's/Baloo2-Variable/LuckiestGuy-Regular/g' client/Unity/Assets/UI/SlopArena.uss
```
(If the `LICENSE` fetch 404s, list the dir: `https://github.com/google/fonts/tree/main/ofl/luckiestguy` — use whatever license file is there.) Delete `Baloo2-Variable.ttf` + `OFL.txt` only if the swap is accepted. Re-run Step 2.

- [ ] **Step 4: Optional title float (spec §3, only if it works)**

Try a gentle idle float on `.title--face` with USS `@keyframes` (Unity 6). Add to `SlopArena.uss`:

```css
@keyframes title-float {
    from { transform: translate(0px, 0px); }
    to   { transform: translate(0px, -6px); }
}

.title--face {
    animation-name: title-float;
    animation-duration: 3s;
    animation-direction: alternate;
    animation-iteration-count: infinite;
    animation-timing-function: ease-in-out;
}
```

If the Console reports unsupported properties or nothing animates, **remove the block** (drop the feature — not a release blocker). Do not attempt the C# fallback in this phase.

- [ ] **Step 5: Tune + commit**

Move/resize backdrop shapes or panel positions per the user's feedback ("user will move things around" — spec §11). Then:

```bash
git add -A client/Unity/Assets/UI/ client/Unity/Assets/Fonts/
git commit -m "style(ui): tune main menu visuals in editor"
```

---

### Task 5: Playtest + Unity checklist

**Files:**
- Create: `TESTING-UNITY.md` (repo root, gitignored — never committed)

**Interfaces:**
- Consumes: completed Tasks 1–4.

- [ ] **Step 1: Run the four flows in Play mode**

1. **TRAINING MODE** → loads CharSelect with Training config (MatchConfig.Mode=Training, IsHost=true).
2. **MULTIPLAYER header** → toggles the panel body collapsed/expanded; starts expanded.
3. **HOST GAME** → host-and-play flow starts (embedded server subprocess); `lbl-host-status` becomes visible with status text; `host-ip-field` value is used (empty = auto).
4. **JOIN GAME** → uses `ip-field` value (default 127.0.0.1), joins.
5. **BROWSE SERVERS** → loads the ServerBrowser scene.

Expected: all five work identically to before the redesign (no C# changed, so regressions here mean a UXML wiring mistake).

- [ ] **Step 2: Write TESTING-UNITY.md**

```markdown
# Test in Unity — MainMenu cartoon redesign (S3-v2)

## Visual checklist
- [ ] Backdrop: gradient sky, sun, clouds, hills, platforms, 3 fighters, texture — static
- [ ] Extruded SLOPARENA title + stripes bar
- [ ] Status pills top-right (online green, host label appears after hosting)
- [ ] Version pill bottom-center
- [ ] TRAINING hero: yellow, big, hover lift, press squish
- [ ] Multiplayer panel: header collapse toggle, BROWSE/HOST/JOIN + IP fields
- [ ] Chat panel: header, placeholder messages, input + send
- [ ] Other screens load (Lobby, ServerBrowser, CharSelect, StageSelect)

## Flow checklist
- [ ] TRAINING → CharSelect
- [ ] HOST GAME → host-and-play starts, status label shows
- [ ] JOIN GAME (default + custom IP)
- [ ] BROWSE SERVERS → ServerBrowser scene
- [ ] Multiplayer header collapse

## Known follow-ups (not this phase)
- Global chat backend (master-server SignalR hub) — separate spec
- Online-count pill is static placeholder
- Other screens' per-screen cartoon polish
```

- [ ] **Step 3: No commit** — `TESTING-UNITY.md` is gitignored by design. If playtest uncovered real bugs, fix them in a follow-up commit before finishing.

---

## Self-Review (done by plan author)

- **Spec coverage:** §4 element map → Task 3; §5 backdrop → Tasks 2+3; §6 chat shell names → Task 3; §3 palette/font/motion → Task 2 (+Task 4 optional float); §8 files → Tasks 1–5; §9 verification → Tasks 3–5; §10 non-goals respected (no C#, no backend, no other screens, static backdrop); online-count placeholder pinned → Task 3. No gaps.
- **Placeholder scan:** every code step carries full code or exact commands; no TBD/TODO.
- **Type consistency:** font path `Assets/Fonts/Baloo2-Variable.ttf` is identical in Task 1 (download), Task 2 (8 USS `-unity-font` urls), Task 4 (sed swap target). Class names in Task 3's UXML all exist in Task 2's USS block (`.title--face`, `.menu-item--join`, `.chat-input-row`, etc.). Element names match the controller list exactly. `chat-feed`/`chat-input`/`chat-send` match spec §6.
