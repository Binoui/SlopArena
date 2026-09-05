#!/usr/bin/env python3
"""Check living documentation for broken links and obsolete workflow guidance.

Run from the repository root with ``python3 tools/check_docs.py``. The checker is
intentionally small: it validates paths and a short list of known migration traps,
not generated packet sizes, test totals, or other volatile implementation details.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent.parent

# These are the current instructional surfaces. ADRs, plans, research, generated
# reports, handoffs, and archived tool skills remain records and are not linted for
# present-tense workflow vocabulary.
LIVING_DOCS = {
    Path("README.md"),
    Path("CONTRIBUTING.md"),
    Path("CONTEXT.md"),
    Path(".omp/AGENTS.md"),
    Path("docs/README.md"),
    Path("docs/architecture-overview.md"),
    Path("docs/testing.md"),
    Path("docs/characters/adding-a-new-character.md"),
    Path("docs/characters/fightguy.md"),
    Path("docs/systems/ability-architecture.md"),
    Path("docs/systems/ability-lab.md"),
    Path("docs/systems/animation-system.md"),
    Path("docs/systems/combat-systems.md"),
    Path("docs/systems/hitstun-di.md"),
    Path("docs/systems/netcode-architecture.md"),
    Path("docs/contributing/conventions.md"),
    Path("docs/contributing/unity-cli.md"),
    Path(".omp/skills/sloparena-character-workflow/SKILL.md"),
    Path(".omp/skills/sloparena-combat-engine/SKILL.md"),
}

CANONICAL_PATHS = {
    "src/Shared",
    "src/Server",
    "tests/Shared.Tests",
    "client/Unity/Assets/CharacterPackages",
    "content-cooked",
    "docs/characters/adding-a-new-character.md",
    "docs/characters/fightguy.md",
    "docs/systems/ability-architecture.md",
    "docs/systems/ability-lab.md",
    "docs/systems/animation-system.md",
    "docs/systems/combat-systems.md",
    "docs/systems/netcode-architecture.md",
    "docs/testing.md",
    "docs/contributing/conventions.md",
    "docs/contributing/unity-cli.md",
}

FORBIDDEN_CURRENT_TERMS = (
    (re.compile(r"\bGodot\b|_PhysicsProcess|MoveAndSlide|\bGD\."), "Godot workflow"),
    (re.compile(r"\bBuildFightGuy\b|\bBuild<Name>\b|\bBuildRegistry\b"), "registry factory workflow"),
    (re.compile(r"\bCharacterRegistry\b"), "global character registry workflow"),
    (re.compile(r"\bCharacterDefinition\.cs\b"), "legacy character-definition file workflow"),
    (re.compile(r"\bCharacterAnimationConfig\b"), "universal manual animation-config workflow"),
)

# A forbidden term is acceptable when a living guide explicitly scopes it to the
# legacy path or warns readers not to use it. This keeps migration notices useful.
LEGACY_OR_WARNING = re.compile(
    r"legacy|historical|superseded|deprecated|modification.only|not a template|"
    r"do not|must not|never|avoid|not universal|not a runtime|not current|compatibility|"
    r"\bno\b",
    re.IGNORECASE,
)

# Inline Markdown links. Reference-style links are intentionally outside this
# check because their destinations are not recoverable without a full Markdown AST.
MARKDOWN_LINK = re.compile(r"(?<!!)\[[^\]]*\]\(([^)\s]+)(?:\s+[^)]*)?\)")


def markdown_files() -> list[Path]:
    try:
        result = subprocess.run(
            [
                "git",
                "ls-files",
                "-z",
                "--cached",
                "--others",
                "--exclude-standard",
                "--",
                "*.md",
            ],
            cwd=ROOT,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=True,
        )
    except (OSError, subprocess.CalledProcessError) as error:
        diagnostic = getattr(error, "stderr", None) or str(error)
        raise RuntimeError(
            f"unable to enumerate project Markdown: {os.fsdecode(diagnostic).strip()}"
        ) from error

    root = ROOT.resolve()
    paths = {
        Path(os.fsdecode(item))
        for item in result.stdout.split(b"\0")
        if item
    }
    return sorted(
        path
        for path in paths
        if path.suffix == ".md"
        and (ROOT / path).is_file()
        and (ROOT / path).resolve().is_relative_to(root)
    )


def is_historical(path: Path) -> bool:
    historical_dirs = {
        Path("docs/adr"),
        Path("docs/plans"),
        Path("docs/research"),
        Path("docs/generated"),
        Path("docs/handoffs"),
        Path("docs/superpowers"),
        Path(".claude"),
    }
    return any(path == directory or directory in path.parents for directory in historical_dirs)


def resolve_link(source: Path, target: str) -> Path | None:
    target = unquote(target.strip("<>"))
    if not target or target.startswith("#"):
        return source
    parsed = urlsplit(target)
    if parsed.scheme or parsed.netloc:
        return None
    raw_path = parsed.path
    if not raw_path:
        return source
    candidate = (ROOT / raw_path.lstrip("/")) if raw_path.startswith("/") else (ROOT / source.parent / raw_path)
    return candidate.resolve().relative_to(ROOT.resolve()) if candidate.resolve().is_relative_to(ROOT.resolve()) else candidate


def check_links(errors: list[str], files: list[Path]) -> None:
    for source in files:
        text = (ROOT / source).read_text(encoding="utf-8", errors="replace")
        for match in MARKDOWN_LINK.finditer(text):
            target = match.group(1)
            if target.startswith(("http://", "https://", "mailto:", "ftp://")):
                continue
            resolved = resolve_link(source, target)
            if resolved is None:
                continue
            full = ROOT / resolved
            if not full.exists():
                errors.append(f"{source}: broken internal link {target}")


def check_forbidden_terms(errors: list[str], files: list[Path]) -> None:
    for source in files:
        if not (
            source in LIVING_DOCS
            or source == Path("CLAUDE.md")
            or (len(source.parts) >= 2 and source.parts[:2] == (".omp", "skills"))
        ):
            continue
        lines = (ROOT / source).read_text(encoding="utf-8", errors="replace").splitlines()
        for line_number, line in enumerate(lines, 1):
            context = "\n".join(lines[max(0, line_number - 3):line_number])
            if LEGACY_OR_WARNING.search(context):
                continue
            for pattern, label in FORBIDDEN_CURRENT_TERMS:
                if pattern.search(line):
                    errors.append(f"{source}:{line_number}: forbidden current-doc term ({label})")


def check_canonical_paths(errors: list[str]) -> None:
    for relative in sorted(CANONICAL_PATHS):
        if not (ROOT / relative).exists():
            errors.append(f"missing canonical path: {relative}")


def main() -> int:
    errors: list[str] = []
    try:
        files = markdown_files()
    except RuntimeError as error:
        print("Documentation checks failed:")
        print(f"- {error}")
        return 1
    check_links(errors, files)
    check_forbidden_terms(errors, files)
    check_canonical_paths(errors)
    if errors:
        print("Documentation checks failed:")
        for error in errors:
            print(f"- {error}")
        return 1
    print(f"Documentation checks passed ({len(files)} Markdown files scanned).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
