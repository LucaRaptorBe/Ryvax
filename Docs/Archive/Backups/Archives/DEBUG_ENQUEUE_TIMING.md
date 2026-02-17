# Debug: Quand l'enqueue se fait-il réellement?

## ✅ Log ajouté

**CommonSocket.cs:80-84**
```csharp
int queueCountBefore = queue.Count;
UnityEngine.Debug.Log($"[COMMON SOCKET SEND] Enqueue frame={frameCount} queueBefore={count} size={bytes}");
queue.Enqueue(outgoing);
```

**Capture:**
- `frame` - Frame Unity où l'enqueue se produit
- `queueBefore` - Taille de la queue AVANT enqueue
- `size` - Taille du packet

---

## 🎯 Scénarios possibles

### Scénario A: Enqueue IMMÉDIAT (attendu selon le code)
```
Frame 330:
  [5.597] [SEND ENQUEUE] seq=1
  [5.597] [COMMON SOCKET SEND] Enqueue frame=330 queueBefore=0  ← Même frame!
  [5.597] [FORCE OUTGOING] frame=330 (LateUpdate)
  [5.597] [CLIENT DEQUEUE] count=1  ← Devrait voir le packet!
  [5.597] [SOCKET SEND] seq=1
```

**Si ce scénario:** Le fix LateUpdate DEVRAIT fonctionner!
**Si ça ne fonctionne PAS:** Il y a un autre problème (garde? multiple queues?)

---

### Scénario B: Enqueue DIFFÉRÉ (explique les logs actuels)
```
Frame 330:
  [5.597] [SEND ENQUEUE] seq=1
  (pas de [COMMON SOCKET SEND])  ← Pas appelé!
  [5.597] [FORCE OUTGOING] frame=330
  [5.597] [CLIENT DEQUEUE] count=0

Frame 332:
  [5.630] [COMMON SOCKET SEND] Enqueue frame=332  ← 2 frames plus tard!
  [5.630] [CLIENT DEQUEUE] count=1
```

**Si ce scénario:** Il y a une couche intermédiaire qui retarde l'appel
**Action:** Chercher entre Broadcast() et SendToServer() ce qui buffer

---

### Scénario C: Enqueue IMMÉDIAT mais queue DIFFÉRENTE
```
Frame 330:
  [5.597] [SEND ENQUEUE] seq=1
  [5.597] [COMMON SOCKET SEND] Enqueue frame=330 queueBefore=0
  [5.597] [CLIENT DEQUEUE] count=0  ← Queue différente?!
```

**Si ce scénario:** CommonSocket.Send enqueue dans une queue, mais DequeueOutgoing lit une autre
**Action:** Vérifier que `queue` passé à Send() == `_outgoing` dans ClientSocket

---

## 🔬 Analyse à faire

### 1. Confirmer le frame d'enqueue
- Si frame=330 → enqueue immédiat ✓
- Si frame=332 → enqueue différé (problème inconnu)

### 2. Vérifier queue count avant/après
- Si queueBefore=0 puis CLIENT DEQUEUE count=1 → même queue ✓
- Si queueBefore=0 MAIS CLIENT DEQUEUE count=0 → queues différentes!

### 3. Compter les appels
- Combien de [COMMON SOCKET SEND] par frame?
- Est-ce que chaque [SEND ENQUEUE] a un [COMMON SOCKET SEND] correspondant?

---

## 📋 Prochaine étape selon résultat

### Si Scénario A (enqueue immédiat, LateUpdate devrait fonctionner):
→ Investiguer pourquoi CLIENT DEQUEUE ne voit pas le packet
→ Possibles causes:
  - Garde dans IterateOutgoing qui skip
  - Queue reference différente
  - Packet consommé entre enqueue et dequeue

### Si Scénario B (enqueue différé):
→ Chercher couche intermédiaire qui buffer
→ Instrumenter TransportManager.SendToServer
→ Ou passer à Option 3 (bypass complet)

### Si Scénario C (queues différentes):
→ Bug architectural, passer à Option 3 (enqueue direct)

---

## ✅ Ce qu'on va apprendre

**Cette instrumentation va DÉFINITIVEMENT révéler:**
- Quand l'enqueue se produit (frame exacte)
- Si le problème est avant l'enqueue (délai d'appel)
- Ou après l'enqueue (lecture mauvaise queue / garde)

**Puis on saura exactement quelle solution implémenter.**
