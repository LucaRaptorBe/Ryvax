# Netcode Flow AS-IS (avec `INTERPOLATION_DISABLED = true`)

> Documentation du flow de données exact du système de netcode.
> Dernière mise à jour: 2026-02-02

## Configuration actuelle

| Paramètre | Valeur | Fichier |
|-----------|--------|---------|
| `INTERPOLATION_DISABLED` | `true` | NetcodeConstants.cs:32 |
| `TICK_RATE` | 60Hz (16.67ms) | NetcodeConstants.cs:43 |
| `SNAPSHOT_RATE` | 60Hz | NetcodeConstants.cs:50 |
| Input send rate | 120Hz (8.3ms) | NetworkClient.cs:54 |
| Intent rate-limit | 120Hz (8.3ms) | IntentBuilder.cs:37 |
| `PLAYER_SPEED` | 8 u/s | NetcodeConstants.cs:56 |

---

## Vue d'ensemble du flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CLIENT (Unity)                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│  1. INPUT CAPTURE (InputCollector.cs:143-192)                               │
│     Keyboard.current.dKey.isPressed → dir = (1, 0)                          │
│     → _networkClient.SetLocalMoveIntent(moveDir3D)  ← ROTATION IMMÉDIATE    │
│     → _intentBuilder.OnKeyboardMove(dir, dt, tick)                          │
│     → Retourne InputIntent.MoveDir si rate-limit OK (120Hz)                 │
│                                                                             │
│  2. INTENT QUEUING (NetworkClient.cs:284-287)                               │
│     SendInputIntent() → _pendingIntent = intent                             │
│                                                                             │
│  3. PACKET SEND (NetworkClient.cs:293-321)                                  │
│     SendInputUpdate() @ 120Hz                                               │
│     → InputPacket.CreateMoveDir(tick, seq, dir, events)                     │
│     → _netAdapter.SendInputPacket(packet) [UDP unreliable]                  │
└────────────────────────────────────┬────────────────────────────────────────┘
                                     │ réseau ~1-50ms
                                     ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                                 SERVEUR                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│  4. RÉCEPTION & SIMULATION                                                  │
│     ServerGameLoop reçoit packet → buffer input                             │
│     Tick @ 60Hz → SimPlayer.SetMoveDirection(dir)                           │
│     → position += velocity × dt                                             │
│                                                                             │
│  5. SNAPSHOT BROADCAST @ 60Hz                                               │
│     SnapshotDelta { Position, Velocity, Rotation, ServerTick }              │
│     → UDP unreliable vers client                                            │
└────────────────────────────────────┬────────────────────────────────────────┘
                                     │ réseau ~1-50ms
                                     ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CLIENT (Unity)                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│  6. SNAPSHOT RECEIVE (NetworkClient.cs:457-510)                             │
│     OnSnapshotReceived()                                                    │
│     → _timeSync.OnSnapshotReceived(serverTime)                              │
│     → _visualPositionManager.OnSnapshotReceived(state, serverTime)          │
│       └→ _interpolator.OnSnapshotReceived() [ajoute au buffer]              │
│                                                                             │
│  7. VISUAL UPDATE (NetworkClient.cs:248-251)                                │
│     Update() chaque frame                                                   │
│     → _timeSync.Update(dt)                                                  │
│       └→ BYPASS: _perceivedServerTime += dt (pas de PLL)                    │
│     → _visualPositionManager.Update(dt)                                     │
│       └→ renderTime = _timeSync.RenderTime                                  │
│          └→ BYPASS: retourne _newestSnapshotTime (pas de buffer)            │
│       └→ _basePos = _interpolator.GetBasePos(renderTime)                    │
│          └→ BYPASS: retourne _buffer.GetLatest().Position                   │
│                     (pas d'interpolation, pas d'extrapolation)              │
│                                                                             │
│  8. RENDU (PlayerView.cs:150-169)                                           │
│     transform.position = _networkClient.GetVisualPosition()                 │
│     transform.rotation = LocalIntentFeedback (immédiat) ou snapshot         │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Détail des données à chaque étape

### 1️⃣ INPUT → NETWORK (Client → Serveur)

#### Étape 1: InputCollector.HandleWASDInput()

```
Keyboard.current.dKey.isPressed = true

→ dir = Vector2(1, 0)  // droite
→ _networkClient.SetLocalMoveIntent(Vector3(1, 0, 0))  ← FEEDBACK LOCAL
→ _intentBuilder.OnKeyboardMove(dir, dt, tick)
```

