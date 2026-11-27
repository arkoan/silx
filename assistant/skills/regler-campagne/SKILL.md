---
name: regler-campagne
description: Choisir, régler, lancer et interpréter une campagne Silx (balayage paramétrique, Monte-Carlo dans les tolérances, coins ou pire cas, balayage de température). À utiliser pour « est-ce que ça tient avec des composants réels ? », dispersion, rendement, taux de conformité, Cpk, pire cas, tenue en température, ou un banc qui passe en nominal avec peu de marge.
---

# Régler et interpréter une campagne

Question à laquelle répond une campagne : **le circuit reste-t-il conforme avec des composants réels ?**

## Choisir le type

| Question | Campagne |
|---|---|
| Comment la mesure évolue avec un paramètre ? Où est la limite ? | **Balayage** (R, C, L, source continue, ou `TEMP`) |
| Quelle proportion de cartes sera conforme ? | **Monte-Carlo** |
| Le pire assemblage possible passe-t-il ? | **Coins** (avec température si demandé) |
| Tenue de −40 à 85 °C ? | Balayage de `TEMP`, ou coins avec `temperature = true` |

## Avant de lancer

- **Tolérances** (`describe_project`) : une tolérance nulle rend la campagne muette sur ce composant. Valeurs
  usuelles à proposer, à confirmer par l'utilisateur ou la datasheet : résistances 1 % ou 5 %, céramiques C0G 5 %,
  X7R 10 %, électrolytiques 20 %, inductances 10 à 20 %. `set_component_value` règle une tolérance.
- **Monte-Carlo** : loi gaussienne (± tolérance = 3 σ) pour des composants triés par le fabricant, uniforme pour
  rester prudent ; **graine fixe** pour pouvoir rejouer. Nombre de tirages : 100 par défaut ; pour affirmer un taux
  de conformité proche de 100 %, il en faut davantage (avec 0 échec sur N tirages, le taux d'échec est seulement
  borné vers 3/N à 95 % de confiance).
- **Coins** : 2ⁿ combinaisons pour n composants dispersés ; le coût grimpe vite, commencez par les composants
  influents (Skill `analyser-sensibilite`).
- Les réglages passés à `run_campaign` ne valent que pour ce lancement ; la campagne enregistrée du banc ne change
  pas.

## Interpréter

- **Taux de conformité** : avec son nombre de tirages ; 97 % sur 100 tirages n'est pas 97 % garanti.
- **Cpk** par mesure : < 1 → des échecs sont attendus ; ≥ 1,33 est l'objectif industriel courant. Un Cpk faible
  avec une moyenne centrée appelle à réduire la dispersion (composants plus précis) ; une moyenne décentrée appelle
  à recentrer le nominal (Skill `optimiser-un-design` quand elle sera disponible, sinon correction manuelle vérifiée).
- **Pire cas** (`get_worst_case`) : valeurs des composants du tirage le plus défavorable ; c'est le point d'entrée du
  diagnostic (Skill `diagnostiquer-banc-en-echec`).
- **Coins et Monte-Carlo** ne disent pas la même chose : les coins bornent, le Monte-Carlo estime une proportion. Un
  coin en échec n'implique pas un rendement faible si cette combinaison est improbable ; dites-le.

## Ce qu'une campagne ne couvre pas

- Les paramètres internes des modèles (β d'un transistor, Vth d'un MOSFET) ne sont pas dispersés aujourd'hui ;
  un balayage de température sur un modèle sans paramètres thermiques calibrés est optimiste.
- Le vieillissement, l'humidité, les parasites du PCB.
Signalez-le dans la conclusion.

## Conclure

Type de campagne et réglages (tirages, loi, graine, tolérances), taux de conformité, mesures critiques avec moyenne,
σ et Cpk, pire cas et composants en cause, limites de l'exercice, recommandation.
