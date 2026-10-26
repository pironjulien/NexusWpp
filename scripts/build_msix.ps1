param(
    [switch]$SkipSigning
)

$ErrorActionPreference = "Stop"

if (!$SkipSigning -and $PSVersionTable.PSEdition -eq "Core") {
    $windowsDir = if ($env:WINDIR) { $env:WINDIR } elseif ($env:SystemRoot) { $env:SystemRoot } else { "C:\Windows" }
    $windowsPowerShell = Join-Path $windowsDir "System32\WindowsPowerShell\v1.0\powershell.exe"
    if (Test-Path -LiteralPath $windowsPowerShell) {
        Write-Host "MSIX signing requires Windows PowerShell certificate cmdlets. Relaunching with Windows PowerShell..."
        & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
        exit $LASTEXITCODE
    }
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$distDir = Join-Path $projectRoot "dist\msix"
$packageDir = Join-Path $distDir "package"
$assetsDir = Join-Path $packageDir "Assets"
$version = & (Join-Path $PSScriptRoot 'read_version.ps1')
$identityName = "julienpiron.fr.NexusWpp"
$msixPath = Join-Path $distDir ($identityName + "_" + $version + "_x64.msix")
$publisher = "CN=C3E3A6F0-11D2-4EE1-B3F2-34EED4CAE7FA"
$publisherDisplayName = "JULIENPIRON.FR"

function Get-WindowsSdkToolPath {
    param([string]$ToolName)

    $command = Get-Command $ToolName -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    if ($command) { return $command }

    $sdkRoots = @(
        "C:\Program Files (x86)\Windows Kits\10\bin",
        "C:\Program Files\Windows Kits\10\bin"
    )

    foreach ($root in $sdkRoots) {
        if (!(Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName ("x64\" + $ToolName) } |
            Where-Object { Test-Path -LiteralPath $_ } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    return $null
}

$makeAppx = Get-WindowsSdkToolPath "makeappx.exe"
$makePri = Get-WindowsSdkToolPath "makepri.exe"
$signTool = Get-WindowsSdkToolPath "signtool.exe"

if (!(Test-Path -LiteralPath $makeAppx)) {
    throw "makeappx.exe not found. Install the Windows SDK or add makeappx.exe to PATH."
}
if (!(Test-Path -LiteralPath $makePri)) {
    throw "makepri.exe not found. Install the Windows SDK or add makepri.exe to PATH."
}
if (!(Test-Path -LiteralPath $signTool)) {
    throw "signtool.exe not found. Install the Windows SDK or add signtool.exe to PATH."
}

& (Join-Path $projectRoot "compile.ps1")

if (-not [IO.Path]::GetFullPath($packageDir).StartsWith([IO.Path]::GetFullPath((Join-Path $projectRoot 'dist')) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package directory.' }
if (Test-Path -LiteralPath $packageDir) {
    Remove-Item -LiteralPath $packageDir -Recurse -Force
}
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

$payloadFiles = & (Join-Path $PSScriptRoot 'application_files.ps1')

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

# Logos rendered at their exact size by scripts/export_icons.py from branding/*.svg:
# the pictogram alone under 44 px, the medallion from 44 px.
function New-LogoPng {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    if ($Width -ne $Height) { throw "Logo ${Width}x${Height} is not square." }
    $kind = if ($Width -ge 44) { "mark" } else { "icon" }
    $source = Join-Path $projectRoot ("branding\png\nexuswpp-" + $kind + "-" + $Width + ".png")
    if (!(Test-Path -LiteralPath $source)) { throw "Missing $source : add $Width to MSIX_SIZES in scripts\export_icons.py and run it." }
    Copy-Item -LiteralPath $source -Destination $Path -Force
}

function New-ScaledLogoSet {
    param(
        [string]$Name,
        [int]$BaseWidth,
        [int]$BaseHeight
    )

    New-LogoPng -Path (Join-Path $assetsDir ($Name + ".png")) -Width $BaseWidth -Height $BaseHeight

    foreach ($scale in @(100, 125, 150, 200, 400)) {
        $width = [int][Math]::Round($BaseWidth * $scale / 100)
        $height = [int][Math]::Round($BaseHeight * $scale / 100)
        New-LogoPng -Path (Join-Path $assetsDir ($Name + ".scale-" + $scale + ".png")) -Width $width -Height $height
    }
}

function New-AppIconTargetAssets {
    foreach ($size in @(16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256)) {
        New-LogoPng -Path (Join-Path $assetsDir ("Square44x44Logo.targetsize-" + $size + ".png")) -Width $size -Height $size
        New-LogoPng -Path (Join-Path $assetsDir ("Square44x44Logo.targetsize-" + $size + "_altform-unplated.png")) -Width $size -Height $size
    }
}

New-ScaledLogoSet -Name "StoreLogo" -BaseWidth 50 -BaseHeight 50
New-ScaledLogoSet -Name "Square44x44Logo" -BaseWidth 44 -BaseHeight 44
New-ScaledLogoSet -Name "Square150x150Logo" -BaseWidth 150 -BaseHeight 150
New-AppIconTargetAssets

# Store name per language (names reserved in Partner Center). The Start menu keeps "NexusWpp".
$storeNames = @{ 'fr-FR' = "NexusWpp – Fond d'écran moniteur système"; 'en-US' = 'NexusWpp – Live System Monitor Wallpaper' }
foreach ($language in $storeNames.Keys) {
    $stringsDir = Join-Path $packageDir "Strings\$language"
    New-Item -ItemType Directory -Path $stringsDir -Force | Out-Null
    $escaped = [System.Security.SecurityElement]::Escape($storeNames[$language])
    $resw = @"
<?xml version="1.0" encoding="utf-8"?>
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <data name="StoreName" xml:space="preserve"><value>$escaped</value></data>
</root>
"@
    [System.IO.File]::WriteAllText((Join-Path $stringsDir 'resources.resw'), $resw, (New-Object System.Text.UTF8Encoding($true)))
}

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
  xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap10 desktop rescap">
  <Identity Name="$identityName" Publisher="$publisher" Version="$version" ProcessorArchitecture="x64" />
  <Properties>
    <DisplayName>ms-resource:StoreName</DisplayName>
    <PublisherDisplayName>$publisherDisplayName</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26200.0" />
  </Dependencies>
  <Resources>
    <Resource Language="fr-fr" />
    <Resource Language="en-us" />
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
        Description="Live PC telemetry wallpaper"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
      <Extensions>
        <desktop:Extension
          Category="windows.startupTask"
          Executable="nexuswpp.exe"
          EntryPoint="Windows.FullTrustApplication">
          <desktop:StartupTask
            TaskId="NexusWppStartup"
            Enabled="true"
            DisplayName="NexusWpp" />
        </desktop:Extension>
      </Extensions>
    </Application>
  </Applications>
  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
"@

Set-Content -LiteralPath (Join-Path $packageDir "AppxManifest.xml") -Value $manifest -Encoding UTF8

$priConfigPath = Join-Path $packageDir "priconfig.xml"
& $makePri createconfig /cf $priConfigPath /dq fr-FR_en-US /o
if ($LASTEXITCODE -ne 0) {
    throw "makepri createconfig failed with exit code $LASTEXITCODE"
}

$priPath = Join-Path $packageDir "resources.pri"
& $makePri new /pr $packageDir /cf $priConfigPath /mn (Join-Path $packageDir "AppxManifest.xml") /of $priPath /o
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath (Join-Path $packageDir "resources.pri"))) {
    throw "makepri new failed with exit code $LASTEXITCODE"
}

Remove-Item -LiteralPath $priConfigPath -Force

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
