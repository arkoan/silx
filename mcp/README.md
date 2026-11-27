# Silx — serveur MCP

Serveur [MCP](https://modelcontextprotocol.io) qui expose Silx à un client IA choisi par l'utilisateur
(Claude Desktop, Claude Code, ou tout autre agent MCP) : ouvrir un projet, lire ses bancs de test, lancer les
simulations et les campagnes, lire les verdicts, le pire cas et les courbes. Silx ne contient pas d'IA : le serveur
donne au modèle des outils déterministes, et chaque valeur qu'il rapporte vient d'une simulation ngspice.

Module séparé : il réutilise le cœur (`app/src/Silx.Etude`, `socle-ngspice`, `mesures`) sans dépendre de l'interface
Avalonia. Transport stdio (le client lance le serveur sur la machine de l'utilisateur ; rien n'écoute sur le réseau).

## Mode direct : voir l'IA travailler dans Silx

Si l'application Silx est ouverte, `silx-mcp` lui passe les appels de l'IA au lieu de les exécuter en arrière-plan :
les outils agissent sur les **onglets de l'application**, et l'utilisateur voit chaque action au fil de l'eau.

- L'écran suit l'IA : l'onglet du projet, le banc simulé ou le circuit sous test modifié passent au premier plan ;
  le composant changé est sélectionné (⌘Z annule le changement) ; verdicts, courbes et vue « Campagne » se mettent à
  jour sans resimuler ; la progression d'une campagne s'affiche dans la barre d'état.
- Un badge dans la barre d'outils dit ce que fait l'IA (« IA : simule « Réponse en fréquence »… ») ; son infobulle
  garde l'historique des actions. Le bouton **Interrompre** arrête l'opération en cours et met l'IA en pause : ses
  appels suivants sont refusés, avec la consigne de s'arrêter et de demander à l'utilisateur, jusqu'à **Reprendre**.
- `list_projects` liste les onglets ouverts (y compris les modifications non enregistrées : l'onglet fait foi) ;
  `open_project` et `open_demo` ouvrent un onglet ; `close_project` refuse de fermer un onglet modifié non enregistré.
- Les outils s'exécutent sur le fil de l'interface (les simulations ngspice tournent à côté) : l'IA et l'utilisateur
  ne modifient jamais le projet au même instant.

Bascule automatique : `silx-mcp` cherche l'application à son lancement, puis à chaque appel d'outil tant qu'il n'a
aucun projet ouvert en arrière-plan (Silx ouvert après le client IA est donc rejoint, sans rien perdre). Silx fermé en
cours de route : l'opération en cours se termine par une erreur lisible et la suite repart en arrière-plan.
`--arriere-plan` force l'ancien fonctionnement ; `SILX_MCP_JOURNAL=1` écrit les bascules sur la sortie d'erreur.

Canal : socket de domaine Unix lisible par l'utilisateur seul, `~/Library/Application Support/Silx/direct.sock`
(`SILX_DIRECT=chemin` pour un autre, `SILX_DIRECT=off` désactive, côté application comme côté serveur). Une seule
fenêtre Silx prend le canal. Code : `Relais` (aiguillage dans silx-mcp), `HoteDirect` (écoute dans l'application),
`app/src/Silx.App/MainWindow.IA.cs` (affichage et interruption).

## Lancer

Avec l'application installée (macOS), le serveur est livré dans le paquet, à côté de Silx, avec le même ngspice :

```
/Applications/Silx.app/Contents/MacOS/silx-mcp
```

Depuis le dépôt (.NET 10 et ngspice requis, `brew install ngspice`) :

```bash
dotnet run --project mcp/src/Silx.Mcp
dotnet test mcp        # logique des outils + bout en bout sur stdio, simulations ngspice réelles
```

ngspice est cherché comme dans l'application : `SILX_NGSPICE`, ngspice embarqué, Homebrew, puis le PATH.
`SILX_DELAI_SIMULATION` (secondes) borne la durée d'une simulation (5 min par défaut).

## Brancher un client

**Claude Code** :

```bash
claude mcp add silx -- /Applications/Silx.app/Contents/MacOS/silx-mcp
```

**Claude Desktop** (`~/Library/Application Support/Claude/claude_desktop_config.json`) :

```json
{
  "mcpServers": {
    "silx": { "command": "/Applications/Silx.app/Contents/MacOS/silx-mcp" }
  }
}
```

Tout autre client MCP : même commande, transport stdio.

## Outils

| Outil | Rôle |
|---|---|
| `list_projects` | Projets ouverts par le serveur et démos disponibles |
| `open_project`, `open_demo` | Ouvre un fichier `.silx` (ou une démo) ; renvoie un identifiant court (`p1`…) |
| `describe_project` | Circuit sous test : composants, valeurs, tolérances, modèles, nœuds de chaque broche, ERC, bancs |
| `list_testbenches` | Bancs : analyse et réglages, sources et charges, critères et bornes, gabarits dessinés, sondes, campagne |
| `list_measurements` | Catalogue des mesures (dépassement, temps de montée, bande passante, marge de phase, THD…) |
| `list_signals` | Tensions de nœud et courants de broche lisibles sur un banc |
| `get_netlist` | Netlist SPICE donnée à ngspice pour un banc |
| `run_simulation` | Simulation nominale d'un banc ou de tous ; verdict, valeur, bornes et marge par contrainte |
| `run_campaign` | Balayage, Monte-Carlo ou coins ; taux de conformité, statistiques (moyenne, σ, Cpk), pire cas |
| `get_test_summary` | Bilan des derniers résultats, sans resimuler |
| `get_worst_case` | Pire cas (ou N pires tirages) de la dernière campagne : valeurs des composants, mesures |
| `get_raw_curve` | Points d'une courbe (réduite en gardant pics et fronts), sur une plage choisie |
| `set_component_value` | Change une valeur ou une tolérance, en mémoire ; les résultats périmés sont effacés |
| `save_project` | Écrit le projet ; ne remplace jamais un fichier sans `ecraser = true` |
| `close_project` | Ferme un projet du serveur |

Les noms d'outils suivent la spec fonctionnelle (§3.9) ; descriptions, messages et résultats sont en français, en
unités SI, avec l'écriture ingénieur à côté (`"valeur": 0.0022, "valeur_texte": "2,20 ms"`). Sans identifiant, un
outil prend le seul projet ouvert ; un banc se désigne par son nom ou son numéro.

Les réglages passés à `run_campaign` ne valent que pour ce lancement : le banc garde sa campagne. Seuls
`set_component_value` (en mémoire) et `save_project` (sur disque) modifient un projet.

## Hors de ce premier lot

Génération de bancs et de gabarits (`generate_testbench`, `generate_mask`), sensibilités, propositions de
modification et optimisation (`propose_modification`, `apply_and_test`, `optimize_parameter`), outils documentaires
(`doc_*`, spec `docs/specs/assistant_documents.md`) : ils dépendent de la structure de l'assistant IA, encore à
définir. Les nouvelles analyses (DC, bruit) apparaîtront d'elles-mêmes dans `list_testbenches` et `run_simulation`
dès qu'elles seront dans le cœur ; leurs réglages propres seront à ajouter à la description des bancs.
