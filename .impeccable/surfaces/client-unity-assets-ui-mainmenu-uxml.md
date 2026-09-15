---
version: 1
slug: "client-unity-assets-ui-mainmenu-uxml"
primary_target: "client/Unity/Assets/UI/MainMenu.uxml"
related_targets: ["client/Unity/Assets/UI/CharSelect.uxml","client/Unity/Assets/UI/StageSelect.uxml","client/Unity/Assets/UI/ServerBrowser.uxml","client/Unity/Assets/UI/LobbyRoom.uxml"]
---

# Pre-match flow
Mode: Operate. Scope: main menu, browser and address dialog, online lobby, Training/Solo/PvP fighter selection and stage selection. User approved full implementation after critique and original mockup comparison.

## Direction contract
THESIS: Get friends into a fight without teaching networking. Keep the mockup's connected composition but trade tactical aggression for a mischievous local fight flyer.
OWN-WORLD: Ink ground, warm paper, acid actions and restrained orange print offsets. Existing Baloo 2 supplies playful display lettering; square controls, calm readable body copy, real portraits and stage captures. No fake chat, skulls, targeting marks, decorative grids or invented activity.
STORY: Pick online, CPU match or training. Online browser exposes room discovery, hosting and advanced address entry. Fighter selection makes player/CPU ownership explicit; host authority and next step remain visible. Failure states offer recovery rather than terminal technical copy.
FIRST VIEWPORT: Substantial two-line title on left; three large explained play actions beneath/alongside it; complete four-fighter roster on right. Whole screen is deliberately occupied, not scattered miniature widgets. Downstream screens use consistent back/title, central selection, and state/action footer.
FORM: Refine the established fight-poster world using the user-supplied first mockup as composition/type-density evidence, not a pixel reproduction or authority for false content. No concept seed: existing world and composition direction already selected by user.
SIGNATURE: Expressive, soft display lettering against crisp pasted-paper controls; selected fighter/CPU target and next-action confirmation are the interactive focal moments, with modest focus/press movement rather than idle animation.

Constraints: Unity desktop UI Toolkit, installed Unity CLI/Pipeline only, server-authoritative simulation and existing lobby protocol untouched. No new dependency, account system, matchmaking or social features. Preserve actual room membership and configured selections on backward navigation. Native verification at 16:9 and current ~2:1 aspect; one batched inspection and one correction confirmation.

## Implementation evidence

- Five active screens plus address modal share the native Baloo 2/paper/ink visual language.
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
