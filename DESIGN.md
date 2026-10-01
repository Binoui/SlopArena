---
name: SlopArena
description: Bold DIY graphics for an unreleased low-poly underground toy-fight-club.
colors:
  ink: "#17131B"
  deep-ink: "#0D0D14"
  paper: "#F0EEE8"
  action-yellow: "#FFCC22"
  combat-orange: "#E05C2A"
  online-green: "#4CDD88"
  utility-surface: "#24202B"
  utility-border: "#625C6B"
  highlight: "#FFFFFF"
typography:
  display:
    fontFamily: "Archivo Black"
    fontWeight: 700
    letterSpacing: "-4px"
  label:
    fontFamily: "Unity UI default sans-serif"
    fontSize: "12px"
    fontWeight: 700
    lineHeight: 1.1
    letterSpacing: "2px"
rounded:
  none: "0px"
spacing:
  sm: "8px"
  md: "12px"
  lg: "18px"
components:
  button-primary:
    backgroundColor: "{colors.action-yellow}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "18px"
    height: "56px"
  button-hot:
    backgroundColor: "{colors.action-yellow}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "18px"
    height: "56px"
  status-pill:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.paper}"
    rounded: "{rounded.none}"
    padding: "8px 12px"
---

# Design System: SlopArena

**Authority and status:** [Visual Language](docs/design/visual-language.md) owns the shared identity; [Art and Asset Conventions](docs/contributing/conventions.md) own 3D presentation and assets. This document specifies the native approved target, mirrored by `.impeccable/design.json` and its surface brief. The unreleased solo-developed game is not on Steam. The player-facing identity rollout is implemented across Home, shared shell, fighter/stage selection, Results, browser/lobby, Settings, pause/Training controls, chat, and entry/confirmation dialogs. HUD neutral chrome is harmonized without changing geometry or functional colors. Creator/Ability Lab tools, website migration, connected-room acceptance, and release-copy changes are not claimed.

## Overview

**Creative North Star: "Low-poly underground toy-fight-club, with DIY internet-game energy."**

SlopArena's interface and imagery feel like an improvised fighting world assembled from scavenged pieces: chunky low-poly forms, exaggerated silhouettes, and a curated collage of assets. “Toy” describes that construction metaphor, not a requirement for literal plastic or cute styling. Slightly crappy on purpose, but extremely readable.

**Cool first, joke second.** Treat the website as the strongest existing graphic reference, not a fully approved implementation. Use authored asymmetry, hard edges, and controlled print offsets to create personality without compromising stable interaction geometry or clear player actions.

