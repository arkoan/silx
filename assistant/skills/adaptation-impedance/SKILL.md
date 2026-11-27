---
name: adaptation-impedance
description: Adapter des impédances en RF ou en signal dans Silx - réseau en L, en π ou en T, transformateur, adaptation sur 50 Ω, ROS (VSWR), pertes par réflexion (return loss), bande d'adaptation, pertes des composants réels. À utiliser pour « adapter mon ampli sur 50 Ω », « réseau d'adaptation », « ROS », « S11 », abaque de Smith.
---

# Adaptation d'impédance

## Choisir la topologie

- **Réseau en L** (deux éléments réactifs) : le plus simple ; son facteur de qualité est imposé par le rapport des
  résistances, Q = √(Rhaute / Rbasse − 1). Bande relative de l'ordre de 1/Q.
- **Réseau en π ou en T** (trois éléments) : Q choisi (plus grand que celui du L) pour régler la bande ou filtrer les
  harmoniques.
- **Deux réseaux en L en cascade** (passage par une résistance intermédiaire) : bande plus large qu'un L unique.
- **Transformateur ou ligne quart d'onde** : large bande ou fort rapport.
- Passe-bas (L série, C parallèle) pour atténuer les harmoniques d'un étage de puissance ; passe-haut pour bloquer le
  continu ou les basses fréquences.

Les formules d'un réseau se font évaluer par un outil de calcul quand le serveur en propose un ; sinon, proposez une
estimation annoncée comme telle, puis ajustez par balayage (ci-dessous). Une partie réactive de la charge s'absorbe
dans l'élément adjacent.

## Vérifier par simulation (outils actuels)

Le serveur ne donne pas encore l'impédance complexe d'un port. Méthode avec un banc AC :
- source AC d'amplitude Vs avec sa résistance interne Rs (par exemple 50 Ω), réseau, charge RL ;
- tracer la tension sur la charge rapportée à Vs. Adaptation parfaite et sans pertes : |VL| = (Vs/2)·√(RL/Rs), soit
  **−6,02 dB** pour RL = Rs. L'écart à cette référence est la perte de désadaptation **plus** les pertes du réseau :
  c'est le gain transducique, la grandeur qui compte en pratique.
- `run_simulation` puis `get_raw_curve` ; l'échantillonnage adaptatif AC de Silx resserre les points autour des
  résonances.
- Ajustement : `run_campaign` en balayage de l'élément à régler (C ou L), puis `set_component_value` à la valeur
  retenue (valeur normalisée).

Le ROS et le S11 se déduisent du coefficient de réflexion ; leur calcul attend `get_impedance` ou
`get_s_parameters` (analyse `.sp` de ngspice) côté serveur.

## Composants réels

- Inductances : Q fini (résistance série) et fréquence d'auto-résonance ; condensateurs : ESR, ESL, auto-résonance.
  Modélisez-les (R série, C parallèle) quand on approche de quelques dizaines de MHz.
- Tolérances : un réseau à fort Q est sensible ; faites un Monte-Carlo (`regler-campagne`).
- Les parasites du PCB (pistes, plots) ne sont pas dans le schéma : à signaler.

## Conclure

Topologie et justification, valeurs normalisées, gain transducique à la fréquence centrale et bande d'adaptation
(simulés), sensibilité aux tolérances, limites (parasites non modélisés, ROS non calculé si l'outil manque).
