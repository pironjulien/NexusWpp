// Interface language: French when the Windows display language of the user is French, English
// everywhere else. The host gives WebView2 that language (CoreWebView2EnvironmentOptions.Language,
// the same CurrentUICulture as the tray menu and the settings window); navigator.language reports it.
"use strict";
const NexusI18n = (() => {
    // Every key exists in both languages (scripts/tests/i18n.test.js). Terms follow Task Manager
    // where it has one (Speed, Committed, Receive / Send, Active time).
    const STRINGS = {
        fr: {
            "label.temperature": "TEMPÉRATURE",
            "label.thermalZone": "TEMPÉRATURE (ACPI)",
            "label.busiestCore": "CŒUR LE PLUS CHARGÉ",
            "label.cacheL2L3": "CACHE L2 / L3",
            "label.topProcess": "PROCESSUS EN TÊTE",
            "label.clockSpeed": "VITESSE",
            "label.procThreads": "PROCESSUS / THREADS",
            "label.video": "VIDÉO",
            "label.dedicatedMemory": "MÉMOIRE DÉDIÉE",
            "label.sharedMemory": "MÉMOIRE PARTAGÉE",
            "label.vram": "MÉMOIRE GPU",
            "label.memoryUsed": "MÉMOIRE UTILISÉE",
            "label.freeMemory": "MÉMOIRE LIBRE",
            "label.totalMemory": "MÉMOIRE TOTALE",
            "label.gpuPower": "CONSOMMATION",
            "label.memoryInUse": "UTILISÉE",
            "label.hardFaults": "DÉFAUTS MATÉRIELS/S",
            "label.diskSpace": "ESPACE UTILISÉ",
            "label.read": "LECTURE",
            "label.write": "ÉCRITURE",
            "label.responseTime": "TEMPS DE RÉPONSE",
            "label.network": "RÉSEAU",
            "label.download": "RÉCEPTION",
            "label.upload": "ENVOI",
            "label.connectionType": "TYPE DE CONNEXION",
            "network.offline": "HORS LIGNE",
            "label.wifiSignal": "SIGNAL WI-FI",
            "label.ping": "LATENCE",
            "label.motherboard": "CARTE MÈRE",
            "node.cpuLoad": "CHARGE CPU",
            "node.igpuLoad": "CHARGE iGPU",
            "node.npuLoad": "CHARGE NPU",
            "node.ramUsed": "MÉMOIRE OCCUPÉE",
            "node.gpuLoad": "CHARGE GPU",
            "node.diskActiveTime": "TEMPS D'ACTIVITÉ",
            "node.linkUsage": "UTILISATION DU LIEN",
            "battery.status": "BATTERIE {pct} % · {source}",
            "battery.charging": "EN CHARGE",
            "battery.left": "{time} RESTANTES",
            "adapter.idle": "Aucun",
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
            "alert.sensorsStale": "Capteurs sans réponse",
            "aria.unavailable": "Mesure indisponible",
            "aria.waiting": "En attente d’une mesure",
            "aria.stale": "{value}, dernière valeur connue (périmée)",
            "aria.map": "Carte interactive de télémétrie matérielle affichant les liens et les flux de données entre composants sous forme de particules animées.",
            "unit.GB": "Go", "unit.MB": "Mo", "unit.KB": "Ko"
        },
        en: {
            "label.temperature": "TEMPERATURE",
            "label.thermalZone": "TEMPERATURE (ACPI)",
            "label.busiestCore": "BUSIEST CORE",
            "label.cacheL2L3": "L2 / L3 CACHE",
            "label.topProcess": "BUSIEST PROCESS",
            "label.clockSpeed": "SPEED",
            "label.procThreads": "PROCESSES / THREADS",
            "label.video": "VIDEO",
            "label.dedicatedMemory": "DEDICATED MEMORY",
            "label.sharedMemory": "SHARED MEMORY",
            "label.vram": "GPU MEMORY",
            "label.memoryUsed": "MEMORY USED",
            "label.freeMemory": "FREE MEMORY",
            "label.totalMemory": "TOTAL MEMORY",
            "label.gpuPower": "POWER DRAW",
            "label.memoryInUse": "IN USE",
            "label.hardFaults": "HARD FAULTS/S",
            "label.diskSpace": "SPACE USED",
            "label.read": "READ",
            "label.write": "WRITE",
            "label.responseTime": "RESPONSE TIME",
            "label.network": "NETWORK",
            "label.download": "RECEIVE",
            "label.upload": "SEND",
            "label.connectionType": "CONNECTION TYPE",
            "network.offline": "OFFLINE",
            "label.wifiSignal": "WI-FI SIGNAL",
            "label.ping": "LATENCY",
            "label.motherboard": "MOTHERBOARD",
            "node.cpuLoad": "CPU LOAD",
            "node.igpuLoad": "iGPU LOAD",
            "node.npuLoad": "NPU LOAD",
            "node.ramUsed": "MEMORY IN USE",
            "node.gpuLoad": "GPU LOAD",
            "node.diskActiveTime": "ACTIVE TIME",
            "node.linkUsage": "LINK UTILIZATION",
            "battery.status": "BATTERY {pct} % · {source}",
            "battery.charging": "CHARGING",
            "battery.left": "{time} LEFT",
            "adapter.idle": "None",
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
            "alert.dataExpired": "Data out of date",
            "alert.sensorsStale": "Sensors not responding",
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
        const language = (requested || navigator.language || "en").toLowerCase();
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
    // Host strings ("35.80", "24.0 Mo") are parsed
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
