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
