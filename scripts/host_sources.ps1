# Host C# sources (every .cs file at the project root) plus the generated version stamp.
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
@(Get-ChildItem -LiteralPath $project -Filter *.cs -File | Sort-Object Name | ForEach-Object { $_.FullName }) + (Join-Path $project 'work\obj\ReleaseVersion.cs')
