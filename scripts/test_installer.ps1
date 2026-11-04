# Installs dist\NexusWppSetup.exe silently, checks what it set up, then uninstalls it and checks
# the cleanup. It changes the machine (C:\nexuswpp, HKLM Run, Start menu, Installed apps):
# run it on a CI runner or a disposable machine only, never on a workstation in use.
param([switch]$OnThisMachine)
$ErrorActionPreference = 'Stop'
if (-not $env:CI -and -not $OnThisMachine) { throw 'test_installer.ps1 changes the machine: run it in CI, or pass -OnThisMachine on a disposable machine.' }

$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$setup = Join-Path $project 'dist\NexusWppSetup.exe'
$installDir = 'C:\nexuswpp'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NexusWpp'
$version = & (Join-Path $PSScriptRoot 'read_version.ps1')
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw "FAIL $message" }
    Write-Host "PASS $message"
}
function Show-Log {
    foreach ($log in @((Join-Path $installDir 'install.log'), (Join-Path $env:TEMP 'NexusWppInstall.log'))) {
        if (Test-Path -LiteralPath $log) { Write-Host "--- $log"; Get-Content -LiteralPath $log -Tail 40 }
    }
}

try {
    $install = Start-Process -FilePath $setup -ArgumentList '/silent' -Wait -PassThru
    Check ($install.ExitCode -eq 0) "silent install exits with 0 (got $($install.ExitCode))"
    Check (Test-Path -LiteralPath (Join-Path $installDir 'nexuswpp.exe')) 'nexuswpp.exe is installed'
    Check (Test-Path -LiteralPath (Join-Path $installDir 'index.html')) 'the page is installed'
    Check (Test-Path -LiteralPath (Join-Path $installDir 'WebView2Loader.dll')) 'the WebView2 loader is installed'
    Check ((Get-Item -LiteralPath (Join-Path $installDir 'nexuswpp.exe')).VersionInfo.FileVersion -eq $version) "installed host is version $version"
    Check (Test-Path -LiteralPath (Join-Path $installDir 'NexusWppSetup.exe')) 'the installer is kept for uninstallation'
    $run = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name NexusWpp -ErrorAction SilentlyContinue
    Check ($run.NexusWpp -eq '"C:\nexuswpp\nexuswpp.exe"') 'one HKLM Run startup entry'
    Check ((Get-ItemProperty -Path $uninstallKey).DisplayVersion -eq $version) 'Installed apps entry carries the version'
    $owner = (Get-Acl -LiteralPath $installDir).Owner
    Check ($owner -match 'Administrators|Administrateurs') "install folder owned by Administrators ($owner)"

    $uninstall = Start-Process -FilePath (Join-Path $installDir 'NexusWppSetup.exe') -ArgumentList '/uninstall', '/silent' -Wait -PassThru
    Check ($uninstall.ExitCode -eq 0) "silent uninstall exits with 0 (got $($uninstall.ExitCode))"
    # The installer removes its own folder through a delayed command once it has exited.
    $deadline = (Get-Date).AddSeconds(30)
    while ((Test-Path -LiteralPath $installDir) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    Check (-not (Test-Path -LiteralPath $installDir)) 'install folder removed'
    Check (-not (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name NexusWpp -ErrorAction SilentlyContinue)) 'startup entry removed'
    Check (-not (Test-Path -LiteralPath $uninstallKey)) 'Installed apps entry removed'
    Write-Host 'RESULT installer regressions passed'
} catch {
    Show-Log
    throw
}
