# Spécification fonctionnelle — Outil de simulation électronique assisté par IA

## 1. Vision du produit

Logiciel de saisie de schéma et de simulation de circuits électroniques
(type LTspice), combinant :
- un véritable éditeur de schéma graphique avec gestion de symboles et
  bibliothèques de modèles,
- un moteur de simulation SPICE éprouvé (ngspice) comme socle de calcul,
- une orchestration de campagnes de tests paramétriques et statistiques,
- une couche d'assistance par IA (via MCP) couvrant quatre rôles distincts :
  interprétation d'intention, exécution de tests, analyse/rapport, assistance
  à la conception.

L'IA ne réalise jamais elle-même un calcul électrique : elle pilote, interprète
et explique un socle de calcul déterministe (ngspice + bibliothèque de mesures).

## 2. Utilisateurs cibles

- Ingénieur électronicien individuel (hobbyiste avancé, freelance, PME)
- Besoin : concevoir, simuler, caractériser, documenter un design sans
  lourdeur d'outillage industriel (Cadence, etc.), avec un gain de temps
  apporté par l'IA sur les tâches répétitives (définition de gabarits,
  rédaction de rapports, exploration de design).

## 3. Primitives fonctionnelles

### 3.1 Saisie de schéma et gestion de circuits

Le logiciel est un véritable outil de **saisie de schéma électronique**
(schematic capture), au même titre que LTspice, Qucs ou KiCad — pas un simple
éditeur de netlist texte avec génération de rapport autour.

**Éditeur de schéma graphique**
- Canevas de dessin avec grille, snap automatique, zoom/pan fluide
- Placement de symboles par glisser-déposer ou recherche rapide (palette de
  composants)
- Câblage interactif (fils, jonctions, labels de nœuds, bus si pertinent)
- Rotation/miroir de symboles, alignement, groupement
- Annotation du schéma (texte libre, cartouche, numérotation automatique
  des références de composants — R1, R2, C1...)
