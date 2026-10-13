// Control Cockpit Frontend Client - SOTA Premium Edition v5
"use strict";
// Circular Jauge progress circumference (2 * PI * r) -> 2 * 3.14159 * 76 = 477.5
const RING_CIRCUMFERENCE = 477.5;

// DOM Telemetry Gauges Elements
const cpuRing = document.getElementById("cpu-ring");
const cpuVal = document.getElementById("cpu-val");
const vramRing = document.getElementById("vram-ring");
const vramVal = document.getElementById("vram-val");
const npuRing = document.getElementById("npu-ring");
const npuVal = document.getElementById("npu-val");
const gpuRing = document.getElementById("gpu-ring");
const gpuVal = document.getElementById("gpu-val");

// ARIA gauge containers bindings (A11y Progressbars)
const cpuGaugeContainer = document.getElementById("cpu-gauge-container");
const vramGaugeContainer = document.getElementById("vram-gauge-container");
const npuGaugeContainer = document.getElementById("npu-gauge-container");
const gpuGaugeContainer = document.getElementById("gpu-gauge-container");

// Overload Card Elements
const cpuCard = document.getElementById("cpu-card");
const gpuCard = document.getElementById("gpu-card");
const npuCard = document.getElementById("npu-card");
const acceleratorRow = document.querySelector(".accelerator-row");
const ramCard = document.getElementById("ram-card");
const vramCard = document.getElementById("vram-card");
const vramTotalSub = document.getElementById("vram-total-sub");
const igpuDecodeSub = document.getElementById("igpu-decode-sub");

// Sub-metrics DOM bindings
const cpuTempSub = document.getElementById("cpu-temp-sub");
const cpuTopProcSub = document.getElementById("cpu-top-proc-sub");
const cpuFreqSub = document.getElementById("cpu-freq-sub");
const cpuProcThreadsSub = document.getElementById("cpu-proc-threads-sub");

const gpuTempSub = document.getElementById("gpu-temp-sub");
const gpuVramSub = document.getElementById("gpu-vram-sub");
const gpuCoreSub = document.getElementById("gpu-core-sub");
const gpuMemClockSub = document.getElementById("gpu-mem-clock-sub");

// Right Column DOM Elements
const ramVal = document.getElementById("ram-val");
const ramRing = document.getElementById("ram-ring");
const ramGaugeContainer = document.getElementById("ram-gauge-container");
const ramFreeSub = document.getElementById("ram-free-sub");
const ramCachedSub = document.getElementById("ram-cached-sub");
const ramTopProcSub = document.getElementById("ram-top-proc-sub");
const ramStatusSub = document.getElementById("ram-status-sub");

const ssdVal = document.getElementById("ssd-val");
const ssdRing = document.getElementById("ssd-ring");
const ssdGaugeContainer = document.getElementById("ssd-gauge-container");
const ssdFreeSub = document.getElementById("ssd-free-sub");
const ssdReadSub = document.getElementById("ssd-read-sub");
const ssdWriteSub = document.getElementById("ssd-write-sub");
const ssdResponseSub = document.getElementById("ssd-response-sub");

const netVal = document.getElementById("net-val");
const netRing = document.getElementById("net-ring");
const netGaugeContainer = document.getElementById("net-gauge-container");
const netWifiSub = document.getElementById("net-wifi-sub");
const netLanSub = document.getElementById("net-lan-sub");
const netTypeSub = document.getElementById("net-type-sub");
const netPingSub = document.getElementById("net-ping-sub");

const specCpuName = document.getElementById("spec-cpu-name");
const specNpuName = document.getElementById("spec-npu-name");
const specGpuName = document.getElementById("spec-gpu-name");
const specRamName = document.getElementById("spec-ram-name");
const specNetMode = document.getElementById("spec-net-mode");
const specVramName = document.getElementById("spec-vram-name");
const specSsdName = document.getElementById("spec-ssd-name");
const vramUsedSub = document.getElementById("vram-used-sub");
const vramFreeSub = document.getElementById("vram-free-sub");
const igpuVramBar = document.getElementById("igpu-vram-bar");
const npuUsedSub = document.getElementById("npu-used-sub");
const npuSharedSub = document.getElementById("npu-shared-sub");
const npuFreeSub = document.getElementById("npu-free-sub");
const npuTotalSub = document.getElementById("npu-total-sub");
const npuUtilBar = document.getElementById("npu-util-bar");
const gpuVramBar = document.getElementById("gpu-vram-bar");
const clockMb = document.getElementById("clock-mb");
const clockBattery = document.getElementById("clock-battery");

// Remote Control Container
const powerPlansContainer = document.getElementById("power-plans-container");

// Local state
let currentPowerPlan = "";
const loadAlerts = new Map();
let lastTelemetry = null;
let lastTelemetryReceived = 0;
let telemetryExpired = false;
let isPowerPlanSwitching = false;
let pendingPowerPlanGuid = "";
let powerPlanSwitchTimer = 0;
let lastRemoteBoundsMsg = "";
let runtimeSuspended = false;
let hostRuntimeSuspended = false;
const pausedTransitions = new Set();
let resumeSnapTimer = 0;
let resumeGeneration = 0;
const lastPacketNodeTime = {};
const lastTelemetryNodeValues = {};
const MAX_DATA_PACKETS = 72;

// --- 🕰️ CYBER-CLOCK WIDGET ENGINE ---
const t = NexusI18n.t;
const GB = NexusI18n.unit("GB"), MB = NexusI18n.unit("MB"), KB = NexusI18n.unit("KB");
NexusI18n.applyStatic();

function updateClock() {
    if (lastTelemetry) refreshTelemetryFreshness();
    const now = new Date();
    const h = String(now.getHours()).padStart(2, '0');
    const m = String(now.getMinutes()).padStart(2, '0');
    const s = String(now.getSeconds()).padStart(2, '0');
    
    document.getElementById("clock-h").textContent = h;
    document.getElementById("clock-m").textContent = m;
    document.getElementById("clock-s").textContent = s;
    
    const dateText = NexusI18n.formatDate(now);
    const dateElement = document.getElementById("clock-date");
    if (dateElement.textContent !== dateText) dateElement.textContent = dateText;
}
updateClock();
let clockTimer = setInterval(updateClock, 1000);

// --- 🌌 SOTA CANVAS 2D PHYSICS ENGINE (HOLOGRAPHIC NEURAL TELEMETRY MAP) ---
const canvas = document.getElementById("physics-canvas");
const ctx = canvas.getContext("2d");
let width, height;
let bgCanvas = null;
let telemetryMapReady = false;
let telemetryNodesInitialized = false;
let canvasHasRendered = false;
const TELEMETRY_ORBIT_SCALE = 0.38;
const TELEMETRY_ORBIT_MAX = 320;
const TELEMETRY_ORBIT_RING_INDEX = 3;
const TELEMETRY_RADAR_RING_COUNT = 5;
// Start at the top with Network, then distribute visible nodes clockwise.
// Keeping this anchor first preserves vertical symmetry for every node count.
const TELEMETRY_ORBIT_ORDER = ["net", "ram", "ssd", "gpu", "igpu", "npuAccel", "cpu"];
const TELEMETRY_ORBIT_START_ANGLE = -90;

function resizeCanvas() {
    const rect = canvas.parentElement.getBoundingClientRect();
    if (!rect.width || !rect.height) return false;
    // Keep the complete diagram (nodes, orbit and captions) in a shared logical
    // coordinate space. A small panel scales that diagram uniformly instead of
    // forcing the old minimum orbit outside the visible canvas.
    const minimumSize = getTelemetryMinimumSize();
    const scale = Math.min(1, rect.width / minimumSize, rect.height / minimumSize);
    const nextWidth = Math.round(rect.width / scale);
    const nextHeight = Math.round(rect.height / scale);
    if (width === nextWidth && height === nextHeight) return false;
    width = canvas.width = nextWidth;
    height = canvas.height = nextHeight;
    bgCanvas = null; // force pre-rendered background to regenerate on next draw
    canvasHasRendered = false;
    return true;
}
window.addEventListener("resize", () => {
    if (!resizeCanvas()) return;
    syncTelemetryLayoutAfterResize(true);
    redrawCanvas();
});

// SOTA ResizeObserver to guarantee perfect circular aspect ratio under any layout dynamic shifts
if (canvas.parentElement && typeof ResizeObserver !== 'undefined') {
    const resizeObserver = new ResizeObserver(() => {
        if (!resizeCanvas()) return;
        syncTelemetryLayoutAfterResize(true);
        redrawCanvas();
    });
    resizeObserver.observe(canvas.parentElement);
}

// SOTA Visibility Change handler to completely sleep the CPU/iGPU when wallpaper hidden
document.addEventListener("visibilitychange", () => {
    setRuntimeSuspended(hostRuntimeSuspended || document.hidden);
});

// Center logo: the brand logo by default, or the user's own image sent by the host settings.
const DEFAULT_LOGO = "julienpiron.png";
const logoImg = new Image();
let logoLoaded = false;
logoImg.onload = () => {
    logoLoaded = true;
    redrawCanvas();
};
logoImg.onerror = () => {
    // An unreadable custom image falls back to the brand logo.
    if (logoImg.src.startsWith("data:")) setCenterLogo(null);
};
function setCenterLogo(source) {
    const next = typeof source === "string" && source.startsWith("data:image/") ? source : DEFAULT_LOGO;
    if (logoImg.getAttribute("src") === next) return;
    logoLoaded = false;
    logoImg.src = next;
}
setCenterLogo(null);

// Interactive state
let mouse = { x: null, y: null, isDown: false, grabbedNode: null };

