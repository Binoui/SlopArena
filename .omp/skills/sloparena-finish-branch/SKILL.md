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
git reset --soft "$(git merge-base HEAD main)" && git commit -m "<subject>"
```

`<subject>` = the last commit's subject (or the user-supplied arg). Clean tree / nothing staged → report and stop.

## 4. Push + PR

```bash
git push -u origin HEAD
gh pr create --title "<subject>" --body "<body>"
```

`<body>` = `git log main..HEAD --format='- %s'` + diffstat (`git diff --stat main...HEAD`).

If `gh` is unavailable → print the push output and the repo URL (`https://github.com/Binoui/SlopArena`) instead, and stop there.

## 5. Report

PR number + URL. Do not delete the branch — the user merges via GitHub.
