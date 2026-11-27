---
name: bonnes-pratiques-instrumentation
description: Règles de l'art et erreurs classiques en instrumentation électronique, appliquées à un schéma Silx - masse et boucles de masse, mode commun et CMRR, filtrage RFI d'un ampli d'instrumentation, garde et fuites en haute impédance, mesure 4 fils (Kelvin), retour des courants de polarisation, f.é.m. thermoélectriques, blindage, isolation, référence et découplage.
---

# Bonnes pratiques d'instrumentation

Pour chaque règle : ce qu'elle évite, si Silx peut la **vérifier par simulation**, et sinon qu'elle va dans la liste
de contrôle (PCB, câblage).

## Règles vérifiables dans Silx

| Règle | Erreur évitée | Comment la vérifier |
|---|---|---|
| **Mode commun dans la plage** de l'ampli d'instrumentation, à toutes les tensions d'entrée | Saturation interne invisible en sortie (sortie fausse mais plausible) | `get_operating_point` et balayage du mode commun (`run_campaign` sur une source continue) |
| **CMRR** : l'appariement des résistances fixe le rejet d'un ampli d'instrumentation discret ou d'un ampli différentiel | Le mode commun (50 Hz, bruit de masse) passe en différentiel | Banc AC avec la même source sur les deux entrées ; Monte-Carlo des résistances : le pire tirage donne le CMRR réel |
| **Filtre RFI d'entrée** équilibré (R et C de mode commun appariés, C différentiel bien plus grand) | Un déséquilibre convertit le mode commun haute fréquence en différentiel | Même banc de mode commun, en AC jusqu'aux hautes fréquences ; Monte-Carlo des C de mode commun |
| **Retour des courants de polarisation** : chaque entrée a un chemin DC vers la référence | Source flottante (thermocouple, transformateur, couplage capacitif) : l'entrée dérive jusqu'au rail | ERC et point de fonctionnement ; sans résistance de retour, la sortie sature |
| **Mesure 4 fils** (Kelvin) pour une faible résistance ou un shunt de courant | La résistance des fils et des contacts s'ajoute à la mesure | Résistances de fils ajoutées au schéma, balayées : l'erreur disparaît en 4 fils |
| **Excitation ratiométrique** | Dérive de la référence dans le résultat | Balayage de la référence (Skill `conditionnement-capteur`) |
| **Découplage et bruit d'alimentation** | Bruit d'alimentation dans la mesure (PSRR fini) | Petit signal AC sur l'alimentation, lecture en sortie |
| **Garde** en haute impédance | Courants de fuite de la carte dans l'entrée | Fuite modélisée par une résistance (par exemple 1 à 100 GΩ) vers un rail, avec et sans anneau de garde au potentiel de l'entrée |

## Règles hors schéma (liste de contrôle)

- **Masse** : un seul point de référence pour la mesure ; pas de courant de puissance dans la masse de mesure ;
  boucles de masse évitées (mesure différentielle, isolation).
- **Blindage** : relié à un seul bout en basse fréquence ; paires torsadées pour les signaux faibles.
- **F.é.m. thermoélectriques** : chaque jonction de métaux différents (bornier, soudure, relais) est un thermocouple
  parasite de quelques µV/°C ; garder les jonctions d'une même paire à la même température.
- **Fuites** : propreté de la carte, vernis, anneaux de garde autour des entrées haute impédance.
- **Isolation** quand la référence de la mesure est à un potentiel élevé ou bruité.
- **Auto-échauffement** des résistances de précision et des shunts : dérive au rythme de la puissance.

## Démarche

1. Identifiez dans le schéma (`describe_project`) les entrées de mesure, la référence, les alimentations.
2. Passez la liste : pour chaque règle vérifiable, faites la simulation proposée, ou décrivez à l'utilisateur
   l'élément à ajouter dans l'éditeur (résistance de fil, de fuite) quand le serveur ne peut pas encore modifier le
   circuit.
3. Rapportez : règle, état (respectée, violée, non vérifiée), preuve, correction.

## Conclure

Tableau des règles avec leur état et leur preuve, puis la liste de contrôle hors schéma.
