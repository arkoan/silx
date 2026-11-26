# Chapitre de spécification : Skills d'électronique de l'assistant Silx

Statut : proposition, à valider. Décisions du 26/11/2026 : §1.1 et §10.1 (voir §12).
Complète la spécification fonctionnelle (§3.7 Assistant de conception, §3.9 Intégration IA),
`assistant_documents.md` et `generation_modeles.md`. S'appuie sur le serveur MCP `silx-mcp` (`mcp/README.md`).

---

## 1. Objectif et architecture

L'assistant de Silx est **Claude, spécialisé sans entraînement**, par trois moyens complémentaires :

1. **le serveur MCP de Silx** : Claude simule, mesure et vérifie au lieu de deviner ;
2. **des Skills d'électronique** : des fiches de savoir-faire (un dossier avec un `SKILL.md` et des fichiers de
   référence) que Claude ne charge que lorsque la demande les concerne ;
3. **la bibliothèque documentaire** (datasheets, notes d'application), consultée par outils pendant le raisonnement.

Une Skill dit **comment un électronicien s'y prend** : dans quel ordre poser les questions, quelles analyses lancer,
quels pièges éviter, comment conclure. Elle ne contient ni code de calcul ni valeur à recopier : les nombres viennent
toujours d'un outil (simulation, mesure, évaluateur de formule, citation vérifiée).

Ce chapitre liste les Skills nécessaires, sans les écrire. Pour chacune : nom, déclenchement, contenu, outils MCP
utilisés, outils manquants et priorité.

### 1.1 Consignes permanentes (hors Skills)

Certaines règles valent pour chaque réponse. Elles ne sont pas des Skills (décision du 26/11/2026) (qui se chargent à la demande) mais des
**consignes permanentes** : texte `Instructions` du serveur MCP (déjà en place) et consignes système de l'agent
intégré.

- Aucune valeur présentée comme simulée, mesurée ou calculée sans venir d'un outil ; aucune valeur tirée d'un document
  sans citation vérifiée (`assistant_documents.md` §4.2).
- Trois catégories visibles dans une réponse : **simulé ou calculé par outil**, **sourcé**, **jugement de l'IA**.
- Aucune modification présentée comme « amélioration confirmée » sans re-simulation (spec fonctionnelle §5).
- L'utilisateur décide : toute écriture sur le projet passe par sa confirmation (`save_project`, `apply_and_test`).
- Français, unités SI, notation ingénieur ; « meg » pour méga dans une netlist.
- Une seule question de levée d'ambiguïté à la fois, avec une valeur par défaut recommandée.

### 1.2 Où vivent les Skills

- Dans le dépôt : `assistant/skills/<nom>/SKILL.md`, avec ses fichiers de référence (`references/*.md` : formules,
  tableaux de topologies, check-lists), versionnés et revus comme le code.
- **Claude Code** : livrées dans un plugin « silx » qui déclare aussi le serveur MCP, ou copiées dans `.claude/skills/`.
- **Claude Desktop** : chaque Skill est importable telle quelle (dossier zippé).
- **Agent intégré** (Claude Agent SDK, plus tard) : charge le même dossier, sans copie.
- Le paquet macOS embarque le dossier `skills/` à côté de `silx-mcp`.

Le nom d'une Skill est un identifiant court en minuscules (sans accents) ; sa description, en français, contient les
mots que l'utilisateur emploiera, car c'est elle qui décide du déclenchement.

### 1.3 Qualité

Chaque Skill est livrée avec **2 à 4 scénarios d'essai** joués avec Claude Code et `silx-mcp` sur les projets de
démonstration (filtre RC, filtre à quartz, Colpitts, ampli émetteur commun) ou sur des projets de test du dépôt.
Critères : la Skill se déclenche sur la demande prévue et pas sur les autres, la conclusion repose sur une
simulation, et la réponse ne contient aucun nombre non tracé.

---

## 2. Vue d'ensemble

Priorités : **v1-A** = écrivable dès maintenant avec les outils de `main` et de la PR 12 ; **v1-B** = v1, mais attend
des outils MCP d'écriture ou de calcul (§10) ; **v1-C** = v1, attend le socle documentaire ou la génération de modèles
(jalons J et M) ; **après v1**.

| # | Skill | Famille | Priorité |
|---|---|---|---|
| S1 | `piloter-silx` | Socle | v1-A |
| S2 | `spice-bonnes-pratiques` | Socle | v1-A |
| B1 | `banc-depuis-cahier-des-charges` | Spécifier | v1-B |
| B2 | `plan-de-test-complet` | Spécifier | v1-B |
| B3 | `regler-campagne` | Spécifier | v1-A |
| C1 | `diagnostiquer-banc-en-echec` | Analyser | v1-A |
| C2 | `analyser-sensibilite` | Analyser | v1-A (dégradée), complète en v1-B |
| C3 | `verifier-polarisation` | Analyser | v1-A |
| C4 | `analyser-stabilite` | Analyser | v1-B |
| C5 | `analyser-bruit` | Analyser | v1-A |
| C6 | `analyser-distorsion-spectre` | Analyser | v1-B |
| C7 | `expliquer-un-circuit` | Analyser | v1-A |
| C8 | `revue-de-schema` | Analyser | v1-A (partielle) |
| D1 | `dimensionner-filtre` | Concevoir | v1-B |
| D2 | `dimensionner-etage-transistor` | Concevoir | v1-B |
| D3 | `dimensionner-montage-ampli-op` | Concevoir | v1-B |
| D4 | `dimensionner-oscillateur` | Concevoir | v1-B |
| D5 | `dimensionner-alimentation-lineaire` | Concevoir | v1-B |
| D6 | `concevoir-logique-mixte` | Concevoir | v1-B |
| D7 | `optimiser-un-design` | Concevoir | v1-B |
| D8 | `dimensionner-convertisseur-decoupage` | Concevoir | après v1 |
| D9 | `commutation-de-puissance` | Concevoir | après v1 |
| E1 | `choisir-et-importer-modele` | Composants | v1-B |
| E2 | `extraire-modele-discret` | Composants | v1-C (M0, M2, M3) |
| E3 | `modeliser-logique-datasheet` | Composants | v1-C (M1, M4) |
| E4 | `confronter-modele-datasheet` | Composants | v1-C (J5) |
| F1 | `lire-datasheet` | Documents | v1-C (J1, J2) |
| F2 | `calculer-depuis-document` | Documents | v1-C (J3) |
| F3 | `verifier-conformite-note-application` | Documents | v1-C (J4) |
| F4 | `arbitrer-sources` | Documents | v1-C (J5) |
| F5 | `suivre-procedure-note-application` | Documents | après v1 |
| G1 | `rapport-de-test` | Restituer | v1-A (partielle) |
| G2 | `importer-depuis-autre-outil` | Migrer | après v1 |

Ce qui a été volontairement **fusionné** pour éviter des Skills qui se déclenchent en même temps : le choix des
stimuli et des mesures est un fichier de référence de B1 (et non une Skill) ; la convergence et les erreurs ngspice
sont dans S2 ; le mode conseil et l'exploration automatique (§3.7) sont dans D7 ; le centrage pour le rendement
(Cpk) est aussi dans D7.

---

## 3. Socle

### S1. `piloter-silx`

- **Déclenchement** : première demande qui touche un projet Silx (« ouvre mon projet », « que contient ce banc ? »,
  « lance les tests »), ou quand Claude ne sait pas quel outil appeler.
- **Ce qu'elle fait** : la démarche complète au-delà du court texte `Instructions` : modèle de données (circuit sous
  test, bancs en calques, critères et gabarits, campagnes), ordre des appels, lecture des verdicts et des marges,
  désignation des bancs et des signaux, limites (`set_component_value` en mémoire, `save_project` avec `ecraser`),
  quand resimuler, comment présenter un résultat (verdict, valeur, bornes, marge).
