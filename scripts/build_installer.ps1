$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$distDir = Join-Path $projectRoot "dist"
$payloadDir = Join-Path $distDir "payload"
$payloadZip = Join-Path $distDir "NexusWppPayload.zip"
$installerExe = Join-Path $distDir "NexusWppSetup.exe"
$installerSource = Join-Path $projectRoot "installer\NexusWppInstaller.cs"
$installerManifest = Join-Path $projectRoot "installer\NexusWppInstaller.manifest"
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$signToolPath = (Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source)

if (!(Test-Path -LiteralPath $cscPath)) {
    throw "C# compiler not found at $cscPath"
}

& (Join-Path $projectRoot "compile.ps1")

if (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

$payloadFiles = @(
    "app.js",
    "style.css",
    "index.html",
    "loading-zero-5120x1440.png",
    "julienpiron.png",
    "splash.jpg",
    "icon.ico"
)

foreach ($file in $payloadFiles) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $payloadDir $file) -Force
}

$payloadScriptsDir = Join-Path $payloadDir "scripts"
New-Item -ItemType Directory -Path $payloadScriptsDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot "scripts\generate_loading_snapshot.ps1") -Destination (Join-Path $payloadScriptsDir "generate_loading_snapshot.ps1") -Force

$binFiles = @(
    "nexuswpp.exe",
    "Microsoft.Web.WebView2.Core.dll",
    "Microsoft.Web.WebView2.WinForms.dll",
    "WebView2Loader.dll"
)

foreach ($file in $binFiles) {
    $src = Join-Path $projectRoot ("bin\" + $file)
    if (!(Test-Path -LiteralPath $src)) {
        throw "Missing binary payload file: $src"
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $payloadDir $file) -Force
}

Compress-Archive -Path (Join-Path $payloadDir "*") -DestinationPath $payloadZip -Force

$compilerArgs = @(
    "/target:winexe",
    "/out:$installerExe",
    "/win32icon:$(Join-Path $projectRoot "icon.ico")",
    "/win32manifest:$installerManifest",
    "/resource:$payloadZip,NexusWpp.Payload.zip",
    "/reference:System.IO.Compression.dll",
    "/reference:System.IO.Compression.FileSystem.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Management.dll",
    "/reference:System.Windows.Forms.dll",
    $installerSource
)

$process = Start-Process -FilePath $cscPath -ArgumentList $compilerArgs -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $installerExe)) {
    throw "Installer compilation failed with exit code $($process.ExitCode)."
}

if ($signToolPath) {
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
        Write-Host "signtool.exe found, but no signing certificate env var was provided. Skipping signing." -ForegroundColor Yellow
    }
} else {
    Write-Host "signtool.exe not found. Installer will be unsigned." -ForegroundColor Yellow
}

Remove-Item -LiteralPath $payloadDir -Recurse -Force
Remove-Item -LiteralPath $payloadZip -Force

Get-Item -LiteralPath $installerExe | Select-Object FullName, Length, LastWriteTime
