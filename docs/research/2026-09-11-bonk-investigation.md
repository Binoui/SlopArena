# Bonk contact and recovery investigation — 2026-09-11

## Scope and decisions

Bonk is not currently playable enough for the friends playtest: poor sword contact,
excessive recovery, and slow aerials. FightGuy is the intentional design baseline;
Manki's copied normals are intentional for now; Kistu ground 4 is intentionally a
hard-to-hit kill move. Those characters are controls, not retuning targets.

This investigation did not change gameplay, attachments, or cooked assets. The only
subsequent production repair restores a missing sampling/serialization block in
`DeterministicPoseTrackBaker.Bake`. It preserves existing marker detection, readable
mesh endpoint extraction, and unreadable/non-blade fallback behavior.

## Run again

From the repository root, with the .NET SDK and the admitted cooked roster present:

```bash
dotnet run --project tools/BonkInvestigation
```

Default output: `tools/BonkInvestigation/bin/Debug/net8.0/report/` (ignored build output).
To keep a particular run somewhere else:

```bash
dotnet run --project tools/BonkInvestigation -- /tmp/bonk-investigation-rerun
```

The tool runs current Shared source against the current admitted cooked packages;
raw `character.json` edits alone do not change its input. The normal Shared build
also refreshes this checkout's ignored Unity plugin DLLs. It does not cook packages,
change source tuning, refresh roster pins, or operate the Editor.

Outputs:

- `summary.txt`: contacts, real jump timing, pose-phase experiments, recovery ticks.
- `geometry.json`: post-tick active hitbox endpoints, radius, facing, elapsed ticks,
  and target position for the directly-ahead contact fixture.
- `trajectories.json`: full-animation hilt/tip geometry, including inactive ticks.
- `content-identities.json`: package identities/hashes for Bonk and both controls.

Compare a future run to the observations below. A changed hash or Shared revision
means a changed input, not an exact historical reproduction. This is telemetry,
not a test that requires Bonk to remain broken. `-1` means no event was observed
before the fixture stopped, not a universal impossibility.

## Historical input identity

Bonk `0.0.0-dev`:

- Source hash: `c8aee2d2c8d0f4fc7ca6ec33c68b2ddb1d438a2bbdbffc5056eaf2e43b9549ff`
- Cooked content hash: `9e65ffa4752dca43537b3dc974abd6b43072aed02ab049515c726d36fb8d0923`
- Package hash: `2c1725aff6432ee59d38e7cfcbe62ac51a59cb80ab366760c70ac61d98bf7e59`

Source and root cooked normal timings matched. The older table in
`docs/characters/bonk.md` did not match and was not used as runtime evidence.
This investigation ran in a dirty worktree; package hashes do not pin the Shared
source revision. The numeric observations here are the historical baseline.

## Ground contact: demonstrated active-window misalignment

Direct target fixture: attacker at X/Z 100/100, FightGuy at 100/101, both grounded;
attack pressed at report tick 0, then neutral. Bonk ground 1–4 all missed. Kistu's
four normals connected at report ticks 17, 9, 11, and 40 respectively.

For Bonk, facing remained 0 radians toward the target. During observed active
frames, the capsule's entire forward-axis extent (including radius) was behind him:

- Ground 1: Z from -2.78 to -0.61 m relative to attacker.
- Ground 4: Z from -2.54 to -0.28 m.

Both animations reach forward later, after the authored damaging window. Ground 2
was predominantly elevated: the lowest observed active capsule extent was about
1.49 m above the floor. Ground 3 swept behind, above, and below the attacker.

### Isolated pose-phase experiment

Thirty starting positions: X offsets -1, -0.5, 0, 0.5, 1 m crossed with Z offsets
0.5, 1, 1.5, 2, 2.5, 3 m. Normal targeting and pushboxes remain enabled.
Each sample uses a fresh simulation. The experimental pose array advances by 0, 8,
or 16 baked frames, clamped at the final frame. No on-disk payload changes.

| Move | Original | +8 pose frames | +16 pose frames |
|---|---:|---:|---:|
| Ground 1 | 0/30 | 0/30 | 13/30 |
| Ground 2 | 0/30 | 0/30 | 0/30 |
| Ground 3 | 0/30 | 0/30 | 0/30 |
| Ground 4 | 0/30 | 29/30 | 14/30 |

