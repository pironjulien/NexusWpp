param(
    [string]$OutputPath = ".\scripts\last-hdr-dxgi-probe.json"
)

$ErrorActionPreference = "Stop"

$source = Join-Path $PSScriptRoot "hdr_dxgi_probe.cs"
$outDir = Join-Path $PSScriptRoot "bin"
$exe = Join-Path $outDir "hdr_dxgi_probe.exe"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (!(Test-Path $source)) {
    throw "Source not found: $source"
}
if (!(Test-Path $csc)) {
    throw "csc.exe not found: $csc"
}
if (!(Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

& $csc /nologo /target:exe /out:$exe $source | Out-Null
if (!(Test-Path $exe)) {
    throw "Probe compilation failed: $exe"
}

$json = & $exe
if ($LASTEXITCODE -ne 0) {
    throw "Probe failed with exit code $LASTEXITCODE`: $json"
}

$dir = Split-Path $OutputPath -Parent
if ($dir -and !(Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$json | Set-Content -Path $OutputPath -Encoding UTF8
$json | ConvertFrom-Json
