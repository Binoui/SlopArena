# SlopArena Visual Language

**Status:** living guide  
**Scope:** graphic identity, UI presentation, marketing surfaces, community/Workshop surfaces, and presentation copy  
**Canonical reference implementation:** [SlopArena Web](https://github.com/Binoui/SlopArena-web) and its [live landing page](https://binoui.github.io/SlopArena-web/)

## Purpose

SlopArena needs to look like the same game across menus, results, trailers, web pages, Workshop documentation, social images, and future creator tools. This guide defines the reusable visual grammar behind those surfaces.

It does not prescribe one layout. New work should preserve the identity while adapting composition to the job of the screen.

The separate [Art and Asset Conventions](../contributing/conventions.md) remain authoritative for 3D character rendering, source assets, animation naming, licensing, and package hygiene. The two guides meet at readability, palette, silhouette, and presentation tone.

## Native pre-match flow

The Unity implementation lives in `Assets/UI/SlopArena.uss` and the Main Menu, Server Browser,
Lobby Room, Character Select, and Stage Select UXML screens. The menu uses the bundled
Baloo 2 display face through `Assets/Fonts/Baloo2.asset`; utility text retains Unity's UI
face. Ink, paper, acid, and orange carry the fight-flyer identity without tactical grit
or invented social activity.

Settings is the first page-by-page pack-inspired layout pass. The frontend and match
settings entry points keep their existing navigation and preferences; both mount the
same `Assets/Resources/UI/SettingsOverlay.uxml` shell, with category controls generated
from the current settings service. `SettingsOverlay.uss` supplies the tracked base
style. Locally, `Assets/Resources/UI/LocalPack/PackSkin.uss` applies recolored FPS UI Pack
sprites to the persistent top bar, Home, and Settings. That directory is excluded through
local `.git/info/exclude`; clean checkouts load only project-owned styles, and local builds
include the licensed sprites until an asset-distribution plan is agreed.

- **Persistent top bar:** The logo is Home when the existing Back path safely returns
  there. ONLINE / TRAINING / SOLO share the existing mode routes and display-name gate;
  Settings and account remain on the right. Stage Select, Rooms, and Results retain
  their Back/Leave flow rather than allowing a tab to abandon it.
- **Main Menu:** The mode buttons and large left-side marketing copy are gone. A
  simple playtest greeting stays in the right-hand pack-inspired poster. Fighter
  selection still reads the admitted cooked catalog.
- **Chat:** The presenter has no header bar; Expand/Minimize icons sit directly
  left of the top-right resize grip, and account entry lives in the shared shell.
  A user-resized width and height are one preference shared across menu and gameplay,
  saved across scenes and launches, with host-specific minimum and viewport bounds.
  Menu chat stays in its bottom-left shell cell; gameplay chat mirrors to the left
  side of the HUD. Match entry still closes interactive chat to protect gameplay
  input; disconnected and send feedback render inside the message history instead
  of below the composer.
- **Online:** browsing, hosting, and Join by Address converge on the registered room
  session before fighter and stage selection. Address entry accepts IPv4 plus an
  optional port; it still requires the room directory. It is not an offline bypass.
- **Solo:** player and CPU selection are independent, with explicit edit targets and
  Easy/Normal/Hard controls. Returning from Stage Select preserves both fighters and difficulty.
- **Training:** fighter selection opens the shared Stage Select page. The Training arena
  is first and preselected when admitted; otherwise the first available stage is selected.
  Enter Training loads the chosen stage with the existing local tools. Training and Solo
  place the player at the first baked spawn and the first NPC at the second; added Training
  NPCs use subsequent authored spawns, cycling those NPC spawns when needed. NPC respawns
  retain the selected marker's position, height, and facing.
- **Stages:** cards show rendered previews of the actual project prefabs. Kistu and Bonk
  portraits likewise come from existing character prefabs; image metadata records provenance.
- **Recovery:** focus is visible; controller Back closes address entry before leaving the
  browser. Escape, gamepad Start, or the top-right gear opens the frontend menu on any
  pre-match page, including over address entry and chat. Resume or a second Escape/Start
  returns to the same page; Settings returns to the menu when closed; Quit Game exits
  immediately. Mandatory identity entry and active Settings confirmations/remaps keep
  their own cancel behavior. Directory, host, and room failures present a retry or a
  route back rather than an indefinite connecting label. Empty player slots are not
  presented as connected players.

Results and in-match HUD presentation are outside this pre-match refresh.

## One-sentence definition

> Underground fight-poster energy filtered through a playful, self-aware prototype: uneven and handmade, but structured enough to stay readable and cool.

## Core tensions

SlopArena should hold these pairs at the same time:

| Keep | In tension with |
| --- | --- |
| Cool fighting imagery | Stupid, dry jokes |
| Rigid geometry | Imperfect placement |
| Strong information hierarchy | Cheap photocopied texture |
| Arcade confidence | Honest prototype energy |
| Bold character silhouettes | Restrained decoration |
| Aggressive impact | A welcoming game made for friends |

If one side takes over, the identity weakens. Pure cool becomes generic esports branding. Pure jokes become disposable meme UI. Pure disorder becomes difficult to use.

## Principles

### 1. Information first

The user should immediately understand what happened and what they can do next. Use scale, contrast, grouping, and short labels before adding decoration.

A screen may look crooked. Its hierarchy must not be crooked.

### 2. Deliberate imperfection

Use slight rotations, offset shadows, pasted cutouts, scribbles, uneven rules, and imperfect spacing as controlled accents. Most elements should still align to a clear grid.

Imperfection should feel authored, not randomized. Do not rotate every card or apply noise to every surface.

### 3. One loud gesture

Give each composition one dominant declaration: a result, character name, call to action, status, or event. Supporting information is smaller and quieter.

Avoid a screen where every label competes at headline size.

### 4. Cool first, joke second

The overall silhouette should work as a fighting-game image before the user reads the joke. Humor lives in details, annotations, loading text, status copy, and secondary labels.

Do not use irony to apologize for weak presentation.

### 5. Characters are graphic material

Treat character renders like cutouts pasted into a fight flyer. They may overlap frames, extend beyond the canvas, sit behind type, or enter at a slight angle.

Preserve the face, pose, weapon, and gameplay silhouette. Cropping should add force, not hide identity.

### 6. Texture supports hierarchy

Paper grain, photocopy noise, halftone, stamps, and rough edges should stop clean areas from feeling sterile. They must never reduce gameplay readability or obscure small text.

## Foundation

### Palette

The landing page establishes the initial canonical graphic palette:

| Token | Value | Role |
| --- | --- | --- |
| Paper | `#DED8C9` | Primary warm background |
| Ink | `#171814` | Text, borders, dark fields |
| Acid | `#DFFF36` | Primary actions, live status, disruptive emphasis |
| Orange | `#F05B35` | Combat energy, highlights, numbering |

Supporting neutrals may be derived from Paper and Ink. Prefer warm greys and dirty off-whites over pure white or blue-grey UI chrome.

Character signature colors and gameplay colors may extend the palette. Do not replace the shared foundation with a different theme per screen.

#### Color discipline

- Paper and Ink carry most of the composition.
- Acid usually marks action, availability, success, or something intentionally obnoxious.
- Orange usually marks impact, combat, numbering, and expressive emphasis.
- Use one accent as dominant in a region; using Acid and Orange equally everywhere removes their meaning.
- Preserve accessible text contrast. Small text belongs on calm, high-contrast fields.
- Do not rely on color alone for gameplay or status information.

### Typography

The visual system uses two typographic voices:

1. **Display:** heavy, compressed or blocky sans-serif for declarations, fighter names, results, section titles, and calls to action. The landing page uses Archivo Black.
2. **Utility:** monospace for instructions, metadata, status, buttons, technical labels, and small jokes. The landing page uses Space Mono.

Equivalent project-safe fonts may be selected for Unity, but they should preserve these roles. Do not make every line display type.

#### Typographic behavior

- Prefer uppercase for short display and utility labels.
- Use extreme scale contrast: very large declarations beside genuinely small metadata.
- Keep body copy short and give it comfortable line height.
- Tighten display tracking; give tiny utility labels slightly wider tracking.
- Use a rotated or shadowed word sparingly to create one focal interruption.
- Avoid fake handwritten fonts for every joke. A real annotation may be handwritten; the system remains typographic.

### Shape and construction

The system is built from ordinary shapes used assertively:

- thick rectangular borders;
- offset hard shadows;
- numbered sections;
- circles as framing or annotation devices;
- rules dividing information;
- rectangular stickers and status strips;
- cutout imagery crossing boundaries.

Corners are usually square. Rounded cards, glass panels, soft shadows, and glossy gradients should be rare exceptions with a clear functional reason.

### Texture

Preferred texture vocabulary:

- warm paper grain;
- photocopy noise;
- coarse halftone;
- slightly misregistered color;
- stamped or taped accents;
- rough masking around character cutouts.

Keep texture subtle on interactive and information-dense surfaces. Never bake critical text into a noisy texture.

## Composition grammar

### Grid first, disruption second

Start with a clear grid. Establish primary, secondary, and utility zones. Then break the grid once or twice through overlap, rotation, cropping, or an annotation.

### Borders and shadows

Use borders to state ownership and grouping. Use hard offset shadows to give important actions or posters physical presence.

- Keep shadow direction consistent within one surface.
- Do not add soft ambient shadows to every element.
- Interactive shadows may collapse or shift on press.
- Borders must remain readable at the target game resolution.

### Rotation

Typical rotation should be subtle: approximately one to three degrees. Larger angles are reserved for stickers, stamps, or clearly decorative fragments.

Do not rotate paragraphs, settings controls, or dense information.

### Layering

A useful default stack is:

1. paper field or dark ink field;
2. structural borders and section geometry;
3. large headline;
4. character or gameplay imagery;
5. small labels, status, and annotations;
6. restrained texture over or under the composition.

Maintain enough separation that all interactive states remain legible.

## Imagery

### Character renders

- Prefer poses with a readable action line and strong silhouette.
- Use project-rendered or approved character imagery rather than generic fighting imagery.
- Cut characters cleanly from the background; roughness may be added at the mask edge afterward.
- Let characters frame information rather than automatically becoming a symmetrical versus poster.
- Avoid overfilling every surface with the full roster.

### Gameplay media

Gameplay footage is evidence, not wallpaper. Preserve readable fighters, stage boundaries, hit effects, and HUD state. Use the [Visual Presentation Baseline](../visual-baseline.md) when comparing presentation changes.

Frames around gameplay may use the poster language, but the footage itself should remain clear.

### Icons and marks

Use simple geometric icons with strong weight. Prefer arrows, crosses, circles, underlines, and compact symbols over elaborate outlined icon sets. Icons should feel printed or constructed from the same rules as the typography.

## Motion

Motion should behave like physical graphic material:

- cards snap, stamp, slide, or slightly overshoot;
- offset shadows compress on press;
- labels may appear as pasted strips;
- transitions should be short and decisive;
- combat events may use harsher scale and positional impact than navigation.

Avoid constant floating, glossy easing, particle decoration behind menus, and slow cinematic transitions for routine actions.

Respect reduced-motion settings outside gameplay. Motion must reinforce state change rather than merely prove that the UI is alive.

## Voice and writing

The voice is short, direct, self-aware, and slightly stupid. It should sound confident enough that the joke does not become an apology.

### Good patterns

- `IT WILL BREAK.`
- `PROBABLY SAFE.`
- `NO BALANCE GUARANTEED.`
- `MADE WITH QUESTIONABLE DECISIONS.`
- `GET IN THE SLOP.`
- `TELL ME WHAT BROKE.`

### Writing rules

- State the useful information first.
- Keep jokes short enough to scan.
- Use dry understatement more often than exclamation marks.
- Let system status be honest.
- Prefer specific failure language over generic `Something went wrong`.
- Do not interrupt repeated gameplay flows with a new joke every time.

### Avoid

- `Experience the ultimate battle.`
- `Master unique heroes.`
- `Enter an epic arena.`
- forced lore voice;
- generic esports aggression;
- excessive meme references;
- jokes that obscure a required action;
- edgy anarchy language used as a substitute for personality.

## Applying the language

### In-game menus

Use the full palette and geometry, but keep navigation stable. Current selection, confirmation, disabled state, and controller focus must be more obvious than decoration.

### Character select

Let character color and silhouette carry identity inside the shared Paper/Ink system. Use asymmetry and overlaps around the stable selection grid, not inside the control logic.

### HUD

Gameplay clarity outranks the poster treatment. The local HUD groups orange normals `1–4` and blue specials `A/E/R/F` into two close-set diamonds at the bottom center. Four circular slots per diamond retain the existing move icons and cooldown overlays. Prompts above the circles follow effective keyboard/gamepad bindings and switch after gameplay input; controller specials show modifier plus face button. A readable text prompt replaces artwork for unsupported keys or missing local assets. The supplied frame art does not encode gameplay state. Damage, stocks, cooldowns, and player identity remain stable while the camera moves.

Kenney Input Prompts 1.5 (CC0) supplies the keyboard and Xbox sheet PNGs and XML maps. For a local build, copy `Keyboard & Mouse/keyboard-&-mouse_sheet_default.{png,xml}` to `client/Unity/Assets/Resources/InputPrompts/keyboard.{png,xml}` and `Xbox Series/xbox-series_sheet_default.{png,xml}` to `client/Unity/Assets/Resources/InputPrompts/xbox.{png,xml}`. The `InputPrompts` directory and its Unity metadata are gitignored; a clean checkout has text prompts until the sheets are provisioned. The atlas XML coordinates are in source-PNG pixels, so Unity import scaling must not change prompt selection.

### Results

Results are a strong candidate for the full language: one dominant outcome, oversized winner/placement type, character cutouts, compact stats, and short contextual copy.

### Ability Lab and creator tools

Use the visual identity for shell, section hierarchy, status, empty states, and previews. Editing controls must remain tool-like, aligned, and predictable. Creator UX should not imitate a chaotic poster at the expense of authoring speed.

### Website, trailers, and social images

These surfaces may use the highest texture, overlap, cropping, and typographic contrast because their content is less interactive. The landing page is the current canonical example.

### Stages and world decoration

Translate the attitude rather than pasting UI onto the world. Favor bold readable geometry, purposeful asymmetry, signs, cheap materials, playful sponsorships, and specific jokes. Avoid generic graffiti, random props, or anarchy symbols as shorthand for “underground.”

## What SlopArena is not

- cyberpunk neon;
- polished esports branding;
- generic graffiti or anarchy imagery;
- soft SaaS cards and glassmorphism;
- grey Unity prototype UI;
- random rotations everywhere;
- grunge texture without hierarchy;
- cartoon meme overload;
- photorealistic military aggression;
- a parody that is embarrassed to be a real fighting game.

## Source-of-truth hierarchy

When references disagree, use this order:

1. This living guide for overall graphic intent and grammar.
2. Functional requirements and gameplay readability for the surface being designed.
3. Existing approved product examples, with the landing page as the initial canonical reference.
4. Exact implementation tokens in the owning project.
5. Moodboard or external references.

If a successful new surface extends the language, update this document with the rule it established. Do not silently copy a one-off accident into every future screen.

## Agent brief

Use this instruction when asking an agent to create or revise a SlopArena visual surface:

> Read `docs/design/visual-language.md` and inspect the canonical SlopArena landing-page implementation before designing. Preserve the visual grammar—Paper/Ink foundation, controlled Acid and Orange accents, heavy display type, monospace utility text, rigid geometry with deliberate imperfection, fight-poster layering, and dry self-aware copy. Adapt the grammar to the surface's function instead of copying the landing-page layout. Gameplay readability and interaction state take priority over decoration. Explain any intentional deviation.

For implementation tasks, also require the agent to inspect the existing scene/component and reuse established project tokens and controls before introducing new ones.

## Review checklist

Before approving a new surface, ask:

- Is the primary information obvious within one second?
- Is there one dominant visual gesture?
- Does the screen feel cool before the user reads the joke?
- Is imperfection controlled rather than random?
- Are Paper and Ink doing most of the work?
- Do Acid and Orange still have distinct jobs?
- Are display and utility typography used for different roles?
- Can characters and gameplay still be read at the actual target size?
- Are controller, keyboard, hover, focus, disabled, loading, and error states clear where applicable?
- Would removing the texture leave a strong composition?
- Does the copy give useful information before personality?
- Does the result feel like SlopArena rather than generic brutalism or generic grunge?

## Maintaining the reference

Add canonical examples only after they have shipped or been explicitly approved. For each example, record:

- the surface and purpose;
- a screenshot or stable link;
- the rule it demonstrates;
- any intentional deviation;
- the project version or commit.

Prefer a small set of strong examples over a large undifferentiated moodboard.
