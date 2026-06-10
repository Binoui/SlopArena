# Quality Tools Cheatsheet

Quick reference for maintaining code quality while coding fast.

## Daily Commands

```bash
make report    # Show full quality metrics (start of session)
make check     # Quick quality check (before push)
make build     # Just build
```

## What Gets Auto-Checked

### ❌ Blocks Commit (Pre-Commit Hook)
- Build fails
- Godot types in Shared/ (architecture violation)

### ⚠️ Warns But Allows (Pre-Commit Hook)
- >5 new debug logs in commit
- French comments detected
- *Can bypass with `y` or `git commit --no-verify`*

### 💡 IDE Suggestions (Real-Time)
- Unused variables
- Redundant code
- Performance tips
- *Ignore while vibing, fix when polishing*

## When Things Block You

### "Pre-commit hook says build failed"
```bash
dotnet build    # See the actual error
# Fix it, then commit normally
```

### "Detected 10 new debug logs"
```bash
# Option 1: Remove them (recommended)
git diff        # See what you added
# Remove the GD.Print() lines

# Option 2: Keep them (if temporary/urgent)
git commit --no-verify -m "temp: debugging X"
# Remember to clean up later!
```

### "Godot types in Shared/"
This is a hard block - Shared/ must stay pure C#.
```bash
git diff Shared/    # See what you changed
# Use System.MathF instead of Godot.Mathf
# Use plain C# types instead of Vector3/Transform3D
```

### "French comments detected"
Open-source project = English only.
```bash
# Option 1: Translate (quick)
# Option 2: Bypass if urgent
git commit --no-verify
```

## Bypassing Checks (Use Sparingly)

```bash
# Skip pre-commit hook entirely
git commit --no-verify -m "message"

# Skip CI checks (push straight to main)
# Can't skip - CI always runs
# But CI only warns, doesn't block merge
```

## Understanding the Metrics

### Debug Logs
- **Current:** ~23 statements
- **Target:** <50
- **Yellow:** 30-50
- **Red:** >50

High count = harder to debug, noise in logs.
Use F3 hitbox toggle instead of console spam.

### Method Complexity
- **Current:** 0 complex methods (>150 lines, non-setup)
- **Setup methods:** ~4 long methods (OK - _Ready, BuildUI, etc.)

Setup methods are **allowed** to be long. Only business logic methods should be short.

### Build Warnings
- **Current:** ~10 warnings (nullable refs, unused params)
- These don't block - fix when refactoring

## Philosophy

**Tools help, not hinder:**
- Most checks can be bypassed
- Focus on catching **obvious mistakes**
- Not about enforcing style
- You stay in control

**Use case:**
- Vibing code fast → ignore warnings, commit --no-verify if needed
- Before push → run `make check`, clean up obvious issues
- End of feature → review metrics, refactor if needed

## Common Patterns

### Adding Debug Logs Temporarily
```csharp
GD.Print("[DEBUG] X"); // OK while debugging
// Remove before final commit
```

### Setup Methods Can Be Long
```csharp
public override void _Ready() // 100+ lines OK
{
    // Sequential setup with fallbacks
}
```

### Business Logic Should Be Short
```csharp
private void ProcessAttack() // Aim for <50 lines
{
    // If >50: extract helpers
    CalculateDirection();  // ← extracted
    SpawnHitbox();         // ← extracted
}
```

## When to Refactor

**Do refactor:**
- Business logic method >150 lines
- Code duplicated 3+ times
- Hard to understand what it does

**Don't refactor:**
- Setup methods (_Ready, BuildUI)
- Declarative code (UI construction)
- Working code that's clear enough

## Questions?

See full docs:
- `QUALITY.md` - Complete system overview
- `CONTRIBUTING.md` - Project guidelines
- `CLAUDE.md` - Architecture rules