Damage, radius, operation triggers, and targeting settings were unchanged. The
entire attacker pose is shifted, including hurtbox bones; this is a diagnostic
counterfactual, not an implementation proposal. Together with the endpoint traces,
it demonstrates active-window/pose misalignment for ground 1 and 4. It does not
prove that their attachment geometry is visually correct, or explain every miss
on ground 2 and 3. Counts are coverage samples, not hit probabilities.

## Recovery: IASA does not release ordinary movement

Authored 60 Hz timing:

| Normal | Hit trigger / duration | Stage duration | IASA | Landing lag |
|---|---:|---:|---:|---:|
| Ground 1 | 9 / 7 | 80 | 30 | 0 |
| Ground 2 | 16 / 12 | 74 | 37 | 0 |
| Ground 3 | 8 / 12 | 60 | 33 | 0 |
| Ground 4 | 20 / 13 | 94 | 52 | 0 |
| Air 1 | 13 / 16 | 46 | 41 | 20 |
| Air 2 | no damaging operation | 30 | 4 | 0 |
| Air 3 | 7 / 10 | 54 | 48 | 22 |
| Air 4 | 25 / 15 | 66 | 59 | 24 |

After a whiff, separately hold movement, dash, or jump from tick 1:

| Move | First dash tick | First movement tick | First jump-squat tick |
|---|---:|---:|---:|
| Ground 1 | 29 | 80 | 79 |
| Ground 2 | 36 | 74 | 73 |
| Ground 3 | 32 | 60 | 59 |
| Ground 4 | 51 | 94 | 93 |

Report ticks are zero-based from the initial input; authored triggers use the
ability's own clock. IASA enables attack/dash cancellation, not movement/jump
recovery. Lowering IASA alone leaves the 1.0–1.57 second movement commitment.

## Aerials: actual jump experiments

Neutral full jump: apex 1.90 m, landing at report tick 43.
Neutral short hop: apex 0.66 m, landing at tick 26.
Jump is pressed at tick 0. Full jump holds the key through tick 39; short hop
releases immediately. Attack probes press at tick 7, 15, or 25. No target is present:
these measure activation, hitbox existence, and landing, not aerial contact.

Results for an attack press at tick 7:

| Move | First active report tick | Full-jump landing lag | Short-hop landing lag |
|---|---:|---:|---:|
| Air 1 | 19 | 0 (auto-cancel) | 20 |
| Air 3 | 13 | 22 | 22 |
| Air 4 | 31 on full jump; absent on short hop | 24 | 24 |

Air 4 lands before activation in this short-hop fixture, then incurs 24 ticks
(0.4 seconds) of landing lock. On a full jump, pressing air 4 at tick 25 also lands
before activation and incurs 24 ticks of lag. Air 3 starts relatively early;
its recovery/landing commitment is a stronger concern than a blanket startup cut.
Air 2 is still a non-damaging pipeline probe, not a completed normal.

## Repair proposal — not yet approved tuning

1. Restore the missing baker block before trying to recook. This repair restores
   SKEL header/names, sorted animation records, 60 Hz clip sampling, and hips-relative
   bone serialization; it does not change weapon endpoint selection.
2. Use ground 1 as the first playable reference: shorten its animation, align the
   damaging window with the forward swing, and reduce total recovery. Merely moving
   the hitbox later would improve contact while worsening startup.
3. Apply the same approach to ground 4, retaining an intentional heavier commitment.
4. Inspect ground 2/3 in Ability Lab before selecting attachment or timing changes.
5. Make air 4 usable in its intended jump window and review aerial landing lag.
   Treat air 3 separately rather than speeding every aerial indiscriminately.
6. Replace air 2's probe with an intended move, then playtest the full kit.

Keep damage and radius unchanged during the initial alignment pass.

## Verification and limitations

The preserved diagnostic was rerun through its regular Shared project reference
and reproduced the contact, phase-shift, jump, and recovery observations above.
The repaired baker and real WeaponAttachConfig compiled against installed Unity
6000.0.78f1 assemblies with zero errors; the isolated build emitted an unassigned
FrameCount warning because its caller is outside that compile scope.

No live Unity sampling or visual alignment verification was performed. The reachable
Editor belongs to the main checkout; worktree agents must not operate it. After
integration, follow `TESTING-UNITY.md` and the main-only CLI workflow in
[Unity CLI](../contributing/unity-cli.md), using a dry-run cook before any publishing
cook or roster refresh. The existing cooked poses were not regenerated by this repair.
