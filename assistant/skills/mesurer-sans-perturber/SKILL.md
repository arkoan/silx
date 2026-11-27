---
name: mesurer-sans-perturber
description: Prendre en compte l'instrument de mesure dans Silx et expliquer un écart entre simulation et maquette - charge d'une sonde d'oscilloscope 1× ou 10×, fil de masse de la sonde, bande passante et temps de montée de l'instrument, entrée 50 Ω, générateur 50 Ω sur haute impédance, multimètre, valeur efficace vraie, analyseur de spectre.
---

# Mesurer sans perturber

L'instrument fait partie du circuit. Avant de conclure qu'une maquette « ne marche pas comme la simulation »,
reproduisez la mesure dans un banc Silx.

## Effets à connaître

| Instrument | Effet | Ordre de grandeur courant (vérifier la notice) |
|---|---|---|
| **Sonde 10×** | Charge R // C sur le nœud | 10 MΩ // 10 à 15 pF |
| **Sonde 1×** | Charge plus lourde, bande réduite | 1 MΩ // 50 à 100 pF avec le câble |
| **Fil de masse de la sonde** | Inductance de la boucle → sonnerie sur les fronts rapides | quelques nH par cm ; ressort de masse court pour les fronts rapides |
| **Bande de l'oscilloscope et de la sonde** | Temps de montée ajouté | tr ≈ 0,35 / BW ; les temps de montée se composent en somme quadratique |
| **Entrée 50 Ω** | Charge forte, réservée aux sources 50 Ω | 50 Ω |
| **Générateur 50 Ω** | L'amplitude affichée suppose une charge de 50 Ω : sur haute impédance, la tension **double** | Rs = 50 Ω |
| **Multimètre** | Charge en tension, chute en courant (shunt), bande limitée en alternatif | 10 MΩ typique en tension |
| **Voltmètre alternatif** | Valeur moyenne redressée étalonnée en efficace, fausse hors sinus, sauf « true RMS » | — |
| **Analyseur de spectre** | Niveau de bruit dépendant de la bande de résolution (RBW) | — |

## Reproduire la mesure dans Silx

1. Dans le banc concerné, ajoutez une **charge** qui représente l'instrument sur le nœud mesuré (R // C de la sonde),
   et la résistance interne du générateur. Le serveur ne peut pas encore ajouter d'élément au banc : décrivez-le à
   l'utilisateur, qui le pose dans le calque du banc (le circuit sous test reste intact).
2. `run_simulation` et `get_raw_curve` avec et sans l'instrument ; comparez forme, amplitude, fréquence, temps de
   montée.
3. Pour un temps de montée, comparez aussi le temps de montée de l'instrument (sa bande) au temps simulé : un signal
   plus rapide que l'oscilloscope sera mesuré au temps de montée de l'oscilloscope.

## Écarts simulation ↔ maquette : autres causes à passer en revue

Modèles indicatifs au lieu des modèles du fabricant, parasites du PCB (pistes, plots, inductance des boîtiers),
découplage absent sur la maquette, composants hors tolérance ou d'une autre référence, température, alimentation de
labo avec sa propre impédance et sa limitation de courant, oscillation parasite masquée par la sonde.

## Conclure

L'écart observé, la part expliquée par l'instrument (simulée), les autres causes probables à vérifier sur la
maquette, et la façon de mesurer proprement (sonde, masse courte, entrée adaptée).
