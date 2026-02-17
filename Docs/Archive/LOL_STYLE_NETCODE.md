# LoL-Style Netcode Architecture (v2.3)

## Vue d'ensemble

Cette implementation adopte une architecture de netcode inspiree de League of Legends, fondamentalement differente du modele FPS classique (prediction + reconciliation).

### Principes fondamentaux

1. **Input = Intentions** - Le client envoie des intentions (`MoveTo`, `Stop`, `Follow`), pas des directions brutes
2. **Pas de rollback/replay** - Le serveur est l'autorite absolue; le client fait de l'anticipation visuelle, pas de prediction structuree
3. **Interpolation d'abord** - `basePos` vient des snapshots serveur interpoles
4. **Correction de l'offset** - On corrige `visualOffset` vers 0, pas `visualPos` vers `serverPos`

### Formule centrale

```
visualPos = basePos + visualOffset
```

- `basePos`: Position interpolee depuis les snapshots serveur (verite retardee)
- `visualOffset`: Decalage visuel qui se corrige vers 0
- `visualPos`: Position affichee a l'ecran

---

## Changements V2.3

### Ameliorations

1. **Horizon d'extrapolation reduit**
   - `horizonHard`: 200ms -> 150ms
   - Dead-reckoning simple (sans pathfinding) = moins fiable a long terme

2. **BasePosQuality tracking**
   - Nouvel enum: `Interpolated`, `Extrapolated`, `Frozen`
   - `AbsorbBasePosJump` seulement si `Quality == Interpolated`
   - Evite d'injecter des offsets artificiels pendant l'extrapolation

3. **Clarification serveur**
   - Pas de navmesh/pathfinding
   - Serveur: tick autoritaire, collisions (static + dynamique), CC, speed mods

---

## Changements V2.2

### Corrections de bugs

1. **Reset basePrev apres discontinuite**
   - Apres teleport/blink, `basePrev` est reset a `baseNow`
   - Evite un absorb enorme au frame suivant

2. **Purge selective des snapshots**
   - `SnapHard(discontinuityTime)` purge seulement les snapshots avant l'evenement
   - Evite de re-interpoler a travers une discontinuite

3. **Immobilize lock sur IntentBuilder**
   - Pendant CC, les MoveTo sont supprimes
   - IntentBuilder emet Stop a 2Hz pour eviter le spam

### Ameliorations

4. **Plafond sur largeGap**
   - `largeGap = clamp(..., 80, 250)`
   - Evite de masquer des vrais desync a haute vitesse

5. **Separation Snap vs SnapHard**
   - `Snap()`: offset=0 seulement (gap trop grand)
   - `SnapHard(time)`: offset=0 + purge buffer (discontinuite)

---

## Architecture des fichiers

### Nouveaux fichiers

| Fichier | Description |
|---------|-------------|
| `Client/Input/InputIntent.cs` | Struct definissant les intentions (MoveTo, Stop, Follow) |
| `Client/Input/IntentBuilder.cs` | Convertit clic/WASD en InputIntent a ~10Hz |
| `Client/Timing/TimeSync.cs` | Buffer adaptatif au jitter (80-160ms) |
| `Client/Interpolation/BaseInterpolator.cs` | Interpole basePos, fallback extrapolation |
| `Client/Prediction/VisualOffsetCorrector.cs` | Corrige l'offset vers 0 (3 tiers) |
| `Client/Prediction/VisualPositionManager.cs` | Orchestre: basePos + offset = visualPos |

### Fichiers modifies

| Fichier | Modifications |
|---------|---------------|
| `Client/Input/InputCollector.cs` | Utilise IntentBuilder, propage immobilize lock |
| `Core/NetworkClient.cs` | Envoie InputIntent, expose IsImmobilized() |
| `Client/View/Entities/PlayerView.cs` | Utilise VisualPositionManager.VisualPosition |
| `Network/NetAdapter/Messages/SnapshotDelta.cs` | Ajoute Speed, EventFlags (CC, blink, teleport) |
| `Network/NetAdapter/Messages/InputPacket.cs` | Format intent-based (v3) |
| `Network/Shared/NetcodeConstants.cs` | Nouveaux seuils (20/80/250 unites monde) |

---

## Flux de donnees (V2.3)

