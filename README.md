# NexusWpp - Native Dynamic Desktop Wallpaper

NexusWpp affiche un cockpit matériel dynamique directement dans le bureau Windows, sans Wallpaper Engine. Le mode officiel est l'hôte natif `nexuswpp.exe`, qui charge `index.html` dans WebView2, s'attache à la couche `WorkerW`/`Progman` du bureau, puis envoie la télémétrie au frontend par messages WebView2.

## Objectif

- Fond d'écran HTML/CSS/Canvas animé sous les icônes Windows.
- Démarrage le plus rapide possible à l'ouverture de session.
- Aucun serveur Node, aucune dépendance npm, aucun Wallpaper Engine.
- Contrôle cliquable des profils d'alimentation Windows depuis le fond d'écran.

## Architecture Actuelle

- `DesktopHtmlHost.cs` : hôte WinForms/WebView2, injection desktop, hook souris, collecte télémétrie native.
- `DesktopVisibility.cs` : détection du bureau couvert, du plein écran et calcul de couverture multi-écrans.
- `index.html`, `app.js`, `style.css` : interface du cockpit et moteur Canvas.
- `compile.ps1` : compile l'hôte et la détection de visibilité en `bin\nexuswpp.exe`.
- `deploy_local.ps1` : copie l'app dans `C:\nexuswpp` et configure le lancement Windows via `HKLM\...\Run`.
- `run.bat` : menu local pour démarrer, compiler, déployer ou arrêter l'app.
- `scripts/benchmark_nexuswpp.ps1` : benchmark multi-run CPU/RAM/startup.
- `scripts/benchmark_fullscreen_suspend.ps1` : ancien benchmark des versions qui utilisaient le motif de journal « fullscreen foreground detected » ; pour la politique actuelle, utiliser `measure_desktop_suspension.ps1`.
- `scripts/measure_desktop_suspension.ps1` : mesure CPU/GPU avec bureau visible, fenêtres juxtaposées, plein écran et fenêtres transparentes, puis restaure les fenêtres précédentes.
- `scripts/test_desktop_coverage.cs` : dix scénarios géométriques indépendants du bureau réel.

## Démarrage Rapide

Prérequis:

- Windows 10/11.
- Microsoft .NET Framework 4.x avec `csc.exe`.
- Microsoft Edge WebView2 Runtime.
- NVIDIA est optionnel; les mesures détaillées utilisent le processus isolé `nvidia-smi`, avec les compteurs Windows lorsque cette source est indisponible.

```powershell
.\compile.ps1
.\deploy_local.ps1
```

Le fichier `VERSION` est la source unique du numéro de l'EXE, de l'installeur et du MSIX. Pour produire un installeur `.exe` autonome:

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

Le package MSIX configure le lancement Windows via une tache de démarrage packagée `windows.startupTask`, car les entrées `HKLM\...\Run` de l'installeur EXE ne s'appliquent pas au mode Store/MSIX. L'hote natif s'enregistre aussi aupres du Restart Manager Windows pour que le Store puisse relancer NexusWpp automatiquement apres une mise a jour qui ferme l'instance active.

L'application contient un verrou single-instance, donc relancer `NexusWpp` depuis le menu Démarrer ne crée pas deux fonds d'écran.

Une installation Store/MSIX se met à jour avec le package de même identité et un numéro de version supérieur. Ne pas lui ajouter le déploiement EXE, qui utilise un autre emplacement et un autre mécanisme de démarrage.

## Pause et retour au bureau

La version `1.0.16.0` fige les animations Canvas/CSS, les transitions en cours et l'horloge quand toutes les zones utiles des écrans sont recouvertes par des fenêtres opaques. La télémétrie cesse ses nouvelles collectes. Le DOM, les particules, les valeurs et la surface WebView2 restent en place : aucune dissimulation du contrôle, suspension Chromium, navigation ni reconstruction n'est nécessaire pour revoir le bureau.

La détection s'effectue toutes les 500 ms. Ce délai concerne la reprise des animations et des mesures ; la scène déjà affichée reste disponible au compositeur Windows. Une collecte terminant après une pause est ignorée. La reprise demande de nouvelles mesures et actualise l'horloge. Un véritable redimensionnement redessine une fois la scène figée.

La couverture est calculée sur l'ensemble des écrans sans double comptage des chevauchements. Les fenêtres transparentes, masquées sur un autre bureau virtuel et les surfaces du shell sont exclues. Un jeu plein écran sur un seul moniteur laisse fonctionner le bureau encore visible sur un autre. Le verrouillage ou la déconnexion de session met également le travail en pause.

La version locale `1.0.15.0` masquait et suspendait WebView2. Ses mesures de consommation ne validaient pas le retour visuel ; ce comportement est remplacé en `1.0.16.0`.

```powershell
.\scripts\test_runtime_pause.ps1
.\scripts\test_desktop_visual_resume.ps1 -Label installed
```

