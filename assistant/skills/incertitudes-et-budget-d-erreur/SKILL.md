---
name: incertitudes-et-budget-d-erreur
description: Établir le budget d'erreur ou d'incertitude d'une chaîne de mesure conçue dans Silx - exactitude, justesse, fidélité, résolution ; erreurs de décalage, de gain, de non-linéarité, de dérive, de bruit ; combinaison au pire cas ou quadratique ; incertitudes de type A et B (GUM), facteur d'élargissement ; ppm, % de la lecture + digits ; ce que corrige un étalonnage.
---

# Incertitudes et budget d'erreur

## Vocabulaire (à employer juste)

- **Exactitude** : écart à la valeur vraie (justesse + fidélité). **Justesse** : erreur systématique moyenne.
  **Fidélité** (répétabilité) : dispersion des mesures répétées. **Résolution** : plus petit écart affiché ou
  converti ; ce n'est **pas** l'exactitude.
- **Erreur** : écart connu, qu'on peut corriger. **Incertitude** : doute qui subsiste après corrections.

## Recenser les sources

| Source | Nature | Où la trouver |
|---|---|---|
| Décalage (offset) et sa dérive en °C | Systématique | Simulation (Monte-Carlo, `TEMP`) + datasheets (offset d'ampli, non dispersé dans les modèles) |
| Erreur de gain et sa dérive | Systématique | Simulation (tolérances des résistances, coins) + référence de tension (datasheet) |
| Non-linéarité | Systématique | Balayage de l'entrée sur l'étendue ; INL de l'ADC (datasheet) |
| Bruit | Aléatoire | `get_noise` sur la bande de mesure ; bruit de quantification q/√12 |
| Vieillissement, hystérésis, humidité | Systématique lente | Datasheets uniquement : non simulé |
| Capteur lui-même | Les deux | Datasheet du capteur (classe, tolérance) |
| Instrument d'étalonnage | Type B | Certificat d'étalonnage |

Mettez **tout dans une même unité** (unité de la grandeur mesurée, ppm ou % de la pleine échelle, LSB) avant de
combiner. Une spécification « ± (x % de la lecture + n digits) » donne une erreur qui dépend du point de mesure.

## Combiner

- **Pire cas** (somme arithmétique des bornes) : sûr, pessimiste ; c'est ce que donnent les coins de Silx pour les
  composants simulés.
- **Somme quadratique** (RSS) des incertitudes-types, pour des sources indépendantes : réaliste.
- Méthode GUM : incertitude-type de chaque source (type A par statistique de mesures ou de tirages Monte-Carlo ; type
  B à partir d'une spécification : une borne ± a avec loi uniforme donne a/√3), incertitude composée par somme
  quadratique, puis incertitude élargie U = k·u (k = 2 pour environ 95 %).
- Le Monte-Carlo de Silx donne directement l'écart-type de la mesure dû aux tolérances simulées : c'est une
  incertitude de type A sur ce sous-ensemble de sources. Les autres sources s'ajoutent à part.
- Les opérations elles-mêmes se font par outil de calcul (`worst_case`, `evaluate_formula`) quand le serveur les
  propose ; d'ici là, présentez le tableau des contributions simulées et sourcées, et la formule de combinaison.

## Étalonnage

- Un étalonnage en deux points corrige le **décalage et le gain**, à la température et au moment de l'étalonnage.
- Il ne corrige **pas** : la non-linéarité (sauf étalonnage multipoint), le bruit, la dérive après étalonnage
  (température, temps), l'incertitude de l'étalon.
- Le budget « après étalonnage » ne garde donc que ces termes : présentez les deux budgets.

## Conclure

Tableau des contributions (source, valeur, type, origine : simulée, datasheet ou hypothèse), combinaison pire cas et
quadratique, incertitude élargie, effet de l'étalonnage, sources non quantifiées.