#### Étape 2: IntentBuilder.OnKeyboardMove() @ 120Hz

```csharp
// IntentBuilder.cs:37
private const float INTENT_SEND_INTERVAL = 0.0083f;  // 120Hz (8.3ms)
```

Si `accumulator >= 0.0083s`:

```
InputIntent {
  Type = MoveDir,
  Direction = Vector2(1, 0),  // float, normalisé
  SeqId = 42,
  ClientTick = 1234
}
```

#### Étape 3: NetworkClient.SendInputUpdate() @ 120Hz

**Quantization** (InputPacket.cs:121-122):
```
Payload0 = QuantizeDirection(1.0f) = (short)(1.0 × 127) = 127
Payload1 = QuantizeDirection(0.0f) = (short)(0.0 × 127) = 0
```

**InputPacket envoyé**:

| Champ | Valeur | Taille |
|-------|--------|--------|
| ClientTick | 1234 | 4 bytes (uint) |
| MovementSeq | 42 | 4 bytes (uint) |
| IntentType | MoveDir (1) | 1 byte |
| Payload0 | 127 | 2 bytes (short) |
| Payload1 | 0 | 2 bytes (short) |
| CommandCount | 0 | 1 byte |
| Commands | [] | 0 bytes |
| **TOTAL** | | **~14 bytes** |

---

### 2️⃣ GAMESIM → NETWORK (Serveur → Client)

#### Étape 4: ServerGameLoop - Après simulation

```
SimPlayer state après tick:
  Position = Vector3(5.267, 0, 0)    // float
  Velocity = Vector3(8, 0, 0)        // 8 u/s vers droite
  RotationY = 90.0f                  // degrees
  Speed = 8.0f                       // u/s
  IsMoving = true
```

#### Étape 5: EntityState.FromSimEntity() - Quantization

**Position** (16-bit, map 200×200):
```
PosX = ((5.267 + 100) / 200) × 65535 = 34504
PosY = (0 / 20) × 65535 = 0
PosZ = ((0 + 100) / 200) × 65535 = 32767
```

**Velocity** (×100 precision):
```
VelX = 8.0 × 100 = 800
VelY = 0
VelZ = 0
```

**Rotation** (16-bit, 0-360°):
```
RotY = (90 / 360) × 65535 = 16383
```

**Speed** (×10 precision):
```
SpeedQ = 8.0 × 10 = 80
```

**EntityState**:

| Champ | Valeur | Taille |
|-------|--------|--------|
| EntityId | 1 | 4 bytes |
| EntityType | Player (0) | 1 byte |
| Flags | Alive (1) | 1 byte |
| PosX | 34504 | 2 bytes (ushort) |
| PosY | 0 | 2 bytes |
| PosZ | 32767 | 2 bytes |
| RotY | 16383 | 2 bytes |
| Health | 100 | 2 bytes |
| State | Moving (1) | 1 byte |
| VelX | 800 | 2 bytes (short) |
| VelZ | 0 | 2 bytes |
| VelY | 0 | 2 bytes |
| SpeedQ | 80 | 2 bytes |
| EventFlags | 0 | 1 byte |
| **TOTAL** | | **26 bytes/entity** |

#### Étape 6: SnapshotDelta @ 60Hz

| Champ | Valeur | Taille |
|-------|--------|--------|
| ServerTick | 3600 | 4 bytes |
| AckInputSeq | 41 | 4 bytes |
| AckMovementSeq | 42 | 4 bytes |
| EntityCount | 1 | 2 bytes |
| Entities | [EntityState×1] | 26 bytes |
| **TOTAL** | | **~40 bytes (1 player)** |

---

### 3️⃣ NETWORK → PLAYERVIEW (Client)

#### Étape 7: NetworkClient.OnSnapshotReceived()

**Dequantization** (SnapshotDelta.cs:168-186):

**Position**:
```
X = (34504 / 65535) × 200 - 100 = 5.267
Y = (0 / 65535) × 20 = 0
Z = (32767 / 65535) × 200 - 100 = 0
→ Vector3(5.267, 0, 0)
```

**Velocity**:
```
X = 800 / 100 = 8.0
Y = 0
Z = 0
→ Vector3(8, 0, 0)
```

