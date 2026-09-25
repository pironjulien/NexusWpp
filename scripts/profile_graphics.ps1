param([string]$Assets = '', [string]$Label = 'graphics')
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $Assets) { $Assets = $project }
$output = Join-Path $project ('work\' + ($Label -replace '[^a-zA-Z0-9_-]', '_'))
$testDir = Join-Path $project 'bin\graphics-tests'
New-Item -ItemType Directory -Path $testDir, $output -Force | Out-Null
foreach ($dll in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $project ('bin\' + $dll)) -Destination $testDir -Force
}
$binary = Join-Path $testDir 'GraphicsProfile.exe'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Management.dll','/reference:System.Web.Extensions.dll',
    "/reference:$testDir\Microsoft.Web.WebView2.Core.dll", "/reference:$testDir\Microsoft.Web.WebView2.WinForms.dll")
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe "/out:$binary" @refs (Join-Path $PSScriptRoot 'tests\GraphicsProfile.cs')
if ($LASTEXITCODE -ne 0) { throw 'Graphics profile compilation failed.' }
& $binary $project $Assets $output
if ($LASTEXITCODE -ne 0) { throw 'Graphics profile failed.' }
