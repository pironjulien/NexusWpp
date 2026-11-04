// Gauge cards: telemetry rendering, power plans, alerts, freshness and the 60 s history.
"use strict";

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

// The accessible value follows the screen: a held value is announced as stale.
function setGaugeAria(container, reading) {
    if (!container) return;
    if (reading.value === null) {
        container.removeAttribute('aria-valuenow');
        setAttrIfChanged(container, 'aria-valuetext', t('aria.unavailable'));
        return;
    }
    setAttrIfChanged(container, 'aria-valuenow', reading.value);
    if (reading.stale) setAttrIfChanged(container, 'aria-valuetext', t('aria.stale', { value: `${num(reading.value)} %` }));
    else container.removeAttribute('aria-valuetext');
}

// Ring, value and accessible value of one gauge. A missing sample keeps the last
// valid reading on screen (no jump to a dash); a reading never seen shows "—".
function setGauge(key, ring, valueElement, container, value) {
    const reading = lastValues.take(key, value);
    if (reading.value !== null) setCircularProgress(ring, reading.value);
    animateTextValue(valueElement, reading.value, "%");
    setGaugeAria(container, reading);
    return reading;
}

// Sub-metric text: `text` is null when the sample is missing. The last text then
// stays, greyed as stale; a reading never seen shows the placeholder.
function shortProcessName(name, max) {
    return name.length > max ? name.slice(0, max - 1) + "…" : name;
}

// Longest form of a process name, followed by its measure, that fits on one line of its slot
// (narrow cards on small screens), measured on the element itself (the slot does not wrap, see .fit-one-line).
function fitProcessName(element, name, measure = '') {
    const suffix = measure ? ` ${measure}` : '';
    const width = element.parentElement ? element.parentElement.clientWidth : 0;
    let candidate = shortProcessName(name, 16) + suffix;
    if (!width) return candidate;
    const previous = element.textContent;
    for (let max = Math.min(name.length, 16); max >= 4; max--) {
        candidate = shortProcessName(name, max) + suffix;
        element.textContent = candidate;
        if (element.scrollWidth <= width) break;
    }
    element.textContent = previous;
    return candidate;
}

// "BATTERIE 73 % · SUR BATTERIE · 12,4 W · 2 H 15 RESTANTES", "BATTERIE 82 % · EN CHARGE · 25,3 W".
function formatBattery(battery) {
    const rate = Number(battery.rateW);
    const charging = battery.ac && rate > 0;
    let text = t("battery.status", { pct: battery.percent, source: t(charging ? "battery.charging" : battery.ac ? "battery.ac" : "battery.dc") });
    if (battery.rateW !== null && battery.rateW !== undefined && rate !== 0 && (charging || !battery.ac)) text += ` · ${num(Math.abs(rate), 1)} W`;
    const minutes = Number(battery.minutesLeft);
    if (!battery.ac && minutes > 0) {
        const time = minutes >= 60 ? `${Math.floor(minutes / 60)} H ${String(minutes % 60).padStart(2, "0")}` : `${minutes} MIN`;
        text += ` · ${t("battery.left", { time })}`;
    }
    return text;
}


// Name and measure last fitted in each slot: a slot resized later (monitor layout, DPI, paused
// rendering) is fitted again to its new width instead of overflowing.
const processFits = new Map();
const processFitSlots = new WeakSet();
const processFitObserver = typeof ResizeObserver === 'function' ? new ResizeObserver(entries => {
    for (const entry of entries) {
        const element = entry.target.querySelector('.fit-one-line');
        const fit = processFits.get(element);
        if (!fit) continue;
        const text = fitProcessName(element, fit.name, fit.measure);
        if (element.textContent !== text) element.textContent = text;
    }
}) : null;

// Busiest process of a card, as "name measure" ("MsMpEng 1 %", "java 3,2 Go").
// top null: no reading (last value kept); empty name: idle.
function setTopProcess(element, top, measure) {
    if (!element) return;
    if (top && top.name) {
        if (processFitObserver && element.parentElement && !processFitSlots.has(element.parentElement)) {
            processFitSlots.add(element.parentElement);
            processFitObserver.observe(element.parentElement);
        }
        processFits.set(element, { name: top.name, measure });
    } else if (top) {
        processFits.set(element, null);
    }
    setHeldText(element, top ? (top.name ? fitProcessName(element, top.name, measure) : t("adapter.idle")) : null);
}

// Adapters (iGPU, GPU, NPU): {"name","pct"} from the host, pct = share of the adapter used by that process.
function setAdapterTopProcess(element, top) {
    setTopProcess(element, top, top && top.name ? `${Math.round(Number(top.pct) || 0)} %` : '');
}

function setHeldText(element, text, placeholder = '—') {
    if (!element) return false;
    const held = text === null && element.dataset.seen === '1';
    if (text !== null) element.dataset.seen = '1';
    const next = text !== null ? text : held ? element.textContent : placeholder;
    if (element.textContent !== next) element.textContent = next;
    element.classList.toggle('sensor-unavailable', held);
    const title = held ? t('sensor.held') : '';
    if (element.title !== title) element.title = title;
    return held;
}

// Cache size sent by the host as invariant text ("24.0 Mo"), shown in the interface language ("24,0 Mo" / "24.0 MB").
function formatCacheSize(text) {
    const value = parseFloat(String(text || '').replace(',', '.'));
    return Number.isFinite(value) ? `${num(value, 1)} ${MB}` : '—';
}

