---
name: sloparena-finish-branch
description: Finish SlopArena work — verify suite, squash to one commit, push, open PR. Use when implementation is done and the user wants to ship ('finish', 'make a PR', 'create pull request', 'ship it')
disable-model-invocation: true
triggers:
  - finish
  - pr
  - pull request
  - ship
---

# SlopArena Finish Branch

**Permission note (first line):** invoking this skill is the explicit "commit push" permission per `.omp/RULES.md`; nothing is pushed without the user invoking it.

Deterministic flow, no menu. Run from the repo root or let `git rev-parse --show-toplevel` find it.

## 0. Subject

Decide `<subject>` before touching git:

- A user-supplied subject wins.
- Otherwise derive one conventional subject for the whole branch, per the commit convention in `docs/contributing/conventions.md` (§ Git & Commits): `<type>(<scope>): <imperative summary>` + ` (issue #N)` when the branch resolves a GitHub issue.
- Branch resolves a GitHub issue? Capture the number (`ISSUE_N`): user-supplied, from session `issue://` links, or `gh issue list --state open`. It goes in the subject AND in the PR body as `Closes #N` (Step 4) — a title ref alone does NOT auto-close the issue (GitHub reads closing keywords from the PR body / commit message only).
- Pick the type/scope from the actual change (`feat(match)` for gameplay, `fix(client)` for repairs, else `refactor`/`docs`/`test`/`chore` + the subsystem scope). Match the existing log style, e.g. `feat(match): roster-driven match start with character classes (issue #35)`.
- If the branch is empty (no code changes), say so and stop.

## 1. Branch guard

```bash
git branch --show-current
```

If the current branch is `main`, refuse: print "work on a feature branch, then invoke finish" and stop.

## 2. Tests

```bash
cd "$(git rev-parse --show-toplevel)" && dotnet test tests/Shared.Tests/ --nologo
```

Failures → stop, report them, do NOT commit.

## 3. Squash

```bash
git add -A   # required: reset --soft does NOT stage untracked files — they'd be dropped from the commit
git reset --soft "$(git merge-base HEAD main)"
git diff --cached --quiet && { echo "nothing to squash — clean tree"; exit 1; }
git commit -m "<subject>"
```

- **Always `git add -A` first.** `git reset --soft` only stages the diff merge-base..HEAD; brand-new files in the working tree would otherwise be silently omitted from the commit.
- **Fresh branch (HEAD == merge-base, no commits)?** The reset is a no-op — that's fine: `git add -A` already staged the whole working tree and the commit is created normally. This is NOT the "clean tree" case.
- "Clean tree / nothing staged" means the tree was already clean after the reset — print "nothing to squash — clean tree" and stop.

## 4. Push + PR

```bash
if git ls-remote --heads origin "$(git branch --show-current)" | grep -q .; then
  git push --force-with-lease -u origin HEAD   # branch was pushed before — rewritten history needs force
else
  git push -u origin HEAD
fi
```

Open the PR with a real description — summary, changes, verification, and a **fenced** diffstat:

````bash
gh pr create --title "<subject>" --body "$(cat <<'EOF'
<1-3 sentences: what the branch does and why>
Closes #<ISSUE_N>.   <- when the branch resolves an issue; REQUIRED or GitHub leaves it open (title refs don't count)

## Changes
- bullet per logical change (start from `git log main..HEAD --format='- %s'` and expand)

## Verification
- what was run and the result: test count, build, manual/e2e checks

## Diffstat
```text
<git diff --stat main...HEAD>
```
EOF
)"
````

- The diffstat MUST be inside a ```text fence — an unfenced diffstat renders with broken alignment in GitHub Markdown.
- Issue branches: the PR body MUST carry `Closes #N.` on its own line (or `Fixes`/`Resolves`) — GitHub auto-close keywords are parsed from the body/commit message, never the title.
- Server-authoritative changes: note which Shared files changed (per `docs/contributing/conventions.md` § Git & Commits).
- If `gh` is unavailable → print the push output and the repo URL (`https://github.com/Binoui/SlopArena`) instead, and stop there.

## 5. Report

PR number + URL. Do not delete the branch — the user merges via GitHub.
