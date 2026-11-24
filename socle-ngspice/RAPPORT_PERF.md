# Performance des calculs et gestion des threads — rapport (2026-11-24)

Bancs : `bench/Silx.Perf` (voir son README). Résultats bruts : `bench/Silx.Perf/reference/`.
Machine de mesure : VM cloud Linux, Intel Xeon 2,1 GHz, **4 cœurs** (1 thread par cœur), 10,8 Go, .NET 10, ngspice 42.
Ce n'est pas un poste d'ingénieur : les chiffres absolus sont à refaire sur Mac et Windows, les tendances devraient tenir.

## Ce qu'il faut retenir

1. **La parallélisation des runs fonctionne bien jusqu'au nombre de cœurs.** Sur des circuits réels (runs de 15 ms à 2 s), 4 runs en parallèle vont ×3,2 à ×4,0 plus vite qu'un seul (efficacité 81 à 100 %). Au-delà du nombre de cœurs, aucun gain et la latence double : la borne par défaut de `ParallelSimulator` (nombre de cœurs) est la bonne.
2. **Un piège grave a été trouvé et corrigé : les threads OpenMP de ngspice.** ngspice évalue les modèles MOS BSIM3/BSIM4 avec 2 threads OpenMP par défaut. En lot, ces threads s'ajoutent aux runs parallèles et tournent à vide en attendant : sur un lot de 8 runs CMOS (100 inverseurs) sur 4 cœurs, **2,7 s avec 1 thread par run, 20,6 s avec le réglage par défaut (×8), 260 s avec 4 threads (×100)**. Après correction, toutes les combinaisons restent entre 2,5 et 2,9 s sur ce lot (et entre 4,4 et 4,8 s avec 200 inverseurs).
3. **Le coût de l'orchestrateur est faible.** Lancement de process + netlist + lecture du .raw : 6 à 8 ms par run, soit 13 % du temps sur les runs les plus courts (RC de 2 ms), négligeable au-delà de 100 ms. Une campagne de 2000 runs courts passe en 3,9 s (508 runs/s).
4. **L'annulation est immédiate et propre** : retour en 21 ms, aucun process ngspice orphelin, aucun répertoire temporaire laissé.
5. **La limite à surveiller est la mémoire des résultats, pas le CPU.** Garder toutes les courbes d'un run de 100 nœuds × 2000 points coûte 1,6 Mo ; une campagne Monte Carlo de 1000 runs en garderait 1,6 Go. Avec seulement les vecteurs sondés (`.save`), c'est 10 Ko par run.

## Correction apportée au socle

- `NgspiceRunner.NumThreads` : nombre de threads OpenMP par run (écrit un `.spiceinit` dans le répertoire du run).
- `ParallelSimulator.ThreadsPerRun` : par défaut, les cœurs sont répartis entre les runs simultanés (1 thread par run quand le lot occupe tous les cœurs, tous les cœurs pour un run seul, qui va alors ×2 plus vite sur 4 cœurs).
- En lot, `OMP_WAIT_POLICY=PASSIVE` est passé à ngspice (sauf si l'utilisateur l'a déjà fixé) : les threads en attente dorment au lieu de tourner à vide. Ce n'est pas mis pour un run seul, où cela coûte ~25 %.
- 2 tests ajoutés (11/11 verts).

## Résultats détaillés

### Temps d'un run seul selon la taille

| Circuit | Petit | Moyen | Gros | Très gros |
|---|---|---|---|---|
| Échelle RC, 2000 pts | 10 cellules : 16 ms | 1000 : 238 ms | 5000 : 2,2 s | 20 000 : 12,4 s |
| Étages BJT 2N2222, 1000 pts | 1 : 9 ms | 10 : 16 ms | 50 : 55 ms | 200 : 205 ms |
| Inverseurs CMOS BSIM4, 2100 pts | 10 : 115 ms | 50 : 611 ms | 200 : 2,2 s | 500 : 6,2 s |
| Mixte XSPICE (bascules + DAC), 26 000 pts | 4 : 84 ms | 16 : 190 ms | 64 : 592 ms | 256 : 2,1 s |

Ces temps sont avec 1 thread OpenMP. Le solveur ngspice représente plus de 90 % du temps dès que le run dépasse 100 ms. La mémoire de ngspice reste faible (13 à 53 Mo par process, même à 20 000 cellules). Le temps croît plus vite que la taille sur l'échelle RC (×5 cellules → ×9 en temps), linéairement sur les autres familles.

### Débit selon le nombre de runs parallèles (4 cœurs)

| Circuit (durée d'un run) | ×1 | ×2 | ×4 | ×8 |
|---|---|---|---|---|
| RC court (6 ms) | 135 runs/s | ×1,97 | ×3,46 | ×3,89 |
| BJT 10 étages (15 ms) | 59 runs/s | ×1,90 | ×3,22 | ×3,67 |
| XSPICE 16 bascules (180 ms) | 5,4 runs/s | ×1,91 | ×3,81 | ×4,08 |
| Échelle RC 1000 (220 ms) | 4,4 runs/s | ×2,00 | ×3,91 | ×3,86 |
| CMOS 50 inverseurs (540 ms) | 1,8 runs/s | ×2,01 | ×3,99 | ×4,04 |

À ×8 (deux fois plus de runs que de cœurs), la latence de chaque run double sans gain de débit, sauf un peu sur les runs très courts où l'attente du disque est recouverte.

### Ordres de grandeur pour une campagne (4 cœurs, après correction)

| Campagne de 1000 runs | Durée estimée |
|---|---|
| Filtre RC ou petit étage BJT | 2 à 6 s |
| Mixte XSPICE 16 bascules | ~50 s |
| CMOS 50 inverseurs BSIM4 | ~2 min 20 |
| CMOS 200 inverseurs BSIM4 | ~9 min |

Le débit est proportionnel au nombre de cœurs : un poste à 8 cœurs performants divise ces durées par 2 environ.

## Hypothèses et limites

1. Mesures sur une VM Linux 4 cœurs uniquement. Le Mac de Régis était hors ligne. Sous Windows, le lancement de process coûte plus cher : le surcoût fixe y sera plus élevé (à mesurer).
2. Pas d'hyperthreading sur la VM : la spec demande de borner aux cœurs **physiques**, je n'ai pas pu vérifier si les cœurs logiques en plus apportent quelque chose. Les Mac Apple Silicon n'ont pas d'hyperthreading.
3. Le piège OpenMP est mesuré avec le ngspice d'Ubuntu (compilé avec OpenMP). Un ngspice compilé sans OpenMP n'est pas concerné, la correction est sans effet dessus.
4. Circuits synthétiques (pas de modèles constructeur ni de sous-circuits lourds).
5. Mémoire de ngspice non mesurée sous macOS, CPU des process non mesuré sous Windows.

## Suites proposées

- Lancer `dotnet run -c Release -- --rapide` sur le Mac de Régis et sur un poste Windows type, puis comparer les JSON.
- Pour les campagnes Monte Carlo : ne garder que les vecteurs sondés et calculer les mesures au fil de l'eau au lieu de garder toutes les courbes (limite mémoire du point 5).
