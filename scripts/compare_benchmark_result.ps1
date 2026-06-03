param(
    [Parameter(Mandatory = $true)]
    [string]$BeforePath,

    [Parameter(Mandatory = $true)]
    [string]$AfterPath,

    [double]$CpuRegressionTolerancePct = 5.0,
    [double]$MemoryRegressionTolerancePct = 3.0,
    [double]$AttachRegressionToleranceSeconds = 0.05,
    [double]$MinCpuGainPct = 1.0,
    [double]$MinMemoryGainPct = 1.0,

    [switch]$AsJson,
    [switch]$FailOnReject,
    [switch]$FailOnNeutral
)

$ErrorActionPreference = "Stop"
$InvariantCulture = [Globalization.CultureInfo]::InvariantCulture

function Format-Decimal {
    param(
        [double]$Value,
        [string]$Format = "0.00"
    )

    return $Value.ToString($Format, $InvariantCulture)
}

function Read-BenchmarkSummary {
    param([string]$Path)

    if (!(Test-Path -LiteralPath $Path)) {
        throw "Benchmark file not found: $Path"
    }

    $payload = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($null -eq $payload.Summary) {
        throw "Benchmark file has no Summary block: $Path"
    }

    return $payload.Summary
}

function Get-RequiredNumber {
    param(
        [object]$Summary,
        [string]$Name,
        [string]$Path
    )

    $property = $Summary.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        throw "Missing Summary.$Name in $Path"
    }

    return [double]$property.Value
}

function Get-DeltaPct {
    param(
        [double]$Before,
        [double]$After
    )

    if ($Before -eq 0) {
        if ($After -eq 0) { return 0.0 }
        return $null
    }

    return (($After - $Before) / $Before) * 100.0
}

function New-MetricResult {
    param(
        [string]$Name,
        [double]$Before,
        [double]$After,
        [int]$Digits
    )

    $delta = $After - $Before
    $deltaPct = Get-DeltaPct -Before $Before -After $After

    [pscustomobject]@{
        Metric = $Name
        Before = [Math]::Round($Before, $Digits)
        After = [Math]::Round($After, $Digits)
        Delta = [Math]::Round($delta, $Digits)
        DeltaPct = if ($null -eq $deltaPct) { $null } else { [Math]::Round($deltaPct, 2) }
    }
}

$before = Read-BenchmarkSummary -Path $BeforePath
$after = Read-BenchmarkSummary -Path $AfterPath

$beforeCpu = Get-RequiredNumber -Summary $before -Name "AvgCpuCores" -Path $BeforePath
$afterCpu = Get-RequiredNumber -Summary $after -Name "AvgCpuCores" -Path $AfterPath
$beforeMemory = Get-RequiredNumber -Summary $before -Name "AvgWorkingSetMB" -Path $BeforePath
$afterMemory = Get-RequiredNumber -Summary $after -Name "AvgWorkingSetMB" -Path $AfterPath
$beforeAttach = Get-RequiredNumber -Summary $before -Name "AvgAttachSeconds" -Path $BeforePath
$afterAttach = Get-RequiredNumber -Summary $after -Name "AvgAttachSeconds" -Path $AfterPath
$beforeErrors = Get-RequiredNumber -Summary $before -Name "TotalErrorsRecent" -Path $BeforePath
$afterErrors = Get-RequiredNumber -Summary $after -Name "TotalErrorsRecent" -Path $AfterPath

$cpuDeltaPct = Get-DeltaPct -Before $beforeCpu -After $afterCpu
$memoryDeltaPct = Get-DeltaPct -Before $beforeMemory -After $afterMemory
$attachDelta = $afterAttach - $beforeAttach

$reasons = New-Object System.Collections.Generic.List[string]
$rejectReasons = New-Object System.Collections.Generic.List[string]

if ($afterErrors -gt $beforeErrors) {
    $rejectReasons.Add("errors increased from $beforeErrors to $afterErrors")
}

if ($null -eq $cpuDeltaPct) {
    if ($afterCpu -gt $beforeCpu) {
        $rejectReasons.Add("CPU increased from zero baseline")
    }
} elseif ($cpuDeltaPct -gt $CpuRegressionTolerancePct) {
    $rejectReasons.Add(("CPU regressed by {0}% (tolerance {1}%)" -f (Format-Decimal $cpuDeltaPct), (Format-Decimal $CpuRegressionTolerancePct)))
}

if ($null -eq $memoryDeltaPct) {
    if ($afterMemory -gt $beforeMemory) {
        $rejectReasons.Add("memory increased from zero baseline")
    }
} elseif ($memoryDeltaPct -gt $MemoryRegressionTolerancePct) {
    $rejectReasons.Add(("memory regressed by {0}% (tolerance {1}%)" -f (Format-Decimal $memoryDeltaPct), (Format-Decimal $MemoryRegressionTolerancePct)))
}

if ($attachDelta -gt $AttachRegressionToleranceSeconds) {
    $rejectReasons.Add(("attach time regressed by {0}s (tolerance {1}s)" -f (Format-Decimal $attachDelta "0.000"), (Format-Decimal $AttachRegressionToleranceSeconds "0.000")))
}

if ($rejectReasons.Count -gt 0) {
    $verdict = "REJECT"
    foreach ($reason in $rejectReasons) { $reasons.Add($reason) }
} else {
    $cpuGainPct = if ($null -eq $cpuDeltaPct) { 0.0 } else { -1.0 * $cpuDeltaPct }
    $memoryGainPct = if ($null -eq $memoryDeltaPct) { 0.0 } else { -1.0 * $memoryDeltaPct }

    if ($cpuGainPct -ge $MinCpuGainPct) {
        $verdict = "KEEP"
        $reasons.Add(("CPU improved by {0}%" -f (Format-Decimal $cpuGainPct)))
    } elseif ($memoryGainPct -ge $MinMemoryGainPct) {
        $verdict = "KEEP"
        $reasons.Add(("memory improved by {0}%" -f (Format-Decimal $memoryGainPct)))
    } else {
        $verdict = "NEUTRAL"
        $reasons.Add("no regression, but no meaningful CPU or memory gain")
    }
}

$result = [pscustomobject]@{
    Verdict = $verdict
    BeforeLabel = $before.Label
    AfterLabel = $after.Label
    Policy = [pscustomobject]@{
        CpuRegressionTolerancePct = $CpuRegressionTolerancePct
        MemoryRegressionTolerancePct = $MemoryRegressionTolerancePct
        AttachRegressionToleranceSeconds = $AttachRegressionToleranceSeconds
        MinCpuGainPct = $MinCpuGainPct
        MinMemoryGainPct = $MinMemoryGainPct
    }
    Metrics = @(
        New-MetricResult -Name "AvgCpuCores" -Before $beforeCpu -After $afterCpu -Digits 4
        New-MetricResult -Name "AvgWorkingSetMB" -Before $beforeMemory -After $afterMemory -Digits 1
        New-MetricResult -Name "AvgAttachSeconds" -Before $beforeAttach -After $afterAttach -Digits 3
        New-MetricResult -Name "TotalErrorsRecent" -Before $beforeErrors -After $afterErrors -Digits 0
    )
    Reasons = @($reasons)
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 5
} else {
    $result
}

if ($FailOnReject -and $verdict -eq "REJECT") {
    exit 1
}

if ($FailOnNeutral -and $verdict -eq "NEUTRAL") {
    exit 2
}
