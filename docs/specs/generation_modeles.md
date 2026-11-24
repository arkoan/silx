# Chapitre de spécification : génération de modèles de simulation par prompt à partir d'une datasheet

Statut : proposition, à valider.
Prolonge `assistant_documents.md` (ingestion des datasheets, paramètres typés,
citations, calculs outillés) et s'appuie sur la bibliothèque de mesures, le
socle ngspice et le mixte de base XSPICE déjà décidé pour la v1.

Les hypothèses sont repérées **[Gn]** et récapitulées au §11.

---

## 1. Objectif

Permettre à l'ingénieur de demander en langage naturel, sur la base d'une
datasheet importée :

> « Crée un modèle SPICE de la 1N4148 à partir de cette datasheet. »
> « Modèle du 2N2222A, pour la température ambiante et le courant de
> collecteur de mon schéma. »
> « Modèle du 74HC74 alimenté en 4,5 V. »

Périmètre de ce chapitre :

1. **Composants discrets simples** : diodes (signal, Schottky, Zener),
   transistors bipolaires, MOSFET (signal et puissance).
2. **Composants logiques** : portes (NAND, NOR, inverseur, etc.), bascules
   (D, JK, T, SR), verrous, tampons, avec leurs délais et leurs niveaux.

Hors périmètre de ce chapitre : circuits intégrés analogiques (ampli-op,
régulateurs, convertisseurs), composants magnétiques, modèles thermiques et
de vieillissement.

Le principe fondateur reste intact. **L'IA ne calcule pas les valeurs des
paramètres.** Elle identifie le composant, choisit un modèle et orchestre ;
l'extraction des paramètres est faite par des outils déterministes qui
partent de valeurs sourcées de la datasheet, et le modèle produit est
**vérifié par simulation contre la datasheet** avant d'être proposé.

## 2. Cas d'usage

- **CG1. Diode de signal** : « 1N4148 » → modèle `.model D` avec IS, N, RS,
  CJO, VJ, M, TT, BV, IBV, et rapport de validation (Vf à plusieurs courants,
  capacité à tension donnée, temps de recouvrement inverse).
- **CG2. Diode Zener ou Schottky** : même démarche, avec le claquage inverse
  (Zener) ou la faible tension directe et la forte fuite (Schottky) dans le
  rapport.
- **CG3. Transistor bipolaire** : « 2N2222A » → modèle Gummel-Poon (IS, BF,
  NF, VAF, IKF, ISE, NE, BR, RB, RC, RE, CJE, CJC, TF, TR), validé sur hFE,
  VCE(sat), VBE(sat), capacités et temps de commutation.
- **CG4. MOSFET de signal** : modèle de base (VTO, KP, LAMBDA, RD, RS,
  capacités) validé sur Vth, courant de drain, Rds(on), Ciss/Coss/Crss.
- **CG5. MOSFET de puissance** : sous-circuit avec cœur MOSFET, résistances
  d'accès calées sur Rds(on), capacités calées sur Ciss/Coss/Crss et la charge
  de grille Qg, diode de corps calée sur Vsd et trr.
- **CG6. Porte logique** : « 74HC00 à 4,5 V » → modèle XSPICE (d_nand) avec
  interface analogique : seuils d'entrée, niveaux de sortie, résistance de
  sortie, délais tpLH et tpHL, capacité d'entrée.
- **CG7. Bascule ou verrou** : « 74HC74 » → d_dff avec ses délais d'horloge,
  de mise à un et de remise à zéro, plus contrôle des temps d'établissement
  et de maintien (§5.5).
- **CG8. Fonction logique quelconque d'après la table de fonction** : table
  de vérité ou de transition lue dans la datasheet → modèle par table
  (d_lut) ou machine à états (d_state).
- **CG9. Prompt sans datasheet** : « diode Schottky 40 V 1 A » : possible,
  mais le modèle est générique et étiqueté **non sourcé** (§8).

## 3. Chaîne de génération

```
prompt ─► identification ─► paramètres typés ─► choix du gabarit de modèle
                                                        │
 modèle proposé ◄─ validation par simulation ◄─ extraction déterministe
        │                        │
        ▼                        ▼
 approbation utilisateur    rapport d'écarts vs datasheet
        │
        ▼
 enregistrement : modèle + symbole + provenance
```

