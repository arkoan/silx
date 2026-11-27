---
name: revue-de-schema
description: Faire une revue de conception (design review) d'un schéma Silx avec la liste de contrôle d'un bureau d'étude - ERC, contraintes appliquées aux composants, valeurs normalisées, découplage, entrées flottantes, modèles indicatifs, exigences sans banc de test. À utiliser pour « relis mon schéma », « revue de conception », avant de figer un design ou d'écrire un rapport.
---

# Revue de schéma

Chaque constat porte : **gravité** (bloquant, à corriger, remarque), **composant ou nœud**, **preuve** (résultat
d'outil, ou jugement annoncé comme tel), **action proposée**.

## Liste de contrôle

### 1. Vérifications de Silx
`describe_project` : ERC (nœuds flottants, broches non connectées, références dupliquées). Tout constat d'ERC est
au moins « à corriger ».

### 2. Point de fonctionnement et contraintes
`get_operating_point` puis, pour chaque composant actif ou de puissance : tension, courant, puissance comparés aux
limites du composant. Silx ne connaît pas encore ces limites : demandez-les, ou citez la datasheet si elle est
disponible (sinon, marquez « limites non vérifiées »). Règle de bureau d'étude courante, à confirmer avec
l'utilisateur : rester sous 50 à 80 % des valeurs maximales absolues (déclassement). Pensez aux condensateurs
(tension de service, polarisation inverse d'un électrolytique) et aux résistances (puissance).

### 3. Pire cas
Si une campagne existe : `run_campaign` en coins ou Monte-Carlo et regardez les contraintes en pire cas, pas
seulement au nominal.

### 4. Valeurs
- Valeurs hors séries normalisées (E12, E24, E96) : remarque, avec la valeur normalisée proche, vérifiée par
  simulation si elle change un résultat.
- Tolérances nulles ou irréalistes dans le projet : la campagne ne prouve rien sur ces composants.

### 5. Pratiques de schéma
- Découplage de chaque alimentation de circuit actif (présent dans le schéma ? sinon remarque).
- Entrées logiques ou de comparateur jamais flottantes ; hystérésis sur un comparateur qui reçoit un signal lent ou
  bruité.
- Chemins DC pour chaque base, grille et entrée d'ampli-op.
- Protection des entrées exposées, courant d'appel des gros condensateurs.
- **Modèles indicatifs** de la palette encore utilisés pour des composants critiques : remarque : importer le
  modèle du fabricant.

### 6. Couverture des exigences
`list_testbenches` : chaque exigence connue a-t-elle un banc et un critère ? Listez les manques (par exemple : pas
de banc en température, pas de test de démarrage, pas de pire cas).

### 7. Hors schéma
Ce qui ne se vérifie pas dans Silx (placement, routage, thermique de la carte, CEM) : liste de contrôle pour la
phase PCB, sans verdict.

## Conclure

Tableau des constats triés par gravité, puis la liste des vérifications non faites et pourquoi (limite d'outil,
information manquante).
