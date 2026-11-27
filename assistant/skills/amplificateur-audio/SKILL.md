---
name: amplificateur-audio
description: Analyser ou concevoir un amplificateur audio dans Silx - préampli, ampli de puissance classe A, AB ou D, ampli casque, courant de repos et distorsion de croisement, puissance sur 4 ou 8 Ω, THD en fonction de la puissance et de la fréquence, slew rate, stabilité sur haut-parleur, réseau de Zobel, emballement thermique.
---

# Amplificateur audio

## 1. Repos et thermique

- `get_operating_point` (Skill `verifier-polarisation`) : tension continue en sortie (proche de 0 V pour une sortie
  sans condensateur de liaison), **courant de repos** de l'étage de sortie.
- Classe AB : trop peu de courant de repos → distorsion de croisement ; trop → dissipation. Le multiplicateur de VBE
  (ou les diodes de polarisation) doit suivre la température des transistors de sortie : balayage de `TEMP`
  (`run_campaign`) et lecture du courant de repos ; une hausse forte avec la température signale un risque
  d'emballement thermique (à confirmer par le couplage thermique réel, que Silx ne modélise pas).

## 2. Puissance et écrêtage

- Banc sinus 1 kHz sur la charge nominale (4 ou 8 Ω, résistance d'abord, puis modèle de haut-parleur).
- Monter l'amplitude jusqu'à l'écrêtage ; la puissance de sortie se déduit de la valeur efficace mesurée
  (`list_measurements` : valeur efficace) sur la charge, P = Veff² / R, calculée par outil quand il existe.
- Vérifier la **symétrie** de l'écrêtage (point de repos centré) et le comportement à la sortie d'écrêtage (collage).
- Si `run_campaign` ne sait pas balayer l'amplitude d'un sinus, faites quelques simulations en changeant l'amplitude
  ou demandez à l'utilisateur de la régler.

## 3. Distorsion

- THD (catalogue : harmoniques 2 à 10, sur des périodes entières du dernier quart) à plusieurs puissances
  (1 W, puissance nominale, juste avant l'écrêtage) et à plusieurs fréquences (100 Hz, 1 kHz, 10 kHz : la
  contre-réaction diminue avec la fréquence).
- Au moins 3 périodes dans le dernier quart du signal : à 100 Hz il faut une simulation d'au moins 120 ms.
- La THD+N et l'intermodulation attendent la bibliothèque de mesures (Skill `mesures-audio`).

## 4. Bande et vitesse

- Bode petit signal : bande à −3 dB, platitude 20 Hz – 20 kHz.
- Slew rate : échelon ou sinus à forte amplitude et haute fréquence ; une sinusoïde qui devient triangle signale la
  limite de vitesse.

## 5. Stabilité sur charge réelle

- Un haut-parleur n'est pas une résistance : modèle simple Re + Le en série, plus un circuit RLC parallèle pour la
  résonance mécanique. Ajoutez une charge capacitive (câble, filtre) pour l'essai de stabilité.
- Échelon carré en sortie (petit niveau) : dépassement et sonnerie révèlent une marge de phase faible ;
  `diagnostiquer-banc-en-echec` si un gabarit est franchi.
- Remèdes : réseau de Zobel (R ≈ impédance nominale, en série avec C) en sortie, inductance de sortie avec
  résistance en parallèle, compensation.

## Classe D

La simulation d'un ampli à découpage est longue (fréquence de découpage élevée, durée audio) : réduisez la durée au
strict nécessaire, vérifiez le filtre de sortie séparément en AC, et attendez-vous à des THD peu précises si le pas
n'est pas fin.

## Conclure

Point de repos et dérive thermique, puissance avant écrêtage, THD aux points choisis, bande, comportement sur charge
réactive, actions proposées (vérifiées par re-simulation).
