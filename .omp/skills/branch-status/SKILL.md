---
name: branch-status
description: Show status of all local feature branches vs main — commits ahead/behind, last message, open PR if any. Also helps draft a squash-merge PR description. Run at session start or before switching branches.
disable-model-invocation: false
---

# branch-status

Surface the state of all feature branches at a glance and optionally draft a PR.

## Step 1: Print branch summary

Run:
```bash
cd ~/Projects/SlopArena
while IFS= read -r b; do
  ahead=$(git rev-list --count main.."$b" 2>/dev/null)
  behind=$(git rev-list --count "$b"..main 2>/dev/null)
  last=$(git log -1 --format="%s" "$b" 2>/dev/null)
  pr=$(gh pr list --state open --head "$b" --json number,title --jq '.[0] | "#\(.number) \(.title)"' 2>/dev/null)
  [ -z "$pr" ] && pr="(no PR)"
  echo "  $b  (+$ahead/-$behind)  $last  $pr"
done < <(git branch --format='%(refname:short)' | grep -v '^main$')
```

## Step 2: If user asks for a PR draft

For the target branch, run:
```bash
git log main..<branch> --oneline --no-merges
```

Format a squash-merge PR body using these sections:
- **Description**: what the branch does (1-3 sentences)
- **Type of Change**: bug fix / new feature / refactor / docs (pick one)
- **Related Issues**: issue numbers if any, else N/A
- **Testing**: which `dotnet test` filter was run and result; if Unity changes, what was tested in-editor
- **Checklist**:
  - [ ] `dotnet build src/Shared/ --nologo` — 0 errors
  - [ ] `Shared/` has zero `UnityEngine.*` or `Godot.*` imports
  - [ ] All durations use `ushort` ticks, no `float` seconds in Shared
  - [ ] Allman braces in C# (`{` on its own line)

## Step 3: If user asks to create the PR

> Before using `gh pr create`, verify auth: `gh auth status`. If not authenticated, run `gh auth login` first.

> **STOP — MUST confirm with user before running this command.** Creating a PR is irreversible from the model's perspective. Do not proceed until the user explicitly says "go ahead" or "create it".

```bash
gh pr create --title "<title>" --body "<body>" --base main --head <branch>
```
