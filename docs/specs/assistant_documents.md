# Chapitre de spécification : assistant IA appuyé sur des datasheets et notes d'application

Statut : validé le 24/11/2026.
Complète la spécification fonctionnelle (§3.7 Assistant de conception, §3.8
Interface, §3.9 Intégration IA) et la spécification technique (§4 Intégration
IA / MCP).

Les hypothèses sont repérées **[Hn]** dans le texte et récapitulées au §11.

---

## 1. Objectif

Permettre à l'ingénieur de donner à l'assistant IA (le prompt local de Silx)
un ou plusieurs documents de référence (datasheet, note d'application, guide
de conception, errata) pour :

1. **travailler précisément autour du schéma** : questions sur un composant
   placé, sur un brochage, sur une condition de fonctionnement ;
2. **calculer des valeurs** à partir des formules et paramètres du document
   (pont de contre-réaction, inductance d'un convertisseur, compensation) ;
3. **se conformer aux pratiques** décrites dans le document (découplage,
   plages de valeurs recommandées, contraintes de stabilité) et vérifier que
   le schéma les respecte.

Le principe fondateur de Silx reste intact : **l'IA ne calcule jamais
elle-même**. Elle lit, cite, propose et explique. Toute valeur numérique
présentée à l'utilisateur sort d'un outil déterministe (évaluateur de
formules, bibliothèque de mesures, ngspice), et toute affirmation tirée d'un
document est accompagnée d'une citation vérifiable (document, page, zone).

## 2. Cas d'usage

Les références de pages, tableaux et numéros de section des exemples sont
illustratives.

### CU1. Question ciblée sur un composant du schéma

> « Quelle est la tension de dropout de U3 à 500 mA et 85 °C ? »

L'assistant identifie la référence de U3 dans le schéma, retrouve la
datasheet associée, renvoie la valeur **avec ses conditions de test** et la
citation (`LM1117, p. 4, tableau Electrical Characteristics, ligne
Dropout Voltage`). Si la valeur n'existe qu'à 25 °C, il le dit, et propose
éventuellement de lire la courbe typique (voir §3.3) en l'annonçant comme
lecture de figure.

### CU2. Calcul de valeurs à partir d'une formule du document

> « Calcule R1 et R2 pour sortir 3,3 V avec le LM317, en E96. »

1. L'assistant retrouve la formule `Vout = Vref·(1 + R2/R1) + Iadj·R2` et
   les paramètres `Vref` et `Iadj` (min/typ/max) dans la datasheet, avec
   citations.
2. Il propose la formule sous forme structurée (variables, unités) ; elle est
   affichée à côté de l'extrait de page d'origine pour contrôle.
3. L'outil `solve_formula` résout R2 pour un R1 choisi, l'outil
   `nearest_standard_value` arrondit en série E96, puis `evaluate_formula`
   recalcule Vout réel, y compris dans les pires cas (Vref min/max, Iadj
   max, tolérances).
4. Sur demande ou par défaut, les valeurs sont appliquées dans une copie du
   schéma et re-simulées (`apply_and_test`) : la tension mesurée par
   ngspice confirme ou infirme le calcul.

### CU3. Conformité aux pratiques d'une note d'application

> « Vérifie mon étage buck contre l'AN de TI. »

L'assistant extrait les recommandations de la note (ex. : « condensateur
d'entrée céramique ≥ 10 µF au plus près de VIN », « ESR du condensateur de
sortie < 50 mΩ », « inductance entre 4,7 et 10 µH pour Fsw = 500 kHz »), les
transforme en **règles de conception** (§5), puis indique pour chacune :
respectée, non respectée, non vérifiable sur schéma (placement PCB), avec la
citation de la règle et l'élément du schéma concerné.

### CU4. Dimensionnement guidé par une procédure de l'AN

> « Suis la procédure de la section 8 pour dimensionner la compensation. »

