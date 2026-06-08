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
const igpuTempSub = document.getElementById("igpu-temp-sub");

// Sub-metrics DOM bindings
const cpuTempSub = document.getElementById("cpu-temp-sub");
const cpuFanSub = document.getElementById("cpu-fan-sub");
const cpuUptimeSub = document.getElementById("cpu-uptime-sub");
const cpuFreqSub = document.getElementById("cpu-freq-sub");
const cpuProcThreadsSub = document.getElementById("cpu-proc-threads-sub");
const cpuCachesSub = document.getElementById("cpu-caches-sub");

const gpuTempSub = document.getElementById("gpu-temp-sub");
const gpuFanSub = document.getElementById("gpu-fan-sub");
const gpuCoreSub = document.getElementById("gpu-core-sub");
const gpuTopsSub = document.getElementById("gpu-tops-sub");

// Right Column DOM Elements
const ramVal = document.getElementById("ram-val");
const ramRing = document.getElementById("ram-ring");
const ramGaugeContainer = document.getElementById("ram-gauge-container");
const ramFreeSub = document.getElementById("ram-free-sub");
const ramCachedSub = document.getElementById("ram-cached-sub");
const ramPoolPagedSub = document.getElementById("ram-pool-paged-sub");
const ramPoolNonPagedSub = document.getElementById("ram-pool-nonpaged-sub");
const ramStatusSub = document.getElementById("ram-status-sub");
const ramSpeedSub = document.getElementById("ram-speed-sub");

const ssdVal = document.getElementById("ssd-val");
const ssdRing = document.getElementById("ssd-ring");
const ssdGaugeContainer = document.getElementById("ssd-gauge-container");
const ssdFreeSub = document.getElementById("ssd-free-sub");
const ssdUsedPctSub = document.getElementById("ssd-used-pct-sub");
const ssdReadSub = document.getElementById("ssd-read-sub");
const ssdWriteSub = document.getElementById("ssd-write-sub");
const ssdActiveSub = document.getElementById("ssd-active-sub");
const ssdResponseSub = document.getElementById("ssd-response-sub");

const netVal = document.getElementById("net-val");
const netRing = document.getElementById("net-ring");
const netGaugeContainer = document.getElementById("net-gauge-container");
const netWifiSub = document.getElementById("net-wifi-sub");
const netLanSub = document.getElementById("net-lan-sub");
const netIpSub = document.getElementById("net-ip-sub");
const netIpv6Sub = document.getElementById("net-ipv6-sub");
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
const ssdTotalSub = document.getElementById("ssd-total-sub");
const clockMb = document.getElementById("clock-mb");

// Remote Control Container
const powerPlansContainer = document.getElementById("power-plans-container");

// Local state
let currentPowerPlan = "";
let systemIsOverloaded = false;
let isPowerPlanSwitching = false;
let pendingPowerPlanGuid = "";
let powerPlanSwitchTimer = 0;
let lastRemoteBoundsMsg = "";
let runtimeSuspended = false;
const lastPacketNodeTime = {};
const lastTelemetryNodeValues = {};
const MAX_DATA_PACKETS = 72;

// --- 🕰️ CYBER-CLOCK WIDGET ENGINE ---
const days = ["DIMANCHE", "LUNDI", "MARDI", "MERCREDI", "JEUDI", "VENDREDI", "SAMEDI"];
const months = ["JANVIER", "FEVRIER", "MARS", "AVRIL", "MAI", "JUIN", "JUILLET", "AOUT", "SEPTEMBRE", "OCTOBRE", "NOVEMBRE", "DECEMBRE"];

function updateClock() {
    const now = new Date();
    const h = String(now.getHours()).padStart(2, '0');
    const m = String(now.getMinutes()).padStart(2, '0');
    const s = String(now.getSeconds()).padStart(2, '0');
    
    document.getElementById("clock-h").textContent = h;
    document.getElementById("clock-m").textContent = m;
    document.getElementById("clock-s").textContent = s;
    
    const dayName = days[now.getDay()];
    const day = String(now.getDate()).padStart(2, '0');
    const month = months[now.getMonth()];
    const year = now.getFullYear();
    
    document.getElementById("clock-date").textContent = `${dayName} ${day} ${month} ${year}`;
}
updateClock();
setInterval(updateClock, 1000); // 1-second interval to reduce CPU load // 1-second interval to reduce CPU load