// The ACPI thermal zone is shown when Windows exposes one: a reading now, or a reading lost
// for a while (the host then reports it stale and the last value stays, greyed).
function cpuZoneShown(stats) {
    return NexusTelemetry.valid(stats.cpu.temp) || NexusTelemetry.sensorState(stats, 'cpuTemperature') === 'stale';
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

// Bar fill as a horizontal scale (see .sub-progress-fill): composited, no layout per frame.
function setBarFill(element, value) {
    if (!element) return;
    const scale = `scaleX(${(NexusTelemetry.valid(value) ? Math.min(100, value) : 0) / 100})`;
    if (element.style.transform !== scale) {
        element.style.transform = scale;
    }
}

// Ring motion keeps the former 0.18 s ease-out, but it is stepped by the shared animation
// clock (radar.js) instead of a CSS transition repainted at the display rate. A 1 s ease was
// measured at +2.3 points of one core (stroke-dashoffset under a drop-shadow is repainted,
// it cannot run on the compositor): more than the whole 12 -> 20 Hz step, so it stays short.
const RING_TWEEN_MS = 180;
const ringTweens = new Map();

function setCircularProgress(ring, value) {
    if (!ring) return;
    const offset = RING_CIRCUMFERENCE - ((NexusTelemetry.valid(value) ? Math.min(100, value) : 0) / 100) * RING_CIRCUMFERENCE;
    const offsetValue = offset.toFixed(3);
    if (ring._nexusOffset === offsetValue) return;
    const animate = ring._nexusShown !== undefined && !runtimeSuspended && !reducedMotionQuery.matches
        && !document.body.classList.contains("runtime-resuming");
    ring._nexusOffset = offsetValue;
    if (!animate) {
        ringTweens.delete(ring);
        writeRingOffset(ring, offset);
        return;
    }
    ringTweens.set(ring, { from: ring._nexusShown, to: offset, start: performance.now() });
    startMotionClock();
}

function writeRingOffset(ring, offset) {
    ring._nexusShown = offset;
    ring.style.strokeDashoffset = offset.toFixed(3);
}

// cubic-bezier(0.22, 1, 0.36, 1): the timing function of the former CSS transition.
function ringEase(x) {
    const curve = (t, a, b) => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;
    let low = 0, high = 1, t = x;
    for (let i = 0; i < 24; i++) {
        t = (low + high) / 2;
        if (curve(t, 0.22, 0.36) < x) low = t; else high = t;
    }
    return curve(t, 1, 1);
}

// Called on each animation tick; returns true while a ring is still easing.
function stepRingTweens(now) {
    ringTweens.forEach((tween, ring) => {
        const progress = (now - tween.start) / RING_TWEEN_MS;
        if (progress >= 1) {
            ringTweens.delete(ring);
            writeRingOffset(ring, tween.to);
        } else if (progress > 0) {
            writeRingOffset(ring, tween.from + (tween.to - tween.from) * ringEase(progress));
        }
    });
    return ringTweens.size > 0;
}

// Shows the target values at once (resume snap, static test snapshots).
function finishRingTweens() {
    ringTweens.forEach((tween, ring) => writeRingOffset(ring, tween.to));
    ringTweens.clear();
}

// Compact rate inside a map node: "16 Ko/s", "2,4 Mo/s", "38 Mo/s", "13 Go/s".
function formatRateShort(kbVal) {
    if (kbVal < 1024) return `${num(Math.round(kbVal))} ${KB}/s`;
    const mb = kbVal / 1024;
    if (mb < 1024) return `${num(mb, mb < 10 ? 1 : 0)} ${MB}/s`;
    const gb = mb / 1024;
    return `${num(gb, gb < 10 ? 1 : 0)} ${GB}/s`;
}

function formatNetSpeed(kbVal) {
    if (kbVal === 0) return `${num(0, 1)} ${KB}/s`;
    if (kbVal < 1024) return `${num(kbVal, 1)} ${KB}/s`;
    return `${num(kbVal / 1024, 1)} ${MB}/s`;
}

// Link speed reported by Windows, in KiB/s; 0 when unknown (no guessed speed).
function getNetworkLinkSpeedKb(stats) {
    const linkSpeedMbps = Number(stats.network.linkSpeedMbps);
    return linkSpeedMbps > 0 ? Math.round((linkSpeedMbps * 1000000) / 8 / 1024) : 0;
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

    // Cards showing a held (stale) reading get the existing stale badge.
    const held = new Set();
    const track = (card, reading) => { if (reading.stale && card) held.add(card); return reading; };

    // 1. Update circular telemetry gauges (Left Column)
    const cpuLoad = track(cpuCard, setGauge("cpu.util", cpuRing, cpuVal, cpuGaugeContainer, stats.cpu.utilization));
    const igpuLoad = track(vramCard, setGauge("igpu.util", vramRing, vramVal, vramGaugeContainer, stats.igpu.utilization));
    const npuLoad = track(npuCard, setGauge("npu.util", npuRing, npuVal, npuGaugeContainer, npuStats.utilization));
    const gpuLoad = track(gpuCard, setGauge("gpu.util", gpuRing, gpuVal, gpuGaugeContainer, stats.gpu.utilization));

    // 2. CPU Sub-metrics: busiest logical processor (the caches without that counter), then the
    // ACPI thermal zone beside it when Windows exposes one. That zone is the board's sensor, not
    // the CPU die (fixed at 28 °C on some desktops), hence its label "TEMPÉRATURE (ACPI)"; Windows
    // gives no other temperature without a driver. The clock moves down when the zone is shown.
    const cpuZone = cpuZoneShown(stats);
    if (NexusTelemetry.valid(stats.cpu.maxCoreUtilization) || lastValues.has('cpu.maxCore')) {
        const busiest = track(cpuCard, lastValues.take('cpu.maxCore', stats.cpu.maxCoreUtilization));
        setSubMetric(cpuTempSub, t('label.busiestCore'), cpuTempSub.textContent);
        setHeldText(cpuTempSub, busiest.stale ? null : `${Math.min(100, busiest.value)} %`);
    } else {
        setSubMetric(cpuTempSub, t("label.cacheL2L3"), `${formatCacheSize(stats.cpu.l2Cache)} / ${formatCacheSize(stats.cpu.l3Cache)}`);
        cpuTempSub.classList.remove('sensor-unavailable');
        cpuTempSub.title = '';
    }
    // Clock: empty when its counter could not be read. Processes / threads: -1 when unreadable
    // this tick (the last reading stays, greyed).
    const cpuClock = parseFloat(stats.cpu.freqGhz) > 0 ? `${num(stats.cpu.freqGhz)} GHz` : null;
    if (cpuZone) {
        track(cpuCard, setSensorMetric(cpuFreqSub, t('label.thermalZone'), stats.cpu.temp, '°C', stats, 'cpuTemperature', 'cpu.temp'));
        setSubMetric(cpuProcThreadsSub, t('label.clockSpeed'), cpuProcThreadsSub.textContent);
        cpuProcThreadsSub.title = '';
        setHeldText(cpuProcThreadsSub, cpuClock);
    } else {
        setSubMetric(cpuFreqSub, t('label.clockSpeed'), cpuFreqSub.textContent);
        cpuFreqSub.title = '';
        setHeldText(cpuFreqSub, cpuClock);
        setSubMetric(cpuProcThreadsSub, t('label.procThreads'), cpuProcThreadsSub.textContent);
        setHeldText(cpuProcThreadsSub, NexusTelemetry.valid(stats.totalProcesses) && NexusTelemetry.valid(stats.cpu.threads)
            ? `${stats.totalProcesses} / ${stats.cpu.threads}` : null);
    }
    if (cpuTopProcSub) {
        const top = (stats.topProcesses && stats.topProcesses.length > 0) ? stats.topProcesses[0] : null;
        const logical = Math.max(1, Number(stats.cpu.logical) || 1);
        const pct = top ? Math.min(99, Math.round((Number(top.PercentProcessorTime) || 0) / logical)) : 0;
        setTopProcess(cpuTopProcSub, top && top.Name ? { name: top.Name } : null, `${pct} %`);
    }

    // 3. GPU Sub-metrics (NVML when available, otherwise real Windows counters fill the slots)
    const dgpuVramUsed = lastValues.take("vram.usedMb", stats.vram.usedMb);
    const dgpuVramTotalGb = stats.vram.totalMb > 0 ? num(stats.vram.totalMb / 1024, 0) : "";
    const dgpuVramUsedGb = dgpuVramUsed.value !== null ? num(dgpuVramUsed.value / 1024, 1) : '—';
    track(gpuCard, setSensorMetric(gpuTempSub, t("label.temperature"), stats.gpu.temp, "°C", stats, "nvidia", "gpu.temp"));
    if (gpuVramSub) {
        const usedOrNull = NexusTelemetry.valid(stats.vram.usedMb) ? dgpuVramUsedGb : null;
        setHeldText(gpuVramSub, usedOrNull === null ? null
            : dgpuVramTotalGb ? `${usedOrNull} / ${dgpuVramTotalGb} ${GB}` : `${usedOrNull} ${GB}`);
        track(gpuCard, dgpuVramUsed);
    }
    track(gpuCard, setSensorMetric(gpuCoreSub, t("label.gpuPower"), stats.gpu.powerW, "W", stats, "nvidia", "gpu.powerW"));
    setAdapterTopProcess(gpuTopProcSub, stats.gpu.topProcess);
    // Live graphics clock in the card title, read like the CPU speed. NVML reports it; the WDDM
    // sensors of other GPUs do not, and a stale reading is not shown: no clock rather than an old one.
    const gpuCoreClock = NexusTelemetry.sensorState(stats, "nvidia") === "fresh" && NexusTelemetry.valid(stats.gpu.coreClock)
        && stats.gpu.coreClock > 0 ? Math.round(stats.gpu.coreClock) : 0;
    if (gpuVramBar && dgpuVramUsed.value !== null) {
        const gpuVramPct = stats.vram.totalMb > 0 ? Math.round((dgpuVramUsed.value / stats.vram.totalMb) * 100) : 0;
        setBarFill(gpuVramBar, gpuVramPct);
    }

    // 4. Circular telemetry gauges (Right Column - RAM, SSD, Network)
    // RAM circular gauge
    const ramLoad = track(ramCard, setGauge("ram.util", ramRing, ramVal, ramGaugeContainer, stats.ram.utilization));

    // The card gives the global state, like the GPU memory: in use / usable (Task Manager "In use"),
    // hard faults per second (Resource Monitor: reads from disk of pages that were not in RAM,
    // the sign that memory really runs short, 0 most of the time) and the speed the modules run at.
    const usedRamText = stats.ram.usedGb !== undefined && stats.ram.usedGb !== "" ? num(stats.ram.usedGb, 1) : null;
    const usableRamGb = Number(stats.ram.usedGb) + Number(stats.ram.availableGb);
    setHeldText(ramUsedSub, usedRamText !== null && usableRamGb > 0 ? `${usedRamText} / ${num(usableRamGb, 1)} ${GB}` : null);
    setHeldText(ramFaultsSub, NexusTelemetry.valid(stats.ram.hardFaults) ? num(stats.ram.hardFaults) : null);
    setHeldText(ramSpeedSub, stats.ram.speedMts > 0 ? `${stats.ram.speedMts} MT/s` : null);
    if (ramTopProcSub) {
        const topRam = stats.topRamProcess;
        const ramMb = topRam ? Number(topRam.ramMb) || 0 : 0;
        const ramText = ramMb >= 1024 ? `${num(ramMb / 1024, 1)} ${GB}` : `${ramMb} ${MB}`;
        setTopProcess(ramTopProcSub, topRam && topRam.name ? topRam : null, ramText);
    }

    // SSD card gauge: disk capacity occupation. The center map node shows live disk activity.
    track(ssdCard, setGauge("disk.storage", ssdRing, ssdVal, ssdGaugeContainer, stats.disk.storagePercent));
    // Space used, like the ring (the ring and the text used to read 41 % and "547 / 930 Go" free).
    const diskTotalGb = Number(stats.disk.totalGb) || 0;
    const diskUsedGb = Math.max(0, diskTotalGb - (Number(stats.disk.freeGb) || 0));
    ssdFreeSub.textContent = diskTotalGb > 0 ? `${num(diskUsedGb, 1)} / ${num(diskTotalGb, 0)} ${GB}` : '—';
    if (ssdReadSub && ssdWriteSub) {
        // Formatter with dynamic unit support
        // Throughput text from the host, or the number -1 when the counters could not be read.
        const formatSpeed = (mbStr) => {
            const val = parseFloat(mbStr);
            if (!NexusTelemetry.valid(val)) return null;
            if (val >= 1000) return `${num(val / 1024, 1)} ${GB}/s`;
            return `${num(val, 1)} ${MB}/s`;
        };
        setHeldText(ssdReadSub, formatSpeed(stats.disk.readMb));
        setHeldText(ssdWriteSub, formatSpeed(stats.disk.writeMb));
    }
    if (ssdResponseSub) {
        setHeldText(ssdResponseSub, NexusTelemetry.valid(stats.disk.responseTimeMs) ? `${num(stats.disk.responseTimeMs, 1)} ms` : null);
    }

    // Network circular gauge & absolute speed rendering
    // -1: rates unreadable this tick; the gauge then counts nothing and the texts hold the last reading.
    const netRatesValid = NexusTelemetry.valid(stats.network.lan) && NexusTelemetry.valid(stats.network.wifi);
    const totalNetKb = netRatesValid ? stats.network.lan + stats.network.wifi : 0;

    // Dynamic value display
    if (!netRatesValid) {
        if (!netVal.dataset.seen) setHtmlIfChanged(netVal, `—<span>${KB}/s</span>`);
    } else if (totalNetKb === 0) {
        setHtmlIfChanged(netVal, `0<span>${KB}/s</span>`);
    } else if (totalNetKb < 1024) {
        setHtmlIfChanged(netVal, `${num(totalNetKb)}<span>${KB}/s</span>`);
    } else {
        setHtmlIfChanged(netVal, `${num(totalNetKb / 1024, 1)}<span>${MB}/s</span>`);
    }

    // Scale the card ring against the real link speed (2.5 Gb/s Ethernet is about 305000 KiB/s).
    const linkSpeedKb = getNetworkLinkSpeedKb(stats);
    const netPercent = totalNetKb > 0 && linkSpeedKb > 0 ? Math.min(100, Math.max(1, Math.round((totalNetKb / linkSpeedKb) * 100))) : 0;
    setCircularProgress(netRing, netPercent);
    setGaugeAria(netGaugeContainer, { value: netPercent, stale: false });

    // Dynamic formatting for Send / Receive speeds

    if (netRatesValid) netVal.dataset.seen = '1';
    setHeldText(netWifiSub, NexusTelemetry.valid(stats.network.wifi) ? formatNetSpeed(stats.network.wifi) : null); // RECEVOIR
    setHeldText(netLanSub, NexusTelemetry.valid(stats.network.lan) ? formatNetSpeed(stats.network.lan) : null);   // ENVOYER
    if (netTypeSub) {
        const wifiSignal = Number(stats.network.signal);
        if (stats.network.type === "Wi-Fi" && wifiSignal >= 0) {
            setSubMetric(netTypeSub, t("label.wifiSignal"), `${wifiSignal} %`);
        } else {
            setSubMetric(netTypeSub, t("label.connectionType"), stats.network.type === "none" ? t("network.offline") : stats.network.type.toUpperCase());
        }
    }
    if (netPingSub) {
        const pingFresh = NexusTelemetry.valid(stats.ping);
        if (!setHeldText(netPingSub, pingFresh
            ? `${stats.ping} ms${stats.pingVia === "gateway" ? ` (${t("ping.router")})` : ""}` : null, "-- ms")) {
            netPingSub.title = stats.pingVia === "gateway" && pingFresh ? t("ping.routerTitle") : "";
        }
    }

    // 5. Update iGPU VRAM Sub-metrics
    const igpuUsed = track(vramCard, lastValues.take("igpu.usedMb", stats.igpu.usedMb));
    // Task Manager's two iGPU memories, each in use / size: shared (system RAM the iGPU borrows)
    // and dedicated (its reserved carve-out). The bar shows both together, like the ring of a GPU.
    const sharedUsed = Number(stats.igpu.sharedUsedMb), sharedTotal = Number(stats.igpu.sharedTotalMb);
    setHeldText(vramUsedSub, NexusTelemetry.valid(sharedUsed) && sharedTotal > 0
        ? `${num(sharedUsed / 1024, 1)} / ${num(sharedTotal / 1024, 0)} ${GB}` : null);
    setAdapterTopProcess(igpuTopProcSub, stats.igpu.topProcess);
    // Same slots as the GPU card: the driver's temperature when it reports one (WDDM), otherwise
    // the dedicated memory; then the busier of the two video engines (decode, encode), measured
    // like the load ring: 0 % except while a video plays or is recorded.
    if (igpuTempSub) {
        if (NexusTelemetry.valid(stats.igpu.temp) || lastValues.has("igpu.temp")) {
            const temp = track(vramCard, lastValues.take("igpu.temp", stats.igpu.temp));
            setSubMetric(igpuTempSub, t("label.temperature"), igpuTempSub.textContent);
            setHeldText(igpuTempSub, temp.stale ? null : `${num(temp.value)} °C`);
        } else {
            const dedicatedUsed = Number(stats.igpu.dedicatedUsedMb), dedicatedTotal = Number(stats.igpu.dedicatedTotalMb);
            setSubMetric(igpuTempSub, t("label.dedicatedMemory"), igpuTempSub.textContent);
            setHeldText(igpuTempSub, NexusTelemetry.valid(dedicatedUsed) && dedicatedTotal > 0
                ? `${num(dedicatedUsed)} / ${num(dedicatedTotal)} ${MB}` : null);
        }
    }
    if (igpuVideoSub) {
        const videoFresh = NexusTelemetry.valid(stats.igpu.decodeUtil) && NexusTelemetry.valid(stats.igpu.encodeUtil);
        if (setHeldText(igpuVideoSub, videoFresh ? `${Math.min(100, Math.max(stats.igpu.decodeUtil, stats.igpu.encodeUtil))} %` : null)) held.add(vramCard);
    }
    if (igpuVramBar && igpuUsed.value !== null) {
        const igpuVramPct = Math.round((igpuUsed.value / stats.igpu.totalMb) * 100) || 0;
        setBarFill(igpuVramBar, igpuVramPct);
    }
    const npuUsed = track(npuCard, lastValues.take("npu.usedMb", npuStats.usedMb));
    const npuUsedFresh = NexusTelemetry.valid(npuStats.usedMb);
    const npuUsedGb = npuUsed.value !== null ? num(npuUsed.value / 1024, 1) : '—';
    const npuTotalMb = Number(npuStats.totalMb) || 0;
    const npuTotalGb = npuTotalMb > 0 ? num(npuTotalMb / 1024, 0) : "0";
    setHeldText(npuUsedSub, npuUsedFresh ? `${npuUsedGb} ${GB}` : null);
    // Which program uses the NPU right now (null: no reading, empty name: measured idle).
    setAdapterTopProcess(npuTopProcSub, npuStats.topProcess);
    setHeldText(npuFreeSub, npuUsedFresh && npuTotalMb > 0 ? `${num(Math.max(0, npuTotalMb - npuStats.usedMb) / 1024, 1)} ${GB}` : null);
    if (npuTotalSub) {
        npuTotalSub.textContent = `${npuTotalGb} ${GB}`;
    }
    if (npuUtilBar && npuUsed.value !== null) {
        const npuMemPct = Math.round((npuUsed.value / npuTotalMb) * 100) || 0;
        setBarFill(npuUtilBar, npuMemPct);
    }

    // 6. Card titles: kind + short model name ("GPU RTX 4080 SUPER · 1245 MHz", "RAM DDR5 · 6400 MT/s");
    // the size and use of each part are in the card itself.
    if (stats.cpu.name && specCpuName) specCpuName.textContent = titleWithKind("CPU", stats.cpu.name);
    if (npuStats.name && specNpuName) specNpuName.textContent = titleWithKind("NPU", npuStats.name);
    if (stats.gpu.name && specGpuName) {
        const gpuBase = titleWithKind("GPU", stats.gpu.name);
        const gpuTitle = gpuBase + (gpuCoreClock ? ` · ${gpuCoreClock} MHz` : "");
        if (specGpuName.textContent !== gpuTitle) specGpuName.textContent = gpuTitle;
        // Titles are sized for the widest clock, so the live value never resizes them every 5 s.
        const nvml = stats.sources && stats.sources.nvidia && stats.sources.nvidia.provider === "nvml";
        const fitText = gpuCoreClock || nvml ? `${gpuBase} · 8888 MHz` : gpuBase;
        if (specGpuName.dataset.fitText !== fitText) specGpuName.dataset.fitText = fitText;
    }
    if (stats.ram.totalGb && specRamName) {
        specRamName.textContent = "RAM" + (stats.ram.type ? ` ${stats.ram.type}` : "");
    }
    if (stats.network.name && specNetMode) specNetMode.textContent = `${t("label.network")} ${shortModelName(stats.network.name)}`;
    if (stats.igpu.name && specVramName) specVramName.textContent = titleWithKind("iGPU", stats.igpu.name);
    if (specSsdName) specSsdName.textContent = stats.disk.name ? titleWithKind("SSD", stats.disk.name) : "SSD";
    if (stats.motherboard && clockMb) {
        clockMb.textContent = `${t("label.motherboard")} ${stats.motherboard}`;
    }
    if (clockBattery) {
        const battery = stats.battery;
        if (battery && battery.present && battery.percent >= 0) {
            const batteryText = formatBattery(battery);
            if (clockBattery.textContent !== batteryText) {
                clockBattery.textContent = batteryText;
            }
            clockBattery.hidden = false;
        } else {
            clockBattery.hidden = true;
        }
    }
    // Windows energy saver: hold decorative motion still; values keep updating. The canvas
    // keeps its former 12 Hz on battery and under energy saver.
    document.body.classList.toggle("energy-saver", stats.energySaver === true);
    setMotionEconomy(stats.energySaver === true || (stats.battery && stats.battery.present === true && stats.battery.ac === false));
    startMotionClock();
    // 7. Update Holographic Neural Telemetry Map live parameters
    const diskActivity = lastValues.take("disk.util", stats.disk.utilization);
    if (telemetryNodes.npu) {
        // Held readings stay on the map, drawn as stale; never-seen ones show "—".
        for (const node of telemetryNodeList) node.stale = false;
        telemetryNodes.npu.value = stats.totalProcesses;

        // Each orbit node repeats the main reading of its card, so the map and the cards agree.
        telemetryNodes.cpu.value = cpuLoad.value;
        telemetryNodes.cpu.stale = cpuLoad.stale;

        telemetryNodes.igpu.value = igpuLoad.value;
        telemetryNodes.igpu.stale = igpuLoad.stale;

        telemetryNodes.npuAccel.value = npuLoad.value;
        telemetryNodes.npuAccel.stale = npuLoad.stale;

        // Memory in use, as the RAM ring (it used to show a mix of CPU load, disk activity and
        // page faults labelled "RAM activity", 37 % next to a 61 % ring).
        telemetryNodes.ram.value = ramLoad.value;
        telemetryNodes.ram.stale = ramLoad.stale;

        telemetryNodes.gpu.value = gpuLoad.value;
        telemetryNodes.gpu.stale = gpuLoad.stale;

        // Every orbit node is a live use of its real capacity, in %. SSD: Task Manager's active time
        // (share of the time the disk is busy; a share of the PCIe link bandwidth would read a few %
        // for a disk saturated by small random reads).
        if (stats.disk.drive) telemetryNodes.ssd.label = `SSD ${stats.disk.drive}`;
        telemetryNodes.ssd.value = diskActivity.value;
        telemetryNodes.ssd.stale = diskActivity.stale;

        // Network: Task Manager's link utilization, received + sent over the speed Windows reports
        // for the link (2.5 Gb/s here); "—" when Windows gives no link speed. The square root of
        // that share only paces the packets, so everyday traffic still shows some.
        const linkKb = getNetworkLinkSpeedKb(stats);
        const linkPct = netRatesValid && linkKb > 0 ? Math.min(100, (totalNetKb / linkKb) * 100) : null;
        telemetryNodes.net.value = linkPct !== null ? Math.round(linkPct) : null;
        telemetryNodes.net.activity = linkPct > 0 ? Math.min(100, Math.max(1, Math.round(Math.sqrt(linkPct / 100) * 100))) : 0;
        pushCardHistory("net-card", telemetryNodes.net.activity);
    }

    pushCardHistory("cpu-card", stats.cpu.utilization);
    pushCardHistory("vram-card", stats.igpu.utilization);
    pushCardHistory("npu-card", npuStats.utilization);
    pushCardHistory("gpu-card", stats.gpu.utilization);
    pushCardHistory("ram-card", stats.ram.utilization);
    pushCardHistory("ssd-card", stats.disk.utilization);

    updatePowerPlanButtons(stats);

    updateLoadAlerts(stats, performance.now());
    heldReadingCards = held;
    refreshTelemetryFreshness(true);

    // Spawn data packets per node so every active circle emits fairly.
    const now = performance.now();
    let spawnedPacket = false;
    let mapValueChanged = false;
    Object.keys(telemetryNodes).forEach(key => {
        if (key === "npu") return;
        const node = telemetryNodes[key];
        if (node.visible === false) return;
        // A held (stale) reading is not live activity: no data packets for it.
        const pct = node.stale ? 0 : nodeActivity(node);
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

    // A renamed plan keeps its GUID: the name is part of the key so its button is rebuilt too.
    const currentGuids = Array.from(powerPlansContainer.children).map(btn => btn.dataset.guid + '|' + btn.title).join(',');
    const newGuids = stats.powerPlans.map(p => p.guid + '|' + p.name).join(',');
    if (currentGuids !== newGuids) {
        powerPlansContainer.innerHTML = "";
        stats.powerPlans.forEach(plan => {
            const button = document.createElement("button");
            button.className = "remote-btn";
            button.dataset.guid = plan.guid;
            button.title = plan.name;
            button.setAttribute("aria-label", t("power.aria", { name: plan.name }));

            // Icon from the kind resolved by the host (plan GUID, then Windows personality)
            let iconSvg = "";
            const kind = powerPlanKind(plan);
            button.dataset.kind = kind;
            if (kind === "performance") {
                iconSvg = `<svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor"><path d="M12 2L1 21h22L12 2zm0 4l7.5 13h-15L12 6zm-1 5h2v4h-2v-4zm0-3h2v2h-2V8z"/></svg>`;
            } else if (kind === "eco") {
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
        if (powerPlanFitObserver && !powerPlansContainer.dataset.fitObserved) {
            powerPlansContainer.dataset.fitObserved = '1';
            powerPlanFitObserver.observe(powerPlansContainer);
        }
    }
    // Every update: the base font follows the viewport (clamp on --lvw) even when the container keeps its size.
    fitPowerPlanLabels();

    // Update active classes
    currentPowerPlan = "";
    Array.from(powerPlansContainer.children).forEach(button => {
        const guid = button.dataset.guid;
        const plan = stats.powerPlans.find(p => p.guid === guid);
        if (plan && plan.active) {
            currentPowerPlan = guid;
            button.className = "remote-btn active";
            button.setAttribute("aria-pressed", "true");
            button.id = powerPlanButtonId(powerPlanKind(plan));
        } else {
            button.className = "remote-btn";
            button.setAttribute("aria-pressed", "false");
            button.id = "";
        }
    });
}

// Width available / width of the text on one line (a range: scrollWidth does not report a
// single nowrap line spilling over).
function titleFitRatio(title) {
    titleRange.selectNodeContents(title);
    const textWidth = titleRange.getBoundingClientRect().width;
    return title.clientWidth && textWidth > title.clientWidth ? title.clientWidth / textWidth : 1;
}

// Model name without what the card already says: trademark marks, the chip makers' names and
// generic words ("Intel(R) Core(TM) i7-14700K" -> "Core i7-14700K", "NVIDIA GeForce RTX 4080 SUPER"
// -> "RTX 4080 SUPER", "Intel(R) UHD Graphics 770" -> "UHD 770", "Intel(R) Ethernet Controller I226-V" -> "Ethernet I226-V").
function shortModelName(name) {
    return String(name)
        .replace(/\s*(\((R|TM|C)\)|[®™©])/gi, " ")
        .replace(/\s+(CPU\s+)?@\s+[\d.]+\s*GHz\b/i, " ")
        .replace(/\b(Intel|AMD|NVIDIA|GeForce|Corporation|Controller|Family|Adapter|Processor|Graphics|\d+-Core)\b/gi, " ")
        .replace(/\s+/g, " ").trim();
}

// "GPU RTX 4080 SUPER": the kind once, never doubled ("SSD Samsung SSD 990 PRO" -> "SSD Samsung 990 PRO").
// A name made only of the maker and generic words ("Intel(R) Graphics") keeps the maker: "iGPU Intel".
function titleWithKind(kind, name) {
    const model = (text) => text.split(" ").filter(word => word && word.toLowerCase() !== kind.toLowerCase());
    let words = model(shortModelName(name));
    if (!words.length) words = model(String(name).replace(/\s*(\((R|TM|C)\)|[®™©])/gi, " ").replace(/(^|\s)Graphics(?=\s|$)/gi, " ").replace(/\s+/g, " ").trim());
    return [kind, ...words].join(" ");
}

// One size for every card title (the iGPU and NPU titles had a fixed 13 px of their own): the
// stylesheet size, reduced just enough for the longest title of the narrowest card to fit on
// its line. Runs when a title changes or a card is resized.
let cardTitleKey = "";
const titleRange = document.createRange();
function fitCardTitles(force = false) {
    const titles = [...document.querySelectorAll(".gauge-card .gauge-label")].filter(title => title.getClientRects().length > 0);
    const key = titles.map(title => (title.dataset.fitText || title.textContent) + "/"
        + title.parentElement.querySelector(".critical-alert").textContent).join("|");
    if (!force && key === cardTitleKey) return;
    cardTitleKey = key;
    // Measured with the widest text each title can take (see the GPU clock), on the full width of
    // the heading: a status badge wraps under the title rather than shrinking every title.
    const shown = titles.map(title => title.textContent);
    const headings = titles.map(title => title.parentElement);
    headings.forEach(heading => heading.classList.add("fitting-title"));
    titles.forEach(title => {
        title.style.fontSize = "";
        title.classList.remove("title-wraps");
        if (title.dataset.fitText) title.textContent = title.dataset.fitText;
    });
    let size = Infinity;
    for (const title of titles) size = Math.min(size, parseFloat(getComputedStyle(title).fontSize));
    if (!Number.isFinite(size)) return;
    // Letter spacing does not shrink with the font: a few passes converge on the fitting size.
    for (let pass = 0; pass < 4; pass++) {
        titles.forEach(title => { title.style.fontSize = `${size}px`; });
        const ratio = Math.min(1, ...titles.map(titleFitRatio));
        if (ratio >= 1 || size <= 8) break;
        size = Math.max(8, Math.floor(size * ratio * 0.98 * 10) / 10);
    }
    headings.forEach(heading => heading.classList.remove("fitting-title"));
    // A title still too long, at 8 px on very small windows or beside the badge of a short card
    // (whose heading stays on one row), wraps between words instead of being cut.
    titles.forEach(title => { if (titleFitRatio(title) < 1) title.classList.add("title-wraps"); });
    titles.forEach((title, index) => { if (title.textContent !== shown[index]) title.textContent = shown[index]; });
}
const cardTitleObserver = typeof ResizeObserver === "function" ? new ResizeObserver(() => fitCardTitles(true)) : null;
if (cardTitleObserver) document.querySelectorAll(".gauge-card").forEach(card => cardTitleObserver.observe(card));

// Shrinks a plan label just enough for its longest word to fit on one line, so a long name wraps
// between words ("PERFORMANCES / OPTIMALES") instead of inside one ("PERFORMANCE / S OPTIMALES").
const labelMeasure = document.createElement("canvas").getContext("2d");
function fitPowerPlanLabels() {
    if (!powerPlansContainer) return;
    powerPlansContainer.querySelectorAll(".btn-lbl").forEach(label => {
        label.style.fontSize = "";
        const width = label.clientWidth;
        if (!width) return;
        const style = getComputedStyle(label);
        const size = parseFloat(style.fontSize);
        labelMeasure.font = `${style.fontWeight} ${size}px ${style.fontFamily}`;
        const spacing = parseFloat(style.letterSpacing) || 0;
        const widest = Math.max(...label.textContent.split(/\s+/).filter(Boolean)
            .map(word => labelMeasure.measureText(word).width + spacing * word.length));
        // 2 % margin for subpixel rounding; overflow-wrap stays the last resort below 8 px.
        if (widest > width) label.style.fontSize = Math.max(8, Math.floor(size * width / widest * 0.98 * 10) / 10) + "px";
    });
}
const powerPlanFitObserver = typeof ResizeObserver === 'function' ? new ResizeObserver(() => fitPowerPlanLabels()) : null;

// Kind sent by the host (plan GUID / Windows personality / power mode); the name is only a
// fallback for payloads without it.
function powerPlanKind(plan) {
    if (plan && (plan.kind === "eco" || plan.kind === "balanced" || plan.kind === "performance")) return plan.kind;
    const words = String(plan && plan.name || "").normalize("NFD").replace(/[̀-ͯ]/g, "").toLowerCase().split(/[^a-z0-9]+/);
    const has = list => list.some(word => words.includes(word));
    if (has(["economie", "economies", "eco", "saver", "silent", "silencieux", "quiet", "veille", "efficiency", "efficacite"])) return "eco";
    if (has(["performance", "performances", "extreme", "extremes", "gaming", "gamer", "jeu", "jeux", "turbo", "ultimate"])) return "performance";
    return "balanced";
}

function powerPlanButtonId(kind) {
    return kind === "performance" ? "btn-extreme" : kind === "eco" ? "btn-eco" : "btn-balanced";
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
                button.id = powerPlanButtonId(button.dataset.kind
                    || powerPlanKind({ name: button.querySelector('.btn-lbl').textContent }));
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

// Sensor sub-metric: a missing or stale sample keeps the last fresh value, greyed;
// a sensor that never produced a value shows "—".
function setSensorMetric(element, label, value, unit, stats, source, key) {
    const state = NexusTelemetry.sensorState(stats, source);
    const reading = lastValues.take(key, value, state === 'fresh');
    const usable = reading.value !== null && !reading.stale;
    setSubMetric(element, label, reading.value !== null ? `${num(reading.value)} ${unit}` : '—');
    element.classList.toggle('sensor-unavailable', !usable);
    element.title = usable ? (unit === 'W' ? t('sensor.boardPower') : '')
        : reading.stale ? t('sensor.held') : state === 'stale' ? t('sensor.stale') : t('sensor.missing');
    return reading;
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
            : card === cpuCard && cpuZoneShown(lastTelemetry) ? NexusTelemetry.sensorState(lastTelemetry, 'cpuTemperature') : 'fresh';
        const stale = expired || status === 'stale' || heldReadingCards.has(card);
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
    fitCardTitles(); // a badge can leave its title less room
    if (expired && !telemetryExpired) {
        telemetryExpired = true;
        loadAlerts.forEach(alert => alert.reset());
        // Keep the last readings on screen so the image does not jump; the "data expired"
        // badge, the stale radar values and the accessible values say they are old.
        for (const gauge of document.querySelectorAll('[role="progressbar"][aria-valuenow]')) {
            gauge.setAttribute('aria-valuetext', t('aria.stale', { value: `${num(Number(gauge.getAttribute('aria-valuenow')))} %` }));
        }
        for (const node of telemetryNodeList) node.stale = true;
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
        `<stop offset="0%" stop-color="${color}" stop-opacity="0.12"/><stop offset="100%" stop-color="${color}" stop-opacity="0"/></linearGradient></defs>` +
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
