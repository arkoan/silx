# Socle de simulation Silx — rapport (2026-11-23)

Code : `silx-socle.zip` (solution .NET 10, 3 projets : Silx.Core, Silx.Bench, Silx.Core.Tests). Aucun dépôt Git, rien n'a été poussé.

## Contenu
- **Modèle de circuit** (`Circuits/Circuit.cs`) : composants SPICE génériques, analyses OP/DC/AC/Tran, détection des références dupliquées.
- **Netlist** (`Netlist/NetlistWriter.cs`) : génération + vérifs basiques (pas de masse, nœud flottant).
- **Exécution** (`Simulation/NgspiceRunner.cs`) : `ngspice -b -r`, un répertoire temporaire par run, annulation par CancellationToken (kill du process), nettoyage.
- **Parallélisme** (`ParallelSimulator.cs`) : `Parallel.ForEachAsync` borné, progression via `Channel`, un run en échec n'arrête pas le lot.
- **Lecture .raw** (`Raw/RawFileReader.cs`) : binaire + ASCII, réel + complexe, détection de troncature.
- **Tests** : 9/9 verts, avec valeurs analytiques (diviseur 5 V, RC à fc = -3 dB, RC à τ = 63 %, ordre des résultats en parallèle, annulation, run en erreur).

## Mesure CLI vs libngspice (4 cœurs logiques, ngspice 42, Linux)
| Cas | CLI séq. | CLI ×4 | libngspice séq. |
|---|---|---|---|
| RC transitoire court (~500 pts) | 9,3 ms | 2,6 ms (379 runs/s) | 1,75 ms (573 runs/s) |
| RC transitoire long (~50 000 pts) | 119 ms | 34 ms (29 runs/s) | 120 ms |

Surcoût fixe de la CLI ≈ 7,5 ms par run (lancement process + fichiers).

## Conclusion proposée
- **Garder la CLI pour la v1.** Le surcoût (~7,5 ms) devient négligeable dès que le run dépasse quelques dizaines de ms, et la CLI se parallélise sur tous les cœurs.
- **libngspice est limité à une instance par process** (état global) : pour paralléliser il faudrait plusieurs process hôtes, donc on retrouve un coût de process. Sur circuit court, libngspice séquentiel (573 runs/s) ne bat la CLI ×4 (379 runs/s) que de ×1,5 ; sur circuit réel, la CLI ×4 est ×3,5 plus rapide.
- Piste à réévaluer seulement pour des campagnes de dizaines de milliers de runs très courts : pool de workers libngspice (1 process par cœur, plusieurs runs chacun).

## Hypothèses (à valider)
1. Parallélisme par défaut = cœurs **logiques** (.NET n'expose pas les cœurs physiques de façon portable) ; la spec dit physiques.
2. Mesures faites sur Linux/conteneur 4 cœurs, pas sur Mac/Windows : le coût de lancement de process est plus élevé sous Windows, la conclusion pourrait pencher un peu vers libngspice là-bas.
3. Circuit RC simple : pas de modèles complexes (BJT/MOS, sous-circuits), pas de Monte Carlo/`.step` natif ngspice.
4. Un seul plot par fichier .raw (pas de multi-plot).
5. XSPICE (décision mixte de base) : les codemodels sont chargés par le `spinit` de ngspice installé ; non testé ici.
6. Mesure du surcoût incluant écriture de netlist et lecture du .raw côté C#, pas seulement ngspice seul.

## Non fait
Bibliothèque de mesures (marge de phase, THD...), `.step`/Monte Carlo, packaging, XSPICE, mesures Mac/Windows.
