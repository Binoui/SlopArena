# Code Quality and Verification

Use these commands from the repository root. There is no repository Makefile or
project pre-commit quality hook. The existing `.githooks` handle session logs and
commit messages rather than project build/style checks. Do not rely on `make build`,
`make check`, `make format`, `make clean`, or `make test`.

## Applicable checks

The CI workflow runs documentation checks and Shared/Server build and tests on
push to `main` and on pull requests. These are the available project verification
commands:

```bash
python3 -m unittest discover -s tools -p 'test_check_docs.py'
python3 tools/check_docs.py
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo
dotnet build src/Server/ --nologo
dotnet test tests/Server.Tests/ --nologo
```

Choose checks for the changed surface rather than treating unrelated layers as
required. See [Testing and Verification](../testing.md) for local iteration,
integrated changes, package workflows, and distributable-demo verification.
Shared builds run `CopyToUnity`, copying the Shared DLL and dependency closure
into `client/Unity/Assets/Plugins/SlopArena.Shared/` unless `SkipUnityPluginCopy`
is explicitly set. For an open Editor, coordinate saved-source writers, acquire
your own bounded gateway hold before that build, and let imports/compilation
settle before ending the hold. Never copy into another owner's Editor window.

The documentation checker validates Markdown links, selected current vocabulary,
and simple executable targets/paths in this guide, including the documented
unittest discovery suite. It does not interpret shell programs or prove that
a command succeeds. Its tests are run in CI by `.github/workflows/ci.yml`.

## EditorConfig and analyzers

`.editorconfig` configures indentation, whitespace, C# style and selected
diagnostic severities. It does not enforce rules by itself in every editor.
Analyzer severities are configured there (including selected CA, Roslynator, and
IDE diagnostics); they are not a generic promise that unused variables,
performance problems, or code smells are all detected or block commits. Build
and test results from the applicable .NET projects are the available automated
compile/test gates. Update `.editorconfig` when changing analyzer configuration.

## Suppressions

Use analyzer suppressions only when justified and scoped to the specific
diagnostic. `.editorconfig` can set a diagnostic's severity, and C# supports
`#pragma warning disable` / `restore`; these alter diagnostics, not CI workflow
or repository-wide quality checks.
