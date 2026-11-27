---
name: conditionnement-capteur
description: Analyser ou concevoir le conditionnement d'un capteur dans Silx - pont de Wheatstone et jauges de contrainte, sondes Pt100 et Pt1000 (2, 3 ou 4 fils), thermistances CTN, thermocouples (soudure froide), capteurs résistifs ou capacitifs, ampli d'instrumentation, excitation ratiométrique, linéarité, dérive, auto-échauffement.
---

# Conditionnement de capteur

## 1. Modéliser le capteur dans Silx

Silx n'a pas encore de bibliothèque de capteurs. Équivalents électriques avec les composants de base :
- **résistif** (Pt100, CTN, jauge, potentiomètre) : une résistance dont on **balaye la valeur** sur l'étendue de mesure
  (`run_campaign`, `mode = balayage`, `cible` = la résistance du capteur). La loi résistance ↔ grandeur physique
  (Callendar-Van Dusen pour une Pt100 : R0 = 100 Ω, environ 0,385 Ω/°C ; loi bêta d'une CTN) se fait évaluer par
  un outil de calcul, ou est fournie par l'utilisateur ou la datasheet ;
- **thermocouple** : source de tension continue de quelques dizaines de µV/°C (type K environ 41 µV/°C) en série avec
  la résistance des fils ; la soudure froide est une seconde source à la température du bornier ;
- **capacitif** : condensateur balayé, avec l'excitation alternative du circuit de lecture ;
- **pont de Wheatstone** : quatre résistances, dont une, deux ou quatre actives.

## 2. Vérifier la chaîne

- **Point de fonctionnement** (`get_operating_point`) : tensions de mode commun en entrée de l'ampli d'instrumentation
  dans sa plage ; courant dans le capteur.
- **Auto-échauffement** : puissance dissipée dans le capteur (courant² × R) ; une Pt100 se lit d'ordinaire avec
  ≤ 1 mA. Comparez au coefficient d'auto-échauffement de la datasheet.
- **Fonction de transfert** : balayage du capteur sur l'étendue de mesure, tension de sortie à chaque point ;
  l'excursion doit couvrir la pleine échelle de l'étage suivant (ADC : Skill `chaine-acquisition-adc`) avec une
  marge.
- **Linéarité** : écart à la droite qui joint les extrémités (la mesure de linéarité n'existe pas encore : présentez
  les points simulés et l'écart lu, et faites calculer l'écart par outil quand il existe). Un pont quart a une
  non-linéarité propre, en plus de celle du capteur.
- **Excitation ratiométrique** : si la même référence alimente le pont et l'ADC, la dérive de la référence s'annule.
  Vérifiez-le en balayant la tension de référence.
- **Mode commun et CMRR** : Skill `bonnes-pratiques-instrumentation` (désappariement des résistances en Monte-Carlo).
- **Dérive en température** de l'électronique (pas du capteur) : balayage de `TEMP` avec le capteur figé.
- **Câblage** : résistance des fils ajoutée au schéma (2 fils : elle s'ajoute à la mesure ; 3 fils : compensée si
  les fils sont égaux ; 4 fils : éliminée). Balayez-la pour montrer l'erreur.

## 3. Thermocouples : erreurs classiques

- Compensation de soudure froide absente ou à la mauvaise température.
- Entrées de l'ampli sans chemin pour leurs courants de polarisation (thermocouple flottant) : ajoutez une
  résistance vers la référence ou la masse.
- Thermocouples parasites aux jonctions de métaux différents (bornier, connecteur) : non simulable, liste de contrôle.

## Conclure

Modèle du capteur retenu, fonction de transfert simulée sur l'étendue, linéarité, sensibilités (référence,
température, câblage, tolérances), erreurs non simulées à surveiller. Pour le budget d'erreur global, enchaînez avec
`incertitudes-et-budget-d-erreur`.