// --- 🌌 SOTA CANVAS 2D PHYSICS ENGINE (HOLOGRAPHIC NEURAL TELEMETRY MAP) ---
const canvas = document.getElementById("physics-canvas");
const ctx = canvas.getContext("2d");
let width, height;
let bgCanvas = null;
let telemetryMapReady = false;
let telemetryNodesInitialized = false;
const CORE_TARGET_OFFSET_X = 10;
const CORE_TARGET_OFFSET_Y = -26;
const TELEMETRY_ORBIT_SCALE = 0.38;
const TELEMETRY_ORBIT_MAX = 320;
const TELEMETRY_ORBIT_RING_INDEX = 3;
const TELEMETRY_RADAR_RING_COUNT = 5;
const TELEMETRY_NODE_ANGLES = {
    cpu: -150,
    net: -90,
    ram: -30,
    ssd: 30,
    gpu: 90,
    igpu: 150,
    npuAccel: 180
};

function resizeCanvas() {
    const rect = canvas.parentElement.getBoundingClientRect();
    width = canvas.width = rect.width;
    height = canvas.height = rect.height;
    bgCanvas = null; // force pre-rendered background to regenerate on next draw
}
window.addEventListener("resize", () => {
    resizeCanvas();
    syncTelemetryLayoutAfterResize(true);
    wakeCanvas();
});
resizeCanvas();

// SOTA ResizeObserver to guarantee perfect circular aspect ratio under any layout dynamic shifts
if (canvas.parentElement && typeof ResizeObserver !== 'undefined') {
    const resizeObserver = new ResizeObserver(() => {
        resizeCanvas();
        syncTelemetryLayoutAfterResize(true);
        wakeCanvas();
    });
    resizeObserver.observe(canvas.parentElement);
}

// SOTA Visibility Change handler to completely sleep the CPU/iGPU when wallpaper hidden
document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
        isCanvasLoopRunning = false;
        // Suspend immediately when Windows hides the WebView to avoid background work.
    } else {
        wakeCanvas();
    }
});

