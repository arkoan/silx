---
name: analyser-bruit
description: Analyser le bruit électronique d'un circuit dans Silx (analyse .noise de ngspice) - densité spectrale en V/√Hz ou nV/√Hz, bruit en sortie et ramené à l'entrée, bruit total intégré, rapport signal sur bruit, contributeurs dominants, choix d'un ampli faible bruit. À utiliser pour toute question de bruit, de SNR ou de plancher de bruit.
---

# Analyser le bruit

## Préalables

- Le bruit se calcule sur un **banc fréquentiel** (stimulus petit signal AC). L'entrée de référence est la source
  petit signal du banc : le bruit « ramené à l'entrée » est rapporté à cette source.
- Le point de fonctionnement doit être bon (`verifier-polarisation`) : le bruit d'un transistor dépend de son
  courant de repos.
- La plage du banc fixe la bande d'intégration du bruit total : elle doit correspondre à la bande utile du système,
  pas à une plage arbitraire.

## Mesurer

`get_noise` (nœud observé, banc) renvoie : densité en sortie et ramenée à l'entrée sur la plage, bruit total intégré
en sortie et à l'entrée (V efficaces), densité à 1 kHz. Si votre version du serveur ne propose pas l'outil, les
critères de bruit d'un banc (bruit total en sortie, ramené à l'entrée, densité à 1 kHz) se lisent par
`run_simulation`.

## Interpréter

- **Forme du spectre** : plateau = bruit blanc (thermique, grenaille) ; pente en 1/f aux basses fréquences ;
  bosse = résonance ou réduction de la contre-réaction.
- **Références physiques** pour contrôler l'ordre de grandeur : bruit thermique d'une résistance √(4kTR)
  (environ 4 nV/√Hz pour 1 kΩ à température ambiante, et proportionnel à √R), bruit kT/C d'un condensateur
  échantillonné. Faites évaluer toute formule par un outil de calcul quand il existe ; sinon donnez seulement l'ordre
  de grandeur, étiqueté comme estimation.
- **Bruit ramené à l'entrée** : c'est lui qu'on compare au signal d'entrée pour le SNR, et aux bruits des sources
  (capteur, résistance de source).
- **SNR** = signal efficace / bruit efficace sur la même bande, en dB. Précisez la bande et la pondération
  éventuelle (audio : Skill `mesures-audio`).

## Trouver le contributeur dominant

Le serveur ne donne pas encore la contribution de chaque composant. Méthode de remplacement :
- par le raisonnement (résistances en entrée et dans la contre-réaction, premier étage actif), annoncé comme tel ;
- par l'expérience : baisser une résistance suspecte (`set_component_value`) et relancer `get_noise` ; remettez la
  valeur ensuite.

## Réduire le bruit (pistes, chacune à vérifier par simulation)

- Impédances plus faibles autour du premier étage (au prix du courant et de la charge).
- Gain dans le premier étage, pour que les étages suivants comptent moins.
- Ampli à faible bruit de tension (source basse impédance) ou de courant (source haute impédance) selon la source.
- Limiter la bande au besoin (filtrage).

## Conclure

Nœud, bande d'intégration, densité au point d'intérêt, bruit total en sortie et ramené à l'entrée (valeurs de
l'outil), SNR si le signal est connu, contributeur dominant (avec le niveau de preuve), action recommandée.
