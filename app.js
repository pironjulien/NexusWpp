// Entry point: host messages, startup and the power-panel bounds sent to the host.
"use strict";

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
