# Legacy Prediction/Reconciliation Code

**Date de dépréciation:** 2026-02-02

## Raison

Ce code faisait partie d'une architecture **FPS-style avec prédiction client** qui n'a jamais été utilisée dans le projet. L'architecture actuelle utilise un modèle **LoL-style** (server-authoritative) sans prédiction côté client.

## Fichiers déplacés

### Scripts

- **ClientClockSync.cs** - Ajustement du tick rate client pour optimiser le buffer d'inputs (jamais utilisé)
- **VisualOffsetCorrector.cs** - Système de correction visuelle avec offset (déprécié en V4.0)
- **Backup/** - Anciens backups de CommandReconciliation

### Documentation

- **PREDICTION_RECONCILIATION.md** - Documentation technique sur la prédiction/réconciliation FPS-style
- **VisualSmoothing.md** - Documentation sur le système de lissage visuel avec offset

## Constantes supprimées de NetcodeConstants.cs

### Clock Synchronization (non utilisées)
- `TARGET_INPUTS_IN_FLIGHT = 2`
- `INPUT_BUFFER_TOLERANCE = 1`
- `CLOCK_SYNC_AGGRESSION = 0.1f`
- `MAX_TICK_RATE_ADJUSTMENT = 0.2f`

### Reconciliation (legacy)
- `SOFT_RECONCILE_TICKS = 2`
- `HARD_RECONCILE_TICKS = 8`
- `MAX_REPLAY_TICKS = 16`
- `SOFT_RECONCILE_THRESHOLD` (calculé)
- `HARD_RECONCILE_THRESHOLD` (calculé)
- `RECONCILE_THRESHOLD` (alias)

### Visual Smoothing (déprécié)
- `POSITION_SMOOTHING_K = 20f` (remplacé par valeur hardcodée dans EntityView)
- `ROTATION_SMOOTHING_K = 10f` (remplacé par valeur hardcodée dans EntityView)

### Input Transport
- `INPUT_SEND_RATE = TICK_RATE` (non utilisé)

## Constantes conservées (utilisées)

- `INPUT_REDUNDANCY_COUNT = 3` - Utilisé par InputBuffer et NetworkClient
- `INPUT_BUFFER_SIZE = 8` - Utilisé par InputBuffer

## Architecture actuelle (LoL-style)

Le projet utilise maintenant une architecture **pure server-authoritative**:
- Pas de simulation client (pas de SimWorld local)
- Pas de prédiction côté client
- Pas de reconciliation/rollback
- `visualPos = basePos` (interpolation directe depuis les snapshots serveur)
- Input envoyé au serveur, serveur simule et renvoie les snapshots

Voir **LOL.md** et **LOL_STYLE_NETCODE.md** pour la documentation actuelle.

## Restauration

Si vous avez besoin de restaurer ces fichiers:
```bash
cp Backups/Legacy_Prediction_Reconciliation/Scripts/*.cs Assets/Scripts/[destination]/
```

**Note:** Ces fichiers nécessiteront des modifications pour fonctionner avec la codebase actuelle.
