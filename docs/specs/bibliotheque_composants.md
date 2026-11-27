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

C'est le point dur, plus que le volume.

1. **Modèles fabricant officiels** (TI, ADI, onsemi, Nexperia, Infineon, Vishay…) : les plus fiables, mais les
   conditions d'utilisation varient et beaucoup n'autorisent pas explicitement la redistribution dans un logiciel
   tiers, ou n'en disent rien. Certains AOP et régulateurs ne sont livrés que **chiffrés** (PSpice, LTspice) et sont
   inutilisables avec ngspice. À vérifier fabricant par fabricant, et à faire valider avant toute diffusion.
2. **Modèles « génériques » qui circulent partout** (2N3904, 1N4148…) : souvent issus d'anciennes bibliothèques
   fabricant ou éditeur ; risque faible mais pas nul, origine souvent introuvable.
3. **Modèles calés par Silx** à partir des valeurs publiées dans la datasheet (Vf à If, hFE à Ic, GBW, SR…) :
   aucun problème de redistribution, mais travail de calage et précision limitée aux points calés.
4. **Catalogue de liens** : Silx connaît la référence, son brochage et son symbole, mais télécharge (ou fait
   télécharger) le modèle officiel chez le fabricant et l'importe localement. Pas de redistribution.

Proposition : intégrer en dur les sources 3 (et 2 quand l'origine est claire), et passer par la source 4 pour les
circuits intégrés dont le modèle fabricant est sous licence restrictive. C'est cohérent avec la priorité au modèle
fabricant de `generation_modeles.md` §7.3.

## 4. Travail par composant

Pour chaque référence :

1. **Modèle** : récupérer ou écrire le `.model`/`.subckt`, vérifier qu'il tourne sous ngspice (`psa`), noter
   provenance et licence.
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
| AOP ou comparateur avec modèle fabricant utilisable | 0,5 à 1 j (compatibilité ngspice, broches, validation) |
| AOP, régulateur ou référence à modéliser soi-même | 1 à 2 j |

## 5. Outillage à créer (lot 0)

| Brique | Charge |
|---|---|
| Format de catalogue hors code (manifeste JSON par famille, métadonnées, provenance, licence, statut) embarqué en ressource, à la place du texte en dur de `BibliothequeBase` | 2 à 3 j |
| Nouveaux gabarits : Zener, Schottky, LED, AOP (5 broches, alimentations masquables), comparateur, régulateur 3 broches, référence shunt, optocoupleur | 3 à 4 j |
| Catégories palette (AOP, comparateurs, régulateurs, références…), filtres et recherche adaptés à 100+ références | 2 j |
| Banc de validation générique par famille + rapport, exécuté en CI sur toute la bibliothèque | 5 à 7 j |
| Import en lot de fichiers fabricant : contrôle ngspice, détection des modèles chiffrés, brochage par commentaires | 2 à 3 j |
| Composants paramétrables : AOP générique, Zener (Vz), LED (couleur → Vf) | 2 j |
| Option : catalogue de liens vers les modèles officiels, téléchargement et import en un clic | 3 à 5 j |

Le banc de validation est le même que celui de `generation_modeles.md` §6 : l'investissement sert aux deux chantiers.

## 6. Estimation en lots

| Lot | Contenu | Références | Charge |
|---|---|---|---|
| 0 | Outillage commun (§5, sans l'option) | | 16 à 21 j |
| 1 | Discrets : diodes, Schottky, Zener, LED, bipolaires, MOSFET, JFET | ~50 | 18 à 24 j |
| 2 | AOP (dont générique) et comparateurs | ~16 | 12 à 16 j |
| 3 | Régulateurs, références, 555, optocoupleurs | ~14 | 12 à 16 j |
| 4 | Option : catalogue de liens fabricant | | 3 à 5 j |
| **Total** | | **~100** | **60 à 80 j** (lots 0 à 3) |

Les jours sont des jours de travail équivalents d'un ingénieur. Avec l'agent, l'outillage, les symboles et l'écriture
des bancs vont nettement plus vite ; ce qui ne se compresse pas, c'est la vérification des licences, le test des
modèles fabricant sous ngspice et le contrôle des écarts avec la datasheet.

Principales incertitudes : la part de modèles fabricant chiffrés ou incompatibles avec ngspice (chaque modèle à
réécrire coûte 1 à 2 j au lieu de 0,5), et la réponse sur la redistribution.

## 7. Recommandation

Commencer par le lot 0 réduit (catalogue, gabarits, banc de validation) puis un premier tir à forte valeur :
Zener, Schottky, LED et **AOP générique paramétrable**, qui couvre la plupart des avant-projets sans dépendre d'un
modèle fabricant. Environ 25 à 30 j pour ce premier jalon, puis les lots 1 à 3 au fil de l'eau.
