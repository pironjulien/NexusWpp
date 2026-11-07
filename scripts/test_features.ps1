$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& node (Join-Path $PSScriptRoot 'tests\telemetry_state.test.js')
if($LASTEXITCODE -ne 0){throw 'JavaScript feature regressions failed.'}
& node (Join-Path $PSScriptRoot 'tests\i18n.test.js')
if($LASTEXITCODE -ne 0){throw 'Interface texts are missing in a language.'}
# C# tests on xUnit v3 (tests\NexusWpp.Tests): sensor, telemetry and alert rules, monitor coverage behind the pause, then
# the host, settings window, mouse hook, runtime pause and responsive layout harnesses (off screen, never activated).
# Explicit tests (real desktop, exhaustive layout) are left out unless asked for (README "Tests").
Push-Location $project
try { & dotnet test --project (Join-Path $project 'tests\NexusWpp.Tests\NexusWpp.Tests.csproj') --results-directory (Join-Path $project 'work\tests\results') }
finally { Pop-Location }
if($LASTEXITCODE -ne 0){throw 'C# tests (xUnit) failed.'}
