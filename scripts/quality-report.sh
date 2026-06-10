#!/bin/bash
# Generate a quick quality report for SlopArena

echo "═══════════════════════════════════════════════════"
echo "  SlopArena Code Quality Report"
echo "═══════════════════════════════════════════════════"
echo ""

# Lines of code
echo "📏 Code Size"
SCRIPTS_LOC=$(find Scripts -name "*.cs" | xargs wc -l 2>/dev/null | tail -1 | awk '{print $1}')
SHARED_LOC=$(find Shared -name "*.cs" | xargs wc -l 2>/dev/null | tail -1 | awk '{print $1}')
TOTAL_LOC=$((SCRIPTS_LOC + SHARED_LOC))
echo "  Scripts:  ${SCRIPTS_LOC} lines"
echo "  Shared:   ${SHARED_LOC} lines"
echo "  Total:    ${TOTAL_LOC} lines"
echo ""

# File count
FILES=$(find Scripts Shared -name "*.cs" | wc -l)
echo "📁 Files: $FILES C# scripts"
echo ""

# Debug logs
DEBUG_LOGS=$(grep -r "GD\.Print(" Scripts --include="*.cs" 2>/dev/null | wc -l)
DEBUG_FILES=$(grep -rl "GD\.Print(" Scripts --include="*.cs" 2>/dev/null | wc -l)
echo "🐛 Debug Logging"
echo "  Total GD.Print(): $DEBUG_LOGS"
echo "  Files with logs:  $DEBUG_FILES"
if [ $DEBUG_LOGS -gt 50 ]; then
    echo "  Status: ⚠️  High (>50)"
elif [ $DEBUG_LOGS -gt 30 ]; then
    echo "  Status: ⚡ Moderate (30-50)"
else
    echo "  Status: ✅ Low (<30)"
fi
echo ""

# Method complexity (methods >50 lines)
LONG_METHODS=$(find Scripts -name "*.cs" -exec awk '/^[[:space:]]*(public|private|protected|internal).*\(.*\)/ {start=NR} /^[[:space:]]*\}/ {if (NR-start>50) print FILENAME":"start}' {} \; 2>/dev/null | wc -l)
echo "📐 Method Complexity"
echo "  Methods >50 lines: $LONG_METHODS"
if [ $LONG_METHODS -eq 0 ]; then
    echo "  Status: ✅ All methods concise"
elif [ $LONG_METHODS -lt 5 ]; then
    echo "  Status: ⚡ Mostly good"
else
    echo "  Status: ⚠️  Consider refactoring"
fi
echo ""

# Recent activity
COMMITS_TODAY=$(git log --since="midnight" --oneline | wc -l)
COMMITS_WEEK=$(git log --since="7 days ago" --oneline | wc -l)
echo "📈 Recent Activity"
echo "  Commits today: $COMMITS_TODAY"
echo "  Commits this week: $COMMITS_WEEK"
echo "  Last commit: $(git log -1 --format='%ar')"
echo ""

# Top contributors (if in a team)
echo "👥 Top Contributors (last 30 days)"
git shortlog -sn --since="30 days ago" | head -3
echo ""

# Build status
echo "🔨 Build Status"
if dotnet build --nologo --verbosity quiet > /dev/null 2>&1; then
    echo "  ✅ Build successful"
else
    echo "  ❌ Build failing"
fi
echo ""

echo "═══════════════════════════════════════════════════"
echo "  Report generated: $(date '+%Y-%m-%d %H:%M')"
echo "═══════════════════════════════════════════════════"
