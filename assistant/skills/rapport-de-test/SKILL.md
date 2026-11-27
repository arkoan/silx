---
name: rapport-de-test
description: Rédiger un rapport de test ou une synthèse de validation vérifiable à partir des résultats Silx - exigences, bancs et conditions, résultats nominaux et de campagne (taux de conformité, Cpk, pire cas), écarts, réserves. À utiliser pour « fais le rapport », « synthèse pour la revue », « compte rendu de simulation », en fin de campagne.
---

# Rapport de test

Règle : **aucun nombre du rapport n'est absent des résultats des outils**. Si un résultat manque, on lance l'outil
ou on écrit « non simulé ».

## Collecter

1. `get_test_summary` : verdict global et par banc. Un banc « non simulé » est soit simulé maintenant
   (`run_simulation`, avec l'accord implicite de la demande), soit listé comme non vérifié.
2. `list_testbenches` : conditions de chaque banc (analyse, plage, stimuli, charges, critères et bornes, gabarits,
   campagne).
3. `describe_project` : liste des composants, modèles utilisés, tolérances.
4. Pour chaque campagne : `run_campaign` si elle n'a pas été lancée dans la session, puis `get_worst_case`.

## Structure

1. **Objet** : circuit, version du projet (fichier, date), but du rapport.
2. **Synthèse** : verdict global en une phrase, contraintes en échec ou à faible marge.
3. **Exigences et bancs** : tableau exigence → banc → critère → bornes.
4. **Résultats nominaux** : par banc, valeur mesurée, bornes, marge, verdict.
5. **Dispersion** : type de campagne, réglages (tirages, loi, graine, tolérances), taux de conformité, moyenne, σ,
   Cpk par critère, pire cas et composants en cause.
6. **Écarts et décisions** : échecs, analyse de cause (si faite), corrections appliquées et leur vérification.
7. **Réserves** : modèles indicatifs, paramètres de modèle non dispersés, plage de température non couverte,
   exigences sans banc, vérifications hors schéma.
8. **Annexes** : références des modèles et documents utilisés.

## Rédaction

- Phrases courtes, au présent, sans adjectifs vagues (« bonne marge » → la marge chiffrée).
- Les nombres en écriture ingénieur avec unité, tels que renvoyés par les outils.
- Distinguer clairement résultat simulé, donnée sourcée (document cité) et appréciation de l'auteur.
- Les courbes et les exports PDF, SVG, PNG et CSV se font depuis l'application (bouton « Exporter », menu
  Fichier) : indiquez à l'utilisateur lesquels joindre. Le serveur MCP ne produit pas encore le rapport lui-même.

## Conclure

Livrez le rapport en Markdown, puis listez ce qui a été simulé pendant sa rédaction et ce qui reste non vérifié.