- **Outils** : tous les outils de lecture (`list_projects`, `open_project`, `open_demo`, `describe_project`,
  `list_testbenches`, `list_measurements`, `list_signals`, `get_netlist`, `run_simulation`, `get_test_summary`,
  `get_raw_curve`), `save_project`.
- **Manquants** : aucun.
- **Priorité** : v1-A.

### S2. `spice-bonnes-pratiques`

- **Déclenchement** : erreur de simulation (« timestep too small », « singular matrix », « gmin stepping failed »),
  résultat aberrant, simulation lente, question sur une valeur ou une unité, relecture de netlist.
- **Ce qu'elle fait** : prévention et réparation. Unités (le piège M/meg, `u` pour µ), nœuds flottants et chemins DC
  vers la masse, sources de tension idéales en parallèle ou boucles d'inductances, conditions initiales et démarrage
  des oscillateurs, pas maximal et durée en transitoire, densité de points en AC (résonances étroites), options
  (`reltol`, `gmin`, `itl4`, `method=gear`) avec leur effet et leur coût, modèles absents ou mal nommés, ordre des
  broches d'un sous-circuit. Chaque correction est vérifiée par une nouvelle simulation.
- **Outils** : `get_netlist`, `run_simulation` (avec `journal = true`), `describe_project`, `set_component_value`.
- **Manquants** : réglage des options ngspice et du pas par banc (`set_analysis`, §10.2) ; journal ngspice détaillé
  structuré (avertissements, statistiques de convergence) plutôt que texte brut.
- **Priorité** : v1-A.

---

## 4. Spécifier et vérifier

### B1. `banc-depuis-cahier-des-charges`

