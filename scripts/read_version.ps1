$ErrorActionPreference = 'Stop'
$nexusVersionText = (Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\VERSION') -Raw).Trim()
if ($nexusVersionText -notmatch '^\d+\.\d+\.\d+\.0$') { throw 'VERSION must contain a four-part Store version ending in .0.' }
$nexusParsedVersion = [version]$nexusVersionText
if ($nexusParsedVersion.Major -lt 1 -or $nexusParsedVersion.Major -gt 65535 -or $nexusParsedVersion.Minor -gt 65535 -or $nexusParsedVersion.Build -gt 65535) {
    throw 'VERSION is outside MSIX version limits.'
}
$nexusVersionText
