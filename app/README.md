# Silx — application (éditeur de schéma + simulation + gabarits)

Évolution du prototype `silx-proto` en application : on dessine un circuit, on simule avec ngspice
(socle `socle-ngspice/`), on trace les courbes et on vérifie des gabarits pass/fail (bibliothèque `mesures/`).

## Lancer (macOS)

```bash
brew install ngspice                      # moteur de simulation
dotnet run --project src/Silx.App         # .NET 10 requis
dotnet test                               # 15 tests, dont simulations ngspice réelles
```

ngspice est cherché dans `SILX_NGSPICE`, puis `/opt/homebrew/bin`, `/usr/local/bin`, puis le PATH.
Captures d'écran hors écran : `SILX_SHOT_DIR=/un/dossier dotnet test`.

## Organisation de l'écran

| Zone | Contenu |
|---|---|
| Barre du haut | Nouveau / exemple, outils (sélection, fil, pivoter, supprimer, annuler), verdict global, **Simuler et vérifier** (F5) |
| Gauche | Palette de composants avec recherche et raccourcis clavier |
| Centre | Onglets **Schéma** / **Netlist** (synchronisée en direct) |
| Bas | **Courbes** (transitoire ou Bode gain + phase), **Vérifications** (ERC), **Journal ngspice** |
| Droite | **Propriétés** du composant sélectionné, réglages des **analyses** (.tran, .ac), **gabarits** (mesure + nœud + bornes → PASS/FAIL) |

## Projets

- `src/Silx.Etude` : modèle de schéma (ex-prototype), connectivité, étiquettes de nœuds, ERC, conversion vers le `Circuit` du socle,
  orchestration simulation → mesures → verdicts (`Verification`).
- `src/Silx.App` : interface Avalonia 12 (UI en C#, sans XAML), canevas, ScottPlot.
- `tests/Silx.App.Tests` : connectivité, ERC, notation ingénieur, RC contre valeurs analytiques (tr = 2,2 ms, fc = 159 Hz), diode, rendu de la fenêtre.

## Raccourcis

R résistance · C condensateur · L inductance · V source de tension · I source de courant · D diode · G masse · N étiquette ·
W fil · ⌘R pivoter · Suppr · Échap · ⌘Z / ⇧⌘Z · Maj+clic pour poser plusieurs composants · clic droit ou bouton du milieu pour déplacer la vue.

## HYPOTHÈSES (à valider)

1. Composants à 2 broches espacées de 4 pas ; masse et étiquette à 1 broche ; broche 1 = « + » d'une source.
2. Toutes les masses forment le nœud 0 ; deux étiquettes de même nom relient leurs nœuds.
3. Analyse AC : la réponse mesurée est V(nœud) brute, ce qui suppose une source d'entrée « AC 1 ».
   Marges de phase/gain : le nœud doit porter le gain de boucle (pas encore d'aide pour ouvrir la boucle).
4. Modèles de diodes intégrés génériques et indicatifs (1N4148, 1N4007, BAT54) en attendant la gestion de bibliothèques.
5. Pas d'enregistrement de fichier ni de transistors dans cette première itération.