1. **Identification** : composant, variante, boîtier, grade (le §3.3 de
   `assistant_documents.md` gère déjà les variantes). La famille de modèle
   est déduite de la nature du composant, et confirmée par l'utilisateur.
2. **Paramètres typés** : les valeurs min/typ/max de la datasheet avec leurs
   conditions, extraites et validées comme au chapitre précédent. Les
   paramètres au statut `extrait` (non validé) sont signalés dans le
   résultat.
3. **Gabarit de modèle** : un gabarit par famille (diode, BJT, MOSFET signal,
   MOSFET puissance, porte, bascule, verrou, table). Le gabarit fixe la
   forme du modèle et la liste des paramètres à ajuster.
4. **Extraction déterministe** : formules analytiques classiques pour une
   première estimation, puis ajustement numérique avec ngspice dans la
   boucle (§4).
5. **Génération du texte du modèle** : produite par code à partir du
   gabarit et des valeurs ; l'IA n'écrit jamais de valeur numérique dans le
   fichier de modèle. **[G1]**
6. **Validation** : banc de test généré automatiquement qui reproduit les
   conditions de test de la datasheet, comparaison aux valeurs sourcées
   (§6).
7. **Approbation et enregistrement** : l'utilisateur voit le rapport avant
   d'accepter (§9).

## 4. Composants discrets : extraction des paramètres

### 4.1 Diode

| Paramètre | Source datasheet | Méthode |
|---|---|---|
| IS, N | Vf à au moins deux courants directs | Résolution de la caractéristique de Shockley sur deux points ; ajustement sur tous les points disponibles |
| RS | Vf à fort courant | Écart de Vf au modèle exponentiel pur |
| CJO, VJ, M | Capacité à une ou plusieurs tensions inverses (CT ou Cj) | Ajustement de la loi de capacité de jonction |
| TT | Temps de recouvrement trr, ou charge Qrr, dans des conditions données | Calage par simulation du banc de commutation de la datasheet |
| BV, IBV | Tension de claquage ou de Zener, courant de test | Directement |
| ISR/NR (fuite) | Courant inverse à une tension donnée | Directement |

### 4.2 Transistor bipolaire

| Paramètre | Source | Méthode |
|---|---|---|
| IS, NF | VBE à un ou deux IC | Comme la diode |
| BF, IKF | hFE à plusieurs IC | Ajustement de la courbe de gain |
| VAF | Pente de IC en fonction de VCE (courbe de sortie ou hoe) | Extraction depuis des points de courbe (voir §4.4) |
| RB, RC, RE | VCE(sat), VBE(sat) à IC/IB donnés | Calage sur la saturation |
| CJE, CJC | Capacités d'entrée et de sortie à tension donnée (Cibo, Cobo) | Ajustement de la loi de jonction |
| TF, TR | fT, délais td, tr, ts, tf | Calage par simulation du banc de commutation |
| BR, ISE, NE | Souvent absents | Valeurs par défaut du gabarit, marquées « par défaut » |

### 4.3 MOSFET

| Paramètre | Source | Méthode |
|---|---|---|
| VTO | VGS(th) à un ID donné | Directement, avec les conditions de test |
| KP (ou W/L) | gfs, ou ID à VGS donné, ou courbe de transfert | Calage sur la transconductance |
| RD + RS (puissance) | Rds(on) à VGS et ID donnés | Calage sur Rds(on) |
| Capacités | Ciss, Coss, Crss à VDS donné ; charge de grille Qg, Qgs, Qgd | Capacités fixes dans la v1 ; non linéarité de Crss (Cgd) par sous-circuit dédié **[G2]** |
| Diode de corps | Vsd, trr, Qrr | Comme une diode (§4.1) |
| LAMBDA | Pente de ID en saturation | Courbe de sortie (§4.4) |

### 4.4 Dépendance aux courbes

Plusieurs paramètres (VAF, LAMBDA, hFE en fonction de IC, capacités en
fonction de la tension, dérive en température) ne figurent dans la
datasheet que **sous forme de courbes**. Or la lecture de valeurs sur
courbe est reportée après la v1 (décision du 24/11/2026, §3.4 de
`assistant_documents.md`).