// Load Logo Image in case it can be drawn at NPU Core
const logoImg = new Image();
logoImg.src = "julienpiron.png";
let logoLoaded = false;
logoImg.onload = () => {
    logoLoaded = true;
    wakeCanvas();
};
logoImg.onerror = () => {};

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
        desc: "CHARGE CPU"
    },
    igpu: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#30d158",
        colorEnd: "#1db954",
        glow: "#30d158",
        label: "iGPU",
        subLabel: "-- Go",
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: "CHARGE iGPU"
    },
    npuAccel: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#00c7be",
        colorEnd: "#64d2ff",
        glow: "#00c7be",
        label: "NPU",
        subLabel: "-- Go",
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: "CHARGE NPU",
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
        subLabel: "-- Go",
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: "ACTIVITÉ RAM"
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
        desc: "CHARGE GPU"
    },
    ssd: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#00c7be",
        colorEnd: "#64d2ff",
        glow: "#00c7be",
        label: "SSD C:",
        subLabel: "-- Go",
        value: 0,
        suffix: "%",
        rotAngle: 0,
        desc: "ACTIVITÉ DISQUE"
    },
    net: {
        x: 0, y: 0,
        targetX: 0, targetY: 0,
        radius: 26,
        color: "#ff9f0a",
        colorEnd: "#ffd60a",
        glow: "#ff9f0a",
        label: "RÉSEAU",
        subLabel: "-- KB/s",
        value: 0,
        suffix: "",
        rotAngle: 0,
        desc: "ACTIVITÉ RÉSEAU"
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

function getTelemetryLayout() {
    const cx = (width / 2) + CORE_TARGET_OFFSET_X;
    const cy = (height / 2) + CORE_TARGET_OFFSET_Y;
    const maxAvailableRadius = Math.max(80, Math.min(cx, width - cx, cy, height - cy) - 28);
    const maxSafeNodeOrbit = Math.max(96, Math.min(cx - 44, width - cx - 44, cy - 44, height - cy - 72));
    const preferredOrbit = Math.min(Math.min(width, height) * TELEMETRY_ORBIT_SCALE, TELEMETRY_ORBIT_MAX);
    const orbitRadius = Math.max(96, Math.min(preferredOrbit, maxSafeNodeOrbit));
    const radarStep = orbitRadius / TELEMETRY_ORBIT_RING_INDEX;
    const maxRadarRadius = Math.max(orbitRadius, Math.min(maxAvailableRadius, radarStep * TELEMETRY_RADAR_RING_COUNT));

    return { cx, cy, orbitRadius, radarStep, maxRadarRadius };
}

// Position targets setup
function updateNodeTargets() {
    const layout = getTelemetryLayout();
    const placeOnOrbit = (node, angleDeg) => {
        const angle = angleDeg * Math.PI / 180;
        node.targetX = layout.cx + Math.cos(angle) * layout.orbitRadius;
        node.targetY = layout.cy + Math.sin(angle) * layout.orbitRadius;
    };
    
    // Core at Center
    telemetryNodes.npu.targetX = layout.cx;
    telemetryNodes.npu.targetY = layout.cy;
    
    // Surround nodes: one shared radius, exact 60-degree spacing for the six visible nodes.
    placeOnOrbit(telemetryNodes.cpu, TELEMETRY_NODE_ANGLES.cpu);
    placeOnOrbit(telemetryNodes.net, TELEMETRY_NODE_ANGLES.net);
    placeOnOrbit(telemetryNodes.ram, TELEMETRY_NODE_ANGLES.ram);
    placeOnOrbit(telemetryNodes.ssd, TELEMETRY_NODE_ANGLES.ssd);
    placeOnOrbit(telemetryNodes.gpu, TELEMETRY_NODE_ANGLES.gpu);
    placeOnOrbit(telemetryNodes.igpu, TELEMETRY_NODE_ANGLES.igpu);
    placeOnOrbit(telemetryNodes.npuAccel, TELEMETRY_NODE_ANGLES.npuAccel);
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
    mouse.x = e.clientX - rect.left;
    mouse.y = e.clientY - rect.top;
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
    mouse.x = e.clientX - rect.left;
    mouse.y = e.clientY - rect.top;
    
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

function scheduleCanvasFrame(delay = physicsFpsInterval) {
    if (runtimeSuspended || !isCanvasLoopRunning || canvasFrameTimer) return;
    canvasFrameTimer = setTimeout(() => {
        canvasFrameTimer = 0;
        requestAnimationFrame(updatePhysics);
    }, delay);
}

function setRuntimeSuspended(suspended) {
    runtimeSuspended = suspended;
    if (suspended) {
        dataPackets.length = 0;
        coreParticles.length = 0;
        mouse.grabbedNode = null;
        isCanvasLoopRunning = false;
        needsRender = false;
        if (canvasFrameTimer) {
            clearTimeout(canvasFrameTimer);
            canvasFrameTimer = 0;
        }
        document.body.classList.remove("system-critical");
    } else {
        needsRender = true;
        wakeCanvas();
        try {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage("REQUEST_TELEMETRY");
            }
        } catch (e) {}
    }
}

// Main update physics and rendering loop
let lastPhysicsTime = 0;
const physicsFpsInterval = 1000 / 12; // active canvas cadence; sleeps fully when idle
function updatePhysics(timestamp) {
    if (!isCanvasLoopRunning) return; // Stop loop if suspended!
    
    if (!timestamp) timestamp = performance.now();
    const elapsed = timestamp - lastPhysicsTime;
    if (elapsed < physicsFpsInterval) {
        scheduleCanvasFrame(physicsFpsInterval - elapsed);
        return;
    }
    lastPhysicsTime = timestamp - (elapsed % physicsFpsInterval);
    

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
        node.rotAngle += baseRotSpeed + (loadFactor * 0.06);
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
        p.update();
        p.draw();
        
        // When packet reaches center NPU node
        if (p.progress >= 1.0) {
            spawnCoreSplash(p.target.x, p.target.y, p.color);
            dataPackets.splice(i, 1);
            
            // Pulse NPU core
            telemetryNodes.npu.radius = 56;
        }
    }
    
    // Decay NPU core radius back to base size
    telemetryNodes.npu.radius = Math.max(50, telemetryNodes.npu.radius - 0.25);
    
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
            ctx.fillText(`${node.value}${node.suffix}`, node.x, node.y - 3);
            
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
        p.x += p.vx;
        p.y += p.vy;
        p.vx *= 0.95;
        p.vy *= 0.95;
        p.life--;
        
        ctx.save();
        const alpha = p.life / p.maxLife;
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
        
        if (p.life <= 0) {
            coreParticles.splice(i, 1);
        }
    }

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
    const textValue = String(value);
    if (element.getAttribute(name) !== textValue) {
        element.setAttribute(name, textValue);
    }
}

