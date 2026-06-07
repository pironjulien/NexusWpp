param(
    [int]$Width = 5120,
    [int]$Height = 1440,
    [double]$RenderScale = 1.25,
    [ValidateSet("Zero", "Normal")]
    [string]$Mode = "Zero",
    [string]$OutputPath = ".\loading-zero-5120x1440.png"
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
$snapshotTempDir = Join-Path $projectRoot ".tmp\snapshot"
New-Item -ItemType Directory -Path $snapshotTempDir -Force | Out-Null
$rawOutput = Join-Path $snapshotTempDir ("nexuswpp-loading-raw-" + [Guid]::NewGuid().ToString("N") + ".png")
$tempHtml = Join-Path $projectRoot ("nexuswpp-loading-snapshot-" + [Guid]::NewGuid().ToString("N") + ".html")
$userDataDir = Join-Path $snapshotTempDir ("nexuswpp-edge-snapshot-" + [Guid]::NewGuid().ToString("N"))

if ($RenderScale -le 0) {
    throw "RenderScale must be greater than 0."
}

$renderWidth = [int][Math]::Round($Width / $RenderScale)
$renderHeight = [int][Math]::Round($Height / $RenderScale)

function Get-TaskbarBottomPixels {
    param([int]$ScreenHeight)

    try {
        $source = @'
using System;
using System.Runtime.InteropServices;

public static class NexusSnapshotDisplayMetrics {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SystemParametersInfo(int uiAction, int uiParam, ref RECT pvParam, int fWinIni);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
'@
        if (-not ("NexusSnapshotDisplayMetrics" -as [type])) {
            Add-Type -TypeDefinition $source
        }

        [NexusSnapshotDisplayMetrics]::SetProcessDPIAware() | Out-Null
        $workArea = New-Object NexusSnapshotDisplayMetrics+RECT
        if ([NexusSnapshotDisplayMetrics]::SystemParametersInfo(0x0030, 0, [ref]$workArea, 0)) {
            $nativeHeight = [NexusSnapshotDisplayMetrics]::GetSystemMetrics(1)
            if ($nativeHeight -gt 0 -and $workArea.Bottom -gt 0) {
                $scaleToOutput = $ScreenHeight / $nativeHeight
                return [int][Math]::Max(0, [Math]::Round(($nativeHeight - $workArea.Bottom) * $scaleToOutput))
            }
        }
    } catch {}

    return 0
}

$taskbarBottomPixels = Get-TaskbarBottomPixels -ScreenHeight $Height

function Convert-WmiDateToDisplay {
    param([string]$Raw)
    if ([string]::IsNullOrWhiteSpace($Raw) -or $Raw.Length -lt 8) { return "" }
    try {
        return "{0}/{1}/{2}" -f $Raw.Substring(6, 2), $Raw.Substring(4, 2), $Raw.Substring(0, 4)
    } catch {
        return ""
    }
}

function Get-CurrentPowerPlans {
    $plans = @()
    try {
        $output = & powercfg /list
        foreach ($line in $output) {
            $match = [regex]::Match($line, ":\s*([a-f0-9\-]+)\s*\(([^)]+)\)", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if ($match.Success) {
                $plans += [pscustomobject]@{
                    guid = $match.Groups[1].Value.Trim()
                    name = $match.Groups[2].Value.Trim()
                    active = $line.Contains("*")
                }
            }
        }
    } catch {}

    return $plans
}

$cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1
$baseBoard = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue | Select-Object -First 1
$memory = Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue
$videoControllers = Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue
$diskDrive = Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | Select-Object -First 1
$npuDriver = Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue |
    Where-Object { $_.DeviceName -match "AI Boost|Neural|VPU|Inference|(^|[ (])NPU([ )]|$)" } |
    Select-Object -First 1
$network = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
    Where-Object {
        $_.OperationalStatus -eq [System.Net.NetworkInformation.OperationalStatus]::Up -and
        $_.NetworkInterfaceType -ne [System.Net.NetworkInformation.NetworkInterfaceType]::Loopback
    } |
    Sort-Object Speed -Descending |
    Select-Object -First 1
$powerPlans = @(Get-CurrentPowerPlans)

$ramTotalGb = 0
$ramSpeedMts = 0
if ($memory) {
    $ramTotalBytes = ($memory | Measure-Object -Property Capacity -Sum).Sum
    if ($ramTotalBytes) { $ramTotalGb = [int][Math]::Round($ramTotalBytes / 1GB) }
    $speed = ($memory | Where-Object { $_.Speed } | Select-Object -First 1 -ExpandProperty Speed)
    if ($speed) { $ramSpeedMts = [int]$speed }
}

$gpuName = ""
$igpuName = ""
foreach ($vc in $videoControllers) {
    $name = [string]$vc.Name
    if ($name -match "NVIDIA") { $gpuName = $name }
    elseif ($name -match "Intel|Arc|UHD") { $igpuName = $name }
}

$boardName = ""
if ($baseBoard) {
    $manufacturer = ([string]$baseBoard.Manufacturer).Replace(" Co., Ltd.", "").Replace("ASUSTeK COMPUTER INC.", "ASUS").Trim()
    $product = ([string]$baseBoard.Product).Trim()
    $boardName = "$manufacturer ($product)".Trim()
}

$hardware = [ordered]@{
    cpuName = if ($cpu.Name) { ([string]$cpu.Name).Trim() } else { "" }
    gpuName = $gpuName
    igpuName = $igpuName
    npuName = if ($npuDriver.DeviceName) { [string]$npuDriver.DeviceName } else { "" }
    npuDetected = [bool]$npuDriver
    npuDriverDate = Convert-WmiDateToDisplay ([string]$npuDriver.DriverDate)
    ramTotalGb = $ramTotalGb
    ramSpeedMts = $ramSpeedMts
    networkName = if ($network) { $network.Description } else { "" }
    diskName = if ($diskDrive.Model) { ([string]$diskDrive.Model).Trim() } else { "" }
    motherboard = $boardName
    powerPlans = @($powerPlans)
}
$hardwareJson = $hardware | ConvertTo-Json -Compress -Depth 6

function Get-PowerPlanButtonId {
    param([string]$Name)

    $lowerName = $Name.ToLowerInvariant()
    if ($lowerName.Contains("gamer") -or $lowerName.Contains("extreme") -or $lowerName.Contains("perform") -or $lowerName.Contains("jeux")) {
        return "btn-extreme"
    }
    if ($lowerName.Contains("veille") -or $lowerName.Contains("eco") -or $lowerName.Contains("silent") -or $lowerName.Contains("silencieux") -or $lowerName.Contains("econom")) {
        return "btn-eco"
    }
    return "btn-balanced"
}

function Get-PowerPlanIconSvg {
    param([string]$Name)

    $buttonId = Get-PowerPlanButtonId -Name $Name
    if ($buttonId -eq "btn-extreme") {
        return '<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor" aria-hidden="true"><path d="M12 2L1 21h22L12 2zm0 4l7.5 13h-15L12 6zm-1 5h2v4h-2v-4zm0-3h2v2h-2V8z"/></svg>'
    }
    if ($buttonId -eq "btn-eco") {
        return '<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor" aria-hidden="true"><path d="M9.5 2c-1.82 0-3.53.5-5 1.35 2.99 1.73 5 4.95 5 8.65s-2.01 6.92-5 8.65c1.47.85 3.18 1.35 5 1.35 5.52 0 10-4.48 10-10S15.02 2 9.5 2z"/></svg>'
    }
    return '<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor" aria-hidden="true"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"/></svg>'
}

$powerPlanButtonsHtml = ""
foreach ($plan in $powerPlans) {
    if ([string]::IsNullOrWhiteSpace($plan.guid) -or [string]::IsNullOrWhiteSpace($plan.name)) { continue }

    $encodedGuid = [System.Net.WebUtility]::HtmlEncode($plan.guid)
    $encodedName = [System.Net.WebUtility]::HtmlEncode($plan.name.ToUpperInvariant())
    $activeClass = if ($plan.active) { " active" } else { "" }
    $pressed = if ($plan.active) { "true" } else { "false" }
    $buttonId = if ($plan.active) { ' id="' + (Get-PowerPlanButtonId -Name $plan.name) + '"' } else { "" }
    $iconSvg = Get-PowerPlanIconSvg -Name $plan.name
    $powerPlanButtonsHtml += @"
                        <button class="remote-btn$activeClass" data-guid="$encodedGuid"$buttonId aria-pressed="$pressed">
                            <div class="icon-wrap">$iconSvg</div>
                            <span class="btn-lbl">$encodedName</span>
                        </button>
"@
}

$html = Get-Content -LiteralPath $indexPath -Raw -Encoding UTF8
$html = $html -replace '<div class="remote-grid horizontal" id="power-plans-container"></div>', ("<div class=""remote-grid horizontal"" id=""power-plans-container"">`r`n" + $powerPlanButtonsHtml + "                    </div>")
$html = $html -replace '<div class="loading-snapshot" id="loading-snapshot" aria-hidden="true"></div>', ''
$html = $html -replace '</head>', @'
    <style>
        .loading-snapshot { display: none !important; }
    </style>
</head>
'@
$snapshotScript = if ($Mode -eq "Zero") { @'
    <script>
        const snapshotHardware = __SNAPSHOT_HARDWARE__;
        function getSnapshotPowerPlanIcon(name) {
            const lowerName = String(name || "").toLowerCase();
            if (lowerName.includes("gamer") || lowerName.includes("extreme") || lowerName.includes("perform") || lowerName.includes("jeux")) {
                return `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M12 2L1 21h22L12 2zm0 4l7.5 13h-15L12 6zm-1 5h2v4h-2v-4zm0-3h2v2h-2V8z"/></svg>`;
            }
            if (lowerName.includes("veille") || lowerName.includes("eco") || lowerName.includes("silent") || lowerName.includes("silencieux") || lowerName.includes("econom")) {
                return `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M9.5 2c-1.82 0-3.53.5-5 1.35 2.99 1.73 5 4.95 5 8.65s-2.01 6.92-5 8.65c1.47.85 3.18 1.35 5 1.35 5.52 0 10-4.48 10-10S15.02 2 9.5 2z"/></svg>`;
            }
            return `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"/></svg>`;
        }

        function getSnapshotPowerPlanId(name) {
            const lowerName = String(name || "").toLowerCase();
            if (lowerName.includes("gamer") || lowerName.includes("extreme") || lowerName.includes("perform") || lowerName.includes("jeux")) return "btn-extreme";
            if (lowerName.includes("veille") || lowerName.includes("eco") || lowerName.includes("silent") || lowerName.includes("silencieux") || lowerName.includes("econom")) return "btn-eco";
            return "btn-balanced";
        }

        function renderSnapshotPowerPlans() {
            const container = document.getElementById("power-plans-container");
            if (!container) return;

            container.innerHTML = "";
            (snapshotHardware.powerPlans || []).forEach((plan) => {
                if (!plan || !plan.guid || !plan.name) return;

                const button = document.createElement("button");
                button.className = plan.active ? "remote-btn active" : "remote-btn";
                button.dataset.guid = plan.guid;
                button.setAttribute("aria-pressed", plan.active ? "true" : "false");
                if (plan.active) {
                    button.id = getSnapshotPowerPlanId(plan.name);
                }

                const iconWrap = document.createElement("div");
                iconWrap.className = "icon-wrap";
                iconWrap.innerHTML = getSnapshotPowerPlanIcon(plan.name);

                const label = document.createElement("span");
                label.className = "btn-lbl";
                label.textContent = String(plan.name).toUpperCase();

                button.appendChild(iconWrap);
                button.appendChild(label);
                container.appendChild(button);
            });
        }

        function drawSnapshotTelemetryMap() {
            const mapPanel = document.querySelector(".hologram-sub-panel");
            if (!mapPanel) return;

            let canvas = document.getElementById("snapshot-telemetry-map");
            if (!canvas) {
                canvas = document.createElement("canvas");
                canvas.id = "snapshot-telemetry-map";
                canvas.setAttribute("aria-hidden", "true");
                mapPanel.appendChild(canvas);
            }

            const panelRect = mapPanel.getBoundingClientRect();
            const height = Math.max(1, Math.round(panelRect.height));
            const width = Math.max(1, Math.round(panelRect.width));

            canvas.style.position = "absolute";
            canvas.style.left = "0";
            canvas.style.top = "0";
            canvas.style.width = "100%";
            canvas.style.height = "100%";
            canvas.style.zIndex = "2";
            canvas.style.pointerEvents = "none";

            canvas.width = width;
            canvas.height = height;

            const ctx = canvas.getContext("2d");
            if (!ctx) return;

            const baseCx = width / 2;
            const baseCy = height / 2;
            const coreTargetOffsetX = 10;
            const coreTargetOffsetY = -26;
            const cx = baseCx + coreTargetOffsetX;
            const cy = baseCy + coreTargetOffsetY;
            const maxAvailableRadius = Math.max(80, Math.min(cx, width - cx, cy, height - cy) - 28);
            const maxSafeNodeOrbit = Math.max(96, Math.min(cx - 44, width - cx - 44, cy - 44, height - cy - 72));
            const preferredOrbit = Math.min(Math.min(width, height) * 0.38, 320);
            const orbitRadius = Math.max(96, Math.min(preferredOrbit, maxSafeNodeOrbit));
            const radarStep = orbitRadius / 3;
            const maxRadarRadius = Math.max(orbitRadius, Math.min(maxAvailableRadius, radarStep * 5));

            ctx.clearRect(0, 0, width, height);
            ctx.strokeStyle = "rgba(255, 255, 255, 0.032)";
            ctx.lineWidth = 1;
            for (let r = radarStep; r <= maxRadarRadius + 0.01; r += radarStep) {
                ctx.beginPath();
                ctx.arc(cx, cy, r, 0, Math.PI * 2);
                ctx.stroke();
            }
            ctx.beginPath();
            ctx.moveTo(cx - maxRadarRadius, cy);
            ctx.lineTo(cx + maxRadarRadius, cy);
            ctx.moveTo(cx, cy - maxRadarRadius);
            ctx.lineTo(cx, cy + maxRadarRadius);
            ctx.stroke();

            const center = {
                x: cx,
                y: cy,
                radius: 50,
                color: "#30d158",
                colorEnd: "#0a84ff",
                glow: "#30d158"
            };
            const nodes = [
                { angle: -150, label: "CPU", desc: "CHARGE CPU", value: "0%", color: "#bf5aff", colorEnd: "#8a2be2", glow: "#bf5aff" },
                { angle: -90, label: "R\u00c9SEAU", desc: "ACTIVIT\u00c9 R\u00c9SEAU", value: "0%", color: "#ff9f0a", colorEnd: "#ffd60a", glow: "#ff9f0a" },
                { angle: -30, label: "RAM", desc: "ACTIVIT\u00c9 RAM", value: "0%", color: "#0a84ff", colorEnd: "#00c7be", glow: "#0a84ff" },
                { angle: 30, label: "SSD C:", desc: "ACTIVIT\u00c9 DISQUE", value: "0%", color: "#00c7be", colorEnd: "#64d2ff", glow: "#00c7be" },
                { angle: 90, label: "GPU", desc: "CHARGE GPU", value: "0%", color: "#ff375f", colorEnd: "#ff2d55", glow: "#ff375f" },
                { angle: 150, label: "iGPU", desc: "CHARGE iGPU", value: "0%", color: "#30d158", colorEnd: "#1db954", glow: "#30d158" }
            ];

            if (snapshotHardware.npuDetected === true) {
                nodes.push({ angle: 180, label: "NPU", desc: "CHARGE NPU", value: "0%", color: "#00c7be", colorEnd: "#64d2ff", glow: "#00c7be" });
            }

            nodes.forEach((node) => {
                const rad = node.angle * Math.PI / 180;
                node.x = cx + Math.cos(rad) * orbitRadius;
                node.y = cy + Math.sin(rad) * orbitRadius;

                const bgGrad = ctx.createLinearGradient(node.x, node.y, center.x, center.y);
                bgGrad.addColorStop(0, node.color + "18");
                bgGrad.addColorStop(0.5, (node.colorEnd || node.color) + "0c");
                bgGrad.addColorStop(1, center.color + "02");
                ctx.beginPath();
                ctx.moveTo(node.x, node.y);
                ctx.lineTo(center.x, center.y);
                ctx.strokeStyle = bgGrad;
                ctx.lineWidth = 3.6;
                ctx.stroke();

                const coreGrad = ctx.createLinearGradient(node.x, node.y, center.x, center.y);
                coreGrad.addColorStop(0, node.color + "44");
                coreGrad.addColorStop(0.5, (node.colorEnd || node.color) + "22");
                coreGrad.addColorStop(1, center.color + "05");
                ctx.beginPath();
                ctx.moveTo(node.x, node.y);
                ctx.lineTo(center.x, center.y);
                ctx.strokeStyle = coreGrad;
                ctx.lineWidth = 1.8;
                ctx.stroke();
            });

            function drawRingNode(node, isCenter) {
                const radius = isCenter ? center.radius : 26;
                const ringGrad = ctx.createLinearGradient(node.x - radius, node.y - radius, node.x + radius, node.y + radius);
                ringGrad.addColorStop(0, node.color);
                ringGrad.addColorStop(1, node.colorEnd || node.color);

                ctx.fillStyle = node.glow;
                ctx.globalAlpha = 0.12;
                ctx.beginPath();
                ctx.arc(node.x, node.y, radius + 8, 0, Math.PI * 2);
                ctx.fill();
                ctx.beginPath();
                ctx.arc(node.x, node.y, radius + 4, 0, Math.PI * 2);
                ctx.fill();
                ctx.globalAlpha = 1;

                ctx.fillStyle = "rgba(4, 5, 9, 0.85)";
                ctx.strokeStyle = ringGrad;
                ctx.lineWidth = 2.2;
                ctx.beginPath();
                ctx.arc(node.x, node.y, radius, 0, Math.PI * 2);
                ctx.fill();
                ctx.stroke();

                ctx.lineWidth = 1;
                ctx.beginPath();
                ctx.arc(node.x, node.y, radius + 7, 0, Math.PI * 2);
                ctx.stroke();

                ctx.lineWidth = 2.5;
                ctx.beginPath();
                ctx.arc(node.x, node.y, radius + 7, 0, Math.PI / 4);
                ctx.stroke();
                ctx.beginPath();
                ctx.arc(node.x, node.y, radius + 7, Math.PI, Math.PI + Math.PI / 4);
                ctx.stroke();

                if (isCenter) {
                    const img = typeof logoImg !== "undefined" ? logoImg : null;
                    if (img && img.complete && img.naturalWidth > 0) {
                        const logoRadius = radius - 5;
                        const logoDiameter = logoRadius * 2;
                        ctx.save();
                        ctx.beginPath();
                        ctx.arc(node.x, node.y, logoRadius, 0, Math.PI * 2);
                        ctx.clip();
                        ctx.drawImage(img, node.x - logoDiameter / 2, node.y - logoDiameter / 2, logoDiameter, logoDiameter);
                        ctx.restore();
                    }
                    return;
                }

                ctx.textAlign = "center";
                ctx.textBaseline = "middle";
                ctx.font = "bold 13px Bahnschrift, 'Segoe UI', sans-serif";
                ctx.fillStyle = "#ffffff";
                ctx.fillText(node.value, node.x, node.y - 3);
                ctx.font = "900 11px Bahnschrift, 'Segoe UI', sans-serif";
                ctx.fillStyle = "rgba(255, 255, 255, 0.62)";
                ctx.fillText(node.label, node.x, node.y + radius + 20);
                ctx.font = "900 9px Bahnschrift, 'Segoe UI', sans-serif";
                ctx.fillStyle = "rgba(255, 255, 255, 0.34)";
                ctx.fillText(node.desc, node.x, node.y + radius + 32);
            }

            nodes.forEach((node) => drawRingNode(node, false));
            drawRingNode(center, true);
        }

        function buildZeroSnapshotStats() {
            return {
                totalProcesses: 0,
                uptime: 0,
                motherboard: snapshotHardware.motherboard || "",
                ping: 0,
                cpu: {
                    utilization: 0,
                    temp: 0,
                    freqGhz: "0.00",
                    threads: 0,
                    l2Cache: "0 Mo",
                    l3Cache: "0 Mo",
                    name: snapshotHardware.cpuName || ""
                },
                fans: { fan1: 0, fan2: 0 },
                igpu: {
                    utilization: 0,
                    usedMb: 0,
                    totalMb: 0,
                    name: snapshotHardware.igpuName || ""
                },
                npu: {
                    utilization: 0,
                    name: snapshotHardware.npuName || "Intel(R) AI Boost",
                    usedMb: 0,
                    totalMb: 0,
                    totalGb: 0,
                    detected: snapshotHardware.npuDetected === true
                },
                gpu: {
                    utilization: 0,
                    temp: 0,
                    coreClock: 0,
                    tops: 0,
                    name: snapshotHardware.gpuName || ""
                },
                vram: { usedMb: 0, totalMb: 0 },
                ram: {
                    utilization: 0,
                    totalGb: snapshotHardware.ramTotalGb || 0,
                    cachedGb: "0.0",
                    poolPagedMb: 0,
                    poolNonPagedMb: 0,
                    commitUsedGb: "0.0",
                    commitLimitGb: "0.0",
                    speedMts: snapshotHardware.ramSpeedMts || 0,
                    activity: 0
                },
                disk: {
                    storagePercent: 0,
                    utilization: 0,
                    freeGb: 0,
                    totalGb: 0,
                    readMb: "0.0",
                    writeMb: "0.0",
                    responseTimeMs: 0,
                    name: snapshotHardware.diskName || ""
                },
                network: {
                    lan: 0,
                    wifi: 0,
                    ip: "",
                    ipv6: "",
                    type: "",
                    name: snapshotHardware.networkName || "",
                    linkSpeedMbps: 0
                },
                powerPlans: snapshotHardware.powerPlans || []
            };
        }

        function forceZeroSnapshotState() {
            const setText = (id, value) => {
                const element = document.getElementById(id);
                if (element) element.textContent = value;
            };

            if (typeof updateDOM === "function") {
                updateDOM(buildZeroSnapshotStats());
            }
            renderSnapshotPowerPlans();

            setText("clock-h", "00");
            setText("clock-m", "00");
            setText("clock-s", "00");
            setText("clock-date", "-- -- ----");
            setText("clock-mb", "--");

            setText("spec-cpu-name", snapshotHardware.cpuName ? "CPU " + snapshotHardware.cpuName : "CPU");
            setText("spec-vram-name", snapshotHardware.igpuName ? "iGPU " + snapshotHardware.igpuName : "iGPU");
            setText("spec-gpu-name", snapshotHardware.gpuName ? "GPU " + snapshotHardware.gpuName : "GPU");
            setText("spec-ram-name", snapshotHardware.ramTotalGb ? "RAM " + snapshotHardware.ramTotalGb + " Go DDR5 Dual-Channel" + (snapshotHardware.ramSpeedMts ? " @ " + snapshotHardware.ramSpeedMts + " MT/s" : "") : "RAM");
            setText("spec-ssd-name", snapshotHardware.diskName ? "SSD " + snapshotHardware.diskName : "SSD");
            setText("spec-net-mode", snapshotHardware.networkName ? "R\u00c9SEAU " + snapshotHardware.networkName : "R\u00c9SEAU");
            if (snapshotHardware.motherboard) {
                setText("clock-mb", "CARTE M\u00c8RE " + snapshotHardware.motherboard);
            }

            if (typeof setNpuVisibility === "function") {
                setNpuVisibility(snapshotHardware.npuDetected);
            }
            if (snapshotHardware.npuDetected && snapshotHardware.npuName) {
                setText("spec-npu-name", "NPU " + snapshotHardware.npuName);
            }

            if (typeof telemetryNodes !== "undefined") {
                telemetryNodes.npu.value = 0;
                telemetryNodes.npu.subLabel = "CORE ACTIVE";
                telemetryNodes.cpu.value = 0;
                telemetryNodes.cpu.subLabel = "0 °C";
                telemetryNodes.igpu.value = 0;
                telemetryNodes.igpu.subLabel = "0.0 Go";
                telemetryNodes.npuAccel.value = 0;
                telemetryNodes.npuAccel.subLabel = "0.0 Go";
                telemetryNodes.ram.value = 0;
                telemetryNodes.ram.subLabel = "0.0 / " + (snapshotHardware.ramTotalGb || 0) + " Go";
                telemetryNodes.gpu.value = 0;
                telemetryNodes.gpu.subLabel = "0 °C";
                telemetryNodes.ssd.value = 0;
                telemetryNodes.ssd.subLabel = "0 Go libre";
                telemetryNodes.net.value = 0;
                telemetryNodes.net.suffix = "%";
                telemetryNodes.net.subLabel = "0.0 Ko/s";
            }

            if (typeof resizeCanvas === "function") resizeCanvas();
            if (typeof initNodes === "function") initNodes();
            if (typeof telemetryNodes !== "undefined" && typeof telemetryNodeList !== "undefined" && typeof renderNodeOffscreen === "function") {
                telemetryNodeList.forEach((node) => {
                    if (node !== telemetryNodes.npu) renderNodeOffscreen(node);
                });
            }
            if (typeof dataPackets !== "undefined") dataPackets.length = 0;
            if (typeof coreParticles !== "undefined") coreParticles.length = 0;
            if (typeof isCanvasLoopRunning !== "undefined") isCanvasLoopRunning = true;
            if (typeof needsRender !== "undefined") needsRender = true;
            if (typeof lastPhysicsTime !== "undefined") lastPhysicsTime = 0;
            if (typeof updatePhysics === "function") updatePhysics(performance.now() + 1000);
            drawSnapshotTelemetryMap();
            if (typeof canvasFrameTimer !== "undefined" && canvasFrameTimer) {
                clearTimeout(canvasFrameTimer);
                canvasFrameTimer = 0;
            }
            if (typeof isCanvasLoopRunning !== "undefined") isCanvasLoopRunning = false;
            if (typeof needsRender !== "undefined") needsRender = false;
        }

        function applyZeroSnapshotFrame() {
            try {
                if (typeof dimGauges === "function") dimGauges();
                forceZeroSnapshotState();
                requestAnimationFrame(forceZeroSnapshotState);
                setTimeout(forceZeroSnapshotState, 250);
                setTimeout(forceZeroSnapshotState, 750);
                document.body.classList.remove("system-critical");
            } catch (e) {}
        }

        applyZeroSnapshotFrame();
        document.addEventListener("DOMContentLoaded", applyZeroSnapshotFrame);
        window.addEventListener("load", applyZeroSnapshotFrame);
    </script>
</body>
'@
} else { @'
    <script>
        const snapshotHardware = __SNAPSHOT_HARDWARE__;

        function applySnapshotHardwareLabels() {
            const setText = (id, value) => {
                const element = document.getElementById(id);
                if (element) element.textContent = value;
            };

            setText("spec-cpu-name", snapshotHardware.cpuName ? "CPU " + snapshotHardware.cpuName : "CPU");
            setText("spec-vram-name", snapshotHardware.igpuName ? "iGPU " + snapshotHardware.igpuName : "iGPU");
            setText("spec-gpu-name", snapshotHardware.gpuName ? "GPU " + snapshotHardware.gpuName : "GPU");
            setText("spec-ram-name", snapshotHardware.ramTotalGb ? "RAM " + snapshotHardware.ramTotalGb + " Go DDR5 Dual-Channel" + (snapshotHardware.ramSpeedMts ? " @ " + snapshotHardware.ramSpeedMts + " MT/s" : "") : "RAM");
            setText("spec-ssd-name", snapshotHardware.diskName ? "SSD " + snapshotHardware.diskName : "SSD");
            setText("spec-net-mode", snapshotHardware.networkName ? "R\u00c9SEAU " + snapshotHardware.networkName : "R\u00c9SEAU");
            if (snapshotHardware.motherboard) {
                setText("clock-mb", "CARTE M\u00c8RE " + snapshotHardware.motherboard);
            }

            if (typeof setNpuVisibility === "function") {
                setNpuVisibility(snapshotHardware.npuDetected);
            }
            if (snapshotHardware.npuDetected && snapshotHardware.npuName) {
                setText("spec-npu-name", "NPU " + snapshotHardware.npuName);
            }
        }

        window.addEventListener("load", function() {
            try {
                if (typeof dimGauges === "function") dimGauges();
                if (typeof updateClock === "function") updateClock();
                applySnapshotHardwareLabels();
                setTimeout(applySnapshotHardwareLabels, 250);
                document.body.classList.remove("system-critical");
            } catch (e) {}
        });
    </script>
</body>
'@ }
$html = $html -replace '</body>', $snapshotScript
$html = $html.Replace("__SNAPSHOT_HARDWARE__", $hardwareJson)

Set-Content -LiteralPath $tempHtml -Value $html -Encoding UTF8

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Force
}
if (Test-Path -LiteralPath $rawOutput) {
    Remove-Item -LiteralPath $rawOutput -Force
}

$uri = (New-Object System.Uri($tempHtml)).AbsoluteUri
$args = @(
    "--headless=new",
    "--disable-gpu",
    "--hide-scrollbars",
    "--allow-file-access-from-files",
    "--user-data-dir=$userDataDir",
    "--window-size=$renderWidth,$renderHeight",
    "--virtual-time-budget=2000",
    "--screenshot=$rawOutput",
    $uri
)

$process = Start-Process -FilePath $edgePath -ArgumentList $args -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $rawOutput)) {
    throw "Snapshot generation failed with exit code $($process.ExitCode)."
}

Add-Type -AssemblyName System.Drawing
$sourceImage = [System.Drawing.Image]::FromFile($rawOutput)
try {
    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($sourceImage, 0, 0, $Width, $Height)
        } finally {
            $graphics.Dispose()
        }

        $bitmap.Save($resolvedOutput, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }
} finally {
    $sourceImage.Dispose()
}

Remove-Item -LiteralPath $tempHtml -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $rawOutput -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $userDataDir -Recurse -Force -ErrorAction SilentlyContinue

Get-Item -LiteralPath $resolvedOutput | Select-Object *, @{ Name = "RenderSize"; Expression = { "$renderWidth x $renderHeight" } }, @{ Name = "TaskbarBottomPixels"; Expression = { $taskbarBottomPixels } }
