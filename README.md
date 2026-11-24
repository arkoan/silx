# Silx

Logiciel de simulation électronique mixte (analogique et numérique), destiné aux ingénieurs de bureau d'étude. Socle de calcul : ngspice ; mesures et gabarits pass/fail en C#/.NET.

## Dossiers prévus

- `mesures/` : bibliothèque de mesures et gabarits pass/fail, avec tests de non-régression (C#/.NET).
- `app/` : application (éditeur de schéma Avalonia + simulation ngspice + gabarits), issue du prototype `silx-proto`.
- `socle-ngspice/` : socle de simulation (netlist, exécution ngspice, lecture .raw).