Conséquence assumée pour le premier jalon (§12) :

- les modèles v1 sont calés **uniquement sur les valeurs tabulées** de la
  datasheet ; les paramètres qui exigent une courbe prennent la valeur par
  défaut du gabarit et sont listés dans le rapport comme « non calibrés
  (valeur par défaut) » ;
- la numérisation assistée de courbes devient la première évolution
  naturelle de la génération de modèles ; elle est donc avancée à
  l'ordre du jour dès que ce chapitre est validé (question §13.1).

### 4.5 Ajustement numérique

Lorsque la formule analytique ne suffit pas, un ajustement par moindres
carrés minimise l'écart entre les valeurs de la datasheet et celles
mesurées par simulation ngspice sur le banc de validation. Contraintes :

- bornes physiques sur chaque paramètre (IS > 0, N entre 1 et 2, etc.) ;
- ajustement déterministe et reproductible (point de départ et critère
  d'arrêt fixés), résultat enregistré avec ses paramètres d'exécution ;
- jamais plus de paramètres ajustés que de points de datasheet : le rapport
  indique le nombre de degrés de liberté.

## 5. Composants logiques

### 5.1 Modèle retenu : XSPICE numérique avec ponts analogiques

Cohérent avec la décision v1 « mixte de base via XSPICE » : les portes et
bascules sont des modèles de code XSPICE (d_and, d_or, d_nand, d_nor,
d_inv, d_buffer, d_dff, d_jkff, d_tff, d_srff, d_dlatch, d_srlatch), reliés
au monde analogique par des ponts (adc_bridge, dac_bridge). C'est la forme
déjà utilisée dans la démo XSPICE du dépôt.

### 5.2 Paramètres extraits d'une datasheet logique

| Élément du modèle | Source datasheet |
|---|---|
| Seuils d'entrée (`in_low`, `in_high` du pont A/N) | VIL max, VIH min à la tension d'alimentation choisie |
| Niveaux de sortie (`out_low`, `out_high`) | VOL, VOH à la charge de test |
| Résistance de sortie | (VOH − VOL) / courant de test IOH, IOL |
| Délais de propagation (`rise_delay`, `fall_delay`) | tpLH, tpHL (typ, ou max pour le pire cas) à Vcc et CL donnés |
| Délais d'horloge, de mise à un/zéro (bascules) | tpd de CLK→Q, de SET→Q, de RESET→Q |
| Capacité d'entrée | CI |
| Temps de transition | tt (t_rise, t_fall du pont N/A) |
| Courant d'alimentation | ICC, pour un bilan de consommation optionnel |
| Table de fonction | Table de vérité ou de transition |

### 5.3 Tension d'alimentation

Les datasheets logiques donnent leurs caractéristiques par tension
d'alimentation (par exemple 2 V, 4,5 V, 6 V pour la famille HC). Règle :

- le modèle est généré pour la tension d'alimentation **présente dans le
  schéma** ;
- si cette tension tombe entre deux points de la datasheet, les paramètres
  sont interpolés linéairement et le rapport le signale ;
- hors de la plage de la datasheet, la génération est **refusée** plutôt
  qu'extrapolée. **[G3]**

### 5.4 Logique combinatoire quelconque

Quand la fonction n'est pas une porte de base (décodeur, multiplexeur,
bascule exotique), la datasheet donne une table de fonction. Elle est
convertie en modèle par table (d_lut, combinatoire) ou en machine à états
(d_state, séquentiel). L'IA propose la lecture de la table, l'utilisateur
la **valide visuellement** à côté de la page d'origine (même mécanisme que
pour les formules), puis la conversion en modèle est déterministe.

### 5.5 Limites des modèles XSPICE de bascules

Les modèles XSPICE de bascules couvrent les délais mais **pas la détection
des violations de temps d'établissement (setup) et de maintien (hold)**.
**[G4]** Pour le cas d'usage CG7, Silx génère donc, en complément du modèle,
un **moniteur de contraintes temporelles** : un sous-circuit d'observation
qui signale chaque violation du setup, du hold et de la largeur minimale
d'impulsion tirés de la datasheet. Les violations sont reportées dans les
résultats de simulation et utilisables dans les gabarits pass/fail. Si le
moniteur s'avère impraticable en XSPICE pur, le contrôle est fait en
post-traitement des courbes numériques par la bibliothèque de mesures.

