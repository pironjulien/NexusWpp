# Consignes NexusWpp

## Projet

- Application Windows native de fond d'ecran dynamique.
- L'hote principal est `DesktopHtmlHost.cs`.
- L'interface est dans `index.html`, `style.css` et `app.js`.
- Le dossier d'installation local est `C:\nexuswpp`.

## Commandes utiles

```powershell
.\compile.ps1
.\scripts\build_installer.ps1
```

## Verification

- Verifier que `.\compile.ps1` compile `bin\nexuswpp.exe`.
- Verifier que `.\scripts\build_installer.ps1` cree `dist\NexusWppSetup.exe`.
- Ne pas versionner `bin/`, `dist/`, les logs, les fichiers temporaires ou les resultats de benchmark.

## Habitudes projet

- Travailler sur `main`.
- Mettre a jour `CHANGELOG.md` pour chaque changement fonctionnel.
- Garder les messages de commit courts et en francais.
- Ne pas ajouter de faux bouton, de fausse donnee ou de fonction non cablee.
