# Changelog

## 2026-06-10

- Correction de l'affichage responsive des modes d'alimentation Windows avec noms longs ou nombreux.
- Passage du package Microsoft Store en version `1.0.4.0` pour publier ce correctif.
- Ajout d'une option de build MSIX sans signature locale pour les postes sans module PKI fonctionnel.

## 2026-06-09

- Passage du package Microsoft Store en version `1.0.3.0` apres rejet du remplacement `1.0.2.0` a contenu different.
- Durcissement du lancement MSIX avec un manifeste Desktop Bridge explicite.
- Deplacement des logs et du profil WebView2 vers le dossier utilisateur local pour eviter les chemins systeme en contexte Store.
- Retrait des arguments GPU WebView2 agressifs afin de stabiliser le lancement sur les pilotes de certification Microsoft.

## 2026-06-08

- Passage de l'installateur Windows en version `1.0.2`.
- Preparation des builds de publication Microsoft Store en version `1.0.2.0`.
- Alignement du Publisher MSIX sur l'identite reservee dans Partner Center.
- Signature locale automatique de l'installeur quand le Windows SDK est installe.
- Suppression du systeme d'image zero au demarrage et pendant l'installation.
- Affichage immediat de l'interface reelle avec des valeurs neutres avant l'arrivee de la telemetrie.
- Retrait de l'image de chargement generee et du script de generation associe.
- Ajout des consignes projet dans `AGENTS.md`.
