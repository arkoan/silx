# Bibliothèque de composants courants : estimation de charge

Statut : estimation, rien n'est implémenté. Rédigée le 28/11/2026 à la demande de Régis.

## 1. Ce qui existe déjà

- **Import de modèles** (`Silx.Etude/ModelesSpice.cs`) : `.model` D, NPN, PNP, NMOS, PMOS, NJF, PJF, `.subckt`,
  portes et bascules XSPICE ; lecture des rôles de broches dans les commentaires fabricant (« 20=DRAIN 10=GATE ») et
  détection de la nature d'un sous-circuit.
- **Compatibilité PSpice** : ngspice est lancé en `ngbehavior=psa` dès qu'un composant importé est présent.
- **Définition de composant** (`DefinitionComposant.cs`) : modèle + symbole + correspondance des broches, broches
  cachées raccordées d'office (utile pour les alimentations d'un AOP), vérification, fichier `.silxcomp` (JSON).
- **Gabarits de symboles** (`GabaritsSymboles.cs`) : diode, NPN, PNP, MOS 3/4 broches (diode de corps), JFET, quartz,
  logique CEI, boîte, dessin libre ; éditeur de symbole.
- **Palette** : une tuile par catégorie puis la liste des références ; recherche ⌘K.
- **Composants intégrés** : 4 diodes (`DiodeModels.cs`), 5 bipolaires, 3 JFET, 4 MOSFET niveau 1, 4 quartz
  (`BibliothequeBase.cs`), portes et bascules XSPICE. Modèles « indicatifs », écrits en dur dans le code.

