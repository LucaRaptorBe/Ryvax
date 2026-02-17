# Prediction & Reconciliation - Documentation Technique

## Vue d'ensemble

Ce document décrit l'implémentation du mécanisme de **Client-Side Prediction** et **Server Reconciliation** dans Ryvax. Cette architecture est inspirée des standards de l'industrie (Overwatch, League of Legends, Valorant).

### Objectifs
- **Zéro input lag** : Le joueur voit ses actions immédiatement
- **Autorité serveur** : Le serveur reste la source de vérité
- **Correction invisible** : Les corrections de désync sont imperceptibles

> **Note d'implémentation:** Cette architecture est maintenant implémentée avec UDP + input redundancy.
> Les fichiers clés: `InputPacket.cs`, `InputBuffer.cs`, modifications dans `NetworkClient.cs` et `FishNetAdapter.cs`.

---

## Architecture Générale

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              CLIENT                                      │
├─────────────────────────────────────────────────────────────────────────┤
│  InputCollector                                                          │
│  ├─ Sample WASD → IsMoving + MoveDirection (état continu)               │
│  └─ Capture Jump/Abilities → GameCommand (événements discrets)          │
│                        │                                                 │
│                        ▼                                                 │
│  NetworkClient.SendInputUpdate() @ 30Hz                                  │
│  ├─ 1. Créer InputPacket (MovementSeq, IsMoving, MoveDir, Events[])     │
│  ├─ 2. Appliquer mouvement localement (prédiction)                      │
│  ├─ 3. RecordMovementState() pour réconciliation                        │
│  └─ 4. Envoyer au serveur (UDP unreliable)                              │
│                        │                                                 │
│                        ▼                                                 │
│  CommandReconciliation                                                   │
│  ├─ Buffer commandes événements en attente d'ACK                        │
│  └─ Track _lastSentMovementSeq pour réconciliation mouvement            │
└─────────────────────────────────────────────────────────────────────────┘
                         │
                         │ InputPacket (UDP unreliable, semi-stateless)
                         ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                              SERVER                                      │
├─────────────────────────────────────────────────────────────────────────┤
│  ServerGameLoop.OnMovementStateReceived()                                │
│  ├─ 1. Monotonic check (rejeter out-of-order)                           │
│  ├─ 2. Appliquer état mouvement comme "vérité actuelle"                 │
│  └─ 3. Tracker _lastMovementSeq[clientId]                               │
│                        │                                                 │
│  ServerGameLoop.OnCommandReceived() (événements discrets)                │
│  └─ Exécuter jump/spells dans SimWorld                                  │
│                        │                                                 │
│                        ▼                                                 │
│  BroadcastSnapshots() @ 30Hz                                             │
│  └─ SnapshotDelta { ServerTick, AckInputSeq, AckMovementSeq, Entities[] }│
└─────────────────────────────────────────────────────────────────────────┘
                         │
                         │ SnapshotDelta (UDP unreliable)
                         ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      CLIENT (Réconciliation)                             │
├─────────────────────────────────────────────────────────────────────────┤
│  CommandReconciliation.OnSnapshotReceived()                              │
│  ├─ 1. Update _lastAckedMovementSeq                                     │
│  ├─ 2. Check pendingMovement = (sent > acked)                           │
│  ├─ 3. Si pendingMovement → skip velocity override                      │
│  ├─ 4. Sinon → appliquer vélocité serveur (soft correction)             │
│  └─ 5. Si erreur > hard threshold → Reconcile() complet                 │
│                                                                          │
│  Reconcile() (hard):                                                     │
│  ├─ Revert au state serveur                                             │
│  ├─ Rejouer commandes événements non-ACK'd                              │
│  └─ Re-simuler ticks jusqu'au tick prédit                               │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Fichiers Clés

