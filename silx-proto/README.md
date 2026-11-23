# Silx — prototype de l'éditeur de schéma (Avalonia)

But : fixer la portée v1 de l'éditeur (décision ouverte n°2) et trancher ScottPlot vs OxyPlot (n°5) sur du concret.

## Contenu
- `src/Silx.Core` : modèle de schéma, connectivité (union-find), génération de netlist, ERC de base, runner `ngspice -b`. Aucune dépendance UI.
- `src/Silx.Editor` : app Avalonia 12 (UI construite en C#, sans XAML), canevas custom, tracé ScottPlot.
- `tests/Silx.Tests` : 4 tests cœur (dont RC contre la valeur analytique 1−e⁻¹ avec ngspice réel) + 1 test UI headless qui rend la fenêtre en PNG.

Lancer : `dotnet run --project src/Silx.Editor` — tests : `dotnet test` (net10.0, xunit.v3 3.2.2, runner Microsoft.Testing.Platform).

## Fonctionnel
Grille + snap, zoom/pan, placement R/C/L/V/GND, déplacement, rotation (R), suppression, fils en L, jonctions en T, numérotation auto (R1, R2…), undo/redo par instantanés, netlist SPICE régénérée en direct, ERC (référence dupliquée, pas de masse, broche flottante, valeur manquante), simulation `.tran` et tracé.

## HYPOTHÈSES (à valider)
1. Composants à 2 broches distantes de 4 pas de grille ; GND à 1 broche ; broche 1 = « + » pour V.
2. Connexion = coïncidence sur un point de grille, ou extrémité/broche posée sur l'intérieur d'un fil (jonction en T implicite).
3. Tous les GND forment le même nœud `0` ; pas de labels de nœuds ni de bus.
4. Nœuds nommés `N001…` (pas de noms utilisateur).
5. Undo/redo par copie complète du schéma (OK pour quelques centaines d'éléments, à remplacer par des commandes si besoin).
6. Netlist → schéma (sens inverse) NON prototypé ; pas d'éditeur de symboles, pas de bibliothèques, pas de semi-conducteurs.
7. Une seule analyse `.tran` codée en dur ; ngspice appelé en CLI avec `wrdata`.
8. Ubuntu `ngspice 42` et .NET 10 utilisés ici ; rien testé sur macOS/Windows.

## Constats pour la portée v1
- Le noyau « schéma → netlist → ngspice → courbe » tient en ~400 lignes de C# : un dessin minimal est réaliste en v1.
- Le coût réel est dans l'éditeur de symboles, les bibliothèques et la netlist→schéma, pas dans le canevas.
- ScottPlot 5.1.59 s'intègre à Avalonia 12 sans friction (1 000 points instantané). Le test de charge Monte Carlo (centaines de courbes) n'est PAS fait : ScottPlot vs OxyPlot reste ouvert.
- Piège : `Avalonia.Headless.XUnit` 12.1.6 exige xunit.v3 **3.2.2** exactement.
