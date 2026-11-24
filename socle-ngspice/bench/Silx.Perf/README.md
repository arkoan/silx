# Silx.Perf — bancs de performance des calculs et des threads

Mesure le socle de simulation (`NgspiceRunner`, `ParallelSimulator`) sur des circuits de taille croissante,
pour comparer les machines des ingénieurs et détecter les régressions.

## Lancer

Prérequis : .NET 10 et ngspice (42 ou plus) dans le `PATH`.

```sh
cd socle-ngspice/bench/Silx.Perf
dotnet run -c Release -- --rapide          # ~2 min, ordre de grandeur
dotnet run -c Release --                   # complet, ~5 min sur 4 cœurs
dotnet run -c Release -- threads openmp    # seulement certains scénarios
```

Options : `--sortie <dossier>` (défaut `resultats/`), `--par-max N` (parallélisme maximal testé, défaut 2 × cœurs).
Fermer les autres applications lourdes pendant la mesure. Résultats de référence (VM Linux 4 cœurs) dans `reference/`. Les résultats sont écrits en Markdown
(`perf-<machine>-<date>.md`) et en JSON pour comparer les machines entre elles.

## Scénarios

| Scénario | Ce qu'il mesure |
|---|---|
| `taille` | Temps d'un run seul selon la taille du circuit (RC, échelle RC, BJT, CMOS BSIM4, mixte XSPICE), part du solveur ngspice et du coût fixe, mémoire de ngspice. |
| `threads` | Débit d'un lot de runs selon le nombre de runs en parallèle : accélération, efficacité, cœurs réellement occupés, latence p50/p95. |
| `openmp` | Threads OpenMP internes de ngspice (modèles BSIM) combinés au parallélisme des runs. |
| `campagne` | Campagne type Monte Carlo : surcoût de l'orchestrateur, mémoire des résultats gardés. |
| `annulation` | Délai d'annulation d'un lot en cours, process ngspice orphelins, répertoires de travail restants. |

## Limites

- Mémoire de ngspice non mesurée sur macOS (« n/d ») ; CPU des process enfants non mesuré sous Windows.
- Circuits synthétiques : les ordres de grandeur valent pour des circuits de même nature, pas pour un modèle constructeur lourd.
