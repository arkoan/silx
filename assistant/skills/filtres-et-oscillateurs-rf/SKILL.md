---
name: filtres-et-oscillateurs-rf
description: Analyser ou concevoir des filtres et oscillateurs RF dans Silx - filtres LC, à quartz ou céramiques (pertes d'insertion, ondulation, réjection, Q chargé), oscillateurs RF et VCO (varicap, plage d'accord, démarrage), mélangeurs (gain de conversion, isolation). À utiliser pour les circuits sélectifs et les sources RF.
---

# Filtres et oscillateurs RF

## Filtres

- **Banc** : AC, source et charge aux impédances de terminaison prévues (un filtre à quartz ou LC n'a la bonne
  réponse que sur ses terminaisons). La démo « Filtre à quartz 4 MHz » de Silx en est un exemple.
- **Grandeurs** : pertes d'insertion au centre de la bande (par rapport à la référence −6,02 dB d'une source et d'une
  charge égales), ondulation dans la bande, largeur de bande à −3 dB, réjection aux fréquences imposées, forme des
  flancs. Le catalogue (`list_measurements`) a le gain maximal et la largeur de bande autour du maximum.
- **Résonances étroites** : l'échantillonnage adaptatif AC de Silx les trouve sans réglage (il resserre les points là
  où le gain varie vite) ; ne réduisez pas le nombre de points à la main.
- **Q chargé** = fréquence centrale / bande ; il dépend des terminaisons, des pertes des inductances et des modèles
  de quartz (résistance motionnelle).
- **Tolérances** : un filtre étroit est très sensible ; balayez les éléments d'accord et faites un Monte-Carlo
  (`regler-campagne`).

## Oscillateurs et VCO

- **Démarrage** : le banc doit contenir une impulsion de démarrage (comme la démo « Oscillateur Colpitts ») et durer
  assez longtemps pour que l'amplitude se stabilise ; fréquence et crête à crête se mesurent en régime établi (dernier
  quart). Un oscillateur qui ne démarre pas en simulation manque de gain de boucle à la fréquence d'oscillation.
- **Plage d'accord d'un VCO** : `run_campaign` en balayage de la tension continue de commande, mesure de fréquence en
  régime établi à chaque point ; vérifiez que l'oscillation démarre aux deux bouts de la plage.
- **Pureté** : ngspice n'a ni équilibrage harmonique ni analyse de bruit de phase. Le bruit de phase n'est pas
  simulable dans Silx : dites-le, et donnez seulement les facteurs qualitatifs (Q du résonateur, niveau, bruit du
  transistor).

## Mélangeurs

Gain de conversion et isolation par transitoire (signaux RF et OL, lecture de la raie à la fréquence intermédiaire).
La lecture des raies attend `get_spectrum` ; sans lui, filtrez la FI dans le banc et mesurez son amplitude en régime
établi.

## Conclure

Grandeurs simulées (pertes, bande, réjection, plage d'accord), sensibilité aux tolérances, ce qui n'est pas
simulable (bruit de phase, parasites du PCB).
