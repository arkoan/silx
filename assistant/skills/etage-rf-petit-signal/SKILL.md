---
name: etage-rf-petit-signal
description: Analyser ou concevoir un étage RF petit signal dans Silx - LNA, ampli RF, étage tampon, gain, stabilité inconditionnelle (facteur K de Rollett), facteur de bruit (NF), compression à 1 dB, IP3, parasites de boîtier. À utiliser pour les amplificateurs de quelques MHz à quelques centaines de MHz.
---

# Étage RF petit signal

## Démarche

1. **Polarisation** : `verifier-polarisation`. Le gain, le bruit et la linéarité dépendent du courant de repos.
2. **Gain et adaptation** : banc AC avec source et charge à l'impédance du système (souvent 50 Ω), gain transducique
   lu en dB (voir `adaptation-impedance` pour la référence −6,02 dB).
3. **Stabilité** : un ampli RF peut osciller hors de sa bande, sur une charge qu'on ne lui présente pas au banc.
   - Critère de Rollett : K > 1 et |Δ| < 1 sur **toute** la bande où le transistor a du gain (pas seulement la bande
     utile). Il demande les paramètres S, que le serveur ne fournit pas encore (`get_s_parameters`).
   - En attendant : balayage de la charge et de la source (résistances et réactances, `run_campaign`) en cherchant un
     pic de gain anormal, et transitoire après une impulsion pour voir une oscillation qui démarre. C'est une
     indication, pas une preuve de stabilité inconditionnelle ; dites-le.
   - Remèdes classiques : résistance série dans la base ou la grille, résistance de stabilisation en sortie,
     contre-réaction résistive, découplage soigné de l'alimentation.
4. **Facteur de bruit** : `get_noise` (Skill `analyser-bruit`) avec la résistance de source dans le circuit. Le
   facteur de bruit F compare le bruit ramené à l'entrée au seul bruit thermique de la résistance de source
   (F = bruit total ramené à l'entrée² / 4kTRs, NF = 10·log F) ; calcul par outil de calcul quand il existe.
5. **Linéarité** : compression à 1 dB par transitoire sinus à amplitude croissante (gain lu sur l'amplitude de
   sortie en régime établi) ; IP3 par essai à deux tons, qui demande les raies du spectre (`get_spectrum`, manquant).
   Si `run_campaign` ne sait pas balayer l'amplitude d'un sinus, faites quelques simulations en changeant
   l'amplitude, ou demandez à l'utilisateur de la régler.

## Pièges

- Les modèles indicatifs de la palette n'ont pas de parasites de boîtier : au-delà de quelques dizaines de MHz,
  utilisez le modèle RF du fabricant (Skill `choisir-et-importer-modele` quand elle sera disponible).
- L'analyse AC est linéarisée : elle ne dit rien de la compression.
- Un découplage d'alimentation idéal dans le schéma cache les oscillations par l'alimentation.

## Conclure

Point de repos, gain et bande (simulés), stabilité (avec le niveau de preuve atteint), NF, compression, limites des
modèles.
