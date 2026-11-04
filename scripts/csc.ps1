# Builds csc-style arguments (/out:, /target:, /main:, /reference:, sources...) as a .NET 10 Windows
# project, so the test and tool harnesses compile against the same runtime as the host.
# Unsafe blocks are on, as in the host (LibraryImport stubs).
# The output folder receives <name>.exe with its runtime files; it runs on the installed .NET 10
# desktop runtime (the shipped app itself is self-contained, see NexusWpp.csproj).
$ErrorActionPreference = 'Stop'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET 10 SDK is required: https://dotnet.microsoft.com/download'
}

$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$out = $null; $outputType = 'Exe'; $main = $null; $platform = 'AnyCPU'; $unsafe = 'true'; $icon = $null; $manifest = $null
$sources = [System.Collections.Generic.List[string]]::new()
$references = [System.Collections.Generic.List[string]]::new()
$packages = [System.Collections.Generic.List[string]]::new()
$resources = [System.Collections.Generic.List[string[]]]::new()

foreach ($arg in $args) {
    switch -Regex ($arg) {
        '^/nologo$' { break }
        '^/optimize[+-]?$' { break }
        '^/out:(.+)$' { $out = [IO.Path]::GetFullPath($Matches[1]); break }
        '^/target:winexe$' { $outputType = 'WinExe'; break }
        '^/target:exe$' { $outputType = 'Exe'; break }
        '^/main:(.+)$' { $main = $Matches[1]; break }
        '^/platform:(.+)$' { $platform = $Matches[1]; break }
        '^/unsafe[+]?$' { $unsafe = 'true'; break }
        '^/win32icon:(.+)$' { $icon = [IO.Path]::GetFullPath($Matches[1]); break }
        '^/win32manifest:(.+)$' { $manifest = [IO.Path]::GetFullPath($Matches[1]); break }
        '^/resource:([^,]+),(.+)$' { $resources.Add(@([IO.Path]::GetFullPath($Matches[1]), $Matches[2])); break }
        '^/reference:(.+)$' {
            $reference = $Matches[1]
            if ([IO.Path]::IsPathRooted($reference)) { $references.Add($reference) }
            # Part of .NET Framework, a NuGet package on .NET.
            elseif ($reference -eq 'System.Management.dll') { $packages.Add('System.Management') }
            # Anything else (System.Windows.Forms.dll, System.Drawing.dll...) is in the Windows desktop framework.
            break
        }
        '^/' { throw "csc.ps1: unsupported option $arg" }
        default { $sources.Add([IO.Path]::GetFullPath($arg)) }
    }
}
if (-not $out) { throw 'csc.ps1: /out: is required.' }

# Same package versions as the host project.
$hostProject = [xml](Get-Content -LiteralPath (Join-Path $project 'NexusWpp.csproj') -Raw)
function Get-PackageVersion([string]$name) {
    $node = $hostProject.SelectSingleNode("//PackageReference[@Include='$name']")
    if (-not $node) { throw "csc.ps1: $name is not referenced by NexusWpp.csproj." }
    $node.Version
}

$name = [IO.Path]::GetFileNameWithoutExtension($out)
$outDir = Split-Path -Parent $out
$projectDir = Join-Path $project "work\obj\csc\$name"
New-Item -ItemType Directory -Path $projectDir, $outDir -Force | Out-Null
$esc = { param($value) [System.Security.SecurityElement]::Escape($value) }

$items = @()
$items += $sources | ForEach-Object { "    <Compile Include=`"$(& $esc $_)`" />" }
$items += $references | ForEach-Object {
    "    <Reference Include=`"$(& $esc ([IO.Path]::GetFileNameWithoutExtension($_)))`"><HintPath>$(& $esc $_)</HintPath></Reference>"
}
$items += $packages | Select-Object -Unique | ForEach-Object { "    <PackageReference Include=`"$_`" Version=`"$(Get-PackageVersion $_)`" />" }
$items += $resources | ForEach-Object { "    <EmbeddedResource Include=`"$(& $esc $_[0])`" LogicalName=`"$(& $esc $_[1])`" />" }

$properties = @(
    "<OutputType>$outputType</OutputType>",
    '<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>',
    '<SupportedOSPlatformVersion>10.0.19041.0</SupportedOSPlatformVersion>',
    '<UseWindowsForms>true</UseWindowsForms>',
    '<LangVersion>latest</LangVersion>',
    "<PlatformTarget>$platform</PlatformTarget>",
    "<AllowUnsafeBlocks>$unsafe</AllowUnsafeBlocks>",
    "<AssemblyName>$name</AssemblyName>",
    '<EnableDefaultItems>false</EnableDefaultItems>',
    '<GenerateAssemblyVersionAttribute>false</GenerateAssemblyVersionAttribute>',
    '<GenerateAssemblyFileVersionAttribute>false</GenerateAssemblyFileVersionAttribute>',
    '<GenerateAssemblyInformationalVersionAttribute>false</GenerateAssemblyInformationalVersionAttribute>',
    '<SatelliteResourceLanguages>en</SatelliteResourceLanguages>',
    '<DebugType>none</DebugType>',
    '<Optimize>true</Optimize>'
)
if ($main) { $properties += "<StartupObject>$main</StartupObject>" }
if ($icon) { $properties += "<ApplicationIcon>$(& $esc $icon)</ApplicationIcon>" }
if ($manifest) { $properties += "<ApplicationManifest>$(& $esc $manifest)</ApplicationManifest>" }

$csproj = Join-Path $projectDir "$name.csproj"
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    $($properties -join "`n    ")
  </PropertyGroup>
  <ItemGroup>
$($items -join "`n")
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $csproj -Encoding UTF8

& dotnet build $csproj -c Release -o $outDir --nologo -v quiet -clp:ErrorsOnly
exit $LASTEXITCODE
