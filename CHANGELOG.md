# Changelog

## 2026-10-28 — Politique de sécurité

- `SECURITY.md` : signalement privé par GitHub, délais de réponse et de correction, publication en avis de sécurité.

## 2026-10-27 — 1.0.49.0

### Médaillon net en grand

- Le médaillon des icônes passe de 240 à 600 px : `scripts/make_medallion.py` le refait à partir de `julienpiron.png` avec le même disque intérieur estompé que le kit d'icônes Nexus. Les grands logos du menu Démarrer et du paquet, l'icône 256 px de l'exe et les logos de la fiche Store ne sont plus flous.
- `splash.jpg`, livré dans chaque installation sans être utilisé nulle part, est retiré.

## 2026-10-27 — 1.0.48.0

### Petites icônes en médaillon rond

- Sous 44 px, l'icône est désormais le médaillon de julienpiron.fr découpé en rond (coins transparents) au lieu du pictogramme seul, selon la nouvelle règle du kit d'icônes Nexus : barre des tâches, zone de notification, barre de titre des réglages, petites tailles du menu Démarrer et de l'exe. À partir de 44 px, rien ne change.
- `branding/nexuswpp-round.svg` remplace `branding/nexuswpp-icon.svg` ; `scripts/export_icons.py` régénère `icon.ico` et les logos MSIX.

## 2026-10-26 — 1.0.47.0

### Nouvelle icône NexusWpp

