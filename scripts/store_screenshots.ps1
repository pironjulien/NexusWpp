# Microsoft Store screenshots rendered by the real page with this PC's real telemetry.
param([int]$WarmupSeconds = 70, [string]$Output)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $project 'compile.ps1') | Out-Null
$toolDir = Join-Path $project 'bin\store-screenshots'
$output = if ($Output) { $Output } else { Join-Path $project 'dist\store-screenshots' }
New-Item -ItemType Directory -Path $toolDir, $output -Force | Out-Null
foreach ($dll in @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $project ('bin\' + $dll)) -Destination $toolDir -Force
}
$binary = Join-Path $toolDir 'StoreScreenshots.exe'
$refs = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Management.dll',
    "/reference:$toolDir\Microsoft.Web.WebView2.Core.dll", "/reference:$toolDir\Microsoft.Web.WebView2.WinForms.dll")
$sources = & (Join-Path $PSScriptRoot 'host_sources.ps1')
& (Join-Path $PSScriptRoot 'csc.ps1') /nologo /target:winexe /platform:x64 /main:StoreScreenshots "/out:$binary" @refs @sources (Join-Path $PSScriptRoot 'tests\StoreScreenshots.cs')
if ($LASTEXITCODE -ne 0) { throw 'Store screenshot tool compilation failed.' }
$settingsShot = Join-Path $toolDir 'StoreSettingsShot.exe'
& (Join-Path $PSScriptRoot 'csc.ps1') /nologo /target:winexe /platform:x64 /main:StoreSettingsShot "/out:$settingsShot" @refs @sources (Join-Path $PSScriptRoot 'tests\StoreSettingsShot.cs')
if ($LASTEXITCODE -ne 0) { throw 'Settings screenshot tool compilation failed.' }
Get-ChildItem $output -Filter *.png | Remove-Item
# Neutral example image for the custom-logo screenshot (clearly not the brand logo).
Add-Type -AssemblyName System.Drawing
function New-SampleLogo([string]$Path, [string]$Text) {
    $bitmap = New-Object System.Drawing.Bitmap 512, 512
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 512, 512), ([System.Drawing.Color]::FromArgb(255, 255, 140, 30)), ([System.Drawing.Color]::FromArgb(255, 210, 40, 120))
    $g.FillRectangle($brush, 0, 0, 512, 512)
    $font = New-Object System.Drawing.Font 'Bahnschrift', 88, ([System.Drawing.FontStyle]::Bold)
    $format = New-Object System.Drawing.StringFormat; $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
    $g.DrawString($Text, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, 512, 512), $format)
    $g.Dispose(); $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()
}
foreach ($language in @('fr', 'en')) {
    $sample = Join-Path $toolDir "sample-logo-$language.png"
    New-SampleLogo $sample $(if ($language -eq 'fr') { "MON`nLOGO" } else { "MY`nLOGO" })
    $process = Start-Process -FilePath $binary -ArgumentList "`"$project`"", "`"$output`"", $language, $WarmupSeconds, "`"$sample`"" -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) { throw "Store screenshot capture failed ($language)." }
    # The real settings window (shown on screen for a moment) over the real dashboard render.
    $process = Start-Process -FilePath $settingsShot -ArgumentList "`"$(Join-Path $project 'julienpiron.png')`"", "`"$(Join-Path $output "nexuswpp-3840x2160-$language.png")`"", "`"$(Join-Path $output "nexuswpp-settings-3840x2160-$language.png")`"", $language -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Settings screenshot failed ($language)." }
}
Get-ChildItem $output -Filter *.png | Select-Object Name, Length
