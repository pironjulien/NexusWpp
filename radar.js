// Radar canvas: telemetry nodes, data packets, background and the pause/resume of animation.
"use strict";

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