| Fichier | Rôle |
|---------|------|
| `Assets/Scripts/Client/Prediction/CommandReconciliation.cs` | Cœur du système de prédiction/réconciliation + mouvement |
| `Assets/Scripts/Client/Input/InputCollector.cs` | Sample WASD → état mouvement + événements discrets |
| `Assets/Scripts/Core/NetworkClient.cs` | Envoi InputPacket, tracking MovementSeq |
| `Assets/Scripts/Server/ServerGameLoop.cs` | Réception mouvement semi-stateless + broadcast snapshots |
| `Assets/Scripts/Client/Interpolation/InterpolationBuffer.cs` | Interpolation des autres joueurs |
| `Assets/Scripts/Network/Shared/NetcodeConstants.cs` | Configuration centralisée |
| `Assets/Scripts/Network/NetAdapter/Messages/InputPacket.cs` | Format semi-stateless (MovementSeq, IsMoving, Events[]) |
| `Assets/Scripts/Network/NetAdapter/Messages/SnapshotDelta.cs` | Snapshot avec AckInputSeq + AckMovementSeq |
| `Assets/Scripts/Network/NetAdapter/Messages/GameCommand.cs` | Format des événements discrets (Jump, Spell) |
| `Assets/Scripts/Network/NetAdapter/SnapshotHelper.cs` | Création snapshots avec ackMovementSeq |
| `Assets/GameSim/Commands/CommandDispatcher.cs` | Routage des commandes événements |
| `Assets/GameSim/Commands/Handlers/MovementHandler.cs` | Exécution du mouvement |

---

## Flux Détaillé

### 1. Envoi de Commande (Client → Server)

**Fichier:** `InputCollector.cs`

```csharp
// Collecte des inputs WASD
Vector2 input = SampleWASD();
bool isMoving = input.sqrMagnitude > 0.01f;

// Envoyer si:
// - État change (start/stop)
// - Direction change (> 0.1 magnitude)
// - Heartbeat (toutes les 200ms pendant mouvement)
if (stateChanged || directionChanged || heartbeatNeeded)
{
    var cmd = isMoving
        ? GameCommand.MoveStart(++_commandSequence, input)
        : GameCommand.MoveStop(++_commandSequence);

    _networkClient.SendCommand(cmd);
}
```

**Format GameCommand (14 bytes):**
```
┌──────────┬──────────┬────────┬───────┬───────┬───────┐
│ Sequence │ Category │ Action │ Data0 │ Data1 │ Data2 │
│  4 bytes │  1 byte  │ 1 byte │ 2 b   │ 2 b   │ 4 b   │
└──────────┴──────────┴────────┴───────┴───────┴───────┘
```

#### Pourquoi UDP pour les Inputs (CRITIQUE)

**TCP est un mauvais choix pour les commandes de mouvement.**

Le problème fondamental est le **head-of-line blocking**:

```
Scénario avec TCP:
┌────────────────────────────────────────────────────────────────────────┐
│ Client envoie: [MoveStart seq=1] [MoveStop seq=2] [MoveStart seq=3]   │
│                                                                        │
│ Paquet seq=2 (MoveStop) est perdu sur le réseau                       │
│                                                                        │
│ TCP BLOQUE la livraison de seq=3 jusqu'à retransmission de seq=2      │
│ Pendant ce temps (~100-300ms de RTT):                                  │
│   - Serveur: continue de simuler avec MoveStart (joueur bouge)        │
│   - Client: prédit correctement (joueur s'arrête puis rebouge)        │
│   - Divergence explose → Hard reconcile fréquent                      │
└────────────────────────────────────────────────────────────────────────┘
```

**Solution standard (Overwatch, Valorant, etc.):**

```
UDP + Input Redundancy:
┌────────────────────────────────────────────────────────────────────────┐
│ Chaque paquet contient les N derniers inputs (N=3 typiquement):       │
│                                                                        │
│ Paquet 1: [cmd seq=1]                                                  │
│ Paquet 2: [cmd seq=1, cmd seq=2]                                       │
│ Paquet 3: [cmd seq=1, cmd seq=2, cmd seq=3]  ← Si paquet 2 perdu,     │
│                                                  seq=2 arrive ici!     │
│                                                                        │
│ Serveur:                                                               │
│   - Track lastProcessedSeq                                             │
│   - Ignore commandes avec seq <= lastProcessedSeq (déjà traitées)     │
│   - Process nouvelles commandes dans l'ordre                           │
└────────────────────────────────────────────────────────────────────────┘
```

