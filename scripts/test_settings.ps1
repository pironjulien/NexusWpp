# Custom logo import and settings window (isolated output folder; the user's settings are untouched).
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& (Join-Path $project 'compile.ps1') | Out-Null
$testDir = Join-Path $project 'bin\settings-tests'
$output = Join-Path $project 'work\settings-tests'
New-Item -ItemType Directory -Path $testDir, $output -Force | Out-Null
foreach ($dll in @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $project ('bin\' + $dll)) -Destination $testDir -Force
}
$binary = Join-Path $testDir 'SettingsRegression.exe'
$refs = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Management.dll',
    "/reference:$testDir\Microsoft.Web.WebView2.Core.dll", "/reference:$testDir\Microsoft.Web.WebView2.WinForms.dll")
$sources = & (Join-Path $PSScriptRoot 'host_sources.ps1')
& (Join-Path $PSScriptRoot 'csc.ps1') /nologo /target:exe /platform:x64 /main:SettingsRegression "/out:$binary" @refs @sources (Join-Path $PSScriptRoot 'tests\SettingsRegression.cs')
if ($LASTEXITCODE -ne 0) { throw 'Settings test compilation failed.' }
& $binary $output $project
if ($LASTEXITCODE -ne 0) { throw 'Settings regressions failed.' }
