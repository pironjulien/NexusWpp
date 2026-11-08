# Consignes NexusWpp

## Projet

- Application Windows native de fond d'ecran dynamique.
- L'hote principal est la classe partielle `DesktopForm` (`DesktopForm*.cs`, un fichier par responsabilite) lancee par `Program.cs`.
- L'interface est dans `index.html`, `style.css` et des scripts classiques charges dans l'ordre (`cockpit-state.js`, `radar.js`, `dashboard.js`, `app.js`) qui partagent la portee globale ; les textes visibles sont traduits dans `i18n.js` (francais et anglais).
- Le mode EXE autonome utilise `C:\nexuswpp`. Le mode Store/MSIX utilise WindowsApps et conserve ses données dans le profil utilisateur packagé ; le seul chemin d'installation EXE est l'installeur (`scripts\build_installer.ps1` puis `dist\NexusWppSetup.exe`).

## Commandes utiles

```powershell
.\compile.ps1
.\scripts\build_installer.ps1
.\scripts\build_msix.ps1
dotnet run --project .\tools\ExportIcons   # icon.ico et logos MSIX depuis branding\*.svg (Svg.Skia)
.\scripts\tag_release.ps1             # avant le push : la version est-elle prête à publier ?
.\scripts\tag_release.ps1 -Download   # fichiers de la release v<VERSION> dans dist\release, attestation vérifiée
```

## Publication

- Les releases sont construites par GitHub, jamais sur le PC : pousser sur `main` un `VERSION` sans release `v<VERSION>` (avec sa section `## [<VERSION>] - <date>` dans `CHANGELOG.md`, au format Keep a Changelog, en anglais) lance `.github/workflows/release.yml` : `test.ps1`, MSIX non signé (le Store le signe), installeur non signé, `SHA256SUMS.txt`, attestation de provenance, puis la release immuable avec tous les fichiers et le tag.
- Le Store reçoit le MSIX de la release (`tag_release.ps1 -Download`), jamais un MSIX construit en local.
- L'installeur n'est pas signé Authenticode (pas de certificat public ; un certificat auto-signé ne prouve rien) : son origine se vérifie par `gh attestation verify <fichier> --repo pironjulien/NexusWpp`.

## Verification

- Verifier que `.\compile.ps1` compile `bin\nexuswpp.exe`.
- Lancer `.\test.ps1` avant chaque commit : compilation, tests JavaScript, puis `dotnet test` sur `tests\NexusWpp.Tests` (xUnit v3 : regles de mesure, couverture du bureau, et les harnais hote, reglages et logo, hook souris, pause, disposition rapide ; fenetres hors ecran, jamais activees).
- Verifier que `.\scripts\build_installer.ps1` cree `dist\NexusWppSetup.exe`.
- Verifier que `.\scripts\build_msix.ps1` cree le package Store `dist\msix\julienpiron.fr.NexusWpp_<VERSION>_x64.msix`, avec le numero du fichier `VERSION`.
- Tests explicites, hors `test.ps1` et CI : toutes les resolutions avec `dotnet test --project tests\NexusWpp.Tests --filter-method *ResponsiveLayoutFull --explicit only` ; captures Store avec `.\scripts\store_screenshots.ps1` (fenetre de reglages a l'ecran, jamais activee).
- Mesurer le retour physique du bureau avec `.\scripts\test_desktop_visual_resume.ps1` (vrai bureau : reduit puis restaure les fenetres, seulement en l'absence de l'utilisateur).
- Ne pas versionner `bin/`, `dist/`, les logs, les fichiers temporaires ou les resultats de benchmark.

## Habitudes projet

- Travailler sur `main`.
- Mettre a jour `CHANGELOG.md` pour chaque changement fonctionnel.
- Pour le Store, garder l'identite MSIX `julienpiron.fr.NexusWpp` et le Publisher `CN=C3E3A6F0-11D2-4EE1-B3F2-34EED4CAE7FA`.
- Garder les messages de commit courts et en anglais (depot public), comme le CHANGELOG.
- Ne pas ajouter de faux bouton, de fausse donnee ou de fonction non cablee.

## Pièges

- Cadence d'animation : la page remet la cadence toutes les 500 ms ; pour un banc, surcharger `setMotionEconomy`, rendre hors écran à la taille réelle de l'écran (ex. 5120×1440), comparer au fond réel gelé et alterner les variantes, sinon les mesures se contaminent.
- Coût GPU : les compteurs `GPU Engine` ne donnent qu'un LUID ; relier d'abord le LUID à l'adaptateur (`\GPU Adapter Memory(*)\Dedicated Usage`, et le LUID de dwm) avant d'attribuer la charge à l'iGPU ou au GPU dédié. Pour le coût réel, mesurer les watts (`nvidia-smi`) rendu actif contre rendu gelé, pas le pourcentage d'utilisation, gonflé à la fréquence plancher.
- Nœud RAM : il affiche la mémoire occupée, pas le débit. Aucune mesure de débit fiable sans droits admin n'existe sur Alder/Raptor Lake : compteurs LLCMisses par ETW (exacts mais admin + SeSystemProfilePrivilege), sonde de latence (erreur ~21 points), puissance « uncore » RAPL (erreur ~8 points, faux trafic en calcul pur), Intel PMT (pas de descripteur DDR pour ces processeurs, le pilote n'expose aucun IOCTL). Ne pas retester ces pistes.
- `radar.js` (nodeLoad : activity sinon value) fait tourner un nœud selon sa valeur : un nœud à valeur stable (RAM) s'anime donc en continu au repos.