**Avantages UDP + Redundancy:**
- Pas de head-of-line blocking
- Latence prévisible (pas de retransmission TCP)
- Perte de paquet récupérée au prochain envoi
- Séquençage manuel pour ordre correct

**Configuration recommandée:**
```csharp
public const int INPUT_REDUNDANCY_COUNT = 3;  // Derniers inputs par paquet
public const float INPUT_SEND_RATE = 30f;     // Hz (même que tick rate)
```

### 2. Prédiction Locale

**Fichier:** `NetworkClient.cs` → appelle `CommandReconciliation.RecordCommand()`

```csharp
public void SendCommand(GameCommand cmd)
{
    // 1. Convertir en SimCommand avec tick courant
    var simCmd = CommandHelper.ToSimCommand(cmd, _predictionWorld.Clock.CurrentTick);

    // 2. Exécuter localement IMMÉDIATEMENT (feedback instantané)
    _predictionWorld.ExecuteCommand(_localClientId, simCmd);

    // 3. Enregistrer pour réconciliation future
    _reconciliation.RecordCommand(simCmd, issueTick);

    // 4. Ajouter au buffer d'envoi (pour redundancy)
    _pendingInputBuffer.Add(cmd);

    // 5. Envoyer avec redundancy (UDP unreliable)
    var packet = BuildInputPacket(_pendingInputBuffer.GetLastN(INPUT_REDUNDANCY_COUNT));
    _netAdapter.SendToServer(packet, reliable: false);
}
```

**Structure PendingCommand:**
```csharp
public struct PendingCommand
{
    public uint Sequence;   // Numéro de séquence pour ACK
    public uint IssueTick;  // Tick où la commande a été envoyée
    public SimCommand Command;
}
```

### 3. Traitement Serveur

**Fichier:** `ServerGameLoop.cs`

```csharp
private void OnCommandReceived(int clientId, GameCommand cmd)
{
    // 1. Valider (normaliser direction si magnitude > 1)
    var dir = cmd.GetDirection();
    if (dir.sqrMagnitude > 1f) dir = dir.normalized;

    // 2. Convertir avec tick SERVEUR (pas client!)
    var simCmd = CommandHelper.ToSimCommand(cmd, _simWorld.Clock.CurrentTick);

    // 3. Exécuter dans simulation authoritative
    _simWorld.ExecuteCommand(clientId, simCmd);

    // 4. Tracker séquence pour ACK
    _commandBuffer.AckCommand(clientId, cmd.Sequence);
}
```

### 4. Broadcast Snapshots

**Fichier:** `ServerGameLoop.cs`

```csharp
private void BroadcastSnapshots()
{
    foreach (var player in _simWorld.AllPlayers)
    {
        int clientId = player.OwnerClientId;

        // AckInputSeq = dernière commande traitée pour CE client
        uint ackSeq = _commandBuffer.GetLastAckSeq(clientId);

        // Créer snapshot personnalisé (avec AOI si activé)
        var snapshot = SnapshotHelper.CreateSnapshot(_simWorld, clientId, ackSeq);

        // Envoyer en UDP (unreliable) - perte acceptable
        _netAdapter.SendToClient(clientId, snapshot, reliable: false);
    }
}
```

### 5. Réconciliation

**Fichier:** `CommandReconciliation.cs`

