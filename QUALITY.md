# Code Quality Tools

## Overview
SlopArena uses automated tools to maintain code quality without slowing you down.

## Quick Commands

```bash
make build    # Build the project
make check    # Run all quality checks
make format   # Auto-format code
make clean    # Clean build artifacts
```

## Automated Checks

### 1. Pre-Commit Hook (Local)
Runs before every `git commit`:
- ✅ Build verification
- ⚠️  Debug log detection (warns if >5 new logs)
- ⚠️  French comment detection
- ❌ Godot types in Shared/ (blocks commit)

**Location:** `.githooks/pre-commit`

**Skip if needed:** `git commit --no-verify`

### 2. CI/CD (GitHub Actions)
Runs on every push/PR:
- Build check
- Godot types in Shared/ check
- Debug log count (warning at >50)
- French comment detection (warning only)

**Location:** `.github/workflows/build.yml`

### 3. EditorConfig
Enforces code style automatically in your IDE:
- 4 spaces indentation
- Unix line endings (LF)
- Trailing whitespace removal
- Private fields with `_` prefix

**Location:** `.editorconfig`

### 4. Roslyn Analyzers
Compile-time checks via Roslynator:
- Unused variables/parameters
- Redundant code
- Performance issues
- Code smells

**Configured in:** `SlopArena.csproj` + `.editorconfig`

## What Gets Checked

### ✅ Automatic (Blocks Commit/CI)
- **Build errors** - Must compile
- **Godot in Shared/** - Pure C# only

### ⚠️ Warnings (Prompts User)
- **Debug logs** - >5 new logs or >50 total
- **French comments** - Should be English
- **Long methods** - >100 lines

### 💡 Suggestions (IDE Only)
- Unused private members
- Redundant casts
- Simplification opportunities

## Quality Metrics

Current state (as of last check):
- Debug logs: ~32 statements (target: <50)
- Avg method length: ~30 lines (target: <50)
- Build warnings: ~10 (mostly nullable refs)

## Adding New Checks

### Pre-commit hook
Edit `.githooks/pre-commit` and add your check:
```bash
# 5. Check for large files
LARGE_FILES=$(git diff --cached --name-only | xargs ls -lh | awk '$5 > 1000000')
if [ -n "$LARGE_FILES" ]; then
    echo "❌ Large files detected (>1MB)"
    exit 1
fi
```

### CI workflow
Edit `.github/workflows/build.yml` under the "Code Quality Checks" step.

### Analyzer rules
Edit `.editorconfig` and add severity levels:
```ini
dotnet_diagnostic.CA1234.severity = warning
```

## Disabling Checks

**Not recommended**, but if you need to:

### Skip pre-commit hook
```bash
git commit --no-verify -m "message"
```

### Disable specific analyzer
Add to `.editorconfig`:
```ini
dotnet_diagnostic.RCS1234.severity = none
```

### Suppress in code (last resort)
```csharp
#pragma warning disable RCS1234
// code here
#pragma warning restore RCS1234
```

## Philosophy

These tools are **guardrails, not roadblocks**:
- Most checks are **warnings** that can be bypassed
- Only **critical issues** block commits (build errors, Shared/ violations)
- Focus is on **catching obvious mistakes**, not enforcing style
- Goal is **fast feedback**, not slow bureaucracy

When vibing code fast, you can bypass warnings. But take 30s before pushing to review them.
