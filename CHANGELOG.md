# Changelog

## 2026-06-11

- Reconnexion automatique du fond d'ecran au retour de veille, de deverrouillage de session ou de changement d'ecran.

- Version MSIX montee a 1.0.6.0 pour la soumission Store.

- Masquage automatique des cartes iGPU et GPU sur les machines qui n'ont pas le materiel, avec redistribution des noeuds de la carte radar.
- Prise en charge des GPU Qualcomm/Adreno (PC ARM Copilot+).
- Filtrage des adaptateurs reseau virtuels (VMware, Hyper-V, VPN) et identite reseau basee sur l'interface portant la passerelle par defaut.
- Affichage de la batterie (pourcentage et secteur) sur les portables, masque sur les tours.
- Consommation GPU en watts via NVML a la place du core clock.
- Top processus RAM sur la carte memoire a la place du pool non pagine.
- Force du signal Wi-Fi via l'API WLAN native quand la connexion est sans fil.

- Remplacement des infos pilote et de l'uptime par des mesures plus utiles : top processus CPU, decodage video iGPU, VRAM utilisee/totale et frequence memoire GPU.
- Correction de la cadence CPU sur les processeurs hybrides (reference `ProcessorFrequency` du compteur au lieu de `MaxClockSpeed` WMI).
- Retrait de la lecture du ventilateur GPU desormais non affichee.

- Suppression de toutes les donnees simulees au profit de mesures reelles.
- Temperature CPU lue depuis le capteur ACPI (`MSAcpi_ThermalZoneTemperature`), cadence CPU reelle via le compteur `PercentProcessorPerformance`.
- Ventilateur GPU reel via NVML (`nvmlDeviceGetFanSpeed`), VRAM totale lue depuis le pilote (registre) et VRAM utilisee via le compteur Windows `DedicatedUsage` quand NVML est absent.
- Remplacement des TOPS/TFLOPS codes en dur par la version reelle du pilote GPU, et de la fausse temperature iGPU par la version du pilote iGPU.
- Type de RAM (DDR4/DDR5...) et nombre de barrettes lus depuis le SMBIOS au lieu du libelle fixe "DDR5 Dual-Channel".
- Uptime systeme reel (`GetTickCount64`) au lieu du temps de vie du processus; le slot ventilateur CPU (non mesurable) affiche desormais l'uptime.
- Quand un capteur est absent, le slot affiche une autre mesure reelle (cache CPU, VRAM, date du pilote) sans casser la grille.
- Activation du nettoyage des processus WebView2 orphelins au demarrage.
- Temperature CPU lue en priorite via le compteur `ThermalZoneInformation` (accessible sans droits administrateur), avec repli ACPI.
- Prise en charge des GPU AMD/Radeon : classification APU integre / carte dediee pour la telemetrie, les pilotes et la VRAM.

## 2026-06-10

- Bascule automatique du build MSIX signe vers Windows PowerShell quand PowerShell 7 ne peut pas charger le module PKI.
- Correction de l'encodage des noms de modes d'alimentation Windows avec accents.
- Passage du package Microsoft Store en version `1.0.5.0` pour publier ce correctif.
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