```csharp
public void OnSnapshotReceived(in SnapshotDelta snapshot)
{
    // 1. Discard commandes ACK'd
    _pendingCommands.RemoveAll(cmd => cmd.Sequence <= snapshot.AckInputSeq);

    // 2. Calculer erreur de prédiction
    Vector3 serverPos = snapshot.PlayerState.Position;
    Vector3 predictedPos = _predictionWorld.localPlayer.Position;
    float error = (predictedPos - serverPos).magnitude;

    // 3. Décider du mode de correction
    if (error <= SOFT_RECONCILE_THRESHOLD)
    {
        // Erreur négligeable → ignorer
        return;
    }
    else if (error <= HARD_RECONCILE_THRESHOLD)
    {
        // Erreur modérée → correction progressive (soft)
        ApplySoftCorrection(serverState);
    }
    else
    {
        // Erreur importante → réconciliation complète (hard)
        Reconcile(player, serverState, serverTick);
    }
}
```

---

## Algorithme de Réconciliation (Hard)

```csharp
private void Reconcile(SimPlayer player, NetEntityState serverState, uint serverTick)
{
    uint predictedTick = _predictionWorld.Clock.CurrentTick;
    int ticksToReplay = (int)(predictedTick - serverTick);

    // Cap pour éviter spiral of death
    ticksToReplay = Math.Min(ticksToReplay, MAX_REPLAY_TICKS);

    // 1. REVERT: Appliquer état serveur
    player.Transform.Position = serverState.Position;
    player.Transform.Velocity = serverState.Velocity;
    player.Stats.Health = serverState.Health;
    _predictionWorld.Clock.SetTick(serverTick);

    // 2. GET COMMANDS: Récupérer commandes non-ACK'd triées par tick
    var commandsToReplay = GetCommandsToReplay();

    // 3. APPLY PAST COMMANDS: Commandes avant serverTick (pour état mouvement)
    int cmdIndex = 0;
    while (cmdIndex < commandsToReplay.Count &&
           commandsToReplay[cmdIndex].IssueTick <= serverTick)
    {
        _predictionWorld.CommandDispatcher.Execute(player, commandsToReplay[cmdIndex].Command);
        cmdIndex++;
    }

    // 4. REPLAY TICKS: De serverTick à predictedTick
    for (int t = 0; t < ticksToReplay; t++)
    {
        uint currentTick = _predictionWorld.Clock.CurrentTick;

        // Appliquer commandes issues à ce tick
        while (cmdIndex < commandsToReplay.Count &&
               commandsToReplay[cmdIndex].IssueTick == currentTick)
        {
            _predictionWorld.CommandDispatcher.Execute(player, commandsToReplay[cmdIndex].Command);
            cmdIndex++;
        }

        // Simuler ce tick
        player.Tick(_config.TickDelta, _config);
        _predictionWorld.Clock.Advance();
    }
}
```

**Exemple concret:**
```
État initial:
- ServerTick: 100, Position serveur: X=10
- ClientTick: 105, Position prédite: X=12
- Commandes non-ackées: [MoveStart seq=16, MoveStop seq=17]
- AckSeq: 15

Réconciliation:
1. Revert: Position = 10, Tick = 100
2. Appliquer TOUTES les commandes non-ackées IMMEDIATEMENT:
   - MoveStart seq=16 → joueur commence à bouger
   - MoveStop seq=17 → joueur s'arrête
3. Simuler 5 ticks (100 → 105) avec physics pure

Résultat: Position = 10 (arrêté immédiatement)
```

**IMPORTANT: Pourquoi ne pas utiliser IssueTick?**

L'ancien code appliquait les commandes à leur `IssueTick` (tick client). C'est faux:
- Les horloges client/serveur dérivent (même avec PLL)
- Créait une oscillation soft → hard permanente
- Le serveur exécute les commandes à son propre tick, pas celui du client

La bonne approche:
- Les commandes non-ackées n'ont PAS ENCORE été vues par le serveur
- On les applique TOUTES au début du replay (ordre par `Sequence`)
- Puis on simule les ticks (physics uniquement)

---

## Correction Progressive (Soft)

Pour les erreurs modérées (entre SOFT et HARD threshold), une correction douce évite les snaps visibles:

