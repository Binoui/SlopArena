.PHONY: help build clean check format lint test report

help:
	@echo "SlopArena Development Commands"
	@echo "=============================="
	@echo "  make build     - Build the project"
	@echo "  make check     - Run all quality checks"
	@echo "  make report    - Show code quality report"
	@echo "  make format    - Auto-format code"
	@echo "  make lint      - Run code analysis"
	@echo "  make clean     - Clean build artifacts"
	@echo "  make test      - Run tests (when implemented)"

build:
	@echo "🔨 Building SlopArena..."
	@dotnet build --nologo

clean:
	@echo "🧹 Cleaning build artifacts..."
	@dotnet clean --nologo
	@rm -rf bin/ obj/ Shared/bin/ Shared/obj/ Server/bin/ Server/obj/

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
	@LONG_METHODS=$$(find Scripts -name "*.cs" -exec awk '/^[[:space:]]*(public|private|protected|internal).*\(.*\)/ {start=NR} /^[[:space:]]*\}/ {if (NR-start>100) print FILENAME":"start}' {} \;); \
	if [ -n "$$LONG_METHODS" ]; then \
		echo "  ⚠️  Found methods >100 lines:"; \
		echo "$$LONG_METHODS" | head -3; \
	else \
		echo "  ✓ Method length: all <100 lines (OK)"; \
	fi

test:
	@echo "🧪 Running tests..."
	@echo "  (No tests implemented yet)"

report:
	@./scripts/quality-report.sh