- **Déclenchement** : une exigence en langage naturel (« ce filtre doit être stable avec 45° de marge à 4 MHz »,
  « temps d'établissement < 2 µs à 0,1 % », « 2 Vcc sur 600 Ω, THD < 0,1 % »). C'est la fenêtre de prompt de la
  spec fonctionnelle §3.8.
- **Ce qu'elle fait** : traduit l'intention en banc structuré, montré à l'utilisateur avant lancement : type
  d'analyse et plage, stimuli et charges, signaux sondés, critères (mesure du catalogue, bornes) et gabarits
  dessinés. Références : choix du stimulus selon le circuit (échelon pour l'établissement, sinus pour la THD, petit
  signal pour Bode, PRBS7 pour une liaison numérique, balayage DC pour un seuil) ; correspondance exigence → mesure ;
  pièges (durée trop courte pour le régime établi, plage AC qui ne contient pas la coupure). Simule une première fois
  et commente la marge.
- **Outils** : `describe_project`, `list_measurements`, `list_signals`, `run_simulation`, `get_raw_curve`.
- **Manquants** : `create_testbench` (depuis les modèles de banc de l'application : Échelon, Sinus, Bode, Transfert
  DC), `set_stimulus`, `set_analysis`, `add_probe`, `add_criterion`, `generate_mask` (§10.2).
- **Priorité** : v1-B. C'est la Skill qui porte le critère de succès « intention → résultat en moins de 2 min ».

### B2. `plan-de-test-complet`

- **Déclenchement** : « prépare les tests de ce circuit », « qu'est-ce que je devrais vérifier ? », un circuit sans
  banc ou presque.
- **Ce qu'elle fait** : identifie la fonction du circuit (voir C7) et propose le plan de test d'un bureau d'étude :
  point de fonctionnement, transfert DC, réponse fréquentielle, réponse indicielle, bruit, distorsion, température,
  dispersion. Pour chaque banc proposé, la raison et le critère ; l'utilisateur coche, puis B1 construit chaque banc.
- **Outils** : `describe_project`, `list_testbenches`, `get_operating_point`.
- **Manquants** : ceux de B1.
- **Priorité** : v1-B.

### B3. `regler-campagne`

- **Déclenchement** : « est-ce que ça tient avec des composants réels ? », « Monte-Carlo », « pire cas »,
  « en température », « quel rendement ? », ou un banc qui passe en nominal avec peu de marge.
- **Ce qu'elle fait** : choisit le type de campagne (balayage, Monte-Carlo, coins), les tolérances réalistes par
  type de composant, le nombre de tirages selon le taux visé, la loi, la graine ; lance, puis interprète : taux de
  conformité et intervalle de confiance, Cpk, pire cas et composants en cause, différence entre pire cas statistique
  et coins. Dit quand une campagne ne prouve rien (tolérances nulles, modèle sans paramètres thermiques).
- **Outils** : `list_testbenches`, `describe_project`, `set_component_value` (tolérances), `run_campaign`,
  `get_worst_case`, `get_test_summary`.
- **Manquants** : `set_campaign` (enregistrer la campagne dans le banc ; `run_campaign` n'en garde rien) ; plus tard,
  campagnes combinées et dispersion des paramètres de modèle (spec fonctionnelle §3.4, « plus tard »).
- **Priorité** : v1-A.

---

## 5. Analyser et diagnostiquer

### C1. `diagnostiquer-banc-en-echec`

- **Déclenchement** : un banc ou une campagne en FAIL, « pourquoi ça ne passe pas ? », « que faut-il changer ? ».
- **Ce qu'elle fait** : démarche de diagnostic : vérifier d'abord que le banc mesure bien ce qu'il faut (signal,
  plage, durée), puis le point de fonctionnement, puis la cause (courbe, pire cas, composants influents), formule une
  hypothèse physique, propose une correction chiffrée et la **vérifie par simulation** avant de la présenter.
  Distingue un défaut du circuit d'un défaut du banc ou du modèle.
- **Outils** : `get_test_summary`, `list_testbenches`, `get_raw_curve`, `get_worst_case`, `get_operating_point`,
  `run_campaign` (balayages), `set_component_value`, `run_simulation`.
- **Manquants** : `get_sensitivity`, `apply_and_test` (copie, resimulation, écart avant/après), `measure_signal`
  (appliquer une mesure du catalogue à un signal sans créer de critère).
- **Priorité** : v1-A (corrections essayées en mémoire avec `set_component_value`, puis annulées si elles ne sont pas
  retenues) ; plus sûre avec `apply_and_test`.

### C2. `analyser-sensibilite`

- **Déclenchement** : « quel composant influence le plus… », « sur quoi jouer ? », préalable à D7.
- **Ce qu'elle fait** : classe les composants par influence sur une mesure (sensibilité relative, effet de la
  tolérance), sépare les composants critiques de ceux qu'on peut élargir (gain de coût). Sans outil dédié : petits
  balayages ± tolérance par composant ; avec l'outil : un appel.
- **Outils** : `run_campaign` (balayage), `describe_project`.
- **Manquants** : `get_sensitivity` (spec fonctionnelle §3.6 et §3.9).
- **Priorité** : v1-A dégradée, complète en v1-B.

### C3. `verifier-polarisation`

- **Déclenchement** : « le transistor est-il bien polarisé ? », « point de repos », gain ou excursion anormaux,
  avant toute analyse petit signal d'un étage actif.