- Génération automatique et bidirectionnelle de la netlist SPICE à partir
  du schéma (et réciproquement : modification de la netlist reflétée sur
  le schéma quand c'est possible)
- Vérification de cohérence électrique basique (nœuds flottants, pins non
  connectées, références dupliquées) avant simulation
- Undo/redo complet sur toutes les opérations de schéma

**Éditeur de symboles**
- Création/édition de symboles graphiques (formes, pins nommées, orientation)
- Association d'un symbole à un modèle SPICE (fichier `.lib`/`.mod`/`.sub`)
  via une interface dédiée, pensée pour être nettement plus simple que
  l'association manuelle typique de LTspice (point faible connu à corriger)
- Bibliothèque de symboles standards fournie par défaut (composants discrets,
  sources, composants actifs courants). Dans la palette, toujours présents : R, C, L, diode, sources, masse,
  étiquette ; puis, par catégorie, des bipolaires (2N3904, 2N3906, 2N2222A, BC547B, BC557B), JFET
  (2N5457, J201, 2N5460), MOSFET discrets avec diode de corps (2N7000, BS250, IRF540N, IRF9540), quartz
  (32,768 kHz, 4, 8, 16 MHz) et portes et bascules logiques XSPICE. Modèles indicatifs, remplacés par ceux du
  fabricant à l'import.
- Tailles : dipôles (R, C, L, D, sources, quartz) sur **3 pas** de grille ; transistors sur 4 pas de haut,
  dessinés finement au quart de pas ; MOSFET compact, 1 pas de large et sans cercle (diode de corps à droite). Un projet enregistré avec les anciens dipôles sur 4 pas (format 1)
  reçoit à l'ouverture un court fil de raccord par dipôle : aucune connexion n'est perdue.

**Gestion de bibliothèques**
- Scan automatique de dossiers de bibliothèques (`.lib`, `.mod`, `.sub`, `.asy`
  ou format de symbole propre à l'outil)
- Indexation et recherche de composants (par nom, type, fabricant, paramètres)
- Import de bibliothèques tierces (fabricants de composants) en un clic,
  avec association automatique symbole ↔ modèle quand c'est déductible
- Gestion de plusieurs bibliothèques en parallèle (projet, utilisateur, globales)

**Import/export**
- Import de netlists SPICE existantes (texte), avec reconstruction d'un
  schéma lisible quand c'est possible (placement automatique, à affiner
  manuellement)
- Export de la netlist générée (format SPICE standard, pour interopérabilité
  avec d'autres outils si besoin)
- Export du schéma (image, PDF) pour documentation — **fait** : bouton « Exporter » (et palette de commandes) : le
  schéma (cadré en entier, avec le calque du banc affiché), les courbes du banc, ou les deux, en **PDF** (vectoriel,
  une page par vue), **SVG** (vectoriel, vues empilées) ou **PNG** (2× pour l'impression) ; toujours en thème clair.
- Export des données en **CSV** — **fait** : même bouton, section « Données » : les **points des courbes** du banc
  (temps ou fréquence, puis une colonne par signal des graphiques ; en fréquentiel, module en dB et phase en °) et les
  **mesures** (chaque critère et gabarit avec bornes, valeur, unité, verdict ; avec une campagne, les statistiques par
  critère puis une ligne par tirage : valeurs des composants, température, mesures, marges). Format « à la
  française » : séparateur « ; », virgule décimale, UTF-8 avec BOM (s'ouvre tel quel dans un tableur).

### 3.2 Moteur de simulation
- Backend ngspice (CLI dans un premier temps, `libngspice` si besoin de perf)
- Types d'analyse supportés : DC, AC, Transient, Noise, FFT
- **Analyse DC** — **faite** :
  - *Caractéristique de transfert* (.dc) : un banc dont le premier stimulus a la forme « **balayage DC** » (de, à,
    pas ; enregistrée `DCSWEEP(de à pas)`, écrite « DC de » + `.dc` dans la netlist ; 10 000 points au plus, pas
    négatif si la plage descend) est un banc **DC** : ses courbes ont pour abscisse la source balayée (« V1 (V) »,
    ou en A pour une source de courant), courants sur l'axe de droite. Mesures sous le graphique : min, max,
    **seuil** (abscisse où la sortie franchit le milieu de son excursion), **pente max** (gain DC). Critères :
    valeur maximale, valeur minimale, seuil de basculement, pente maximale ; gabarits dessinés comme ailleurs.
    Curseurs : Δ donne la pente. Modèle de banc « Caractéristique de transfert DC » (0 → 5 V, pas 10 mV).
  - *Point de fonctionnement* (.op) : bouton « V » cerclé de la barre d'outils (et palette de commandes). Sur le
    schéma, la **tension de chaque nœud** (étiquette bleue près de la broche la plus en haut à gauche du nœud) et le
    **courant de chaque composant** (étiquette verte : entrant par la broche 1 ; collecteur / drain pour un
    transistor), sans chevauchement. Avec un banc actif, ses stimuli sont à leur valeur continue (début du
    balayage, valeur à t = 0, polarisation du petit signal) ; recalculé à chaque modification tant qu'il est affiché.
- **Analyse de bruit** (.noise) — **faite** : sur un banc **fréquentiel**, bouton « bruit » de la barre des courbes
  (à côté de Bode). Pour chaque tension de nœud affichée (les courants sont ignorés), une analyse .noise sur la plage
  du banc (20 points par décade), entrée = la source petit signal du banc ; les nœuds sont calculés en parallèle,
  en tâche de fond. Tracé : densité de bruit **en sortie** (trait plein) et **ramenée à l'entrée** (tirets), en
  V/√Hz sur échelle logarithmique (120 dB de dynamique au plus). Sous le graphique : bruit total en sortie et ramené
  à l'entrée (V efficaces intégrés sur la plage), densité à 1 kHz. Curseurs : fréquence et densité. Critères (banc
  fréquentiel) : bruit total en sortie, bruit total ramené à l'entrée, densité de bruit en sortie à 1 kHz ; calculés
  avec la simulation (une analyse .noise par nœud contrôlé), et aussi en campagne. Vérifié : √(4kTR) et √(kT/C)
  d'un filtre RC.
- **Échantillonnage adaptatif (mode « Auto », par défaut)** :
  - *Temporel* : ngspice choisit son pas (pas maximal = durée / 200) ; il pose un point d'arrêt à chaque coin des
    stimuli (fronts) et y resserre ses points, puis les espace sur les paliers. Tous les points calculés sont
    gardés, sans rééchantillonnage.
  - *Fréquentiel* : balayage grossier (20 points par décade, au moins 100 points), puis à chaque passe (12 au plus)
    des sous-balayages linéaires de 20 points sur les intervalles où le gain varie de plus de 3 dB ou la phase de
    plus de 20°, ou bien où la pente du gain change brusquement (résonance cachée entre deux points, ex. la marche
    de 0,014 dB que laisse un quartz). Toutes les zones d'une passe sont calculées dans une seule netlist (une
    analyse .ac par zone), puis les points sont fusionnés en une courbe. Exemple : la résonance série (32 763 Hz,
    35 kΩ) et parallèle (+26 Hz) d'un quartz 32,768 kHz sont trouvées sans réglage, en moins de 5 000 points.
  - *Manuel* (case décochée) : pas maximal imposé en temporel ; en fréquentiel décade (au moins 100 points sur la
    plage) ou linéaire sur une bande étroite. Sur moins d'une demi-décade, l'axe des fréquences est gradué au hertz.
- Saisie des valeurs en notation ingénieur dans tous les champs : « M » majuscule et « meg » = méga (2 MHz,
  4,7 MΩ), « m » minuscule = milli (3 ms, 10 mHz) ; unités tolérées (Hz, s, V, A, Ω, F, H, dB, %). Les valeurs
  de R, C, L sont réécrites sans ambiguïté dans la netlist (« 1meg », jamais « 1M » que SPICE lit milli).
- **Pincement** (pavé tactile du Mac, écran tactile) : zoom **homogène** (les deux axes) autour du point entre les
  doigts, sur le schéma comme sur les courbes (borné aux données), avec la sensibilité du zoom des Paramètres. Le
  glissement à deux doigts garde son comportement (zoom horizontal sur les courbes, zoom sur le schéma).
- **Sonde implicite** : quand le graphique a la main (zone des courbes active), survoler le schéma montre ce qu'on
  sonderait, sans choisir d'outil : un **net** (fil, étiquette) → sa **tension** ; une **broche** → le **courant qui
  y entre** (« I(R1) », « I(Q1, C) », lu dans les vecteurs « .options savecurrents » de ngspice ; indisponible pour
  un sous-circuit ou un composant logique). Un clic l'ajoute au graphique actif sans donner la main au schéma ;
  quand le schéma a la main, pas de sonde (le clic sélectionne). En temporel, les courants ont leur axe à droite.
  L'outil Sonde (P) reste disponible et suit les mêmes règles.
- Courbes : curseurs A / B (double-clic ou touches A et B) en temporel, en Bode et sur la **vue FFT** (fréquence,
  amplitude en dBV, écarts). **Couleur d'un signal** au choix : clic sur sa pastille au-dessus du graphique
  (nuancier, code hexadécimal, « par défaut ») ; enregistrée par banc dans le projet et reprise par les courbes de
  campagne.
- Robustesse : une erreur de simulation, de mesure ou de tracé est signalée dans la barre d'état et journalisée
  (~/Library/Logs/Silx/silx.log) ; elle ne ferme jamais l'application.
- Exécution en parallèle de runs indépendants, bornée au nombre de cœurs
  disponibles

### 3.3 Gabarits de test (le cœur du produit)
- Définition d'un **gabarit** : critère quantifié + métrique + analyse associée
  (ex : marge de phase ≥ 45° à 4 MHz en analyse AC)
- Bibliothèque de fonctions de mesure réutilisables, implémentées et validées
  en dur (pas de calcul délégué à l'IA) :
  - marge de phase / marge de gain
  - bande passante à -3 dB
  - dépassement (overshoot), temps de montée, temps d'établissement (settling time)
  - distorsion harmonique totale (THD)
  - gain DC, offset
  - (extensible : liste ouverte, ajout de nouvelles mesures sans toucher au moteur)
- Mesures ajoutées : fréquence et amplitude crête à crête en régime établi (dernier quart du signal : oscillateurs,
  signaux périodiques), gain maximal et largeur de bande à −3 dB autour du maximum (passe-bande)
- Mesures ajoutées (régime établi, sur des périodes entières quand le signal est périodique) : **valeur efficace**,
  **valeur moyenne**, **THD** (harmoniques 2 à 10, transformée de Fourier exacte sur des périodes entières, sans
  fenêtre ; au moins 3 périodes dans le dernier quart du signal)
- **Critères sur les courants** : un critère chiffré ou un gabarit dessiné peut porter sur un courant sondé
  (« I(R1) », « I(Q1, C) ») : la liste des signaux d'un critère propose les nœuds et les courants présents dans les
  graphiques du banc ; l'unité suit le signal (A au lieu de V)
- Évaluation automatique pass/fail contre un ou plusieurs gabarits
- Gabarits dessinés : leurs points se posent et se déplacent dans la zone de tracé visible seulement (ils
  restent toujours attrapables)
- **Bancs de test en calques** : le circuit sous test ne contient que le montage ; chaque banc est un
  calque posé dessus (sources, charges, analyses, gabarits). Depuis un banc, la **topologie** du circuit
  est verrouillée (câblage, ajout ou retrait de composants : onglet « Circuit sous test »), mais ses
  **valeurs restent modifiables** pour la mise au point : clic sur un composant estompé, dans la bulle
  d'édition. Une valeur modifiée vaut pour tous les bancs.
- **Alimentations dans le circuit sous test** : les sources se posent aussi dans le circuit de base (alimentation,
  tension de polarisation commune à toutes les simulations) ; elles sont alors présentes dans tous les bancs.
  Le type d'un banc (temporel ou fréquentiel) reste fixé par ses propres stimuli. Les références restent uniques
  entre le circuit et les bancs (V1 d'un banc, V2 du circuit…).
- **Bulle d'édition** d'un composant (double-clic, ou clic sur un composant du circuit depuis un banc) :
  boutons **OK** et **Annuler** ; Entrée valide, Échap annule (retour à l'état d'ouverture de la bulle).

### 3.4 Campagnes de test
Détail : `docs/specs/balayages_dispersion.md` (validé le 25/11/2026, livré dans l'ordre balayage,
Monte-Carlo, coins). Une campagne se règle dans la roue crantée du banc et répond à « mon circuit
reste-t-il conforme avec des composants réels ? ».
- **Balayage paramétrique** : un paramètre (R, C, L, source continue, ou température), plage linéaire,
  logarithmique ou liste ; une simulation par valeur, courbes superposées en dégradé, mesure tracée en
  fonction du paramètre avec les bornes du critère. Le balayage de température couvre le besoin
  « thermique ».
- **Monte Carlo** : tolérance par composant (± %, défauts par type dans les Paramètres), loi gaussienne
  (± tol. = 3σ) ou uniforme, N tirages (100 par défaut), graine fixe pour la reproductibilité ;
  histogramme par mesure, moyenne, écart-type, Cpk.
- **Coins (pire cas)** : combinaisons extrêmes des tolérances, et de la température si demandé ; Silx
  désigne le coin le plus défavorable.
- Résultats intégrés au plan de test : taux de conformité (« 97,3 % conforme »), statistiques par
  critère, clic sur un tirage en échec pour voir ses courbes et les valeurs des composants en cause.
- Exécution parallélisée sur les cœurs, suivi de progression, annulation possible ; seuls les signaux
  sondés et les mesures sont gardés.
- *Plus tard* : combinaison de plusieurs dimensions (ex : Monte Carlo × thermique), tirages corrélés par
  lot, dispersion des paramètres de modèle (datasheet min / max), coins process (modèles extrêmes).

### 3.4 bis Projets de démonstration
Ouverts en onglets à chaque démarrage, à côté de l'exemple RC (variété), et depuis la palette de commandes :
- **Filtre à quartz 4 MHz** : échelle de deux quartz, terminaisons 470 Ω ; critères gain maximal et largeur de
  bande, gabarits de bande passante et de réjection.
- **Oscillateur Colpitts** : 2N3904 à émetteur commun, ≈ 2,2 MHz ; schéma dessiné dans Silx par Régis (projet
  embarqué) ; alimentation dans le circuit de base, impulsion de démarrage dans le banc ; critères de fréquence et
  d'amplitude.
- **Ampli émetteur commun** : BC547B, ≈ 42 dB de 100 Hz à 800 kHz ; campagne Monte-Carlo réglée (résistances ± 5 %).
Chaque démo est conforme sous ngspice (test automatique).

### 3.4 ter Session et disposition
- **État conservé** à la fermeture et restauré à l'ouverture : onglets de projets (fichier ; contenu non enregistré,
  qui reste marqué modifié ; démonstrations non modifiées recréées à jour), onglet et banc affichés, disposition de
  l'écran, tiroir du bas et sa hauteur (~/Library/Application Support/Silx/session.json). Sans session : l'exemple
  RC et les démonstrations.
- En **courbes seules**, la barre des bancs (pastilles et leur roue crantée) reste affichée au-dessus des courbes.
- Le **tiroir du bas** (netlist, vérifications, journal, campagne) se réduit (« ▾ Réduire », ou re-clic sur l'onglet)
  et se redimensionne par la poignée au-dessus de lui.

### 3.4 quater Installation
- **macOS** : `Silx.app` autonome (runtime .NET inclus, aucune installation de .NET), icône, association des fichiers
  `.silx`, livrée en image disque `.dmg` (glisser dans Applications). Construite par `app/packaging/paquet-macos.sh`.
- **ngspice embarqué** quand le paquet est construit avec `NGSPICE_DIR` (bin/ngspice et modèles de code XSPICE) :
  au premier usage, Silx écrit le spinit qui charge ces modèles depuis l'emplacement réel de l'application et le
  passe à chaque processus ngspice (SPICE_SCRIPTS) ; sinon, ngspice du système (Homebrew).
- Signature ad hoc (pas de notarisation Apple) : au premier lancement d'un `.dmg` téléchargé, macOS demande
  d'ouvrir l'application par clic droit › Ouvrir.

### 3.5 Stimuli
- Bibliothèque de stimuli standards (échelon, rampe, sinus, bruit, PWL, PRBS) — **fait** : formes du panneau de
  propriétés d'une source :
  - continu, impulsion / échelon (PULSE), sinus (SIN), petit signal (AC) ;
  - **rampe** : départ, arrivée, retard, durée (écrite PWL à 3 points, relue comme rampe) ;
  - **PWL** : une ligne « temps ; valeur » par point, saisie ou **importée d'un CSV** (deux colonnes ; « ; »,
    tabulation, espace ou virgule ; virgule ou point décimal ; notation ingénieur ; en-têtes ignorés ; temps
    strictement croissants, 20 Mo au plus) ;
  - **PRBS7** (x⁷ + x⁶ + 1, 127 bits) : niveaux bas / haut, débit, retard, transition ; enregistrée en syntaxe Silx
    `PRBS(v1 v2 débit td tr)` et développée en PWL dans la netlist ;
  - **bruit blanc** gaussien (TRNOISE de ngspice) : décalage, valeur efficace, pas d'échantillonnage ;
  - expression SPICE libre pour le reste (EXP, SFFM, AM…).
  Le front de la rampe et de la PRBS sert d'ancrage aux gabarits relatifs, comme celui d'une impulsion.
- **Export de la forme d'onde** d'un stimulus déterministe en CSV (« temps ; valeur » sur la durée du banc), relisible
  comme source PWL ; le bruit (aléatoire) ne s'exporte pas.
- Génération de stimuli pertinents suggérée par l'IA selon le type de circuit détecté
- Possibilité de stimulus personnalisé (import de fichier PWL, script)

### 3.6 Résultats et rapports
- Visualisation des courbes (temporel, AC bode, FFT, histogrammes Monte Carlo)
- Agrégation de campagne : taux de pass/fail, pire cas, distribution statistique
  (vue « Campagne » du tiroir Détails, section de campagne du rapport de test avec histogrammes et pire cas)
- Analyse de sensibilité (quel paramètre composant influence le plus une métrique)
- Export de rapport (PDF/Markdown) : synthèse rédigée automatiquement par IA,
  conforté par les données de test (pas de texte non vérifiable)

### 3.7 Assistant de conception (IA créative bouclée)
- Mode conseil : propositions de modification argumentées (composant + valeur +
  justification appuyée sur la sensibilité mesurée), chacune re-simulée avant
  présentation comme "amélioration confirmée"
- Mode exploration automatique : recherche bornée sur paramètres jugés sensibles
  (optimisation classique pilotée, pas du pur LLM), restitution du chemin suivi
- L'utilisateur reste toujours décisionnaire final sur l'application d'une modification

### 3.8 Interface utilisateur
- **Vue principale** : éditeur de schéma graphique (canevas central), avec
  panneau de bibliothèques/symboles, panneau de propriétés du composant
  sélectionné, et accès à la netlist texte sous-jacente (vue alternative,
  synchronisée)
- Fenêtre de prompt en langage naturel, accessible depuis le schéma en cours
  (ex : "ce filtre doit être stable avec 45° de marge à 4 MHz")
- Traduction de l'intention en gabarit structuré, visible/éditable par
  l'utilisateur avant lancement (transparence et contrôle)
- Tableau de bord de campagne de test (statuts, progression, résultats)
- Visualiseur de courbes interactif (temporel, AC bode, FFT, histogrammes
  Monte Carlo), avec possibilité de superposer les traces d'un run
  sélectionné directement sur le schéma (probes visuelles sur les nœuds)
- **Barre de menus native** (en haut de l'écran sur macOS) — **faite** :
  - *Silx* : À propos de Silx, Paramètres… (⌘,) ; Masquer et Quitter (⌘Q) fournis par macOS.
  - *Fichier* : Nouveau projet (⌘N), Ouvrir… (⌘O), Ouvrir un projet récent (10 derniers ouverts ou enregistrés,
    « Effacer la liste »), Enregistrer (⌘S), Enregistrer sous… (⇧⌘S), Exporter (schéma, courbes, les deux en
    PDF/SVG/PNG ; points des courbes et mesures en CSV ; rapport Markdown), Fermer le projet (⌘W).
  - *Édition* : Annuler (⌘Z), Rétablir (⇧⌘Z), Couper, Copier, Coller, Dupliquer (⌘D), Supprimer, Tout sélectionner
    (⌘A), Pivoter (⌘R), Miroir, Rechercher une commande ou un composant… (⌘K). Dans un champ texte, ces commandes
    agissent sur le texte.
  - *Affichage* : Schéma seul / Schéma et courbes / Courbes seules (F1, F2, F3), zoom avant, arrière, tout voir
    (⌘+, ⌘−, ⌘0), tiroir du bas (netlist, vérifications, journal, campagne, réduire), explorateur, palette, panneau
    de propriétés, thème (clair, sombre, système).
  - *Placer* : résistance, condensateur, inductance, diode, sources de tension et de courant, masse, étiquette, fil,
    sonde (lettre du raccourci rappelée dans le libellé, sans raccourci de menu pour ne pas gêner la saisie),
    importer un modèle SPICE.
  - *Simulation* : Lancer (⌘↵ ; ⌘R reste « pivoter »), Arrêter (⌘.), simulation automatique, point de
    fonctionnement DC, nouveau banc (modèles), réglages du banc et campagne, circuit sous test.
  - *Aide* : manuel en ligne (README du dépôt), gestes et raccourcis, palette de commandes, démos.
- **Icônes légères** : icônes au trait dessinées sur une grille 24×24 commune et mises à l'échelle telles quelles
  (même corps optique pour toutes, pas d'étirement jusqu'au bord), 16 px par défaut, **trait de 1,15 px** quelle
  que soit la taille ; boutons-icônes avec 6 px de marge.

### 3.9 Intégration IA (MCP)
- Serveur MCP exposant les tools nécessaires aux quatre rôles IA :
  - `generate_testbench`, `generate_mask`
  - `run_simulation_batch`, `run_monte_carlo`, `run_parametric`
  - `get_test_summary`, `get_worst_case`, `get_sensitivity`, `get_raw_curve`
  - `propose_modification`, `apply_and_test`, `optimize_parameter`
- Premier lot livré (serveur `silx-mcp`, transport stdio, `mcp/README.md`) : `open_project`, `describe_project`,
  `list_testbenches`, `list_measurements`, `list_signals`, `get_netlist`, `run_simulation`, `run_campaign`
  (balayage, Monte-Carlo, coins), `get_test_summary`, `get_worst_case`, `get_raw_curve`, `get_operating_point`, `get_noise`, `set_component_value`,
  `save_project`. Les outils de génération, de modification et d'optimisation attendent la structure de l'assistant.
- Agnostique au modèle LLM utilisé (Claude, Mistral, autres) côté client MCP
- Option d'exécution locale d'un modèle (Mistral local via Ollama/LM Studio) pour
  usage offline/confidentiel

## 4. Hors périmètre (v1)

- Simulation numérique/mixte avancée (FPGA, VHDL-AMS)
- PCB layout, routage
- Simulation EM/RF 3D
- Édition de schéma hiérarchique multi-feuilles complexe (sous-circuits
  imbriqués sur plusieurs niveaux) — un schéma simple avec sous-circuits
  de base reste dans le périmètre v1, la hiérarchie profonde est différée

## 5. Critères de succès

- Temps pour définir et lancer une campagne de test (intention → résultat) < 2 min
  pour un cas standard
- Fiabilité : aucune proposition IA présentée comme "validée" sans re-simulation
- Portabilité effective Mac/Windows pour l'éditeur de schéma et le moteur de
  simulation