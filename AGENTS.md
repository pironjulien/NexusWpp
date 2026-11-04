# Consignes NexusWpp

## Projet

- Application Windows native de fond d'ecran dynamique.
- L'hote principal est la classe partielle `DesktopForm` (`DesktopForm*.cs`, un fichier par responsabilite) lancee par `Program.cs`.
- L'interface est dans `index.html`, `style.css` et des scripts classiques charges dans l'ordre (`cockpit-state.js`, `radar.js`, `dashboard.js`, `app.js`) qui partagent la portee globale ; les textes visibles sont traduits dans `i18n.js` (francais et anglais).
- Le mode EXE autonome utilise `C:\nexuswpp`. Le mode Store/MSIX utilise WindowsApps et conserve ses données dans le profil utilisateur packagé ; ne pas lui ajouter deploy_local.ps1.

## Commandes utiles

```powershell
.\compile.ps1
.\scripts\build_installer.ps1
.\scripts\build_msix.ps1
python .\scripts\export_icons.py   # icon.ico et logos MSIX depuis branding\*.svg (cairosvg)
.\scripts\tag_release.ps1             # avant le push : la version est-elle prête à publier ?
.\scripts\tag_release.ps1 -Download   # fichiers de la release v<VERSION> dans dist\release, attestation vérifiée
```

## Publication

- Les releases sont construites par GitHub, jamais sur le PC : pousser sur `main` un `VERSION` sans release `v<VERSION>` (avec sa section `## <date> — <VERSION>` dans `CHANGELOG.md`) lance `.github/workflows/release.yml` : `test.ps1`, MSIX non signé (le Store le signe), installeur non signé, `SHA256SUMS.txt`, attestation de provenance, puis la release immuable avec tous les fichiers et le tag.
- Le Store reçoit le MSIX de la release (`tag_release.ps1 -Download`), jamais un MSIX construit en local.
- L'installeur n'est pas signé Authenticode (pas de certificat public ; un certificat auto-signé ne prouve rien) : son origine se vérifie par `gh attestation verify <fichier> --repo pironjulien/NexusWpp`.

## Verification

- Verifier que `.\compile.ps1` compile `bin\nexuswpp.exe`.
- Verifier le transfert des clics et le cycle de vie du hook avec `.\scripts\test_mouse_hook.ps1` apres une modification de la gestion souris.
- Verifier que `.\scripts\build_installer.ps1` cree `dist\NexusWppSetup.exe`.
- Verifier que `.\scripts\build_msix.ps1` cree le package Store `dist\msix\julienpiron.fr.NexusWpp_<VERSION>_x64.msix`, avec le numero du fichier `VERSION`.
- Verifier les reglages et le logo personnalise avec `.\scripts\test_settings.ps1`.
- Verifier les changements de disposition avec `.\scripts\test_responsive_layout.ps1` (WebView2, resolutions, DPI, portrait et variantes materielles).
- Verifier la pause et la conservation de scene avec `.\scripts\test_runtime_pause.ps1`, puis mesurer le retour physique avec `.\scripts\test_desktop_visual_resume.ps1`.
- Ne pas versionner `bin/`, `dist/`, les logs, les fichiers temporaires ou les resultats de benchmark.

## Habitudes projet

- Travailler sur `main`.
- Mettre a jour `CHANGELOG.md` pour chaque changement fonctionnel.
- Pour le Store, garder l'identite MSIX `julienpiron.fr.NexusWpp` et le Publisher `CN=C3E3A6F0-11D2-4EE1-B3F2-34EED4CAE7FA`.
- Garder les messages de commit courts et en francais.
- Ne pas ajouter de faux bouton, de fausse donnee ou de fonction non cablee.
