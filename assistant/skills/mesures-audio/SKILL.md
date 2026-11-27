---
name: mesures-audio
description: Définir et réaliser dans Silx les mesures audio - rapport signal sur bruit (SNR, pondéré A), bruit de fond, dynamique, THD et THD+N, intermodulation (SMPTE, CCIF), diaphonie, réponse 20 Hz – 20 kHz, unités dBu, dBV et dBFS. À utiliser pour toute spécification ou mesure audio.
---

# Mesures audio

Une mesure audio n'a de sens qu'avec ses **conditions** : niveau de référence, charge, bande, pondération. Écrivez-les
toujours à côté du résultat.

## Unités

- **dBV** : référence 1 V efficace. **dBu** : référence 0,775 V efficace (1 mW sur 600 Ω). **dBFS** : référence la
  pleine échelle d'un convertisseur.
- Niveaux usuels : ligne grand public −10 dBV, ligne professionnelle +4 dBu.
- Les conversions se font par outil de calcul quand il existe ; sinon donnez la valeur dans l'unité renvoyée par
  Silx (V efficaces) et la formule.

## Mesures et comment les faire dans Silx

| Mesure | Définition | Dans Silx aujourd'hui |
|---|---|---|
| **Réponse en fréquence** | Écart de gain sur 20 Hz – 20 kHz (±x dB) par rapport à 1 kHz | Banc Bode, `get_raw_curve` ; gabarit dessiné pour la tolérance |
| **Bruit de fond** | Bruit en sortie, entrée chargée par l'impédance de source, sur 20 Hz – 20 kHz | `get_noise` avec la plage du banc réglée à 20 Hz – 20 kHz (Skill `analyser-bruit`) |
| **SNR** | Niveau de référence / bruit de fond, en dB, même bande | Bruit total de `get_noise`, rapporté au niveau de référence ; la **pondération A** n'est pas encore disponible : donnez le SNR non pondéré et dites-le |
| **Dynamique** | Niveau maximal avant écrêtage (THD 1 %) / bruit de fond | Niveau maximal par transitoire (Skill `amplificateur-audio`), bruit par `get_noise` |
| **THD** | Harmoniques 2 à 10 / fondamental | Mesure THD du catalogue (sur des périodes entières du dernier quart) |
| **THD+N** | Tout ce qui n'est pas le fondamental, sur la bande | Pas encore dans la bibliothèque de mesures |
| **IMD SMPTE** | 60 Hz + 7 kHz (rapport 4:1), produits autour de 7 kHz | Demande les raies du spectre (`get_spectrum`, manquant) |
| **IMD CCIF** | 19 kHz + 20 kHz, produit à 1 kHz | Idem |
| **Diaphonie** | Signal dans la voie non attaquée / voie attaquée | Banc AC sur un circuit stéréo : gain de l'entrée gauche vers la sortie droite |

## Pièges

- Le bruit intégré sur une plage plus large que 20 kHz gonfle le résultat ; réglez la plage du banc.
- La THD dépend du niveau et de la fréquence : une seule valeur sans conditions ne veut rien dire.
- Une THD calculée sur trop peu de périodes ou avant le régime établi est fausse : simulez assez longtemps.

## Conclure

Tableau mesure → conditions → valeur (simulée) → ce qui n'a pas pu être mesuré et pourquoi.
