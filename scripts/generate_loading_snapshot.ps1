param(
    [int]$Width = 2560,
    [int]$Height = 1440,
    [string]$OutputPath = ".\loading-zero-1440p.png"
)

$ErrorActionPreference = "Stop"

$edgePath = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if (!(Test-Path -LiteralPath $edgePath)) {
    $edgePath = "C:\Program Files\Microsoft\Edge\Application\msedge.exe"
}
if (!(Test-Path -LiteralPath $edgePath)) {
    throw "Microsoft Edge not found."
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$indexPath = Join-Path $projectRoot "index.html"
$resolvedOutput = Join-Path $projectRoot $OutputPath
$tempHtml = Join-Path $projectRoot ("nexuswpp-loading-snapshot-" + [Guid]::NewGuid().ToString("N") + ".html")
$userDataDir = Join-Path $env:TEMP ("nexuswpp-edge-snapshot-" + [Guid]::NewGuid().ToString("N"))

$html = Get-Content -LiteralPath $indexPath -Raw
$html = $html -replace '<div class="loading-snapshot" id="loading-snapshot" aria-hidden="true"></div>', ''
$html = $html -replace '</head>', @'
    <style>
        .loading-snapshot { display: none !important; }
    </style>
</head>
'@
$html = $html -replace '</body>', @'
    <script>
        function forceZeroSnapshotState() {
            const setText = (id, value) => {
                const element = document.getElementById(id);
                if (element) element.textContent = value;
            };

            setText("clock-h", "00");
            setText("clock-m", "00");
            setText("clock-s", "00");
            setText("clock-date", "-- -- ----");
            setText("clock-mb", "--");

            document.querySelectorAll(".remote-btn.active").forEach((button) => {
                button.classList.remove("active");
                button.setAttribute("aria-pressed", "false");
            });
        }

        window.addEventListener("load", function() {
            try {
                if (typeof dimGauges === "function") dimGauges();
                forceZeroSnapshotState();
                setTimeout(forceZeroSnapshotState, 250);
                setTimeout(forceZeroSnapshotState, 750);
                document.body.classList.remove("system-critical");
            } catch (e) {}
        });
    </script>
</body>
'@

Set-Content -LiteralPath $tempHtml -Value $html -Encoding UTF8

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Force
}

$uri = (New-Object System.Uri($tempHtml)).AbsoluteUri
$args = @(
    "--headless=new",
    "--disable-gpu",
    "--hide-scrollbars",
    "--allow-file-access-from-files",
    "--user-data-dir=$userDataDir",
    "--window-size=$Width,$Height",
    "--screenshot=$resolvedOutput",
    $uri
)

$process = Start-Process -FilePath $edgePath -ArgumentList $args -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $resolvedOutput)) {
    throw "Snapshot generation failed with exit code $($process.ExitCode)."
}

Remove-Item -LiteralPath $tempHtml -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $userDataDir -Recurse -Force -ErrorAction SilentlyContinue

Get-Item -LiteralPath $resolvedOutput
