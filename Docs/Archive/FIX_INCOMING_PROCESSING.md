# Fix: Process Incoming @ Update (pas @ Tick)

## ✅ Changement appliqué

### Avant (33ms delay):
```csharp
// ServerGameLoop.cs
private void FixedUpdate()  // 30Hz (33ms)
{
    _netAdapter.ForceIterateIncoming();  // ← Limité à 30Hz
    RunSimulation();
    BroadcastSnapshots();
}
```

**Problème:**
- Incoming processing @ 30Hz (FixedUpdate)
- Packet arrive à t=5ms → attend jusqu'à t=33ms pour être traité
- Délai inutile de ~20-30ms

---

### Après (délai minimal):
```csharp
// ServerGameLoop.cs
private void Update()  // 60-120 FPS
{
    _netAdapter.ForceIterateIncoming();  // ← Chaque frame!
}

private void FixedUpdate()  // 30Hz
{
    RunSimulation();  // Sim reste à 30Hz
    BroadcastSnapshots();
}
```

**Avantages:**
- Incoming processing @ frame rate (60-120 FPS)
- Packet arrive à t=5ms → traité à t=16ms (next frame)
- Délai réduit à ~8-16ms (au lieu de 33ms)
- Simulation reste à 30Hz (déterministe)

---

## 📊 Timeline attendue

### Avec le fix:
```
t=0ms:   Client SOCKET SEND
         ↓ (réseau localhost ~0.1ms)
t=0.1ms: Serveur socket reçoit → PollEvents() → _incoming queue
         ↓ (attente next Update)
t=16ms:  Update → ForceIterateIncoming() → TRANSPORT RECV
         ↓ Input bufferisé dans _inputQueue
t=33ms:  FixedUpdate → RunSimulation → SERVER APPLY
```

**Amélioration:**
- Avant: 33ms (SOCKET → TRANSPORT RECV)
- Après: **16ms** (SOCKET → TRANSPORT RECV)
- **Gain: ~17ms de latence en moins**

---

## 🔧 Architecture résultante

### Séparation claire:

| Composant | Fréquence | Rôle |
|-----------|-----------|------|
| **Update** | 60-120 FPS | Process incoming packets (buffer inputs) |
| **FixedUpdate** | 30 Hz | Run simulation (consume buffered inputs) |

### Flow complet:
```
1. Update (60 FPS):
   - ForceIterateIncoming()
   - Packets → _inputQueue (ConcurrentQueue)

2. FixedUpdate (30 Hz):
   - DrainInputQueueForTick(currentTick)
   - _inputQueue → _pendingNextTick (Dictionary)
   - FlushPendingMovementInputs(currentTick)
   - _pendingNextTick → Apply to simulation

3. BroadcastSnapshots (30 Hz):
   - Send snapshots to clients
```

---

## ⚠️ Considérations

### Buffering multiple inputs:
Si frame rate > tick rate (ex: 120 FPS vs 30 Hz):
- 4 frames Update pour 1 tick FixedUpdate
- Jusqu'à 4 inputs peuvent s'accumuler dans _inputQueue

**Solution existante:**
```csharp
// Last-input-wins avec Dictionary
_pendingNextTick[clientId] = newerInput;  // Remplace l'ancien
```
→ Seul le dernier input par client est appliqué (OK pour movement)

### Thread safety:
- `_inputQueue` = ConcurrentQueue ✅
- Writes @ Update thread (main)
- Reads @ FixedUpdate thread (main)
- Pas de race condition (même thread)

---

## 📈 Résultat attendu

### Logs après le fix:
```
[0.000] [SEND ENQUEUE] C→S seq=1 ticks=T1
[0.016] [SOCKET SEND] C→S seq=1 ticks=T2  (flush delay ~16ms)
[0.016] [TRANSPORT RECV] C→S seq=1 ticks=T3  (immédiat après socket send!)
[0.033] [SERVER APPLY] seq=1 (appliqué au prochain tick)
```

**Délai total:** ~33ms (16ms flush + 0ms poll + 17ms intraServer)
- **-17ms vs avant** (qui avait 50ms total)

---

## 🎯 Impact sur gameplay

### Avant:
- Input lag: ~50ms (client) + 33ms (serveur) = **83ms total**

### Après:
- Input lag: ~50ms (client) + 16ms (serveur) = **66ms total**

**Amélioration: -17ms de latency serveur** ✅

Prochaine étape: Fixer le client flush delay (16ms → immédiat)
