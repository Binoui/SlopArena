# Playable friends demo reset

**Goal approved:** playable game ASAP; four-character friends demo.
**Scope:** preserve the current game baseline on local `main`, then prioritize playable matches over further platform expansion. The cleanup batches below are proposed work, not claims of implementation. No push, deployment, dependency installation, or destructive cleanup is authorized by this document.

## Product target

The admitted roster is **Manki, FightGuy, Kistu, Bonk** (`content-cooked/roster/manifest.json`). Nilus remains legacy compatibility, not a fifth demo requirement. Package admission does not prove kit completeness or player acceptance.

Use one existing stage and the existing dedicated-server Join flow for the first remote session. Keep all four characters as the demo milestone, but run a rough match with one friend before polishing all four. Host-and-play remains a technical fallback, not a second workflow to redesign.

Demo-ready means all intended normals/specials work on ground and in air; recovery, damage, interruption, KO/respawn, match end and another match work; attacks are readable; no common match-breaking bug prevents ordinary play. Balance and polish remain provisional until humans play.

## Execution order

| Order | Deliverable | Acceptance |
| --- | --- | --- |
| 1 | Coherent local main baseline | Preserve staged game work and the normal-facing fix; integrate incoming test cleanup; retain valid cooked roster pins; build Shared/server, run remaining Shared tests, check Unity. Keep website as a separate local repository. |
| 2 | Remove iteration-policy contradictions | One authoritative Unity CLI workflow; minimum session context; explicit local-tuning versus publishing gates. No global skill deletion. |
| 3 | Package the complete admitted roster | Server publish and client StreamingAssets contain every package and dependency required by the roster, with matching identities/hashes. Verify in a fresh output directory so stale files cannot hide omissions. |
| 4 | First remote two-player match | Both players launch the distributable, join, select, fight, KO/respawn, finish, and start another match without Editor help. Record concrete blockers, not completion percentages. |
| 5 | Finish four demo-ready kits | Use observed match problems to prioritize missing/broken moves, recovery, camera readability and animation clarity. Exercise every roster character remotely. |
| 6 | Friends demo | Repeat the packaged join-to-rematch smoke; provide a short play guide and known issues. Validate four-player play before advertising it as supported. |

Only work that unblocks this sequence or fixes observed gameplay belongs before the demo. Do not turn this reset into another architecture milestone.

## Highest-impact findings

### Release correctness before architecture cleanup

`src/Server/SlopArena.Server.csproj` includes only `content-cooked/fightguy/**` plus the roster. `scripts/build-release.sh` verifies FightGuy and stages only FightGuy into the client and embedded server. `scripts/deploy-server.sh` also verifies only FightGuy. The current roster requires four packages. These paths are statically inconsistent; a successful build alone does not prove a usable distribution.

**Observed on the integrated baseline:** `dotnet publish src/Server/SlopArena.Server.csproj --no-build --nologo -o <fresh temporary directory>` succeeded, but its roster-required payload check found FightGuy present and all four payload files missing for Manki, Kistu and Bonk. This is reproduced packaging incompleteness, not merely a suspected architectural issue.

**Next coding slice:** make publishing roster-complete, reuse current package verification, and smoke-load the resulting catalog from a clean publish directory. Do not narrow the roster, add fallback content, or weaken admission. Then exercise the actual Windows client and dedicated-server path; the current build script targets a Windows x64 client, not a cross-platform client release.

### Keep the useful architecture

| Module / files | Decision | Reason |
| --- | --- | --- |
| `src/Shared/ServerSimulation.cs`, `Simulation.cs`, `SpellResolver.cs` | Keep | One deterministic gameplay authority for server and local simulation. |
| `src/Shared/MatchContentCatalog.cs`, package compiler/runtime, `content-cooked/` | Keep | Consistent match content and fail-closed admission; removing them would create a new migration. |
| `client/Unity/Assets/Scripts/Runtime/Entities/PlayerRenderer.cs` and existing bridges | Keep | Presentation consumes Shared state; no Unity-only gameplay fixes. |
| `client/Unity/Assets/Scripts/Editor/EditorDevelopmentContentProvider.cs`, runtime `ClientSession.cs`, `Content/LocalContentResolver.cs` | Use existing local iteration | Editor Training compiles source in memory through the same Shared catalog seam; publishing is not required for each tuning attempt. See `docs/testing.md`. |
| `src/Server/MatchControlServer.cs`, `MultiMatchOrchestrator.cs`, `MatchContentCatalogProvider.cs` | Keep / freeze | Already own match startup and content admission; prove remote play before redesigning. |
| `src/Shared/BuiltInContentResolver.cs`, server/client catalog-loading variants | Defer consolidation | Similar code has different roots, failure contracts and runtime duties. No deletion without callsite and behavior proof. |
| Workshop capability generalization, legacy Nilus migration, expanded stage/asset tooling | Freeze expansion | Retain working infrastructure; no new platform features unless a demo move or observed bug requires them. |

No new abstraction, event bus, registry, content format, or networking rewrite is part of this reset. Profile actual frame/network problems before optimizing speculative hot paths.

## Skills and operating-policy cleanup

