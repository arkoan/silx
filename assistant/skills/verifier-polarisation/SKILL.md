---
name: verifier-polarisation
description: Vérifier le point de fonctionnement continu (point de repos, polarisation) d'un circuit Silx - régime de chaque transistor bipolaire ou MOSFET (bloqué, actif, saturé, triode), marges d'excursion, dissipation, stabilité vis-à-vis de la température et des tolérances. À utiliser avant toute analyse petit signal d'un étage actif, ou quand un gain, une excursion ou une oscillation sont anormaux.
---

# Vérifier la polarisation

## Obtenir le point de fonctionnement

`get_operating_point` (circuit seul, ou avec un banc : ses sources à leur valeur continue) donne la tension de chaque
nœud et le courant de chaque composant (entrant par la broche 1 ; collecteur ou drain pour un transistor).
`describe_project` donne les nœuds de chaque broche. Si l'outil n'est pas proposé par votre version du serveur,
dites-le : un banc « Transfert DC » ou une simulation transitoire courte lue à t = 0 sont des substituts imparfaits.

## Régime de chaque transistor

Relevez les tensions des broches dans le résultat de l'outil, puis classez (les différences de tension sont lues sur
les valeurs de l'outil ; pour un calcul, utilisez un outil de calcul s'il existe) :

| Composant | Bloqué | Actif / saturation (MOS) | Saturé / triode (MOS) |
|---|---|---|---|
| NPN | VBE nettement < 0,5 V, IC ≈ 0 | VBE ≈ 0,6–0,75 V et VCE > ~0,3 V | VCE ≲ 0,2 V |
| PNP | idem, signes inversés | VEB ≈ 0,6–0,75 V et VEC > ~0,3 V | VEC ≲ 0,2 V |
| NMOS | VGS < Vth | VGS > Vth et VDS > VGS − Vth | VDS < VGS − Vth |
| PMOS | idem, signes inversés | | |

Un amplificateur linéaire veut ses transistors en **actif** (BJT) ou en **saturation** (MOS) ; un interrupteur les
veut bloqués ou saturés (triode).

## Ce qu'il faut vérifier ensuite

- **Excursion** : marge entre le point de repos et l'écrêtage, de chaque côté (alimentation, saturation).
  Pour une excursion symétrique, le point de repos est centré dans la plage utile.
- **Dissipation** : produit tension × courant de chaque transistor et résistance de puissance, comparé aux limites
  du composant (datasheet ; Silx ne les connaît pas encore). Signalez ce qui dépasse la moitié de la limite.
- **Stabilité de la polarisation** : la polarisation ne doit pas dépendre de β ni trop de la température.
  `run_campaign` en balayage de `TEMP` (par exemple −40 à 85 °C) et, si l'étage est discret, en coins ou Monte-Carlo
  sur les résistances de polarisation. Regardez le courant de collecteur ou de drain et la tension de sortie au repos.
- **Courants de fuite et entrées** : une base ou une grille sans chemin DC (couplage capacitif seul) est un défaut.

## Pièges

- Un modèle indicatif de la palette donne un ordre de grandeur, pas le comportement exact du composant réel.
- Un étage à forte contre-réaction peut sembler bien polarisé à 27 °C et dériver : faites le balayage thermique.
- En AC, Silx linéarise autour de ce point : un mauvais point de repos rend l'analyse AC trompeuse.

## Conclure

Tableau par transistor (tensions et courant relevés par l'outil, régime, marge d'excursion, dissipation), puis
dérive en température et dispersion si elles ont été simulées, puis corrections proposées (vérifiées par
re-simulation, voir `diagnostiquer-banc-en-echec`).