**Rotation**: `(16383 / 65535) × 360 = 90.0°`

**Speed**: `80 / 10 = 8.0 u/s`

#### Étape 8: VisualPositionManager.Update()

Avec `INTERPOLATION_DISABLED = true`:

```csharp
// TimeSync.cs:54-55 - BYPASS
renderTime = _newestSnapshotTime  // PAS de buffer!

// BaseInterpolator.cs:228-240 - BYPASS
_basePos = _buffer.GetLatest().Position  // PAS d'interpolation!
        = Vector3(5.267, 0, 0)

// VisualPositionManager.cs:48
VisualPosition = _basePos = Vector3(5.267, 0, 0)  // direct, pas de lerp
```

#### Étape 9: PlayerView.UpdatePosition()

```csharp
// PlayerView.cs:154
transform.position = _networkClient.GetVisualPosition()
                   = Vector3(5.267, 0, 0)

// ROTATION (V4.0 LocalIntentFeedback) - PlayerView.cs:158-169
if (localIntent.HasIntent):
    → rotation = LocalIntentFeedback.GetSmoothedRotationY()
    → Rotation IMMÉDIATE vers direction input (pas le serveur!)
else:
    → rotation = _networkClient.GetVisualRotationY() = 90°

// ANIMATION - PlayerView.cs:203-214
localIsMoving = _networkClient.HasLocalMoveIntent() = true
→ animator.SetFloat("Speed", 1.0)
→ animator.SetBool("IsMoving", true)
```

---

## Résumé des données par couche

| Couche | Structure | Données position | Quantization |
|--------|-----------|------------------|--------------|
| **Input Unity** | `Vector2` | `(1, 0)` float | aucune |
| **Intent** | `InputIntent` | `(1, 0)` float | aucune |
| **Packet réseau** | `InputPacket` | `Payload0=127, Payload1=0` | ×127 (short) |
| **GameSim** | `SimPlayer` | `(5.267, 0, 0)` float | aucune |
| **Snapshot réseau** | `EntityState` | `PosX=34504` etc. | ×65535/200 (ushort) |
| **Client receive** | `SnapshotState` | `(5.267, 0, 0)` float | dequantized |
| **PlayerView** | `transform.position` | `(5.267, 0, 0)` float | aucune |

---

## BYPASS actifs (INTERPOLATION_DISABLED = true)

| Composant | Ligne | Comportement normal | Comportement BYPASS |
|-----------|-------|---------------------|---------------------|
| **TimeSync.RenderTime** | :54-56 | `perceivedTime - adaptiveBuffer` | `_newestSnapshotTime` (aucun délai) |
| **TimeSync.Update()** | :133-137 | PLL ajuste playbackRate | `+= dt` fixe (1:1) |
| **BaseInterpolator.GetBasePos()** | :228-241 | Lerp entre 2 snapshots | `GetLatest().Position` directement |
| **BaseInterpolator.GetBaseRotationY()** | :277-284 | LerpAngle entre 2 snapshots | `GetLatest().RotationY` directement |

---

## Comportement visible

Avec `INTERPOLATION_DISABLED = true`:
- **Position "saute"** entre les snapshots serveur (pas de lissage)
- **Rotation instantanée** via LocalIntentFeedback (feedback local)
- **Latence perçue = RTT** (~50-100ms typique) car pas de buffer
- **Pas de prédiction client** - position = 100% serveur

---

## Fichiers clés

```
Client/Input/InputCollector.cs           - Capture WASD → Intent
Client/Input/IntentBuilder.cs            - Rate-limit 120Hz, crée InputIntent
Core/NetworkClient.cs                    - Envoie packets, reçoit snapshots
Client/Timing/TimeSync.cs                - RenderTime (BYPASS: direct)
Client/Interpolation/BaseInterpolator.cs - GetBasePos (BYPASS: latest)
Client/Prediction/VisualPositionManager.cs - visualPos = basePos
Client/View/Entities/PlayerView.cs       - Applique transform
Network/NetAdapter/Messages/InputPacket.cs - Structure packet input
Network/NetAdapter/Messages/SnapshotDelta.cs - Structure snapshot
Network/Shared/NetcodeConstants.cs       - Configuration globale
```

---

## Historique

| Version | Date | Changements |
|---------|------|-------------|
| AS-IS | 2026-02-02 | Documentation de l'état actuel avec INTERPOLATION_DISABLED=true |
