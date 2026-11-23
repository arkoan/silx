# Démo simulation mixte XSPICE (v0 – hypothèses marquées)

Environnement : ngspice 42 (Ubuntu), mode batch, 0,3 s pour 8 ms simulées.

## Circuit
Sinus 1 kHz (2,5 V ± 1 V) + bruit blanc 40 mV rms → comparateur comportemental (source B, gain 1e4, pôle ~1 MHz, option hystérésis ±0,23 V) → `adc_bridge` → compteur 3 bits (3 × `d_dff` en cascade asynchrone + `d_inverter`) + `d_and` (détection « 7 ») → `dac_bridge` → charge RC.
Fichiers : `comparateur_compteur.cir` (avec hystérésis), `comparateur_sans_hysteresis.cir`, variantes `*_seuils_serres.cir`, `analyse.py`, `resultats.png`, sorties `.txt` / `.vcd`.

## Ce qui est démontré (vérifié)
- Les modèles XSPICE de base (`adc_bridge`, `dac_bridge`, `d_dff`, `d_inverter`, `d_and`) fonctionnent dans ngspice 42 sans compilation de code-model : le mixte de base est réalisable avec le socle prévu.
- Le comparateur commute 8 fois sur 8 périodes ; le compteur répond ; les signaux numériques ressortent côté analogique via `dac_bridge` (visibles dans `resultats.png`).
- Export des signaux numériques en VCD (`eprvcd`) possible.

## Investigation du compteur qui comptait faux (résolue)
**Cause : une erreur dans MON circuit, pas une limite d'XSPICE.** L'entrée `in` est sur la borne + du comparateur ; renvoyer la sortie vers cette même borne via R2 faisait une contre-réaction (anti-hystérésis) et non une réaction positive. Le comparateur oscillait à chaque franchissement, d'où des fronts parasites et un compteur faux. Correction : retour via l'inverse de la sortie (`Einv`, 5 V − V(cmp_out)).

Résultats après correction (comptage sur le VCD, attendu : 8 fronts, 4 impulsions q0, 1 impulsion `full`, valeur finale 0) :

| Variante | fronts d_cmp | passages en X | impulsions q0 | full | valeur finale |
|---|---|---|---|---|---|
| hystérésis, seuils 1,5/3,5 V | 8 | 16 | 4 | 1 | 0 (correct) |
| hystérésis, seuils 2,4/2,6 V | 8 | 2 | 4 | 1 | 0 (correct) |
| sans hystérésis, 1,5/3,5 V | 29 | 58 | 15 | 3 | 1 (faux) |
| sans hystérésis, 2,4/2,6 V | 25 | 9 | 13 | 3 | 1 (faux) |

Écarté : l'hypothèse « le front X→1 de `adc_bridge` est pris pour une horloge en plus ». Un test isolé (horloge propre 5 V → `adc_bridge` → `d_dff`) montre une seule bascule par front montant, même avec un X avant le 1. Le X ne cause donc pas de comptage double sur des fronts propres.

## Limites constatées
1. **Bruit sans hystérésis** : avec 40 mV rms de bruit, un comparateur nu fait chatter et le compteur compte 13 à 15 impulsions au lieu de 4, sans aucun avertissement de ngspice. C'est physiquement normal, mais Silx devra aider l'utilisateur à le voir (VCD / courbes numériques) ; resserrer les seuils de `adc_bridge` ne remplace pas l'hystérésis.
2. **État X** : `adc_bridge` produit un état UNKNOWN entre `in_low` et `in_high` (16 passages en X avec 1,5/3,5 V contre 2 avec 2,4/2,6 V pour la même fonction). Sans effet ici, mais à surveiller avec des composants sensibles au X.
3. **Visualisation** : `wrdata` exporte le numérique via `dac_bridge` (niveaux 0/2,5/5 V, X devient 2,5 V) ; il faut le VCD pour l'état logique réel. Silx devra gérer deux types de signaux (analogique échantillonné, numérique événementiel) dans ses courbes et ses mesures.
4. **Mesures du §3.3** (marge de phase, THD…) : non définies pour le numérique ; seules des mesures de timing (délais, fronts) ont du sens.
5. **Monte Carlo / .STEP** : non testés ; le bruit `TRNOISE` n'est pas reproductible par seed dans ce test (à vérifier pour §8 Reproductibilité).
6. **Portabilité** : ngspice installé via apt ; les code-models XSPICE dépendent du build (absents de certains binaires Windows/mac) → à vérifier pour le packaging (spec_technique §6).
7. **Hors v1** : pas de Verilog, conforme à la décision.

## Hypothèses
Modèle de comparateur comportemental (pas un composant réel), délais logiques arbitraires (5–10 ns), alimentation 5 V, bruit 40 mV.