function setWidthIfChanged(element, value) {
    if (!element) return;
    const widthValue = `${value}%`;
    if (element.style.width !== widthValue) {
        element.style.width = widthValue;
    }
}

function setCircularProgress(ring, value) {
    const offset = RING_CIRCUMFERENCE - (value / 100) * RING_CIRCUMFERENCE;
    const offsetValue = offset.toFixed(3);
    if (ring && ring._nexusOffset !== offsetValue) {
        ring._nexusOffset = offsetValue;
        ring.style.strokeDashoffset = offsetValue;
    }
}

function formatNetSpeed(kbVal) {
    if (kbVal === 0) return "0.0 Ko/s";
    if (kbVal < 1024) return `${kbVal.toFixed(1)} Ko/s`;
    return `${(kbVal / 1024).toFixed(1)} Mo/s`;
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

function updateDOM(stats) {
    const npuStats = stats.npu || {
        utilization: 0,
        name: "Intel(R) AI Boost",
        usedMb: 0,
        totalMb: 0,
        totalGb: 0,
        detected: false
    };
    const hasNpu = npuStats.detected === true;
    setNpuVisibility(hasNpu);

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

    // 2. CPU Sub-metrics
    cpuTempSub.textContent = `${stats.cpu.temp} °C`;
    cpuFanSub.textContent = `${stats.fans.fan1} RPM`;
    if (cpuFreqSub) {
        cpuFreqSub.textContent = `${stats.cpu.freqGhz} GHz`;
    }
    if (cpuProcThreadsSub) {
        cpuProcThreadsSub.textContent = `${stats.totalProcesses} / ${stats.cpu.threads}`;
    }
    if (cpuCachesSub) {
        cpuCachesSub.textContent = `${stats.cpu.l2Cache} / ${stats.cpu.l3Cache}`;
    }
    if (cpuUptimeSub) {
        const totalSecs = Number(stats.uptime) || 0;
        const daysCount = Math.floor(totalSecs / 86400);
        const hh = String(Math.floor((totalSecs % 86400) / 3600)).padStart(2, '0');
        const mm = String(Math.floor((totalSecs % 3600) / 60)).padStart(2, '0');
        const ss = String(totalSecs % 60).padStart(2, '0');
        cpuUptimeSub.textContent = `${daysCount > 0 ? daysCount + 'j ' : ''}${hh}:${mm}:${ss}`;
    }

    // 3. GPU Sub-metrics
    gpuTempSub.textContent = `${stats.gpu.temp} °C`;
    gpuFanSub.textContent = stats.fans.fan2 > 0 ? `${stats.fans.fan2} RPM` : "SILENT";
    gpuCoreSub.textContent = `${stats.gpu.coreClock} MHz`;
    if (gpuTopsSub) {
        gpuTopsSub.textContent = `${stats.gpu.tops} TOPS`;
    }
    if (gpuVramBar) {
        const gpuVramPct = Math.round((stats.vram.usedMb / stats.vram.totalMb) * 100) || 0;
        setWidthIfChanged(gpuVramBar, gpuVramPct);
    }

    // 4. Circular telemetry gauges (Right Column - RAM, SSD, Network)
    // RAM circular gauge
    setCircularProgress(ramRing, stats.ram.utilization);
    animateTextValue(ramVal, stats.ram.utilization, "%");
    setAttrIfChanged(ramGaugeContainer, "aria-valuenow", stats.ram.utilization);
    
    const totalRamGb = stats.ram.totalGb || 32;
    const freeRamGb = ((1 - stats.ram.utilization / 100) * totalRamGb).toFixed(1);
    const usedRamGb = ((stats.ram.utilization / 100) * totalRamGb).toFixed(1);
    ramFreeSub.textContent = `${freeRamGb} Go`;
    if (ramCachedSub) {
        ramCachedSub.textContent = `${stats.ram.cachedGb} Go`;
    }
    if (ramPoolPagedSub) {
        ramPoolPagedSub.textContent = `${stats.ram.poolPagedMb} Mo`;
    }
    if (ramPoolNonPagedSub) {
        ramPoolNonPagedSub.textContent = `${stats.ram.poolNonPagedMb} Mo`;
    }
    ramStatusSub.textContent = `${stats.ram.commitUsedGb} Go / ${stats.ram.commitLimitGb} Go`;
    ramStatusSub.className = "val text-blue";
    if (ramSpeedSub) {
        ramSpeedSub.textContent = `${stats.ram.speedMts} MT/s`;
    }

    // SSD card gauge: disk capacity occupation. The center map node shows live disk activity.
    setCircularProgress(ssdRing, stats.disk.storagePercent);
    animateTextValue(ssdVal, stats.disk.storagePercent, "%");
    setAttrIfChanged(ssdGaugeContainer, "aria-valuenow", stats.disk.storagePercent);
    ssdFreeSub.textContent = `${stats.disk.freeGb} Go / ${stats.disk.totalGb} Go`;
    if (ssdUsedPctSub) {
        ssdUsedPctSub.textContent = `${stats.disk.storagePercent} %`;
    }
    if (ssdReadSub && ssdWriteSub) {
        // Formatter with dynamic unit support
        const formatSpeed = (mbStr) => {
            const val = parseFloat(mbStr);
            if (val >= 1000) return `${(val / 1024).toFixed(1)} Go/s`;
            return `${val.toFixed(1)} Mo/s`;
        };
        ssdReadSub.textContent = formatSpeed(stats.disk.readMb);
        ssdWriteSub.textContent = formatSpeed(stats.disk.writeMb);
    }
    if (ssdActiveSub) {
        ssdActiveSub.textContent = `${stats.disk.utilization} %`;
    }
    if (ssdResponseSub) {
        ssdResponseSub.textContent = `${stats.disk.responseTimeMs.toFixed(1)} ms`;
    }

    // Network circular gauge & absolute speed rendering
    const totalNetKb = stats.network.lan + stats.network.wifi;
    
    // Dynamic value display
    if (totalNetKb === 0) {
        setHtmlIfChanged(netVal, `0<span>Ko/s</span>`);
    } else if (totalNetKb < 1024) {
        setHtmlIfChanged(netVal, `${totalNetKb}<span>Ko/s</span>`);
    } else {
        setHtmlIfChanged(netVal, `${(totalNetKb / 1024).toFixed(1)}<span>Mo/s</span>`);
    }
    
    // Scale the card ring against the real link speed (2.5 Gb/s Ethernet is about 305000 KiB/s).
    const linkSpeedKb = getNetworkLinkSpeedKb(stats);
    const netPercent = totalNetKb > 0 ? Math.min(100, Math.max(1, Math.round((totalNetKb / linkSpeedKb) * 100))) : 0;
    setCircularProgress(netRing, netPercent);
    setAttrIfChanged(netGaugeContainer, "aria-valuenow", netPercent);

    // Dynamic formatting for Send / Receive speeds

    netWifiSub.textContent = formatNetSpeed(stats.network.wifi); // RECEVOIR
    netLanSub.textContent = formatNetSpeed(stats.network.lan);   // ENVOYER
    if (netIpSub) {
        netIpSub.textContent = stats.network.ip || "127.0.0.1";
    }
    if (netIpv6Sub) {
        const cleanIpv6 = stats.network.ipv6.split('%')[0];
        netIpv6Sub.textContent = cleanIpv6 || "fe80::";
        netIpv6Sub.title = cleanIpv6;
    }
    if (netTypeSub) {
        netTypeSub.textContent = stats.network.type.toUpperCase();
    }
    if (netPingSub) {
        netPingSub.textContent = stats.ping > 0 ? `${stats.ping} ms` : "-- ms";
    }

    // 5. Update iGPU VRAM Sub-metrics
    const usedVramGb = (stats.igpu.usedMb / 1024).toFixed(1);
    const freeVramGb = ((stats.igpu.totalMb - stats.igpu.usedMb) / 1024).toFixed(1);
    const totalVramGb = (stats.igpu.totalMb / 1024).toFixed(0);
    vramUsedSub.textContent = `${usedVramGb} Go`;
    vramFreeSub.textContent = `${freeVramGb} Go`;
    if (vramTotalSub) {
        vramTotalSub.textContent = `${totalVramGb} Go`;
    }
    if (igpuTempSub) {
        igpuTempSub.textContent = `${stats.cpu.temp} °C`;
    }
    if (igpuVramBar) {
        const igpuVramPct = Math.round((stats.igpu.usedMb / stats.igpu.totalMb) * 100) || 0;
        setWidthIfChanged(igpuVramBar, igpuVramPct);
    }
    const npuUsedGb = ((Number(npuStats.usedMb) || 0) / 1024).toFixed(1);
    const npuTotalMb = Number(npuStats.totalMb) || 0;
    const npuTotalGb = npuTotalMb > 0 ? (npuTotalMb / 1024).toFixed(0) : "0";
    const npuFreeGb = npuTotalMb > 0 ? ((npuTotalMb - (Number(npuStats.usedMb) || 0)) / 1024).toFixed(1) : "0.0";
    if (npuUsedSub) {
        npuUsedSub.textContent = `${npuUsedGb} Go`;
    }
    if (npuSharedSub) {
        npuSharedSub.textContent = `${npuUsedGb} Go`;
    }
    if (npuFreeSub) {
        npuFreeSub.textContent = `${npuFreeGb} Go`;
    }
    if (npuTotalSub) {
        npuTotalSub.textContent = `${npuTotalGb} Go`;
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
        specRamName.textContent = `RAM ${stats.ram.totalGb} Go DDR5 Dual-Channel${speed}`;
    }
    if (stats.network.name && specNetMode) {
        specNetMode.textContent = stats.network.name.startsWith("RÉSEAU") ? stats.network.name : "RÉSEAU " + stats.network.name;
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
        clockMb.textContent = stats.motherboard.startsWith("CARTE MÈRE") ? stats.motherboard : "CARTE MÈRE " + stats.motherboard;
    }
    // 7. Update Holographic Neural Telemetry Map live parameters
    if (telemetryNodes.npu) {
        telemetryNodes.npu.value = stats.totalProcesses;
        telemetryNodes.npu.subLabel = stats.totalProcesses ? `${stats.totalProcesses} PROCESSUS` : "CORE ACTIVE";
        
        telemetryNodes.cpu.value = stats.cpu.utilization;
        telemetryNodes.cpu.subLabel = `${stats.cpu.temp} °C`;
        
        telemetryNodes.igpu.value = stats.igpu.utilization;
        telemetryNodes.igpu.subLabel = `${usedVramGb} Go`;

        telemetryNodes.npuAccel.value = npuStats.utilization || 0;
        telemetryNodes.npuAccel.subLabel = `${npuUsedGb} Go`;
        
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
        telemetryNodes.ram.subLabel = `${usedRamGb} / ${totalRamGb} Go`;
        
        telemetryNodes.gpu.value = stats.gpu.utilization;
        telemetryNodes.gpu.subLabel = `${stats.gpu.temp} °C`;
        
        telemetryNodes.ssd.value = stats.disk.utilization; // Dynamic active % inside the center node map!
        telemetryNodes.ssd.subLabel = `${stats.disk.freeGb} Go libre`;
        
        const maxSpeedKb = getNetworkLinkSpeedKb(stats);
        
        let netActivityPct = 0;
        if (totalNetKb > 0) {
            const rawPct = (totalNetKb / maxSpeedKb) * 100;
            // Logarithmic/square root scale to keep it beautifully alive on everyday usage
            netActivityPct = Math.min(100, Math.max(1, Math.round(Math.sqrt(rawPct / 100) * 100)));
        }
        
        telemetryNodes.net.value = netActivityPct;
        telemetryNodes.net.suffix = "%";
        
        const speedStr = formatNetSpeed(totalNetKb);
        telemetryNodes.net.subLabel = speedStr;
    }

    // 9. Update Remote controllers buttons dynamically based on actual Windows power plans
    if (stats.powerPlans && powerPlansContainer) {
        if (pendingPowerPlanGuid) {
            const pendingPlan = stats.powerPlans.find(p => p.guid === pendingPowerPlanGuid);
            if (pendingPlan && pendingPlan.active) {
                clearPowerPlanPending();
            }
        }

        if (isPowerPlanSwitching) {
            return;
        }

        const currentGuids = Array.from(powerPlansContainer.children).map(btn => btn.dataset.guid).join(',');
        const newGuids = stats.powerPlans.map(p => p.guid).join(',');
        if (currentGuids !== newGuids) {
            powerPlansContainer.innerHTML = "";
            stats.powerPlans.forEach(plan => {
                const button = document.createElement("button");
                button.className = "remote-btn";
                button.dataset.guid = plan.guid;
                
                // Determine icon based on name
                let iconSvg = "";
                const lowerName = plan.name.toLowerCase();
                if (lowerName.includes("gamer") || lowerName.includes("extreme") || lowerName.includes("perform") || lowerName.includes("jeux")) {
                    iconSvg = `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M12 2L1 21h22L12 2zm0 4l7.5 13h-15L12 6zm-1 5h2v4h-2v-4zm0-3h2v2h-2V8z"/></svg>`;
                } else if (lowerName.includes("veille") || lowerName.includes("eco") || lowerName.includes("silent") || lowerName.includes("silencieux") || lowerName.includes("econom")) {
                    iconSvg = `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M9.5 2c-1.82 0-3.53.5-5 1.35 2.99 1.73 5 4.95 5 8.65s-2.01 6.92-5 8.65c1.47.85 3.18 1.35 5 1.35 5.52 0 10-4.48 10-10S15.02 2 9.5 2z"/></svg>`;
                } else {
                    iconSvg = `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"/></svg>`;
                }
                
                // Use actual Windows plan name directly for premium custom setups
                let displayName = plan.name;
                
                button.innerHTML = `
                    <div class="icon-wrap">${iconSvg}</div>
                    <span class="btn-lbl">${displayName.toUpperCase()}</span>
                `;
                
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
                } else if (lowerName.includes("veille") || lowerName.includes("eco") || lowerName.includes("silent") || lowerName.includes("silencieux") || lowerName.includes("econom")) {
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



    // 10. Overload Crisis Easter Egg (>80%) - Triggers map stress vibrations
    let hasOverload = false;

    if (stats.cpu.utilization > 80) {
        cpuCard.classList.add("overload");
        hasOverload = true;
    } else {
        cpuCard.classList.remove("overload");
    }

    if (stats.gpu.utilization > 80) {
        gpuCard.classList.add("overload");
        hasOverload = true;
    } else {
        gpuCard.classList.remove("overload");
    }

    if (npuCard && hasNpu) {
        if (npuStats.utilization > 90) {
            npuCard.classList.add("overload");
            hasOverload = true;
        } else {
            npuCard.classList.remove("overload");
        }
    } else if (npuCard) {
        npuCard.classList.remove("overload");
    }

    if (stats.ram.utilization > 80) {
        ramCard.classList.add("overload");
        hasOverload = true;
    } else {
        ramCard.classList.remove("overload");
    }

    if (stats.igpu.utilization > 90) { // check iGPU utilization for overload
        vramCard.classList.add("overload");
        hasOverload = true;
    } else {
        vramCard.classList.remove("overload");
    }

    const overloadChanged = hasOverload !== systemIsOverloaded;

    // Sync stress mode state variable for the map
    systemIsOverloaded = hasOverload;

    if (hasOverload) {
        document.body.classList.add("system-critical");
    } else {
        document.body.classList.remove("system-critical");
    }
    
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
    } else if (mapValueChanged || overloadChanged) {
        wakeCanvas();
    }
    sendRemoteBounds(false);
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
                } else if (lowerName.includes("veille") || lowerName.includes("eco") || lowerName.includes("silent") || lowerName.includes("silencieux") || lowerName.includes("econom")) {
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

// Direct DOM update instead of requestAnimationFrame loop to save CPU and iGPU cycles
function animateTextValue(element, targetValue, suffix = "") {
    const currentText = element.textContent || "";
    if (currentText.includes(suffix)) {
        const parsed = parseInt(currentText, 10);
        if (parsed === targetValue) return;
    }

    if (element.firstChild && element.firstChild.nodeType === 3) {
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
                        setRuntimeSuspended(stats.control === "SUSPEND");
                    } else if (stats.control === "POWER_RESULT") {
                        if (stats.success || !pendingPowerPlanGuid || stats.requestedGuid === pendingPowerPlanGuid) {
                            clearPowerPlanPending();
                            if (stats.activeGuid) {
                                currentPowerPlan = stats.activeGuid;
                            }
                        }
                    }
                    return;
                }
                updateDOM(stats);
            } catch (error) {
                console.error("WebView2 message parse error:", error);
            }
        });
        try {
            window.chrome.webview.postMessage("REQUEST_TELEMETRY");
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
    netVal.innerHTML = `0<span>Ko/s</span>`;
    
    systemIsOverloaded = false;
    
    cpuTempSub.textContent = `0 °C`;
    cpuFanSub.textContent = `0 RPM`;
    if (cpuFreqSub) cpuFreqSub.textContent = `0.00 GHz`;
    if (cpuProcThreadsSub) cpuProcThreadsSub.textContent = `0 / 0`;
    if (cpuCachesSub) cpuCachesSub.textContent = `0 Mo / 0 Mo`;
    if (cpuUptimeSub) {
        cpuUptimeSub.textContent = `00:00:00`;
    }
    if (igpuVramBar) igpuVramBar.style.width = "0%";
    if (gpuVramBar) gpuVramBar.style.width = "0%";
    
    gpuTempSub.textContent = `0 °C`;
    gpuFanSub.textContent = `SILENT`;
    gpuCoreSub.textContent = `0 MHz`;
    if (gpuTopsSub) gpuTopsSub.textContent = `0 TOPS`;

    vramUsedSub.textContent = `0.0 Go`;
    vramFreeSub.textContent = `0.0 Go`;
    if (vramTotalSub) vramTotalSub.textContent = `0 Go`;
    if (igpuTempSub) igpuTempSub.textContent = `0 °C`;
    if (npuUsedSub) npuUsedSub.textContent = `0.0 Go`;
    if (npuSharedSub) npuSharedSub.textContent = `0.0 Go`;
    if (npuFreeSub) npuFreeSub.textContent = `0.0 Go`;
    if (npuTotalSub) npuTotalSub.textContent = `0 Go`;
    if (npuUtilBar) npuUtilBar.style.width = "0%";

    ramFreeSub.textContent = `0.0 Go`;
    if (ramCachedSub) ramCachedSub.textContent = `0.0 Go`;
    if (ramPoolPagedSub) ramPoolPagedSub.textContent = `0 Mo`;
    if (ramPoolNonPagedSub) ramPoolNonPagedSub.textContent = `0 Mo`;
    ramStatusSub.textContent = `0.0 Go / 0.0 Go`;
    ramStatusSub.className = "val";
    if (ramSpeedSub) ramSpeedSub.textContent = `0 MT/s`;
    
    ssdFreeSub.textContent = `0 Go`;
    if (ssdUsedPctSub) ssdUsedPctSub.textContent = `0 %`;
    if (ssdReadSub && ssdWriteSub) {
        ssdReadSub.textContent = `0.0 Mo/s`;
        ssdWriteSub.textContent = `0.0 Mo/s`;
    }
    if (ssdActiveSub) ssdActiveSub.textContent = `0 %`;
    if (ssdResponseSub) ssdResponseSub.textContent = `0.0 ms`;
    
    netWifiSub.textContent = `0.0 Ko/s`;
    netLanSub.textContent = `0.0 Ko/s`;
    if (netIpSub) netIpSub.textContent = `--`;
    if (netIpv6Sub) netIpv6Sub.textContent = `--`;
    if (netTypeSub) netTypeSub.textContent = `--`;
    if (netPingSub) netPingSub.textContent = `0 ms`;
    
    if (clockMb) {
        clockMb.textContent = "--";
    }

    if (vramCard) vramCard.classList.remove("overload");
    document.body.classList.remove("system-critical");
}

window.addEventListener("DOMContentLoaded", () => {
    resizeCanvas();
    syncTelemetryLayoutAfterResize(true);
    dimGauges();
    connectStream();
    wakeCanvas();

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
            const msg = "BOUNDS:" + Math.round(rect.left) + "," + Math.round(rect.top) + "," + Math.round(rect.right) + "," + Math.round(rect.bottom);
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
    wakeCanvas();
});

if (typeof module !== 'undefined' && module.exports) {
    module.exports = {
        setCircularProgress,
        animateTextValue,
        formatNetSpeed,
        updateClock
    };
}
