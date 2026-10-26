# Fiche Microsoft Store — NexusWpp

Textes à coller dans Partner Center (Descriptions dans le Store). Chaque fonction citée existe dans l'application.
Captures : `.\scripts\store_screenshots.ps1` (rendu réel avec la télémétrie réelle du PC) → `dist\store-screenshots\`. Ordre conseillé : cockpit 4K, réglages, logo personnalisé, ultra-large, portrait.
Logos Windows Store (affiche 9:16, zone 1:1, vignettes 300/150/71) : `python .\scripts\export_icons.py` → `dist\store-logos\`.

---

## Français (fr-fr)

### Description courte

Votre PC en direct, en fond d'écran. CPU, GPU, NPU, RAM, SSD et réseau sur un cockpit animé derrière vos icônes, qui se met en pause tout seul quand une fenêtre le couvre.

### Description

NexusWpp transforme le bureau Windows en cockpit vivant. Derrière vos icônes, un tableau de bord animé affiche en continu l'état réel de votre machine : processeur, carte graphique, puce graphique intégrée, NPU, mémoire, SSD et réseau.

Aucun réglage n'est nécessaire : installez, et votre fond d'écran prend vie. NexusWpp détecte votre matériel et n'affiche que ce que votre PC possède réellement.

Conçu pour rester léger :
• le rendu se met en pause dès qu'une fenêtre couvre le bureau, et reprend instantanément quand il redevient visible ;
• la télémétrie utilise les compteurs natifs de Windows : quelques millisecondes par mesure ;
• les animations restent sobres (12 images/s) ;
• avec l'économiseur d'énergie de Windows, les anneaux du logo s'immobilisent pour économiser la batterie.

Au centre, un radar relie chaque composant au logo et fait circuler des paquets de données au rythme de l'activité réelle. Les alertes « Charge élevée » et « Mémoire très utilisée » n'apparaissent qu'après plusieurs secondes de charge réelle, sans clignotement.

Vous pouvez changer de mode d'alimentation Windows d'un clic, directement sur le fond d'écran. Depuis l'icône de la zone de notification, remplacez le logo central par votre propre image, ou quittez l'application.

Confidentialité : tout est mesuré localement. Pas de compte, pas de publicité, aucune donnée collectée ni envoyée. Seul un ping vers 1.1.1.1 ou 8.8.8.8 mesure la latence internet.

### Fonctionnalités

- Fond d'écran animé avec cockpit système en temps réel, derrière les icônes du bureau
- CPU : charge, fréquence réelle, zone thermique, processus le plus gourmand
- Cartes NVIDIA : charge, température, puissance, VRAM et fréquence mémoire
- Puce graphique intégrée et NPU (Intel AI Boost et autres) : charge et mémoire
- RAM, SSD (activité, espace, débits, temps de réponse) et réseau (débit, type, ping)
- Pause automatique quand une fenêtre couvre le bureau, reprise instantanée
- Animations immobilisées quand l'économiseur d'énergie de Windows est actif
- Changement du mode d'alimentation Windows d'un clic sur le fond d'écran
- Logo central personnalisable avec votre propre image
- Historique des 60 dernières secondes en filigrane dans chaque carte
- S'adapte à toutes les résolutions, à la mise à l'échelle et au mode portrait
- Démarre avec Windows ; pas de compte, pas de publicité, aucune donnée envoyée
- Interface en français ou en anglais, selon la langue de Windows

### Mots-clés (7 max)

fond d'écran animé, moniteur système, cockpit, CPU GPU, température, NPU, widget bureau

### Nouveautés de cette version (1.0.47.0)

Nouvelle icône NexusWpp : un écran avec jauge et barres dans le médaillon de julienpiron.fr, nette dans la barre des tâches, la zone de notification, le menu Démarrer et la fenêtre de réglages.

---

## English (en-us)

### Short description

Your PC, live, as your wallpaper. CPU, GPU, NPU, RAM, SSD and network on an animated cockpit behind your desktop icons, which pauses itself whenever a window covers it.

### Description

NexusWpp turns the Windows desktop into a live cockpit. Behind your icons, an animated dashboard continuously shows the real state of your machine: processor, graphics card, integrated GPU, NPU, memory, SSD and network.

No setup needed: install it and your wallpaper comes alive. NexusWpp detects your hardware and only shows what your PC actually has.

Built to stay light:
• rendering pauses as soon as a window covers the desktop, and resumes instantly when it is visible again;
• telemetry uses native Windows performance counters: a few milliseconds per reading;
• animations stay restrained (12 frames per second);
• with Windows energy saver on, the logo rings hold still to save battery.

In the center, a radar links every component to the logo, with data packets flowing at the pace of real activity. "High load" and "Memory nearly full" alerts only appear after several seconds of real load, without flickering.

Switch your Windows power mode with one click, right on the wallpaper. From the notification area icon, replace the center logo with your own image, or quit the app.

Privacy: everything is measured locally. No account, no ads, no data collected or sent. The only network use is a ping to 1.1.1.1 or 8.8.8.8 to measure internet latency.

### Features

- Animated wallpaper with a real-time system cockpit, behind your desktop icons
- CPU: load, real clock speed, thermal zone, top process
- NVIDIA graphics cards: load, temperature, power, VRAM and memory clock
- Integrated GPU and NPU (Intel AI Boost and others): load and memory
- RAM, SSD (activity, space, throughput, response time) and network (throughput, type, ping)
- Automatic pause when a window covers the desktop, instant resume
- Animations hold still while Windows energy saver is on
- Switch the Windows power mode with one click on the wallpaper
- Customizable center logo with your own image
- Last 60 seconds of history drawn faintly in every card
- Adapts to every resolution, display scaling and portrait mode
- Starts with Windows; no account, no ads, no data sent
- English or French interface, following your Windows language

### Keywords (7 max)

live wallpaper, system monitor, hardware monitor, CPU GPU, temperature, NPU, desktop dashboard

### What's new in this version (1.0.47.0)

New NexusWpp icon: a screen with a gauge and bars in the julienpiron.fr medallion, crisp in the taskbar, the notification area, the Start menu and the settings window.
