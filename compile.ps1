$ErrorActionPreference = "Stop"

$binDir = Join-Path $PSScriptRoot "bin"
$project = Join-Path $PSScriptRoot "NexusWpp.csproj"
$outputExe = Join-Path $binDir "nexuswpp.exe"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK (8 or later) is required: https://dotnet.microsoft.com/download"
}

# Version stamped from the VERSION file.
$version = & (Join-Path $PSScriptRoot 'scripts\read_version.ps1')
$versionDir = Join-Path $PSScriptRoot 'work\obj'
if (!(Test-Path $versionDir)) { New-Item -ItemType Directory -Path $versionDir -Force | Out-Null }
@"
using System.Reflection;
[assembly: AssemblyVersion("$version")]
[assembly: AssemblyFileVersion("$version")]
namespace NexusWpp { internal static class ReleaseVersion { internal const string Value = "$version"; } }
"@ | Set-Content -LiteralPath (Join-Path $versionDir 'ReleaseVersion.cs') -Encoding UTF8

Write-Host "Building NexusWpp.csproj into bin\nexuswpp.exe..." -ForegroundColor Cyan
& dotnet build $project -c Release -o $binDir --nologo -v quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0 -or !(Test-Path $outputExe)) {
    Write-Error "Compilation failed. dotnet build exited with code $LASTEXITCODE."
}

Write-Host "`n[SUCCESS] nexuswpp.exe has been compiled successfully!" -ForegroundColor Green
Write-Host "Location: $outputExe" -ForegroundColor Green
