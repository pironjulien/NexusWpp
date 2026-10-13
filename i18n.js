// Interface language: French on French Windows, English everywhere else.
// WebView2 reports the Windows display language through navigator.language.
"use strict";
const NexusI18n = (() => {
    const STRINGS = {
        fr: {
            "label.temperature": "TEMPÉRATURE",
            "label.thermalZone": "ZONE THERMIQUE",
            "label.cacheL2L3": "CACHE L2 / L3",
            "label.topProcess": "TOP PROCESSUS",
            "label.clockSpeed": "CADENCE",
            "label.procThreads": "PROC / THREADS",
            "label.videoDecode": "DÉCODAGE VIDÉO",
            "label.sharedVram": "VRAM PARTAGÉE",
            "label.freeVram": "VRAM LIBRE",
            "label.totalVram": "VRAM TOTALE",
            "label.memoryUsed": "MÉMOIRE UTILISÉE",
            "label.sharedMemory": "MÉMOIRE PARTAGÉE",
            "label.freeMemory": "MÉMOIRE LIBRE",
            "label.totalMemory": "MÉMOIRE TOTALE",
            "label.gpuPower": "PUISSANCE GPU",
            "label.memoryClock": "FRÉQ. MÉMOIRE",
            "label.available": "DISPONIBLE",
            "label.cached": "MISE EN CACHE",
            "label.virtualMemory": "MÉMOIRE VIRTUELLE",
            "label.diskSpace": "ESPACE DISQUE",
            "label.read": "LECTURE",
            "label.write": "ÉCRITURE",
            "label.responseTime": "TEMPS DE RÉPONSE",
            "label.network": "RÉSEAU",
            "label.download": "RECEVOIR (DÉBIT)",
            "label.upload": "ENVOYER (DÉBIT)",
            "label.connectionType": "TYPE DE CONNEXION",
            "label.wifiSignal": "SIGNAL WI-FI",
            "label.ping": "PING RÉSEAU",
            "label.motherboard": "CARTE MÈRE",
            "node.cpuLoad": "CHARGE CPU",
            "node.igpuLoad": "CHARGE iGPU",
            "node.npuLoad": "CHARGE NPU",
            "node.ramActivity": "ACTIVITÉ RAM",
            "node.gpuLoad": "CHARGE GPU",
            "node.diskActivity": "ACTIVITÉ DISQUE",
            "node.networkActivity": "ACTIVITÉ RÉSEAU",
            "node.processes": "{n} PROCESSUS",
            "node.diskFree": "{gb} libre",
            "ram.modules": "{n} barrettes",
            "battery.status": "BATTERIE {pct} % · {source}",
            "battery.ac": "SECTEUR",
            "battery.dc": "SUR BATTERIE",
            "ping.router": "routeur",
            "ping.routerTitle": "Internet ne répond pas au ping : latence mesurée jusqu'au routeur local.",
            "power.aria": "Mode d'alimentation {name}",
            "sensor.boardPower": "Puissance de la carte entière, pas celle de NexusWpp.",
            "sensor.stale": "Dernière lecture périmée : mesure ignorée.",
            "sensor.missing": "Ce capteur ne fournit pas de mesure.",
            "sensor.held": "Dernière valeur connue : le capteur ne répond plus.",
            "alert.highLoad": "Charge élevée",
            "alert.memoryFull": "Mémoire très utilisée",
            "alert.highLoadTitle": "Au moins 90 % pendant 5 s. Retour à la normale sous 80 % pendant 5 s. Ce n’est pas un diagnostic de panne.",
            "alert.dataExpired": "Données périmées",
            "alert.sensorsStale": "Capteurs périmés",
            "aria.unavailable": "Mesure indisponible",
            "aria.waiting": "En attente d’une mesure",
            "aria.stale": "{value}, dernière valeur connue (périmée)",
            "aria.map": "Carte interactive de télémétrie matérielle affichant les liens et les flux de données entre composants sous forme de particules animées.",
            "unit.GB": "Go", "unit.MB": "Mo", "unit.KB": "Ko"
        },
        en: {
            "label.temperature": "TEMPERATURE",
            "label.thermalZone": "THERMAL ZONE",
            "label.cacheL2L3": "L2 / L3 CACHE",
            "label.topProcess": "TOP PROCESS",
            "label.clockSpeed": "CLOCK SPEED",
            "label.procThreads": "PROCS / THREADS",
            "label.videoDecode": "VIDEO DECODE",
            "label.sharedVram": "SHARED VRAM",
            "label.freeVram": "FREE VRAM",
            "label.totalVram": "TOTAL VRAM",
            "label.memoryUsed": "MEMORY USED",
            "label.sharedMemory": "SHARED MEMORY",
            "label.freeMemory": "FREE MEMORY",
            "label.totalMemory": "TOTAL MEMORY",
            "label.gpuPower": "GPU POWER",
            "label.memoryClock": "MEMORY CLOCK",
            "label.available": "AVAILABLE",
            "label.cached": "CACHED",
            "label.virtualMemory": "COMMITTED MEMORY",
            "label.diskSpace": "DISK SPACE",
            "label.read": "READ",
            "label.write": "WRITE",
            "label.responseTime": "RESPONSE TIME",
            "label.network": "NETWORK",
            "label.download": "DOWNLOAD",
            "label.upload": "UPLOAD",
            "label.connectionType": "CONNECTION TYPE",
            "label.wifiSignal": "WI-FI SIGNAL",
            "label.ping": "NETWORK PING",
            "label.motherboard": "MOTHERBOARD",
            "node.cpuLoad": "CPU LOAD",
            "node.igpuLoad": "iGPU LOAD",
            "node.npuLoad": "NPU LOAD",
            "node.ramActivity": "RAM ACTIVITY",
            "node.gpuLoad": "GPU LOAD",
            "node.diskActivity": "DISK ACTIVITY",
            "node.networkActivity": "NETWORK ACTIVITY",
            "node.processes": "{n} PROCESSES",
            "node.diskFree": "{gb} free",
            "ram.modules": "{n} modules",
            "battery.status": "BATTERY {pct} % · {source}",
            "battery.ac": "PLUGGED IN",
            "battery.dc": "ON BATTERY",
            "ping.router": "router",
            "ping.routerTitle": "The internet does not answer pings: latency measured to the local router.",
            "power.aria": "Power mode {name}",
            "sensor.boardPower": "Power of the whole card, not of NexusWpp.",
            "sensor.stale": "Last reading is stale: value ignored.",
            "sensor.missing": "This sensor provides no reading.",
            "sensor.held": "Last known value: the sensor stopped responding.",
            "alert.highLoad": "High load",
            "alert.memoryFull": "Memory nearly full",
            "alert.highLoadTitle": "At least 90 % for 5 s. Back to normal below 80 % for 5 s. This is not a fault diagnosis.",
            "alert.dataExpired": "Data expired",
            "alert.sensorsStale": "Sensors stale",
            "aria.unavailable": "Reading unavailable",
            "aria.waiting": "Waiting for a reading",
            "aria.stale": "{value}, last known value (stale)",
            "aria.map": "Interactive hardware telemetry map showing links and data flows between components as animated particles.",
            "unit.GB": "GB", "unit.MB": "MB", "unit.KB": "KB"
        }
    };

    function detect() {
        let requested = "";
        try { requested = new URLSearchParams(location.search).get("lang") || ""; } catch (e) {}
        const language = (requested || navigator.language || "fr").toLowerCase();
        return language.startsWith("fr") ? "fr" : "en";
    }

    const lang = detect();
    const table = STRINGS[lang];
    const systemLocale = navigator.language || "";
    const locale = lang === "fr" ? "fr-FR" : (systemLocale && !systemLocale.toLowerCase().startsWith("fr") ? systemLocale : "en-US");

    function t(key, vars) {
        let text = table[key] !== undefined ? table[key] : STRINGS.fr[key];
        if (text === undefined) return key;
        if (vars) text = text.replace(/\{(\w+)\}/g, (match, name) => vars[name] !== undefined ? String(vars[name]) : match);
        return text;
    }

    // Size unit for the interface language: "GB" -> "Go" in French.
    function unit(name) {
        return t("unit." + name);
    }

    // Displayed numbers: "35,8" in French, "35.8" in English, never grouped ("1234,5").
    // Host strings ("35.80", or "24,0 Mo" from a culture-formatted value) are parsed
    // and keep their own number of decimals unless `digits` is given.
    const numberLocale = lang === "fr" ? "fr-FR" : "en-US";
    const numberFormats = new Map();
    function num(value, digits) {
        let number = value;
        if (typeof value === "string") {
            const text = value.trim().replace(",", ".");
            number = parseFloat(text);
            if (digits === undefined) {
                const decimals = /^[-+]?\d*\.(\d+)/.exec(text);
                digits = decimals ? decimals[1].length : 0;
            }
        }
        if (typeof number !== "number" || !Number.isFinite(number)) return "—";
        const key = digits === undefined ? "auto" : digits;
        if (!numberFormats.has(key)) {
            numberFormats.set(key, new Intl.NumberFormat(numberLocale, digits === undefined
                ? { useGrouping: false, maximumFractionDigits: 3 }
                : { useGrouping: false, minimumFractionDigits: digits, maximumFractionDigits: digits }));
        }
        return numberFormats.get(key).format(number);
    }

    // "MARDI 13 OCTOBRE 2026" / "TUESDAY, OCTOBER 13, 2026"
    const dateFormat = new Intl.DateTimeFormat(locale, { weekday: "long", day: "2-digit", month: "long", year: "numeric" });
    function formatDate(date) {
        return dateFormat.format(date).toUpperCase();
    }

    function applyStatic(root) {
        document.documentElement.lang = lang;
        for (const element of (root || document).querySelectorAll("[data-i18n]")) {
            element.textContent = t(element.dataset.i18n);
        }
        for (const element of (root || document).querySelectorAll("[data-i18n-aria]")) {
            element.setAttribute("aria-label", t(element.dataset.i18nAria));
        }
    }

    return { lang, t, unit, num, formatDate, applyStatic, STRINGS };
})();

if (typeof module !== "undefined" && module.exports) module.exports = NexusI18n;
