---
name: chaine-acquisition-adc
description: Analyser ou concevoir l'étage d'entrée d'un convertisseur analogique-numérique dans Silx - adaptation à la pleine échelle, filtre anti-repliement, pilotage d'un ADC SAR (charge de la capacité d'échantillonnage, établissement à ½ LSB), référence de tension, budget d'erreur en LSB, SNR et ENOB attendus.
---

# Chaîne d'acquisition vers un ADC

## 1. Adapter l'excursion

- Le signal doit occuper la pleine échelle de l'ADC (moins une marge pour les tolérances) : gain et décalage de
  l'étage d'attaque ; vérifiez par balayage de l'entrée continue (banc Transfert DC) les deux extrémités et
  l'écrêtage de l'ampli (excursion de sortie près des rails).
- Entrée différentielle : tension de mode commun dans la plage spécifiée de l'ADC.

## 2. Anti-repliement

- Tout signal ou bruit au-dessus de fe/2 se replie dans la bande. Atténuation visée à fe/2 (ou à fe − fmax) de
  l'ordre de la dynamique utile : un ADC idéal de N bits a un SNR de 6,02·N + 1,76 dB.
- Ordre et fréquence de coupure : voir le dimensionnement de filtre ; vérifiez par Bode l'atténuation simulée à fe/2
  et l'erreur de gain dans la bande utile.
- Le suréchantillonnage relâche le filtre analogique (filtrage numérique ensuite) : à proposer.

## 3. Piloter un ADC SAR

- À chaque acquisition, la capacité d'échantillonnage interne se connecte à l'entrée et prend une charge (« kickback »).
  On place un **condensateur réservoir** au plus près de l'entrée (souvent beaucoup plus grand que la capacité
  d'échantillonnage), avec une résistance série qui isole l'ampli.
- L'entrée doit **s'établir à ½ LSB** pendant le temps d'acquisition : il faut environ ln(2^(N+1)) constantes de
  temps (environ 9 τ pour 12 bits, 12 τ pour 16 bits).
- Silx n'a pas encore de modèle d'entrée d'ADC : modélisez-la par une capacité commutée (interrupteur commandé par une
  source impulsionnelle, capacité de la datasheet, condensateur déchargé à chaque cycle) si l'utilisateur accepte de
  la dessiner ; mesurez le temps d'établissement (catalogue) de la tension d'entrée après chaque connexion.
- L'ampli doit rester stable avec le condensateur réservoir en charge : échelon et dépassement.

## 4. Référence

Bruit et dérive de la référence passent directement dans le résultat (erreur de gain) : découplage selon la
datasheet, et mesure ratiométrique quand c'est possible (Skill `conditionnement-capteur`).

## 5. Budget en LSB

Rassemblez, en LSB ou en ppm de la pleine échelle : décalage et gain de l'étage (Monte-Carlo, température), bruit
intégré sur la bande (`get_noise`), erreur d'établissement, erreurs de l'ADC (datasheet : INL, DNL, bruit de
transition). La combinaison se fait avec la Skill `incertitudes-et-budget-d-erreur`. ENOB = (SINAD − 1,76) / 6,02 ;
son évaluation demande les raies du spectre (`get_spectrum`, manquant) ou le calcul par outil à partir du bruit et de
la distorsion simulés.

## Conclure

Excursion et marges, atténuation à fe/2, établissement simulé, budget d'erreur en LSB, ce qui vient de la datasheet
et ce qui a été simulé.