- **Ce qu'elle fait** : lit le point de fonctionnement, déduit le régime de chaque transistor (bloqué, actif,
  saturé ; triode ou saturation pour un MOSFET) à partir des tensions données par l'outil, vérifie les marges
  d'excursion, la dissipation et la stabilité de la polarisation (balayage de β ou de température).
- **Outils** : `get_operating_point`, `describe_project`, `run_campaign` (TEMP, balayage).
- **Manquants** : `get_component_ratings` (VCEO, VDS max, P max du modèle ou de la datasheet) ; paramètres petit
  signal au point de repos (gm, rπ, ro, Cgs… via `.op` de ngspice).
- **Priorité** : v1-A.

### C4. `analyser-stabilite`

- **Déclenchement** : « est-ce stable ? », « marge de phase », oscillation parasite, dépassement excessif, boucle de
  régulation, ampli-op chargé par une capacité.
- **Ce qu'elle fait** : choisit la méthode (gain de boucle par injection, méthode de Middlebrook ou de Tian ;
  à défaut, indicateurs en boucle fermée : dépassement, pic de résonance), place l'injection au bon endroit, lit
  marges de gain et de phase, relie le résultat au dépassement observé, propose une compensation (puis D7).
- **Outils** : `list_measurements` (marge de phase, marge de gain), `run_simulation`, `get_raw_curve`.
- **Manquants** : `create_testbench` avec un modèle « gain de boucle » (insertion de la sonde d'injection sans
  modifier le circuit sous test) ; outils de B1.
- **Priorité** : v1-B.

### C5. `analyser-bruit`

- **Déclenchement** : « bruit », « rapport signal sur bruit », « densité en nV/√Hz », choix d'un ampli faible bruit.
- **Ce qu'elle fait** : choisit le nœud et la bande, lit densités et bruit total, compare aux références physiques
  (√(4kTR), √(kT/C)) fournies par l'outil de calcul, convertit en SNR ou en bruit ramené à l'entrée, identifie le
  contributeur dominant et propose une action (impédances plus faibles, autre composant, limitation de bande).
- **Outils** : `get_noise`, `list_testbenches`, `run_simulation`.
- **Manquants** : `get_noise_contributions` (contribution de chaque composant, fournie par ngspice) ;
  `evaluate_formula` pour les références et le SNR.
- **Priorité** : v1-A (sans le classement des contributeurs).

### C6. `analyser-distorsion-spectre`

- **Déclenchement** : « THD », « harmoniques », « écrêtage », « spectre », intermodulation.
- **Ce qu'elle fait** : règle un banc sinus adapté (amplitude, périodes entières, régime établi), lit la THD et les
  raies, relie la distorsion à sa cause (excursion, point de repos, non-linéarité d'un composant), balaye
  l'amplitude pour trouver le point de compression.
- **Outils** : `list_measurements` (THD, efficace), `run_simulation`, `run_campaign`.
- **Manquants** : `get_spectrum` (raies de la vue FFT : fréquence, amplitude en dBV) ; outils de B1.
- **Priorité** : v1-B.

### C7. `expliquer-un-circuit`

- **Déclenchement** : « explique-moi ce montage », « à quoi sert R4 ? », reprise d'un schéma inconnu.
- **Ce qu'elle fait** : reconnaît la topologie à partir des connexions réelles (étages, contre-réactions, filtres,
  polarisation), explique le rôle de chaque composant et le fonctionnement, puis **appuie l'explication sur des
  simulations** (point de fonctionnement, Bode, transitoire) plutôt que sur des ordres de grandeur supposés.
- **Outils** : `describe_project`, `get_netlist`, `get_operating_point`, `run_simulation`, `get_raw_curve`.
- **Manquants** : aucun pour l'essentiel.
- **Priorité** : v1-A.

### C8. `revue-de-schema`

- **Déclenchement** : « relis mon schéma », « revue de conception », avant de figer un design ou d'écrire le rapport.
- **Ce qu'elle fait** : liste de contrôle d'un bureau d'étude : ERC, valeurs hors séries normalisées, tensions et
  puissances appliquées par rapport aux limites des composants (au point de fonctionnement et en pire cas),
  découplage, entrées flottantes en logique, chemins DC, modèles indicatifs encore utilisés (palette) au lieu de
  ceux du fabricant, bancs manquants pour les exigences. Chaque constat : gravité, composant, preuve.
- **Outils** : `describe_project`, `get_operating_point`, `list_testbenches`, `run_campaign`.
- **Manquants** : `get_component_ratings`, `nearest_standard_value`, `check_design_rules` (règles issues des
  documents, F3).
- **Priorité** : v1-A partielle (ERC, point de fonctionnement, bancs) ; complète avec v1-B et v1-C.

---

## 6. Concevoir et dimensionner

Les Skills de cette famille contiennent la démarche et les formules de référence (dans `references/`), mais
**l'évaluation des formules passe par `evaluate_formula` et `solve_formula`**, et l'arrondi par
`nearest_standard_value`. Chacune finit par une simulation qui confirme le dimensionnement contre l'exigence.