Le premier test exécute la page réelle dans WebView2 et vérifie notamment les pixels Canvas, les particules, la première scène et les transitions rapides. Le second capture le bureau composé pendant le retrait de fenêtres maximisées, juxtaposées, plein écran, partielles et transparentes. Il vérifie le chemin du processus packagé, restaure les fenêtres et conserve captures, temps mesurés et limites sous `work\resume-20260925`. Les mesures CPU/GPU se font séparément avec `measure_desktop_suspension.ps1` : une baisse de consommation ou des journaux de reprise seuls ne prouvent pas l'absence de disparition visuelle.

## Portabilité

### Résolutions et mise à l'échelle

À partir de la version `1.0.18.0`, la grille respecte les deux dimensions du viewport WebView2 et préserve les marges pour une rangée d'icônes en haut et un espace au-dessus de la barre des tâches. Les cartes adaptent la place de leurs jauges et de leurs mesures à leur taille réelle ; les écrans portrait utilisent deux colonnes sous l'horloge et les commandes. Le radar ajuste uniformément son dessin et ses coordonnées de souris. Les mesures et les boutons restent présents, sans défilement des cartes du bureau.

La version `1.0.19.0` répartit cette même réserve verticale avec davantage d'espace en haut pour les libellés des icônes, ce qui décale légèrement le cockpit vers le bas sans redimensionner les cartes.

```powershell
.\scripts\test_responsive_layout.ps1 -Label current
```

Le harnais charge les fichiers livrés dans le véritable Runtime WebView2 Evergreen, avec émulation Chromium du viewport et du ratio de pixels. Il couvre 29 résolutions physiques de 1280×720 à 7680×4320, les échelles 100, 125, 150, 175, 200, 225, 250, 300, 350 et 400 % lorsque la surface logique reste au moins 800×450 en paysage ou 432×768 en portrait. Il inclut les anciens seuils CSS, des variantes sans iGPU/NPU/GPU dédié et un à quatre profils d'alimentation.

Les contrôles portent sur les cartes hors écran, les textes et commandes coupés, les chevauchements, la taille et la circularité des jauges, les nœuds du radar, les coordonnées de souris et le maintien de la pause pendant les redimensionnements. Les données de test restent dans le harnais et ne sont jamais embarquées dans le produit. Les JSON et captures PNG se trouvent dans `work\responsive\<Label>` ; ce test d'émulation ne valide ni l'overscan d'un téléviseur physique ni une topologie de plusieurs moniteurs aux DPI différents.

### Installation et matériel

- Le matériel est détecté automatiquement via WMI, Win32, interfaces réseau Windows et `nvidia-smi` quand disponible.
- Le MSIX est installé par Windows dans `WindowsApps`; seul le mode EXE autonome utilise `C:\nexuswpp`. Les données et le profil WebView2 restent dans le dossier utilisateur local.
- Le raccourci du menu Démarrer est créé dans le dossier commun Windows, pas dans un chemin utilisateur codé en dur.
- Le fond d'écran fonctionne sans serveur Node et sans dépendance npm.
- Le sélecteur d'alimentation confirme le GUID actif Windows et ignore les clics quand une autre application recouvre le panneau.
- Le premier affichage est directement l'interface réelle, avec des valeurs neutres. La télémétrie remplit ensuite les champs existants dès qu'elle arrive.

## Pourquoi C'est Plus Rapide Qu'Avant

L'ancienne version attendait de trouver `WorkerW` avant d'initialiser WebView2. Au démarrage Windows, cette couche peut arriver tard. La version actuelle précharge WebView2 immédiatement hors écran, cherche `WorkerW` toutes les 100 ms, puis utilise temporairement `Progman` si `WorkerW` tarde trop.

## Notes

- `server.js`, `get-stats.ps1`, `get-startup.ps1` et le flux SSE ne font plus partie de cette architecture.
- Le mode navigateur simple affiche l'interface, mais sans télémétrie ni changement de profil.
- Pour connaître le chemin MSIX réel : `(Get-AppxPackage julienpiron.fr.NexusWpp).InstallLocation`.
- Le rond réseau est calibré sur la vitesse réelle du lien Windows, par exemple `2.5 Gb/s` pour l'Intel I226-V.

## Mesures utiles

```powershell
.\scripts\benchmark_nexuswpp.ps1 -DurationSeconds 18 -Runs 3 -Label current -OutputPath .\scripts\last-benchmark.json
.\scripts\compare_benchmark_result.ps1 -BeforePath .\scripts\last-benchmark-before.json -AfterPath .\scripts\last-benchmark-after.json
$package = Get-AppxPackage julienpiron.fr.NexusWpp
.\scripts\measure_desktop_suspension.ps1 -AppPath (Join-Path $package.InstallLocation nexuswpp.exe) -LogPath "$env:LOCALAPPDATA\Packages\julienpiron.fr.NexusWpp_yq2nr3sn86fpg\LocalCache\Local\NexusWpp\webview_debug.log" -OutputPath .\work\desktop-performance.json
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

Les champs historiques `SuspendLatencyMs`, `ResumeLatencyMs` et `TelemetryAfterResumeMs` mesuraient la réception des journaux, pas le retour visuel. Les captures du harnais `test_desktop_visual_resume.ps1` constituent la vérification du retour au bureau, complétée par la mesure CPU/GPU séparée.
