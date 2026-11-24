# Bancs de performance Silx — 2026-11-24 20:33

- Machine : Intel(R) Xeon(R) Processor @ 2.10GHz, 4 cœurs logiques, 10.8 Go, Ubuntu 24.04.5 LTS (X64)
- Logiciels : .NET 10.0.15, ngspice-42
- Mode : complet

## 1. Temps d'un run selon la taille du circuit (1 run à la fois)

Temps médian sur 3 runs. « Analyse » = temps du solveur ngspice ; « Fixe » = lancement, netlist, lecture .raw.

| Famille | Taille | Points | Run (ms) | Analyse (ms) | Fixe (ms) | Mém. ngspice (Mo) |
|---|---:|---:|---:|---:|---:|---:|
| rc | 1 cellule | 526 | 9.6 | 2.0 | 7.6 | n/d |
| echelle_rc | 10 cellules | 2044 | 16.2 | 7.0 | 9.2 | 13 |
| echelle_rc | 100 cellules | 2050 | 33.6 | 25.0 | 8.6 | 13 |
| echelle_rc | 1000 cellules | 2050 | 238.3 | 216.0 | 22.3 | 15 |
| echelle_rc | 5000 cellules | 2050 | 2165.4 | 2089.0 | 76.4 | 23 |
| echelle_rc | 20000 cellules | 2050 | 12443.6 | 12261.0 | 182.6 | 53 |
| bjt | 1 étage(s) | 1008 | 9.2 | 3.0 | 6.2 | n/d |
| bjt | 10 étage(s) | 1008 | 15.7 | 9.0 | 6.7 | 13 |
| bjt | 50 étage(s) | 1008 | 55.1 | 46.0 | 9.1 | 13 |
| bjt | 200 étage(s) | 1008 | 204.9 | 192.0 | 12.9 | 15 |
| cmos | 10 inverseurs | 2125 | 115.3 | 105.0 | 10.3 | 13 |
| cmos | 50 inverseurs | 2125 | 611.4 | 603.0 | 8.4 | 14 |
| cmos | 200 inverseurs | 2125 | 2241.5 | 2227.0 | 14.5 | 16 |
| cmos | 500 inverseurs | 2125 | 6234.1 | 6203.0 | 31.1 | 20 |
| xspice | 4 bascules | 26237 | 84.0 | 75.0 | 9.0 | 13 |
| xspice | 16 bascules | 26454 | 190.2 | 179.0 | 11.2 | 13 |
| xspice | 64 bascules | 26454 | 591.8 | 580.0 | 11.8 | 14 |
| xspice | 256 bascules | 26454 | 2136.2 | 2108.0 | 28.2 | 16 |

## 2. Débit selon le nombre de runs en parallèle

Lot de runs indépendants (variantes de valeurs) exécuté par `ParallelSimulator`, 1 thread OpenMP par run. Accélération = débit / débit à 1 ; efficacité = accélération / parallélisme. CPU = temps CPU des process ngspice / durée (cœurs réellement occupés).

**RC 1er ordre, 500 points (coût fixe) (1 cellule), lot de 1074 runs**

| Parallélisme | Durée (s) | Débit (runs/s) | Accélération | Efficacité | CPU (cœurs) | Latence p50 (ms) | p95 (ms) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 7.94 | 135.3 | ×1.00 | 100 % | 0.79 | 5.9 | 8.5 |
| 2 | 4.03 | 266.2 | ×1.97 | 98 % | 1.59 | 5.9 | 8.5 |
| 4 | 2.29 | 468.6 | ×3.46 | 87 % | 2.60 | 6.5 | 11.4 |
| 8 | 2.04 | 526.5 | ×3.89 | 49 % | 3.10 | 12.8 | 20.6 |

**Échelle RC de N cellules, 2000 points (1000 cellules), lot de 36 runs**

| Parallélisme | Durée (s) | Débit (runs/s) | Accélération | Efficacité | CPU (cœurs) | Latence p50 (ms) | p95 (ms) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 8.14 | 4.4 | ×1.00 | 100 % | 0.97 | 218.2 | 246.7 |
| 2 | 4.07 | 8.8 | ×2.00 | 100 % | 1.94 | 219.6 | 260.6 |
| 4 | 2.08 | 17.3 | ×3.91 | 98 % | 3.80 | 217.4 | 265.5 |
| 8 | 2.11 | 17.1 | ×3.86 | 48 % | 3.75 | 447.0 | 693.9 |

**N étages émetteur commun 2N2222 (10 étage(s)), lot de 338 runs**

