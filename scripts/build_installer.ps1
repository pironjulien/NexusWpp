$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$distDir = Join-Path $projectRoot "dist"
$payloadDir = Join-Path $distDir "payload"
$payloadZip = Join-Path $distDir "NexusWppPayload.zip"
$installerExe = Join-Path $distDir "NexusWppSetup.exe"
$installerProject = Join-Path $projectRoot "installer\NexusWppInstaller.csproj"
$installerPublishDir = Join-Path $projectRoot "work\installer-publish"

function Get-SignToolPath {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if ($command) { return $command }

    $sdkRoots = @(
        "C:\Program Files (x86)\Windows Kits\10\bin",
        "C:\Program Files\Windows Kits\10\bin"
    )

    foreach ($root in $sdkRoots) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    return $null
}

if (-not [IO.Path]::GetFullPath($payloadDir).StartsWith([IO.Path]::GetFullPath($distDir) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid payload directory.' }
if (Test-Path -LiteralPath $payloadDir) {
    Remove-Item -LiteralPath $payloadDir -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

$payloadFiles = & (Join-Path $PSScriptRoot 'application_files.ps1')

foreach ($file in $payloadFiles) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $payloadDir $file) -Force
}

# The self-contained host: nexuswpp.exe, its .NET 10 runtime and WebView2.
& (Join-Path $PSScriptRoot 'publish_host.ps1') -Destination $payloadDir

Compress-Archive -Path (Join-Path $payloadDir "*") -DestinationPath $payloadZip -Force

# One self-contained, compressed .NET 10 exe that embeds the payload: it runs on a bare Windows.
& dotnet publish $installerProject -c Release -o $installerPublishDir "-p:PayloadZip=$payloadZip" --nologo -v quiet -clp:ErrorsOnly
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $installerPublishDir "NexusWppSetup.exe"))) {
    throw "Installer publication failed with exit code $LASTEXITCODE."
}
Copy-Item -LiteralPath (Join-Path $installerPublishDir "NexusWppSetup.exe") -Destination $installerExe -Force

# Signed only with a real code-signing certificate. Otherwise the installer stays unsigned: a self-signed
# certificate proves nothing to Windows; the GitHub release attests the file instead (README, "Releases").
$signToolPath = $null
if ($env:NEXUSWPP_SIGN_CERT_THUMBPRINT -or $env:NEXUSWPP_SIGN_PFX) {
    $signToolPath = Get-SignToolPath
    if (!$signToolPath) {
        throw "signtool.exe not found. Install the Windows SDK or add signtool.exe to PATH."
    }
}

$timestampUrl = if ($env:NEXUSWPP_TIMESTAMP_URL) { $env:NEXUSWPP_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }
if ($env:NEXUSWPP_SIGN_CERT_THUMBPRINT) {
    Write-Host "Signing installer with certificate thumbprint from NEXUSWPP_SIGN_CERT_THUMBPRINT..." -ForegroundColor Cyan
    & $signToolPath sign /fd SHA256 /tr $timestampUrl /td SHA256 /sha1 $env:NEXUSWPP_SIGN_CERT_THUMBPRINT $installerExe
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
} elseif ($env:NEXUSWPP_SIGN_PFX) {
    Write-Host "Signing installer with PFX from NEXUSWPP_SIGN_PFX..." -ForegroundColor Cyan
    $pfxArgs = @("sign", "/fd", "SHA256", "/tr", $timestampUrl, "/td", "SHA256", "/f", $env:NEXUSWPP_SIGN_PFX)
    if ($env:NEXUSWPP_SIGN_PFX_PASSWORD) {
        $pfxArgs += @("/p", $env:NEXUSWPP_SIGN_PFX_PASSWORD)
    }
    $pfxArgs += $installerExe
    & $signToolPath @pfxArgs
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }
} else {
    Write-Host "Installer left unsigned (no NEXUSWPP_SIGN_CERT_THUMBPRINT or NEXUSWPP_SIGN_PFX)." -ForegroundColor Yellow
}

Remove-Item -LiteralPath $payloadDir -Recurse -Force
Remove-Item -LiteralPath $payloadZip -Force

Get-Item -LiteralPath $installerExe | Select-Object FullName, Length, LastWriteTime
