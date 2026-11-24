# Fils élastiques, composants SPICE et éditeur de symbole — note de conception

Statut : proposition, implémentée en première version sur la branche `claude/project-thread-yl7yb9`
(partie de `ui/sondes-graphiques`). Les points marqués **HYPOTHÈSE** sont à valider par Régis.

## 1. Fils qui suivent les composants

### Besoin
On déplace (ou on pivote) un composant : les fils restent attachés à ses broches. Un fil n'est jamais oblique ;
il s'étire horizontalement ou verticalement.

### Modèle
Un fil reste un segment horizontal ou vertical entre deux points de grille (`Wire(A, B)`). Une liaison est une
suite de segments ; il n'y a pas d'objet « fil multi-segments ». C'est ce qui rend l'algorithme local et simple.

### Algorithme (`Elastique.Suivre`, fonction pure, testée)
Entrée : les fils du schéma, et pour chaque broche déplacée son ancienne et sa nouvelle position
(un déplacement donne le même vecteur à toutes les broches ; une rotation donne un vecteur par broche).

1. **Découpe des T** : un fil dont l'intérieur passe par une broche déplacée est coupé en deux à cet endroit,
   pour que la broche emporte une extrémité au lieu de se détacher.
2. **Fils entièrement entraînés** : un fil dont les deux extrémités sont sur des broches déplacées est translaté.
3. **Fils attachés par une extrémité** P (déplacée de **d**), l'autre extrémité Q restant en place :
   - la composante de **d** le long du fil est absorbée : le fil s'allonge ou se raccourcit ;
   - la composante perpendiculaire d⊥ décale tout le segment : Q devient Q' = Q + d⊥ ;
   - au coin Q : si un seul autre fil y arrive, perpendiculaire, et qu'aucune broche ni jonction n'est en Q,
     ce fil voisin **glisse** (son extrémité suit Q', il s'étire dans son axe) ; sinon on **insère un petit
     segment** Q→Q' (un « coude »), ce qui préserve les jonctions et les broches fixes.
4. **Normalisation** : suppression des segments de longueur nulle et des doublons, fusion de deux segments
   alignés qui se suivent sans broche ni jonction au point commun.

Pendant le glisser, le calcul repart toujours du schéma d'origine avec le déplacement total
(pas d'accumulation d'erreurs, retour exact si on revient au point de départ). Une seule entrée d'annulation.

Le même calcul sert à la **rotation** (vecteur différent par broche) et aux **bancs de test** : quand on déplace un
composant du circuit sous test, les fils des calques des bancs attachés à ses broches suivent aussi ; un élément
de banc posé directement sur la broche reçoit un fil en L pour rester connecté.

### Limites connues (v1)
- Chaque fil décalé peut faire son coude au coin (par défaut) ou du côté de la broche ; on garde la première
  combinaison (64 essais au plus) qui conserve les connexions. Si aucune ne convient (ex. masse remontée juste sous une
  broche : tout coude droit passe dessus), le canevas l'indique par un bandeau rouge pendant le glisser
  (**HYPOTHÈSE** : avertir plutôt qu'interdire, comme la plupart des éditeurs).
- Un fil dont aucune extrémité n'est tenue par une broche fixe (fil en l'air) est translaté avec le composant.
- Pas encore de déplacement d'un segment de fil seul (glisser un fil perpendiculairement) ni de routage
  automatique évitant les composants ; prévus ensuite, ils réutiliseront `Elastique`.

## 2. Composants basés sur des modèles SPICE

### Ce qu'on importe
Un fichier texte (.lib, .mod, .cir, .sub, .txt) ou un texte collé. L'analyseur (`BibliothequeSpice.Analyser`)
reconnaît, en gérant les lignes de continuation `+` et les commentaires `*` et `;` :

| Forme | Élément instancié | Broches du modèle (ordre SPICE) |
|---|---|---|
| `.model X D(...)` | `D` | A, K |
| `.model X NPN(...)` / `PNP` | `Q` | C, B, E |
| `.model X NMOS(...)` / `PMOS` | `M` | D, G, S, B |
| `.model X NJF(...)` / `PJF` | `J` | D, G, S |
| `.subckt X n1 n2 … [params: …]` … `.ends` | `X` | n1, n2, … (noms du fichier) |

Les modèles XSPICE numériques (`d_and`, `d_dff`…) ne sont pas encore importables : ils demandent la syntaxe
tableau `[ ]` et des ponts A/D ; ils viendront avec le mixte avancé.

### Définition de composant
Une **définition** (`DefinitionComposant`, enregistrée en JSON, extension `.silxcomp`) rassemble :
- le nom, la lettre SPICE (D, Q, M, J, X), le nom du modèle ou du sous-circuit et **le texte SPICE d'origine**
  (recopié tel quel dans la netlist, une seule fois par définition) ;
- la liste ordonnée des **broches du modèle** ;
- le **symbole** : primitives graphiques (traits, rectangles, cercles, textes) en pas de grille, et les
  **broches du symbole** (position entière sur la grille, nom affiché) ;
- la **correspondance** : chaque broche du symbole porte le nom de la broche du modèle qu'elle représente.

Instance placée : `ComponentKind.Spice` + référence à la définition. Sa « valeur » contient les paramètres
d'instance facultatifs (`W=2u L=180n`, `area=2`, ou `params: GAIN=10`). Ligne générée :
`Q1 <nœud C> <nœud B> <nœud E> 2N2222 [valeur]` ; les nœuds sont pris **dans l'ordre des broches du modèle**,
via la correspondance, quel que soit l'ordre ou la position des broches sur le symbole.