| Source | Finding | Proposed action |
| --- | --- | --- |
| `.omp/skills/sloparena-build/SKILL.md` vs session-mounted `skill://sloparena-build` | Repository file uses Unity CLI correctly; the mounted copy read during this session still prescribed removed MCP commands. | Repair skill discovery/source precedence; refreshing the wrong repository file will not fix a stale mounted copy. Confirm effective instructions in a fresh session. |
| `.omp/skills/unity-mcp-gamedev/SKILL.md` | Discoverable retired MCP-named entry repeats the current CLI gate. | Retire from active skill discovery after checking references. Keep historical context outside executable skill routing if needed. |
| `.agents/skills/unity-skills/skills/unity-cli/SKILL.md` | Explicitly forbids `unity command` / `unity pipeline`, contrary to this project. | Do not activate its operational workflow in SlopArena; prefer repository CLI guidance. Retain useful advisory material on demand. |
| `.agents/skills/unity-skills`, `.claude/skills/unity-skills` | Ignored local skill installation; the `.claude` entry is a symlink to `.agents`. | Treat as one local install, not duplicate project code. Do not delete user/global tools as repository cleanup. |
| `.omp/skills/orient/SKILL.md`, `.omp/skills/branch-status/SKILL.md` | Both claim session-start work. | Orient from current goal, current work and relevant contract only; branch inventory on integration/branch requests, not every tuning session. |
| `.omp/skills/sloparena-build-export/SKILL.md`, `sloparena-build/SKILL.md`, `sim-test/SKILL.md` | Broad overlapping build/test triggers. | Release skill only for packaging/deploy; Shared tests and live Editor verification have distinct gates. |
| Character, animation, stage and asset skills | Repeat general verification commands and broad completion requirements. | Link one verification policy; retain only domain-specific cook/bake/asset checks in each skill. |
| `.omp/skills/sloparena-finish-branch/SKILL.md` | Contains commit/push operations behind explicit permission controls. | Keep on demand and preserve those controls; never make task completion imply a push. |

**Small first cleanup batch:** resolve effective Unity skill routing, retire the obsolete project skill from discovery, narrow overlapping triggers, and express the following three verification modes in one living reference. No framework or skill-manager implementation is proposed.

| Mode | Proof |
| --- | --- |
| Local tuning | Edit source, exercise affected move through Editor development content. Focused tests when simulation behavior changes; no publishing cook for every experiment. |
| Shared mechanic / integrated change | Focused regression, full Shared suite at delivery, current Unity compile/console and one affected runtime scenario. Broader checks only for affected seams. |
| Distributable demo | Cook changed content, verify roster identities, publish all required packages, build player/server, run remote join-to-rematch smoke. |

Always retain Shared authority, preservation of unrelated work, permission for commits/push/installations, and honest verification claims. Default to one agent; delegate only genuinely independent substantial slices. An audit is a valid exception, not the template for a two-line edit.

## Documentation and file cleanup

| Paths | Proposed action |
| --- | --- |
| `docs/README.md` | This plan is the current product entry point. Keep a short route to architecture, character authoring, testing and release operations; historical plans are not a mandatory reading list. |
| `README.md`, `docs/architecture-overview.md`, relevant contribution links | Align playable-roster wording with the cooked manifest: include Bonk; distinguish Nilus compatibility. Avoid invented readiness percentages. |
| `docs/systems/netcode-architecture.md` | Remove contradictory living guidance: introduction/raw-state sections and unfinished phase checklists say prediction is absent, while section 6 describes implemented tracks. Verify against `PvPMatch`/`RollbackSimulationBridge`; do not rewrite networking to match stale prose. |
| `docs/plans/2026-08-01-pvp-roadmap-v2.md`, `2026-08-02-exe-release-plan.md`, `2026-08-26-fightguy-character-cooking-cutover.md` | Clearly mark dated execution assumptions as historical and direct current work here. Preserve decisions/evidence; do not execute their old unchecked lists. |
| `PHASE_3_COMPLETION_PLAN.md`, `PHASE_4_AUTHORITATIVE_PLAN.md` | Already marked historical at the top. Move out of root only in a small link-preserving archival change; this is not a release blocker. |
| Accepted ADRs, character move-data and research reports | Keep as on-demand records. Do not delete history merely to lower file counts. |
| Root visual-check outputs, `tmp/`, asset/stage caches | Mostly local/ignored generated work. Exclude from orientation; delete only with explicit permission and known regeneration paths. |
| `SlopArena-web/` | Keep the independent repository on disk, excluded from the game index. A Git link without `.gitmodules` is not a usable submodule. |
| Stage material metadata and ignored local art | `.gitignore` excludes `.mat`, FBX, textures and vendor packs. A clean game checkout is not a complete art checkout. Record licensed/local build prerequisites; do not force-add restricted assets to make Git appear self-contained. |
| `tools/check_docs.py` | Its whole-tree Markdown scan excludes `.claude` but not ignored `.agents`. The baseline command reports six broken links, all in locally installed `.agents` skills. Scope checks to project-owned documentation; do not repair vendor docs or suppress real project link failures. |

Net deletion estimate is deliberately unset: this audit establishes priorities, not proven dead-code counts. The useful first result is less mandatory coordination, not fewer source lines.

## Limits and next action

This is a repository/workflow audit, not evidence of a working remote match. The last normal-facing change was exercised in Training, not with another player. Editor compilation, headless tests, packaged-content completeness, and human play acceptance are separate facts.

**First coding task after baseline integration: fix four-package distribution and prove it from a clean publish directory.** In parallel with ordinary kit work, arrange the first rough friend session. Do not wait for final VFX, final balance, a new stage, or further Workshop architecture.
