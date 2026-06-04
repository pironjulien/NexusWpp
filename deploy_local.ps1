$ErrorActionPreference = "Stop"

# 0. Check if running as administrator, relaunch if not (UAC Auto-elevation)
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$isAdmin) {
    Write-Host "Relaunching as Administrator to configure Task Scheduler..." -ForegroundColor Yellow
    $p = Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs -PassThru -Wait
    if ($p.ExitCode -ne 0) {
        Write-Error "Elevated deployment failed with exit code $($p.ExitCode)"
        Exit $p.ExitCode
    }
    Exit
}

try {
    # Remove any old deployment error log
    if (Test-Path "C:\nexuswpp\deploy_error.txt") { Remove-Item "C:\nexuswpp\deploy_error.txt" -Force }

    Write-Host "--- DEPLOYING HARDWARE WALLPAPER TO LOCAL OFFLINE STORAGE ---" -ForegroundColor Cyan

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

# 1. Stop all active telemetry processes to prevent locks during copy
Write-Host "Stopping active telemetry processes..." -ForegroundColor Yellow

# Force terminate processes using taskkill to release file handles instantly
cmd /c "taskkill /F /IM nexuswpp.exe /T 2>nul" | Out-Null
cmd /c "taskkill /F /IM DesktopHtmlHost.exe /T 2>nul" | Out-Null
Stop-NexusWebView2

$processes = @("DesktopHtmlHost", "MSI_Hardware_Wallpaper", "nexuswpp")
foreach ($proc in $processes) {
    try {
        Get-Process -Name $proc -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    } catch {
        # Ignore errors if process not found or unable to stop
    }
}

# Node.js server port 29090 is no longer used by the native telemetry host
Start-Sleep -Seconds 1

# 2. Copy optimized files from PSScriptRoot to local folder C:\nexuswpp
$localFolder = "C:\nexuswpp"

if (!(Test-Path $localFolder)) {
    New-Item -ItemType Directory -Path $localFolder -Force | Out-Null
}

# 1.8 Clean up obsolete files and leftovers in destination directory C:\nexuswpp
Write-Host "Cleaning up obsolete files in C:\nexuswpp..." -ForegroundColor Yellow
$allowedFiles = @(
    "app.js", "style.css", "index.html", "loading-zero-5120x1440.png", "julienpiron.png", "splash.jpg", "icon.ico", "nexuswpp.exe",
    "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll",
    "webview_debug.log"
)
Get-ChildItem -Path $localFolder | ForEach-Object {
    if ($allowedFiles -notcontains $_.Name) {
        try {
            Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop
            Write-Host "Deleted obsolete: $($_.Name)" -ForegroundColor Yellow
        } catch {
            Write-Warning "Could not delete obsolete file $($_.Name): $_"
        }
    }
}

Write-Host "Copying optimized files from $PSScriptRoot to local folder..." -ForegroundColor Yellow
$filesToCopy = @("app.js", "style.css", "index.html", "loading-zero-5120x1440.png", "julienpiron.png", "splash.jpg", "icon.ico")
foreach ($file in $filesToCopy) {
    $src = Join-Path $PSScriptRoot $file
    $dest = Join-Path $localFolder $file
    if (Test-Path $src) {
        Copy-Item -Path $src -Destination $dest -Force
        Write-Host "Synced: $file -> local folder" -ForegroundColor Green
    } else {
        Write-Warning "Source file not found: $src"
    }
}

# 2.5 Copy binary host files and WebView2 dependencies robustly
$binSrc = Join-Path $PSScriptRoot "bin"
if (Test-Path $binSrc) {
    Get-ChildItem -Path $binSrc -File | ForEach-Object {
        $dest = Join-Path $localFolder $_.Name
        try {
            Copy-Item -Path $_.FullName -Destination $dest -Force -ErrorAction Stop
            Write-Host "Synced binary: $($_.Name) -> local folder" -ForegroundColor Green
        } catch {
            if (Test-Path $dest) {
                Write-Host "Warning: Could not overwrite $($_.Name) (locked), but file is already present. Continuing." -ForegroundColor Yellow
            } else {
                throw "Failed to copy $($_.FullName) to $($dest): $_"
            }
        }
    }
} else {
    throw "Binary host folder not found at $binSrc. You must run compile.ps1 first."
}

# 3. Clean up any old unused launch files if they exist
$vbsCombinedPath = Join-Path $localFolder "launch_combined.vbs"
if (Test-Path $vbsCombinedPath) { Remove-Item $vbsCombinedPath -Force }
$vbsPath = Join-Path $localFolder "launch.vbs"
if (Test-Path $vbsPath) { Remove-Item $vbsPath -Force }

# 5. Create fast logon launchers targeting nexuswpp.exe directly.
$startupFolder = [Environment]::GetFolderPath("Startup")
$oldShortcutPath = Join-Path $startupFolder "MSI_Hardware_Desktop_Dashboard.lnk"
if (Test-Path $oldShortcutPath) { Remove-Item $oldShortcutPath -Force }
$oldDashboardShortcutPath = Join-Path $startupFolder "NexusWpp_Dashboard.lnk"
if (Test-Path $oldDashboardShortcutPath) { Remove-Item $oldDashboardShortcutPath -Force }
$oldShortcutPath2 = Join-Path $startupFolder "nexuswpp.lnk"
if (Test-Path $oldShortcutPath2) { Remove-Item $oldShortcutPath2 -Force }

if (!(Test-Path $startupFolder)) {
    New-Item -ItemType Directory -Path $startupFolder -Force | Out-Null
}

$shortcutPath = Join-Path $startupFolder "NexusWpp.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = "C:\nexuswpp\nexuswpp.exe"
$shortcut.WorkingDirectory = "C:\nexuswpp"
$shortcut.IconLocation = "C:\nexuswpp\icon.ico"
$shortcut.WindowStyle = 7
$shortcut.Save()
Write-Host "Startup shortcut registered for fastest user-session launch." -ForegroundColor Green

$serializeKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize"
if (!(Test-Path $serializeKey)) {
    New-Item -Path $serializeKey -Force | Out-Null
}
New-ItemProperty -Path $serializeKey -Name "StartupDelayInMSec" -Value 0 -PropertyType DWord -Force | Out-Null
Write-Host "Windows startup app delay disabled for this user." -ForegroundColor Green

$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
New-ItemProperty -Path $runKey -Name "NexusWpp" -Value '"C:\nexuswpp\nexuswpp.exe"' -PropertyType String -Force | Out-Null
$startupApprovedRunKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
if (Test-Path $startupApprovedRunKey) {
    New-ItemProperty -Path $startupApprovedRunKey -Name "NexusWpp" -Value ([byte[]](0x02,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00)) -PropertyType Binary -Force | Out-Null
}
Write-Host "HKCU Run startup entry registered for immediate user logon launch." -ForegroundColor Green

Write-Host "Registering non-elevated backup Windows Scheduled Task for nexuswpp.exe..." -ForegroundColor Yellow

# Clean up old Telemetry task
Get-ScheduledTask -TaskName "NexusWppTelemetry" -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false

$taskName = "NexusWpp"
$action = New-ScheduledTaskAction -Execute "C:\nexuswpp\nexuswpp.exe" -WorkingDirectory "C:\nexuswpp"
$trigger = New-ScheduledTaskTrigger -AtLogOn
# Run in the normal user session. Highest elevation can delay launch at logon and is not needed here.
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -RunLevel Limited
# Configure settings for earliest practical user-session launch.
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -Priority 0 -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Seconds 0)

try {
    # Remove existing task if any to prevent conflicts
    Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue | Unregister-ScheduledTask -Confirm:$false

    # Register the new task
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings | Out-Null

    Write-Host "Windows Scheduled Task '$taskName' successfully registered as a non-elevated backup launcher!" -ForegroundColor Green
} catch {
    Write-Host "Warning: Scheduled Task registration was refused by Windows. HKCU Run and Startup shortcut remain active." -ForegroundColor Yellow
}

Write-Host "--- DEPLOYMENT COMPLETED! ---" -ForegroundColor Cyan
} catch {
    $errMessage = "Deployment failed at $(Get-Date):`n$_`n`nStack Trace:`n$($_.ScriptStackTrace)"
    [System.IO.File]::WriteAllText("C:\nexuswpp\deploy_error.txt", $errMessage)
    Write-Error "Deployment failed: $_"
    Start-Sleep -Seconds 5
    Exit 1
}
