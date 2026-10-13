# Microsoft Store screenshots rendered by the real page with this PC's real telemetry.
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $project 'compile.ps1') | Out-Null
$toolDir = Join-Path $project 'bin\store-screenshots'
$output = Join-Path $project 'dist\store-screenshots'
New-Item -ItemType Directory -Path $toolDir, $output -Force | Out-Null
foreach ($dll in @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $project ('bin\' + $dll)) -Destination $toolDir -Force
}
$binary = Join-Path $toolDir 'StoreScreenshots.exe'
$refs = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Management.dll',
    "/reference:$toolDir\Microsoft.Web.WebView2.Core.dll", "/reference:$toolDir\Microsoft.Web.WebView2.WinForms.dll")
$sources = @('DesktopHtmlHost.cs', 'DesktopVisibility.cs', 'NvidiaTelemetry.cs', 'TelemetryCollector.cs', 'WindowsGpuTelemetry.cs', 'work\obj\ReleaseVersion.cs') |
    ForEach-Object { Join-Path $project $_ }
& (Join-Path $PSScriptRoot 'csc.ps1') /nologo /target:winexe /platform:x64 /main:StoreScreenshots "/out:$binary" @refs @sources (Join-Path $PSScriptRoot 'tests\StoreScreenshots.cs')
if ($LASTEXITCODE -ne 0) { throw 'Store screenshot tool compilation failed.' }
Get-ChildItem $output -Filter *.png | Remove-Item
foreach ($language in @('fr', 'en')) {
    $process = Start-Process -FilePath $binary -ArgumentList "`"$project`"", "`"$output`"", $language -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) { throw "Store screenshot capture failed ($language)." }
}
Get-ChildItem $output -Filter *.png | Select-Object Name, Length
