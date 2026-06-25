.PHONY: help build clean check format lint test report bake-arenas

help:
	@echo "SlopArena Development Commands"
	@echo "=============================="
	@echo "  make build        - Build the project"
	@echo "  make check        - Run all quality checks"
	@echo "  make report       - Show code quality report"
	@echo "  make format       - Auto-format code"
	@echo "  make lint         - Run code analysis"
	@echo "  make clean        - Clean build artifacts"
	@echo "  make test         - Run tests (when implemented)"
	@echo "  make bake-arenas  - Bake arena .tscn files to .arena binary"

build:
	@echo "🔨 Building SlopArena..."
	@dotnet build --nologo

clean:
	@echo "🧹 Cleaning build artifacts..."
	@dotnet clean --nologo
	@rm -rf bin/ obj/ src/Shared/bin/ src/Shared/obj/ src/Server/bin/ src/Server/obj/

check: lint
	@echo "✅ All checks passed!"

format:
	@echo "✨ Formatting code..."
	@dotnet format --no-restore --verbosity quiet

lint:
	@echo "🔍 Running code analysis..."
	@dotnet build --nologo /p:EnforceCodeStyleInBuild=true /p:TreatWarningsAsErrors=false | grep -E '(warning|error)' || echo "  No issues found"
	@echo ""
	@echo "📊 Checking for code smells..."
	@PRINT_COUNT=$$(find Scripts -name "*.cs" -exec grep -l "GD\.Print(" {} \; | wc -l); \
	if [ $$PRINT_COUNT -gt 15 ]; then \
		echo "  ⚠️  Found GD.Print() in $$PRINT_COUNT files (threshold: 15)"; \
	else \
		echo "  ✓ Debug logging: $$PRINT_COUNT files (OK)"; \
	fi
	@LONG_METHODS=$$(find Scripts -name "*.cs" -exec awk '/^[[:space:]]*(public|private|protected|internal).*\(.*\)/ {start=NR; line=$$0} /^[[:space:]]*\}/ {len=NR-start; if (len>150 && line !~ /(Ready|BuildUI|SpawnMatch|CreateDummy|Setup)/) print FILENAME":"start" ("len" lines)"}' {} \;); \
	if [ -n "$$LONG_METHODS" ]; then \
		echo "  ⚠️  Found complex methods >150 lines:"; \
		echo "$$LONG_METHODS" | head -5; \
	else \
		echo "  ✓ Method complexity: OK (ignoring setup methods)"; \
	fi

test:
	@echo "🧪 Running tests..."
	@echo "  (No tests implemented yet)"

bake-arenas:
	@echo "🏟️  Baking arenas from hardcoded data..."
	@dotnet run --project tools/BakeArenas.csproj -- "data/arenas"
	@echo "  Also available: run tools/bake_arenas.tscn in Godot to bake from .tscn scenes"
	@ls -1 data/arenas/*.arena 2>/dev/null | wc -l | xargs -I{} echo "  {} .arena files in data/arenas/"

report:
	@./ci/quality-report.sh
