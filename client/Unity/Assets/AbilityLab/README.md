# Ability Lab

## Purpose

Ability Lab is SlopArena's internal fighter/move authoring and debugging environment.

## Ownership

Ability Lab owns editor and preview tooling.

Core gameplay data and simulation remain owned by SlopArena/shared runtime code.

## Direction

The tool may eventually become the basis of a standalone creator tool, so Ability Lab-specific code should stay isolated from unrelated game code.

## Dependency rule

Prefer:

```text
AbilityLab → SlopArena gameplay/shared code
```

Avoid:

```text
SlopArena gameplay → AbilityLab
```