### D1. `dimensionner-filtre`

- **Déclenchement** : « filtre passe-bas à 10 kHz », « Butterworth d'ordre 4 », « anti-repliement », réjection à une
  fréquence.
- **Ce qu'elle fait** : du gabarit à l'ordre et à la famille (Butterworth, Bessel, Chebyshev, elliptique), choix de
  la structure (RC passif, LC, Sallen-Key, MFB, à quartz), calcul des valeurs, arrondi en série E, effet des
  tolérances (campagne), vérification par Bode et gabarit.
- **Outils** : `run_simulation`, `get_raw_curve`, `run_campaign`, `set_component_value`.
- **Manquants** : outils de calcul (§10.3), écriture du circuit (`edit_circuit`), outils de B1.
- **Priorité** : v1-B.

### D2. `dimensionner-etage-transistor`

- **Déclenchement** : « ampli à transistor », « émetteur commun avec 40 dB », « polarise ce BJT », « source commune ».
- **Ce qu'elle fait** : choix de la topologie (EC, BC, CC, source/grille/drain commun), point de repos, polarisation
  stable vis-à-vis de β et de la température, gain, impédances, bande passante, excursion ; vérification par C3 puis
  par bancs Bode et transitoire.
- **Outils** : `get_operating_point`, `run_simulation`, `run_campaign`, `set_component_value`.
- **Manquants** : outils de calcul, `edit_circuit`, outils de B1.
- **Priorité** : v1-B.

### D3. `dimensionner-montage-ampli-op`

- **Déclenchement** : « ampli inverseur gain 10 », « intégrateur », « adaptation de niveau », « comparateur à
  hystérésis ».
- **Ce qu'elle fait** : montages de base et leurs limites réelles (GBW, slew rate, excursion de sortie, offset,
  impédance de charge, stabilité avec charge capacitive), choix du modèle d'ampli-op, vérification.
- **Outils** : comme D2.
- **Manquants** : outils de calcul, `edit_circuit`, outils de B1, `choisir-et-importer-modele` (E1) pour un ampli-op
  réel.
- **Priorité** : v1-B.

### D4. `dimensionner-oscillateur`

- **Déclenchement** : « oscillateur à 2 MHz », « Colpitts », « Pierce », « quartz », « ça n'oscille pas ».
- **Ce qu'elle fait** : condition de Barkhausen et marge de démarrage, choix de la topologie, capacité de charge
  d'un quartz, mise en route de la simulation (impulsion de démarrage, durée, pas), mesure de fréquence et
  d'amplitude en régime établi, sensibilité à la température et aux tolérances.
