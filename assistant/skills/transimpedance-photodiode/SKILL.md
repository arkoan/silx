---
name: transimpedance-photodiode
description: Analyser ou concevoir un amplificateur transimpédance (TIA) dans Silx - photodiode en mode photoconducteur ou photovoltaïque, choix de la résistance et de la capacité de contre-réaction, stabilité (le TIA qui oscille), bande passante, bruit d'un récepteur optique, courant d'obscurité.
---

# Ampli transimpédance et photodiode

## Modéliser la photodiode

Avec les composants de base : **source de courant** (photocourant : continu, échelon ou petit signal AC) en
parallèle avec la **capacité de jonction** Cj et la **résistance de shunt** Rsh, plus éventuellement le courant
d'obscurité (source continue). Valeurs de Cj (qui dépend de la tension inverse), Rsh et courant d'obscurité : datasheet
ou utilisateur.

- **Photovoltaïque** (0 V aux bornes) : courant d'obscurité minimal, faible bruit, mais Cj maximale → bande plus
  étroite. Précision en basse fréquence.
- **Photoconducteur** (polarisation inverse) : Cj réduite → bande plus large ; le courant d'obscurité augmente.

## Dimensionner (avec vérification)

- **Rf** : gain en V/A, fixé par le courant maximal et l'excursion de sortie.
- **Cf** : indispensable pour la stabilité. La capacité d'entrée totale Ci (Cj + capacités d'entrée de l'ampli) crée
  avec Rf un zéro dans le gain de bruit ; sans Cf, le TIA oscille ou sonne. Formule classique pour une marge de phase
  d'environ 45° : Cf ≈ √(Ci / (2π·Rf·GBW)). Bande résultante de l'ordre de √(GBW / (2π·Rf·Ci)). Faites évaluer ces
  formules par un outil de calcul quand il existe ; sinon annoncez-les comme estimations et ajustez par simulation.
- **Vérification** :
  - échelon de photocourant en transitoire : dépassement et sonnerie (temps d'établissement du catalogue) ;
  - `run_campaign` en balayage de Cf autour de l'estimation, pour choisir la valeur normalisée ;
  - Bode du gain transimpédance (source de courant AC) : bande à −3 dB et pic éventuel.
- Le gain de boucle direct (marge de phase exacte) attend un modèle de banc « gain de boucle » dans
  Silx ; d'ici là, le dépassement à l'échelon et le pic du Bode sont les indicateurs.

## Bruit

`get_noise` en sortie (Skill `analyser-bruit`). Contributions attendues :
- bruit thermique de Rf (courant √(4kT/Rf) : plus Rf est grand, moins il pèse ramené à l'entrée) ;
- bruit de courant de l'ampli (choisir un ampli à entrée JFET ou CMOS pour un fort Rf) ;
- bruit de tension de l'ampli multiplié par le gain de bruit, qui **monte avec la fréquence** à cause de Ci : il
  domine souvent en haut de bande ;
- bruit de grenaille du photocourant et du courant d'obscurité.

## Pièges

- Les courants de fuite de la carte et la capacité parasite de Rf (souvent de l'ordre de 0,1 à 0,2 pF pour un CMS)
  comptent avec des Rf de plusieurs MΩ : ajoutez-les au schéma.
- Un ampli-op modélisé de façon idéale cache le problème de stabilité : utilisez un modèle avec GBW réaliste.

## Conclure

Mode de la photodiode, Rf et Cf (normalisés), réponse indicielle et bande simulées, bruit total ramené à l'entrée
(courant équivalent), limites du modèle.
