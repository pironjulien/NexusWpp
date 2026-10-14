# Exclusive lock for a test that owns shared files of the repository (test binaries in bin\, WebView2
# profile and results in work\). Two runs on the same checkout (two agents, a global Test-NexusProjets
# run) would otherwise overwrite each other's binaries and profile; the second one now waits its turn.
# The lock is an open file handle: Windows releases it when the holding process ends, even if killed.
# Usage: $lock = & (Join-Path $PSScriptRoot 'test_lock.ps1') 'responsive-layout'
#        try { ... } finally { $lock.Dispose() }
param([Parameter(Mandatory)][string]$Name, [int]$TimeoutMinutes = 20)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$directory = Join-Path $project 'work\locks'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$path = Join-Path $directory ($Name + '.lock')
$deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
$announced = $false
while ($true) {
    try {
        $stream = [IO.FileStream]::new($path, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read)
        $stream.SetLength(0)
        $owner = [Text.Encoding]::UTF8.GetBytes("PID $PID, $(Get-Date -Format s), $([Environment]::CommandLine)")
        $stream.Write($owner, 0, $owner.Length)
        $stream.Flush()
        return $stream
    } catch [IO.IOException] {
        $holder = try {
            $reader = [IO.StreamReader]::new([IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite))
            try { $reader.ReadToEnd() } finally { $reader.Dispose() }
        } catch { 'unknown process' }
        if ([DateTime]::UtcNow -ge $deadline) { throw "Test '$Name' still locked after $TimeoutMinutes min by: $holder" }
        if (-not $announced) { Write-Host "Test '$Name' already running in this checkout ($holder); waiting for it to finish..."; $announced = $true }
        Start-Sleep -Seconds 2
    }
}