| Parallélisme | Durée (s) | Débit (runs/s) | Accélération | Efficacité | CPU (cœurs) | Latence p50 (ms) | p95 (ms) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 5.74 | 58.9 | ×1.00 | 100 % | 0.90 | 15.1 | 22.6 |
| 2 | 3.03 | 111.7 | ×1.90 | 95 % | 1.78 | 15.5 | 22.2 |
| 4 | 1.78 | 189.7 | ×3.22 | 81 % | 3.06 | 18.8 | 29.2 |
| 8 | 1.57 | 216.0 | ×3.67 | 46 % | 3.41 | 33.4 | 54.5 |

**N inverseurs CMOS BSIM4 (50 inverseurs), lot de 16 runs**

| Parallélisme | Durée (s) | Débit (runs/s) | Accélération | Efficacité | CPU (cœurs) | Latence p50 (ms) | p95 (ms) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 9.05 | 1.8 | ×1.00 | 100 % | 0.99 | 539.7 | 704.6 |
| 2 | 4.50 | 3.6 | ×2.01 | 101 % | 1.95 | 538.6 | 621.2 |
| 4 | 2.27 | 7.1 | ×3.99 | 100 % | 3.89 | 548.0 | 630.1 |
| 8 | 2.24 | 7.2 | ×4.04 | 51 % | 3.87 | 1055.7 | 1184.7 |

**Compteur XSPICE de N bascules + N DAC/RC (16 bascules), lot de 45 runs**

| Parallélisme | Durée (s) | Débit (runs/s) | Accélération | Efficacité | CPU (cœurs) | Latence p50 (ms) | p95 (ms) |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 8.34 | 5.4 | ×1.00 | 100 % | 0.98 | 180.3 | 208.5 |
| 2 | 4.35 | 10.3 | ×1.91 | 96 % | 1.92 | 180.3 | 234.6 |
| 4 | 2.19 | 20.6 | ×3.81 | 95 % | 3.70 | 180.6 | 211.6 |
| 8 | 2.04 | 22.0 | ×4.08 | 51 % | 3.88 | 351.5 | 500.8 |

## 3. Threads OpenMP de ngspice × runs parallèles (CMOS BSIM4)

ngspice parallélise l'évaluation des modèles BSIM3/BSIM4 avec OpenMP, 2 threads par défaut. On compare un run seul (latence) et un lot (débit) selon ce réglage. « auto » = réglage choisi par `ParallelSimulator` (cœurs / runs simultanés). En lot, les threads fixés à la main tournent avec OMP_WAIT_POLICY=PASSIVE.

| Threads OpenMP/run | Runs parallèles | Lot | Durée (s) | Débit (runs/s) | Latence p50 (ms) | CPU (cœurs) |
|---:|---:|---:|---:|---:|---:|---:|
| auto (4) | 1 | 3 | 3.26 | 0.92 | 1086 | 3.94 |
| 1 | 1 | 3 | 6.52 | 0.46 | 2144 | 0.99 |
| 2 | 1 | 3 | 5.08 | 0.59 | 1679 | 1.98 |
| 4 | 1 | 3 | 3.38 | 0.89 | 1135 | 3.92 |
| auto (1) | 4 | 8 | 4.53 | 1.77 | 2129 | 3.79 |
| 1 | 4 | 8 | 4.41 | 1.82 | 2164 | 3.95 |
| 2 | 4 | 8 | 4.63 | 1.73 | 2237 | 3.87 |
| 4 | 4 | 8 | 4.78 | 1.67 | 2303 | 3.84 |

## 4. Campagne de runs : surcoût de l'orchestrateur et mémoire

Tous les résultats (.raw lus) sont gardés en mémoire, comme pour un post-traitement statistique. Surcoût = durée réelle − (somme des temps ngspice / parallélisme).

| Cas | Runs | Parallélisme | Durée (s) | Débit (runs/s) | Surcoût orchestrateur | Mémoire .NET gardée (Mo) | Par run (Ko) |
|---|---:|---:|---:|---:|---:|---:|---:|
| RC court, 1 vecteur, 500 pts | 2000 | 4 | 3.94 | 508 | 13 % | 20.9 | 10 |
| Échelle RC 100, tous les vecteurs, 2000 pts | 200 | 4 | 2.05 | 98 | 13 % | 320.2 | 1601 |

## 5. Annulation d'un lot en cours

Lot de 16 runs CMOS (≈ 1 à 4 s chacun), 4 en parallèle, annulé après 1,5 s :

- Retour de `RunAllAsync` après annulation : **21 ms**
- Process ngspice encore vivants au retour : 0 ; 500 ms après : 0
- Répertoires de travail restants : 0

