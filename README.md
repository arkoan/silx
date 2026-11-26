# Silx

Logiciel de simulation électronique mixte (analogique et numérique), destiné aux ingénieurs de bureau d'étude. Socle de calcul : ngspice ; mesures et gabarits pass/fail en C#/.NET.

## Dossiers prévus

- `mesures/` : bibliothèque de mesures et gabarits pass/fail, avec tests de non-régression (C#/.NET).
- `app/` : application (éditeur de schéma Avalonia + simulation ngspice + gabarits), issue du prototype `silx-proto`.
- `socle-ngspice/` : socle de simulation (netlist, exécution ngspice, lecture .raw).

## Télécharger Silx (macOS, Apple Silicon)

- **Version publiée** : onglet *Releases* du dépôt, fichier `Silx-<version>-osx-arm64.dmg` (créé à chaque étiquette `vX.Y.Z`).
- **Dernier build** : onglet *Actions* › workflow *build* › dernier run réussi › artefact `Silx-macos-arm64`.

Ouvrir le `.dmg`, glisser **Silx** dans **Applications**. L'application n'est pas notarisée par Apple : au premier
lancement, clic droit sur Silx › **Ouvrir**, puis confirmer. ngspice est inclus (aucune installation de .NET ni de
ngspice nécessaire).

Construire localement : `app/packaging/paquet-macos.sh` (ngspice du système), ou avec un ngspice compilé par
`app/packaging/ngspice-construire.sh` : `NGSPICE_DIR=<dossier> app/packaging/paquet-macos.sh`.
