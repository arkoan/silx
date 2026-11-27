---
name: filtres-audio-et-egalisation
description: Vérifier ou concevoir des filtres audio dans Silx - correcteur de tonalité (Baxandall), égaliseur, préampli phono RIAA, filtre de séparation d'enceinte (Linkwitz-Riley, Butterworth), filtre anti-rumble ou anti-ronflement, avec courbe cible, tolérances et sommation des voies.
---

# Filtres audio et égalisation

## Courbes cibles

| Fonction | Ce qu'il faut vérifier |
|---|---|
| **RIAA** (lecture) | Constantes de temps normalisées 3180 µs, 318 µs, 75 µs ; écart à la courbe normalisée sur 20 Hz – 20 kHz, typiquement ±0,5 dB ou mieux |
| **Correcteur Baxandall** | Plage de réglage grave et aigu (potentiomètres aux butées), réponse plate au milieu, fréquences de charnière |
| **Égaliseur** | Fréquence centrale, gain et Q de chaque bande ; interaction entre bandes voisines |
| **Filtre de séparation** | Fréquence de coupure, pente, **somme des voies** en amplitude et en phase ; Linkwitz-Riley d'ordre 4 : voies à −6 dB à la coupure, en phase, somme plate |
| **Anti-rumble, anti-ronflement** | Atténuation sous 20 Hz ou à 50/100 Hz, effet sur la bande utile |

## Démarche

1. Banc Bode (AC) avec l'impédance de source et la charge réelles (entrée d'ampli, haut-parleur pour un filtre
   passif).
2. `run_simulation` puis `get_raw_curve` (gain en dB) ; comparez à la courbe cible. Un **gabarit dessiné** du banc
   (bande haute et basse) rend la vérification automatique ; la génération de gabarits normalisés par l'IA attend
   l'outil `generate_mask`.
3. **Potentiomètres** : modélisez-les par deux résistances, et balayez-les avec `run_campaign` (balayage de chacune,
   ou liste de positions) pour tracer la famille de courbes.
4. **Filtre de séparation** : simulez chaque voie et leur somme (nœud qui additionne, ou somme des tensions sur les
   charges) ; la phase relative compte autant que l'amplitude.
5. **Tolérances** : `regler-campagne` en Monte-Carlo ; pour la RIAA, l'écart à la courbe en pire cas est la grandeur
   qui intéresse.

## Pièges

- Un filtre passif d'enceinte se calcule sur l'impédance du haut-parleur, pas sur sa valeur nominale : avec une
  résistance de charge, la courbe réelle sera différente.
- Les condensateurs céramiques X7R varient avec la tension et sont microphoniques : en audio, préférez film ou C0G
  dans le chemin du signal (remarque de revue, pas simulable).
- La dynamique d'entrée : un égaliseur qui amplifie de 12 dB peut écrêter l'étage suivant ; vérifiez en transitoire.

## Conclure

Courbe simulée comparée à la cible (écart maximal et où), plage de réglage, effet des tolérances, remarques de choix
de composants.