- **Outils** : `run_simulation`, `get_raw_curve`, `list_measurements` (fréquence, crête à crête), `run_campaign`.
- **Manquants** : outils de calcul, `edit_circuit`, outils de B1.
- **Priorité** : v1-B (la démo Colpitts sert de scénario d'essai).

### D5. `dimensionner-alimentation-lineaire`

- **Déclenchement** : « régulateur 3,3 V », « LM317 », « Zener », « référence de tension », « ondulation ».
- **Ce qu'elle fait** : régulation ballast ou à régulateur intégré, pont de contre-réaction, dropout, dissipation,
  ondulation et réjection, démarrage, stabilité avec le condensateur de sortie ; pire cas sur Vref et tolérances.
  Rejoint F2 quand une datasheet est disponible.
- **Outils** : `get_operating_point`, `run_simulation`, `run_campaign`.
- **Manquants** : outils de calcul (dont `worst_case`), `edit_circuit`, outils de B1.
- **Priorité** : v1-B.

### D6. `concevoir-logique-mixte`

- **Déclenchement** : portes, bascules, compteur, « signal numérique », comparateur qui pilote de la logique,
  « niveaux logiques », « temps de propagation ».
- **Ce qu'elle fait** : usage du mixte XSPICE de Silx : ponts analogique ↔ numérique et leurs seuils, tension
  logique, délais, horloge, lecture des signaux numériques ; pièges (rebonds sans hystérésis, comme dans la démo
  XSPICE, seuils mal placés, entrées non connectées) ; bancs de contrôle (délais, fréquence maximale).
- **Outils** : `describe_project`, `run_simulation`, `get_raw_curve`, `list_signals`.
- **Manquants** : `edit_circuit`, outils de B1 ; lecture des signaux numériques (états, instants de transition) dans
  `get_raw_curve` si elle n'y est pas déjà.
- **Priorité** : v1-B.

### D7. `optimiser-un-design`

- **Déclenchement** : « améliore la marge », « propose une modification », « trouve la meilleure valeur de C3 »,
  « augmente le rendement », après C1 ou C2.
- **Ce qu'elle fait** : les deux modes du §3.7 de la spec fonctionnelle. **Conseil** : propositions argumentées
  (composant, valeur normalisée, justification par la sensibilité mesurée), chacune re-simulée sur tous les bancs
  avant d'être présentée comme confirmée. **Exploration** : recherche bornée sur les paramètres sensibles
  (optimisation classique pilotée), chemin suivi restitué. Inclut le centrage pour le rendement (viser le Cpk, pas
  seulement le nominal) et la vérification qu'aucun autre banc ne régresse.
- **Outils** : `get_test_summary`, `run_campaign`, `set_component_value`, `run_simulation`, `save_project`.
- **Manquants** : `propose_modification`, `apply_and_test`, `optimize_parameter`, `get_sensitivity`,
  `nearest_standard_value`.
- **Priorité** : v1-B.

### D8. `dimensionner-convertisseur-decoupage`

- **Déclenchement** : buck, boost, flyback, « alimentation à découpage », ondulation de courant.
- **Ce qu'elle fait** : choix de la topologie et du mode de conduction, inductance, condensateurs, compensation de
  boucle (avec C4), modèle moyen pour la boucle et modèle commuté pour les contraintes, durée et pas de simulation
  adaptés aux longs transitoires.
- **Outils** : `run_simulation`, `get_raw_curve`, `run_campaign`.
- **Manquants** : outils de calcul, `edit_circuit`, outils de B1 ; modèles moyens de cellules de commutation ; F2 et
  F3 pour suivre l'AN du contrôleur.
- **Priorité** : après v1 (simulations longues, besoin du socle documentaire).

### D9. `commutation-de-puissance`

- **Déclenchement** : MOSFET ou BJT en commutation, driver de grille, pertes, snubber, diode de roue libre,
  temps morts.
- **Ce qu'elle fait** : courant de grille et charge Qg, pertes par conduction et par commutation, surtensions et
  snubber, aire de sécurité ; mesures sur les fronts.
- **Outils** : `run_simulation`, `get_raw_curve`, `list_measurements`.
- **Manquants** : `get_component_ratings`, mesure d'énergie et de puissance moyenne sur un intervalle
  (`measure_signal` avec produit v·i), modèles MOSFET de puissance (E1 ou E2).
- **Priorité** : après v1.

---

## 7. Composants et modèles

### E1. `choisir-et-importer-modele`

- **Déclenchement** : « utilise le vrai modèle du 2N3904 », « importe ce .lib », un composant de la palette encore au
  modèle indicatif, un sous-circuit dont les broches ne tombent pas juste.
- **Ce qu'elle fait** : cherche d'abord un modèle officiel (bibliothèques du projet et de l'utilisateur), l'importe,
  associe symbole et broches (contrôle de l'ordre des broches du `.subckt`), vérifie par une simulation de contrôle
  simple (point de fonctionnement, caractéristique) que le modèle se comporte comme attendu ; signale les modèles
  chiffrés ou incompatibles avec ngspice (syntaxes PSpice ou LTspice).
- **Outils** : `describe_project`, `get_operating_point`, `run_simulation`.
- **Manquants** : `search_models` (bibliothèques indexées), `import_spice_model`, `set_component_model`,
  `list_model_pins`.
- **Priorité** : v1-B.

### E2. `extraire-modele-discret`

- **Déclenchement** : « crée un modèle SPICE de la 1N4148 à partir de cette datasheet », diode, Zener, Schottky,
  BJT, MOSFET (cas CG1 à CG5 et CG9 de `generation_modeles.md`).
- **Ce qu'elle fait** : déroule la chaîne de `generation_modeles.md` §3 : identification et variante, une seule
  question d'ambiguïté, récapitulatif des paramètres sourcés et non calibrés, extraction, ajustement, rapport de
  validation, approbation. Explique les paramètres « par défaut » et la plage de validité.
- **Outils** : `model_plan`, `extract_diode_params`, `extract_bjt_params`, `extract_mosfet_params`, `fit_model`,
  `generate_model`, `validate_model_vs_datasheet`, `save_model`, outils `doc_*`.
- **Manquants** : tous (spécifiés dans `generation_modeles.md` §10.1, pas encore développés).
- **Priorité** : v1-C, jalons M0 (diode), M2 (BJT), M3 (MOSFET).

### E3. `modeliser-logique-datasheet`

- **Déclenchement** : « modèle du 74HC74 à 4,5 V », porte, bascule, verrou, table de fonction (CG6 à CG8).
- **Ce qu'elle fait** : tension d'alimentation prise dans le schéma, interpolation ou refus hors plage, lecture de
  la table de fonction validée visuellement, modèle XSPICE et ponts, moniteur de setup et hold.
- **Outils** : `extract_logic_params`, `generate_model`, `generate_timing_monitor`, `validate_model_vs_datasheet`,
  `save_model`, outils `doc_*`.
- **Manquants** : tous.
- **Priorité** : v1-C, jalons M1 et M4.

### E4. `confronter-modele-datasheet`

- **Déclenchement** : « ce modèle est-il cohérent avec la datasheet ? », écart inexpliqué entre simulation et
  mesure sur maquette, avant de faire confiance à un modèle de fabricant (CU5).
- **Ce qu'elle fait** : simule les caractéristiques mesurables dans les conditions de la datasheet, compare aux
  min/typ/max cités, conclut (modèle fiable, fiable dans telle plage, à remplacer).
- **Outils** : `validate_model_vs_datasheet`, outils `doc_*`.
- **Manquants** : tous.
- **Priorité** : v1-C, jalon J5.

---

## 8. Documents

### F1. `lire-datasheet`

- **Déclenchement** : question sur un composant documenté (« dropout de U3 à 500 mA et 85 °C ? »), recherche dans le
  corpus (CU1, CU6).
- **Ce qu'elle fait** : lecture d'électronicien : min/typ/max (le garanti prime sur le typique), conditions de test,
  variante de l'instance, notes de bas de tableau, valeurs absolues maximales opposées aux conditions recommandées,
  figures citées comme figures ; réponse sourcée et vérifiée, ou marquée non sourcée.
- **Outils** : `doc_search`, `doc_get_passage`, `doc_get_table`, `doc_get_figure`, `doc_get_parameter`,
  `verify_citation`, `doc_list`, `doc_link`.
- **Manquants** : tous (spécifiés dans `assistant_documents.md` §10.2).
- **Priorité** : v1-C, jalons J1 et J2. Mode dégradé possible avant : dans Claude Desktop, avec la datasheet jointe à
  la conversation, citations données mais **non vérifiées**, et dites comme telles.

### F2. `calculer-depuis-document`

- **Déclenchement** : « calcule R1 et R2 pour 3,3 V avec le LM317, en E96 » (CU2).
- **Ce qu'elle fait** : formule trouvée et transcrite (validée à côté de l'image), entrées sourcées, pire cas par
  défaut, arrondi normalisé, application dans une copie et simulation de confirmation, écart calcul ↔ simulation
  commenté.
- **Outils** : `doc_*`, `verify_citation`, `evaluate_formula`, `solve_formula`, `worst_case`,
  `nearest_standard_value`, `apply_and_test`.
- **Manquants** : tous.
- **Priorité** : v1-C, jalon J3.

### F3. `verifier-conformite-note-application`

- **Déclenchement** : « vérifie mon étage buck contre l'AN de TI » (CU3).
- **Ce qu'elle fait** : recommandations transformées en règles (structurelles, comportementales, non vérifiables),
  évaluées, puis tableau respectée / violée / non vérifiable avec citation et composant concerné ; les règles non
  vérifiables vont dans la liste de contrôle PCB du rapport.
- **Outils** : `list_design_rules`, `check_design_rules`, `doc_*`, `run_simulation`.
- **Manquants** : tous sauf `run_simulation`.
- **Priorité** : v1-C, jalon J4.

### F4. `arbitrer-sources`

- **Déclenchement** : deux documents en désaccord, nouvelle révision d'une datasheet, écart document ↔ modèle ou
  document ↔ gabarit de l'utilisateur (`assistant_documents.md` §7).
- **Ce qu'elle fait** : présente le conflit avec les deux citations, propose l'ordre de priorité du §7.2 sans
  l'appliquer, fait trancher l'utilisateur, mesure l'impact sur les calculs et règles du projet.
- **Outils** : `list_conflicts`, `doc_*`.
- **Manquants** : tous.
- **Priorité** : v1-C, jalon J5.

### F5. `suivre-procedure-note-application`

- **Déclenchement** : « suis la procédure de la section 8 pour la compensation » (CU4).
- **Ce qu'elle fait** : procédure en étapes, chaque calcul par outil, chaque choix soumis à l'utilisateur, résultat
  vérifié par gabarit.
- **Outils** : ceux de F2 et B1.
- **Manquants** : ceux de F2 et B1.
- **Priorité** : après v1 (comme prévu dans `assistant_documents.md` §12).

---

## 9. Restituer et migrer

### G1. `rapport-de-test`

- **Déclenchement** : « fais le rapport », « synthèse pour la revue », fin d'une campagne.
- **Ce qu'elle fait** : rapport de bureau d'étude vérifiable : objet, exigences, bancs et conditions, résultats
  nominaux et de campagne (taux, Cpk, pire cas), écarts et décisions, modèles et documents utilisés (révision,
  empreinte), réserves (modèles indicatifs, paramètres non calibrés, règles non vérifiables). Aucun nombre absent des
  résultats des outils.
- **Outils** : `get_test_summary`, `list_testbenches`, `get_worst_case`, `describe_project`.
- **Manquants** : `export_report` (produire le rapport Markdown et PDF de l'application, avec figures, plutôt qu'un
  texte reconstruit par l'IA).
- **Priorité** : v1-A partielle (synthèse en texte), complète avec `export_report`.

### G2. `importer-depuis-autre-outil`

- **Déclenchement** : « j'ai un schéma LTspice », une netlist SPICE existante à reprendre.
- **Ce qu'elle fait** : import de la netlist, différences de syntaxe (LTspice, PSpice ↔ ngspice), reconstruction
  du schéma, contrôle d'équivalence par simulation des deux versions.
- **Outils** : `get_netlist`, `run_simulation`.
- **Manquants** : `import_netlist` (et l'import lui-même dans le cœur, prévu au §3.1 de la spec fonctionnelle).
- **Priorité** : après v1.

---

## 10. Outils MCP manquants, par ordre d'utilité

Ce tableau est la conséquence directe de la liste : il dit quels outils débloquent le plus de Skills.

### 10.1 Calcul (débloque D1 à D5, D7, C5, C8, F2)

`evaluate_formula`, `solve_formula`, `worst_case`, `nearest_standard_value`, déjà spécifiés dans
`assistant_documents.md` §6.1 au jalon J3. **Décision (26/11/2026) : ils sortent du chantier documentaire et sont
livrés tôt**, avant le lot 2, car aucune Skill de dimensionnement ne peut respecter la règle « l'IA ne calcule pas »
sans eux, avec ou sans datasheet. Au jalon J3 ne restent que la transcription de formules depuis un document et la
boucle de confirmation par simulation propre aux documents.

### 10.2 Écriture du projet (débloque B1, B2, C4, C6, D1 à D6, E1)

| Outil | Rôle |
|---|---|
| `edit_circuit` | Ajouter, retirer, relier des composants du circuit sous test (placement automatique, à affiner dans l'éditeur) |
| `create_testbench` | Créer un banc depuis un modèle (Échelon, Sinus, Bode, Transfert DC, et un nouveau modèle « gain de boucle ») |
| `set_stimulus`, `set_analysis` | Régler une source et l'analyse d'un banc (plage, pas, options ngspice) |
| `add_probe`, `add_criterion`, `generate_mask` | Sondes, critères chiffrés, gabarits dessinés (spec fonctionnelle §3.9) |
| `set_campaign` | Enregistrer la campagne dans le banc |

Toutes en mémoire, comme `set_component_value` ; `save_project` reste la seule écriture sur disque.

### 10.3 Conception bouclée (débloque C1, C2, D7)

`get_sensitivity`, `apply_and_test`, `propose_modification`, `optimize_parameter` (spec fonctionnelle §3.9).

### 10.4 Lecture enrichie

| Outil | Skills |
|---|---|
| `measure_signal` (mesure du catalogue sur un signal, sans critère) | C1, D9 |
| `get_spectrum` (raies FFT) | C6 |
| `get_noise_contributions` | C5 |
| `get_component_ratings` | C3, C8, D9 |
| paramètres petit signal au point de repos | C3, D2 |
| `export_report` | G1 |

### 10.5 Bibliothèques de modèles (débloque E1)

`search_models`, `import_spice_model`, `set_component_model`, `list_model_pins`.

### 10.6 Documents et génération de modèles (débloque E2 à E4, F1 à F5)

Déjà spécifiés : `assistant_documents.md` §10.2 et `generation_modeles.md` §10.1.

### 10.7 Nombre d'outils

Avec ces ajouts, le serveur dépasse 50 outils. Les Skills aident ici : chacune nomme les quelques outils dont elle a
besoin, ce qui guide le choix du modèle. Si nécessaire, le serveur exposera les outils par groupes (lecture,
écriture, calcul, documents, modèles), comme le suggère `assistant_documents.md` §10.5.

---

## 11. Ordre de réalisation proposé

1. **Lot 1, tout de suite** (outils de `main` et de la PR 12) : S1 `piloter-silx`, S2 `spice-bonnes-pratiques`,
   C1 `diagnostiquer-banc-en-echec`, B3 `regler-campagne`, C3 `verifier-polarisation`, C5 `analyser-bruit`,
   C7 `expliquer-un-circuit`, C8 `revue-de-schema` (partielle), G1 `rapport-de-test` (partielle),
   C2 `analyser-sensibilite` (dégradée). Plus le plugin Claude Code qui réunit serveur MCP et Skills.
2. **Outils de calcul** (§10.1, avancés) **et d'écriture** (§10.2), puis **lot 2** : B1, B2, C4, C6, D1 à D6, E1.
3. **Conception bouclée** (§10.3), puis D7, et C1 et C2 en version complète.
4. **Lot 3**, au rythme des jalons J et M : F1 à F4, E2 à E4.
5. **Après v1** : D8, D9, F5, G2.

## 12. Décisions et questions ouvertes

Décisions validées (26/11/2026) :

1. Les règles permanentes (§1.1) ne sont pas des Skills : elles vont dans les consignes de l'agent (texte
   `Instructions` du serveur MCP, consignes système de l'agent intégré).
2. Les outils de calcul (§10.1) sont livrés avant le socle documentaire, sans attendre le jalon J3.

Questions ouvertes :

1. Le plugin Claude Code (serveur MCP + Skills) est-il le premier mode de diffusion, avant l'agent intégré ?
2. Des domaines manquent-ils pour votre usage (RF, audio, capteurs et conditionnement, CEM, mesures sur batteries) ?
