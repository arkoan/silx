# Silx — application (éditeur de schéma + simulation + gabarits)

Évolution du prototype `silx-proto` en application : on dessine un circuit, on simule avec ngspice
(socle `socle-ngspice/`), on trace les courbes et on vérifie des gabarits pass/fail (bibliothèque `mesures/`).

## Lancer (macOS)

```bash
brew install ngspice                      # moteur de simulation
dotnet run --project src/Silx.App         # .NET 10 requis
dotnet test                               # 25 tests, dont simulations ngspice réelles
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
et se glissent. Le tableau en bas à droite donne t/f et la valeur en A et B, et B−A (Δt, ΔV, 1/Δt ; en Bode : gain, phase, rapport de fréquences, Δφ et pente dB/déc).

## Fils élastiques

Glisser un composant (ou le pivoter) garde ses fils attachés : ils restent horizontaux ou verticaux et s'étirent ; un coude est
ajouté quand il le faut, sans déplacer les jonctions ni les broches fixes. Masses et étiquettes posées sur une broche suivent.
Les fils des bancs de test attachés au circuit suivent aussi (et ⌘Z les restaure). Si un étirement touche une autre broche,
un bandeau rouge le signale pendant le glisser. Calcul dans `Silx.Etude/Elastique.cs`.

## Composants SPICE importés et éditeur de symbole

Palette, groupe du bas : l'icône d'import lit un fichier de modèles (`.model` D, NPN, PNP, NMOS, PMOS, NJF, PJF et `.subckt`).
Chaque modèle devient une tuile avec un symbole par défaut (diode, transistor, ou boîte pour un sous-circuit) et ses broches
reliées par nom. Clic droit sur la tuile, ou « Modifier le symbole » dans les propriétés : choisir le gabarit, glisser les
broches, et relier une broche du modèle à une broche du symbole (cliquer l'une puis l'autre). La netlist suit toujours
l'ordre des broches du modèle. Bibliothèque : un fichier `.silxcomp` (JSON) par composant dans `~/Documents/Silx/Composants`
(ou `SILX_COMPOSANTS`). Conception : `docs/specs/fils_elastiques_composants_spice.md`.

## Raccourcis

R résistance · C condensateur · L inductance · V source de tension · I source de courant · D diode · G masse · N étiquette ·
W fil · + / − / 0 zoom du schéma · ⌘R pivoter · Suppr · Échap · ⌘Z / ⇧⌘Z · Maj+clic pour poser plusieurs composants · clic droit ou bouton du milieu pour déplacer la vue.

## HYPOTHÈSES (à valider)

1. Composants à 2 broches espacées de 4 pas ; masse et étiquette à 1 broche ; broche 1 = « + » d'une source.
2. Toutes les masses forment le nœud 0 ; deux étiquettes de même nom relient leurs nœuds.
3. Analyse AC : la réponse mesurée est V(nœud) brute, ce qui suppose une source d'entrée « AC 1 ».
   Marges de phase/gain : le nœud doit porter le gain de boucle (pas encore d'aide pour ouvrir la boucle).
4. Modèles de diodes intégrés génériques et indicatifs (1N4148, 1N4007, BAT54) en attendant la gestion de bibliothèques.
5. Pas encore d'enregistrement d'étude ; les transistors passent par l'import de modèles SPICE.
6. Gabarits dessinés en temps absolu (pas encore relatifs au front du stimulus) ; en Bode, sur le gain en dB uniquement.
7. Les éléments d'un banc se connectent au circuit par coïncidence de broches ou par étiquette de même nom ; les alimentations sont dans le banc.
8. Une définition de composant importé est partagée par toutes ses instances : modifier son symbole les met toutes à jour.
9. Le socle refuse un nœud relié à une seule broche, même de sous-circuit : une sortie de sous-circuit en l'air bloque la simulation.


## Stimuli en clair

Une source se règle par sa forme (continu, échelon / impulsion, sinus, petit signal AC, ou expression SPICE libre) et
des champs en notation ingénieur ; l'expression SPICE est générée. Le **premier stimulus** posé dans un banc fixe son type :
petit signal → fréquentiel (Bode), sinon temporel. Curseurs : en mode curseurs, un clic pose un curseur sur la courbe la plus
proche (A puis B) ; un gabarit dessiné s'applique à la courbe la plus proche de son premier point.
