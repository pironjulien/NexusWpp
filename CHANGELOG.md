# Changelog

## 2026-09-25 — 1.0.20.0

- Ajustement fin du placement : cockpit remonté de 10 pixels sur un écran à 125 %, avec une rangée d'icônes préservée sur les petits formats et des cartes de taille identique.

## 2026-09-25 — 1.0.19.0

- Léger décalage du cockpit vers le bas en rééquilibrant les marges existantes : davantage d'espace pour les libellés des icônes en haut, moins de vide au-dessus de la barre des tâches, sans changer la taille des cartes.

## 2026-09-25 — 1.0.18.0

- Correction du bas du cockpit coupé sur les écrans fortement mis à l'échelle, notamment en 4K à 225 % : pistes de grille bornées et cartes adaptées à la place réellement disponible.
- Jauges proportionnelles, disposition compacte préservant toutes les mesures et grille spécifique aux écrans portrait, sans défilement des panneaux de télémétrie.
- Réservation d'une rangée d'icônes au-dessus du cockpit et d'un espace au-dessus de la barre des tâches, également vérifiée dans la matrice de tailles.
- Radar entièrement contenu dans son panneau, avec mise à l'échelle uniforme et coordonnées de souris correspondantes.
- Suppression des anciennes règles responsive contradictoires et du tronquage des noms des modes d'alimentation.
- Vérification automatique dans WebView2 des résolutions 720p à 8K, des échelles 100 à 400 %, des formats ultralarges/portrait et des variantes de matériel et de profils d'alimentation. Captures et mesures conservées hors Git.

## 2026-09-25 — 1.0.16.0

- Conservation de la scène réelle du bureau pendant la pause : WebView2 reste visible, sans suspension Chromium ni reconstruction à la reprise.
- Arrêt des animations Canvas/CSS, des transitions, de l'horloge et des nouvelles collectes sous couverture ; conservation des particules et des valeurs déjà affichées.
- Synchronisation de l'état de pause au chargement et après récupération, premier affichage directement lisible, protection contre les collectes et callbacks devenus périmés.
- Couverture opaque calculée sur tous les écrans, avec exclusion des fenêtres transparentes et des surfaces système ; un écran encore visible reste animé.
- Réintégration des corrections distantes de septembre (hook souris dédié, collecte NVIDIA isolée, récupération WebView2 et affichage TV), absentes de l'essai local 1.0.15.0.
- Source de version unique `VERSION` pour l'EXE, l'installeur et le MSIX. Le build de l'installeur conserve les packages MSIX existants.
- Tests réels WebView2 de conservation de scène et harnais de captures du bureau avec chronologie, fenêtres maximisées/juxtaposées/plein écran, couverture partielle/transparente et transitions répétées.
- La 1.0.15.0 locale, non publiée au Store, masquait WebView2 et provoquait une réapparition tardive : ses mesures CPU/GPU ne constituaient pas une validation visuelle.
## 2026-09-10

- Isolation du hook souris dans un thread dedie : les attentes de l'affichage WebView2 et de la detection plein ecran ne bloquent plus les mouvements de souris du bureau.
- Publication atomique des coordonnees du panneau interactif depuis le thread d'affichage et retrait des acces WinForms, recherches de fenetres et ecritures de journal du callback souris.
- Conservation des clics du selecteur d'alimentation, avec verification de leur transfert avant de les intercepter.
- Passage du package MSIX en version `1.0.14.0` et de l'installateur autonome en version `1.0.12`.

## 2026-09-04

- Suppression des appels NVML dans le processus principal et collecte NVIDIA isolee via `nvidia-smi`, afin qu'un plantage du pilote ne puisse plus arreter NexusWpp.
- Recuperation automatique de WebView2 et protection des acces asynchrones pendant sa fermeture.
- Adaptation verticale du cockpit aux affichages 4K fortement mis a l'echelle, notamment les televiseurs 16:9, sans couper les cartes du bas.
- Exclusion du panneau de saisie tactile Windows de la detection plein ecran pour eviter une suspension permanente de la telemetrie.
- Passage du package Microsoft Store en version `1.0.13.0` et de l'installateur autonome en version `1.0.11`.

## 2026-07-23

- Passage des mises à jour GitHub Actions de Dependabot à un rythme mensuel,
  regroupées dans une seule PR au maximum.

## 2026-06-20

- Correction du crash WebView2 a la fermeture quand le controle recree son handle Windows apres avoir ete detruit.
- Ajout d'une recuperation automatique quand WebView2 est detruit sans fermeture complete de NexusWpp.

## 2026-06-19

- Ajout d'une CI GitHub Actions Windows pour compiler NexusWpp a chaque push.
- Ajout d'une politique de securite, de Dependabot et d'un scan GitHub Actions Gitleaks/TruffleHog hebdomadaire.
- Audit de proprete du depot et documentation de release actualisee apres verification de la compilation, de l'installeur EXE et du package MSIX.

## 2026-06-15

- Version MSIX montee a 1.0.12.0 pour publier le redemarrage automatique apres mise a jour Store.
- Enregistrement de l'hote natif aupres du Restart Manager Windows afin que le fond d'ecran soit relance quand une mise a jour MSIX ferme l'application.
- Version MSIX montee a 1.0.11.0 pour publier le correctif de plaque noire derriere l'icone Windows.
- Generation des assets d'icone MSIX `targetsize` et `altform-unplated`, avec index `resources.pri`, pour conserver la transparence dans le shell Windows.
- Audit du projet, nettoyage des liaisons JS/CSS mortes, alignement de la version de l'installeur EXE sur 1.0.10 et detection robuste des outils Windows SDK pour le build MSIX.
- Version MSIX montee a 1.0.10.0 pour publier le correctif des clics d'alimentation.
- Correction du transfert des clics du selecteur de modes d'alimentation depuis le fond d'ecran Windows vers WebView2, avec prise en compte du DPI reel du WebView.

## 2026-06-12

- Version MSIX montee a 1.0.9.0 pour publier le correctif d'icone Store.
- Regeneration des icones NexusWpp en haute definition avec transparence native, sans fond noir dans le MSIX.
- Version MSIX montee a 1.0.8.0 pour publier le correctif de demarrage Windows.
- Ajout d'une tache de demarrage MSIX `windows.startupTask` activee pour relancer NexusWpp a l'ouverture de session.

## 2026-06-11

- Version MSIX montee a 1.0.7.0 pour publier le correctif de reprise apres veille.

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