// Hardware Telemetry Nodes Configuration
const telemetryNodes = {
    // Central Core (represented by npu key internally)
    npu: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 50,
        color: "#30d158",
        colorEnd: "#0a84ff",
        glow: "#30d158",
        label: "SYSTEM CORE",
        subLabel: "CORE ACTIVE",
        value: 0,
        suffix: "",
        rotAngle: 0
    },
    // Surrounding nodes
    cpu: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#bf5aff",
        colorEnd: "#8a2be2",
        glow: "#bf5aff",
        label: "CPU",
        subLabel: "-- °C",
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: t("node.cpuLoad")
    },
    igpu: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#30d158",
        colorEnd: "#1db954",
        glow: "#30d158",
        label: "iGPU",
        subLabel: `-- ${GB}`,
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: t("node.igpuLoad")
    },
    npuAccel: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#00c7be",
        colorEnd: "#64d2ff",
        glow: "#00c7be",
        label: "NPU",
        subLabel: `-- ${GB}`,
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: t("node.npuLoad"),
        visible: false
    },
    ram: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#0a84ff",
        colorEnd: "#00c7be",
        glow: "#0a84ff",
        label: "RAM",
        subLabel: `-- ${GB}`,
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: t("node.ramActivity")
    },
    gpu: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#ff375f",
        colorEnd: "#ff2d55",
        glow: "#ff375f",
        label: "GPU",
        subLabel: "-- °C",
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: t("node.gpuLoad")
    },
    ssd: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#00c7be",
        colorEnd: "#64d2ff",
        glow: "#00c7be",
        label: "SSD C:",
        subLabel: `-- ${GB}`,
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: t("node.diskActivity")
    },
    net: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#ff9f0a",
        colorEnd: "#ffd60a",
        glow: "#ff9f0a",
        label: t("label.network"),
        subLabel: `-- ${KB}/s`,
        value: 0,
        suffix: "",
        rotAngle: 0,
        desc: t("node.networkActivity")
    }
};

// Pre-allocated array of telemetry node objects to completely eliminate Object.keys() allocations inside loop
const telemetryNodeList = Object.values(telemetryNodes);

// SOTA Offscreen Cache for surround nodes to eliminate heavy main canvas draws
function renderNodeOffscreen(node) {
    if (node === telemetryNodes.npu) return; // npu size is dynamic (pulsing), keep it dynamically rendered
    
    const size = (node.radius + 12) * 2;
    const offscreen = document.createElement("canvas");
    offscreen.width = size;
    offscreen.height = size;
    const oCtx = offscreen.getContext("2d");
    const cx = size / 2;
    const cy = size / 2;
    
    // Draw Simulated Glow (concentric transparent circles)
    oCtx.fillStyle = node.glow;
    oCtx.globalAlpha = 0.12;
    oCtx.beginPath();
    oCtx.arc(cx, cy, node.radius + 8, 0, Math.PI * 2);
    oCtx.fill();
    oCtx.beginPath();
    oCtx.arc(cx, cy, node.radius + 4, 0, Math.PI * 2);
    oCtx.fill();
    oCtx.globalAlpha = 1.0;
    
    // Create static linear gradient
    const ringGrad = oCtx.createLinearGradient(cx - node.radius, cy - node.radius, cx + node.radius, cy + node.radius);
    ringGrad.addColorStop(0, node.color);
    ringGrad.addColorStop(1, node.colorEnd || node.color);
    
    // Node Glass Body
    oCtx.fillStyle = "rgba(4, 5, 9, 0.85)";
    oCtx.strokeStyle = ringGrad;
    oCtx.lineWidth = 2.2;
    oCtx.beginPath();
    oCtx.arc(cx, cy, node.radius, 0, Math.PI * 2);
    oCtx.fill();
    oCtx.stroke();
    
    // Outer orbiting track static ring
    oCtx.strokeStyle = ringGrad;
    oCtx.lineWidth = 1.0;
    oCtx.beginPath();
    oCtx.arc(cx, cy, node.radius + 7, 0, Math.PI * 2);
    oCtx.stroke();
    
    node.offscreenCanvas = offscreen;
    node.ringGrad = ringGrad; // Cache gradient
}

// Pre-render all surround nodes offscreen immediately
for (let i = 0; i < telemetryNodeList.length; i++) {
    renderNodeOffscreen(telemetryNodeList[i]);
}

function getTelemetryMinimumSize() {
    const nodes = TELEMETRY_ORBIT_ORDER.map(key => telemetryNodes[key]);
    // Measure the longest rendered caption, including its letter spacing.
    ctx.save();
    let extent = 0;
    for (const node of nodes) {
        ctx.font = "900 11px Bahnschrift, 'Segoe UI', sans-serif";
        ctx.letterSpacing = '1.5px';
        const labelWidth = ctx.measureText(node.label).width;
        ctx.font = "700 9px Bahnschrift, 'Segoe UI', sans-serif";
        ctx.letterSpacing = '1px';
        const descriptionWidth = ctx.measureText(node.desc || '').width;
        extent = Math.max(extent, labelWidth / 2, descriptionWidth / 2, node.radius + 38);
    }
    ctx.restore();
    const orbit = Math.max(telemetryNodes.npu.radius + extent, extent / Math.sin(Math.PI / nodes.length));
    return Math.ceil(2 * (orbit + extent));
}

function getTelemetryLayout() {
    const cx = width / 2;
    const cy = (height - 26) / 2; // Balance the captions below the orbit nodes.
    const maxAvailableRadius = Math.max(1, Math.min(cx, width - cx, cy, height - cy) - 12);
    const maxSafeNodeOrbit = Math.max(1, Math.min(cx - 70, width - cx - 70, cy - 40, height - cy - 66));
    const preferredOrbit = Math.min(Math.min(width, height) * TELEMETRY_ORBIT_SCALE, TELEMETRY_ORBIT_MAX);
    const orbitRadius = Math.min(preferredOrbit, maxSafeNodeOrbit);
    const radarStep = orbitRadius / TELEMETRY_ORBIT_RING_INDEX;
    const maxRadarRadius = Math.max(orbitRadius, Math.min(maxAvailableRadius, radarStep * TELEMETRY_RADAR_RING_COUNT));

    return { cx, cy, orbitRadius, radarStep, maxRadarRadius };
}

// Position targets setup
function updateNodeTargets() {
    const layout = getTelemetryLayout();
    
    // Core at Center
    telemetryNodes.npu.targetX = layout.cx;
    telemetryNodes.npu.targetY = layout.cy;
    
    // Surround nodes: one shared radius, visible nodes evenly distributed.
    const visibleKeys = TELEMETRY_ORBIT_ORDER.filter(key => telemetryNodes[key].visible !== false);
    const step = 360 / Math.max(1, visibleKeys.length);
    visibleKeys.forEach((key, index) => {
        const node = telemetryNodes[key];
        const angle = (TELEMETRY_ORBIT_START_ANGLE + index * step) * Math.PI / 180;
        node.targetX = layout.cx + Math.cos(angle) * layout.orbitRadius;
        node.targetY = layout.cy + Math.sin(angle) * layout.orbitRadius;
    });
}

// Initialize nodes at targets
function initNodes() {
    updateNodeTargets();
    Object.keys(telemetryNodes).forEach(key => {
        const node = telemetryNodes[key];
        node.x = node.targetX;
        node.y = node.targetY;
    });
    telemetryNodesInitialized = true;
}

function syncTelemetryLayoutAfterResize(snapToTargets = false) {
    if (!telemetryMapReady || !width || !height) return;

    updateNodeTargets();
    if (snapToTargets || telemetryNodesInitialized) {
        Object.keys(telemetryNodes).forEach(key => {
            const node = telemetryNodes[key];
            if (mouse.grabbedNode === node) return;
            node.x = node.targetX;
            node.y = node.targetY;
        });
        telemetryNodesInitialized = true;
    }
}

telemetryMapReady = true;
resizeCanvas();
syncTelemetryLayoutAfterResize(true);

// Neural flow data packets system
let dataPackets = [];
class DataPacket {
    constructor(sourceNode, color) {
        this.source = sourceNode;
        this.target = telemetryNodes.npu;
        this.x = sourceNode.x;
        this.y = sourceNode.y;
        this.progress = 0;
        this.speed = 0.008 + Math.random() * 0.012; // speed along path
        this.color = color;
        this.size = 1.5 + Math.random() * 2.5;
    }
    update() {
        this.progress += this.speed;
        if (this.progress > 1.0) this.progress = 1.0;
        
        // Path linear interpolation with spring curves
        const p = Math.max(0, this.progress);
        this.x = this.source.x + (this.target.x - this.source.x) * p;
        this.y = this.source.y + (this.target.y - this.source.y) * p;
    }
    draw() {
        if (this.progress < 0) return; // Don't draw staggered packets yet
        ctx.save();
        // Glow simulation: draw a larger, faint circle first (very fast!)
        ctx.fillStyle = this.color;
        ctx.globalAlpha = 0.25;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.size + 2.5, 0, Math.PI * 2);
        ctx.fill();
        
        // Main particle
        ctx.globalAlpha = 1.0;
        ctx.beginPath();
        ctx.arc(this.x, this.y, this.size, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
    }
}

function getPacketIntervalMs(pct) {
    const normalized = Math.max(1, Math.min(100, Number(pct) || 0));
    return Math.max(520, 2600 - normalized * 19);
}

function getPacketBurstCount(pct) {
    const normalized = Math.max(0, Math.min(100, Number(pct) || 0));
    if (normalized >= 85) return 4;
    if (normalized >= 55) return 3;
    if (normalized >= 25) return 2;
    return 1;
}

// Particle splash effects when packets reach NPU Core
let coreParticles = [];
function spawnCoreSplash(x, y, color) {
    const count = 4 + Math.floor(Math.random() * 4);
    for (let i = 0; i < count; i++) {
        const angle = Math.random() * Math.PI * 2;
        const speed = 0.8 + Math.random() * 2.5;
        coreParticles.push({
            x, y,
            vx: Math.cos(angle) * speed,
            vy: Math.sin(angle) * speed,
            color,
            size: 0.8 + Math.random() * 1.5,
            life: 15 + Math.random() * 15,
            maxLife: 30
        });
    }
}

// Mouse interaction listeners for node dragging
canvas.addEventListener("mousedown", (e) => {
    const rect = canvas.getBoundingClientRect();
    mouse.x = (e.clientX - rect.left) * width / rect.width;
    mouse.y = (e.clientY - rect.top) * height / rect.height;
    mouse.isDown = true;
    
    // Find closest node to grab (within 60px)
    let closestDist = 60;
    Object.keys(telemetryNodes).forEach(key => {
        const node = telemetryNodes[key];
        if (node.visible === false) return;
        const d = Math.hypot(node.x - mouse.x, node.y - mouse.y);
        if (d < closestDist) {
            closestDist = d;
            mouse.grabbedNode = node;
        }
    });
    
    wakeCanvas(); // Wake canvas on interaction!
});

