$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testDir = Join-Path $project 'bin\runtime-pause-tests'
New-Item -ItemType Directory -Path $testDir -Force | Out-Null
foreach ($dll in @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $project ('bin\' + $dll)) -Destination $testDir -Force
}
$binary = Join-Path $testDir 'RuntimePauseRegression.exe'
$refs = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',
    "/reference:$testDir\Microsoft.Web.WebView2.Core.dll", "/reference:$testDir\Microsoft.Web.WebView2.WinForms.dll")
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe "/out:$binary" @refs (Join-Path $PSScriptRoot 'tests\RuntimePauseRegression.cs')
if ($LASTEXITCODE -ne 0) { throw 'Runtime pause test compilation failed.' }
& $binary $project
if ($LASTEXITCODE -ne 0) { throw 'Runtime pause regression failed.' }
