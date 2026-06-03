# NexusWpp - Native Dynamic Desktop Wallpaper

NexusWpp affiche un cockpit matériel dynamique directement dans le bureau Windows, sans Wallpaper Engine. Le mode officiel est l'hôte natif `nexuswpp.exe`, qui charge `index.html` dans WebView2, s'attache à la couche `WorkerW`/`Progman` du bureau, puis envoie la télémétrie au frontend par messages WebView2.

## Objectif

- Fond d'écran HTML/CSS/Canvas animé sous les icônes Windows.
- Démarrage le plus rapide possible à l'ouverture de session.
- Aucun serveur Node, aucune dépendance npm, aucun Wallpaper Engine.
- Contrôle cliquable des profils d'alimentation Windows depuis le fond d'écran.

## Architecture Actuelle

- `DesktopHtmlHost.cs` : hôte WinForms/WebView2, injection desktop, hook souris, collecte télémétrie native.
- `index.html`, `app.js`, `style.css` : interface du cockpit et moteur Canvas.
- `compile.ps1` : compile `DesktopHtmlHost.cs` en `bin\nexuswpp.exe`.
- `deploy_local.ps1` : copie l'app dans `C:\nexuswpp`, crée un raccourci Startup et une tâche planifiée de secours.
- `run.bat` : menu local pour démarrer, compiler, déployer ou arrêter l'app.
- `scripts/benchmark_nexuswpp.ps1` : benchmark multi-run CPU/RAM/startup.
- `scripts/benchmark_fullscreen_suspend.ps1` : mesure actif vs suspension plein écran.
- `scripts/generate_loading_snapshot.ps1` : régénère l'image de chargement 2560x1440 à zéro.

## Démarrage Rapide

Prérequis:

- Windows 10/11.
- Microsoft .NET Framework 4.x avec `csc.exe`.
- Microsoft Edge WebView2 Runtime.
- NVIDIA/NVML est optionnel; l'application garde des valeurs de secours si NVML n'est pas disponible.

```powershell
.\compile.ps1
.\deploy_local.ps1
```

Le déploiement configure deux lanceurs:

- un raccourci `NexusWpp.lnk` dans le dossier Startup, pour un lancement utilisateur très tôt;
- une tâche planifiée `NexusWpp` non élevée, comme secours.

L'application contient un verrou single-instance, donc les deux lanceurs ne créent pas deux fonds d'écran.

## Portabilité

- Le matériel est détecté automatiquement via WMI, Win32, interfaces réseau Windows et NVML quand disponible.
- Le dossier de production reste `C:\nexuswpp` pour accélérer le démarrage et éviter OneDrive.
- Le raccourci de démarrage utilise le dossier Startup de l'utilisateur courant, pas un chemin utilisateur codé en dur.
- Le fond d'écran fonctionne sans serveur Node et sans dépendance npm.
- Le sélecteur d'alimentation confirme le GUID actif Windows et ignore les clics quand une autre application recouvre le panneau.
- `loading-zero-1440p.png` est affiché immédiatement au lancement pour masquer le court temps de chargement WebView2.

## Pourquoi C'est Plus Rapide Qu'Avant

L'ancienne version attendait de trouver `WorkerW` avant d'initialiser WebView2. Au démarrage Windows, cette couche peut arriver tard. La version actuelle précharge WebView2 immédiatement hors écran, cherche `WorkerW` toutes les 100 ms, puis utilise temporairement `Progman` si `WorkerW` tarde trop.

## Notes

- `server.js`, `get-stats.ps1`, `get-startup.ps1` et le flux SSE ne font plus partie de cette architecture.
- Le mode navigateur simple affiche l'interface, mais sans télémétrie ni changement de profil.
- Le chemin de production est `C:\nexuswpp\nexuswpp.exe`.
- Le rond réseau est calibré sur la vitesse réelle du lien Windows, par exemple `2.5 Gb/s` pour l'Intel I226-V.

## Mesures utiles

```powershell
.\scripts\benchmark_nexuswpp.ps1 -DurationSeconds 18 -Runs 3 -Label current -OutputPath .\scripts\last-benchmark.json
.\scripts\compare_benchmark_result.ps1 -BeforePath .\scripts\last-benchmark-before.json -AfterPath .\scripts\last-benchmark-after.json
.\scripts\benchmark_fullscreen_suspend.ps1 -ActiveSeconds 10 -FullscreenSeconds 10 -OutputPath .\scripts\last-benchmark-fullscreen.json
```

## Garde-fou benchmark

Avant de garder une optimisation, produire un JSON avant/apres avec `benchmark_nexuswpp.ps1`, puis comparer:

```powershell
.\scripts\compare_benchmark_result.ps1 -BeforePath .\scripts\last-benchmark-before.json -AfterPath .\scripts\last-benchmark-after.json
```

Le verdict est:

- `KEEP` si CPU ou RAM baisse de maniere mesurable sans regression.
- `REJECT` si les erreurs augmentent, si le CPU/RAM regressent au-dela des tolerances, ou si l'attache au bureau ralentit trop.
- `NEUTRAL` si rien ne regresse, mais que le gain n'est pas significatif.

Le benchmark plein ecran verifie aussi `SuspendLatencyMs`, `ResumeLatencyMs`, `TelemetryAfterResumeMs` et `ProbeSuspendMatched` pour eviter qu'une autre application plein ecran fausse la mesure.