```csharp
public void UpdateCorrection(float deltaTime)
{
    if (!_isSoftCorrecting) return;

    // Calculer offset entre sim et cible serveur
    Vector3 targetOffset = _softCorrectionTarget - player.Transform.Position;

    // Smoothing exponentiel (framerate-independent)
    _visualCorrectionOffset = Vector3.Lerp(
        _visualCorrectionOffset,
        targetOffset,
        1f - Mathf.Exp(-POSITION_SMOOTHING_K * deltaTime)
    );

    // Convergence terminée?
    if (_visualCorrectionOffset.magnitude < 0.01f)
    {
        _isSoftCorrecting = false;
        _visualCorrectionOffset = Vector3.zero;
    }
}
```

L'offset est appliqué dans la **couche visuelle** (pas la simulation), permettant:
- La prédiction continue normalement
- Le visuel converge progressivement vers la position serveur
- Aucun snap perceptible par le joueur

---

## Interpolation des Autres Joueurs

**Fichier:** `InterpolationBuffer.cs`

Les joueurs distants (non-locaux) utilisent l'interpolation au lieu de la prédiction:

```csharp
public bool TryInterpolate(float perceivedServerTime, out Vector3 position, out float rotationY)
{
    // Render time = temps perçu - buffer d'interpolation
    float renderTime = perceivedServerTime - (INTERPOLATION_BUFFER_TICKS * TICK_DELTA);

    // Trouver deux états encadrant renderTime
    for (int i = 0; i < _count - 1; i++)
    {
        var s1 = _buffer[idx1];
        var s2 = _buffer[idx2];

        if (s1.Timestamp <= renderTime && s2.Timestamp >= renderTime)
        {
            float t = (renderTime - s1.Timestamp) / (s2.Timestamp - s1.Timestamp);
            position = Vector3.Lerp(s1.Position, s2.Position, t);
            rotationY = Mathf.LerpAngle(s1.RotationY, s2.RotationY, t);
            return true;
        }
    }

    // Fallback: utiliser dernier état
    return UseLatestState(out position, out rotationY);
}
```

**Délai d'interpolation:**
- 2 snapshots @ 30Hz = **66ms**
- Protège contre 1 paquet perdu
- Standard industrie (Source Engine, Overwatch)

---

## Configuration

**Fichier:** `NetcodeConstants.cs`

### Paramètres de base
```csharp
// Simulation
public const int TICK_RATE = 30;              // Hz (ticks/seconde)
public const int SNAPSHOT_RATE = 30;          // Hz (snapshots/seconde)
public const float PLAYER_SPEED = 8f;         // units/seconde

// Interpolation
public const int INTERPOLATION_BUFFER_TICKS = 2;   // 66ms de délai
public const float INTERPOLATION_PLL_GAIN = 0.2f;  // Ajustement playback rate

// Réconciliation
public const int SOFT_RECONCILE_TICKS = 1;    // ~0.27 units
public const int HARD_RECONCILE_TICKS = 4;    // ~1.07 units

// Smoothing
public const float POSITION_SMOOTHING_K = 20f;  // Convergence rapide
public const float ROTATION_SMOOTHING_K = 10f;  // Plus lent pour confort visuel

// Transport (Input Redundancy)
public const int INPUT_REDUNDANCY_COUNT = 3;    // Derniers inputs par paquet UDP
public const float INPUT_SEND_RATE = 30f;       // Hz (aligner sur tick rate)
```

### Valeurs dérivées (calculées automatiquement)
```csharp
public const float TICK_DELTA = 1f / TICK_RATE;                    // 0.0333s
public const float DISTANCE_PER_TICK = PLAYER_SPEED * TICK_DELTA;  // 0.267 units
public const float SOFT_RECONCILE_THRESHOLD = SOFT_RECONCILE_TICKS * DISTANCE_PER_TICK;  // 0.267 units
public const float HARD_RECONCILE_THRESHOLD = HARD_RECONCILE_TICKS * DISTANCE_PER_TICK;  // 1.07 units
```

---

## Mécanismes de Sécurité

### 1. Movement Watchdog (Serveur)

Prévient les "ghost runs" si un paquet MoveStop est perdu:

