// Test-only boundary data. This file is never packaged with the application.
window.layoutFixture = function (test) {
    window.layoutExpectedDpr = test.dpr;
    const full = test.hardware !== 'minimal';
    const igpu = ['full', 'integrated'].includes(test.hardware);
    const npu = ['full', 'npu-only'].includes(test.hardware);
    const dgpu = ['full', 'minimal'].includes(test.hardware);
    window.layoutExpectedCards = 4 + Number(igpu) + Number(npu) + Number(dgpu);
    const planNames = ['Utilisation normale', 'Économies d’énergie', 'Performances élevées', 'Performances optimales'];
    const stats = {
        cpu: { utilization: 48, temp: 65, name: 'Intel Core Ultra 9 285K', freqGhz: 5.7, threads: 5462, logical: 24 },
        igpu: { utilization: 35, detected: igpu, name: 'Intel Graphics', usedMb: 2048, totalMb: 32768, decodeUtil: 24 },
        npu: { utilization: 68, detected: npu, name: 'Intel AI Boost', usedMb: 2048, totalMb: 32768 },
        gpu: { utilization: 73, detected: dgpu, name: 'NVIDIA GeForce RTX 5090', temp: 76, powerW: 575, coreClock: 2875, memoryClock: 15001 },
        vram: { usedMb: 24576, totalMb: 32768 },
        ram: { utilization: 72, totalGb: 128, speedMts: 6400, type: 'DDR5', modules: 4, cachedGb: 32.4, commitUsedGb: 100.5, commitLimitGb: 192.8 },
        disk: { name: 'Samsung SSD 990 PRO 4TB', utilization: 58, storagePercent: 65, freeGb: 1234.5, totalGb: 3725.9, readMb: 7250.5, writeMb: 6300.4, responseTimeMs: 12.6 },
        network: { name: 'Realtek Gaming 2.5GbE Family Controller', type: 'Ethernet', lan: 32768, wifi: 262144, linkSpeedMbps: 2500 },
        motherboard: 'ASUSTeK ROG MAXIMUS Z890 HERO',
        battery: { present: full, percent: 100, ac: true },
        topProcesses: [{ Name: 'ApplicationFrameHost', PercentProcessorTime: 456 }],
        topRamProcess: { name: 'ApplicationFrameHost', ramMb: 12288 },
        totalProcesses: 456, ping: 124,
        powerPlans: planNames.slice(0, test.plans || 4).map((name, index) => ({ name, guid: 'layout-test-' + index, active: index === 0 }))
    };
    if (test.state === 'high') {
        for (const key of ['cpu','igpu','npu','gpu','ram']) stats[key].utilization = 96;
    } else if (test.state === 'stale') {
        stats.sources = { nvidia: { status: 'stale' }, windowsGpu: { status: 'stale' } };
        stats.gpu.temp = stats.gpu.powerW = stats.gpu.memoryClock = -1;
    }
    updateDOM(stats);
    if (typeof loadAlerts !== 'undefined') {
        loadAlerts.forEach(alert => alert.reset());
        updateLoadAlerts(stats, 1000);
        updateLoadAlerts(stats, 6000);
        refreshTelemetryFreshness(true);
    }
    // Settle telemetry transitions in this static boundary-data snapshot.
    document.getAnimations().filter(animation => animation instanceof CSSTransition).forEach(animation => animation.finish());
    setRuntimeSuspended(true);
};

