param(
    [int]$Seconds = 8,
    [string]$AppPath = "C:\nexuswpp\nexuswpp.exe",
    [string]$OutputPath = ".\scripts\last-benchmark-hdr-native-swapchain.json",
    [switch]$WithFullscreenProbe,
    [switch]$NoRestartNexus
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

$source = Join-Path $PSScriptRoot "hdr_native_swapchain_probe.cs"
$outDir = Join-Path $PSScriptRoot "bin"
$exe = Join-Path $outDir "hdr_native_swapchain_probe.exe"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (!(Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

if (Test-Path $exe) {
    Remove-Item -LiteralPath $exe -Force
}

$compileOutput = & $csc /nologo /target:exe /out:$exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $source 2>&1
if ($LASTEXITCODE -ne 0 -or !(Test-Path $exe)) {
    throw "Probe compilation failed: $($compileOutput -join [Environment]::NewLine)"
}

function New-FullscreenProbe {
    param([int]$DurationSeconds)

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
            timer.Interval = $($DurationSeconds * 1000);
            timer.Tick += (s, e) => { timer.Stop(); f.Close(); };
            timer.Start();
            Application.Run(f);
        }
    }
}
"@

    $src = Join-Path $env:TEMP "NexusHdrNativeFullscreenProbe.cs"
    $fullscreenExe = Join-Path $env:TEMP "NexusHdrNativeFullscreenProbe.exe"
    Set-Content -Path $src -Value $code -Encoding ASCII
    & $csc /nologo /target:winexe /out:$fullscreenExe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $src | Out-Null
    return $fullscreenExe
}

Get-Process -Name nexuswpp -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Stop-NexusWebView2
Start-Sleep -Milliseconds 700

$stdout = Join-Path $env:TEMP ("hdr_native_swapchain_probe_" + [Guid]::NewGuid().ToString("N") + ".json")
$started = Get-Date
$proc = Start-Process -FilePath $exe -ArgumentList $Seconds -PassThru -RedirectStandardOutput $stdout -WindowStyle Hidden

$fullscreenProc = $null
if ($WithFullscreenProbe) {
    Start-Sleep -Seconds ([Math]::Min(2, [Math]::Max(1, [int]($Seconds / 3))))
    $fullscreenExe = New-FullscreenProbe -DurationSeconds ([Math]::Max(1, $Seconds - 3))
    $fullscreenProc = Start-Process -FilePath $fullscreenExe -PassThru
}

$peakWorkingSet = 0L
$lastCpu = 0.0
while (-not $proc.HasExited) {
    try {
        $sample = Get-Process -Id $proc.Id -ErrorAction Stop
        if ($sample.WorkingSet64 -gt $peakWorkingSet) {
            $peakWorkingSet = [int64]$sample.WorkingSet64
        }
        if ($null -ne $sample.CPU) {
            $lastCpu = [double]$sample.CPU
        }
    } catch {}
    Start-Sleep -Milliseconds 200
}

if ($fullscreenProc -and -not $fullscreenProc.HasExited) {
    try { $fullscreenProc.WaitForExit(1500) | Out-Null } catch {}
}

$ended = Get-Date
try {
    $proc.WaitForExit()
    $proc.Refresh()
    if ($null -ne $proc.CPU) {
        $lastCpu = [double]$proc.CPU
    }
} catch {}

$elapsed = ($ended - $started).TotalSeconds
$jsonText = if (Test-Path $stdout) { (Get-Content $stdout -Raw).Trim() } else { "" }
$probe = if ($jsonText) { $jsonText | ConvertFrom-Json } else { $null }
$exitCode = if ($probe -and $probe.ok) { 0 } elseif ($null -ne $proc.ExitCode) { $proc.ExitCode } else { 1 }

$result = [pscustomobject]@{
    StartedAt = $started.ToString("s")
    SecondsRequested = $Seconds
    ElapsedSeconds = [Math]::Round($elapsed, 3)
    ExitCode = $exitCode
    AvgCpuCores = if ($elapsed -gt 0) { [Math]::Round($lastCpu / $elapsed, 4) } else { 0 }
    PeakWorkingSetMB = [Math]::Round($peakWorkingSet / 1MB, 1)
    WithFullscreenProbe = [bool]$WithFullscreenProbe
    Probe = $probe
}

if ($OutputPath) {
    $dir = Split-Path $OutputPath -Parent
    if ($dir -and !(Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputPath -Encoding UTF8
}

if (!$NoRestartNexus -and (Test-Path $AppPath)) {
    Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path $AppPath) -WindowStyle Hidden
}

$result