canvas.addEventListener("mousemove", (e) => {
    const rect = canvas.getBoundingClientRect();
    mouse.x = (e.clientX - rect.left) * width / rect.width;
    mouse.y = (e.clientY - rect.top) * height / rect.height;
    
    if (mouse.isDown && mouse.grabbedNode) {
        mouse.grabbedNode.x = mouse.x;
        mouse.grabbedNode.y = mouse.y;
    }
    
    wakeCanvas(); // Wake canvas on mouse move!
});

window.addEventListener("mouseup", () => {
    mouse.isDown = false;
    mouse.grabbedNode = null;
});

canvas.addEventListener("mouseleave", () => {
    mouse.x = null;
    mouse.y = null;
    mouse.isDown = false;
    mouse.grabbedNode = null;
});

// Idle-Stop engine control variables
let isCanvasLoopRunning = false;
let needsRender = true;
let canvasFrameTimer = 0;
let canvasAnimationFrame = 0;

function renderStaticBackground() {
    if (!bgCanvas) {
        bgCanvas = document.createElement("canvas");
    }
    bgCanvas.width = width;
    bgCanvas.height = height;
    const bgCtx = bgCanvas.getContext("2d");
    
    bgCtx.strokeStyle = "rgba(255, 255, 255, 0.032)";
    bgCtx.lineWidth = 1;
    
    // Draw concentric radar lines around the same center used by the telemetry nodes.
    const { cx, cy, radarStep, maxRadarRadius } = getTelemetryLayout();
    for (let r = radarStep; r <= maxRadarRadius + 0.01; r += radarStep) {
        bgCtx.beginPath();
        bgCtx.arc(cx, cy, r, 0, Math.PI * 2);
        bgCtx.stroke();
    }
    
    // Draw crosshair grid lines
    bgCtx.beginPath();
    bgCtx.moveTo(cx - maxRadarRadius, cy);
    bgCtx.lineTo(cx + maxRadarRadius, cy);
    bgCtx.moveTo(cx, cy - maxRadarRadius);
    bgCtx.lineTo(cx, cy + maxRadarRadius);
    bgCtx.stroke();
}

function wakeCanvas() {
    if (runtimeSuspended) return;
    needsRender = true;
    if (!isCanvasLoopRunning) {
        isCanvasLoopRunning = true;
        scheduleCanvasFrame(0);
    }
}

function redrawCanvas() {
    // A real resize or loaded asset needs one paint even while paused.
    // Draw the existing scene without advancing particles or starting a loop.
    if (runtimeSuspended) updatePhysics(0, true);
    else wakeCanvas();
}

function scheduleCanvasFrame(delay = physicsFpsInterval) {
    if (runtimeSuspended || !isCanvasLoopRunning || canvasFrameTimer || canvasAnimationFrame) return;
    canvasFrameTimer = setTimeout(() => {
        canvasFrameTimer = 0;
        canvasAnimationFrame = requestAnimationFrame(timestamp => {
            canvasAnimationFrame = 0;
            updatePhysics(timestamp);
        });
    }, delay);
}

function setRuntimeSuspended(suspended) {
    document.body.classList.toggle("runtime-suspended", suspended);
    if (runtimeSuspended === suspended) return;
    runtimeSuspended = suspended;
    if (suspended) {
        endResumeSnap();
        loadAlerts.forEach(alert => alert.reset());
        pauseRunningTransitions();
        mouse.grabbedNode = null;
        mouse.isDown = false;
        isCanvasLoopRunning = false;
        needsRender = false;
        if (canvasFrameTimer) {
            clearTimeout(canvasFrameTimer);
            canvasFrameTimer = 0;
        }
        if (canvasAnimationFrame) {
            cancelAnimationFrame(canvasAnimationFrame);
            canvasAnimationFrame = 0;
        }
        clearInterval(clockTimer);
        clockTimer = 0;
        if (!canvasHasRendered) redrawCanvas();
    } else {
        pausedTransitions.forEach(animation => {
            if (animation.playState === "paused") animation.play();
        });
        pausedTransitions.clear();
        // Snap gauges to fresh values instead of animating from stale ones.
        resumeGeneration++;
        document.body.classList.add("runtime-resuming");
        clearTimeout(resumeSnapTimer);
        resumeSnapTimer = setTimeout(endResumeSnap, 3000);
        // The pause itself is not a telemetry outage: give the first fresh sample
        // the normal window before declaring the last reading expired.
        if (!telemetryExpired) lastTelemetryReceived = performance.now();
        updateClock();
        clockTimer = setInterval(updateClock, 1000);
        lastPhysicsTime = 0;
        needsRender = true;
        updatePhysics(0, true); // paint the scene now, not on the next timer/rAF
        wakeCanvas();
        try {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage("REQUEST_TELEMETRY");
            }
        } catch (e) {}
    }
}

function endResumeSnap() {
    clearTimeout(resumeSnapTimer);
    resumeSnapTimer = 0;
    document.body.classList.remove("runtime-resuming");
}

function pauseRunningTransitions() {
    // animation-play-state covers CSS animations, but not in-flight transitions.
    document.getAnimations().forEach(animation => {
        if (animation instanceof CSSTransition && animation.playState === "running") {
            animation.pause();
            pausedTransitions.add(animation);
        }
    });
}
document.addEventListener("transitionrun", () => {
    if (runtimeSuspended) pauseRunningTransitions();
});

