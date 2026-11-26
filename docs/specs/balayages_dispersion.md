# Balayages et dispersion — spécification

Question à laquelle répond la fonction : **« mon circuit reste-t-il conforme avec des composants réels ? »**,
plutôt que de tester une seule valeur nominale. Validé par Régis le 25/11/2026 ; livré dans l'ordre
balayage, Monte-Carlo, coins.

## 1. Trois modes, réglés dans la roue crantée du banc

La bulle « Banc de test » (roue crantée de la pastille) a une section **Campagne** avec quatre choix :
Nominal (défaut, une seule simulation), Balayage, Monte-Carlo, Coins. Le réglage est enregistré dans le
projet (`BancF.Campagne`, absent du fichier en mode nominal).

### 1.1 Balayage paramétrique
- Paramètre : une résistance, un condensateur, une inductance, une source continue (DC) du circuit ou du
  banc (désigné par sa référence), ou la température (`TEMP`, en °C, écrite `.options temp=…`).
- Plage : linéaire (de, à, nombre de points), logarithmique, ou liste de valeurs (« 1k 2,2k 4,7k »).
- Une simulation par valeur. Les courbes des signaux sondés se superposent **en dégradé de couleur**
  (Viridis) sous la courbe nominale.
- La vue « Campagne » montre chaque mesure chiffrée (temps de montée, fc…) **en fonction du paramètre**,
  avec les bornes du critère en tirets rouges et les points en échec en rouge.

### 1.2 Monte-Carlo
- N tirages (100 par défaut), loi **gaussienne** (± tolérance = 3 σ, tronquée à ± tolérance) ou
  **uniforme** (± tolérance).
- Chaque composant qui a une tolérance est tiré indépendamment. **Graine** réglable : même graine, mêmes
  tirages (reproductibilité, rejeu d'un tirage).
- Résultat : histogramme par mesure (classes ≈ √N, entre 5 et 30), moyenne, écart-type, extrêmes, **Cpk**
  (bilatéral si le critère a deux bornes, unilatéral sinon), nombre d'échecs.
- *Plus tard* : tirages corrélés par lot (même lot de résistances), combinaison Monte-Carlo × température.

### 1.3 Coins (pire cas)
- Chaque composant tolérant prend sa valeur mini ou maxi ; la température (mini, maxi, réglables,
  −40 / 85 °C par défaut) peut s'ajouter comme une dimension.
- Jusqu'à 10 dimensions : toutes les combinaisons (2ⁿ, soit 1024 au plus). Au-delà : tout au mini, tout au
  maxi, puis chaque paramètre seul à ses extrêmes (2n + 2), avec un avertissement.
- Silx désigne le **coin le plus défavorable** : le plus de contraintes franchies, puis la plus petite
  marge relative.

## 2. Tolérances

- Saisie dans la bulle d'édition du composant (et le panneau de propriétés) : champ « Tolérance (± %) ».
  Vide = tolérance par défaut du type. Enregistrée dans le projet (`ComposantF.Tolerance`).
- Défauts par type dans **Paramètres › Tolérances** : résistances ± 1 %, condensateurs ± 10 %,
  inductances ± 10 %, sources continues 0 % (valeur exacte : alimentation régulée).
- Seuls les composants à valeur numérique sont dispersés : R, C, L, sources DC. Les modèles importés, les
  stimuli (impulsion, sinus) et les composants logiques gardent leur valeur.
- *Plus tard* : valeurs mini / maxi de datasheet pour les paramètres de modèle (Vth d'un MOSFET, β…).

## 3. Résultats intégrés aux tests

- **Plan de test** : l'en-tête affiche le taux de conformité (« Monte-Carlo : 96,7 % conforme (58/60) »).
  Chaque critère montre μ, σ, extrêmes, Cpk et nombre d'échecs (balayage : plage de la mesure et
  échecs) ; chaque gabarit dessiné, le nombre de tirages qui le franchissent. Le badge d'une contrainte
  passe en échec dès qu'un tirage la franchit ; le verdict global de la barre d'outils aussi.
- **Courbes** : les tirages sont dessinés sous les courbes nominales (gris translucide si conforme, rouge
  si non conforme ; dégradé pour un balayage), 400 tirages au plus.
- **Vue « Campagne »** (tiroir Détails) : un graphique par critère chiffré (histogramme ou mesure selon le
  paramètre) et la liste des tirages, non conformes d'abord puis du plus défavorable au plus sûr, avec le
  pire cas signalé. **Un clic sur un tirage** met ses courbes en avant (bleu, épais) et affiche les valeurs
  des composants qui l'ont produit.
- **Rapport de test** : pour chaque banc avec campagne, le taux de conformité, la loi et la graine, un
  tableau (moyenne, écart-type, min, max, Cpk, échecs), le pire cas avec ses valeurs et une image par
  mesure (histogramme ou mesure selon le paramètre).

## 4. Fonctionnement

- « Simuler » lance d'abord la simulation nominale (affichée tout de suite), puis la campagne. La
  simulation automatique (après une modification) ne relance **que** le nominal ; une modification du
  circuit, d'une tolérance ou du réglage de campagne efface les résultats de campagne (« à relancer »).
- Exécution **en parallèle** sur les cœurs du Mac (un processus ngspice par tirage, au plus un par cœur).
  Barre de progression et bouton **Annuler** dans la barre d'état ; l'annulation arrête les processus.
- Mémoire : seuls les **signaux sondés** (graphiques du banc et nœuds des contraintes) sont gardés,
  sous-échantillonnés à 1500 points par courbe, avec les mesures et les verdicts de chaque tirage. Les
  fichiers bruts de ngspice ne sont pas conservés.
- Les gabarits relatifs au front sont recalés une fois avant la campagne.

## 5. Mise en œuvre

| Élément | Où |
| --- | --- |
| Modèle (`Campagne`, `Tirage`, `ResultatTirage`, `StatMesure`, `ResultatCampagne`), plan des tirages, exécution parallèle, statistiques | `app/src/Silx.Etude/Campagne.cs` |
| Variante de schéma et directives par simulation | `Verification.RunAsync(…, variante, directives)` |
| Tolérance d'un composant | `PlacedComponent.Tolerance`, `Projet.ComposantF.Tolerance` |
| Interface : réglages, plan de test, courbes, vue Campagne | `app/src/Silx.App/MainWindow.Campagne.cs` |
| Défauts de tolérance | `Parametres` (section Tolérances de la fenêtre Paramètres) |
| Rapport | `MainWindow.Rapport.cs`, `SectionCampagne` |
| Tests | `CampagneTests.cs` (moteur, ngspice réel), `UiTests.Campagnes_monte_carlo_et_balayage_dans_l_interface` |

## 6. Hors périmètre de cette version

- Analyse de sensibilité (quel composant influence le plus une mesure) : les données des tirages le
  permettront (régression sur les tirages Monte-Carlo).
- Combinaison de plusieurs dimensions de balayage (balayage × Monte-Carlo).
- Tirages corrélés par lot, paramètres de modèle dispersés.
