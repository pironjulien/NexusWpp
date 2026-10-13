# Modern C# compiler (Roslyn from the .NET SDK) targeting the .NET Framework 4.8 built into Windows.
# Drop-in replacement for Framework64\v4.0.30319\csc.exe: same arguments, same default references.
$ErrorActionPreference = 'Stop'

$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sdkLine = & dotnet --list-sdks 2>$null | Select-Object -Last 1
if (-not $sdkLine -or $sdkLine -notmatch '^(\S+)\s+\[(.+)\]$') {
    throw 'The .NET SDK (8 or later) is required: https://dotnet.microsoft.com/download'
}
$compiler = Join-Path (Join-Path $Matches[2] $Matches[1]) 'Roslyn\bincore\csc.dll'
if (-not (Test-Path -LiteralPath $compiler)) { throw "C# compiler not found at $compiler" }

& dotnet $compiler /noconfig /nostdlib+ /langversion:latest "/lib:$framework" /reference:mscorlib.dll "@$framework\csc.rsp" @args
exit $LASTEXITCODE