- NexusWpp a sa propre icône, celle de la famille Nexus : un écran vert avec jauge et barres, posé dans le médaillon de julienpiron.fr. Elle remplace le logo π du site dans l'exécutable, la barre des tâches, la zone de notification, la fenêtre de réglages, le menu Démarrer, les tuiles et le paquet Store.
- Sous 44 px, le pictogramme seul (le médaillon ne se lit plus) ; à partir de 44 px, le médaillon. La barre de titre des réglages et la zone de notification lisent `icon.ico` à leur taille : avant, Windows y réduisait l'image 48 px à 125 %.
- Source unique dans `branding/` (SVG du kit d'icônes Nexus) : `scripts/export_icons.py` en tire `icon.ico` et chaque logo MSIX à sa taille exacte, au lieu de réduire `julienpiron.png` au moment du paquet.
- Le logo par défaut du radar du cockpit (`julienpiron.png`, remplaçable dans les réglages) ne change pas.

## 2026-10-20 — 1.0.46.0

### Modes d'alimentation toujours à jour

- Les boutons de mode suivent les modes de Windows sans redémarrer NexusWpp : un mode ajouté, supprimé ou renommé (powercfg, Paramètres, pilote) apparaît ou disparaît en quelques secondes. La liste n'était lue qu'au démarrage : après la suppression de deux modes, le panneau en montrait encore cinq.
- Un nom long passe à la ligne entre deux mots, plus au milieu d'un mot (« PERFORMANCE / S OPTIMALES ») : l'étiquette réduit juste assez sa police pour que son mot le plus long tienne sur une ligne.
- Tests de disposition : aucun mot coupé dans les boutons de mode ; cinq modes sur des écrans de bureau réels.

## 2026-10-20 — Chaîne d'approvisionnement GitHub

- Actions GitHub épinglées par empreinte (SHA + version en commentaire), checkout sans identifiants persistés ;
  zizmor sans trouvaille moyenne ni haute.
- Dependabot ajouté avec un délai de 7 jours (cooldown) et un regroupement des mises à jour : GitHub Actions et NuGet (WebView2).
- Workflow OpenSSF Scorecard et badge dans le README.

## 2026-10-15 — 1.0.45.0

### Retrait du battement de pause de la 1.0.44.0

- La cause attribuée en 1.0.44.0 était fausse. L'écran noir (AOC 5120x1440 non certifié G-SYNC, VRR forcé par le pilote NVIDIA) venait de G-SYNC : il persistait sans aucune pause de NexusWpp et même avec l'EDID d'usine, et disparaît dès que G-SYNC est désactivé pour cet écran. Les essais A/B précédents étaient trompés par un problème intermittent.
- Le pixel invisible rafraîchi une fois par seconde pendant la pause est retiré : la pause redevient totale, comme en 1.0.43.0.

## 2026-10-15 — 1.0.44.0

### Fin de l'écran noir au retour de pause

- Deux fenêtres côte à côte (ou rangées par Snap) mettent NexusWpp en pause ; en déplacer, fermer ou réduire une faisait passer tout l'écran au noir quelques secondes (AOC 5120x1440 G-SYNC sur la NVIDIA, sans déconnexion vue par Windows). Cause isolée par essais : la surface WebView2 de la taille du bureau ne présentait plus aucune image pendant la pause, et la reprise brutale faisait réorganiser l'affichage au pilote NVIDIA. Sans NexusWpp, sans pause, ou page qui dessine pendant la pause : plus de noir.
- Pendant la pause, la page change un pixel invisible une fois par seconde : la surface reste vivante, la pause reste quasi gratuite (une image par seconde au lieu de 12). Vérifié sur le poste : plus d'écran noir.
- Tests de pause : battement présent pendant la pause, arrêté à la reprise.

## 2026-10-14 — 1.0.43.0

### Processus principal harmonisé

- Les cinq cartes (CPU, iGPU, NPU, GPU, RAM) montrent le processus le plus actif de la même façon : libellé « TOP PROCESSUS » partout (« Utilisé par » disparaît), toujours dans la dernière case (ligne 2, à droite), valeur « nom + mesure » (« MsMpEng 1 % », « java 3,2 Go », « dwm 12 % »). Les cartes graphiques et le NPU affichent désormais la part de charge du processus, déjà mesurée par l'hôte. « Aucun » au repos au lieu de « AUCUN ».
- CPU et RAM : le nom est raccourci juste assez pour tenir dans la case, comme sur les autres cartes (au lieu d'une coupe fixe à 11 ou 13 caractères) ; une lecture manquante garde la dernière valeur, grisée.
- Carte GPU : la case VRAM est traduite et le texte par défaut de « Puissance GPU » ne dit plus « CORE CLOCK ».
- Tests : même libellé, même case et même forme « nom + mesure » sur toutes les cartes ; « Aucun » sans mesure au repos.
- Tests de disposition : le programme de test libère WebView2 et se termine à la fin de sa série, au lieu de rester parfois bloqué en verrouillant ses DLL.

### Tests WebView2 exclusifs par dépôt

- Tests de disposition, de pause et des réglages : un seul lancement à la fois par copie du dépôt (`scripts\test_lock.ps1`, verrou sur fichier dans `work\locks`, libéré par Windows même si le processus est tué). Un second lancement (autre agent, `Test-NexusProjets.ps1`) attend son tour en indiquant qui tient le verrou, au lieu d'écraser les binaires et le profil WebView2 du premier. Cause des arrêts « code -1 sans exception » : un lancement concurrent tuait le programme de test en cours par `Stop-Process -Force`.
- Tests de disposition : le code de sortie du programme est indiqué en cas d'échec, et un arrêt forcé par un autre processus (-1) est signalé comme tel.

## 2026-10-14 — Point d'entrée de contrôle commun

- `test.ps1` à la racine : contrôle complet du projet en une commande, hors ligne et sans rien laisser dans le dépôt (convention commune à tous les dépôts de Julien, lancée pour tous par `NexusInfra\ops\workstation\Test-NexusProjets.ps1`). Mêmes étapes que la CI : compilation puis tests de régression.

## 2026-10-14 — Automatisations GitHub allégées

- Mises à jour Dependabot automatiques retirées (`.github/dependabot.yml`) ; les alertes de faille
  GitHub restent actives. Dépôt public : ses workflows ne consomment pas de minutes et restent inchangés.

## 2026-10-13 — 1.0.42.0

- Carte GPU : la fréquence mémoire passe dans le titre (« … @ 15001 MHz »), comme la vitesse de la RAM ; sa case montre « Utilisé par », le programme qui sollicite le plus la carte graphique (3D, calcul, vidéo). « AUCUN » quand la carte est au repos.
- Carte iGPU : « VRAM 1,2 / 16 Go » en une seule case, comme sur la carte GPU ; « Encodage vidéo » (moteur VideoEncode de Windows) et « Utilisé par » remplacent « VRAM libre » et « VRAM totale », qui répétaient la même mesure.
- Tests : programme utilisé par l'iGPU et le GPU, fréquence mémoire dans le titre, VRAM utilisée / totale et encodage vidéo.

## 2026-10-13 — 1.0.41.0

- Carte NPU : « Utilisé par » montre le programme qui utilise le NPU (lu dans les compteurs GPU de Windows, par processus) ; remplace « Mémoire partagée », qui répétait « Mémoire utilisée ». « AUCUN » quand rien ne l'utilise. Le nom est raccourci juste assez pour tenir dans la carte.
- Batterie (portables) : sur batterie, la ligne du centre ajoute la consommation en watts et le temps restant estimé par Windows ; en charge, la puissance de charge. Lu par l'API d'alimentation de Windows, sans WMI. Rien d'inventé quand Windows ne fournit pas la valeur.
- Tests : 45 cas de disposition « portable sur batterie », tests du programme NPU et de la batterie.

## 2026-10-13 — 1.0.40.0

- Courbes d'historique sur toute la hauteur des cartes, en filigrane derrière les valeurs.
- Fin des coupures noires : un changement d'affichage (bascule de carte graphique, réveil, résolution) redimensionne le fond d'écran sur place au lieu de le détacher puis le rattacher au bureau ; le journal en comptait 534 en pleine session. Le double rattachement au démarrage disparaît aussi.
- WebView2 ne calcule plus lui-même le masquage de la fenêtre (CalculateNativeWinOcclusion) : quand deux fenêtres couvrent tout l'écran, l'image n'est plus jetée ; NexusWpp gère seul sa pause.
- Journal : vraie version de Windows (RtlGetVersion) au lieu de 6.2.9200.

## 2026-10-13 — 1.0.39.0

Audit complet sur 8 axes (86 constats vérifiés) et corrections :

- Valeurs manquantes : la dernière valeur mesurée reste affichée, grisée avec le badge « Capteurs périmés » ou « Données périmées », au lieu d'un « — » qui faisait sauter l'image. « — » ne reste que pour un capteur qui n'a jamais répondu.
- Aucune valeur inventée : plus de « ETHERNET » hors ligne (affiche « HORS LIGNE »), plus de 180 processus / 1200 threads ni de 3,80 GHz par défaut ; une carte Intel Arc dédiée n'est plus prise pour une puce intégrée ; après une pause ou une mise en veille, les débits repartent d'une mesure fraîche.
- Wi-Fi : le signal est lu sans l'API que Windows 11 soumet à la localisation ; NexusWpp ne déclenche plus l'autorisation de localisation.
- Multi-écrans : le fond d'écran ne part plus hors écran avec un moniteur à gauche ou au-dessus du principal ; le cockpit se place sur l'écran principal au lieu d'être coupé à la jointure. Sur un seul écran, rien ne change. Détection de pause cohérente avec des DPI différents.
- Auto-réparation : WebView2 est relancé s'il meurt (un arrêt de 42 h avait été constaté), avec un traitement adapté à chaque type de panne ; NexusWpp se rattache après un redémarrage d'Explorer au lieu de se fermer.
- Clics : la fenêtre de réglages et ses boîtes de dialogue gardent leurs clics ; appui et relâchement sont traités ensemble ; le hook souris est retiré pendant la pause.
- Pause aussi quand l'écran est éteint.
- Consommation : collecte de télémétrie en ~16 ms au lieu de ~63 ms (réseau et disque via les compteurs Windows), nvidia-smi espacé sur batterie et jamais quand la carte dort ; animations calées sur une seule horloge à 12 Hz (page ~70 % moins coûteuse en banc de mesure ; sur le bureau réel 5120x1440, environ 21 à 30 % d'un cœur au total selon les mesures).
- Radar net en haute résolution (125 à 200 %). Réglage Windows « Effets d'animation » respecté.
- Capteurs GPU AMD et Intel dédiés (température, fréquence mémoire, ventilateur) via WDDM, sans pilote supplémentaire.
- Panneau d'alimentation : plans lus par l'API Windows et triés Économie → Normal → Performances ; sur les portables qui n'ont qu'un plan, les trois modes d'alimentation de Windows sont proposés.
- Nombres : virgule décimale en français ; plus de « Mo »/« Go » dans l'interface anglaise.
- Réglages : accueil au premier lancement, version, bouton « Noter NexusWpp », accès au démarrage avec Windows et au dossier des journaux ; import de logo plus sûr (orientation des photos, taille limitée, PNG/JPEG/BMP) ; la fenêtre passe au premier plan quand on relance NexusWpp.
- Installeur EXE : dossier sécurisé, fond d'écran lancé sans droits administrateur, runtime WebView2 vérifié par signature Microsoft ; la désinstallation redessine le fond Windows et supprime les données de l'utilisateur.
- Sécurité : WebView2 verrouillé (menus, outils de développement, zoom, téléchargements, permissions) et politique de sécurité du contenu stricte ; adresses IP retirées des données envoyées à la page.
- Diagnostics : version et vraie version de Windows dans le journal, journal limité à 1 Mo, vrais plantages remontés à Windows.
- CI : nouvelles suites d'interface lancées sur GitHub ; script `scripts	ag_release.ps1` pour étiqueter chaque version.
- Store : nom de fiche avec mots-clés, « NexusWpp – Fond d'écran moniteur système » et « NexusWpp – Live System Monitor Wallpaper » (noms réservés, déclarés par langue dans le paquet) ; le menu Démarrer garde « NexusWpp ».

- Fenêtre de réglages : le texte d'aide n'est plus coupé avec une mise à l'échelle Windows de 125 % ou plus. La mise à l'échelle automatique n'était pas appliquée (construction sans `SuspendLayout`/`ResumeLayout`) et les textes avaient des tailles fixes ; la fenêtre utilise maintenant une mise en page automatique qui s'adapte au texte, à la langue et au DPI.
- Test : la fenêtre de réglages est vérifiée de 100 % à 200 %, avec et sans mise à l'échelle de la mise en page (aucun texte coupé, hors de la fenêtre ou superposé). Ce test échoue sur la version 1.0.37.

## 2026-10-13 — 1.0.38.0

- Historique des 60 dernières secondes dans chaque carte (CPU, iGPU, NPU, GPU, RAM, activité disque, réseau) : une courbe discrète dans la couleur de la carte, en bas et derrière le contenu, sans changer la disposition. Une mesure manquante coupe la courbe au lieu d'afficher un faux zéro.
- Fenêtre de réglages plus aérée.
- Code réorganisé sans changement de comportement : l'hôte est découpé en classes partielles par responsabilité (`Program.cs`, `DesktopForm*.cs`, `NativeMethods.cs`) et l'interface en scripts chargés dans l'ordre (`cockpit-state.js`, `radar.js`, `dashboard.js`, `app.js`). Les scripts de test lisent la liste des sources dans `scripts\host_sources.ps1`.
- Captures Store : cockpit, écran ultra-large, portrait, logo personnalisé et fenêtre de réglages, en français et en anglais, générées par `scripts\store_screenshots.ps1` avec les vraies mesures du PC.

## 2026-10-13 — 1.0.37.0

- Réglages : icône NexusWpp dans la zone de notification (clic : réglages ; menu : réglages, quitter). Relancer NexusWpp depuis le menu Démarrer ouvre aussi les réglages. Il est enfin possible de quitter l'application sans le Gestionnaire des tâches ; le fond d'écran Windows est alors redessiné.
- Logo central personnalisable : n'importe quelle image (PNG, JPEG, BMP, GIF, TIFF) est recadrée en carré au centre, normalisée en 512 px et appliquée en direct ; retour au logo d'origine en un clic. Une image illisible revient au logo d'origine.
- Télémétrie beaucoup plus légère : mémoire, système, fréquence CPU, GPU/NPU et classement des processus passent des requêtes WMI aux compteurs natifs Windows (PDH). Une collecte prend environ 50 ms au lieu d'environ 1,4 s ; l'hôte passe d'environ 15,6 % à 2,4 % d'un cœur (bureau visible, mesure A/B sur 20 s), et le service WMI n'apparaît plus comme processus le plus actif.
- Premier affichage plus rapide : une collecte d'échauffement au démarrage donne des valeurs dès la première image, même quand l'application démarre derrière des fenêtres ; le classement des processus est disponible dès la deuxième seconde.
- Package Store : l'anglais est déclaré en plus du français (langues prises en charge affichées par le Store).
- Tests : import du logo (recadrage, formats, rejets), fenêtre de réglages, logo côté page et compteurs PDH.

## 2026-10-13 — 1.0.35.0

- Interface en anglais : la langue suit celle de Windows (français sur un Windows en français, anglais partout ailleurs), y compris la date, les unités (Go/GB, Mo/s/MB/s) et les messages d'accessibilité.
- Icône « économie » reconnue aussi pour les modes d'alimentation Windows en anglais (« Power saver »).
- Test de disposition exécuté dans les deux langues (1230 cas chacune) ; captures Store générées en français et en anglais.

## 2026-10-13 — 1.0.34.0

- Économiseur d'énergie Windows : quand il est activé, les deux anneaux du logo s'immobilisent (c'est l'animation qui coûte le plus) ; valeurs, horloge et paquets de données continuent de se mettre à jour. Aucun changement visuel le reste du temps.
- Compilation modernisée : projet `NexusWpp.csproj` compilé par le SDK .NET (C# récent) au lieu du `csc.exe` C# 5 de Windows. L'application cible toujours .NET Framework 4.8, intégré à Windows : même taille, aucun runtime à installer.
- WebView2 SDK mis à jour de 1.0.2592.51 à 1.0.4258.31, récupéré par NuGet.
- Tests et installeur compilés avec le même compilateur moderne (`scripts\csc.ps1`).

## 2026-10-12 — 1.0.33.0

Revue complète du code, corrections vérifiées :

- Disque : l'activité réelle et le temps de réponse étaient toujours à 0 (compteur WMI lu avec le mauvais type, erreur masquée). Ils sont désormais calculés à partir des compteurs bruts du disque système.
- Ping : les cibles internet (1.1.1.1 puis 8.8.8.8) sont toujours essayées avant la passerelle ; une latence mesurée jusqu'au routeur est affichée « (routeur) » et n'est plus présentée comme une latence internet. 0 ms est une mesure valide. Un ping sans réponse ne peut plus bloquer la mesure.
- Pause : les hooks d'événements Windows n'existent plus que pendant la pause (ils réveillaient l'hôte à chaque événement système même fond d'écran visible), filtrés aux fenêtres de premier niveau ; la confirmation de 1,5 s est mesurée en temps réel ; cache des noms de processus limité à 10 s.
- Démarrage : si WebView2 ne peut pas démarrer, l'application se ferme au lieu de laisser un bureau gris et de bloquer toute relance.
- Robustesse matérielle : lectures WMI tolérantes aux valeurs absentes (machines virtuelles, ARM), VRAM bornée à 100 %, volume de taille nulle, erreurs WMI GPU contenues, `powercfg` bloqué interrompu après 3 s.
- Interface : les jauges ne rejouent plus d'animation depuis d'anciennes valeurs même si la première mesure tarde ; particules sans flash opaque ; barres remises à zéro quand les données expirent ; alertes accessibles réécrites seulement quand elles changent ; un ancien résultat de changement de mode d'alimentation n'annule plus un clic plus récent.
- Installation EXE : le dossier `C:\nexuswpp` n'est plus modifiable par les comptes standard ; l'installation n'arrête plus les processus WebView2 d'autres applications ; exécutable compilé en x64 explicite.
- Test de reprise visuelle : option `-DevBuild` pour mesurer la version de développement.

## 2026-10-12 — 1.0.32.0

- Consommation du fond d'écran visible presque divisée par deux : les deux anneaux rotatifs du logo ne forcent plus 60 compositions par seconde ; ils tournent à 12 images/s, comme le reste du rendu, avec le même mouvement. Mesure A/B sur 20 s (processus hôte + WebView2) : environ 71 % d'un cœur avant, environ 39 % après (GPU WebView2 ≈ 45 % → ≈ 19 %, rendu ≈ 17 % → ≈ 9 %).

## 2026-10-12 — 1.0.31.0

- Ping réseau plus universel : si 1.1.1.1 ne répond pas (réseau d'entreprise, ICMP public filtré), la mesure bascule sur 8.8.8.8 puis sur la passerelle locale, et mémorise la cible qui répond. L'indicateur n'est indisponible que si les trois échouent.

## 2026-10-12 — 1.0.30.0

- Disque système détecté dynamiquement (variable `SystemDrive`) au lieu de `C:` figé : charge, espace libre, nom du disque et libellé du radar suivent le vrai lecteur Windows, y compris sur les PC installés sur une autre lettre.

## 2026-10-12 — 1.0.29.0

- Reprise déclenchée par les événements Windows (premier plan, déplacement, minimisation, fermeture de fenêtre) : tant que le bureau est couvert, un changement de fenêtre relance la vérification en 40 ms au lieu d'attendre le prochain sondage. Aucun coût lorsque le fond d'écran est visible.

## 2026-10-12 — 1.0.28.0

- Correction de l'affichage « Données périmées » et des tirets qui apparaissaient à la reprise après une pause : la durée de la pause n'est plus comptée comme une panne de télémétrie, les dernières valeurs restent affichées jusqu'à la première mesure fraîche.
- Test de régression ajouté (reprise après une longue pause).

## 2026-10-12 — 1.0.27.0

- Reprise plus rapide du fond d'écran : détection du bureau visible toutes les 250 ms (au lieu de 500 ms) et première image dessinée immédiatement à la reprise.
- Fin des saccades : la pause n'est confirmée qu'après 1,5 s de bureau réellement couvert, ce qui évite les cycles pause/reprise lors des changements de fenêtre.
- Les jauges affichent directement les valeurs fraîches à la reprise, sans animation de rattrapage depuis d'anciennes valeurs.
- Le journal indique désormais les fenêtres qui couvrent le bureau (processus, classe, taille, position) pour diagnostiquer les écrans larges ; noms de processus mis en cache pour réduire le coût de la détection.

## 2026-09-28 — 1.0.26.0

- Réseau reste exactement au-dessus du logo central, quel que soit le nombre de composants affichés ; les autres ronds du radar se répartissent symétriquement à rayon et espacement angulaire constants.
- Vérification de l'axe, de la symétrie et de l'espacement dans la matrice WebView2, avec ajout de la configuration à six ronds.

## 2026-09-28 — 1.0.25.0

- Détection des moteurs NPU « Neural », notamment Intel AI Boost, en complément de « Compute » : la charge et la mémoire partagée sont désormais lues sur le bon adaptateur.
- Affichage d'un vrai 0 % au repos lorsque les compteurs répondent ; conservation de l'état indisponible en l'absence de mesure. Les moteurs distincts ne sont pas additionnés.

## 2026-09-25 — 1.0.24.0

- Alertes « Charge élevée » et « Mémoire très utilisée » intégrées au titre sans chevauchement ; confirmation à 90 % pendant 5 secondes et retour à 80 % pendant 5 secondes, sans clignotement ni tremblement.
- Capteurs NVIDIA traités individuellement, anciennes valeurs invalidées après échec, indication des données périmées et conservation de la précision des watts. Les puissances affichées concernent la carte entière.
- Collecte des moteurs et de la mémoire GPU Windows regroupée en deux requêtes ; pourcentages calculés par moteur, sans additionner des moteurs distincts.
- Transition des jauges raccourcie après profilage A/B ; halos et radar à 12 images/s conservés.
- Séparation de la collecte et des capteurs GPU en modules testables ; manifeste de fichiers livré partagé entre les modes d’installation.
- Matrice de disposition étendue aux charges élevées et aux capteurs périmés ; vérification de la récupération après une interruption des mesures.
- Protection de l’initialisation WebView2 contre les récupérations concurrentes ; notifications d’écran traitées sur le thread de l’interface.

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
