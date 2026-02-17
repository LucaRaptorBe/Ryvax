# Instrumentation Tugboat: Mesurer le flush delay

## ✅ Logs ajoutés

### Client (ClientSocket.cs:211)
```csharp
[SOCKET SEND] ticks={timestamp} size={bytes}
```
**Moment:** Juste avant `peer.Send()` (envoi socket réel)

### Serveur (ServerSocket.cs:405-413)
```csharp
[SERVER SOCKET SEND] ticks={timestamp} size={bytes} broadcast={true/false}
```
**Moment:** Juste avant `peer.Send()` ou `NetManager.SendToAll()`

---

## 🎯 Mesures à effectuer

### A. Flush delay client (ENQUEUE → SOCKET SEND)

**Timeline attendue:**
```
[SEND ENQUEUE] ticks=T1 → FishNetAdapter.SendInputPacket()
         ↓ (packet dans _outgoing queue)
[SOCKET SEND] ticks=T2  → peer.Send() dans ClientSocket
         ↓ (réseau localhost ~0.1ms)
[TRANSPORT RECV] ticks=T3 → Serveur reçoit
```

**Calcul du flush delay:**
```
flushDelayMs = (T2 - T1) * 1000 / Stopwatch.Frequency
```

**Interprétation:**
- Si `< 1ms`: Flush immédiat ✅
- Si `≈ 16-33ms`: Flush 1 frame plus tard ⚠️
- Si `≈ 50ms`: Problème de scheduling ❌

---

### B. Latence réseau réelle (SOCKET SEND → TRANSPORT RECV)

**Calcul:**
```
networkLatencyMs = (T3 - T2) * 1000 / Stopwatch.Frequency
```

**Attendu sur localhost:** `< 1ms`

---

### C. Flush delay serveur (BROADCAST → SOCKET SEND)

**Timeline attendue:**
```
[SERVER BROADCAST] → ServerGameLoop enqueue snapshots
         ↓ (packet dans _outgoing queue)
[SERVER SOCKET SEND] → peer.Send() réel
```

**Attendu:** Devrait être immédiat si `IterateOutgoing()` est appelé juste après

---

## 📊 Exemple d'analyse

### Logs typiques:
```
[1.774] [SEND ENQUEUE] ticks=117190502622
[1.824] [SOCKET SEND] ticks=117190986000  ← +50ms de flush delay!
[1.824] [TRANSPORT RECV] ticks=117190986097 ← +0.09ms réseau OK
```

**Diagnostic:**
- Enqueue→SocketSend = 50ms → **Problème de flush delay**
- SocketSend→Recv = 0.09ms → Réseau localhost OK

---

## 🔧 Fix potentiel

Si le flush delay est confirmé (~50ms), options:

### Option 1: Flush immédiat après Broadcast
```csharp
// Dans FishNetAdapter.SendInputPacket()
Broadcast(inputPacket);
_networkManager.TransportManager.Transport.IterateOutgoing(asServer: false);
```

### Option 2: Augmenter la fréquence de IterateOutgoing
- Actuellement appelé dans TimeManager.TickUpdate()
- Peut être limité à une certaine fréquence
- Vérifier `TimeManager.cs` pour la logique de polling

### Option 3: Utiliser le mode immédiat de LiteNetLib
- LiteNetLib supporte `SendImmediate()` qui bypass la queue
- Nécessite modification de Tugboat

---

## 🎲 Prochaine étape

1. **Relancer Unity en mode Play**
2. **Observer les nouveaux logs:**
   - `[SEND ENQUEUE]`
   - `[SOCKET SEND]` ← NOUVEAU
   - `[TRANSPORT RECV]`
   - `[SERVER SOCKET SEND]` ← NOUVEAU

3. **Calculer les deltas** pour identifier le goulot d'étranglement exact

4. **Appliquer le fix approprié** selon les résultats
