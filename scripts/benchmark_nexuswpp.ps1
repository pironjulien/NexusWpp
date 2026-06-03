param(
    [int]$DurationSeconds = 20,
    [int]$Runs = 1,
    [int]$StartupDelaySeconds = 2,
    [string]$AppPath = "C:\nexuswpp\nexuswpp.exe",
    [string]$LogPath = "C:\nexuswpp\webview_debug.log",
    [string]$Label = "current",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

function Stop-NexusWebView2 {
    Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.CommandLine -like "*\AppData\Local\nexuswpp\EBWebView*" -or
            $_.CommandLine -like "*--webview-exe-name=nexuswpp.exe*"
        } |
        ForEach-Object {
            try {
                Invoke-CimMethod -InputObject $_ -MethodName Terminate -ErrorAction Stop | Out-Null
            } catch {}
        }
}

function Get-ProcSnapshot {
    param([datetime]$Since)

    $items = Get-Process -Name nexuswpp,msedgewebview2 -ErrorAction SilentlyContinue |
        Where-Object {
            try { $_.StartTime -ge $Since } catch { $true }
        } |
        Select-Object Id, ProcessName, CPU, WorkingSet64, StartTime

    $cpu = 0.0
    $mem = 0L
    foreach ($item in $items) {
        if ($null -ne $item.CPU) { $cpu += [double]$item.CPU }
        $mem += [int64]$item.WorkingSet64
    }

    [pscustomobject]@{
        Time = Get-Date
        CpuSeconds = $cpu
        WorkingSetBytes = $mem
        Count = @($items).Count
    }
}

if (!(Test-Path $AppPath)) {
    throw "App not found: $AppPath"
}

if ($Runs -lt 1) {
    throw "Runs must be >= 1"
}

function Invoke-NexusBenchmarkRun {
    param([int]$RunIndex)

    $startMarker = Get-Date
    Get-Process -Name nexuswpp -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Stop-NexusWebView2
    Start-Sleep -Milliseconds 700

    Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path $AppPath) -WindowStyle Hidden
    Start-Sleep -Seconds $StartupDelaySeconds

    $first = Get-ProcSnapshot -Since $startMarker
    Start-Sleep -Seconds $DurationSeconds
    $last = Get-ProcSnapshot -Since $startMarker

    $elapsed = ($last.Time - $first.Time).TotalSeconds
    $cpuDelta = [Math]::Max(0, $last.CpuSeconds - $first.CpuSeconds)
    $avgCpuCores = if ($elapsed -gt 0) { $cpuDelta / $elapsed } else { 0 }
    $avgCpuPercentOneCore = $avgCpuCores * 100.0

    $recentLog = @()
    if (Test-Path $LogPath) {
        $recentLog = Get-Content $LogPath -Tail 160
    }

    $attach = $recentLog | Select-String -Pattern "Wallpaper attached after ([0-9.]+)s" | Select-Object -Last 1
    $request = $recentLog | Select-String -Pattern "\[(.*?)\] REQUEST_TELEMETRY" | Select-Object -Last 1
    $errors = @($recentLog | Select-String -Pattern "ERROR|CONSOLE_ERROR|Telemetry .*error|UpdateTopProcesses error|Violation")

    $attachSeconds = $null
    if ($attach -and $attach.Matches.Count -gt 0) {
        $attachSeconds = [double]::Parse($attach.Matches[0].Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
    }

    [pscustomobject]@{
        Label = $Label
        Run = $RunIndex
        StartedAt = $startMarker.ToString("s")
        DurationSeconds = $DurationSeconds
        AttachSeconds = $attachSeconds
        LastRequestTelemetry = if ($request) { $request.Line } else { $null }
        ErrorCountRecent = $errors.Count
        AvgCpuCores = [Math]::Round($avgCpuCores, 4)
        AvgCpuPercentOfOneCore = [Math]::Round($avgCpuPercentOneCore, 2)
        WorkingSetMB = [Math]::Round($last.WorkingSetBytes / 1MB, 1)
        ProcessCount = $last.Count
    }
}

function Get-RoundedMetric {
    param(
        [object[]]$Values,
        [int]$Digits = 2
    )

    $valid = @($Values | Where-Object { $null -ne $_ })
    if ($valid.Count -eq 0) { return $null }
    return [Math]::Round((($valid | Measure-Object -Average).Average), $Digits)
}

$results = for ($i = 1; $i -le $Runs; $i++) {
    Invoke-NexusBenchmarkRun -RunIndex $i
}

$summary = [pscustomobject]@{
    Label = $Label
    Runs = $Runs
    DurationSeconds = $DurationSeconds
    AvgCpuCores = Get-RoundedMetric -Values @($results | ForEach-Object { $_.AvgCpuCores }) -Digits 4
    MinCpuCores = [Math]::Round((($results | Measure-Object AvgCpuCores -Minimum).Minimum), 4)
    MaxCpuCores = [Math]::Round((($results | Measure-Object AvgCpuCores -Maximum).Maximum), 4)
    AvgWorkingSetMB = Get-RoundedMetric -Values @($results | ForEach-Object { $_.WorkingSetMB }) -Digits 1
    AvgAttachSeconds = Get-RoundedMetric -Values @($results | ForEach-Object { $_.AttachSeconds }) -Digits 3
    TotalErrorsRecent = ($results | Measure-Object ErrorCountRecent -Sum).Sum
}

$payload = [pscustomobject]@{
    Summary = $summary
    Runs = @($results)
}

if ($OutputPath) {
    $dir = Split-Path $OutputPath -Parent
    if ($dir -and !(Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if ([System.IO.Path]::GetExtension($OutputPath).Equals(".json", [System.StringComparison]::OrdinalIgnoreCase)) {
        $payload | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding UTF8
    } else {
        $results | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8
    }
}

$payload
