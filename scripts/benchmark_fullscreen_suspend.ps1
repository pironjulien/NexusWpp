param(
    [int]$ActiveSeconds = 10,
    [int]$FullscreenSeconds = 10,
    [string]$AppPath = "C:\nexuswpp\nexuswpp.exe",
    [string]$LogPath = "C:\nexuswpp\webview_debug.log",
    [string]$OutputPath = "",
    [int]$DetectionTimeoutSeconds = 5,
    [string]$ExpectedProbeProcessPattern = "nexusfullscreenprobe"
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

function Get-NexusSnapshot {
    param([datetime]$Since)

    $items = Get-Process -Name nexuswpp,msedgewebview2 -ErrorAction SilentlyContinue |
        Where-Object {
            try { $_.StartTime -ge $Since } catch { $true }
        }

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

function Measure-NexusCpu {
    param(
        [datetime]$Since,
        [int]$Seconds
    )

    $first = Get-NexusSnapshot -Since $Since
    Start-Sleep -Seconds $Seconds
    $last = Get-NexusSnapshot -Since $Since
    $elapsed = ($last.Time - $first.Time).TotalSeconds
    $delta = [Math]::Max(0, $last.CpuSeconds - $first.CpuSeconds)

    [pscustomobject]@{
        Seconds = $Seconds
        AvgCpuCores = if ($elapsed -gt 0) { [Math]::Round($delta / $elapsed, 4) } else { 0 }
        WorkingSetMB = [Math]::Round($last.WorkingSetBytes / 1MB, 1)
        ProcessCount = $last.Count
    }
}

function Get-LogLineCount {
    if (!(Test-Path $LogPath)) { return 0 }
    return @((Get-Content -LiteralPath $LogPath -ErrorAction SilentlyContinue)).Count
}

function Get-NewLogLines {
    param([int]$StartLine)

    if (!(Test-Path $LogPath)) { return @() }
    $allLines = @(Get-Content -LiteralPath $LogPath -ErrorAction SilentlyContinue)
    if ($allLines.Count -le $StartLine) { return @() }
    return @($allLines | Select-Object -Skip $StartLine)
}

function Wait-NewLogMatch {
    param(
        [int]$StartLine,
        [string]$Pattern,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $match = Get-NewLogLines -StartLine $StartLine |
            Select-String -Pattern $Pattern |
            Select-Object -First 1
        if ($match) {
            return [pscustomobject]@{
                FoundAt = Get-Date
                Line = $match.Line
            }
        }
        Start-Sleep -Milliseconds 25
    }

    return $null
}

function New-FullscreenProbe {
    param([int]$Seconds)

    $code = @"
using System;
using System.Drawing;
using System.Windows.Forms;

public class FullscreenProbe {
    [STAThread]
    public static void Main() {
        Application.EnableVisualStyles();
        using (var f = new Form()) {
            f.FormBorderStyle = FormBorderStyle.None;
            f.WindowState = FormWindowState.Maximized;
            f.TopMost = true;
            f.BackColor = Color.Black;
            var timer = new Timer();
            timer.Interval = $($Seconds * 1000);
            timer.Tick += (s, e) => { timer.Stop(); f.Close(); };
            timer.Start();
            Application.Run(f);
        }
    }
}
"@

    $src = Join-Path $env:TEMP "NexusFullscreenProbe.cs"
    $exe = Join-Path $env:TEMP "NexusFullscreenProbe.exe"
    Set-Content -Path $src -Value $code -Encoding ASCII
    & C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:$exe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $src | Out-Null
    return $exe
}

if (!(Test-Path $AppPath)) {
    throw "App not found: $AppPath"
}

$startMarker = Get-Date
Get-Process -Name nexuswpp -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Stop-NexusWebView2
Start-Sleep -Milliseconds 700

Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path $AppPath) -WindowStyle Hidden
Start-Sleep -Seconds 3

$active = Measure-NexusCpu -Since $startMarker -Seconds $ActiveSeconds

$probeExe = New-FullscreenProbe -Seconds $FullscreenSeconds
$fullscreenStartLine = Get-LogLineCount
$fullscreenStartedAt = Get-Date
$probe = Start-Process -FilePath $probeExe -PassThru
$suspendPattern = "Runtime suspended: fullscreen foreground detected .*" + [regex]::Escape($ExpectedProbeProcessPattern)
$suspendMatch = Wait-NewLogMatch -StartLine $fullscreenStartLine -Pattern $suspendPattern -TimeoutSeconds $DetectionTimeoutSeconds
Start-Sleep -Milliseconds 100
$elapsedBeforeMeasure = [Math]::Max(0, ((Get-Date) - $fullscreenStartedAt).TotalSeconds)
$fullscreenMeasureSeconds = [Math]::Max(1, [Math]::Floor($FullscreenSeconds - $elapsedBeforeMeasure - 1))
$fullscreen = Measure-NexusCpu -Since $startMarker -Seconds $fullscreenMeasureSeconds
$probe.WaitForExit()
$fullscreenEndedAt = Get-Date
$resumeMatch = Wait-NewLogMatch -StartLine $fullscreenStartLine -Pattern "Runtime resumed: fullscreen foreground cleared" -TimeoutSeconds $DetectionTimeoutSeconds
$telemetryMatch = Wait-NewLogMatch -StartLine $fullscreenStartLine -Pattern "REQUEST_TELEMETRY" -TimeoutSeconds $DetectionTimeoutSeconds
Start-Sleep -Seconds 2

$recentLog = if (Test-Path $LogPath) { Get-Content $LogPath -Tail 120 } else { @() }
$requestLine = $recentLog | Select-String -Pattern "REQUEST_TELEMETRY" | Select-Object -Last 1

$gainPct = if ($active.AvgCpuCores -gt 0) {
    [Math]::Round((1.0 - ($fullscreen.AvgCpuCores / $active.AvgCpuCores)) * 100.0, 1)
} else {
    $null
}

$result = [pscustomobject]@{
    StartedAt = $startMarker.ToString("s")
    Active = $active
    FullscreenSuspended = $fullscreen
    CpuReductionPercent = $gainPct
    SuspendLatencyMs = if ($suspendMatch) { [Math]::Round(($suspendMatch.FoundAt - $fullscreenStartedAt).TotalMilliseconds, 0) } else { $null }
    ResumeLatencyMs = if ($resumeMatch) { [Math]::Round(($resumeMatch.FoundAt - $fullscreenEndedAt).TotalMilliseconds, 0) } else { $null }
    TelemetryAfterResumeMs = if ($telemetryMatch) { [Math]::Round(($telemetryMatch.FoundAt - $fullscreenEndedAt).TotalMilliseconds, 0) } else { $null }
    ProbeSuspendMatched = $null -ne $suspendMatch
    SuspendLine = if ($suspendMatch) { $suspendMatch.Line } else { $null }
    ResumeLine = if ($resumeMatch) { $resumeMatch.Line } else { $null }
    LastRequestTelemetry = if ($requestLine) { $requestLine.Line } else { $null }
    FullscreenProbeStartedAt = $fullscreenStartedAt.ToString("s")
    FullscreenProbeEndedAt = $fullscreenEndedAt.ToString("s")
}

if ($OutputPath) {
    $dir = Split-Path $OutputPath -Parent
    if ($dir -and !(Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $result | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputPath -Encoding UTF8
}

$result