Manquent pour une vraie bibliothèque : symboles Zener, Schottky, LED, AOP, comparateur, régulateur, référence ;
catégories pour les circuits intégrés (tout sous-circuit tombe aujourd'hui dans « Sous-circuit ») ; métadonnées
(fabricant, boîtier, lien datasheet, provenance, licence, statut) ; validation automatique des modèles ; catalogue
hors du code.

## 2. Périmètre proposé (environ 100 références)

| Famille | Références type | Nb |
|---|---|---|
| Diodes signal et redressement | 1N4148, 1N914, 1N4001…1N4007, 1N5408, UF4007 | 6 |
| Schottky | BAT54, BAT85, 1N5817, 1N5819, SS34 | 5 |
| Zener | BZX79 de 2V7 à 24 V (valeurs E12) + Zener paramétrable (Vz) | 12 |
| LED | rouge, verte, jaune, bleue, blanche, IR + LED paramétrable | 6 |
| Bipolaires | existants + BC337/327, BC548C/558C, 2N2907, BD139/140, TIP31C/32C, TIP120 | 10 |
| MOSFET | existants + BSS138, BSS84, AO3400, AO3401, IRLZ44N, IRFZ44N, IRF3205, Si2302 | 8 |
| JFET | existants + 2N3819, J310, BF245 | 3 |
| AOP | AOP générique paramétrable (gain, GBW, SR, rails, offset) + LM358, LM324, TL071/072/074, NE5532, OP07, µA741, MCP6002, OPA2134, LMV321 | 12 |
| Comparateurs | LM393, LM339, LM311, TLV3201 | 4 |
| Régulateurs linéaires | 7805, 7812, 7815, 7905, LM317, LM337, AMS1117-3.3, LDO 3,3 V | 8 |
| Références | TL431, LM4040-2.5, REF3030 | 3 |
| Divers | NE555 (mixte), PC817, 4N35 | 3 |

Hors périmètre v1 : alimentations à découpage, drivers de grille, ADC/DAC, microcontrôleurs, modèles RF.

## 3. Sources de modèles et licences

**Décision de Régis (28/11/2026)** : utiliser les modèles officiels des fabricants plutôt que des modèles calés par
Silx, avec une fenêtre qui prévient l'utilisateur que l'usage de ces modèles est sous sa responsabilité.

Ce que cela implique :

- **Télécharger à la demande, ne pas redistribuer.** La fenêtre d'avertissement couvre l'usage par l'utilisateur,
  pas la redistribution : si les fichiers fabricant sont livrés dans Silx.app ou dans le dépôt, c'est Silx qui les
  redistribue, et beaucoup de licences fabricant ne l'autorisent pas explicitement. Silx livre donc un **catalogue**
  (référence, fabricant, description, brochage, symbole, lien vers la page du modèle et la datasheet) et le fichier
  du modèle est récupéré sur le site du fabricant au moment où l'utilisateur en a besoin, puis gardé dans sa
  bibliothèque locale. Le dépôt GitHub ne contient aucun fichier fabricant ; les tests CI les téléchargent.
- **Fenêtre d'avertissement** à la première utilisation d'un modèle fabricant (et rappel par fabricant) : modèle
  tiers, conditions du fabricant, responsabilité de l'utilisateur, lien vers la licence d'origine. Le choix est
  mémorisé.
- **Modèles chiffrés** : certains AOP, régulateurs et références ne sont publiés que chiffrés (PSpice, LTspice).
  ngspice ne peut pas les lire. Pour ces références, Silx propose le composant générique le plus proche (AOP
  générique paramétrable, Zener paramétrable…) et le signale.
- **Téléchargement automatique pas toujours possible** : certaines pages demandent une connexion, une acceptation
  de licence ou servent une archive ; les liens changent. Repli : Silx ouvre la page du fabricant et l'utilisateur
  importe le fichier téléchargé (import déjà en place). Le catalogue est vérifié en CI (liens morts, empreinte du
  fichier changée).
- **Compatibilité ngspice** : les modèles écrits pour PSpice ou LTspice ne passent pas tous, même en mode `psa`.
  Chaque référence du catalogue est testée ; une référence qui ne passe pas est retirée ou reçoit un correctif
  appliqué localement après téléchargement (sans redistribuer le fichier).
- Les composants intégrés actuels (`BibliothequeBase`, `DiodeModels`) restent comme modèles génériques de repli.

## 4. Travail par composant

Pour chaque référence :

1. **Modèle** : trouver le modèle officiel, noter l'URL, la licence et l'empreinte du fichier, vérifier qu'il n'est
   pas chiffré et qu'il tourne sous ngspice (`psa`), écrire un correctif local si besoin.
2. **Symbole** : choisir le gabarit (aucun dessin si le gabarit existe).
3. **Brochage** : relier les broches du modèle aux rôles du symbole ; ordre des broches variable selon le fabricant
   pour les AOP et régulateurs ; alimentations éventuellement cachées.
4. **Validation** : banc automatique qui simule 2 à 4 grandeurs de la datasheet et les compare (tolérances de
   `generation_modeles.md` §6) : Vf à If, Vz à Iz, hFE à Ic, VGS(th), Rds(on), GBW, SR, excursion de sortie,
   Vout et chute de tension d'un régulateur, etc.
5. **Métadonnées** : description, fabricant, boîtier, lien datasheet, statut (indicatif ou validé).

Charge unitaire, outillage en place :

| Type | Charge |
|---|---|
| Diode, Zener, LED, bipolaire ou JFET petit signal | 0,25 à 0,5 j |
| MOSFET de puissance, Darlington | 0,5 à 1 j (capacités non linéaires, Rds(on) en température) |
| AOP, comparateur, régulateur avec modèle fabricant utilisable | 0,5 à 1 j (compatibilité ngspice, broches, validation) |
| Modèle chiffré ou incompatible : repli sur un générique | 0,25 j par référence (une fois le générique écrit) |

## 5. Outillage à créer (lot 0)

| Brique | Charge |
|---|---|
| Format de catalogue hors code (manifeste JSON par famille : référence, fabricant, URL, licence, empreinte, brochage, symbole, statut), embarqué en ressource | 2 à 3 j |
| Téléchargement à la demande, cache local, repli « ouvrir la page du fabricant puis importer », correctifs locaux | 3 à 4 j |
| Fenêtre d'avertissement (responsabilité de l'utilisateur, licence d'origine, choix mémorisé) | 1 j |
| Nouveaux gabarits : Zener, Schottky, LED, AOP (5 broches, alimentations masquables), comparateur, régulateur 3 broches, référence shunt, optocoupleur | 3 à 4 j |
| Catégories palette (AOP, comparateurs, régulateurs, références…), filtres et recherche adaptés à 100+ références | 2 j |
| Banc de validation générique par famille + rapport, exécuté en CI (modèles téléchargés pendant le test), avec contrôle des liens | 5 à 7 j |
| Import de fichiers fabricant : détection des modèles chiffrés, brochage par commentaires (existe en partie) | 1 à 2 j |
| Composants génériques de repli : AOP paramétrable, Zener (Vz), LED (couleur → Vf) | 2 à 3 j |

Le banc de validation est le même que celui de `generation_modeles.md` §6 : l'investissement sert aux deux chantiers.

## 6. Estimation en lots

| Lot | Contenu | Références | Charge |
|---|---|---|---|
| 0 | Outillage commun (§5) | | 19 à 26 j |
| 1 | Discrets : diodes, Schottky, Zener, LED, bipolaires, MOSFET, JFET | ~50 | 15 à 20 j |
| 2 | AOP et comparateurs | ~16 | 9 à 13 j |
| 3 | Régulateurs, références, 555, optocoupleurs | ~14 | 9 à 13 j |
| **Total** | | **~100** | **52 à 72 j** |

Par rapport à des modèles calés par Silx, on gagne sur le calage des intégrés mais on ajoute le téléchargement, le
suivi des liens et les tests de compatibilité. Les jours sont des jours de travail équivalents d'un ingénieur ;
avec l'agent, l'outillage et les bancs vont nettement plus vite, pas la vérification des modèles un par un.

Principales incertitudes : la part de modèles chiffrés ou incompatibles avec ngspice, et la part de fabricants dont
le téléchargement ne peut pas être automatisé.

## 7. Recommandation

Commencer par le lot 0, puis un premier tir sur une vingtaine de références très courantes, choisies chez plusieurs
fabricants (1N4148, BAT54, BZX79, BC547, 2N7000, IRLZ44N, LM358, TL072, NE5532, LM393, LM317, 7805, TL431…), pour
mesurer tôt la part de modèles chiffrés ou non téléchargeables avant de chiffrer définitivement les lots 1 à 3.
Environ 25 à 32 j pour ce premier jalon.

À faire valider avant diffusion publique : que télécharger à la demande depuis le site du fabricant, sans
redistribuer, suffit au regard des conditions des principaux fabricants retenus.