```
INPUT (clic ou WASD)
      |
      v
[InputCollector]
   - Propage immobilize lock vers IntentBuilder
      |
      v
[IntentBuilder]
   if immobilizeLocked:
       emit Stop @2Hz (rate-limited)
   else:
       clic -> MoveTo(clickedPos) immediat
       WASD -> dir=0? Stop : MoveTo(origin + dir*lookaheadDist) @10Hz
                  origin = basePos + clamp(visualOffset, largeGap)
                  lookaheadDist = clamp(speed * 0.25s, 50, 200)
      |
      v
[NetworkClient.SendInputIntent()]
      |
      +------------------------------------> SERVEUR (tick autoritaire, collisions, CC, speed mods)
      |
      |  SNAPSHOTS (pos, vel, speed, ccFlags, events, lastProcessedSeq)
      |                 |
      v                 v
[VisualPositionManager.OnSnapshotReceived()]
   - OnImmobilizeCC(state.HasImmobilizeCC)
   - if blink/teleport/reset: SnapHard(serverTime) + basePrevValid=false
      |
      v
[TimeSync] renderTime = serverTime - adaptiveBuffer
              adaptiveBuffer = clamp(target + k*jitter, 80ms, 160ms)
      |
      v
[BaseInterpolator.GetBasePos(renderTime)]
   - 2 snapshots disponibles: basePos = Lerp(A, B, alpha), Quality=Interpolated
   - Discontinuite (teleport): basePos = B.pos, HadDiscontinuity=true
   - Extrapolation (V2.3): horizonSoft=100ms, horizonHard=150ms, Quality=Extrapolated
   - Au-dela: freeze, Quality=Frozen
      |
      v
[VisualPositionManager.Update()]
   if HadDiscontinuity:
       SnapHard(discontinuityTime) // purge selectif
       basePrev = baseNow         // V2.2: reset basePrev
       basePrevValid = true
   else if basePrevValid:
       if Quality == Interpolated:  // V2.3: condition sur qualite
           AbsorbBasePosJump(basePrev, baseNow)
       basePrev = baseNow
   else:
       basePrev = baseNow
       basePrevValid = true
      |
      v
[VisualOffsetCorrector.Update()]
   - gap < smallGap (20): Smooth (k=6, ~115ms)
   - gap < largeGap (80-250): Accelerated (k=15, ~46ms)
   - gap >= largeGap: Snap() (offset=0, pas de purge)
      |
      v
visualPos = basePos + visualOffset
      |
      v
[PlayerView.transform.position]
```

---

## Composants detailles

### InputIntent

```csharp
public enum InputIntentType : byte
{
    MoveTo,   // Deplacement vers position monde
    Stop,     // Arret immediat
    Follow    // Suivre une entite
}

public struct InputIntent
{
    public InputIntentType Type;
    public Vector2 WorldPos;       // Pour MoveTo
    public uint TargetEntityId;    // Pour Follow
    public uint SeqId;             // Sequence monotonique
    public uint ClientTick;        // Pour debug/lag compensation
}
```

### IntentBuilder (V2.2)

Convertit l'input brut en intentions:

- **Clic droit**: `MoveTo(clickedPos)` immediat (sauf si immobilize)
- **WASD/ZQSD**: `MoveTo(lookaheadPoint)` a 10Hz (sauf si immobilize)
  - `lookaheadPoint = origin + direction * lookaheadDist`
  - `origin = basePos + clamp(visualOffset, largeGap)`
  - `lookaheadDist = clamp(speed * 0.25s, 50, 200)`
- **Relacher WASD**: `Stop` immediat
- **Pendant immobilize**: `Stop` a 2Hz (rate-limited)

```csharp
// V2.2: Immobilize lock
public void SetImmobilizeLock(bool locked);
public bool IsImmobilizeLocked { get; }
```

### TimeSync

Buffer adaptatif base sur le jitter reseau:

```
adaptiveBuffer = clamp(TARGET + JITTER_GAIN * estimatedJitter, MIN, MAX)
```

| Constante | Valeur | Description |
|-----------|--------|-------------|
| TARGET | 100ms | Buffer cible |
| MIN | 80ms | Minimum absolu |
| MAX | 160ms | Maximum absolu |
| JITTER_GAIN | 0.5 | Facteur de compensation |

### BaseInterpolator (V2.3)

Interpole `basePos` depuis les snapshots avec tracking de qualite:

```csharp
// V2.3: Qualite de la position
public enum BasePosQuality
{
    Interpolated,   // Normal: deux snapshots
    Extrapolated,   // Fallback: dead-reckoning
    Frozen          // Pas de donnees valides
}

public BasePosQuality Quality { get; }
```

1. **Interpolation normale**: Deux snapshots encadrent renderTime -> `Lerp(A, B, alpha)`, `Quality=Interpolated`
2. **Detection discontinuite**: Si B a un flag teleport/blink -> snap a B, signal `HadDiscontinuity`
3. **Extrapolation fallback (V2.3)**:
   - `dt <= 100ms`: Dead-reckoning normal, `Quality=Extrapolated`
   - `dt <= 150ms`: Dead-reckoning cappe, `Quality=Extrapolated`
   - `dt > 150ms`: Freeze, `Quality=Frozen`

