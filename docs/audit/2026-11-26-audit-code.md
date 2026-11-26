# Audit du code et gestion des erreurs — 26/11/2026

Périmètre : `app/` (Silx.Etude, Silx.App), `socle-ngspice/`, `mesures/`. Branche `chore/audit-erreurs`.

## 1. État général

| Indicateur | Avant | Après |
| --- | --- | --- |
| Avertissements de compilation | 0 | 0 |
| Tests (app / socle / mesures) | 151 / 11 / 49 | 155 / 11 / 49 |
| Plus gros fichier | `MainWindow.cs`, 2 590 lignes | `MainWindow.cs`, ~930 lignes |
| Tâches lancées sans être attendues (`_ = …Async()`) | 10 | 0 |
| Fermeture de l'application sur une erreur imprévue | possible | non (filets ci-dessous) |

Points sains : pas d'avertissement, `Nullable` activé partout, enregistrement de projet atomique (fichier
temporaire puis remplacement), processus ngspice tués à l'annulation, mesures validées par tests, `.First()` tous
gardés (groupes non vides, effectifs vérifiés).

## 2. Constats et corrections

| # | Constat | Risque | Correction |
| --- | --- | --- | --- |
| E1 | Actions asynchrones lancées par `_ = XAsync()` (ouvrir, enregistrer, simuler, rapport, éditeur) : une erreur y était **perdue** | erreur silencieuse | `Lancer(action, où)` : attend la tâche, signale l'erreur dans la barre d'état et le journal ; annulation silencieuse |
| E2 | Aucune erreur hors interface n'était journalisée (tâche oubliée, thread) | perte de diagnostic | `Program` : `AppDomain.UnhandledException` et `TaskScheduler.UnobservedTaskException` → journal |
| E3 | Erreurs d'interface (tracé, événements) : fermaient l'application (corrigé le 26/11 pour le tracé) | plantage | filet `Dispatcher.UnhandledException` + tracé protégé par graphique ; journal unique `Journal.Ecrire` (~/Library/Logs/Silx/silx.log) |
| E4 | ngspice absent : message système en anglais (« An error occurred trying to start process ») | incompréhensible | message clair : « ngspice introuvable … brew install ngspice … SILX_NGSPICE » |
| E5 | Simulation sans fin (circuit instable, pas minuscule) : interface « Calcul… » indéfiniment | blocage | durée maximale (5 min, SILX_DELAI_SIMULATION) ; ngspice tué ; message « Simulation arrêtée au bout de … » |
| E6 | Projet abîmé (champ nul, JSON tronqué) : `NullReferenceException` non prévue par l'ouverture | plantage / message obscur | `Projet.DepuisJson` ne lève plus qu'une `InvalidDataException` au message clair |
| E7 | Import d'un fichier de modèles illisible ou énorme : exception brute | message obscur, mémoire | lecture protégée (droits, E/S) et refus au-delà de 50 Mo |
| E8 | Analyse AC qui ne donne qu'un point (bande étroite en décade) : plantage (corrigé le 26/11) | plantage | ≥ 100 points garantis, message si un seul point |
| S1 | `MainWindow.cs` de 2 590 lignes | maintenance | découpé en classes partielles : `Courbes`, `Panneaux`, `Gabarits`, `Simulation`, `Erreurs` (+ `Campagne`, `Commandes`, `Explorateur`, `Rapport` existants) |

Tests ajoutés : ngspice absent et durée maximale (sans toucher aux autres tests : remplacements `AsyncLocal`),
projets abîmés (3 cas).

## 3. Dette restante (non bloquante)

- **Interface construite en C# sans liaison de données** : chaque panneau se reconstruit entièrement. Simple et testé,
  mais verbeux ; un passage progressif à des vues-modèles faciliterait les évolutions (pas urgent).
- **Tests d'interface headless** : ils ne couvrent ni les vrais gestes (pincement) ni le placement des info-bulles ;
  une vérification manuelle reste nécessaire pour ces points.
- **`MainWindow.Courbes.cs` (~900 lignes)** : candidat à une classe `VueCourbes` distincte si la zone des courbes
  grossit encore.
- **Bibliothèque de composants** : un fichier `.silxcomp` illisible est ignoré sans prévenir ; à signaler dans la
  barre d'état au démarrage.
- **ngspice** : chemin cherché dans SILX_NGSPICE puis Homebrew ; l'application installable (étape suivante)
  l'embarquera.