## 6. Validation du modèle contre la datasheet

Un modèle n'est jamais présenté sans rapport de validation.

- Un **banc de test est généré** pour chaque famille (génération déjà
  prévue par `generate_testbench`), reproduisant les conditions de test de
  la datasheet : Vf à If, hFE à IC et VCE, Rds(on) à VGS et ID, tpd à Vcc et
  CL, etc.
- Chaque grandeur de la datasheet utilisée à l'extraction est **simulée
  puis comparée** : valeur datasheet (avec citation), valeur simulée,
  écart relatif, verdict.
- Les grandeurs **non utilisées** pour l'extraction (valeurs de contrôle) sont
  comparées aussi et pèsent davantage dans la confiance : un modèle qui
  reproduit les points qui ont servi à le caler n'est pas pour autant
  correct ailleurs. **[G5]**
- **Tolérance de validation par défaut** : à définir par famille et
  modifiable par l'utilisateur ; point de départ : Vf, VBE, VGS(th) à ±5 %,
  hFE, Rds(on), capacités à ±15 %, délais à ±10 %. **[G6]**
- Un écart hors tolérance bloque le statut « validé » et est affiché en
  premier dans le rapport.
- Plage de validité : le rapport précise la plage de courants, de tensions
  et de température sur laquelle le modèle a été confronté à la datasheet,
  et signale les usages hors plage.

## 7. Autres exigences

### 7.1 Dérive en température

La plupart des datasheets donnent peu de points en température. Règles :

- si la datasheet fournit des valeurs à plusieurs températures (tableau),
  elles servent à ajuster les paramètres thermiques (EG, XTI, TNOM, TC1,
  TC2 selon le modèle) ;
- sinon, le modèle est valable à la température des conditions de test et
  **le rapport le dit**. Une simulation de campagne thermique (§3.4 de la
  spec fonctionnelle) sur un modèle sans paramètres thermiques calibrés
  affiche un avertissement.

### 7.2 Variantes min / typ / max

Pour chaque modèle, Silx peut générer, en plus du modèle typique, des
**jeux min et max** à partir des valeurs bornées de la datasheet (hFE min
et max, tpd max, Rds(on) max, etc.). Ces jeux alimentent directement les
campagnes de pire cas et Monte Carlo (paramètre `.param` sélectionnable) ;
un jeu min/max n'est pas une distribution statistique, et le rapport ne la
présente pas comme telle. **[G7]**

### 7.3 Modèle du fabricant prioritaire

Si le fabricant fournit un modèle SPICE officiel (fichier `.lib` ou `.mod`),
il est plus fiable qu'un modèle généré à partir de la datasheet.

- Silx recherche un modèle officiel dans les bibliothèques et documents du
  projet avant de générer ;