```csharp
private void TickWatchdog(float dt)
{
    foreach (var player in _simWorld.AllPlayers)
    {
        player.TimeSinceLastMoveCmd += dt;

        if (player.Transform.MoveDirection.sqrMagnitude > 0f &&
            player.TimeSinceLastMoveCmd > 0.3f)  // 300ms timeout
        {
            player.StopMoving();
        }
    }
}
```

### 2. Command Buffer Cap

Limite le nombre de commandes en attente pour éviter memory leak:

```csharp
private const int MAX_PENDING_COMMANDS = 32;

public void RecordCommand(...)
{
    _pendingCommands.Add(cmd);

    while (_pendingCommands.Count > MAX_PENDING_COMMANDS)
        _pendingCommands.RemoveAt(0);
}
```

### 3. Replay Tick Cap + Snap Brutal

Au-delà d'un certain seuil, le replay ne peut pas masquer la correction.
On fait un snap brutal assumé plutôt que de gaspiller du CPU:

```csharp
public int MaxReplayTicks { get; set; } = 8;  // ~266ms @ 30Hz

if (ticksToReplay > MaxReplayTicks)
{
    // SNAP BRUTAL: pas de replay, juste téléporter
    Debug.LogWarning("SNAP: Network issues detected");
    ApplyServerState(player, serverState);
    _pendingCommands.Clear();  // Commandes trop vieilles
    return;
}
```

**Pourquoi 8 ticks max?**
- 8 ticks @ 30Hz = 266ms de replay
- Au-delà: correction visible de toute façon
- Re-simuler 30+ ticks = coût CPU inutile
- Indique un problème réseau sérieux (lag spike, packet burst)

---

## Diagramme de Séquence

```
Client                          Server                          Client
(Local)                                                        (Remote)
   │                              │                              │
   │ Press W (MoveStart)          │                              │
   │ Execute locally (instant)    │                              │
   │                              │                              │
   │ ═══════════════════════════> │                              │
   │   UDP: [cmd1, cmd2, cmd3]    │ (input redundancy)           │
   │                              │                              │
   │                              │ Process new commands         │
   │                              │ Track lastProcessedSeq       │
   │                              │                              │
   │                              │ ──────────────────────────── │
   │                              │       Snapshot (UDP)         │
   │                              │  (ServerTick, AckSeq,        │
   │                              │   EntityStates[])            │
   │                              │ ──────────────────────────── │
   │                              │                              │
   │ <─────────────────────────── │                              │
   │      Snapshot received       │                              │
   │                              │                              │
   │ Compare predicted vs server  │                              │
   │ Discard ACK'd commands       │                              │
   │ Reconcile if needed          │                              │
   │                              │                              │
   │                              │                              │ Add to
   │                              │                              │ interp buffer
   │                              │                              │
   │                              │                              │ Interpolate
   │                              │                              │ at renderTime
   │                              │                              │
```

---

## Avantages de cette Architecture

| Aspect | Bénéfice |
|--------|----------|
| **Input lag** | Éliminé par exécution locale immédiate |
| **Bandwidth** | Événements seulement, pas inputs/frame (sparse) |
| **Scalabilité** | Supporte 50v50 avec bandwidth minimal |
| **Smoothness** | Interpolation pour joueurs distants |
| **Déterminisme** | Fixed tick rate + command replay = consistance |
| **Récupération** | Réconciliation corrige sans snap visible |
| **Anti-cheat** | Serveur reste authoritative |

---

## Système Semi-Stateless (Mouvement)

> **Implémenté en Janvier 2026**

### Problème avec le Système Event-Based

L'ancien système envoyait des événements `MoveStart` et `MoveStop`. Problème critique:

```
Scénario de perte de paquet MoveStop:
┌────────────────────────────────────────────────────────────────────────┐
│ Client: Appuie W → MoveStart(seq=1) envoyé                             │
│ Client: Relâche W → MoveStop(seq=2) envoyé                             │
│                                                                        │
│ ❌ MoveStop(seq=2) PERDU sur le réseau!                                │
│                                                                        │
│ Serveur: Continue de simuler le mouvement indéfiniment                 │
│ Client: Prédit correctement l'arrêt                                    │
│ Résultat: Divergence massive → "ghost run" côté serveur                │
│                                                                        │
│ Même avec input redundancy, le timing entre MoveStart et MoveStop      │
│ crée une fenêtre de vulnérabilité                                      │
└────────────────────────────────────────────────────────────────────────┘
```

