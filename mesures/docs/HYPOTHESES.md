# Bibliothèque de mesures Silx — hypothèses de la v0.1

À valider par Régis. Chaque hypothèse est testée ou garde-foutée dans `tests/`.

## Périmètre
- .NET 10 (LTS), `Silx.Mesures` (bibliothèque) + `Silx.Mesures.Tests` (xUnit). Dépend de MathNet.Numerics (FFT du THD uniquement).
- Entrées : `CourbeTemporelle` (t, v) et `ReponseFrequentielle` (f en Hz, H complexe). Validées à la construction (finies, abscisses strictement croissantes, f > 0). Pas de lecture ngspice ici.
- Pas de dépendance à la simulation : les tests utilisent des solutions analytiques (RC, 2e ordre, boucle 3e ordre).

## Conventions de mesure
- **Valeur initiale** = premier échantillon. **Valeur finale** = moyenne temporelle des 5 % finaux de la durée (biais ≈ 2e-4 % sur un RC à 12 τ).
- **Garde-fou « établi »** : si la fenêtre finale varie de plus de 1 % de l'excursion, dépassement / montée / établissement lèvent `MesureImpossibleException` (courbe tronquée).
- **Dépassement** en % de l'excursion, ≥ 0. **Montée** 10–90 % (paramétrable). **Établissement** ±2 % (paramétrable) depuis t[0], dernière sortie de bande interpolée.
- **Gain DC** AC = |H| au plus bas point de fréquence. **Gain DC** temporel = valeur finale / amplitude d'entrée.
- **Bande passante −3 dB** relative au gain du premier point, première descente sous le seuil, interpolation en (log f, dB).
- **Marge de phase / de gain** sur le gain de boucle L(jf), retour unitaire. Phase déroulée. Première intersection (0 dB, −180°) vers les hautes fréquences.
- **Marge de gain** : si la phase n'atteint jamais −180° dans la plage → `NonEvaluable` (jamais « infini » ni pass).
- **THD** (ratio, pas %) : fondamentale connue, jusqu'à 10 périodes entières en fin de courbe, ré-échantillonnage linéaire uniforme (256 pts/période), 10 harmoniques, fenêtre rectangulaire.

## Gabarits
- `Gabarit` = mesure + bornes inclusives (≥, ≤ ou plage). Verdicts : `Pass`, `Fail`, `NonEvaluable` (mesure impossible ou NaN). Verdict global : Fail > NonEvaluable > Pass.
- Extensible : implémenter `IMesure<TEntree>` ou `new Mesure<T>(nom, unité, fonction)`.
- Un gabarit « à une fréquence donnée » (ex. PM ≥ 45° à 4 MHz) n'est **pas** encore couvert : la marge est mesurée à la fréquence de gain unité, pas imposée.

## Hors v0.1
Gain à une fréquence imposée, marges à plusieurs croisements, signal bruité (lissage), THD+N, offset d'un amplificateur (Vos), lecture des fichiers ngspice, campagnes.
