# Assistant Silx : Skills d'électronique et serveur MCP

Ce dossier est un **plugin Claude Code** : il déclare le serveur MCP de Silx (`silx-mcp`) et 22 Skills, des fiches de
savoir-faire que Claude charge seulement quand la demande les concerne. Claude simule, mesure et vérifie avec Silx ;
les Skills lui disent comment un électronicien s'y prend. Liste complète, priorités et outils manquants :
`docs/specs/skills.md`.

## Installer

Prérequis : Silx installé dans `/Applications` (le serveur `silx-mcp` est livré dans le paquet).

**Claude Code**, depuis le dépôt GitHub :

```
/plugin install silx --marketplace arkoan/silx
```

ou, depuis une copie locale du dépôt (essai, développement) :

```bash
claude --plugin-dir ./assistant
```

**Claude Desktop** : déclarez le serveur MCP (voir `mcp/README.md`), puis importez chaque Skill voulue : zippez son
dossier (`assistant/skills/<nom>/`) et ajoutez-le dans les réglages de Claude Desktop, section des Skills.

**Autre emplacement** : un dossier de Skill se copie tel quel dans `~/.claude/skills/` (toutes vos sessions Claude
Code) ou dans `.claude/skills/` d'un projet.

Avec l'application Silx ouverte, le serveur passe en **mode direct** : vous voyez Claude travailler dans vos onglets
(voir `mcp/README.md`).

## Skills livrées

| Famille | Skills |
|---|---|
| Socle | `piloter-silx`, `spice-bonnes-pratiques` |
| Spécifier et vérifier | `regler-campagne` |
| Analyser | `diagnostiquer-banc-en-echec`, `analyser-sensibilite`, `verifier-polarisation`, `analyser-bruit`, `expliquer-un-circuit`, `revue-de-schema` |
| Restituer | `rapport-de-test` |
| RF | `adaptation-impedance`, `etage-rf-petit-signal`, `filtres-et-oscillateurs-rf` |
| Audio | `amplificateur-audio`, `filtres-audio-et-egalisation`, `mesures-audio` |
| Capteurs | `conditionnement-capteur`, `transimpedance-photodiode`, `chaine-acquisition-adc` |
| Instrumentation | `incertitudes-et-budget-d-erreur`, `bonnes-pratiques-instrumentation`, `mesurer-sans-perturber` |

`verifier-polarisation` et `analyser-bruit` utilisent `get_operating_point` et `get_noise` (PR 12) ; sans eux, elles
disent comment faire autrement.

## Écrire ou modifier une Skill

- Un dossier par Skill, nommé comme le champ `name` de son `SKILL.md` (minuscules, sans accents, tirets).
- La `description` décide du déclenchement : elle dit quand utiliser la Skill, avec les mots de l'utilisateur.
- Une Skill décrit une démarche et des pièges ; elle ne contient pas de valeur à recopier dans une réponse. Les
  nombres viennent des outils (simulation, mesure, calcul) ; un outil qui manque est signalé, avec la façon de faire
  sans lui.
- Les règles qui valent pour chaque réponse (aucun nombre non tracé, confirmation par simulation) ne sont pas dans
  les Skills mais dans les consignes du serveur (`mcp/src/Silx.Mcp/Instructions.cs`).
- Vérification : `claude plugin validate ./assistant`.
