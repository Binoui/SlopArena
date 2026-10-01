---
version: 1
slug: "client-unity-assets-ui-mainmenu-uxml"
primary_target: "client/Unity/Assets/UI/MainMenu.uxml"
related_targets: ["client/Unity/Assets/UI/CharSelect.uxml","client/Unity/Assets/UI/StageSelect.uxml","client/Unity/Assets/UI/ServerBrowser.uxml","client/Unity/Assets/UI/LobbyRoom.uxml"]
---

# Frontend match flow
Mode: Operate. Scope: player-facing frontend match flow, Settings, pause/Training controls, chat, dialogs, and restrained HUD neutral chrome. The identity rollout is implemented; creator/Ability Lab tools remain excluded. Connected-room and distributable-player acceptance are separate from local native presentation checks.

## Direction contract
THESIS: Get friends into a fight without teaching networking. Keep a connected composition with low-poly underground toy-fight-club and DIY internet-game energy: slightly crappy on purpose, extremely readable. “Toy” describes scavenged construction, not literal plastic or required cuteness.
OWN-WORLD: Paper-led frontend with the user-authored `Assets/UI/FrontendPaperBackground.png`; exact ink `#1B1A18` remains dark text/framing rather than being inverted. Page ink/muted/warning/error roles are separate from dark panel roles. Action Yellow `#FFCC22`, Archivo declarations, and readable sans/short useful mono remain. Settings, match overlays, and HUD keep their restrained fields and functional colors.
STORY: Pick online, CPU match or training. Online browser exposes room discovery, hosting and advanced address entry. Fighter selection makes player/CPU ownership explicit; host authority and next step remain visible. Failure states offer recovery rather than terminal technical copy.
FIRST VIEWPORT: Home separates left mode controls, a larger aspect-fitted framed poster, and right announcements. Wallpaper continues behind the transparent top bar; control-local paper backing preserves readable navigation over authored black marks. Chat is an absolute user-sized bottom-left overlay in every scene, never a reserved cell or chat-aware poster shrink. Fighter/stage participant and configuration essentials occupy bottom middle/right, leaving bottom-left clear by default; larger user overlays may still overlap without rebalancing.
FORM: Use chunky low-poly forms, exaggerated silhouettes, curated asset-pack collage, harmonized through scale, lighting, material response, palette/value hierarchy, and composition; no compulsory universal shader. Website is strongest graphic reference, not fully approved implementation.
SIGNATURE: Bold Archivo Black target display against crisp constructed controls; selected fighter/CPU target and next-action confirmation are focal moments, with authored asymmetry and controlled hard-edge print offsets. Stable interaction geometry and cool-first readability take priority over decoration.

Constraints: Unity desktop UI Toolkit, installed Unity CLI/Pipeline only, server-authoritative simulation and existing lobby protocol untouched. No new dependency, account system, matchmaking or social features. Preserve actual room membership and configured selections on backward navigation. Native verification at 16:9 and current ~2:1 aspect; one batched inspection and one correction confirmation.

## Implementation evidence

- Implemented fighter-select treatment: Archivo Black declarations/name strips/controls, Space Mono participant metadata, paper editing panels, Action Yellow selection with a check icon, explicit local P1/P2 assignment markers, and hard structural press edges. Existing roster, navigation, CPU difficulty, and portrait assets remain intact. First-pass evidence: `.impeccable/review/fighter-select-unity-20261001/`; no connected-room/PvP acceptance claim.
- Approved material cutover: shared palette separates paper-page roles from exact `#1B1A18` ink. `FrontendPaperBackground.png` is a byte-identical copy of the user-authored background supplied on 2026-10-01, painted behind all frontend shell pages; neutral card/control fields remain `#F0EEE8`.
- Implemented native paper-led Home: left modes, full original artwork in an aspect-fitted ink/paper frame with yellow attachment strips, right announcements, and an absolute chat overlay. The social host no longer occupies page lower-row space; resizing/expansion does not alter the composition below. Other shell pages inherit paper headers and appropriate exposed text colors while dense panels remain dark. Evidence: `.impeccable/review/paper-shell-unity-20261001/`; gameplay/HUD/arena visuals remain unchanged.
- Implemented stage/Results rollout: full-opacity existing stage previews, paper stage-name strips, yellow selection distinct from paper focus, matching participant/action fields, and stronger Archivo outcome/player declarations. Actual ranking/stats/return paths remain; obsolete Results bursts/grid/fake-feed and broadcast footer were removed. Native evidence: `.impeccable/review/stage-results-20261001/`. Actual Solo data was recorded before the change and replayed unchanged after compilation; unavailable duration stays unavailable.
- Implemented online-flow treatment: Archivo browser/lobby/dialog declarations, paper room/player/form fields, yellow create/join/continue, dark inputs with visible focus, and readable connection/retry/invite-disabled feedback. Preserved actual data, named nodes, authority, and network behavior. Fixed the collapsed mounted address modal and made Editor-only tool panels natively scrollable. Native evidence: `.impeccable/review/online-flow-20261001/`; Steam unavailable, so no connected-room acceptance claim. Explicitly labelled native layout specimens were temporary and never represented live Rooms. This is not a whole-interface migration.
- Remaining player-facing finish: all five Settings categories, reset/display confirmation, remap cancellation, match Settings, Training/Solo pause, gameplay join presentation, and frontend/gameplay chat. HUD neutral fields/outlines are harmonized without geometry/assets/animation or functional-color changes. Optional local PackSkin assets remain untouched; migrated Settings controls are independently styled. Evidence: `.impeccable/review/remaining-ui-20261001/`. Removed temporary online/Results palette classes and unsupported USS declarations after the shell cutover; no new theme framework.
- Chat contrast/control refinement: paper frame and channel tabs, dark history/input with paper rules, yellow active/available actions, readable truthful disabled states. Grouped Hide/Resize only; removed maximize plus obsolete expanded host/focus APIs. Hidden CHAT anchors bottom-left. Existing Direct conversation/mute management remains without a duplicate sidebar. Top-bar modes have visible ink borders. Evidence: `.impeccable/review/chat-contrast-controls-20261001/`.
- Settings material finish: paper outer panel, ink Archivo title, ink-bordered paper categories/yellow selection, dark scroll/control field, and readable ink status. Keyboard-focused categories/Back use ink/paper with a yellow frame. Same categories, bindings, slider values, confirmation/remap/reset/display-revert logic, and frontend/match ownership. Evidence: `.impeccable/review/settings-paper-20261001/`.
- Address entry resolves registered servers through the existing directory and joins the
  same lobby session as browsing. The unreachable legacy UDP lobby scene/controller/UXML
  and build entry were retired; no second match protocol was added.
- Existing character/stage prefabs supplied missing Kistu/Bonk portraits and seven stage
  previews. No gameplay package or cooked artifact was edited.
- Native screenshots and smoke results live in `.impeccable/review/pre-match/`.
- Solo and Training were launched into `Arena_Offline`; selection persistence, invalid
  address/cancel, directory failure, host cancellation, and expired-room recovery were exercised.
- Live multi-client online success remains unverified: directory guest authentication
  failed in this environment. No connected-room screenshots are fabricated.
- Training launch exposed a separate camera occlusion: the initial view is behind South
  Wall. `smoke-results.json` records camera position and the nearest renderer; no arena
  or camera code was changed in this UI pass.
