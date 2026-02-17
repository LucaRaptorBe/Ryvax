# Diagnostic: 50ms de latence apparente

## Hypothèse 1: Cross-horloge (très probable)

**Problème:** `Time.time` client et serveur ont des origines différentes.

**Test:** METRIC B - Ping RTT avec Stopwatch (horloge unique côté client)

**Comment:**
1. Attacher `NetworkPingMeasure` au GameObject `NetworkClient` dans la scène
2. Enable Ping dans l'Inspector (déjà à `true` par défaut)
3. Observer les logs `[METRIC B] [RTT]`

**Résultat attendu:**
- Si RTT < 1ms → Les 50ms sont FICTIFS (cross-horloge)
- Si RTT ≈ 50ms → Les 50ms sont RÉELS (scheduling/buffering)

---

## Hypothèse 2: Flush outgoing retardé (probable aussi)

**Problème:** `Broadcast()` enqueue, mais `IterateOutgoing()` flush 1 frame plus tard.

**Timeline actuelle:**
```
t=1.408  [SEND ENQUEUE] → ClientManager.Broadcast()
         ↓ (packet dans _outgoing queue)
t=1.4XX  TimeManager appelle IterateOutgoing()
         → DequeueOutgoing()
         → peer.Send() ← VRAI envoi socket
         ↓ (réseau localhost ~0.1ms)
t=1.458  [TRANSPORT RECV] serveur reçoit
```

**Logs pour vérifier:**
- `[SEND ENQUEUE]` = moment où on appelle Broadcast (déjà ajouté ✅)
- `[SOCKET SEND]` = moment où peer.Send() est appelé (à ajouter dans Tugboat)
- `[TRANSPORT RECV]` = moment où serveur reçoit (déjà présent ✅)

**Si Enqueue→SocketSend = 50ms:** C'est le flush delay (IterateOutgoing trop tard)
**Si SocketSend→Recv < 1ms:** Réseau localhost est OK

---

## Actions prioritaires

1. **Test immédiat:** Activer NetworkPingMeasure et observer RTT
   - Si RTT < 1ms → Cross-horloge confirmé, pas de vrai problème
   - Si RTT > 10ms → Problème réel de scheduling

2. **Si problème réel:** Instrumenter Tugboat pour logger le moment exact de `peer.Send()`
   - Fichier: `Tugboat/Core/ClientSocket.cs:211`
   - Ajouter log juste avant `peer.Send()`

3. **Fix potentiel:** Appeler `IterateOutgoing()` immédiatement après `Broadcast()`
   - Évite d'attendre le prochain frame
   - Réduit latency de ~1 frame
