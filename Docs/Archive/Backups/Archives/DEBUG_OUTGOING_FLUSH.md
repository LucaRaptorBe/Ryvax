# Debug: Pourquoi ForceIterateOutgoing() ne flush pas?

## ✅ Logs ajoutés

### A) FishNetAdapter.ForceIterateOutgoing()
**Fichier:** `FishNetAdapter.cs:576-604`

```
[TIME] [FORCE OUTGOING] Called frame=N
[TIME] [FORCE OUTGOING] Client - BEFORE IterateOutgoing (IsClientStarted=true)
[TIME] [FORCE OUTGOING] Client - AFTER IterateOutgoing
```

### B) ClientSocket.DequeueOutgoing()
**Fichier:** `ClientSocket.cs:185-199`

```
[TIME] [CLIENT DEQUEUE] Entered - queue count=N frame=F
[TIME] [CLIENT DEQUEUE] Flushing N packets to peer
```

---

## 🔍 Scénarios possibles et diagnostic

### Scénario 1: ForceIterateOutgoing PAS APPELÉ
**Logs attendus:**
```
[1.481] [SEND ENQUEUE] seq=1
(pas de [FORCE OUTGOING])
[1.500] [CLIENT DEQUEUE] count=1  ← FishNet tick flush
[1.500] [SOCKET SEND] seq=1
```

**Diagnostic:** NetworkClient.Update() ne s'exécute pas ou guard bloque
**Fix:** Vérifier Update() et garde frameCount

---

### Scénario 2: ForceIterateOutgoing APPELÉ mais queue VIDE
**Logs attendus:**
```
[1.481] [SEND ENQUEUE] seq=1
[1.481] [FORCE OUTGOING] Called frame=100
[1.481] [FORCE OUTGOING] Client - BEFORE IterateOutgoing
[1.481] [CLIENT DEQUEUE] Entered - queue count=0  ← VIDE!
[1.481] [FORCE OUTGOING] Client - AFTER IterateOutgoing
...
[1.500] [CLIENT DEQUEUE] count=1  ← Packet apparaît plus tard
[1.500] [SOCKET SEND] seq=1
```

**Diagnostic:** Ordre d'exécution - packet enqueued APRÈS ForceIterateOutgoing
**Cause probable:** SendInputUpdate() appelé après ForceIterateOutgoing dans Update
**Fix:** Inverser ordre ou flush dans SendInputUpdate()

---

### Scénario 3: ForceIterateOutgoing APPELÉ mais IterateOutgoing SKIP
**Logs attendus:**
```
[1.481] [SEND ENQUEUE] seq=1
[1.481] [FORCE OUTGOING] Called frame=100
[1.481] [FORCE OUTGOING] Client - BEFORE IterateOutgoing
(pas de [CLIENT DEQUEUE])  ← Jamais entré!
[1.481] [FORCE OUTGOING] Client - AFTER IterateOutgoing
...
[1.500] [CLIENT DEQUEUE] count=1
[1.500] [SOCKET SEND] seq=1
```

**Diagnostic:** Tugboat/FishNet a un garde qui empêche IterateOutgoing
**Cause probable:** "Already iterated this frame" ou état interne
**Fix:** Chercher garde dans Tugboat.IterateOutgoing ou ClientSocket

---

### Scénario 4: ForceIterateOutgoing FLUSH mais FishNet RE-FLUSH
**Logs attendus:**
```
[1.481] [SEND ENQUEUE] seq=1
[1.481] [FORCE OUTGOING] Called frame=100
[1.481] [FORCE OUTGOING] Client - BEFORE IterateOutgoing
[1.481] [CLIENT DEQUEUE] count=1  ← Flush notre packet
[1.481] [SOCKET SEND] seq=1  ← Immédiat!
[1.481] [FORCE OUTGOING] Client - AFTER IterateOutgoing
...
[1.500] [CLIENT DEQUEUE] count=0  ← FishNet re-flush (vide)
```

**Diagnostic:** Notre flush FONCTIONNE! Mais garde frameCount rate
**Fix:** Garde fonctionne mal, besoin meilleure stratégie

---

## 🎯 Hypothèse 4 (probable): Garde FishNet

### Chercher dans Tugboat.cs:
```csharp
public override void IterateOutgoing(bool asServer)
{
    // Garde possible ici?
    if (_lastOutgoingFrame == Time.frameCount)
        return;  // ← Empêche flush multiple!

    _lastOutgoingFrame = Time.frameCount;

    if (asServer)
        ServerSocket.IterateOutgoing();
    else
        ClientSocket.IterateOutgoing();
}
```

**Si cette garde existe:**
- FishNet empêche flush multiple par frame
- Notre ForceIterateOutgoing est no-op si FishNet a déjà flush
- **Mais:** On flush AVANT FishNet, donc devrait fonctionner

---

## 📋 Actions selon résultat

### Si Scénario 1:
→ Fixer Update() ou garde

### Si Scénario 2:
→ Appeler ForceIterateOutgoing DANS SendInputUpdate (après enqueue)

### Si Scénario 3:
→ Chercher et bypasser garde FishNet

### Si Scénario 4:
→ Fix fonctionne, optimiser garde

---

## 🔬 Prochaine run

Observer les logs pour identifier le scénario exact, puis appliquer le fix correspondant.