```csharp
// V2.2: Expose le temps de discontinuite
public float DiscontinuityTime { get; }

// V2.2: Purge selective
public void PurgeSnapshotsBefore(float time);
```

### VisualOffsetCorrector (V2.2)

Corrige l'offset vers 0 avec seuils dynamiques:

```csharp
// V2.2: Seuils avec plafond
smallGap = max(20, speed * 0.06)
largeGap = clamp(max(80, speed * max(0.25, bufferSeconds * 2)), 80, 250)

// Correction par tier
if (gap < epsilon)     -> offset = 0
else if (gap < smallGap) -> offset *= exp(-6 * dt)   // ~115ms half-life
else if (gap < largeGap) -> offset *= exp(-15 * dt)  // ~46ms half-life
else                     -> Snap()                   // offset = 0, pas de purge
```

**Note**: `bufferSeconds` est en secondes (pas en millisecondes).

**Distinction Snap vs SnapHard (V2.2)**:

| Methode | Action | Usage |
|---------|--------|-------|
| `Snap()` | offset=0 | Gap trop grand (correction visuelle) |
| `SnapHard(time)` | offset=0 + purge avant `time` | Discontinuite (teleport/blink) |
| `SnapHard()` | offset=0 + purge tout | Reset complet |

**Cas speciaux**:
- **CC immobilisant**: `offset = 0` + lock jusqu'a fin du CC
- **Blink/Teleport**: SnapHard + purge selective
- **State Reset**: SnapHard + purge

### VisualPositionManager (V2.3)

Orchestre tous les composants avec tracking de basePrev et qualite:

```csharp
public void Update(float dt)
{
    _timeSync.Update(dt);
    _basePos = _interpolator.GetBasePos(_timeSync.RenderTime);

    // V2.2: Gestion propre de basePrev
    if (_interpolator.HadDiscontinuity)
    {
        _corrector.SnapHard(_interpolator.DiscontinuityTime);
        _basePrev = _basePos;  // Reset basePrev
        _basePrevValid = true;
    }
    else if (_basePrevValid)
    {
        // V2.3: Absorb seulement si interpolation valide
        if (_interpolator.Quality == BasePosQuality.Interpolated)
        {
            _corrector.AbsorbBasePosJump(_basePrev, _basePos);
        }
        _basePrev = _basePos;
    }
    else
    {
        _basePrev = _basePos;
        _basePrevValid = true;
    }

    _corrector.Update(dt, _currentSpeed);
}

// V2.2: Expose immobilize pour InputCollector
public bool IsImmobilized => _corrector.IsImmobilized;
```

---

## Flags de snapshot (EntityState)

### EntityEventFlags

```csharp
[Flags]
public enum EntityEventFlags : byte
{
    None = 0,
    ImmobilizeCC = 1 << 0,   // Root/stun actif
    BlinkEvent = 1 << 1,     // Blink ce tick
    TeleportEvent = 1 << 2,  // Teleport ce tick
    StateReset = 1 << 3,     // Respawn, etc.
    Dashing = 1 << 4,        // Dash en cours
}
```

### Champs ajoutes a EntityState

| Champ | Type | Description |
|-------|------|-------------|
| `SpeedQ` | ushort | Vitesse quantifiee (precision 0.1 u/s) |
| `EventFlags` | byte | Flags CC/events |

---

## Constantes (NetcodeConstants)

### Seuils de correction (V2.2)

| Constante | Valeur | Description |
|-----------|--------|-------------|
| `LOL_BASE_SMALL_GAP` | 20 | Seuil smooth (unites monde) |
| `LOL_BASE_LARGE_GAP` | 80 | Seuil snap min (unites monde) |
| `MAX_LARGE_GAP` | 250 | **V2.2**: Plafond largeGap |
| `LOL_SMALL_GAP_TIME` | 0.06s | Facteur temps pour smallGap |
| `LOL_LARGE_GAP_TIME` | 0.25s | Facteur temps pour largeGap |
| `LOL_SMOOTH_CORRECTION_K` | 6 | Taux correction smooth |
| `LOL_ACCEL_CORRECTION_K` | 15 | Taux correction acceleree |
| `LOL_OFFSET_EPSILON` | 0.5 | Epsilon pour clamp a 0 |

### Extrapolation (V2.3)

| Constante | Valeur | Description |
|-----------|--------|-------------|
| `HORIZON_SOFT` | 100ms | Dead-reckoning normal |
| `HORIZON_HARD` | 150ms | **V2.3**: Dead-reckoning max (etait 200ms) |

### Buffer adaptatif

