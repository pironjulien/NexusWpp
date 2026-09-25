param(
    [string]$Label = 'installed',
    [string]$OutputDirectory,
    [ValidateSet('all', 'maximized', 'tiled', 'fullscreen', 'partial', 'transparent', 'rapid')]
    [string]$Scenario = 'all',
    [ValidateRange(500, 10000)][int]$BaselineMilliseconds = 2000,
    [ValidateRange(500, 30000)][int]$CoveredMilliseconds = 5000,
    [ValidateRange(250, 10000)][int]$ResumeMilliseconds = 3000,
    [ValidateRange(1, 1000)][int]$SampleMilliseconds = 16,
    [ValidateRange(1, 30)][int]$RapidCycles = 8,
    [switch]$BuildOnly
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$dataRoot = Join-Path $project 'work\resume-20260925'
$binaryDirectory = Join-Path $dataRoot 'bin'
New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
$binary = Join-Path $binaryDirectory 'DesktopVisualResume.exe'
$source = Join-Path $PSScriptRoot 'tests\DesktopVisualResume.cs'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /unsafe /optimize+ ("/out:" + $binary) /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $source
if ($LASTEXITCODE -ne 0) { throw 'Visual measurement harness compilation failed.' }
if ($BuildOnly) {
    Write-Output "Compiled without opening a window or capturing the desktop: $binary"
    return
}

# Test exactly the installed packaged application. Never restart or deploy it here.
$package = Get-AppxPackage -Name 'julienpiron.fr.NexusWpp'
if (-not $package) { throw 'The NexusWpp package is not installed for this user.' }
$processes = @(Get-CimInstance Win32_Process -Filter "Name='nexuswpp.exe'")
if ($processes.Count -ne 1) { throw "Expected one running NexusWpp process; found $($processes.Count)." }
$appProcess = $processes[0]
if (-not $appProcess.ExecutablePath -or -not $appProcess.ExecutablePath.StartsWith($package.InstallLocation + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The running process could not be verified as the installed NexusWpp package.'
}
if (-not $OutputDirectory) {
    $safeLabel = $Label -replace '[^a-zA-Z0-9_-]', '_'
    $OutputDirectory = Join-Path $dataRoot ($safeLabel + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$allowedPrefix = [IO.Path]::GetFullPath($dataRoot).TrimEnd('\') + '\'
if (-not $OutputDirectory.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Keep visual measurement artifacts below $dataRoot."
}
if (Test-Path -LiteralPath $OutputDirectory) {
    if (@(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count) { throw 'Use a new, empty output directory to preserve existing measurements.' }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$metadata = [ordered]@{
    Label = $Label
    CapturedAt = (Get-Date).ToUniversalTime().ToString('o')
    PackageFullName = $package.PackageFullName
    PackageFamilyName = $package.PackageFamilyName
    Version = $package.Version.ToString()
    InstallLocation = $package.InstallLocation
    ProcessId = $appProcess.ProcessId
    ExecutablePath = $appProcess.ExecutablePath
    ExecutableSha256 = (Get-FileHash -LiteralPath $appProcess.ExecutablePath -Algorithm SHA256).Hash
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'installed-package.json') -Encoding UTF8
$arguments = @(
    '--out', $OutputDirectory, '--label', $Label, '--scenario', $Scenario,
    '--baseline-ms', $BaselineMilliseconds.ToString(), '--cover-ms', $CoveredMilliseconds.ToString(),
    '--resume-ms', $ResumeMilliseconds.ToString(), '--sample-ms', $SampleMilliseconds.ToString(),
    '--rapid-cycles', $RapidCycles.ToString(), '--app-pid', $appProcess.ProcessId.ToString()
)
if ($arguments | Where-Object { $_.Contains('"') }) { throw 'Quotation marks are not supported in measurement arguments.' }
$argumentLine = ($arguments | ForEach-Object { '"' + $_ + '"' }) -join ' '
Write-Output "Measuring real desktop pixels for $($package.PackageFullName); windows restore automatically."
$probe = Start-Process -FilePath $binary -ArgumentList $argumentLine -WindowStyle Hidden -Wait -PassThru
$resultPath = Join-Path $OutputDirectory 'result.json'
Write-Output "Visual evidence: $resultPath"
if ($probe.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $resultPath) { Get-Content -LiteralPath $resultPath -Raw | Write-Output }
    throw "Visual measurement harness exited with code $($probe.ExitCode)."
}
$stillRunning = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $appProcess.ProcessId)
if (-not $stillRunning -or $stillRunning.ExecutablePath -ne $appProcess.ExecutablePath) {
    throw 'NexusWpp stopped or changed during the measurement. Do not treat this run as a valid comparison.'
}
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$result.Scenarios | ForEach-Object {
    $name = $_.Name
    $_.Screens | ForEach-Object {
        [pscustomobject]@{
            Scenario = $name; Screen = $_.DeviceName; Anchors = $_.AnchorPairCount
            FirstCaptureEndMs = $_.FirstUncoverCaptureEndMilliseconds
            FirstExactReferenceMs = $_.FirstExactReferenceEndMilliseconds
            WorstExactPairFractionAfterRemoval = $_.WorstExactPairFractionAfterRemoval
            CaptureIntervalMedianMs = $_.CaptureIntervalMedianMilliseconds
            CaptureIntervalMaximumMs = $_.CaptureIntervalMaximumMilliseconds
        }
    }
} | Format-Table -AutoSize