Les notes d'application décrivent souvent une procédure en étapes (calcul de
fréquence de coupure, placement du zéro, choix de Rc et Cc). L'assistant
déroule la procédure étape par étape ; chaque étape de calcul est un appel
d'outil, chaque étape de choix est soumise à l'utilisateur. Le résultat final
est vérifié par simulation avec un gabarit (ex. : marge de phase ≥ 45°).

### CU5. Confrontation modèle SPICE ↔ datasheet

> « Le modèle SPICE de cet ampli-op est-il cohérent avec la datasheet ? »

L'outil `compare_model_to_datasheet` simule les caractéristiques
mesurables (GBW, slew rate, offset, courant de repos) avec le modèle associé
au symbole et les compare aux valeurs min/typ/max du document. Les écarts
sont signalés ; c'est une source fréquente d'erreurs de conception que les
outils actuels ne détectent pas.

### CU6. Recherche dans un corpus de documents

> « Quelles datasheets du projet donnent une tenue en température au-delà
> de 105 °C ? »

Recherche plein texte et par paramètre dans l'ensemble des documents du
projet, réponses listées avec citations.

## 3. Ingestion des documents

### 3.1 Formats et rattachement

- Formats v1 : **PDF** (texte natif ou scanné). HTML et images simples en
  phase ultérieure. **[H1]**
- Un document est importé dans le **dossier documentaire du projet** (copie
  locale, empreinte SHA-256, date d'import). Métadonnées détectées puis
  corrigeables : fabricant, référence(s) couverte(s), type (datasheet, AN,
  errata, guide), révision, date de publication.
- Rattachement : un document peut être lié à un symbole, à un modèle SPICE
  ou à une instance du schéma (U3). La liaison est proposée automatiquement
  par correspondance de référence, confirmée par l'utilisateur.
- Une bibliothèque documentaire utilisateur (hors projet), **locale au poste**,
  permet de réutiliser les datasheets courantes d'un projet à l'autre. Une
  bibliothèque partagée en équipe est hors périmètre v1 (décision du
  24/11/2026).

### 3.2 Extraction du texte

- Extraction du texte avec positions (page, boîte englobante, police), pour
  pouvoir citer et surligner une zone précise.
- Reconstruction de l'ordre de lecture (multi-colonnes, en-têtes et pieds de
  page répétés retirés).
- PDF scannés : OCR local, avec indicateur de confiance par page. Une page à
  faible confiance est signalée et ses valeurs ne sont jamais utilisées sans
  validation humaine.
- Normalisations électroniques indispensables : `µ`/`u`, signe moins réel
  (U+2212) vs tiret, exposants et indices (`V_REF`, `10^-6`), préfixes SI,
  `Ω`/`ohm`, séparateur décimal.

### 3.3 Tableaux

Les tableaux de caractéristiques électriques sont la matière la plus
précieuse et la plus difficile.

- Détection et reconstruction des tableaux (lignes, colonnes, cellules
  fusionnées, notes de bas de tableau).
- Conversion en **paramètres typés** :
  `{ nom, symbole, min, typ, max, unité, conditions, notes, source }`, où
  `conditions` conserve les conditions de test (Ta, Vin, Iout…) et `source`
  pointe vers document, page et cellule.
- Gestion des variantes : une même datasheet couvre souvent plusieurs
  suffixes (‑3.3, ‑5.0, ‑ADJ), boîtiers ou grades de température. Chaque
  paramètre porte la variante à laquelle il s'applique ; l'assistant utilise
  la variante de l'instance du schéma et le signale.