### Solution: Mouvement Semi-Stateless

Chaque `InputPacket` contient l'**état actuel** du mouvement, pas des événements:

```csharp
public struct InputPacket : INetMessage
{
    public uint ClientTick;
    public uint MovementSeq;      // Séquence monotone pour rejet out-of-order

    // État de mouvement (semi-stateless)
    public bool IsMoving;         // Vrai si touches pressées
    public short MoveX;           // Direction X quantifiée
    public short MoveZ;           // Direction Z quantifiée

    // Événements discrets (jump, spells) - toujours event-based
    public byte CommandCount;
    public GameCommand[] Commands;
}
```

**Flux:**
1. Client envoie son état de mouvement **à chaque tick** (30Hz)
2. Serveur utilise l'état le plus récent comme "vérité actuelle"
3. Si un paquet est perdu, le suivant corrige immédiatement l'état

```
Avec Semi-Stateless:
┌────────────────────────────────────────────────────────────────────────┐
│ Paquet 1: MovementSeq=1, IsMoving=true, Dir=(1,0)                      │
│ Paquet 2: MovementSeq=2, IsMoving=true, Dir=(1,0)  ← PERDU             │
│ Paquet 3: MovementSeq=3, IsMoving=false            ← Serveur reçoit    │
│                                                                        │
│ Serveur: Applique immédiatement IsMoving=false                         │
│ Résultat: Arrêt correct, pas de ghost run!                             │
└────────────────────────────────────────────────────────────────────────┘
```

---

## Réconciliation de Mouvement (AckMovementSeq)

### Problème Initial

Avec le système semi-stateless, un nouveau problème apparaît:

```
Scénario de correction prématurée:
┌────────────────────────────────────────────────────────────────────────┐
│ tick=67: Client relâche W → envoie MovementSeq=67, IsMoving=false      │
│ tick=67: Client prédit immédiatement vel=(0,0,0)                       │
│                                                                        │
│ tick=69: Snapshot arrive du serveur (ServerTick=69)                    │
│          Mais serveur n'a pas encore reçu MovementSeq=67!              │
│          serverVel=(8,0,0) (toujours en mouvement côté serveur)        │
│                                                                        │
│ ❌ Sans protection: Client écrase sa vélocité avec vel serveur         │
│    → Joueur recommence à bouger alors qu'il a relâché la touche!       │
└────────────────────────────────────────────────────────────────────────┘
```

### Solution: Dual Acknowledgment

Le snapshot contient maintenant **deux** séquences d'acknowledgment:

```csharp
public struct SnapshotDelta : INetMessage
{
    public uint ServerTick;
    public uint AckInputSeq;      // Dernière commande event traitée
    public uint AckMovementSeq;   // Dernière séquence mouvement traitée ← NOUVEAU
    public ushort EntityCount;
    public EntityState[] Entities;
}
```

**Côté Client (CommandReconciliation):**

```csharp
// Tracking du mouvement envoyé
private uint _lastAckedMovementSeq;
private uint _lastSentMovementSeq;

public void OnSnapshotReceived(in SnapshotDelta snapshot)
{
    _lastAckedMovementSeq = snapshot.AckMovementSeq;

    // Y a-t-il du mouvement en attente d'acknowledgment?
    bool pendingMovement = _lastSentMovementSeq > _lastAckedMovementSeq;

    // ... dans ProcessLocalPlayerState() ...
    if (!pendingMovement)
    {
        // Serveur a traité notre dernier mouvement → safe d'appliquer sa vélocité
        player.Transform.Velocity = serverState.Velocity;
    }
    else
    {
        // Mouvement pending → garder notre vélocité prédite
        // Le serveur n'a pas encore vu notre arrêt/changement!
    }
}
```

