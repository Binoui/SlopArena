---
name: SlopArena
description: An underground fight-poster interface for a playful, deterministic 3D platform fighter.
colors:
  ink: "#17131B"
  deep-ink: "#0D0D14"
  paper: "#F0EEE8"
  acid: "#FFC821"
  combat-orange: "#E05C2A"
  online-green: "#4CDD88"
  utility-surface: "#24202B"
  utility-border: "#625C6B"
  highlight: "#FFFFFF"
typography:
  display:
    fontFamily: "Baloo 2"
    fontSize: "148px"
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
    backgroundColor: "{colors.acid}"
    textColor: "{colors.ink}"
    rounded: "{rounded.none}"
    padding: "18px"
    height: "56px"
  button-hot:
    backgroundColor: "{colors.combat-orange}"
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

## Overview

**Creative North Star: "The Questionable Fight Flyer"**

SlopArena's interface feels like a handmade fight poster that became a live game broadcast. It is mischievous, energetic, and slightly unruly, but never confused about what the player should see or do next. The roughness is deliberate: offset blocks, clipped silhouettes, hard borders, tape-like labels, signal annotations, and small visual misregistrations give the system personality without turning it into generic esports branding or meme UI.

The governing voice is **cool first, joke second**. A screen must work as a fighting-game composition before its annotations are read. Paper and ink establish the field; acid marks action and availability; combat orange marks impact and expressive emphasis. Information remains aligned to a stable grid while one or two elements break it through overlap, rotation, or cropping.

**Key Characteristics:**
- Underground fight-poster energy with disciplined hierarchy.
- Square geometry, heavy borders, and offset physical layering.
- Rounded, heavy display type paired with compact utility labels.
- Characters treated as graphic cutouts, not decorative wallpaper.
- Dry, self-aware copy that never obscures an action or status.

## Colors

The palette is warm paper and near-black ink interrupted by rare hazard acid and combat orange. Use the accents as semantic signals, not as a general saturation wash.

### Primary
- **Acid** (`#FFC821`): Primary actions, available states, selection emphasis, stock markers, and the loudest signal in a region.
- **Combat Orange** (`#E05C2A`): Hit energy, hot actions, combat accents, numbering, and structural impact bars.

### Secondary
- **Online Green** (`#4CDD88`): Live/online status only. Pair with text or structure; never make it the sole status channel.

### Neutral
- **Paper** (`#F0EEE8`): Light type, tape labels, cutout fields, and warm contrast against ink.
- **Ink** (`#17131B`): Main dark field, utility panels, text, and structural grouping.
- **Deep Ink** (`#0D0D14`): Hard borders, outlines, and maximum contrast.
- **Utility Surface** (`#24202B`): Dense HUD tiles and secondary utility controls.
- **Utility Border** (`#625C6B`): Muted inactive slot and utility boundaries.
- **Highlight** (`#FFFFFF`): Hover/focus lift only; do not replace Paper as the default light field.

### Named Rules
**The One Accent Rule.** Acid and Combat Orange may both exist on a screen, but one accent owns each region. If both compete equally, neither communicates anything.

**The Paper-on-Ink Rule.** Small text and gameplay status belong on calm, high-contrast fields. Texture and decoration never carry critical information.

## Typography

**Display Font:** Bundled Baloo 2, through the native TextCore `Assets/Fonts/Baloo2.asset`; bold treatment for pre-match titles.
**Body Font:** Unity UI default sans-serif, compact utility treatment.
**Label/Mono Font:** No bundled mono face is currently established; utility labels use the same UI family with uppercase, tracking, and weight to create a technical voice.

**Character:** Broad, playful, and printed rather than tactical. Display type makes one statement at a time; utility type carries short actions, status, and annotations.

### Hierarchy
- **Display** (bold Baloo 2, `148px`, `-4px` tracking): Main title declaration, with a `7px` print offset. Flow titles use a smaller screen-specific size.
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
- **Primary:** Acid background, Ink text, uppercase bold label, `56px` minimum height, and `18px` horizontal padding.
- **Main-menu actions:** `80px` minimum height, with distinct Online, Solo vs CPU, and Training labels. Ink text on acid, paper, and orange.
- **Hot:** Combat Orange background with Ink text for host/join actions: `5.01:1` contrast. Paper text on this orange is only `3.16:1` and must not be used for small action labels.
- **Hover / Focus:** Lighten the field and translate upward by about `2px`. Paper focus borders remain distinct from acid selection borders on stage cards.
- **Active:** Translate downward by about `3px` so press has a physical response.
- **Secondary:** Paper field with Ink text; retain the same border and geometry so hierarchy comes from color, not a different control language.

### Chips
- **Style:** Flat Ink or Paper strips with uppercase bold text, wide tracking, and a left accent bar when they represent status.
- **State:** Acid indicates action/availability; Online Green indicates live presence; Combat Orange indicates hot or combat state. Add text or structural change so color is not the only cue.

### Cards / Containers
- **Corner Style:** Square.
- **Background:** Ink for broadcast/status panels; Paper for labels and light cutouts; Utility Surface for dense HUD slots.
- **Shadow Strategy:** Hard borders, offset layers, and structural accent edges. No default soft shadow.
- **Border:** `2px` to `4px` Deep Ink, with `5px` to `8px` accent edges for important cards.
- **Internal Padding:** `8px` to `18px` depending on density.

### Inputs / Fields
- **Style:** Rectangular, high-contrast, and compact. Keep the field readable against the panel rather than decorating the input itself.
- **Focus:** Acid border or clear field lift; preserve the same focus treatment as buttons.
- **Error / Disabled:** Use explicit status copy and reduced opacity/contrast, not color alone.

### Navigation

Navigation is a stable action stack or flow-specific footer, not a decorative poster collage. Primary actions stay in a predictable region; secondary actions may be smaller or nested. Labels are short, uppercase, and explicit: `JOIN A FIGHT`, `HOST A MATCH`, `RETURN TO LOBBY`, `LOCK IN`.

### HUD Billboards

Player identity, damage, and stocks live in stable left-side billboard cards with portrait cutouts, accent edges, and large outlined percentage readouts. The local kit uses an orange normal diamond (`1–4`) beside a blue special diamond (`A/E/R/F`) at the bottom center. Each has four circular slots ordered top, right, bottom, left, with an effective-binding prompt above each slot and cooldown feedback inside it. Keyboard prompts follow the active layout; Xbox controller specials show modifier plus face button. Unsupported glyphs fall back to text. Slot readiness uses scale/translation pulses rather than constant animation.

## Do's and Don'ts

### Do:
- **Do** establish the grid before adding crooked poster gestures.
- **Do** use Ink, Paper, Acid, and Combat Orange as semantic roles rather than equal decoration.
- **Do** make focus, selected state, disabled state, and confirmation more obvious than texture.
- **Do** treat character renders as graphic material while preserving face, weapon, and gameplay silhouette.
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
