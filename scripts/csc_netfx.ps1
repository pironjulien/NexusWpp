# Modern C# compiler (Roslyn from the .NET SDK) targeting the .NET Framework 4.8 built into Windows.
# Only the installer uses it: a setup program must run before anything is installed, and
# .NET Framework 4.8 ships with Windows. The application itself runs on .NET 10 (csc.ps1).
$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sdkLine = & dotnet --list-sdks 2>$null | Select-Object -Last 1
if (-not $sdkLine -or $sdkLine -notmatch '^(\S+)\s+\[(.+)\]$') {
    throw 'The .NET 10 SDK is required: https://dotnet.microsoft.com/download'
}
$compiler = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw "C# compiler not found at $compiler" }

& dotnet $compiler /noconfig /nostdlib+ /langversion:latest "/lib:$framework" /reference:mscorlib.dll "@$framework\csc.rsp" @args
exit $LASTEXITCODE
