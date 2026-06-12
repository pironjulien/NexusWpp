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
- `deploy_local.ps1` : copie l'app dans `C:\nexuswpp` et configure le lancement Windows via `HKLM\...\Run`.
- `run.bat` : menu local pour démarrer, compiler, déployer ou arrêter l'app.
- `scripts/benchmark_nexuswpp.ps1` : benchmark multi-run CPU/RAM/startup.
- `scripts/benchmark_fullscreen_suspend.ps1` : mesure actif vs suspension plein écran.

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

Pour produire un installeur `.exe` autonome:

```powershell
.\scripts\build_installer.ps1
```

Le fichier généré est `dist\NexusWppSetup.exe`. Il embarque l'application compilée, les DLL WebView2 SDK et les assets. Au lancement, il demande les droits administrateur, installe dans `C:\nexuswpp`, vérifie le Runtime WebView2 Evergreen, le télécharge depuis Microsoft si nécessaire, configure un seul lancement Windows via `HKLM\...\Run`, ajoute `NexusWpp` au menu Démarrer, inscrit NexusWpp dans Applications installées avec une commande de désinstallation, puis démarre le fond d'écran.

Pour signer l'installeur, installer le Windows SDK puis définir l'une des configurations suivantes avant le build:

```powershell
$env:NEXUSWPP_SIGN_CERT_THUMBPRINT = "THUMBPRINT_CERTIFICAT_CODESIGNING"
.\scripts\build_installer.ps1
```

ou:

```powershell
$env:NEXUSWPP_SIGN_PFX = "C:\certs\nexuswpp.pfx"
$env:NEXUSWPP_SIGN_PFX_PASSWORD = "mot-de-passe"
.\scripts\build_installer.ps1
```

Sans certificat code-signing public, l'installeur fonctionne mais peut afficher un avertissement SmartScreen.

Pour mettre a jour une installation existante, relancer simplement un `NexusWppSetup.exe` plus recent. L'installeur arrete l'instance active, remplace les fichiers, nettoie les anciens lanceurs, conserve un seul demarrage `HKLM\...\Run`, met a jour le raccourci du menu Demarrer et relance le fond. La desinstallation Windows utilise `C:\nexuswpp\NexusWppSetup.exe /uninstall`.

L'installeur configure:

- une entrée de démarrage unique `NexusWpp` dans `HKLM\...\Run`;
- un raccourci `NexusWpp.lnk` dans le menu Démarrer commun;
- une entrée de désinstallation dans Applications installées.

Le package MSIX configure le lancement Windows via une tache de démarrage packagée `windows.startupTask`, car les entrées `HKLM\...\Run` de l'installeur EXE ne s'appliquent pas au mode Store/MSIX.

L'application contient un verrou single-instance, donc relancer `NexusWpp` depuis le menu Démarrer ne crée pas deux fonds d'écran.

## Portabilité

- Le matériel est détecté automatiquement via WMI, Win32, interfaces réseau Windows et NVML quand disponible.
- Le dossier de production reste `C:\nexuswpp` pour accélérer le démarrage et éviter OneDrive.
- Le raccourci du menu Démarrer est créé dans le dossier commun Windows, pas dans un chemin utilisateur codé en dur.
- Le fond d'écran fonctionne sans serveur Node et sans dépendance npm.
- Le sélecteur d'alimentation confirme le GUID actif Windows et ignore les clics quand une autre application recouvre le panneau.
- Le premier affichage est directement l'interface réelle, avec des valeurs neutres. La télémétrie remplit ensuite les champs existants dès qu'elle arrive.

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
