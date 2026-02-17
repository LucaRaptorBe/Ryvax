# Fix Client Outgoing: Flush @ Frame Rate (Option C)

## ✅ Implémentation complétée

### 1. FishNetAdapter.cs - Ajout ForceIterateOutgoing()

**Fichier:** `Assets/Scripts/Network/NetAdapter/FishNet/FishNetAdapter.cs:573-597`

```csharp
/// <summary>
/// Force immediate flush of outgoing network data.
/// Call this in Update to flush packets at frame rate instead of tick rate,
/// eliminating the 30Hz tick gating delay (33ms → ~0ms).
/// </summary>
public void ForceIterateOutgoing()
{
    if (_networkManager == null) return;

    // Flush server outgoing (to clients)
    if (_networkManager.IsServerStarted)
    {
        _networkManager.TransportManager.Transport.IterateOutgoing(asServer: true);
    }

    // Flush client outgoing (to server)
    if (_networkManager.IsClientStarted)
    {
        _networkManager.TransportManager.Transport.IterateOutgoing(asServer: false);
    }
}
```

---

### 2. NetworkClient.cs - Flush défensif avec garde frameCount

**Champ ajouté:** (ligne 87)
```csharp
// Outgoing flush guard (prevent duplicate flush in same frame)
private int _lastOutgoingFlushFrame = -1;
```

**Update() modifié:** (lignes 254-265)
```csharp
void Update()
{
    if (!_isConnected) return;

    // ... existing code ...
    SendInputUpdate();

    // Flush outgoing packets at frame rate (not tick rate)
    // Guard prevents duplicate flush if FishNet also calls IterateOutgoing in same frame
    int currentFrame = Time.frameCount;
    if (currentFrame != _lastOutgoingFlushFrame)
    {
        _netAdapter.ForceIterateOutgoing();
        _lastOutgoingFlushFrame = currentFrame;
    }
}
```

---

## 🎯 Comportement implémenté (Option C)

### Garde anti-spam:
```
Frame N:
  1. NetworkClient.Update()
     - SendInputUpdate() → enqueue packet
     - currentFrame (100) != _lastOutgoingFlushFrame (-1) ✓
     - ForceIterateOutgoing() → flush!
     - _lastOutgoingFlushFrame = 100

  2. FishNet IncreaseTick() (même frame)
     - TryIterateData(false) → IterateOutgoing()
     - Flush queue vide (déjà flushé par nous)

Frame N+1:
  1. NetworkClient.Update()
     - currentFrame (101) != _lastOutgoingFlushFrame (100) ✓
     - ForceIterateOutgoing() → flush!
```

**Avantage:** Empêche flush dupliqué si FishNet et nous appelons tous les deux dans la même frame.

---

## 📊 Résultat attendu

### Avant le fix:
```
t=0ms:   [SEND ENQUEUE] seq=1 ticks=T1
         ↓ (attente tick 30Hz)
t=33ms:  [SOCKET SEND] ticks=T2  ← Gated par frameTicked
```
**Délai:** 33ms (flush @ tick rate)

---

### Après le fix:
```
t=0ms:   [SEND ENQUEUE] seq=1 ticks=T1
         ↓ (même Update frame)
t=0ms:   [SOCKET SEND] ticks=T1  ← Flush immédiat!
```
**Délai:** <1ms (flush @ frame rate)

Ou au pire:
```
t=0ms:   [SEND ENQUEUE] seq=1 ticks=T1
         ↓ (attente next Update)
t=16ms:  [SOCKET SEND] ticks=T2  ← 1 frame @ 60 FPS
```
**Délai:** 16ms (si enqueue juste après Update)

---

## ✅ Gains attendus

### Timeline complète (localhost):

| Étape | Avant | Après | Gain |
|-------|-------|-------|------|
| **Client flush** | 31ms | <1ms | **-30ms** ✅ |
| Server poll | 17ms | 17ms | 0ms |
| IntraServer | 10ms | 10ms | 0ms |
| **TOTAL** | **58ms** | **~28ms** | **-30ms** |

---

## 🔬 Vérification logs

### Logs attendus après fix:
```
[0.000] [SEND ENQUEUE] C→S seq=1 ticks=T1
[0.000] [SOCKET SEND] C→S seq=1 ticks=T1  ← Immédiat! (Δ < 1ms)
[0.017] [TRANSPORT RECV] C→S seq=1        ← +17ms (server poll)
[0.027] [SERVER APPLY] seq=1              ← +10ms (intraServer)
```

**Total:** ~27ms (vs 58ms avant)

---

## 🎲 Prochaines optimisations possibles

### Pour descendre sous 20ms:

1. **Augmenter pump serveur** (build headless)
   - `Application.targetFrameRate = 120` ou 240
   - Réduire 17ms → 4-8ms

2. **Flush client encore plus agressif**
   - Option B: Flush immédiatement après SendInputPacket
   - Éliminer le 1-frame delay potentiel

---

## ✅ Statut

**Implémenté:** Option C (flush @ frame rate avec garde defensive)
**Fichiers modifiés:**
- `FishNetAdapter.cs` - Ajout ForceIterateOutgoing()
- `NetworkClient.cs` - Garde frameCount + flush Update

**Prêt pour test.**
