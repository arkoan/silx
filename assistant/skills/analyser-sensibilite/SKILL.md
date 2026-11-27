---
name: analyser-sensibilite
description: Classer les composants d'un circuit Silx selon leur influence sur une mesure (sensibilité), pour savoir sur quoi jouer, quels composants doivent être précis et lesquels peuvent être élargis. À utiliser pour « quel composant influence le plus… », « sur quoi jouer ? », avant une optimisation ou pour expliquer une dispersion.
---

# Analyser la sensibilité

## Définition utile

Sensibilité relative d'une mesure M à un composant x : variation relative de M pour une variation relative de x
(S = (ΔM/M) / (Δx/x)). Ce qui compte en pratique est l'**effet de la tolérance** : S × tolérance de x.

## Méthode (sans outil dédié)

Le serveur n'a pas encore `get_sensitivity`. On procède par petits balayages :

1. Choisir la mesure (un critère du banc) et les composants candidats : `describe_project`, `list_testbenches`.
   Commencez par ceux du chemin du signal et de la contre-réaction ; ignorez les découplages évidents (annoncez-le).
2. Pour chaque candidat : `run_campaign` avec `mode = balayage`, `cible = <réf>`, liste de trois valeurs
   (valeur − tolérance, nominale, + tolérance ; ou ±5 % si la tolérance est nulle). Les statistiques du résultat
   donnent le minimum et le maximum de chaque mesure sur le balayage ; `get_worst_case` avec un nombre de tirages
   donne chaque point. La mesure doit être un critère du banc pour figurer dans les statistiques.
3. Classez les composants par l'écart de la mesure sur leur plage. Si un outil de calcul est disponible, faites-lui
   calculer S ; sinon présentez les écarts mesurés eux-mêmes, qui suffisent pour le classement.
4. Contrôle : une campagne Monte-Carlo (`regler-campagne`) dont le pire cas met en avant les mêmes composants
   confirme le classement.

Coût : trois simulations par composant ; pour un gros circuit, limitez-vous aux dix candidats les plus probables
et dites-le.

## Lire le résultat

- Composants **critiques** : fort effet → tolérance serrée, ou topologie qui les rend moins influents.
- Composants **peu influents** : leur tolérance peut être élargie (gain de coût), à confirmer par Monte-Carlo.
- Un effet non monotone (la mesure passe par un extremum) signale un réglage optimal ou un changement de régime :
  regardez la courbe et le point de fonctionnement.

## Conclure

Tableau composant → plage balayée → mesure min et max (valeurs simulées) → rang, puis recommandations (composants à
resserrer, à élargir, à ajuster), chacune à vérifier par campagne.
