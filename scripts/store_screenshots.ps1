# Microsoft Store screenshots rendered by the real page with this PC's real telemetry, then the real settings window
# (shown on top for its capture, never activated). An explicit xUnit test: HarnessTests.StoreScreenshotCapture.
param([int]$WarmupSeconds = 70, [string]$Output)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$outputDirectory = if ($Output) { [IO.Path]::GetFullPath($Output) } else { Join-Path $project 'dist\store-screenshots' }
$env:NEXUSWPP_STORE_WARMUP = $WarmupSeconds
$env:NEXUSWPP_STORE_OUTPUT = $outputDirectory
try {
    & dotnet test --project (Join-Path $project 'tests\NexusWpp.Tests\NexusWpp.Tests.csproj') --filter-method 'NexusWpp.Tests.HarnessTests.StoreScreenshotCapture' --explicit only --output Detailed
    if ($LASTEXITCODE -ne 0) { throw "Store screenshot capture failed (code $LASTEXITCODE)." }
} finally { Remove-Item Env:NEXUSWPP_STORE_WARMUP, Env:NEXUSWPP_STORE_OUTPUT -ErrorAction SilentlyContinue }
Get-ChildItem $outputDirectory -Filter *.png | Select-Object Name, Length
