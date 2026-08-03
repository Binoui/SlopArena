#!/usr/bin/env bash
# Link gitignored local Unity packages (e.g. Packages/com.kybernetik.animancer —
# paid, never committed) from the main checkout into this worktree.
#
# Worktrees share the git index but NOT gitignored files, so a fresh worktree's
# client/Unity/Packages/ lacks Animancer and the Unity compile gate fails with
# "The type or namespace name 'Animancer' could not be found". Run this once per
# worktree before `$UNITY_EDITOR -batchmode -quit -projectPath <worktree>/client/Unity`.
#
# Symlinks (not copies) so package updates in the main checkout propagate and no
# disk is duplicated. Only links packages git ignores — tracked packages already
# exist in the worktree.
#
# Usage: scripts/setup-worktree-unity-packages.sh [MAIN_CHECKOUT]
#   MAIN_CHECKOUT defaults to ~/Documents/projects/SlopArena (the main repo).
set -euo pipefail

MAIN="${1:-$HOME/Documents/projects/SlopArena}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [ ! -d "$MAIN/client/Unity/Packages" ]; then
  echo "error: main checkout not found at '$MAIN' (pass it as \$1)" >&2
  exit 1
fi

linked=0
for pkg in "$MAIN"/client/Unity/Packages/*/; do
  [ -d "$pkg" ] || continue
  [ -f "$pkg/package.json" ] || continue

  name="$(basename "$pkg")"
  dest="$HERE/client/Unity/Packages/$name"
  [ -e "$dest" ] && continue # already present (copied or linked)

  if git -C "$HERE" check-ignore -q "client/Unity/Packages/$name/"; then
    ln -s "$pkg" "$dest"
    echo "linked $name -> $pkg"
    linked=$((linked + 1))
  fi
done

if [ "$linked" -eq 0 ]; then
  echo "no missing gitignored packages — worktree ready for the Unity gate."
fi