- s'il existe, il propose de l'utiliser, et peut quand même **comparer** le
  modèle officiel à la datasheet avec le même banc de validation (§6 ; c'est
  le cas d'usage CU5 de `assistant_documents.md`) ;
- la génération reste utile quand aucun modèle n'existe, pour combler un
  modèle incomplet ou pour des composants anciens.

### 7.4 Statut et provenance d'un modèle généré

Chaque modèle porte, dans l'en-tête de son fichier et dans les métadonnées
du projet :

- l'étiquette **« généré par Silx, non officiel »** ;
- le document source (révision, empreinte, pages), la variante, les
  conditions retenues ;
- pour chaque paramètre : valeur, origine (extrait de telle ligne,
  ajusté, par défaut du gabarit), citation ;
- la version du gabarit et de l'algorithme d'extraction ;
- le rapport de validation et sa date.

Ainsi un rapport de campagne (§3.6) peut dire exactement sur quel modèle,
généré à partir de quoi, reposent ses résultats.

## 8. Cas sans datasheet

Un prompt sans document source est accepté pour le prototypage rapide
(« diode Schottky 40 V 1 A », « inverseur CMOS 5 V »), mais :

- le modèle est généré à partir de **valeurs génériques de gabarit** et
  étiqueté **« générique, non sourcé »** ;
- il ne peut pas être présenté comme validé ;
- l'IA doit le dire dans sa réponse, en une phrase, et proposer d'importer
  la datasheet.

## 9. Interaction avec l'utilisateur

- L'utilisateur demande dans le prompt, depuis un composant du schéma ou
  depuis la bibliothèque. Si plusieurs variantes ou conditions sont
  possibles, l'assistant pose **une seule** question de levée d'ambiguïté,
  avec une valeur par défaut recommandée.
- Avant de générer, un récapitulatif affiche les paramètres de datasheet
  retenus (avec citations) et ceux qui ne sont pas calibrés.
- Après génération : rapport de validation côte à côte avec les extraits de
  datasheet, tracés comparatifs (par exemple Vf en fonction de If, simulé
  contre points de la datasheet).
- **L'utilisateur approuve** le modèle ; seulement alors il est enregistré
  dans la bibliothèque du projet et associé à un symbole (association
  symbole ↔ modèle prévue au §3.1 de la spec fonctionnelle, avec symbole
  standard fourni pour les familles couvertes).
- Les modèles enregistrés restent modifiables à la main (fichier SPICE
  texte) ; une modification manuelle retire le statut « validé » jusqu'à
  une nouvelle validation.

## 10. Impact sur les spécifications

### 10.1 Nouveaux outils MCP

| Outil | Rôle |
|---|---|
| `model_plan` | Identifie le composant, propose famille de modèle et conditions, liste les paramètres disponibles et manquants |
| `extract_diode_params`, `extract_bjt_params`, `extract_mosfet_params` | Extraction déterministe d'après les paramètres typés |
| `extract_logic_params` | Seuils, niveaux, délais, capacité, pour une tension d'alimentation donnée |
| `fit_model` | Ajustement numérique avec ngspice dans la boucle (§4.5) |
| `generate_model` | Produit le texte `.model` / `.subckt` ou le jeu de modèles XSPICE à partir du gabarit et des valeurs |
| `validate_model_vs_datasheet` | Banc de validation et rapport d'écarts (généralise `compare_model_to_datasheet`) |
| `generate_timing_monitor` | Moniteur de setup, hold et largeur minimale d'impulsion (§5.5) |
| `save_model` | Enregistre le modèle et sa provenance, avec confirmation de l'utilisateur |

Les outils d'écriture (`save_model`) passent par la confirmation de
l'utilisateur dans l'interface, comme `apply_and_test`.

### 10.2 Nouvelles briques

- **Gabarits de modèles** : fichiers de gabarit versionnés (un par famille),
  avec la liste de leurs paramètres, bornes, valeurs par défaut et banc de
  validation associé. Ajouter un gabarit n'exige pas de modifier le moteur.
- **Extracteurs** : bibliothèque C# testée comme la bibliothèque de mesures,
  avec jeux de référence connus (modèles officiels de composants courants
  dont on connaît la datasheet) ; un modèle généré à partir de la
  datasheet de ce composant doit retrouver le comportement de la datasheet
  dans les tolérances du §6.
- **Ajusteur** : moindres carrés borné (MathNet.Numerics, déjà retenu pour
  la bibliothèque de mesures) piloté par le socle ngspice.
- **Association symbole ↔ modèle** : symboles standards pour diode,
  Zener, Schottky, NPN, PNJ, NMOS, PMOS, portes et bascules.

### 10.3 Modifications à reporter

- Spec fonctionnelle §3.1 (bibliothèques) : modèles générés, provenance.
- Spec fonctionnelle §3.9 : outils du §10.1.
- Spec fonctionnelle §3.4 (campagnes) : jeux min/max issus de la génération.
- `assistant_documents.md` §10.2 : renvoi vers les outils du §10.1 ;
  `compare_model_to_datasheet` devient `validate_model_vs_datasheet`.
- `assistant_documents.md` §3.4 : la numérisation assistée de courbes est
  réexaminée au vu du §4.4 (question §13.1).
- Spec technique : gabarits de modèles, ajusteur, moniteurs de contraintes
  temporelles.

## 11. Hypothèses

- **[G1]** Le fichier de modèle est produit par code à partir d'un
  gabarit ; l'IA choisit le gabarit et commente, elle n'écrit pas les
  valeurs.
- **[G2]** Un MOSFET de puissance avec capacités fixes et diode de corps
  suffit pour la v1 ; la non-linéarité de Crss est un sous-circuit à part
  si l'écart de validation l'exige.
- **[G3]** Pas d'extrapolation en tension d'alimentation hors des points de
  la datasheet ; interpolation linéaire entre points.
- **[G4]** Les modèles XSPICE de bascules fournis avec ngspice ne
  détectent pas les violations de setup/hold ; à confirmer sur la version de
  ngspice retenue (la démo XSPICE du dépôt utilise d_dff avec délais).
- **[G5]** Il est possible de réserver une partie des valeurs de datasheet
  à la validation (non utilisées à l'extraction), à condition que la
  datasheet en contienne assez ; sinon le rapport signale l'absence de
  valeurs de contrôle.
- **[G6]** Les tolérances du §6 sont des points de départ raisonnables ;
  à calibrer sur un corpus de composants connus.
- **[G7]** Les jeux min/max suffisent au pire cas ; la modélisation
  statistique des paramètres (distributions) n'est pas dérivée des
  datasheets, qui ne les fournissent pas.
- **[G8]** Les modèles de niveau 1 pour MOSFET et Gummel-Poon pour BJT sont
  suffisants pour des circuits de bureau d'étude de signal et de puissance
  modérée ; les modèles plus avancés (BSIM, VBIC, HiSIM) ne sont pas
  dérivables d'une datasheet.
- **[G9]** La datasheet est la bonne source : elle donne des valeurs
  typiques et garanties, pas la physique interne ; les modèles générés sont
  des modèles de comportement calés sur la datasheet, pas des modèles de
  procédé.

## 12. Jalons

| Jalon | Contenu | Critère de sortie |
|---|---|---|
| **M0. Diode** | Gabarit diode, extraction IS/N/RS/CJO/BV, banc et rapport de validation, `generate_model`, enregistrement avec provenance | CG1 : modèle d'une diode de signal courante reproduisant Vf, Cj et trr de la datasheet dans les tolérances du §6 |
| **M1. Portes et bascules** | `extract_logic_params`, modèles XSPICE de portes et de bascules D, interpolation en tension, ponts analogiques | CG6 et CG7 sur une porte et une bascule HC de référence, avec délais reproduits à ±10 % |
| **M2. BJT** | Gabarit Gummel-Poon, extraction sur valeurs tabulées, validation hFE, VCE(sat), commutation | CG3 sur un transistor courant ; paramètres non calibrés listés |
| **M3. MOSFET** | Gabarits signal et puissance, diode de corps, Qg | CG4 et CG5 sur un MOSFET de puissance de référence (Rds(on), Ciss/Coss/Crss, Qg dans les tolérances) |
| **M4. Contraintes temporelles et fonctions** | Moniteur setup/hold, table de fonction (d_lut, d_state), jeux min/max | CG7 avec violations détectées ; CG8 sur un décodeur ou un compteur |
| **M5. Courbes** | Numérisation assistée des courbes (liée au §13.1), calibration de VAF, hFE(IC), capacités vs tension | Les paramètres « non calibrés » des jalons précédents passent en « ajustés » |

Les jalons M0 et M1 partagent le socle documentaire des jalons J0 à J2 de
`assistant_documents.md` et ne peuvent commencer qu'après J2.

## 13. Questions ouvertes pour validation

1. **Courbes** : sans lecture de courbes, les modèles de transistors
   restent partiellement calibrés (VAF, LAMBDA, hFE en fonction de IC).
   Faut-il avancer la numérisation assistée de courbes, aujourd'hui
   reportée après la v1, pour qu'elle arrive au jalon M2 ?
2. **Priorité des familles** : diodes, puis logique, puis BJT, puis MOSFET
   (ordre des jalons) vous convient-il, ou la logique passe-t-elle avant
   les diodes ?
3. **Violations de temps** : le moniteur de setup/hold (§5.5) est-il
   nécessaire dès la première version des bascules, ou les délais seuls
   suffisent-ils pour commencer ?
4. **Modèle officiel** : faut-il que Silx refuse de générer un modèle quand
   un modèle officiel est présent dans la bibliothèque, ou seulement le
   proposer en premier (option retenue ici) ?
