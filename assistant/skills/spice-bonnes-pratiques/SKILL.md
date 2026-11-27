---
name: spice-bonnes-pratiques
description: Prévenir et réparer les problèmes de simulation SPICE dans Silx (ngspice) - erreurs de convergence (timestep too small, singular matrix, gmin stepping failed, no convergence in DC), résultats aberrants, simulations lentes, unités et notation (piège M/meg), nœuds flottants, modèles introuvables, relecture de netlist.
---

# Bonnes pratiques SPICE dans Silx

Objectif : obtenir une simulation **juste** avant de conclure quoi que ce soit sur le circuit. Chaque correction
proposée est vérifiée par une nouvelle simulation.

## Diagnostic d'une simulation qui échoue

1. `run_simulation` avec `journal = true` (le journal ngspice est toujours joint en cas d'erreur).
2. `get_netlist` du banc : c'est ce que ngspice a reçu. Lisez-la avant de soupçonner le circuit.
3. Classez le message (tableau ci-dessous), formulez la cause, proposez une correction, resimulez.

| Message ngspice | Causes fréquentes | Remèdes |
|---|---|---|
| `singular matrix` | Nœud flottant (aucun chemin DC vers la masse), condensateurs en série sans résistance, broche non connectée, boucle de sources de tension ou d'inductances | Vérifier l'ERC de `describe_project` ; résistance de fuite (ex. 1 GΩ) vers la masse ; résistance série sur une boucle de sources ou d'inductances |
| `gmin stepping failed`, `source stepping failed`, `no convergence in DC` | Point de fonctionnement difficile : forte contre-réaction positive, bascules, comparateurs, modèles discontinus | Donner un point de départ (`.nodeset`), adoucir les modèles idéaux, vérifier les alimentations, découper le problème |
| `timestep too small` | Front infiniment raide (temps de montée nul), inductance idéale coupée brusquement, commutateur idéal, oscillation numérique | Temps de montée et de descente non nuls sur les sources ; résistance série ou parallèle sur L et C idéaux ; modèle moins idéal ; méthode `gear` |
| `unknown model`, `could not find`, `unknown subckt` | Modèle non importé, nom différent, syntaxe PSpice ou LTspice non comprise par ngspice | Importer le modèle du fabricant dans l'application ; vérifier le nom exact et l'ordre des broches du `.subckt` |
| Simulation très lente | Pas maximal trop petit, durée trop longue, oscillateur haute fréquence simulé sur une longue durée | Réduire la durée au besoin réel, laisser le mode « Auto » de Silx choisir le pas |

Aujourd'hui le serveur MCP ne règle pas les options ngspice ni le pas d'un banc : décrivez le réglage à
l'utilisateur (roue crantée du banc) quand il est nécessaire. Les valeurs, elles, se changent avec
`set_component_value`.

## Unités et notation

- Dans les champs de Silx : « M » et « meg » = méga, « m » = milli, unités tolérées (Hz, s, V, A, Ω, F, H, dB, %).
- Dans une **netlist SPICE**, « M » = **milli** : `1M` vaut 1 mΩ. Silx écrit toujours `1meg` pour R, C, L ;
  contrôlez les expressions libres (sources SPICE écrites à la main, `.param`).
- `u` = micro dans les netlists ; `µ` est accepté dans les champs de Silx.
- Une valeur aberrante d'un facteur 10⁶ ou 10⁹ est presque toujours une erreur de préfixe.

## Avant de croire un résultat

- **Transitoire** : la durée couvre-t-elle le régime établi (mesures sur le dernier quart) ? Les fronts sont-ils
  résolus (assez de points sur un front) ? Un oscillateur a-t-il démarré (impulsion de démarrage dans le banc) ?
- **AC** : la plage contient-elle les fréquences intéressantes ? L'analyse AC est **linéarisée autour du point de
  fonctionnement** : elle ignore l'écrêtage et la saturation. Un gain AC énorme sur un étage saturé est un symptôme,
  pas un résultat (vérifier le point de fonctionnement).
- **DC** : un balayage qui « saute » signale souvent une bistabilité (hystérésis) ou un problème de convergence.
- **Sources idéales** : une source de tension sans résistance interne qui attaque un condensateur donne un courant
  d'appel infini ; un générateur réel a souvent 50 Ω.
- **Modèles indicatifs** : les composants de la palette ont des modèles génériques, à remplacer par ceux du
  fabricant pour une conclusion sérieuse.
- **Température** : la simulation nominale est à 27 °C par défaut dans ngspice, pas à 25 °C.

## Conclure

Dites ce qui ne marchait pas, la correction faite ou demandée, et la preuve (simulation qui aboutit, résultat
plausible et cohérent avec un ordre de grandeur indépendant).