- Chaque paramètre a un **statut** : `extrait` (automatique, non vérifié) ou
  `validé` (confirmé par l'utilisateur, avec date). Un calcul utilisant un
  paramètre `extrait` est marqué comme tel dans son résultat.

### 3.4 Figures

- v1 : les figures (courbes typiques, schémas d'application, brochages) sont
  extraites comme images, indexées par leur légende et leur numéro, et
  affichables en citation. **[H2]**
- Lecture de valeurs sur une courbe : **numérisation assistée**, reportée
  après la v1 (décision du 24/11/2026). Principe retenu pour plus tard :
  l'utilisateur cale les axes, éventuellement log, et clique des points ;
  l'outil interpole. Toute valeur ainsi obtenue sera étiquetée « lue sur
  figure », avec une incertitude affichée, et jamais présentée comme une
  valeur garantie.
- La lecture automatique de courbe par un modèle à vision est **hors
  périmètre v1** : les modèles locaux visés en sont peu capables et l'erreur
  ne serait pas maîtrisée. **[H3]**
- Schémas d'application typiques : en phase ultérieure, import du schéma
  d'application comme point de départ d'un schéma Silx (redessin assisté,
  pas automatique).

### 3.5 Équations

- Les équations sont souvent des images ou du texte mal structuré. Elles sont
  repérées (zone, numéro d'équation), puis **transcrites** en formule
  structurée par l'IA, mais la transcription est **toujours montrée à
  l'utilisateur à côté de l'image d'origine** et doit être validée avant
  usage dans un calcul.
- Une formule validée est stockée dans le projet (et réutilisable), avec sa
  source.

### 3.6 Indexation et recherche

- Découpage en passages (paragraphe, ligne de tableau, légende) avec leur
  position.
- Recherche lexicale locale (adaptée aux références et symboles très
  spécifiques des datasheets), complétée en option par une recherche
  sémantique avec un modèle d'embeddings local. **[H4]**
- L'IA n'a **jamais** le document entier dans son contexte par défaut :
  elle interroge l'index par outils et reçoit les passages pertinents avec
  leurs identifiants de source. Cela limite la taille de contexte requise
  (modèles locaux) et rend chaque information traçable.

## 4. Citations sourcées

### 4.1 Format

Toute affirmation issue d'un document est suivie d'une citation :
`[LM317 rev. Y, p. 5, Tableau 7.5, « Reference voltage »]`.
Dans l'interface, la citation est cliquable : elle ouvre le visualiseur PDF
à la page, zone surlignée.

### 4.2 Vérification automatique des citations

C'est le principal garde-fou contre les hallucinations.

- L'IA ne produit pas de citation libre : elle renvoie l'identifiant du
  passage obtenu par l'outil de recherche, plus l'extrait cité.
- L'outil `verify_citation` contrôle que l'extrait cité existe bien dans le
  passage référencé (comparaison tolérante aux espaces et à la normalisation)
  et que **chaque nombre cité figure à l'identique** dans la source.
- Une affirmation dont la citation échoue est affichée comme **« non
  sourcée »**, visuellement distincte, et ne peut pas alimenter un calcul ni
  une règle.
- Une réponse de l'assistant sur un document comporte donc trois catégories
  visibles : sourcé et vérifié, calculé par outil (avec trace), et
  explication de l'IA sans source (jugement, reformulation).

### 4.3 Traçabilité dans les rapports

Les rapports (§3.6 de la spec fonctionnelle) reprennent les citations et la
révision exacte des documents utilisés, avec leur empreinte. Un rapport
reste ainsi auditable même si le fabricant publie une nouvelle révision.

## 5. Règles de conception issues des documents

Une recommandation de document est transformée, après validation par
l'utilisateur, en **règle de conception** de l'un de trois types :

| Type | Vérification | Exemple |
|---|---|---|
| Structurelle | Statique, sur la netlist / le schéma | « 100 nF entre VDD et GND sur chaque broche d'alimentation » ; « R de pull-up ≤ 10 kΩ sur EN » |
| Comportementale | Par simulation, via un gabarit (§3.3 spec fonctionnelle) | « Marge de phase ≥ 45° avec Cout = 22 µF » ; « courant d'appel < 2 A au démarrage » |
| Non vérifiable sur schéma | Liste de contrôle manuelle | « Placer Cin au plus près de VIN » ; « plan de masse continu sous l'inductance » |

- Chaque règle porte sa source (citation), sa portée (quels composants) et
  son statut (respectée / violée / non vérifiable / ignorée par décision
  justifiée de l'utilisateur).
- L'outil `check_design_rules` évalue les règles structurelles de façon
  déterministe ; les règles comportementales sont évaluées par la machinerie
  de gabarits existante.
- Les règles non vérifiables sont reportées dans le rapport comme liste de
  contrôle pour la phase PCB (hors périmètre de Silx v1).

## 6. Calculs outillés

L'IA ne fait aucune arithmétique. Elle choisit une formule validée, fournit
les entrées (chacune sourcée ou saisie par l'utilisateur) et appelle un
outil.

### 6.1 Outils de calcul

- `evaluate_formula(formule, entrées)` : évaluation d'une expression avec
  **analyse dimensionnelle** (une formule incohérente en unités est
  rejetée, pas évaluée).
- `solve_formula(formule, inconnue, entrées)` : résolution pour une
  inconnue (analytique quand c'est possible, numérique bornée sinon).
- `worst_case(formule, entrées avec min/typ/max et tolérances)` : bornes
  par évaluation aux extrêmes, et en option Monte Carlo sur l'expression.
- `nearest_standard_value(valeur, série)` : arrondi en séries E6 à E192,
  avec choix de l'arrondi (plus proche, par excès, par défaut) et
  recherche de couples (ex. : meilleur couple E96 pour un rapport R2/R1).
- Le résultat de chaque calcul est un objet tracé : formule (et sa source),
  entrées (et leurs sources et statuts), sortie, unité, date. Il est
  affichable et rejouable.

### 6.2 Boucle de confirmation par simulation

Un calcul issu d'un document reste une prédiction. Conformément au §3.7 de la
spec fonctionnelle, une valeur proposée n'est présentée comme **« confirmée »**
qu'après application dans une copie du schéma et re-simulation, avec écart
calcul / simulation affiché. Un écart important est un signal utile en soi
(modèle SPICE incohérent, formule simplifiée de l'AN, mauvaise variante).

### 6.3 Ce que l'IA a le droit de faire

- Repérer la formule pertinente, la transcrire (soumise à validation).
- Choisir les entrées et expliquer ce choix.
- Interpréter le résultat, expliquer une procédure, signaler une hypothèse
  implicite de l'AN (ex. : « formule valable en conduction continue »).
- Elle n'a pas le droit d'énoncer un résultat numérique qui ne provient pas
  d'un outil. Une valeur numérique sans trace d'outil ou de citation dans
  une réponse est détectée et signalée (même mécanisme qu'au §4.2).

## 7. Conflits entre documents

Les conflits sont fréquents : révisions successives, datasheet vs note
d'application générique, datasheet vs errata, document vs modèle SPICE,
document vs gabarit de l'utilisateur.

### 7.1 Principe

Silx **ne tranche jamais silencieusement**. Quand deux sources donnent des
valeurs ou des consignes incompatibles pour le même paramètre et les mêmes
conditions, le conflit est affiché avec les deux citations, et l'utilisateur
choisit. La décision est enregistrée dans le projet (qui, quand, pourquoi)
et reprise dans le rapport.

### 7.2 Ordre de priorité par défaut (validé le 24/11/2026)

Proposé comme suggestion, jamais appliqué sans affichage :

1. Errata du composant exact ;
2. Datasheet du composant exact, révision la plus récente ;
3. Note d'application spécifique au composant ;
4. Note d'application ou guide générique du fabricant ;
5. Autres documents (livres, notes internes).

Et, au sein d'une même source : une valeur **min/max garantie** prime sur une
valeur **typique**, et une valeur tabulée prime sur une valeur lue sur figure.

### 7.3 Détection

- Même paramètre (nom normalisé + conditions compatibles) avec des valeurs
  incompatibles dans deux documents liés au même composant.
- Nouvelle révision importée : comparaison automatique des paramètres
  validés de l'ancienne révision, liste des changements, impact sur les
  calculs et règles du projet qui les utilisent.
- Écart modèle SPICE ↔ datasheet (CU5).
- Règle de document contradictoire avec un gabarit de l'utilisateur : le
  gabarit de l'utilisateur prime (c'est son exigence), mais le conflit est
  signalé.

## 8. Choix des IA par l'utilisateur et gestion des données

Décision (24/11/2026) : pas d'exigence de confidentialité particulière sur
les documents. En revanche, **l'utilisateur garde la main sur les IA qu'il
connecte** à Silx.

### 8.1 Connexion de ses propres IA

- **Fournisseurs configurables** : l'utilisateur déclare un ou plusieurs
  modèles, chacun par un profil (nom, type, adresse, clé d'API, modèle) :
  - API compatibles OpenAI (couvre la plupart des fournisseurs et des
    serveurs locaux : Ollama, LM Studio, vLLM, llama.cpp) ;
  - API Anthropic (Claude) et API Mistral ;
  - adresse personnalisée (serveur d'entreprise, passerelle interne).
- **Clients MCP externes** : le serveur MCP de Silx est accessible à un client
  MCP choisi par l'utilisateur (Claude Desktop, Claude Code, autre agent),
  qui pilote alors Silx avec les mêmes outils que le prompt intégré. Le
  prompt intégré n'est qu'un client parmi d'autres. **[H6]**
- **Choix par usage** : un modèle par défaut, modifiable par conversation ; en
  option, un modèle différent par rôle (ex. : modèle local rapide pour la
  recherche, modèle plus puissant pour l'explication).
- **Aucun modèle imposé ni fourni par Silx** : Silx fonctionne sans IA
  (toutes les fonctions documentaires de lecture, recherche, paramètres,
  calculs et règles restent utilisables à la main) ; l'IA est un mode de
  pilotage, pas une dépendance.
- **Capacités détectées** : à la connexion, Silx teste le modèle (appel
  d'outils, taille de contexte, vision) et adapte ce qu'il lui confie. Un
  modèle sans appel d'outils fiable est refusé pour les calculs et les
  règles, puisque tout passe par des outils.
- **Clés d'API** stockées dans le trousseau du système (Keychain macOS,
  gestionnaire d'identifiants Windows), jamais dans le projet.

### 8.2 Données transmises

- Documents, texte extrait et index restent stockés localement dans le
  projet ou la bibliothèque utilisateur ; Silx n'a pas de service en ligne.
- Seuls les passages retournés par les outils sont transmis au modèle, jamais
  le PDF entier (pour la précision et la taille de contexte, pas pour la
  confidentialité).
- L'interface indique quel modèle est actif et où il tourne (local ou
  distant), pour que l'utilisateur sache à qui il envoie ses données.
- En option, un journal des échanges avec le modèle (passages envoyés,
  appels d'outils, réponses), utile aussi pour comprendre une réponse.

## 9. Limites et risques d'hallucination

| Risque | Exemple | Parade |
|---|---|---|
| Valeur inventée ou mal recopiée | « Vref = 1,24 V » au lieu de 1,25 V | Valeurs uniquement via outils et citations vérifiées (§4.2) ; nombres comparés à la source |
| Confusion min / typ / max | Dimensionner sur une valeur typique | Paramètres typés ; pire cas par défaut dans les calculs ; mention explicite du champ utilisé |
| Conditions de test perdues | Valeur à 25 °C utilisée à 85 °C | Conditions conservées et affichées ; avertissement si les conditions du calcul diffèrent |
| Mauvaise variante | Paramètres du ‑5.0 utilisés pour le ‑ADJ | Variante liée à l'instance du schéma, affichée à chaque réponse |
| Erreurs d'extraction de tableau | Colonnes décalées, cellules fusionnées | Statut `extrait` vs `validé` ; aperçu côte à côte avec la page ; corpus de tests d'extraction (§10.3) |
| Erreurs OCR | `µ` lu `u` ou `p`, `0` lu `O`, signe moins perdu | Confiance OCR par page ; page faible confiance = validation obligatoire |
| Formule mal transcrite | Facteur oublié, mauvaise parenthèse | Validation de la transcription à côté de l'image ; analyse dimensionnelle ; confirmation par simulation |
| Formule hors domaine de validité | Formule en conduction continue appliquée en discontinue | L'IA doit énoncer les hypothèses de la formule ; la simulation révèle l'écart |
| Pratique mal généralisée | Règle d'un autre composant appliquée par analogie | Portée de chaque règle explicite ; règle non sourcée interdite |
| Modèle local trop faible | Petits modèles peu fiables sur les tableaux et les longs contextes | Recherche par outils (contexte réduit) ; l'extraction des tableaux est déterministe, pas faite par le LLM **[H7]** |
| Lecture de courbe | Valeur lue avec 20 % d'erreur sur échelle log | Numérisation assistée, incertitude affichée, jamais présentée comme garantie |

Limites assumées en v1 :

- Pas de lecture automatique de figures ni de schémas d'application.
- Qualité dépendante des PDF : un PDF mal construit ou scanné à basse
  résolution donne une extraction partielle, signalée comme telle.
- Silx ne garantit pas l'exactitude du document lui-même : il garantit que
  ce qu'il affiche est fidèle au document cité.
- Les pratiques de placement et de routage ne sont pas vérifiables (pas de
  PCB en v1), seulement listées.

## 10. Impact sur la spécification technique

### 10.1 Nouvelle brique : base documentaire (`Silx.Documents`)

- **Extraction PDF** : bibliothèque .NET de lecture PDF avec positions des
  mots et analyse de mise en page ; candidat : PdfPig (licence Apache 2.0).
  **[H8]**
- **Rendu des pages** pour le visualiseur et les aperçus de citation :
  liaison PDFium (candidats : Docnet.Core ou PDFtoImage), à confirmer sur
  macOS arm64 et Windows x64.
- **Tableaux** : heuristiques de reconstruction à partir des positions des
  mots et des traits ; c'est le point technique le plus risqué, à évaluer
  sur corpus avant engagement (jalon J0).
- **OCR** : Tesseract local (liaison .NET), optionnel, téléchargé à la
  demande pour ne pas alourdir le paquet.
- **Index** : SQLite avec recherche plein texte (FTS5) par projet ;
  embeddings locaux optionnels (via le même serveur Ollama que le modèle
  local). **[H4]**
- **Stockage projet** : sous-dossier `documents/` contenant les PDF, un
  fichier de métadonnées par document, les paramètres validés, formules,
  règles et décisions de conflit (format texte versionnable, JSON),
  l'index étant reconstructible.

### 10.2 Nouveaux outils MCP

À ajouter à la liste du §3.9 de la spec fonctionnelle :

| Outil | Rôle |
|---|---|
| `doc_import`, `doc_list`, `doc_link` | Import, inventaire, rattachement à un composant |
| `doc_search` | Recherche de passages, retourne identifiants de source |
| `doc_get_passage`, `doc_get_table`, `doc_get_figure` | Lecture ciblée |
| `doc_get_parameter` | Paramètre typé (min/typ/max, conditions, variante, statut) |
| `verify_citation` | Contrôle d'une citation et des nombres cités |
| `evaluate_formula`, `solve_formula`, `worst_case` | Calcul déterministe avec unités |
| `nearest_standard_value` | Séries E6 à E192, couples de valeurs |
| `list_design_rules`, `check_design_rules` | Règles issues des documents |
| `compare_model_to_datasheet` | Simulation des caractéristiques du modèle vs document |
| `list_conflicts` | Conflits détectés entre sources |

Les outils de création ou de modification (formule, règle, paramètre
`validé`, décision de conflit) passent par une **confirmation utilisateur**
dans l'interface, comme `apply_and_test`.

### 10.3 Calcul

- Évaluateur d'expressions avec unités : moteur maison léger ou bibliothèque
  existante (analyse d'expression + gestion d'unités, ex. UnitsNet) ;
  arbitrage à faire en J3. Testé comme la bibliothèque de mesures : tests de
  non-régression sur formules de référence (LM317, TL431, buck, filtres RC).
- `compare_model_to_datasheet` réutilise le socle ngspice et la bibliothèque
  de mesures existants (`mesures/`), aucune nouvelle logique de calcul.

### 10.4 Interface

- Panneau **Documents** (liste, statut d'extraction, rattachements).
- **Visualiseur PDF** intégré avec surlignage des citations.
- Vue **paramètres extraits** côte à côte avec la page source, pour
  validation rapide.
- Dans le prompt : les trois catégories de contenu (sourcé, calculé, non
  sourcé) visuellement distinctes ; indicateur du modèle actif (local ou distant).
- Écran **Modèles IA** : profils de fournisseurs, test de capacités,
  modèle par défaut et par rôle.

### 10.5 Couche d'accès aux modèles (multi-fournisseurs)

La couche d'adaptation function-calling ↔ MCP prévue au §4 de la spec
technique pour Mistral est généralisée en **couche fournisseurs** : une
interface commune (conversation, appel d'outils, flux de réponse) et un
adaptateur par famille d'API (compatible OpenAI, Anthropic, Mistral).
L'adaptateur compatible OpenAI couvre à lui seul la plupart des serveurs
locaux. Candidat : abstraction `Microsoft.Extensions.AI` (IChatClient),
qui s'intègre au SDK MCP .NET retenu. **[H8]**

En parallèle, le serveur MCP est exposé en transport local (stdio, ou HTTP
sur la boucle locale) pour les clients MCP externes de l'utilisateur.

Le volume d'outils augmente, ce qui pèse sur les petits modèles locaux
(choix d'outil). Piste : exposer les outils par groupes selon le contexte.

### 10.6 Tests et qualité

- **Corpus de référence** : une vingtaine de datasheets et AN publiques
  représentatives (régulateurs linéaires, références de tension, ampli-op,
  convertisseurs à découpage, logique), avec valeurs attendues saisies à la
  main pour les tableaux principaux.
- Mesures suivies : taux de paramètres correctement extraits, taux de
  citations vérifiées, taux de réponses contenant un nombre non tracé
  (objectif : zéro affiché sans marquage).

### 10.7 Modifications à reporter dans les specs existantes

- Spec fonctionnelle §3.9 : ajouter les outils du §10.2.
- Spec fonctionnelle §3.8 : panneau Documents, visualiseur PDF.
- Spec fonctionnelle §4 (hors périmètre v1) : lecture automatique de figures,
  formats autres que PDF, vérification des pratiques de placement.
- Spec fonctionnelle §5 (critères de succès) : « aucune valeur issue d'un
  document affichée sans citation vérifiée ou marquage non sourcé ».
- Spec technique §4 : remplacer la couche Mistral par la couche
  fournisseurs (§10.5) et ajouter l'exposition MCP aux clients externes.
- Spec technique §9 (décisions ouvertes) : extraction de tableaux maison vs
  bibliothèque, évaluateur de formules, embeddings locaux oui/non.

## 11. Hypothèses

- **[H1]** Le PDF couvre l'essentiel du besoin (datasheets et AN sont
  quasiment toujours en PDF).
- **[H2]** En v1, les figures servent de citation visuelle uniquement, pas de
  source de valeurs (validé le 24/11/2026).
- **[H3]** Les modèles locaux visés (taille 7 à 30 milliards de paramètres)
  n'ont pas une vision fiable sur des courbes techniques. À réévaluer si un
  modèle local multimodal fiable devient courant.
- **[H4]** La recherche lexicale suffit pour la v1, car les requêtes portent
  surtout sur des références, symboles et noms de paramètres ; la recherche
  sémantique est un plus optionnel.
- **[H5]** L'ordre de priorité des sources du §7.2 correspond à la pratique
  des bureaux d'étude (validé le 24/11/2026).
- **[H6]** Exposer le serveur MCP à des clients externes (transport local,
  sur la machine de l'utilisateur) est souhaité, en plus du prompt intégré.
- **[H7]** L'extraction des tableaux en paramètres est faite par du code
  déterministe ; l'IA peut aider à nommer ou rapprocher les paramètres,
  mais pas produire les valeurs.
- **[H8]** Les bibliothèques citées sont des candidates, non évaluées à ce
  stade ; leurs licences et leur fonctionnement sur macOS arm64 et
  Windows x64 sont à vérifier au jalon J0.
- **[H9]** Une validation humaine de moins de 5 minutes par datasheet est
  l'ordre de grandeur acceptable pour l'utilisateur ; objectif à confirmer.

## 12. Premiers jalons

| Jalon | Contenu | Critère de sortie |
|---|---|---|
| **J0. Faisabilité extraction** | Corpus de référence (§10.6) ; prototype d'extraction texte + tableaux sur PdfPig ; rendu de page | Taux de paramètres correctement extraits mesuré sur le corpus ; décision go / ajustement sur l'approche tableaux |
| **J1. Questions sourcées** | Import, métadonnées, profils de modèles IA, index FTS, `doc_search`, `doc_get_passage`, `verify_citation`, visualiseur avec surlignage | CU1 et CU6 fonctionnels avec modèle local ; 100 % des affirmations affichées avec citation vérifiée ou marquées non sourcées |
| **J2. Paramètres typés** | Tableaux → paramètres (min/typ/max, conditions, variantes), statut extrait/validé, vue de validation côte à côte | Paramètres principaux du corpus extraits et validables en moins de 5 min par datasheet **[H9]** |
| **J3. Calculs outillés** | Évaluateur avec unités, `solve_formula`, `worst_case`, séries E (livrés plus tôt, hors chantier documentaire : décision du 26/11/2026, `skills.md` §10.1), transcription de formule validée, boucle de confirmation par simulation | CU2 de bout en bout (LM317 → valeurs E96 → simulation confirmant Vout) |
| **J4. Règles de conception** | Règles structurelles et comportementales, liste de contrôle manuelle, `check_design_rules` | CU3 sur un étage buck de référence |
| **J5. Conflits et modèles** | Détection de conflits, comparaison de révisions, `compare_model_to_datasheet` | CU5 sur un ampli-op du corpus ; rapport listant conflits et décisions |

Après la v1, selon les retours : procédures d'AN pas à pas (CU4),
numérisation assistée de courbes (§3.4), bibliothèque documentaire partagée
en équipe.

## 13. Décisions validées (24/11/2026)

1. Ordre de priorité des sources du §7.2 : conservé tel quel.
2. Numérisation assistée de courbes : reportée après la v1 ; en v1, les
   figures servent de citation visuelle.
3. Bibliothèque documentaire : locale à l'utilisateur en v1 ; bibliothèque
   partagée en équipe reportée.
4. Pas d'exigence de confidentialité particulière ; l'utilisateur connecte
   librement ses propres IA (§8).

Restent ouverts, à trancher aux jalons : méthode d'extraction des tableaux
(J0), évaluateur de formules avec unités (J3), recherche sémantique en
complément de la recherche plein texte (optionnelle).