// Main update physics and rendering loop
let lastPhysicsTime = 0;
const physicsFpsInterval = 1000 / 12; // active canvas cadence; sleeps fully when idle
function updatePhysics(timestamp, renderOnly = false) {
    if (!renderOnly && (!isCanvasLoopRunning || runtimeSuspended)) return;
    
    if (!timestamp) timestamp = performance.now();
    const elapsed = timestamp - lastPhysicsTime;
    if (!renderOnly && elapsed < physicsFpsInterval) {
        scheduleCanvasFrame(physicsFpsInterval - elapsed);
        return;
    }
    if (!renderOnly) lastPhysicsTime = timestamp - (elapsed % physicsFpsInterval);
    

    if (!telemetryNodesInitialized && width > 0) {
        initNodes();
    }
    
    ctx.clearRect(0, 0, width, height);
    
    // 1. Draw sci-fi background target radar/circles (Offscreen Pre-rendered!)
    if (!bgCanvas || bgCanvas.width !== width || bgCanvas.height !== height) {
        renderStaticBackground();
    }
    ctx.drawImage(bgCanvas, 0, 0);
    
    // 2. Update node physics (elastic spring return back to target coordinates)
    for (let i = 0; i < telemetryNodeList.length; i++) {
        const node = telemetryNodeList[i];
        if (node.visible === false) continue;
        
        // Keep resting nodes exactly on the geometric target grid.
        if (mouse.grabbedNode !== node) {
            node.x = node.targetX;
            node.y = node.targetY;
        }
        
        // Orbit ring rotation angle increment
        const baseRotSpeed = 0.008;
        const loadFactor = (node.value || 0) / 100;
        if (!renderOnly) node.rotAngle += baseRotSpeed + (loadFactor * 0.06);
    }
    
    // 3. Neural data flow data packets spawner
    // NOTE: Packets are now spawned dynamically in updateDOM on telemetry changes to support SOTA Idle-Stop sleep.
    
    // 4. Update and draw neural pathways (lines between surround nodes and NPU center)
    for (let i = 0; i < telemetryNodeList.length; i++) {
        const node = telemetryNodeList[i];
        if (node.visible === false) continue;
        if (node === telemetryNodes.npu) continue;
        const npu = telemetryNodes.npu;
        
        // Optimize: Cache pathways linear gradients and update only when nodes move!
        if (!node.pathBgGrad || node.lastGradX !== node.x || node.lastGradY !== node.y || node.lastNpuX !== npu.x || node.lastNpuY !== npu.y) {
            node.lastGradX = node.x;
            node.lastGradY = node.y;
            node.lastNpuX = npu.x;
            node.lastNpuY = npu.y;
            
            node.pathBgGrad = ctx.createLinearGradient(node.x, node.y, npu.x, npu.y);
            node.pathBgGrad.addColorStop(0, node.color + "18"); // ~9% opacity start
            node.pathBgGrad.addColorStop(0.5, (node.colorEnd || node.color) + "0c"); // ~5% opacity mid
            node.pathBgGrad.addColorStop(1, npu.color + "02"); // ~1% opacity end NPU
            
            node.pathCoreGrad = ctx.createLinearGradient(node.x, node.y, npu.x, npu.y);
            node.pathCoreGrad.addColorStop(0, node.color + "44"); // ~27% opacity
            node.pathCoreGrad.addColorStop(0.5, (node.colorEnd || node.color) + "22"); // ~13% opacity
            node.pathCoreGrad.addColorStop(1, npu.color + "05"); // ~2% opacity
        }
        
        // 1. Draw glowing background connection line (no shadowBlur!)
        ctx.beginPath();
        ctx.moveTo(node.x, node.y);
        ctx.lineTo(npu.x, npu.y);
        ctx.strokeStyle = node.pathBgGrad;
        ctx.lineWidth = 3.6;
        ctx.stroke();
        
        // 2. Draw core gradient line
        ctx.beginPath();
        ctx.moveTo(node.x, node.y);
        ctx.lineTo(npu.x, npu.y);
        ctx.strokeStyle = node.pathCoreGrad;
        ctx.lineWidth = 1.8;
        ctx.stroke();
    }
    
    // 5. Update and draw data packet signals traveling
    for (let i = dataPackets.length - 1; i >= 0; i--) {
        const p = dataPackets[i];
        if (!renderOnly) p.update();
        p.draw();
        
        // When packet reaches center NPU node
        if (!renderOnly && p.progress >= 1.0) {
            spawnCoreSplash(p.target.x, p.target.y, p.color);
            dataPackets.splice(i, 1);
            
            // Pulse NPU core
            telemetryNodes.npu.radius = 56;
        }
    }
    
    // Decay NPU core radius back to base size
    if (!renderOnly) telemetryNodes.npu.radius = Math.max(50, telemetryNodes.npu.radius - 0.25);
    
    // 6. Draw component nodes
    for (let i = 0; i < telemetryNodeList.length; i++) {
        const node = telemetryNodeList[i];
        if (node.visible === false) continue;
        
        ctx.save();
        ctx.translate(node.x, node.y);
        
        if (node.offscreenCanvas) {
            // Draw SOTA Pre-rendered static body (Fast iGPU draw!)
            ctx.drawImage(node.offscreenCanvas, -(node.radius + 12), -(node.radius + 12));
        } else {
            // Fallback / Dynamic NPU rendering
            // Draw simulated glow using concentric transparent circles (highly optimized, 0% GPU load!)
            ctx.fillStyle = node.glow;
            ctx.globalAlpha = 0.12;
            ctx.beginPath();
            ctx.arc(0, 0, node.radius + 8, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.arc(0, 0, node.radius + 4, 0, Math.PI * 2);
            ctx.fill();
            ctx.globalAlpha = 1.0; // reset
            
            // Create local linear gradient matching card's start/end accents (Cached static gradient!)
            const baseRadius = node === telemetryNodes.npu ? 50 : 26;
            if (!node.ringGrad) {
                node.ringGrad = ctx.createLinearGradient(-baseRadius, -baseRadius, baseRadius, baseRadius);
                node.ringGrad.addColorStop(0, node.color);
                node.ringGrad.addColorStop(1, node.colorEnd || node.color);
            }
            const ringGrad = node.ringGrad;

            // Node glass body
            ctx.fillStyle = "rgba(4, 5, 9, 0.85)";
            ctx.strokeStyle = ringGrad;
            ctx.lineWidth = 2.2;
            ctx.beginPath();
            ctx.arc(0, 0, node.radius, 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
            
            // Draw static outer orbit track
            ctx.shadowBlur = 0; // reset
            ctx.strokeStyle = ringGrad;
            ctx.lineWidth = 1.0;
            ctx.beginPath();
            ctx.arc(0, 0, node.radius + 7, 0, Math.PI * 2);
            ctx.stroke();
        }
        
        // Static NPU Core logo draw on top of dynamic NPU body (or overlayed if logoLoaded)
        if (node === telemetryNodes.npu && logoLoaded) {
            const logoRadius = node.radius - 5;
            const logoDiameter = logoRadius * 2;
            ctx.save();
            ctx.beginPath();
            ctx.arc(0, 0, logoRadius, 0, Math.PI * 2);
            ctx.clip();
            ctx.drawImage(
                logoImg,
                -logoDiameter / 2,
                -logoDiameter / 2,
                logoDiameter,
                logoDiameter
            );
            ctx.restore();
        }
        
        // Rotating tick orbits (always dynamic on top)
        const baseRadius = node === telemetryNodes.npu ? 50 : 26;
        const ringGrad = node.ringGrad || (node.ringGrad = ctx.createLinearGradient(-baseRadius, -baseRadius, baseRadius, baseRadius));
        
        ctx.rotate(node.rotAngle);
        ctx.strokeStyle = ringGrad;
        ctx.lineWidth = 2.5;
        ctx.beginPath();
        ctx.arc(0, 0, node.radius + 7, 0, Math.PI / 4);
        ctx.stroke();
        ctx.beginPath();
        ctx.arc(0, 0, node.radius + 7, Math.PI, Math.PI + Math.PI / 4);
        ctx.stroke();
        
        ctx.restore();
        
        // Text overlays (Holographic titles)
        ctx.save();
        ctx.textAlign = "center";
        ctx.textBaseline = "middle";
        
        // Draw Value text inside node
        if (node !== telemetryNodes.npu) {
            ctx.font = "bold 13px Bahnschrift, 'Segoe UI', sans-serif";
            ctx.fillStyle = "#ffffff";
            ctx.fillText(NexusTelemetry.valid(node.value) ? `${node.value}${node.suffix}` : '—', node.x, node.y - 3);
            
            // Draw Label below node
            ctx.font = "900 11px Bahnschrift, 'Segoe UI', sans-serif";
            ctx.fillStyle = "rgba(255, 255, 255, 0.45)";
            ctx.letterSpacing = "1.5px";
            ctx.fillText(node.label, node.x, node.y + node.radius + 20);
            
            // Draw percentage description below Label
            if (node.desc) {
                ctx.font = "700 9px Bahnschrift, 'Segoe UI', sans-serif";
                ctx.fillStyle = "rgba(255, 255, 255, 0.35)";
                ctx.letterSpacing = "1px";
                ctx.fillText(node.desc, node.x, node.y + node.radius + 32);
            }
        }
        ctx.restore();
    }
    
    // 7. Update and draw NPU core splash sparkles
    for (let i = coreParticles.length - 1; i >= 0; i--) {
        const p = coreParticles[i];
        if (!renderOnly) {
            p.x += p.vx;
            p.y += p.vy;
            p.vx *= 0.95;
            p.vy *= 0.95;
            p.life--;
        }
        
        ctx.save();
        const alpha = Math.max(0, p.life / p.maxLife);
        ctx.fillStyle = p.color;
        
        // Draw simulated glow: larger circle first (extremely fast!)
        ctx.globalAlpha = alpha * 0.3;
        ctx.beginPath();
        ctx.arc(p.x, p.y, p.size + 1.8, 0, Math.PI * 2);
        ctx.fill();
        
        // Main particle
        ctx.globalAlpha = alpha;
        ctx.beginPath();
        ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
        ctx.fill();
        ctx.restore();
        
        if (!renderOnly && p.life <= 0) {
            coreParticles.splice(i, 1);
        }
    }

    canvasHasRendered = true;
    if (renderOnly) return;

    // 8. Idle-Stop Canvas Engine sleep check
    let nodesAreMoving = false;
    for (let i = 0; i < telemetryNodeList.length; i++) {
        const node = telemetryNodeList[i];
        const dx = node.targetX - node.x;
        const dy = node.targetY - node.y;
        if (Math.abs(dx) > 0.15 || Math.abs(dy) > 0.15) {
            nodesAreMoving = true;
        }
    }

    const hasActivity = dataPackets.length > 0 || coreParticles.length > 0 || mouse.grabbedNode !== null || nodesAreMoving;

    if (!hasActivity && !needsRender) {
        isCanvasLoopRunning = false;
    } else {
        scheduleCanvasFrame();
    }
    
    needsRender = false; // reset
}

// --- Native WebView2 telemetry rendering ---
function setHtmlIfChanged(element, value) {
    if (!element) return;
    if (element.innerHTML !== value) {
        element.innerHTML = value;
    }
}

function setAttrIfChanged(element, name, value) {
    if (!element) return;
    if (name === 'aria-valuenow' && !NexusTelemetry.valid(value)) {
        element.removeAttribute(name);
        element.setAttribute('aria-valuetext', t('aria.unavailable'));
        return;
    }
    if (name === 'aria-valuenow') element.removeAttribute('aria-valuetext');
    const textValue = String(value);
    if (element.getAttribute(name) !== textValue) {
        element.setAttribute(name, textValue);
    }
}

// Swaps a sub-metric slot (label + value) so a missing sensor can be replaced by another real metric.
function setSubMetric(valEl, label, value) {
    if (!valEl) return;
    const lblEl = valEl.parentElement ? valEl.parentElement.querySelector(".lbl") : null;
    if (lblEl && lblEl.textContent !== label) {
        lblEl.textContent = label;
    }
    if (valEl.textContent !== value) {
        valEl.textContent = value;
    }
}

function setWidthIfChanged(element, value) {
    if (!element) return;
    const widthValue = `${NexusTelemetry.valid(value) ? Math.min(100, value) : 0}%`;
    if (element.style.width !== widthValue) {
        element.style.width = widthValue;
    }
}

function setCircularProgress(ring, value) {
    const offset = RING_CIRCUMFERENCE - ((NexusTelemetry.valid(value) ? Math.min(100, value) : 0) / 100) * RING_CIRCUMFERENCE;
    const offsetValue = offset.toFixed(3);
    if (ring && ring._nexusOffset !== offsetValue) {
        ring._nexusOffset = offsetValue;
        ring.style.strokeDashoffset = offsetValue;
    }
}

function formatNetSpeed(kbVal) {
    if (kbVal === 0) return `0.0 ${KB}/s`;
    if (kbVal < 1024) return `${kbVal.toFixed(1)} ${KB}/s`;
    return `${(kbVal / 1024).toFixed(1)} ${MB}/s`;
}

function getNetworkLinkSpeedKb(stats) {
    const fallbackMbps = ((stats.network.type || "").toLowerCase().includes("ether")) ? 2500 : 500;
    const linkSpeedMbps = Number(stats.network.linkSpeedMbps) || fallbackMbps;
    return Math.max(1024, Math.round((linkSpeedMbps * 1000000) / 8 / 1024));
}

function setNpuVisibility(hasNpu) {
    if (acceleratorRow) {
        acceleratorRow.classList.toggle("no-npu", !hasNpu);
        acceleratorRow.classList.toggle("has-npu", hasNpu);
    }

    if (npuCard) {
        npuCard.setAttribute("aria-hidden", hasNpu ? "false" : "true");
    }

    if (telemetryNodes.npuAccel) {
        const changed = telemetryNodes.npuAccel.visible !== hasNpu;
        telemetryNodes.npuAccel.visible = hasNpu;
        if (hasNpu && !telemetryNodes.npuAccel.offscreenCanvas) {
            renderNodeOffscreen(telemetryNodes.npuAccel);
        }
        if (changed) {
            syncTelemetryLayoutAfterResize(true);
            needsRender = true;
        }
    }
}

// Hides the iGPU / dGPU cards and orbit nodes on machines that lack the hardware.
function setGpuVisibility(hasIgpu, hasDgpu) {
    if (vramCard) {
        vramCard.classList.toggle("card-hidden", !hasIgpu);
        vramCard.setAttribute("aria-hidden", hasIgpu ? "false" : "true");
    }
    if (gpuCard) {
        gpuCard.classList.toggle("card-hidden", !hasDgpu);
        gpuCard.setAttribute("aria-hidden", hasDgpu ? "false" : "true");
    }
    if (acceleratorRow) {
        acceleratorRow.classList.toggle("no-igpu", !hasIgpu);
    }

    let changed = false;
    if (telemetryNodes.igpu.visible !== hasIgpu) {
        telemetryNodes.igpu.visible = hasIgpu;
        changed = true;
    }
    if (telemetryNodes.gpu.visible !== hasDgpu) {
        telemetryNodes.gpu.visible = hasDgpu;
        changed = true;
    }
    if (changed) {
        syncTelemetryLayoutAfterResize(true);
        needsRender = true;
    }
}

function updateDOM(stats) {
    lastTelemetry = stats;
    lastTelemetryReceived = performance.now();
    telemetryExpired = false;
    const npuStats = stats.npu || {
        utilization: 0,
        name: "",
        usedMb: 0,
        totalMb: 0,
        totalGb: 0,
        detected: false
    };
    const hasNpu = npuStats.detected === true;
    setNpuVisibility(hasNpu);
    // Older payloads do not carry the detected flags: keep everything visible then.
    setGpuVisibility(stats.igpu.detected !== false, stats.gpu.detected !== false);

    // 1. Update circular telemetry gauges (Left Column)
    setCircularProgress(cpuRing, stats.cpu.utilization);
    animateTextValue(cpuVal, stats.cpu.utilization, "%");
    setAttrIfChanged(cpuGaugeContainer, "aria-valuenow", stats.cpu.utilization);

    setCircularProgress(vramRing, stats.igpu.utilization);
    animateTextValue(vramVal, stats.igpu.utilization, "%");
    setAttrIfChanged(vramGaugeContainer, "aria-valuenow", stats.igpu.utilization);

    setCircularProgress(npuRing, npuStats.utilization);
    animateTextValue(npuVal, npuStats.utilization, "%");
    setAttrIfChanged(npuGaugeContainer, "aria-valuenow", npuStats.utilization);

    setCircularProgress(gpuRing, stats.gpu.utilization);
    animateTextValue(gpuVal, stats.gpu.utilization, "%");
    setAttrIfChanged(gpuGaugeContainer, "aria-valuenow", stats.gpu.utilization);

    // 2. CPU Sub-metrics (real sensor when present, otherwise another real metric fills the slot)
    if (NexusTelemetry.sensorState(stats, 'cpuTemperature') === 'stale' || stats.cpu.temp >= 0) {
        setSensorMetric(cpuTempSub, t('label.thermalZone'), stats.cpu.temp, '°C', stats, 'cpuTemperature');
    } else {
        setSubMetric(cpuTempSub, t("label.cacheL2L3"), `${stats.cpu.l2Cache} / ${stats.cpu.l3Cache}`);
        cpuTempSub.classList.remove('sensor-unavailable');
        cpuTempSub.title = '';
    }
    if (cpuFreqSub) {
        cpuFreqSub.textContent = `${stats.cpu.freqGhz} GHz`;
    }
    if (cpuProcThreadsSub) {
        cpuProcThreadsSub.textContent = `${stats.totalProcesses} / ${stats.cpu.threads}`;
    }
    if (cpuTopProcSub) {
        const top = (stats.topProcesses && stats.topProcesses.length > 0) ? stats.topProcesses[0] : null;
        if (top && top.Name) {
            const logical = Math.max(1, Number(stats.cpu.logical) || 1);
            const pct = Math.min(99, Math.round((Number(top.PercentProcessorTime) || 0) / logical));
            const shortName = top.Name.length > 14 ? top.Name.slice(0, 13) + "…" : top.Name;
            cpuTopProcSub.textContent = `${shortName} ${pct}%`;
        } else {
            cpuTopProcSub.textContent = "--";
        }
    }

    // 3. GPU Sub-metrics (NVML when available, otherwise real Windows counters fill the slots)
    const dgpuVramUsedGb = NexusTelemetry.valid(stats.vram.usedMb) ? (stats.vram.usedMb / 1024).toFixed(1) : '—';
    const dgpuVramTotalGb = stats.vram.totalMb > 0 ? (stats.vram.totalMb / 1024).toFixed(0) : "";
    setSensorMetric(gpuTempSub, t("label.temperature"), stats.gpu.temp, "°C", stats, "nvidia");
    setSubMetric(gpuVramSub, "VRAM", dgpuVramTotalGb ? `${dgpuVramUsedGb} / ${dgpuVramTotalGb} ${GB}` : `${dgpuVramUsedGb} ${GB}`);
    setSensorMetric(gpuCoreSub, t("label.gpuPower"), stats.gpu.powerW, "W", stats, "nvidia");
    setSensorMetric(gpuMemClockSub, t("label.memoryClock"), stats.gpu.memoryClock, "MHz", stats, "nvidia");
    if (gpuVramBar) {
        const gpuVramPct = stats.vram.totalMb > 0 ? Math.round((stats.vram.usedMb / stats.vram.totalMb) * 100) : 0;
        setWidthIfChanged(gpuVramBar, gpuVramPct);
    }

    // 4. Circular telemetry gauges (Right Column - RAM, SSD, Network)
    // RAM circular gauge
    setCircularProgress(ramRing, stats.ram.utilization);
    animateTextValue(ramVal, stats.ram.utilization, "%");
    setAttrIfChanged(ramGaugeContainer, "aria-valuenow", stats.ram.utilization);
    
    const totalRamGb = stats.ram.totalGb || 0;
    const freeRamGb = ((1 - stats.ram.utilization / 100) * totalRamGb).toFixed(1);
    const usedRamGb = ((stats.ram.utilization / 100) * totalRamGb).toFixed(1);
    ramFreeSub.textContent = `${freeRamGb} ${GB}`;
    if (ramCachedSub) {
        ramCachedSub.textContent = `${stats.ram.cachedGb} ${GB}`;
    }
    if (ramTopProcSub) {
        const topRam = stats.topRamProcess;
        if (topRam && topRam.name) {
            const ramMb = Number(topRam.ramMb) || 0;
            const ramText = ramMb >= 1024 ? `${(ramMb / 1024).toFixed(1)} ${GB}` : `${ramMb} ${MB}`;
            const shortRamName = topRam.name.length > 12 ? topRam.name.slice(0, 11) + "…" : topRam.name;
            ramTopProcSub.textContent = `${shortRamName} ${ramText}`;
        } else {
            ramTopProcSub.textContent = "--";
        }
    }
    ramStatusSub.textContent = `${stats.ram.commitUsedGb} ${GB} / ${stats.ram.commitLimitGb} ${GB}`;
    ramStatusSub.className = "val text-blue";

    // SSD card gauge: disk capacity occupation. The center map node shows live disk activity.
    setCircularProgress(ssdRing, stats.disk.storagePercent);
    animateTextValue(ssdVal, stats.disk.storagePercent, "%");
    setAttrIfChanged(ssdGaugeContainer, "aria-valuenow", stats.disk.storagePercent);
    ssdFreeSub.textContent = `${stats.disk.freeGb} ${GB} / ${stats.disk.totalGb} ${GB}`;
    if (ssdReadSub && ssdWriteSub) {
        // Formatter with dynamic unit support
        const formatSpeed = (mbStr) => {
            const val = parseFloat(mbStr);
            if (val >= 1000) return `${(val / 1024).toFixed(1)} ${GB}/s`;
            return `${val.toFixed(1)} ${MB}/s`;
        };
        ssdReadSub.textContent = formatSpeed(stats.disk.readMb);
        ssdWriteSub.textContent = formatSpeed(stats.disk.writeMb);
    }
    if (ssdResponseSub) {
        ssdResponseSub.textContent = `${stats.disk.responseTimeMs.toFixed(1)} ms`;
    }

    // Network circular gauge & absolute speed rendering
    const totalNetKb = stats.network.lan + stats.network.wifi;
    
    // Dynamic value display
    if (totalNetKb === 0) {
        setHtmlIfChanged(netVal, `0<span>${KB}/s</span>`);
    } else if (totalNetKb < 1024) {
        setHtmlIfChanged(netVal, `${totalNetKb}<span>${KB}/s</span>`);
    } else {
        setHtmlIfChanged(netVal, `${(totalNetKb / 1024).toFixed(1)}<span>${MB}/s</span>`);
    }
    
    // Scale the card ring against the real link speed (2.5 Gb/s Ethernet is about 305000 KiB/s).
    const linkSpeedKb = getNetworkLinkSpeedKb(stats);
    const netPercent = totalNetKb > 0 ? Math.min(100, Math.max(1, Math.round((totalNetKb / linkSpeedKb) * 100))) : 0;
    setCircularProgress(netRing, netPercent);
    setAttrIfChanged(netGaugeContainer, "aria-valuenow", netPercent);

    // Dynamic formatting for Send / Receive speeds

    netWifiSub.textContent = formatNetSpeed(stats.network.wifi); // RECEVOIR
    netLanSub.textContent = formatNetSpeed(stats.network.lan);   // ENVOYER
    if (netTypeSub) {
        const wifiSignal = Number(stats.network.signal);
        if (stats.network.type === "Wi-Fi" && wifiSignal >= 0) {
            setSubMetric(netTypeSub, t("label.wifiSignal"), `${wifiSignal} %`);
        } else {
            setSubMetric(netTypeSub, t("label.connectionType"), stats.network.type.toUpperCase());
        }
    }
    if (netPingSub) {
        netPingSub.textContent = stats.ping >= 0
            ? `${stats.ping} ms${stats.pingVia === "gateway" ? ` (${t("ping.router")})` : ""}` : "-- ms";
        netPingSub.title = stats.pingVia === "gateway" && stats.ping >= 0
            ? t("ping.routerTitle") : "";
    }

    // 5. Update iGPU VRAM Sub-metrics
    const usedVramGb = NexusTelemetry.valid(stats.igpu.usedMb) ? (stats.igpu.usedMb / 1024).toFixed(1) : '—';
    const freeVramGb = NexusTelemetry.valid(stats.igpu.usedMb) ? (Math.max(0, stats.igpu.totalMb - stats.igpu.usedMb) / 1024).toFixed(1) : '—';
    const totalVramGb = (stats.igpu.totalMb / 1024).toFixed(0);
    vramUsedSub.textContent = `${usedVramGb} ${GB}`;
    vramFreeSub.textContent = `${freeVramGb} ${GB}`;
    if (vramTotalSub) {
        vramTotalSub.textContent = `${totalVramGb} ${GB}`;
    }
    if (igpuDecodeSub) {
        const decodePct = NexusTelemetry.valid(stats.igpu.decodeUtil) ? Math.min(100, stats.igpu.decodeUtil) : '—';
        igpuDecodeSub.textContent = `${decodePct} %`;
    }
    if (igpuVramBar) {
        const igpuVramPct = Math.round((stats.igpu.usedMb / stats.igpu.totalMb) * 100) || 0;
        setWidthIfChanged(igpuVramBar, igpuVramPct);
    }
    const npuUsedGb = NexusTelemetry.valid(npuStats.usedMb) ? (npuStats.usedMb / 1024).toFixed(1) : '—';
    const npuTotalMb = Number(npuStats.totalMb) || 0;
    const npuTotalGb = npuTotalMb > 0 ? (npuTotalMb / 1024).toFixed(0) : "0";
    const npuFreeGb = npuTotalMb > 0 && NexusTelemetry.valid(npuStats.usedMb) ? (Math.max(0, npuTotalMb - npuStats.usedMb) / 1024).toFixed(1) : '—';
    if (npuUsedSub) {
        npuUsedSub.textContent = `${npuUsedGb} ${GB}`;
    }
    if (npuSharedSub) {
        npuSharedSub.textContent = `${npuUsedGb} ${GB}`;
    }
    if (npuFreeSub) {
        npuFreeSub.textContent = `${npuFreeGb} ${GB}`;
    }
    if (npuTotalSub) {
        npuTotalSub.textContent = `${npuTotalGb} ${GB}`;
    }
    if (npuUtilBar) {
        const npuMemPct = Math.round(((Number(npuStats.usedMb) || 0) / npuTotalMb) * 100) || 0;
        setWidthIfChanged(npuUtilBar, npuMemPct);
    }

    // 6. Dynamically update specs descriptions in DOM
    if (stats.cpu.name && specCpuName) {
        specCpuName.textContent = stats.cpu.name.startsWith("CPU") ? stats.cpu.name : "CPU " + stats.cpu.name;
    }
    if (npuStats.name && specNpuName) {
        specNpuName.textContent = npuStats.name.startsWith("NPU") ? npuStats.name : "NPU " + npuStats.name;
    }
    if (stats.gpu.name && specGpuName) {
        specGpuName.textContent = stats.gpu.name.startsWith("GPU") ? stats.gpu.name : "GPU " + stats.gpu.name;
    }
    if (stats.ram.totalGb && specRamName) {
        const speed = stats.ram.speedMts ? ` @ ${stats.ram.speedMts} MT/s` : "";
        const ramType = stats.ram.type ? ` ${stats.ram.type}` : "";
        const ramModules = stats.ram.modules > 1 ? ` (${t("ram.modules", { n: stats.ram.modules })})` : "";
        specRamName.textContent = `RAM ${stats.ram.totalGb} ${GB}${ramType}${ramModules}${speed}`;
    }
    if (stats.network.name && specNetMode) {
        specNetMode.textContent = `${t("label.network")} ${stats.network.name}`;
    }
    if (stats.igpu.name && specVramName) {
        specVramName.textContent = (stats.igpu.name.toLowerCase().startsWith("igpu") || stats.igpu.name.toLowerCase().startsWith("intel")) 
            ? (stats.igpu.name.toLowerCase().startsWith("igpu") ? stats.igpu.name : "iGPU " + stats.igpu.name) 
            : "iGPU " + stats.igpu.name;
    }

    if (specSsdName) {
        specSsdName.textContent = stats.disk.name ? `SSD ${stats.disk.name}` : "SSD";
    }
    if (stats.motherboard && clockMb) {
        clockMb.textContent = `${t("label.motherboard")} ${stats.motherboard}`;
    }
    if (clockBattery) {
        const battery = stats.battery;
        if (battery && battery.present && battery.percent >= 0) {
            const batteryText = t("battery.status", { pct: battery.percent, source: t(battery.ac ? "battery.ac" : "battery.dc") });
            if (clockBattery.textContent !== batteryText) {
                clockBattery.textContent = batteryText;
            }
            clockBattery.style.display = "";
        } else {
            clockBattery.style.display = "none";
        }
    }
    // Windows energy saver: hold decorative motion still; values keep updating.
    document.body.classList.toggle("energy-saver", stats.energySaver === true);
    // 7. Update Holographic Neural Telemetry Map live parameters
    if (telemetryNodes.npu) {
        telemetryNodes.npu.value = stats.totalProcesses;
        telemetryNodes.npu.subLabel = stats.totalProcesses ? t("node.processes", { n: stats.totalProcesses }) : "CORE ACTIVE";
        
        telemetryNodes.cpu.value = stats.cpu.utilization;
        telemetryNodes.cpu.subLabel = stats.cpu.temp >= 0 ? `${stats.cpu.temp} °C` : `${stats.cpu.freqGhz} GHz`;
        
        telemetryNodes.igpu.value = stats.igpu.utilization;
        telemetryNodes.igpu.subLabel = `${usedVramGb} ${GB}`;

        telemetryNodes.npuAccel.value = npuStats.utilization;
        telemetryNodes.npuAccel.subLabel = `${npuUsedGb} ${GB}`;
        
        // Rolling minimum baseline for page faults/sec to filter idle background noise on any PC
        if (!window.ramFaultsHistory) {
            window.ramFaultsHistory = [];
        }
        const faults = stats.ram.activity || 0;
        window.ramFaultsHistory.push(faults);
        if (window.ramFaultsHistory.length > 30) {
            window.ramFaultsHistory.shift();
        }
        const ramBaseline = Math.min(...window.ramFaultsHistory);
        const ramDelta = Math.max(0, faults - ramBaseline);
        
        // Dynamic noise gate (minimum of 5000 faults/sec or 15% of baseline)
        const noiseGate = Math.max(5000, ramBaseline * 0.15);
        const activeDelta = ramDelta > noiseGate ? (ramDelta - noiseGate) : 0;
        
        // Combine soft page faults rate, CPU load, and Disk activity for a hyper-realistic real-time memory bus activity
        const faultsFactor = Math.min(40, Math.sqrt(activeDelta) * 0.15);
        const cpuFactor = stats.cpu.utilization * 0.45;
        const diskFactor = stats.disk.utilization * 0.45;
        
        let ramActivityPct = Math.round(cpuFactor + diskFactor + faultsFactor);
        
        // Drop completely to 0% when idle to satisfy user request
        if (ramActivityPct < 4) {
            ramActivityPct = 0;
        }
        ramActivityPct = Math.min(100, ramActivityPct);
        
        telemetryNodes.ram.value = ramActivityPct;
        telemetryNodes.ram.subLabel = `${usedRamGb} / ${totalRamGb} ${GB}`;
        
        telemetryNodes.gpu.value = stats.gpu.utilization;
        telemetryNodes.gpu.subLabel = stats.gpu.temp >= 0 ? `${stats.gpu.temp} °C` : `${dgpuVramUsedGb} ${GB}`;
        
        if (stats.disk.drive) telemetryNodes.ssd.label = `SSD ${stats.disk.drive}`;
        telemetryNodes.ssd.value = stats.disk.utilization; // Dynamic active % inside the center node map!
        telemetryNodes.ssd.subLabel = t("node.diskFree", { gb: `${stats.disk.freeGb} ${GB}` });
        
        const maxSpeedKb = getNetworkLinkSpeedKb(stats);
        
        let netActivityPct = 0;
        if (totalNetKb > 0) {
            const rawPct = (totalNetKb / maxSpeedKb) * 100;
            // Logarithmic/square root scale to keep it beautifully alive on everyday usage
            netActivityPct = Math.min(100, Math.max(1, Math.round(Math.sqrt(rawPct / 100) * 100)));
        }
        
        telemetryNodes.net.value = netActivityPct;
        pushCardHistory("net-card", netActivityPct);
        telemetryNodes.net.suffix = "%";
        
        const speedStr = formatNetSpeed(totalNetKb);
        telemetryNodes.net.subLabel = speedStr;
    }

    pushCardHistory("cpu-card", stats.cpu.utilization);
    pushCardHistory("vram-card", stats.igpu.utilization);
    pushCardHistory("npu-card", npuStats.utilization);
    pushCardHistory("gpu-card", stats.gpu.utilization);
    pushCardHistory("ram-card", stats.ram.utilization);
    pushCardHistory("ssd-card", stats.disk.utilization);

    updatePowerPlanButtons(stats);

    updateLoadAlerts(stats, performance.now());
    refreshTelemetryFreshness(true);

    // Spawn data packets per node so every active circle emits fairly.
    const now = performance.now();
    let spawnedPacket = false;
    let mapValueChanged = false;
    Object.keys(telemetryNodes).forEach(key => {
        if (key === "npu") return;
        const node = telemetryNodes[key];
        if (node.visible === false) return;
        const pct = node.value || 0;
        const previous = lastTelemetryNodeValues[key];
        lastTelemetryNodeValues[key] = pct;
        if (previous === undefined || Math.abs(pct - previous) >= 1) {
            mapValueChanged = true;
        }
        if (pct < 1) return;

        const elapsedForNode = now - (lastPacketNodeTime[key] || 0);
        const interval = getPacketIntervalMs(pct);
        const changedEnough = previous === undefined || Math.abs(pct - previous) >= Math.max(2, Math.min(8, Math.round(pct / 12)));
        const heartbeatDue = elapsedForNode >= interval;
        if (!changedEnough && !heartbeatDue) return;

        if (dataPackets.length >= MAX_DATA_PACKETS) return;
        
        const count = Math.min(getPacketBurstCount(pct), MAX_DATA_PACKETS - dataPackets.length);
        
        for (let i = 0; i < count; i++) {
            const packet = new DataPacket(node, node.color);
            packet.progress = -Math.random() * 0.35; // Staggered start
            dataPackets.push(packet);
        }
        lastPacketNodeTime[key] = now;
        spawnedPacket = true;
    });
    
    if (spawnedPacket) {
        wakeCanvas();
    } else if (mapValueChanged) {
        wakeCanvas();
    }
    sendRemoteBounds(false);
}

function updatePowerPlanButtons(stats) {
    if (!stats.powerPlans || !powerPlansContainer) return;
    powerPlansContainer.dataset.planCount = String(stats.powerPlans.length);

    if (pendingPowerPlanGuid) {
        const pendingPlan = stats.powerPlans.find(p => p.guid === pendingPowerPlanGuid);
        if (pendingPlan && pendingPlan.active) {
            clearPowerPlanPending();
        }
    }

    if (isPowerPlanSwitching) return;

    const currentGuids = Array.from(powerPlansContainer.children).map(btn => btn.dataset.guid).join(',');
    const newGuids = stats.powerPlans.map(p => p.guid).join(',');
    if (currentGuids !== newGuids) {
        powerPlansContainer.innerHTML = "";
        stats.powerPlans.forEach(plan => {
            const button = document.createElement("button");
            button.className = "remote-btn";
            button.dataset.guid = plan.guid;
            button.title = plan.name;
            button.setAttribute("aria-label", t("power.aria", { name: plan.name }));

            // Determine icon based on name
            let iconSvg = "";
            const lowerName = plan.name.toLowerCase();
            if (lowerName.includes("gamer") || lowerName.includes("extreme") || lowerName.includes("perform") || lowerName.includes("jeux")) {
                iconSvg = `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M12 2L1 21h22L12 2zm0 4l7.5 13h-15L12 6zm-1 5h2v4h-2v-4zm0-3h2v2h-2V8z"/></svg>`;
            } else if (lowerName.includes("veille") || lowerName.includes("eco") || lowerName.includes("silent") || lowerName.includes("silencieux") || lowerName.includes("econom") || lowerName.includes("saver")) {
                iconSvg = `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M9.5 2c-1.82 0-3.53.5-5 1.35 2.99 1.73 5 4.95 5 8.65s-2.01 6.92-5 8.65c1.47.85 3.18 1.35 5 1.35 5.52 0 10-4.48 10-10S15.02 2 9.5 2z"/></svg>`;
            } else {
                iconSvg = `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"/></svg>`;
            }

            // Use actual Windows plan name directly for premium custom setups
            let displayName = plan.name;

            button.innerHTML = `<div class="icon-wrap">${iconSvg}</div><span class="btn-lbl"></span>`;
            button.querySelector('.btn-lbl').textContent = displayName.toUpperCase();

            button.addEventListener("click", () => setPowerPlan(plan.guid));
            powerPlansContainer.appendChild(button);
        });
    }

    // Update active classes
    currentPowerPlan = "";
    Array.from(powerPlansContainer.children).forEach(button => {
        const guid = button.dataset.guid;
        const plan = stats.powerPlans.find(p => p.guid === guid);
        if (plan && plan.active) {
            currentPowerPlan = guid;
            button.className = "remote-btn active";
            button.setAttribute("aria-pressed", "true");
            const lowerName = plan.name.toLowerCase();
            if (lowerName.includes("gamer") || lowerName.includes("extreme") || lowerName.includes("perform") || lowerName.includes("jeux")) {
                button.id = "btn-extreme";
            } else if (lowerName.includes("veille") || lowerName.includes("eco") || lowerName.includes("silent") || lowerName.includes("silencieux") || lowerName.includes("econom") || lowerName.includes("saver")) {
                button.id = "btn-eco";
            } else {
                button.id = "btn-balanced";
            }
        } else {
            button.className = "remote-btn";
            button.setAttribute("aria-pressed", "false");
            button.id = "";
        }
    });
}

function setPowerPlan(guid) {
    if (!guid || guid === currentPowerPlan || guid === pendingPowerPlanGuid) return;

    currentPowerPlan = guid;
    pendingPowerPlanGuid = guid;
    isPowerPlanSwitching = true;
    if (powerPlanSwitchTimer) {
        clearTimeout(powerPlanSwitchTimer);
    }
    powerPlanSwitchTimer = setTimeout(() => {
        pendingPowerPlanGuid = "";
        isPowerPlanSwitching = false;
        powerPlanSwitchTimer = 0;
    }, 6000);

    // Optimistic UI update: instantly toggle active classes in DOM for instantaneous feedback
    if (powerPlansContainer) {
        Array.from(powerPlansContainer.children).forEach(button => {
            if (button.dataset.guid === guid) {
                button.className = "remote-btn active";
                button.setAttribute("aria-pressed", "true");
                const lowerName = button.querySelector('.btn-lbl').textContent.toLowerCase();
                if (lowerName.includes("gamer") || lowerName.includes("extreme") || lowerName.includes("perform") || lowerName.includes("jeux")) {
                    button.id = "btn-extreme";
                } else if (lowerName.includes("veille") || lowerName.includes("eco") || lowerName.includes("silent") || lowerName.includes("silencieux") || lowerName.includes("econom") || lowerName.includes("saver")) {
                    button.id = "btn-eco";
                } else {
                    button.id = "btn-balanced";
                }
            } else {
                button.className = "remote-btn";
                button.setAttribute("aria-pressed", "false");
                button.id = "";
            }
        });
    }

    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage("SET_POWER:" + guid);
    } else {
        console.warn("[USER CLICK] Power plan switching requires the native WebView2 host.");
    }
}

function clearPowerPlanPending() {
    pendingPowerPlanGuid = "";
    isPowerPlanSwitching = false;
    if (powerPlanSwitchTimer) {
        clearTimeout(powerPlanSwitchTimer);
        powerPlanSwitchTimer = 0;
    }
}

function setSensorMetric(element, label, value, unit, stats, source) {
    const state = NexusTelemetry.sensorState(stats, source);
    const usable = state === 'fresh' && NexusTelemetry.valid(value);
    setSubMetric(element, label, usable ? `${value} ${unit}` : '—');
    element.classList.toggle('sensor-unavailable', !usable);
    element.title = usable ? (unit === 'W' ? t('sensor.boardPower') : '')
        : state === 'stale' ? t('sensor.stale') : t('sensor.missing');
}

function updateLoadAlerts(stats, now) {
    for (const [card, section] of [[cpuCard, 'cpu'], [vramCard, 'igpu'], [npuCard, 'npu'], [gpuCard, 'gpu'], [ramCard, 'ram']]) {
        if (!card) continue;
        if (!loadAlerts.has(section)) loadAlerts.set(section, new NexusTelemetry.SustainedLoad());
        const metric = stats[section];
        const high = loadAlerts.get(section).update(metric && metric.detected !== false ? metric.utilization : null, now);
        card.classList.toggle('high-load', high);
        card.dataset.alertText = high ? t(section === 'ram' ? 'alert.memoryFull' : 'alert.highLoad') : '';
    }
}

const HIGH_LOAD_TITLE = t('alert.highLoadTitle');
// The badge is an aria-live region: rewriting identical text would be re-announced.
function setBadge(card, text, title) {
    const badge = card.querySelector('.critical-alert');
    if (!badge) return;
    if (badge.textContent !== text) badge.textContent = text;
    if (badge.title !== title) badge.title = title;
}

function refreshTelemetryFreshness(force = false) {
    if (!lastTelemetry || (runtimeSuspended && !force)) return;
    const expired = performance.now() - lastTelemetryReceived > 6000;
    for (const card of document.querySelectorAll('.gauge-card')) {
        const isGpu = card === gpuCard;
        const status = isGpu ? NexusTelemetry.sensorState(lastTelemetry, 'nvidia')
            : card === vramCard || card === npuCard ? NexusTelemetry.sensorState(lastTelemetry, 'windowsGpu')
            : card === cpuCard ? NexusTelemetry.sensorState(lastTelemetry, 'cpuTemperature') : 'fresh';
        const stale = expired || status === 'stale';
        card.classList.toggle('sensor-stale', stale);
        if (stale) {
            card.classList.remove('high-load');
            setBadge(card, t(expired ? 'alert.dataExpired' : 'alert.sensorsStale'), '');
        } else if (card.classList.contains('high-load')) {
            setBadge(card, card.dataset.alertText || t('alert.highLoad'), HIGH_LOAD_TITLE);
        } else {
            setBadge(card, '', '');
        }
    }
    if (expired && !telemetryExpired) {
        telemetryExpired = true;
        loadAlerts.forEach(alert => alert.reset());
        for (const value of document.querySelectorAll('.gauge-value, .sub-metric .val')) value.textContent = '—';
        for (const ring of document.querySelectorAll('.ring-fill')) setCircularProgress(ring, 0);
        for (const bar of [igpuVramBar, gpuVramBar, npuUtilBar]) if (bar) bar.style.width = "0%";
        for (const gauge of document.querySelectorAll('[role="progressbar"]')) gauge.removeAttribute('aria-valuenow');
        for (const node of telemetryNodeList) { node.value = null; node.subLabel = t('node.dataExpired'); }
        redrawCanvas();
    }
}

// --- 60 s history drawn as a faint area behind each card's content ---
const HISTORY_LENGTH = 60;
const cardHistories = [
    ["cpu-card", "#bf5aff"], ["vram-card", "#30d158"], ["npu-card", "#00c7be"], ["gpu-card", "#ff375f"],
    ["ram-card", "#0a84ff"], ["ssd-card", "#64d2ff"], ["net-card", "#ff9f0a"]
].map(([id, color]) => {
    const card = document.getElementById(id);
    if (!card) return null;
    const ns = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(ns, "svg");
    svg.setAttribute("class", "history-line");
    svg.setAttribute("viewBox", `0 0 ${HISTORY_LENGTH - 1} 100`);
    svg.setAttribute("preserveAspectRatio", "none");
    svg.setAttribute("aria-hidden", "true");
    const gradientId = `${id}-history-fill`;
    svg.innerHTML = `<defs><linearGradient id="${gradientId}" x1="0" y1="0" x2="0" y2="1">` +
        `<stop offset="0%" stop-color="${color}" stop-opacity="0.16"/><stop offset="100%" stop-color="${color}" stop-opacity="0"/></linearGradient></defs>` +
        `<path class="history-area" fill="url(#${gradientId})"/><path class="history-stroke" stroke="${color}"/>`;
    card.insertBefore(svg, card.firstChild);
    return { id, values: [], area: svg.querySelector(".history-area"), stroke: svg.querySelector(".history-stroke") };
}).filter(Boolean);

// Builds the line and filled area from the samples; missing readings break the line instead of faking zero.
function historyPaths(values) {
    const offset = HISTORY_LENGTH - values.length;
    let line = "", area = "", runStart = -1, last = -1;
    const closeRun = () => { if (runStart >= 0) area += `L${last} 100 L${runStart} 100 Z`; runStart = -1; };
    values.forEach((value, index) => {
        const x = offset + index;
        if (value === null) { closeRun(); return; }
        const y = (100 - Math.max(0, Math.min(100, value)) * 0.92).toFixed(1);
        if (runStart < 0) { line += `M${x} ${y}`; area += `M${x} 100 L${x} ${y}`; runStart = x; }
        else { line += `L${x} ${y}`; area += `L${x} ${y}`; }
        last = x;
    });
    closeRun();
    return { line, area };
}

function pushCardHistory(id, value) {
    const history = cardHistories.find(h => h.id === id);
    if (!history) return;
    history.values.push(NexusTelemetry.valid(value) ? value : null);
    if (history.values.length > HISTORY_LENGTH) history.values.shift();
    const paths = historyPaths(history.values);
    history.stroke.setAttribute("d", paths.line);
    history.area.setAttribute("d", paths.area);
}

// Direct DOM update instead of requestAnimationFrame loop to save CPU and iGPU cycles
function animateTextValue(element, targetValue, suffix = "") {
    if (!NexusTelemetry.valid(targetValue)) {
        element.textContent = "—";
        return;
    }
    const currentText = element.textContent || "";
    if (currentText.includes(suffix)) {
        const parsed = parseInt(currentText, 10);
        if (parsed === targetValue) return;
    }

    if (element.firstChild && element.firstChild.nodeType === 3 && element.querySelector('span')) {
        element.firstChild.nodeValue = targetValue;
    } else {
        element.innerHTML = `${targetValue}<span>${suffix}</span>`;
    }
}

// Stream connection
function connectStream() {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.addEventListener('message', event => {
            try {
                const stats = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
                if (stats && stats.control) {
                    if (stats.control === "SUSPEND" || stats.control === "RESUME") {
                        hostRuntimeSuspended = stats.control === "SUSPEND";
                        setRuntimeSuspended(hostRuntimeSuspended || document.hidden);
                    } else if (stats.control === "SETTINGS") {
                        setCenterLogo(stats.logo);
                    } else if (stats.control === "POWER_RESULT") {
                        if (!pendingPowerPlanGuid || stats.requestedGuid === pendingPowerPlanGuid) {
                            clearPowerPlanPending();
                            if (stats.activeGuid) {
                                currentPowerPlan = stats.activeGuid;
                            }
                        }
                    }
                    return;
                }
                if (!runtimeSuspended) {
                    updateDOM(stats);
                    if (resumeSnapTimer) {
                        const generation = resumeGeneration;
                        requestAnimationFrame(() => requestAnimationFrame(() => {
                            if (generation === resumeGeneration && !runtimeSuspended) endResumeSnap();
                        }));
                    }
                }
            } catch (error) {
                console.error("WebView2 message parse error:", error);
            }
        });
        try {
            window.chrome.webview.postMessage("REQUEST_RUNTIME_STATE");
        } catch (e) {}
        return;
    }

    console.warn("Native WebView2 host not detected; telemetry is disabled in standalone browser mode.");
    dimGauges();
}
function dimGauges() {
    setCircularProgress(cpuRing, 0);
    setCircularProgress(vramRing, 0);
    setCircularProgress(npuRing, 0);
    setCircularProgress(gpuRing, 0);
    setCircularProgress(ramRing, 0);
    setCircularProgress(ssdRing, 0);
    setCircularProgress(netRing, 0);
    
    if (cpuGaugeContainer) cpuGaugeContainer.setAttribute("aria-valuenow", 0);
    if (vramGaugeContainer) vramGaugeContainer.setAttribute("aria-valuenow", 0);
    if (npuGaugeContainer) npuGaugeContainer.setAttribute("aria-valuenow", 0);
    if (gpuGaugeContainer) gpuGaugeContainer.setAttribute("aria-valuenow", 0);
    if (ramGaugeContainer) ramGaugeContainer.setAttribute("aria-valuenow", 0);
    if (ssdGaugeContainer) ssdGaugeContainer.setAttribute("aria-valuenow", 0);
    if (netGaugeContainer) netGaugeContainer.setAttribute("aria-valuenow", 0);
    
    cpuVal.innerHTML = `0<span>%</span>`;
    vramVal.innerHTML = `0<span>%</span>`;
    npuVal.innerHTML = `0<span>%</span>`;
    gpuVal.innerHTML = `0<span>%</span>`;
    ramVal.innerHTML = `0<span>%</span>`;
    ssdVal.innerHTML = `0<span>%</span>`;
    netVal.innerHTML = `0<span>${KB}/s</span>`;
    

    
    cpuTempSub.textContent = `-- °C`;
    if (cpuFreqSub) cpuFreqSub.textContent = `0.00 GHz`;
    if (cpuProcThreadsSub) cpuProcThreadsSub.textContent = `0 / 0`;
    if (cpuTopProcSub) {
        cpuTopProcSub.textContent = `--`;
    }
    if (igpuVramBar) igpuVramBar.style.width = "0%";
    if (gpuVramBar) gpuVramBar.style.width = "0%";
    
    gpuTempSub.textContent = `-- °C`;
    if (gpuVramSub) gpuVramSub.textContent = `-- ${GB}`;
    gpuCoreSub.textContent = `-- MHz`;
    if (gpuMemClockSub) gpuMemClockSub.textContent = `-- MHz`;

    vramUsedSub.textContent = `0.0 ${GB}`;
    vramFreeSub.textContent = `0.0 ${GB}`;
    if (vramTotalSub) vramTotalSub.textContent = `0 ${GB}`;
    if (igpuDecodeSub) igpuDecodeSub.textContent = `-- %`;
    if (npuUsedSub) npuUsedSub.textContent = `0.0 ${GB}`;
    if (npuSharedSub) npuSharedSub.textContent = `0.0 ${GB}`;
    if (npuFreeSub) npuFreeSub.textContent = `0.0 ${GB}`;
    if (npuTotalSub) npuTotalSub.textContent = `0 ${GB}`;
    if (npuUtilBar) npuUtilBar.style.width = "0%";

    ramFreeSub.textContent = `0.0 ${GB}`;
    if (ramCachedSub) ramCachedSub.textContent = `0.0 ${GB}`;
    if (ramTopProcSub) ramTopProcSub.textContent = `--`;
    ramStatusSub.textContent = `0.0 ${GB} / 0.0 ${GB}`;
    ramStatusSub.className = "val";
    
    ssdFreeSub.textContent = `0 ${GB}`;
    if (ssdReadSub && ssdWriteSub) {
        ssdReadSub.textContent = `0.0 ${MB}/s`;
        ssdWriteSub.textContent = `0.0 ${MB}/s`;
    }
    if (ssdResponseSub) ssdResponseSub.textContent = `0.0 ms`;
    
    netWifiSub.textContent = `0.0 ${KB}/s`;
    netLanSub.textContent = `0.0 ${KB}/s`;
    if (netTypeSub) netTypeSub.textContent = `--`;
    if (netPingSub) netPingSub.textContent = `0 ms`;
    
    if (clockMb) {
        clockMb.textContent = "--";
    }
    if (clockBattery) {
        clockBattery.style.display = "none";
    }

    // Await real readings; a missing measurement is not an idle component.
    for (const node of telemetryNodeList) node.value = null;
    for (const value of document.querySelectorAll('.gauge-value, .sub-metric .val')) value.textContent = '—';
    for (const gauge of document.querySelectorAll('[role="progressbar"]')) {
        gauge.removeAttribute('aria-valuenow');
        gauge.setAttribute('aria-valuetext', t('aria.waiting'));
    }


}