| Constante | Valeur | Description |
|-----------|--------|-------------|
| `ADAPTIVE_BUFFER_TARGET` | 100ms | Buffer cible |
| `ADAPTIVE_BUFFER_MIN` | 80ms | Buffer minimum |
| `ADAPTIVE_BUFFER_MAX` | 160ms | Buffer maximum |
| `INTENT_SEND_RATE` | 10Hz | Frequence d'envoi des intents WASD |
| `IMMOBILIZE_STOP_RATE` | 2Hz | **V2.2**: Frequence Stop pendant CC |

---

## Recommandations serveur

### Protection anti-spam (indispensable)

Meme si le client est correct, un client modifie peut spam. Le serveur doit:

1. **Rate limit intents**: max 20/s par client
2. **Coalescing**: appliquer seulement le dernier intent recu avant chaque tick
3. **Validation seqId**: drop les intents trop vieux ou out-of-order

### Collisions dynamiques

Sans pathfinding, les collisions avec d'autres entites sont geres serveur-side.
Le client ne peut pas les predire, donc les offsets peuvent augmenter.
Les seuils "accelerated" (80-250) doivent tolerer ce cas.

### lastProcessedSeq

Les snapshots doivent inclure `lastProcessedSeq` (dernier seqId d'intent traite).
Cela permet:
- Mesurer l'input lag (seqId actuel - lastProcessedSeq)
- Correlater corrections avec inputs
- Debug des problemes de sync

---

## Comparaison avec FPS-style

| Aspect | FPS-style | LoL-style |
|--------|-----------|-----------|
| **Input** | Direction brute (WASD) | Intentions (MoveTo, Stop) |
| **Prediction** | Full client-side + rollback | Anticipation visuelle (pas de rollback) |
| **Reconciliation** | Replay des inputs | Correction de l'offset visuel |
| **Autorite** | Client predit, serveur valide | Serveur decide, client affiche |
| **Latence ressentie** | Immediate (prediction) | Retardee (~100ms buffer) |
| **Rubber-banding** | Visible sur misprediction | Gere par correction smooth |

---

## Tests manuels

### Verification de base

- [ ] Clic droit -> MoveTo envoye immediatement
- [ ] WASD -> MoveTo envoyes a ~10Hz vers lookahead
- [ ] Relacher WASD -> Stop envoye
- [ ] Interpolation smooth entre snapshots
- [ ] Extrapolation courte si manque de snapshots (<150ms)

### Correction visuelle

- [ ] Gap < 20: Correction invisible
- [ ] 20 < gap < 80: Rubber-band perceptible mais smooth
- [ ] Gap >= 80 (ou 250 max): Snap immediat

### Cas speciaux (V2.2/V2.3)

- [ ] CC immobilisant (root/stun): Snap + lock de l'offset + Stop a 2Hz
- [ ] Blink/teleport: SnapHard + purge selective
- [ ] Jitter eleve: Buffer s'adapte (80-160ms)
- [ ] Apres teleport: basePrev reset, pas d'absorb enorme
- [ ] **V2.3**: Pendant extrapolation: pas d'absorb (Quality != Interpolated)

---

## Debug

### Informations disponibles

```csharp
// Dans NetworkClient
float gap = networkClient.GetCurrentGap();
Vector3 basePos = networkClient.GetBasePosition();
Vector3 offset = networkClient.GetVisualOffset();
Vector3 visualPos = networkClient.GetVisualPosition();
float speed = networkClient.GetCurrentSpeed();
bool immobilized = networkClient.IsImmobilized();  // V2.2

// Dans VisualPositionManager
string debug = visualPositionManager.GetDebugInfo();
bool isImmob = visualPositionManager.IsImmobilized;  // V2.2

// Dans IntentBuilder
bool locked = intentBuilder.IsImmobilizeLocked;  // V2.2

// V2.3: Dans BaseInterpolator
BasePosQuality quality = interpolator.Quality;
```

### Metriques a surveiller

1. **Gap moyen**: Devrait rester < 20 en jeu normal
2. **Buffer adaptatif**: 80-160ms selon jitter
3. **Frequence de snap**: Devrait etre rare (<1% des frames)
4. **Extrapolation**: Devrait etre rare (< 5% du temps)
5. **V2.2 - Immobilize lock active**: Verifie que Stop est bien emis a 2Hz pendant CC
6. **V2.3 - BasePosQuality**: Surveiller le ratio Interpolated vs Extrapolated

---

## Historique

- **v2.3**: Horizon reduit (150ms), BasePosQuality, absorb conditionnel, clarifications serveur
- **v2.2**: Corrections bugs (basePrev reset, purge selective, immobilize lock, plafond largeGap)
- **v2.1**: Edge cases (CC, blink, teleport, discontinuites)
- **v2.0**: Architecture basePos + visualOffset
- **v1.0**: Implementation initiale VisualExtrapolator
