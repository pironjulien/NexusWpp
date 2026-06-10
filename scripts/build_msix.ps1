param(
    [switch]$SkipSigning
)

$ErrorActionPreference = "Stop"

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$distDir = Join-Path $projectRoot "dist\msix"
$packageDir = Join-Path $distDir "package"
$assetsDir = Join-Path $packageDir "Assets"
$version = "1.0.4.0"
$identityName = "julienpiron.fr.NexusWpp"
$msixPath = Join-Path $distDir ($identityName + "_" + $version + "_x64.msix")
$publisher = "CN=C3E3A6F0-11D2-4EE1-B3F2-34EED4CAE7FA"
$publisherDisplayName = "JULIENPIRON.FR"
$makeAppx = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe"
$signTool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"

if (!(Test-Path -LiteralPath $makeAppx)) {
    throw "makeappx.exe not found at $makeAppx"
}
if (!(Test-Path -LiteralPath $signTool)) {
    throw "signtool.exe not found at $signTool"
}

& (Join-Path $projectRoot "compile.ps1")

if (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

$payloadFiles = @(
    "app.js",
    "style.css",
    "index.html",
    "julienpiron.png",
    "splash.jpg",
    "icon.ico"
)

foreach ($file in $payloadFiles) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $packageDir $file) -Force
}

$binFiles = @(
    "nexuswpp.exe",
    "Microsoft.Web.WebView2.Core.dll",
    "Microsoft.Web.WebView2.WinForms.dll",
    "WebView2Loader.dll"
)

foreach ($file in $binFiles) {
    Copy-Item -LiteralPath (Join-Path $projectRoot ("bin\" + $file)) -Destination (Join-Path $packageDir $file) -Force
}

Add-Type -AssemblyName System.Drawing
function New-LogoPng {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.Clear([System.Drawing.Color]::FromArgb(11, 15, 20))

            $logo = [System.Drawing.Image]::FromFile((Join-Path $projectRoot "julienpiron.png"))
            try {
                $size = [Math]::Min($Width, $Height) - 12
                $x = [Math]::Round(($Width - $size) / 2)
                $y = [Math]::Round(($Height - $size) / 2)
                $graphics.DrawImage($logo, $x, $y, $size, $size)
            } finally {
                $logo.Dispose()
            }
        } finally {
            $graphics.Dispose()
        }

        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }
}

New-LogoPng -Path (Join-Path $assetsDir "StoreLogo.png") -Width 50 -Height 50
New-LogoPng -Path (Join-Path $assetsDir "Square44x44Logo.png") -Width 44 -Height 44
New-LogoPng -Path (Join-Path $assetsDir "Square150x150Logo.png") -Width 150 -Height 150

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 rescap">
  <Identity Name="$identityName" Publisher="$publisher" Version="$version" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>NexusWpp</DisplayName>
    <PublisherDisplayName>$publisherDisplayName</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26200.0" />
  </Dependencies>
  <Resources>
    <Resource Language="fr-fr" />
  </Resources>
  <Applications>
    <Application
      Id="NexusWpp"
      Executable="nexuswpp.exe"
      EntryPoint="Windows.FullTrustApplication"
      uap10:RuntimeBehavior="packagedClassicApp"
      uap10:TrustLevel="mediumIL">
      <uap:VisualElements
        DisplayName="NexusWpp"
        Description="Fond d'ecran telemetry cockpit"
        BackgroundColor="#0B0F14"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

Set-Content -LiteralPath (Join-Path $packageDir "AppxManifest.xml") -Value $manifest -Encoding UTF8

& $makeAppx pack /d $packageDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) {
    throw "makeappx failed with exit code $LASTEXITCODE"
}

if (!$SkipSigning) {
    $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher -and $_.HasPrivateKey } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if (!$cert) {
        $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName "NexusWpp MSIX Test Certificate" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")
    }

    & $signTool sign /fd SHA256 /sha1 $cert.Thumbprint $msixPath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Warning "MSIX signing skipped. Use this for Store packaging validation only."
}

Get-Item -LiteralPath $msixPath | Select-Object FullName, Length, LastWriteTime
