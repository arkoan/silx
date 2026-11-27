---
name: diagnostiquer-banc-en-echec
description: Trouver pourquoi un banc de test Silx est en FAIL (simulation nominale ou campagne Monte-Carlo, coins, balayage) et proposer une correction vérifiée par simulation. À utiliser pour « pourquoi ça ne passe pas ? », « que faut-il changer ? », un critère ou un gabarit non respecté, un taux de conformité trop bas.
---

# Diagnostiquer un banc en échec

Principe : un FAIL peut venir du **circuit**, du **banc** (il mesure mal) ou du **modèle**. On élimine dans cet
ordre : banc, point de fonctionnement, cause physique. Aucune correction n'est présentée sans re-simulation.

## 1. Lire l'échec

- `get_test_summary` : quels bancs, quelles contraintes, de combien (marge négative).
- `list_testbenches` : définition exacte du critère (mesure, signal, bornes, plage) et du banc (stimuli, analyse,
  durée, plage de fréquences).
- Campagne : `get_worst_case` (et les N pires tirages) pour voir **quels composants** sont aux extrêmes dans les
  tirages en échec.

## 2. Le banc mesure-t-il ce qu'il faut ?

Erreurs fréquentes, à vérifier avant le circuit :
- mauvais signal sondé (entrée au lieu de sortie, nœud interne) ;
- durée trop courte : régime établi non atteint, mesure sur le démarrage ;
- plage AC qui ne contient pas la coupure ou la résonance ;
- stimulus inadapté (amplitude qui sature l'étage, front trop lent pour un temps de montée) ;
- bornes incohérentes avec l'exigence (unité, dB contre rapport, signe) ;
- charge absente ou irréaliste.

Regardez la courbe : `get_raw_curve` sur le signal du critère, sur la plage utile.

## 3. Le point de fonctionnement

Pour tout circuit actif : `get_operating_point` (voir `verifier-polarisation`). Un transistor saturé ou bloqué
explique la plupart des gains effondrés, des distorsions et des oscillations absentes.

## 4. Trouver la cause

- Formulez une hypothèse physique (« le pôle de C3 avec R5 est trop bas »), puis testez-la.
- Influence d'un composant : petit balayage avec `run_campaign` (`mode = balayage`, `cible = R5`, de part et
  d'autre de la valeur). Sans `get_sensitivity`, c'est la méthode de la Skill `analyser-sensibilite`.
- En campagne : un composant systématiquement extrême dans les pires tirages est un suspect ; confirmez par un
  balayage de ce seul composant.

## 5. Corriger et vérifier

1. Proposez une modification chiffrée (valeur normalisée, série E), avec sa justification mesurée.
2. Essai : `set_component_value`, puis `run_simulation` sur **tous** les bancs (une correction peut en casser un
   autre). Si la campagne était en échec, relancez-la avec la même graine.
3. Présentez avant / après : valeur, marge, verdict, pour chaque contrainte touchée.
4. Si l'essai n'est pas retenu, remettez la valeur d'origine (`set_component_value`) et dites-le.
5. N'enregistrez (`save_project`) qu'à la demande de l'utilisateur.

Si l'exigence est inatteignable avec la topologie actuelle, dites-le avec la preuve (balayage montrant la limite)
plutôt que de proposer une suite de retouches.

## Conclure

Cause (avec sa preuve), correction (avec avant / après simulés), effets sur les autres bancs, risques restants
(marge faible, modèle indicatif, paramètre non dispersé).