window.addEventListener("DOMContentLoaded", () => {
    resizeCanvas();
    syncTelemetryLayoutAfterResize(true);
    dimGauges();
    connectStream();
    redrawCanvas();

    // Attach click listeners immediately to pre-populated power plans
    if (powerPlansContainer) {
        Array.from(powerPlansContainer.children).forEach(button => {
            const guid = button.dataset.guid;
            if (guid) {
                button.addEventListener("click", () => setPowerPlan(guid));
            }
        });
    }

    setTimeout(() => sendRemoteBounds(true), 1000);
});

function sendRemoteBounds(force = false) {
    const el = document.querySelector('.remote-sub-panel');
    if (el) {
        const rect = el.getBoundingClientRect();
        try {
            const dpr = window.devicePixelRatio || 1;
            const msg = "BOUNDS:" + Math.round(rect.left) + "," + Math.round(rect.top) + "," + Math.round(rect.right) + "," + Math.round(rect.bottom) + "," + dpr.toFixed(4);
            if (force || msg !== lastRemoteBoundsMsg) {
                lastRemoteBoundsMsg = msg;
                window.chrome.webview.postMessage(msg);
            }
        } catch(e) {}
    }
}

window.addEventListener("resize", () => sendRemoteBounds(true));

window.addEventListener("load", () => {
    resizeCanvas();
    syncTelemetryLayoutAfterResize(true);
    redrawCanvas();
});

if (typeof module !== 'undefined' && module.exports) {
    module.exports = {
        setCircularProgress,
        animateTextValue,
        formatNetSpeed,
        updateClock
    };
}
