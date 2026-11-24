# Silx — application (éditeur de schéma + simulation + gabarits)

Évolution du prototype `silx-proto` en application : on dessine un circuit, on simule avec ngspice
(socle `socle-ngspice/`), on trace les courbes et on vérifie des gabarits pass/fail (bibliothèque `mesures/`).

## Lancer (macOS)

```bash
brew install ngspice                      # moteur de simulation
dotnet run --project src/Silx.App         # .NET 10 requis
dotnet test                               # 24 tests, dont simulations ngspice réelles
```

ngspice est cherché dans `SILX_NGSPICE`, puis `/opt/homebrew/bin`, `/usr/local/bin`, puis le PATH.
Captures d'écran hors écran : `SILX_SHOT_DIR=/un/dossier dotnet test`.

## Organisation de l'écran

| Zone | Contenu |
|---|---|
| Barre du haut | Nouveau / exemple, outils (sélection, fil, pivoter, supprimer, annuler), verdict global, **Simuler et vérifier** (F5) |
| Gauche | Palette de composants avec recherche et raccourcis clavier |
| Centre | Onglets **Schéma** / **Netlist** ; au-dessus du schéma, **Circuit sous test** puis un onglet par **banc de test** |
| Bas | **Courbes** en mode *Simulation* (courbes seules) ou *Test* (gabarits dessinés, zone interdite hachurée, marge affichée), vue temporelle ou Bode ; **Vérifications** (ERC) ; **Journal ngspice** |
| Droite | **Propriétés** du composant sélectionné, réglages des **analyses** (.tran, .ac), **critères mesurés** (mesure + nœud + bornes → PASS/FAIL) et **gabarits dessinés** (verdict + marge) |

## Projets

- `src/Silx.Etude` : modèle de schéma (ex-prototype), connectivité, étiquettes de nœuds, ERC, conversion vers le `Circuit` du socle,
  orchestration simulation → mesures → verdicts (`Verification`).
- `src/Silx.App` : interface Avalonia 12 (UI en C#, sans XAML), canevas, ScottPlot.
- `tests/Silx.App.Tests` : connectivité, ERC, notation ingénieur, RC contre valeurs analytiques (tr = 2,2 ms, fc = 159 Hz), diode, rendu de la fenêtre.

## Circuit sous test et bancs de test

Le circuit sous test ne contient que le montage (pas de source) ; ses étiquettes de nœud (IN, OUT, VCC…) servent de points de
connexion. Un **banc de test** est un calque posé sur le circuit, comme les appareils d'un labo : sources, alimentations,
charges, plus ses analyses et ses gabarits. Ses éléments (en violet) ne s'affichent et ne s'éditent que lorsque le banc est
sélectionné ; le circuit reste visible dessous, atténué et verrouillé. Une modification du circuit vaut pour tous les bancs ;
« Simuler et vérifier » depuis l'onglet du circuit relance tous les bancs en parallèle (non-régression).

## Gabarits dessinés

En mode Test, choisir le signal, cliquer « Limite haute » ou « Limite basse », poser les points de gauche à droite sur le graphique,
puis double-clic ou Entrée (Échap annule). Pour modifier un gabarit, survoler un point (il grossit) et le glisser : la marge est recalculée en direct. Le gabarit est évalué aussitôt sur les résultats déjà simulés : verdict, et marge
(plus petite distance verticale à la limite) avec le point où elle est atteinte. Calcul dans `mesures/` (`GabaritGraphique`).

## Sondes et graphiques empilés

Chaque banc a une pile de graphiques ; chacun affiche les tensions sondées (pastilles colorées, retirables).
Outil sonde (P) : survoler le schéma surligne l'équipotentiel et affiche son nom ; un clic ajoute sa tension au graphique
actif (bordure bleue). L'icône « ajouter un graphique » crée un graphique vide et active la sonde. Gabarits et curseurs
s'affichent sur les graphiques qui contiennent leur signal.

## Loupes et curseurs

Schéma (barre du haut) et courbes (barre des courbes) : zoom +, zoom −, tout voir, zoom sur une fenêtre (tracer un rectangle).
Curseurs : activer l'icône curseurs, choisir le signal, cliquer pour poser A puis B (deux par courbe) ; ils suivent la courbe
et se glissent. Le tableau en bas à droite donne t/f et la valeur en A et B, et B−A (Δt, ΔV, 1/Δt ; en Bode rapport de fréquences et pente dB/déc).

## Raccourcis

R résistance · C condensateur · L inductance · V source de tension · I source de courant · D diode · G masse · N étiquette ·
W fil · + / − / 0 zoom du schéma · ⌘R pivoter · Suppr · Échap · ⌘Z / ⇧⌘Z · Maj+clic pour poser plusieurs composants · clic droit ou bouton du milieu pour déplacer la vue.

## HYPOTHÈSES (à valider)

1. Composants à 2 broches espacées de 4 pas ; masse et étiquette à 1 broche ; broche 1 = « + » d'une source.
2. Toutes les masses forment le nœud 0 ; deux étiquettes de même nom relient leurs nœuds.
3. Analyse AC : la réponse mesurée est V(nœud) brute, ce qui suppose une source d'entrée « AC 1 ».
   Marges de phase/gain : le nœud doit porter le gain de boucle (pas encore d'aide pour ouvrir la boucle).
4. Modèles de diodes intégrés génériques et indicatifs (1N4148, 1N4007, BAT54) en attendant la gestion de bibliothèques.
5. Pas d'enregistrement de fichier ni de transistors dans cette première itération.
6. Gabarits dessinés en temps absolu (pas encore relatifs au front du stimulus) ; en Bode, sur le gain en dB uniquement.
7. Les éléments d'un banc se connectent au circuit par coïncidence de broches ou par étiquette de même nom ; les alimentations sont dans le banc.