**Approved visual reference:** inspect the [offline board's interface and type specimens](docs/design/references/sloparena-visual-identity/visual-reference-board.html), approved on 2026-09-30. Use the aligned controls, hard press depth, clear selection/readiness treatment, and declaration/annotation/information hierarchy as target-treatment evidence. Its tab labels, layout, timings, and starter neutral/orange shades are illustrative—not approved navigation changes, replacement tokens, or shipped Unity UI. [Shared reference boundaries](docs/design/visual-language.md#approved-visual-board--rough-edges-clear-decisions) govern what to copy. This reference does not make the identity migration a release prerequisite.

**Key Characteristics:**
- Underground fight-club energy with DIY internet-game construction.
- Chunky low-poly silhouettes and curated asset-pack collage, harmonized through scale, lighting, material response, palette/value hierarchy, and composition—not a compulsory universal shader.
- Bold Archivo Black name mark and target display voice, with readable utility type.
- Character forms remain recognizable and gameplay-readable.
- Dry, self-aware copy never obscures an action or status.

## Colors

The palette uses local paper and ink values, with Action Yellow reserved for primary actions and selection. Combat Orange remains a local accent; do not replace it with a shared orange value.

### Primary
- **Action Yellow** (`#FFCC22`): Graphic primary actions and selection emphasis.
- **Combat Orange** (`#E05C2A`): Native local impact and expressive emphasis.

### Secondary
- **Online Green** (`#4CDD88`): Live/online status only. Pair with text or structure; never make it the sole status channel.

### Neutral
- **Paper** (`#F0EEE8`): Frontend canvas/header and light control/card fields; readable text on dark panels.
- **Ink** (`#1B1A18`): Dark text/framing and calm recessed regions; never globally inverted to make a light page.
- **Deep Ink** (`#161513`): Recessed fields, hard backing, and neutral HUD outlines.
- **Utility Surface** (`#252422`): Dense utility fields and secondary controls.
- **Utility Border** (`#7B7872`): Quiet boundaries and inactive structure.
- **Highlight** (`#FFFFFF`): Hover/focus lift only; do not replace Paper as the default light field.

**Implemented paper/ink roles:** The frontend is paper-led, with the user-authored `Assets/UI/FrontendPaperBackground.png` behind every shell page. Shared `--shell-page`, page-ink/muted/warning/error roles distinguish the outer surface from dark content fields. Existing `--shell-canvas` retains exact ink `#1B1A18`; surface/recess/rule remain `#252422`, `#161513`, `#7B7872`. Paper controls remain `#F0EEE8`, Action Yellow `#FFCC22`. Settings, chat, match overlays, and HUD keep their calm dark-field and functional-color semantics; 3D arenas are not wallpapered or recolored.

### Named Rules
**The One Accent Rule.** Action Yellow and Combat Orange may both exist on a screen, but one accent owns each region.

**The Paper-on-Ink Rule.** Small text and gameplay status belong on calm, high-contrast fields. Texture and decoration never carry critical information.

## Typography

**Display Font:** Archivo Black is implemented on player-facing menu, selection, outcome, Settings, pause, and dialog declarations/actions. Chat uses it only for short actions. Settings rows, message bodies, percentages, stocks, cooldowns, bindings, and other dense utility remain readable sans or quiet utility mono.
**Body Font:** Native utility sans-serif remains allowed, especially where readability or dense information benefits.
**Label/Mono Font:** Marketing pairs Archivo Black with Space Mono. Short native utility labels may use mono where practical; dense settings and HUD text should stay readable sans.

**Home and shared-shell treatment:** Mode buttons form the left column, original artwork sits fully visible in an aspect-fitted paper/ink frame at center, and announcements form the right column. The poster uses available page height without reserving chat space; two yellow attachment strips and a yellow announcement heading connect it to the printed world. The authored background continues behind the transparent top bar; individual navigation controls retain paper backing and an ink border for legibility over black marks, with yellow selected modes. Calm page-header fields remain. Native evidence: `.impeccable/review/paper-shell-unity-20261001/`, `.impeccable/review/selection-chat-header-20261001/`, and `.impeccable/review/chat-contrast-controls-20261001/`.

**Chat overlay rule:** Chat is an absolute user-sized bottom-left overlay in every scene. The frontend social host is a workspace sibling, not part of the page's lower row; gameplay was already absolute. Resize and hide never reserve page space or reposition the content underneath. Author selection pages with the bottom-left clear: fighter P1/P2, Player/CPU configuration, and stage participants/actions belong in the bottom middle/right. This is a fixed responsive layout, not a reaction to chat dimensions. User-enlarged overlays may still cover content; do not clamp their size or shrink Home's poster. Shared saved dimensions, channel/input ownership, modal guards, and viewport limits remain.

**Chat controls and contrast:** Paper outer frame, paper/ink channel tabs, dark message/input fields with paper rules, yellow active channel and available SEND. Unavailable SEND/server chat remain visibly disabled and readable. Hide and resize share one top-right row; maximize and its expanded-state/focus/host code are removed. Hidden chat's CHAT reopen button stays at the bottom-left. Conversation/mute management remains in the existing Direct view, without a second sidebar.

**Stage-select treatment:** Existing admitted preview assets and grid/scroll layout remain. Full-opacity previews have paper name strips; selected stages retain a yellow strip while keyboard focus uses a paper frame. Local participant paper fields and deeper green readiness text preserve state meaning and contrast. Back and confirmation use the shared physical control language. Stage-only styles now live together in `FrontendShell.uss`, not as competing overrides in two sheets.

**Results treatment:** Keep the winner-left/standings-right composition, actual snapshot bindings, ranking colors, portrait assets, and local/online return semantics. Archivo outcome/winner/player declarations and readable statistics take priority; empty bursts/grid, fake feed annotations, redundant eyebrow, and speculative broadcast footer were removed. No winner, score, duration, or shared-victory data is invented. Native evidence for both surfaces: `.impeccable/review/stage-results-20261001/`, including a recorded genuine Solo snapshot reused for before/after rendering.

**Online-flow treatment:** Preserve room discovery/create columns, roster order, actual membership/phase/capacity/IDs, host/leader gating, and network routes. Paper room/player name fields and form/dialog cards sit on near-neutral ink; Action Yellow owns create/join/continue actions. Room-mode name-only children are styled alongside legacy class-based rows. The address modal's mounted wrapper fills the shell without intercepting the page while closed; native scrolling keeps Editor-only tools reachable. Identity and frontend join confirmation share paper cards, dark fields, and physical controls. Native evidence: `.impeccable/review/online-flow-20261001/`. Steam was unavailable: actual connection failures and local interactions were checked, not connected-room admission. Explicitly labelled layout specimens are not live-room evidence.

**Settings, match overlays, and chat:** Settings now uses paper outer framing, ink Archivo title, bordered paper category tabs, yellow selection, and a quiet dark scroll/control field. Keyboard-focused tabs and Back use ink/paper contrast with a yellow frame; outer status text is ink, not yellow on paper. All five categories retain actual values, controls, scrolling, remap, reset, and display-revert behavior. Migrated controls out-specify optional local licensed-study images without editing those excluded assets. Training/Solo pause retains paper controls and yellow Resume; Training utility stays quieter ink. Gameplay join dialogs preserve input/modal/pause ownership. Chat retains channel/history/draft/mute/resize and hide/reopen behavior; maximize is removed. Native evidence: `.impeccable/review/remaining-ui-20261001/`, `.impeccable/review/settings-paper-20261001/`, and `.impeccable/review/chat-contrast-controls-20261001/`.

**Restrained HUD finish:** Only neutral text/background/outline/decorative edges changed. Billboard/portrait/diamond/slot/readout geometry, assets, rotations, animations, P1–P4 identity colors, damage-tier/target colors, living/lost stocks, and normal-orange/special-blue ability semantics remain. Ordinary utility typography remains; combat numbers are not turned into menu declarations.

**Character:** Heavy, blocky declarations paired with clear utility typography. Display type makes one statement at a time; utility type carries actions, status, and annotations.

### Hierarchy
- **Display** (Archivo Black target): Main title declaration; size and tracking are surface-specific, not a universal fixed 148px prescription.
- **Headline** (bold, screen-specific): Results, fighter names, and major state changes. Keep it materially larger than metadata.
- **Title** (bold, screen-specific): Section and flow titles such as Character Select or Lobby Room.
- **Body** (regular/bold, screen-specific): Short explanatory copy, status messages, and join instructions. Keep it brief and readable.
- **Label** (bold, `12px`, `2px` tracking, uppercase): Kicker text, status strips, player metadata, roster roles, and technical annotations.

### Named Rules
**The Declaration Rule.** Give each composition one loud phrase. Supporting labels are smaller, quieter, and more useful.

**The Joke Is Metadata Rule.** Humor belongs in annotations, status copy, or secondary labels. It never replaces the label for a required action.

## Layout

Screens begin with a stable rectangular grid: primary action zone, content or roster zone, status zone, and utility zone. The grid is then disrupted once or twice with controlled rotation, overlap, crop, or a burst shape. Keep interactive controls aligned even when surrounding poster elements are crooked.

The current Unity UI uses proportional screen anchoring for major regions, with fixed internal dimensions for buttons, HUD billboards, and slots. Common observed rhythm values are `8px`, `12px`, `14px`, and `18px`; dense HUD elements use tighter internal padding than menus. Gameplay HUD information stays stable while the camera moves: player billboards stack predictably, two adjacent ability diamonds stay centered at the lower edge, and overhead damage remains spatially attached to fighters.

Do not rotate dense paragraphs, settings controls, or critical status. Rotation belongs to decorative notes, tape labels, poster groups, and cutout material.

## Elevation & Depth

**Poster layering** is the depth model. SlopArena does not depend on soft ambient card shadows or glossy glass. Depth comes from a dark field, pasted paper blocks, thick borders, offset title shadows, clipped character portraits, overlapping bursts, and short state translations. At rest, surfaces are mostly flat; physicality appears through hard edges and deliberate misregistration.

### Shadow Vocabulary
- **Offset title shadow:** Hard orange or deep-ink duplicate behind a display declaration; creates poster print depth.
- **Structural bottom edge:** Thick bottom border on primary controls and HUD cards; makes controls feel stamped and pressable.
- **Outline layer:** Deep-ink text outline on damage and combat readouts; protects legibility over active gameplay.
- **Cutout overlap:** Portraits and character imagery may cross panel boundaries to provide depth without a soft shadow.

### Named Rules
**The Hard Edge Rule.** Use borders, offsets, and layers before blur, glow, or ambient shadow. Soft depth is an exception requiring a functional reason.

## Shapes

Corners are square by default (`0px`). Forms are rectangular, assertive, and constructed from borders rather than rounded containers. Use thick outlines, left or bottom accent bars, tape-like labels, circles or bursts as framing devices, and clipped character portraits.

Rotation is authored and restrained: typically under three degrees for grouped content, with larger angles reserved for decorative bursts or notes. Interactive controls use a strong dark border, a heavier bottom edge, and a short press translation. Dense HUD geometry may use tiny offsets and thin dividers, but its alignment remains stable.

## Components

### Buttons
- **Character:** Tactile and declarative; a button should feel stamped into the poster.
- **Shape:** Square corners, `2px` dark border, and a `6px` bottom edge on pre-match controls.
- **Primary:** Action Yellow background, Ink text, uppercase bold label; stable interaction geometry is more important than any fixed menu sizing.
- **Main-menu actions:** Training/Solo use paper controls; Online uses Action Yellow. Existing mode order, artwork, and routes remain.
- **Hot:** Host/join primary actions use Action Yellow; Combat Orange remains a gameplay/impact accent, not a competing primary-action field.
- **Hover / Focus:** Use clear field/frame contrast without moving the control upward. Paper stage focus remains distinct from yellow selection; Settings keyboard focus uses ink/paper with a yellow frame rather than disappearing into its paper outer panel.
- **Active:** Translate downward by about `3px` so press has a physical response.
- **Secondary:** Paper field with Ink text; retain the same border and geometry so hierarchy comes from color, not a different control language.

### Chips
- **Style:** Flat Ink or Paper strips with uppercase bold text, wide tracking, and a left accent bar when they represent status.
- **State:** Action Yellow indicates action/selection; Online Green indicates live presence; Combat Orange indicates hot or combat state. Add text or structural change so color is not the only cue.

### Cards / Containers
- **Corner Style:** Square.
- **Background:** Ink for broadcast/status panels; Paper for labels and light cutouts; Utility Surface for dense HUD slots.
- **Shadow Strategy:** Hard borders, offset layers, and structural accent edges. No default soft shadow.
- **Border:** `2px` to `4px` Deep Ink, with `5px` to `8px` accent edges for important cards.
- **Internal Padding:** `8px` to `18px` depending on density.

### Inputs / Fields
- **Style:** Rectangular, high-contrast, and compact. Keep the field readable against the panel rather than decorating the input itself.
- **Focus:** Action Yellow border or clear field lift; preserve the same focus treatment as buttons.
- **Error / Disabled:** Use explicit status copy and readable utility colors with inactive structure; do not make required disabled labels legible only through opacity.

### Navigation

Navigation stays stable: the current persistent Training/Solo/Online tabs and flow-specific Back/Leave paths are implementation evidence, not a decorative collage. Preserve predictable routes and active-session guards; this visual direction does not approve a navigation redesign. Labels remain short, uppercase, and explicit.

### HUD Billboards

Player identity, damage, and stocks live in stable left-side billboard cards with portrait cutouts, accent edges, and large outlined percentage readouts. The local kit uses an orange normal diamond (`1–4`) beside a blue special diamond (`A/E/R/F`) at the bottom center. Each has four circular slots ordered top, right, bottom, left, with an effective-binding prompt above each slot and cooldown feedback inside it. Physical number-row keys show digits even when the keyboard layout displays punctuation; other prompts follow effective labels. Controller specials show only the face button; actual gameplay input still requires LB. Unsupported glyphs fall back to text. Slot readiness uses brief feedback rather than constant decorative animation.

## Do's and Don'ts

### Do:
- **Do** establish the grid before adding crooked poster gestures.
- **Do** use Ink, Paper, Action Yellow, and Combat Orange as semantic roles rather than equal decoration.
- **Do** make focus, selected state, disabled state, and confirmation more obvious than texture.
- **Do** treat character renders as graphic material while preserving face, weapon, and gameplay silhouette.
- **Do** preserve actual model proportions, costume, weapon, signature colors, and chunky forms in splash art; amplification must not promise a glossy cinematic version of another game.
- **Do** keep gameplay HUD texture restrained and information stable while the camera moves.
- **Do** use short, honest, self-aware copy after the useful information.
- **Do** preserve readability for damage, stocks, cooldowns, player identity, and match flow.

### Don't:
- **Don't** use rounded cards, glassmorphism, soft ambient shadows, or glossy gradients as the default language.
- **Don't** rotate dense information, settings controls, or required actions.
- **Don't** let texture, jokes, or character art obscure gameplay state.
- **Don't** use color as the only indicator of player identity, readiness, error, or status.
- **Don't** turn every element into a sticker; one loud gesture is stronger than total disorder.
- **Don't** replace the fighting-game silhouette with generic esports aggression, cyberpunk neon, or meme overload.
