# Démo simulation mixte XSPICE (v0 – hypothèses marquées)

Environnement : ngspice 42 (Ubuntu), mode batch, 0,3 s pour 8 ms simulées.

## Circuit
Sinus 1 kHz (2,5 V ± 1 V) + bruit blanc 40 mV rms → comparateur comportemental (source B, gain 1e4, pôle ~1 MHz, option hystérésis ±0,23 V) → `adc_bridge` → compteur 3 bits (3 × `d_dff` en cascade asynchrone + `d_inverter`) + `d_and` (détection « 7 ») → `dac_bridge` → charge RC.
Fichiers : `comparateur_compteur.cir` (avec hystérésis), `comparateur_sans_hysteresis.cir`, variantes `*_seuils_serres.cir`, `analyse.py`, `resultats.png`, sorties `.txt` / `.vcd`.

## Ce qui est démontré (vérifié)
- Les modèles XSPICE de base (`adc_bridge`, `dac_bridge`, `d_dff`, `d_inverter`, `d_and`) fonctionnent dans ngspice 42 sans compilation de code-model : le mixte de base est réalisable avec le socle prévu.
- Le comparateur commute 8 fois sur 8 périodes ; le compteur répond ; les signaux numériques ressortent côté analogique via `dac_bridge` (visibles dans `resultats.png`).
- Export des signaux numériques en VCD (`eprvcd`) possible.

## Limites constatées
1. **État inconnu (X)** : `adc_bridge` produit un état UNKNOWN entre `in_low` et `in_high`. Avec 1,5/3,5 V, chaque transition passe par X et le front X→1 est vu comme un front d'horloge par `d_dff`. Résultat : le compteur compte plus que les 8 fronts réels (valeur finale 2 au lieu de 0) — **sans** erreur ni avertissement de ngspice. Resserrer les seuils (2,4/2,6 V) ne l'a pas corrigé (14 impulsions q0 au lieu de 4 attendues) : **cause pas encore établie** (hypothèses : chatter dû au bruit pendant la transition, interprétation X→1 par le flip-flop, interaction pas de temps). À investiguer avant de conclure.
2. **Glitchs sans hystérésis** : impulsions parasites visibles sur q0 et `full` (fines raies dans `resultats.png`). L'hystérésis les réduit mais le comptage reste faux (point 1).
3. **Visualisation** : `wrdata` exporte le numérique via `dac_bridge` (niveaux 0/2,5/5 V, l'état X devient 2,5 V) ; il faut passer par le VCD pour avoir le vrai état logique. Silx devra gérer deux types de signaux (analogique échantillonné, numérique événementiel) dans ses courbes et ses mesures.
4. **Mesures du §3.3** (marge de phase, THD…) : non définies pour les signaux numériques ; seules des mesures de timing (délais, fronts) ont du sens.
5. **Monte Carlo / .STEP** : non testés ici ; le bruit `TRNOISE` n'est pas reproductible par seed dans ce test (point à vérifier pour §8 Reproductibilité).
6. **Portabilité** : ngspice installé via apt ; les code-models XSPICE dépendent du build (absents de certains binaires Windows/mac) → à vérifier pour le packaging (spec_technique §6).
7. **Hors v1** : pas de Verilog (nécessite d'autres outils type Verilator/co-simulation), conforme à la décision.

## Hypothèses
Modèle de comparateur comportemental (pas un composant réel), délais logiques arbitraires (5–10 ns), alimentation 5 V, bruit 40 mV.
