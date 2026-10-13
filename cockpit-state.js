// Cockpit state: DOM references, shared runtime state, translations and the clock.
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
const ssdCard = document.getElementById("ssd-card");
const vramCard = document.getElementById("vram-card");
const igpuEncodeSub = document.getElementById("igpu-encode-sub");
const igpuDecodeSub = document.getElementById("igpu-decode-sub");

// Sub-metrics DOM bindings
const cpuTempSub = document.getElementById("cpu-temp-sub");
const cpuTopProcSub = document.getElementById("cpu-top-proc-sub");
const cpuFreqSub = document.getElementById("cpu-freq-sub");
const cpuProcThreadsSub = document.getElementById("cpu-proc-threads-sub");

const gpuTempSub = document.getElementById("gpu-temp-sub");
const gpuVramSub = document.getElementById("gpu-vram-sub");
const gpuCoreSub = document.getElementById("gpu-core-sub");
const gpuTopProcSub = document.getElementById("gpu-top-proc-sub");

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
const igpuTopProcSub = document.getElementById("igpu-top-proc-sub");
const igpuVramBar = document.getElementById("igpu-vram-bar");
const npuUsedSub = document.getElementById("npu-used-sub");
const npuTopProcSub = document.getElementById("npu-top-proc-sub");
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
let heldReadingCards = new Set(); // cards currently showing a held (stale) reading
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
const num = NexusI18n.num;
const GB = NexusI18n.unit("GB"), MB = NexusI18n.unit("MB"), KB = NexusI18n.unit("KB");
// Last valid value of every displayed reading (see NexusTelemetry.LastValues).
const lastValues = new NexusTelemetry.LastValues();
// Windows "Animation effects" off: hold decorative motion still, like energy saver.
const reducedMotionQuery = window.matchMedia ? window.matchMedia("(prefers-reduced-motion: reduce)") : { matches: false };
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
