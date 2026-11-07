param([string]$Assets = '', [string]$Label = 'graphics')
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $Assets) { $Assets = $project }
$output = Join-Path $project ('work\' + ($Label -replace '[^a-zA-Z0-9_-]', '_'))
$binDir = Join-Path $project 'work\tools\GraphicsProfile\publish'
New-Item -ItemType Directory -Path $output -Force | Out-Null
# A real project (tools\GraphicsProfile) with the host's runtime, WebView2 version and analysis.
& dotnet build (Join-Path $project 'tools\GraphicsProfile\GraphicsProfile.csproj') -c Release -o $binDir --nologo -v quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { throw 'Graphics profile compilation failed.' }
& (Join-Path $binDir 'GraphicsProfile.exe') $project $Assets $output
if ($LASTEXITCODE -ne 0) { throw 'Graphics profile failed.' }
