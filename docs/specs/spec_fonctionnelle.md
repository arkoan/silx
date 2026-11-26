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
- Export du schéma (image, PDF) pour documentation

### 3.2 Moteur de simulation
- Backend ngspice (CLI dans un premier temps, `libngspice` si besoin de perf)
- Types d'analyse supportés : DC, AC, Transient, Noise, FFT
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
- Évaluation automatique pass/fail contre un ou plusieurs gabarits
- Gabarits dessinés : leurs points se posent et se déplacent dans la zone de tracé visible seulement (ils
  restent toujours attrapables)
- **Bancs de test en calques** : le circuit sous test ne contient que le montage ; chaque banc est un
  calque posé dessus (sources, charges, analyses, gabarits). Depuis un banc, la **topologie** du circuit
  est verrouillée (câblage, ajout ou retrait de composants : onglet « Circuit sous test »), mais ses
  **valeurs restent modifiables** pour la mise au point : clic sur un composant estompé, dans la bulle
  d'édition. Une valeur modifiée vaut pour tous les bancs.
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

### 3.5 Stimuli
- Bibliothèque de stimuli standards (échelon, rampe, sinus, bruit, PWL, PRBS)
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

### 3.9 Intégration IA (MCP)
- Serveur MCP exposant les tools nécessaires aux quatre rôles IA :
  - `generate_testbench`, `generate_mask`
  - `run_simulation_batch`, `run_monte_carlo`, `run_parametric`
  - `get_test_summary`, `get_worst_case`, `get_sensitivity`, `get_raw_curve`
  - `propose_modification`, `apply_and_test`, `optimize_parameter`
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