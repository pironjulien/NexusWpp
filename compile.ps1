$ErrorActionPreference = "Stop"

$binDir = Join-Path $PSScriptRoot "bin"
$project = Join-Path $PSScriptRoot "NexusWpp.csproj"
$outputExe = Join-Path $binDir "nexuswpp.exe"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET 10 SDK is required: https://dotnet.microsoft.com/download"
}

# The version stamp comes from the VERSION file (Directory.Build.targets, checked like scripts\read_version.ps1).

Write-Host "Building NexusWpp.csproj into bin\nexuswpp.exe..." -ForegroundColor Cyan
& dotnet build $project -c Release -o $binDir --nologo -v quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0 -or !(Test-Path $outputExe)) {
    Write-Error "Compilation failed. dotnet build exited with code $LASTEXITCODE."
}

Write-Host "`n[SUCCESS] nexuswpp.exe has been compiled successfully!" -ForegroundColor Green
Write-Host "Location: $outputExe" -ForegroundColor Green
