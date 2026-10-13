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
