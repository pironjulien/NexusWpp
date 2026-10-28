# Host C# sources (every .cs file at the project root) plus the generated version stamp,
# then the WinRT references they need beyond csc.rsp (StartupTask in PriorityStartup.cs).
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$winmd = Join-Path $env:WINDIR 'System32\WinMetadata'
@(Get-ChildItem -LiteralPath $project -Filter *.cs -File | Sort-Object Name | ForEach-Object { $_.FullName }) + (Join-Path $project 'work\obj\ReleaseVersion.cs') +
    @("/reference:$framework\System.Runtime.dll",
      "/reference:$winmd\Windows.Foundation.winmd", "/reference:$winmd\Windows.ApplicationModel.winmd")
