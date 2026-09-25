param(
    [Parameter(Mandatory)][string]$AppPath,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$LogPath = "$env:LOCALAPPDATA\NexusWpp\webview_debug.log",
    [int]$Seconds = 8
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ProbeWindow {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[ProbeWindow]::SetProcessDPIAware() | Out-Null
$previousFocus = [ProbeWindow]::GetForegroundWindow()
$desktopShell = New-Object -ComObject Shell.Application
$forms = [Collections.Generic.List[Windows.Forms.Form]]::new()
$start = Get-Date
$logOffset = if(Test-Path -LiteralPath $LogPath){@(Get-Content -LiteralPath $LogPath).Count}else{0}

function Pump([double]$Duration) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while($timer.Elapsed.TotalSeconds -lt $Duration) {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }
}
function Snapshot {
    $rows = @(Get-CimInstance Win32_Process)
    $ids = [Collections.Generic.HashSet[int]]::new()
    $rows | Where-Object Name -eq 'nexuswpp.exe' | ForEach-Object { [void]$ids.Add([int]$_.ProcessId) }
    do {
        $changed = $false
        foreach($row in $rows){if($ids.Contains([int]$row.ParentProcessId) -and $ids.Add([int]$row.ProcessId)){$changed=$true}}
    } while($changed)
    $processes = @(Get-Process | Where-Object {$ids.Contains($_.Id)})
    $gpu = @(Get-CimInstance Win32_PerfRawData_GPUPerformanceCounters_GPUEngine | Where-Object {
        $_.Name -match '^pid_(\d+)_' -and $ids.Contains([int]$Matches[1])
    })
    [pscustomobject]@{At=Get-Date; CPU=($processes|Measure-Object CPU -Sum).Sum; Memory=($processes|Measure-Object WorkingSet64 -Sum).Sum; Count=$processes.Count; GPU=$gpu}
}
function Measure-Stage([string]$Name) {
    Pump 2
    $first=Snapshot
    Pump $Seconds
    $last=Snapshot
    $old=@{}
    $first.GPU|ForEach-Object {$old[$_.Name]=$_}
    $engineTotals=@{}
    foreach($counter in $last.GPU){
        $prior=$old[$counter.Name]
        if($prior -and $counter.Name -match '^pid_\d+_(.+)$'){
            $key=$Matches[1]
            $value=([double]$counter.RunningTime-[double]$prior.RunningTime)/([double]$counter.Timestamp_Sys100NS-[double]$prior.Timestamp_Sys100NS)*100
            $engineTotals[$key]+=$value
        }
    }
    [pscustomobject]@{
        Stage=$Name;At=$last.At.ToString('o');
        CPUCores=[math]::Round(($last.CPU-$first.CPU)/($last.At-$first.At).TotalSeconds,4);
        GPUBusiestEnginePercent=if($engineTotals.Count){[math]::Round(($engineTotals.Values|Measure-Object -Maximum).Maximum,3)}else{$null};
        WorkingSetMiB=[math]::Round($last.Memory/1MB,1);ProcessCount=$last.Count
    }
}
function Show-Cover([switch]$Fullscreen,[switch]$Transparent) {
    $screens = [Windows.Forms.Screen]::AllScreens
    if($Fullscreen){$screens=@([Windows.Forms.Screen]::PrimaryScreen)}
    foreach($screen in $screens){
        $area=if($Fullscreen){$screen.Bounds}else{$screen.WorkingArea}
        $parts=if($Fullscreen){1}else{2}
        for($part=0;$part -lt $parts;$part++){
            $left=$area.Left+[int][math]::Floor($area.Width*$part/$parts)
            $right=$area.Left+[int][math]::Floor($area.Width*($part+1)/$parts)
            $form=[Windows.Forms.Form]::new()
            $form.FormBorderStyle='None';$form.StartPosition='Manual';$form.ShowInTaskbar=$false
            $form.TopMost=$true;$form.BackColor=[Drawing.Color]::FromArgb(28,30,35)
            $form.Bounds=[Drawing.Rectangle]::FromLTRB($left,$area.Top,$right,$area.Bottom)
            if($Transparent){$form.Opacity=0.5}
            $label=[Windows.Forms.Label]::new();$label.Dock='Fill';$label.TextAlign='MiddleCenter'
            $label.ForeColor=[Drawing.Color]::White;$label.Text='Vérification du repos de NexusWpp — fermeture automatique dans quelques secondes'
            $form.Controls.Add($label);$forms.Add($form);$form.Show()
        }
    }
    Pump 0.5
}
function Close-Cover {
    foreach($form in $forms){$form.Close();$form.Dispose()}
    $forms.Clear();Pump 0.5
}
try {
    $desktopShell.MinimizeAll()
    Pump 1
    Get-Process -Name nexuswpp -ErrorAction SilentlyContinue | Stop-Process -Force
    Pump 1
    Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path -Parent $AppPath) -WindowStyle Hidden
    Pump 4
    $stages=@()
    $stages+=Measure-Stage 'visible'
    Show-Cover
    $stages+=Measure-Stage 'covered-by-tiled-windows'
    Close-Cover
    $stages+=Measure-Stage 'resumed'
    Show-Cover -Fullscreen
    $stages+=Measure-Stage 'fullscreen'
    Close-Cover
    Show-Cover -Transparent
    $stages+=Measure-Stage 'transparent-windows'
    Close-Cover
    Pump 2
    $newLog=@(Get-Content -LiteralPath $LogPath | Select-Object -Skip $logOffset)
    $payload=[pscustomobject]@{
        StartedAt=$start.ToString('o');AppPath=$AppPath;Stages=$stages;
        Events=@($newLog|Where-Object {$_ -match 'Runtime suspend|Runtime resume|WebView2 suspension|REQUEST_TELEMETRY|error|ERROR'}|ForEach-Object {$_.ToString()})
    }
    $payload|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $OutputPath -Encoding utf8
    $payload|ConvertTo-Json -Depth 5
}finally{
    Close-Cover
    $desktopShell.UndoMinimizeALL()
    [ProbeWindow]::SetForegroundWindow($previousFocus)|Out-Null
}