**Côté Serveur (ServerGameLoop):**

```csharp
// Tracking par client
private readonly Dictionary<int, uint> _lastMovementSeq = new();

private void OnMovementStateReceived(int clientId, uint movementSeq, bool isMoving, Vector2 moveDir)
{
    // Rejet des paquets out-of-order (monotonic check)
    if (movementSeq <= _lastMovementSeq[clientId]) return;

    _lastMovementSeq[clientId] = movementSeq;
    // Appliquer l'état de mouvement...
}

private void BroadcastSnapshots()
{
    foreach (var player in _simWorld.AllPlayers)
    {
        uint movementSeq = _lastMovementSeq[clientId];
        snapshot = SnapshotHelper.CreateSnapshot(_simWorld, clientId, ackSeq, movementSeq);
        // Envoyer...
    }
}
```

### Flux Complet avec Acknowledgment

```
Timeline:
────────────────────────────────────────────────────────────────────────────
tick=65  Client: Envoie MovementSeq=65, IsMoving=true
tick=66  Client: Envoie MovementSeq=66, IsMoving=true
tick=67  Client: Relâche W → Envoie MovementSeq=67, IsMoving=false ⭐
         Client: Prédit vel=(0,0,0) immédiatement

tick=68  Snapshot reçu: AckMovementSeq=64
         pendingMovement = (67 > 64) = true
         → Skip velocity override ✓

tick=69  Snapshot reçu: AckMovementSeq=66
         pendingMovement = (67 > 66) = true
         → Skip velocity override ✓

tick=70  Serveur reçoit MovementSeq=67, applique IsMoving=false

tick=71  Snapshot reçu: AckMovementSeq=67
         pendingMovement = (67 > 67) = false
         → Safe to apply server velocity ✓
         → Positions convergent, erreur → 0
────────────────────────────────────────────────────────────────────────────
```

### Logs de Debug

Pour diagnostiquer le système:

```
[INPUT] MOVE START | dir=(1.00, 0.00) | tick=10
[NET SEND] moveSeq=10 | isMoving=True | dir=(1.00, 0.00) | tick=10
[SERVER MOVEMENT STATE] seq=10 | tick=15 | client=0 | START
...
[INPUT] MOVE STOP | tick=67
[NET SEND] moveSeq=67 | isMoving=False | tick=67
[RECONCILIATION SNAPSHOT] ackMovementSeq=63 | pendingMovement=True
[SOFT CORRECTION] Skipping velocity override - pendingMovement=True (sent=67, acked=63)
...
[SERVER MOVEMENT STATE] seq=67 | tick=72 | client=0 | STOP
[RECONCILIATION SNAPSHOT] ackMovementSeq=67 | pendingMovement=False
[RECONCILIATION ERROR] error=0.002m  ← Convergé!
```

---

## Résumé des Fichiers Modifiés

| Fichier | Changement |
|---------|------------|
| `SnapshotDelta.cs` | Ajout `AckMovementSeq` field |
| `SnapshotHelper.cs` | Paramètre `ackMovementSeq` dans `CreateSnapshot()` et `CreateFilteredSnapshot()` |
| `ServerGameLoop.cs` | Passe `_lastMovementSeq[clientId]` aux snapshots |
| `CommandReconciliation.cs` | `MovementState` struct, `RecordMovementState()`, check `pendingMovement` avant override |
| `NetworkClient.cs` | Appelle `RecordMovementState()` à chaque envoi |
| `InputCollector.cs` | Logs de debug sur changement d'état mouvement |
| `InputPacket.cs` | Structure avec `MovementSeq`, `IsMoving`, `MoveX`, `MoveZ` |

---

## Références

- **Overwatch GDC 2017** - "Networking Scripted Weapons and Abilities"
- **Valve Source Multiplayer Networking** - cl_interp_ratio
- **Glenn Fiedler** - "Networked Physics"
- **Gabriel Gambetta** - "Fast-Paced Multiplayer"
