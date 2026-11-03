# Self-contained host (nexuswpp.exe, its .NET 10 runtime and WebView2) published alone into a
# folder, for the MSIX, the installer payload and the standalone deployment.
param([Parameter(Mandatory = $true)][string]$Destination)
$ErrorActionPreference = 'Stop'

$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
# Stamps the version from the VERSION file and builds.
& (Join-Path $project 'compile.ps1') | Out-Null

& dotnet publish (Join-Path $project 'NexusWpp.csproj') -c Release -o $Destination --nologo -v quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $Destination 'nexuswpp.exe'))) {
    throw "Publishing NexusWpp failed. dotnet publish exited with code $LASTEXITCODE."
}
