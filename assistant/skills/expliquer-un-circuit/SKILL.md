---
name: expliquer-un-circuit
description: Expliquer le fonctionnement d'un schéma ouvert dans Silx - reconnaître la topologie (étages, contre-réactions, filtres, polarisation, oscillateur, logique), le rôle de chaque composant, et appuyer l'explication sur des simulations. À utiliser pour « explique-moi ce montage », « à quoi sert R4 ? », la reprise d'un schéma inconnu ou la préparation d'une revue.
---

# Expliquer un circuit

## Lire le schéma réel

1. `describe_project` : composants, valeurs, modèles et **nœuds de chaque broche**. Raisonnez sur les connexions,
   pas sur les noms (un nœud appelé `OUT` peut ne pas être la sortie).
2. `get_netlist` si un doute subsiste sur une connexion ou un modèle.
3. `list_testbenches` : les bancs disent souvent ce que l'auteur voulait vérifier (entrée, sortie, exigences).

## Reconnaître la structure

Découpez en blocs et nommez-les : alimentation et découplage, polarisation, étage d'entrée, étage de gain, étage de
sortie, contre-réaction, filtre, oscillateur (réseau sélectif + gain), interface logique (ponts XSPICE), protection.
Pour chaque bloc : fonction, composants concernés, grandeur qui le caractérise (gain, fréquence de coupure,
fréquence d'oscillation, seuil).

## Appuyer sur la simulation

Les ordres de grandeur supposés sont des hypothèses ; montrez-les :
- polarisation : `get_operating_point` (Skill `verifier-polarisation`) ;
- comportement fréquentiel : banc Bode existant, `run_simulation` puis `get_raw_curve` ;
- comportement temporel : banc transitoire existant ;
- rôle d'un composant : changez-le franchement (`set_component_value`, par exemple ×10 ou ÷10), resimulez,
  montrez l'effet, puis **remettez la valeur d'origine**. Annoncez l'essai avant de le faire (en mode direct,
  l'utilisateur le voit à l'écran).
S'il n'existe pas de banc adapté, dites lequel serait utile ; sa création attend les outils d'écriture du serveur.

## Adapter au lecteur

L'utilisateur est ingénieur : allez à l'essentiel (topologie, choix de conception, points faibles) ; détaillez la
physique seulement si on vous le demande.

## Conclure

Une phrase sur la fonction du circuit, puis les blocs, puis le rôle des composants qui ne sont pas évidents, puis les
points d'attention relevés (polarisation fragile, composant surdimensionné ou sous-dimensionné, modèle indicatif),
chacun avec sa preuve simulée ou marqué comme jugement.