### Bibliothèque
Les définitions de l'utilisateur sont dans un dossier (`~/Documents/Silx/Composants`, ou `SILX_COMPOSANTS`) et
apparaissent en bas de la palette. **HYPOTHÈSE** (v1) : une définition est partagée par toutes ses instances ; modifier
son symbole les met toutes à jour et leurs fils suivent (broches repérées par leur nom de modèle). Quand l'enregistrement
d'étude existera, il faudra y copier les définitions utilisées pour qu'une étude reste simulable si la bibliothèque change.

### Contrôles (ERC)
Erreur si une broche du modèle n'est reliée à aucune broche du symbole, ou à plusieurs ; si la référence ne
commence pas par la bonne lettre ; avertissement habituel pour les broches non connectées.

## 3. Éditeur de symbole

Fenêtre ouverte à l'import d'un fichier qui ne contient qu'un modèle, par clic droit sur une tuile de la palette, et
depuis le panneau Propriétés (« Modifier le symbole »).
Économe en place, trois zones :

```
┌───────────────┬──────────────────────────────────┐
│ Broches du    │  [gabarit ▾] [⟲ auto] [+ broche]  │
│ modèle        │                                  │
│ ✓ C  → 1      │        aperçu du symbole         │
│ ✓ B  → 2      │      sur la grille, broches      │
│ ✗ E           │      numérotées et nommées       │
│               │                                  │
├───────────────┴──────────────────────────────────┤
│ ⚠ E n'est reliée à aucune broche   [Annuler] [OK] │
└──────────────────────────────────────────────────┘
```

- **Gabarit de dessin** : diode, NPN, PNP, NMOS, PMOS, JFET, ou boîte rectangulaire. Le gabarit adapté au type du
  modèle est choisi d'office ; pour un sous-circuit, une boîte avec les broches réparties à gauche et à droite
  (**HYPOTHÈSE** : première moitié des broches à gauche, le reste à droite, dans l'ordre du fichier).
- **Correspondance automatique** par nom (C/B/E, D/G/S/B, A/K, ou noms identiques ; casse ignorée). Le reste se fait
  à la main : cliquer une broche du modèle dans la liste puis une broche du symbole (ou l'inverse) pour les relier ;
  un nouveau clic sur une broche reliée la délie.
- **Broches du symbole** : on les fait glisser sur la grille ; « + broche » en ajoute une (pour une boîte) ;
  Suppr enlève la broche sélectionnée.
- **État** en bas : liste des problèmes en clair ; OK reste possible avec des avertissements, pas avec une broche du
  modèle non reliée.

## 4. Tests

- Fils : déplacements horizontal, vertical, oblique, rotation, T sur la broche, coude près d'une jonction,
  retour au point de départ, orthogonalité et conservation de la connectivité sur l'exemple RC pour une série de
  déplacements.
- Modèles : analyse de `.model` (continuations, commentaires, paramètres entre parenthèses ou non), de `.subckt`
  avec `params:` ; netlist dans l'ordre du modèle même quand le symbole est dans un autre ordre ; JSON aller-retour.
- Simulations ngspice réelles : suiveur à NPN importé (V(E) ≈ V(B) − 0,65 V), filtre RC en sous-circuit importé
  (fc = 159 Hz).
- Interface hors écran : déplacement d'un composant à la souris avec fils attachés, ouverture de l'éditeur de
  symbole et capture.


## 5. Lire le modèle pour s'y retrouver (ajout)

Retour sur un modèle de MOSFET fabricant (BSS138) : broches numérotées `20 10 30 50`, rôles seulement en commentaire
(`*20=DRAIN 10=GATE 30=SOURCE 50=VTEMP`), quatrième broche thermique, deux sous-circuits dans le même fichier.

- **Infos lues** (`InfosModele`) : commentaires d'en-tête (description), libellés des broches (`n=NOM` ou `n: NOM` dans
  les commentaires autour de `.subckt`), composition interne (nombre de MOS, diodes, R, C, sources commandées ; modèles internes).
- **Nature probable** : libellés DRAIN/GATE/SOURCE (ou COLLECTOR/BASE/EMITTER, ANODE/CATHODE) + modèle interne NMOS/PMOS/NPN/PNP
  → symbole du composant au lieu d'une boîte, broches reliées par rôle ; sans broche de substrat, symbole MOS 3 pattes.
  **HYPOTHÈSE** : sans aucun libellé, un sous-circuit décrit comme MOSFET (N-Ch, P-Ch, DMOS…) à 3 ou 4 broches suit l'ordre
  usuel D, G, S (, B) ; c'est écrit « à vérifier » dans l'éditeur.
- **Broches raccordées d'office** (`BrochesCachees`) : une broche du modèle peut rester hors du symbole, reliée à la masse ou à
  une tension fixe (source générée par instance). **HYPOTHÈSE** : une broche dont le libellé contient TEMP reçoit 25 V
  (température en °C ; vérifié sur le BSS138 : V(50) − 25 est l'écart à 25 °C).
- **Compatibilité PSpice** : dès qu'un composant importé est présent, ngspice tourne avec `ngbehavior=psa` (TEMP, accolades
  imbriquées des modèles PSpice) ; les circuits sans modèle importé ne changent pas.
- L'éditeur affiche ces infos, le nom des broches « 20 (DRAIN) », et un bouton « Voir le texte SPICE ».
