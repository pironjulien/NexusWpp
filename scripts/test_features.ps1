$ErrorActionPreference='Stop'
$project=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
& node (Join-Path $PSScriptRoot 'tests\telemetry_state.test.js')
if($LASTEXITCODE -ne 0){throw 'JavaScript feature regressions failed.'}
$testDir=Join-Path $project 'bin\feature-tests'
$output=Join-Path $project 'work\feature-tests'
New-Item -ItemType Directory -Path $testDir,$output -Force | Out-Null
$binary=Join-Path $testDir 'FeatureRegression.exe'
& (Join-Path $PSScriptRoot 'csc.ps1') /nologo /target:exe "/out:$binary" /reference:System.Management.dll (Join-Path $project 'NvidiaTelemetry.cs') (Join-Path $project 'WindowsGpuTelemetry.cs') (Join-Path $project 'PdhCounters.cs') (Join-Path $project 'NetworkTelemetry.cs') (Join-Path $project 'PowerSchemes.cs') (Join-Path $PSScriptRoot 'tests\FeatureRegression.cs')
if($LASTEXITCODE -ne 0){throw 'Native feature test compilation failed.'}
& $binary $output $project
if($LASTEXITCODE -ne 0){throw 'Native feature regressions failed.'}

# Host tests compile every host source: the version stamp and WebView2 assemblies come from compile.ps1.
if(!(Test-Path -LiteralPath (Join-Path $project 'work\obj\ReleaseVersion.cs')) -or !(Test-Path -LiteralPath (Join-Path $project 'bin\Microsoft.Web.WebView2.Core.dll'))){
    & (Join-Path $project 'compile.ps1') | Out-Null
}
foreach($dll in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){
    Copy-Item -LiteralPath (Join-Path $project ('bin\'+$dll)) -Destination $testDir -Force
}
$refs=@('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Management.dll',
    "/reference:$testDir\Microsoft.Web.WebView2.Core.dll","/reference:$testDir\Microsoft.Web.WebView2.WinForms.dll")
$sources=& (Join-Path $PSScriptRoot 'host_sources.ps1')

# Monitor coverage behind the pause (negative origins, overlaps, gaps).
$coverage=Join-Path $testDir 'DesktopCoverage.exe'
& (Join-Path $PSScriptRoot 'csc.ps1') /nologo /target:exe /platform:x64 /main:CoverageTests "/out:$coverage" @refs @sources (Join-Path $PSScriptRoot 'test_desktop_coverage.cs')
if($LASTEXITCODE -ne 0){throw 'Desktop coverage test compilation failed.'}
& $coverage
if($LASTEXITCODE -ne 0){throw 'Desktop coverage regressions failed.'}

# Window placement below WorkerW, recovery rules, shell events and the host message loop.
$hostTests=Join-Path $testDir 'HostRegression.exe'
& (Join-Path $PSScriptRoot 'csc.ps1') /nologo /target:exe /platform:x64 /main:HostRegression "/out:$hostTests" @refs @sources (Join-Path $PSScriptRoot 'tests\HostRegression.cs')
if($LASTEXITCODE -ne 0){throw 'Host regression compilation failed.'}
& $hostTests $output
if($LASTEXITCODE -ne 0){throw 'Host regressions failed.'}
