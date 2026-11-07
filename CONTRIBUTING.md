# Contributing

## Environment

- Windows 10 or 11, the .NET SDK pinned by `global.json`, PowerShell 7, the Microsoft Edge WebView2 Runtime;
- Node.js on the PATH for the JavaScript tests.

## Rules

1. Open an issue or a pull request against `main`. A change is accepted only when `.\test.ps1` passes: the CI
   (`.github/workflows/ci.yml`) runs the same compilation and tests on every push and pull request, and the `Release`
   workflow runs `test.ps1` itself before publishing.
2. **Every new feature and every bug fix comes with its test**: the host, telemetry, settings and layout in
   `tests/NexusWpp.Tests` (xUnit v3, windows off screen, never activated), the interface scripts in `scripts/tests/`
   (`node`). A fix starts with a test that fails without it.
3. Every visible text is translated in `i18n.js`, in French and English (`scripts/tests/i18n.test.js` fails on a missing
   one). No fake button, fake data or feature that is not wired.
4. An optimization is kept only with a before/after measurement (`scripts/benchmark_nexuswpp.ps1`, then
   `scripts/compare_benchmark_result.ps1`, verdict `KEEP`).
5. Never commit `bin/`, `dist/`, `work/`, logs, temporary files or benchmark results. Commit messages and
   `CHANGELOG.md` entries (Keep a Changelog, under `[Unreleased]`) are written in English; every functional change has
   its entry.

## Checks

```powershell
.\test.ps1        # compile.ps1, then the JavaScript tests and dotnet test (scripts\test_features.ps1); offline
git diff --check
```

Run one `test.ps1` at a time per copy of the repository: two runs would share the binaries and WebView2 profiles of
`work\` (`test.ps1` takes a lock).

## Version

The `VERSION` file (four parts, the Store version) is the single source of the version for the EXE, the installer and
the MSIX. A push to `main` whose `VERSION` has no `v<VERSION>` release yet, with its `## [<VERSION>]` section in
`CHANGELOG.md`, makes the `Release` workflow (`.github/workflows/release.yml`) run `test.ps1`, build, attest and publish
that release.
