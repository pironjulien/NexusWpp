param(
    [string]$Label = 'current',
    [switch]$Quick
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$testDir = Join-Path $project 'bin\responsive-tests'
$output = Join-Path $project ('work\responsive\' + ($Label -replace '[^a-zA-Z0-9_-]', '_'))
New-Item -ItemType Directory -Path $testDir, $output -Force | Out-Null
foreach ($dll in @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')) {
    Copy-Item -LiteralPath (Join-Path $project ('bin\' + $dll)) -Destination $testDir -Force
}
$binary = Join-Path $testDir 'ResponsiveLayout.exe'
$refs = @('/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
    "/reference:$testDir\Microsoft.Web.WebView2.Core.dll", "/reference:$testDir\Microsoft.Web.WebView2.WinForms.dll")
& (Join-Path $PSScriptRoot 'csc.ps1') /nologo /target:exe "/out:$binary" @refs (Join-Path $PSScriptRoot 'tests\ResponsiveLayout.cs')
if ($LASTEXITCODE -ne 0) { throw 'Responsive test compilation failed.' }
$resolutions = @(
    @(1280,720), @(1280,800), @(1366,768), @(1440,900), @(1600,900), @(1920,1080), @(1920,1200),
    @(1920,1280), @(2256,1504), @(2560,1080), @(2560,1440), @(2560,1600), @(2880,1800),
    @(3000,2000), @(3200,1800), @(3440,1440), @(3840,1600), @(3840,2160), @(3840,2400),
    @(5120,1440), @(5120,2160), @(5120,2880), @(6016,3384), @(7680,2160), @(7680,4320),
    @(1080,1920), @(1200,1920), @(1440,2560), @(2160,3840)
)
if ($Quick) { $resolutions = @(@(1280,720), @(1920,1080), @(2560,1440), @(3840,2160), @(3440,1440), @(1080,1920)) }
$scales = @(1, 1.25, 1.5, 1.75, 2, 2.25, 2.5, 3, 3.5, 4)
if ($Quick) { $scales = @(1, 1.5, 2, 3) }
$cases = [Collections.Generic.List[object]]::new()
foreach ($resolution in $resolutions) {
    foreach ($scale in $scales) {
        $w = [int][math]::Floor($resolution[0] / $scale)
        $h = [int][math]::Floor($resolution[1] / $scale)
        if (($w -ge 800 -and $h -ge 450) -or ($w -ge 432 -and $h -ge 768)) {
            $id = "$($resolution[0])x$($resolution[1])-$([int]($scale*100))pct"
            $capture = ($resolution[0] -eq 3840 -and $resolution[1] -eq 2160 -and $scale -in @(1.5,2,2.25,2.5,3,4)) -or
                ($id -in @('1280x720-100pct','2560x1440-125pct','1080x1920-200pct','3440x1440-100pct','1920x1080-125pct'))
            $cases.Add(@{ id=$id; width=$w; height=$h; dpr=$scale; plans=4; hardware='full'; capture=$capture })
        }
    }
}
# Exercise both sides of historical breakpoints, hardware removal, and plan reflow.
foreach ($size in @(@(1000,900),@(1001,901),@(1280,900),@(1280,901),@(1920,1200),@(1920,1201),@(2500,1400),@(2501,1400),@(800,450),@(432,768))) {
    foreach ($plans in @(1,3,4)) {
        $cases.Add(@{ id="boundary-$($size[0])x$($size[1])-$plans"; width=$size[0]; height=$size[1]; dpr=1; plans=$plans; hardware='minimal'; capture=$false })
    }
}
$casesPath = Join-Path $output 'cases.json'
foreach ($size in @(@(800,450),@(960,540),@(1280,720),@(1706,960),@(432,768),@(540,960),@(1000,900),@(3840,2160))) {
    foreach ($hardware in @('full','dual-gpu','integrated','npu-only','cpu-only')) {
        foreach ($plans in @(1,2,3,4)) {
            $cases.Add(@{ id="hardware-$($size[0])x$($size[1])-$hardware-$plans"; width=$size[0]; height=$size[1]; dpr=1.25; plans=$plans; hardware=$hardware; capture=($plans -eq 4 -and $hardware -eq 'full' -and $size[0] -in @(800,432)) })
        }
    }
}
# Laptop on battery: the longest battery line (percent, source, power, time left) and an NPU client name.
foreach ($size in @(@(800,450),@(432,768),@(1280,720),@(1920,1080),@(3840,2160))) {
    foreach ($dpr in @(1, 1.5, 2)) {
        $cases.Add(@{ id="battery-$($size[0])x$($size[1])-$([int]($dpr*100))pct"; width=$size[0]; height=$size[1]; dpr=$dpr; plans=3; hardware='full';
            battery=@{ present=$true; percent=73; ac=$false; rateW=-12.4; minutesLeft=135 }; capture=($size[0] -eq 1920 -and $dpr -eq 1) })
    }
}
# Multi-monitor desktops: the window spans every monitor, the cockpit stays on the primary one.
# Window size, then primary monitor rectangle relative to the window (negative virtual origins
# put the primary away from the window's top-left corner).
$topologies = @(
    @{ id='side-by-side-primary-right'; w=3840; h=1080; p=@(1920,0,1920,1080) },
    @{ id='side-by-side-primary-left';  w=3840; h=1080; p=@(0,0,1920,1080) },
    @{ id='stacked-primary-below';      w=1920; h=2160; p=@(0,1080,1920,1080) },
    @{ id='l-shape-portrait-left';      w=3000; h=1920; p=@(1080,420,1920,1080) },
    @{ id='mixed-sizes-primary-right';  w=3584; h=1440; p=@(1024,0,2560,1440) },
    @{ id='three-monitors-middle';      w=5760; h=1080; p=@(1920,0,1920,1080) },
    @{ id='portrait-primary';           w=3000; h=1920; p=@(0,0,1080,1920) }
)
foreach ($topology in $topologies) {
    foreach ($dpr in @(1, 1.25)) {
        $w = [int][math]::Floor($topology.w / $dpr); $h = [int][math]::Floor($topology.h / $dpr)
        $primary = @{ x=$topology.p[0] / $dpr; y=$topology.p[1] / $dpr; w=$topology.p[2] / $dpr; h=$topology.p[3] / $dpr }
        $cases.Add(@{ id="monitors-$($topology.id)-$([int]($dpr*100))pct"; width=$w; height=$h; dpr=$dpr; plans=4; hardware='full';
            layout=@{ control='LAYOUT'; window=@{ w=$w; h=$h }; primary=$primary }; capture=($dpr -eq 1) })
    }
}
$expanded = foreach ($item in $cases) {
    foreach ($state in @('normal','high','stale')) {
        $variant=$item.Clone(); $variant.state=$state; $variant.id=$item.id+'-'+$state; $variant
    }
}
$expanded | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $casesPath -Encoding UTF8
# Every case runs in both interface languages: English labels have other lengths.
foreach ($language in @('fr', 'en')) {
    $languageOutput = Join-Path $output $language
    New-Item -ItemType Directory -Path $languageOutput -Force | Out-Null
    & $binary $project $languageOutput $casesPath $language
    if ($LASTEXITCODE -ne 0) { throw "Responsive layout failures ($language); see $languageOutput\results.json" }
}
