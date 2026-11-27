---
name: piloter-silx
description: Démarche pour travailler avec le simulateur Silx via son serveur MCP (silx-mcp). À utiliser dès qu'une demande touche un projet Silx (ouvrir un projet .silx ou une démo, décrire un circuit, lister ou lancer des bancs de test, lire verdicts, marges et courbes, modifier une valeur, enregistrer) ou quand on hésite sur l'outil Silx à appeler.
---

# Piloter Silx

Silx est un simulateur électronique (ngspice, XSPICE pour le mixte) pour ingénieurs de bureau d'étude. Le serveur
MCP `silx-mcp` donne des outils déterministes : **chaque nombre rapporté doit venir d'un de ces outils**.

## Modèle de données

- Un **projet** (`.silx`) contient un **circuit sous test** (le montage, éventuellement ses alimentations) et des
  **bancs de test**.
- Un **banc** est un calque posé sur le circuit : sources et charges propres, une analyse (transitoire, AC, DC),
  des signaux sondés, des **critères** chiffrés (mesure du catalogue + bornes → PASS/FAIL) et des **gabarits**
  dessinés. Le type d'analyse découle des stimuli du banc (petit signal AC → fréquentiel ; « balayage DC » → DC).
- Un banc peut porter une **campagne** : balayage, Monte-Carlo ou coins.
- Une valeur de composant modifiée vaut pour **tous** les bancs.

## Mode direct

Si l'application Silx est ouverte, les outils agissent sur ses onglets et l'utilisateur voit chaque action.
`list_projects` le dit et liste les onglets : **commencez toujours par `list_projects`**, et travaillez sur le projet
déjà ouvert plutôt que d'en rouvrir un. Si un outil répond que l'utilisateur a interrompu ou mis l'IA en pause,
arrêtez-vous et demandez comment poursuivre.

## Ordre des appels

1. `list_projects` → `open_project` (chemin `.silx`) ou `open_demo` si rien n'est ouvert.
2. `describe_project` : composants, valeurs, tolérances, modèles, nœuds de chaque broche, ERC, bancs.
3. `list_testbenches` : analyse et réglages, sources, critères et bornes, gabarits, sondes, campagne.
4. `run_simulation` (un banc ou tous) → verdict, valeur, bornes et **marge** par contrainte.
5. `get_test_summary` pour relire sans resimuler ; `get_raw_curve` pour les points d'un signal
   (`list_signals` donne les noms : `OUT`, `I(R1)`, `I(Q1, C)`…).
6. `run_campaign` puis `get_worst_case` pour la dispersion (voir la Skill `regler-campagne`).
7. `set_component_value` change une valeur ou une tolérance **en mémoire** ; les résultats périmés sont effacés,
   il faut resimuler. `save_project` écrit sur disque et ne remplace un fichier qu'avec `ecraser = true` :
   ne l'appelez qu'à la demande de l'utilisateur.

Autres outils selon la version du serveur : `get_netlist` (netlist donnée à ngspice), `list_measurements`
(catalogue des mesures), `get_operating_point` (point de fonctionnement .op), `get_noise` (analyse de bruit). Si un
outil cité par une Skill n'existe pas, dites-le et faites sans, ne l'inventez pas.

## Lire un résultat

- La **marge** dit à quelle distance de la borne on est : un PASS à 1 % de marge est fragile, signalez-le.
- Un banc jamais simulé est « non simulé » : ne concluez rien sur lui.
- Les mesures en régime établi (fréquence, crête à crête, efficace, THD) portent sur le **dernier quart** du signal,
  sur des périodes entières : une durée de simulation trop courte fausse ces mesures.
- Les valeurs arrivent en SI avec une écriture ingénieur (`"valeur": 0.0022, "valeur_texte": "2,20 ms"`) : reprenez
  l'écriture ingénieur dans vos réponses.

## Présenter

Pour chaque contrainte : verdict, valeur mesurée, bornes, marge. Séparez visiblement ce qui est **simulé**, ce qui
est **sourcé** (document cité) et ce qui est **votre jugement**. Une modification n'est « confirmée » qu'après
re-simulation de tous les bancs concernés.

## Pièges

- Notation : « M » et « meg » = méga dans les champs de Silx, mais dans une netlist SPICE « M » = milli. Silx réécrit
  R, C, L sans ambiguïté ; méfiez-vous des expressions libres.
- Ne modifiez pas un composant pour « tester » sans le dire : en mode direct l'utilisateur le voit, et ⌘Z annule ;
  remettez la valeur d'origine si l'essai n'est pas retenu.