window.inspectLayout = function () {
    const issues = [];
    const visible = element => element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden';
    const rect = element => element.getBoundingClientRect();
    const name = element => element.id || element.className;
    const inside = (a, b, tolerance = 1.5) => a.left >= b.left - tolerance && a.top >= b.top - tolerance && a.right <= b.right + tolerance && a.bottom <= b.bottom + tolerance;
    const viewport = { left: 0, top: 0, right: innerWidth, bottom: innerHeight };
    const dashboard = rect(document.querySelector('.dashboard'));
    // Room for desktop icon labels above and a 48 CSS px taskbar plus clearance below.
    if (dashboard.top < 96 - 1 || innerHeight - dashboard.bottom < 60 - 1) issues.push('missing desktop icon/taskbar safe area');
    const cards = [...document.querySelectorAll('.gauge-card, .column-center')].filter(visible);
    if (cards.length !== window.layoutExpectedCards + 1) issues.push('missing hardware cards');
    for (const element of cards) {
        if (!inside(rect(element), viewport)) issues.push(name(element) + ': outside viewport');
    }
    for (const element of document.querySelectorAll('.gauge-label, .critical-alert, .settings-button, .gauge-container, .gauge-value, .sub-metric, .sub-metric .lbl, .sub-metric .val, .sub-progress-container, .clock-time, .clock-date, .clock-mb, .clock-battery, .remote-btn, .btn-lbl, #physics-canvas')) {
        if (!visible(element)) continue;
        const bounds = rect(element);
        if (!inside(bounds, viewport)) issues.push(name(element) + ': outside viewport');
        for (let parent = element.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
            if (/(hidden|clip|auto|scroll)/.test(getComputedStyle(parent).overflow) && !inside(bounds, rect(parent))) {
                issues.push(name(element) + ': clipped by ' + name(parent));
                break;
            }
        }
        if (element.matches('.critical-alert, .settings-button, .sub-metric .lbl, .sub-metric .val, .btn-lbl, .clock-date, .clock-mb, .clock-battery')) {
            const range = document.createRange();
            range.selectNodeContents(element);
            for (const line of range.getClientRects()) {
                if (!inside(line, bounds, 2)) { issues.push(name(element) + ': text overflow'); break; }
            }
        }
        if (element.matches('.gauge-container')) {
            if (Math.abs(bounds.width - bounds.height) > 1.5) issues.push(name(element) + ': distorted gauge');
            if (bounds.width < 30 || bounds.height < 30) issues.push(name(element) + ': unreadable gauge');
        }
        if (element.matches('.gauge-value') && !inside(bounds, rect(element.closest('.gauge-slot')))) issues.push(name(element) + ': outside gauge slot');
    }
    for (const card of cards.filter(card => card.matches('.gauge-card'))) {
        const badge = card.querySelector('.critical-alert');
        if (!badge || !visible(badge)) continue;
        const a = rect(badge);
        for (const other of card.querySelectorAll('.gauge-label,.gauge-container,.sub-metric')) {
            const b = rect(other);
            if (Math.min(a.right,b.right)-Math.max(a.left,b.left)>1 && Math.min(a.bottom,b.bottom)-Math.max(a.top,b.top)>1) issues.push(name(card)+': status overlaps '+name(other));
        }
    }
    for (let i = 0; i < cards.length; i++) for (let j = i + 1; j < cards.length; j++) {
        const a = rect(cards[i]), b = rect(cards[j]);
        if (Math.min(a.right, b.right) - Math.max(a.left, b.left) > 2 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 2) issues.push(name(cards[i]) + ': overlaps ' + name(cards[j]));
    }
    const nodes = telemetryNodeList.filter(node => node.visible !== false);
    for (const node of nodes) {
        const bounds = { left: node.x - node.radius - 12, top: node.y - node.radius - 12,
            right: node.x + node.radius + 12, bottom: node.y + node.radius + (node === telemetryNodes.npu ? 12 : 38) };
        if (!inside(bounds, { left: 0, top: 0, right: width, bottom: height })) issues.push('radar: clipped ' + node.label);
    }
    const canvasBounds = rect(canvas);
    const node = telemetryNodes.cpu;
    canvas.dispatchEvent(new MouseEvent('mousedown', { clientX: canvasBounds.left + node.x * canvasBounds.width / width,
        clientY: canvasBounds.top + node.y * canvasBounds.height / height, bubbles: true }));
    if (mouse.grabbedNode !== node) issues.push('radar: scaled pointer missed node');
    window.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
    if (innerWidth !== screen.width || Math.abs(devicePixelRatio - window.layoutExpectedDpr) > .01) issues.push('unexpected emulation metrics');
    if (!runtimeSuspended || canvasFrameTimer || canvasAnimationFrame) issues.push('resize restarted paused animation');
    return { issues: [...new Set(issues)], viewport: { width: innerWidth, height: innerHeight, dpr: devicePixelRatio },
        desktopInsets: { top: dashboard.top, bottom: innerHeight - dashboard.bottom },
        cards: cards.map(element => ({ id: name(element), bounds: rect(element).toJSON() })),
        remoteBounds: rect(document.querySelector('.remote-sub-panel')).toJSON(),
        overflowDetails: issues.length ? [...document.querySelectorAll('#gpu-card, #ssd-card, #gpu-card > *, #ssd-card > *, #gpu-card .sub-progress-container, #ssd-card .sub-metric')].filter(visible).map(element => ({
            id: name(element), bounds: rect(element).toJSON(), rows: getComputedStyle(element).gridTemplateRows,
            fontSize: getComputedStyle(element).fontSize, padding: getComputedStyle(element).padding
        })) : [],
        canvas: { width, height }, plans: document.querySelectorAll('.remote-btn').length,
        paused: runtimeSuspended && !canvasFrameTimer && !canvasAnimationFrame };
};
