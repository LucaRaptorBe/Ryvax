# Tugboat: Chaîne d'appel du pump LiteNetLib (serveur)

## 📍 Call Stack complet

### 1. FishNet TimeManager → Tugboat
**Fichier:** `Tugboat.cs:134`
```csharp
networkManager.TimeManager.OnUpdate += TimeManager_OnUpdate;
```
**Fréquence:** Chaque frame Unity (Update)

---

### 2. Tugboat.TimeManager_OnUpdate()
**Fichier:** `Tugboat.cs:222-226`
```csharp
private void TimeManager_OnUpdate()
{
    ServerSocket?.PollSocket();  // ← Pump LiteNetLib
    ClientSocket?.PollSocket();
}
```
**Appelé:** Chaque Update (pas FixedUpdate!)

---

### 3. ServerSocket.PollSocket()
**Fichier:** `ServerSocket.cs:116-119`
```csharp
internal void PollSocket()
{
    base.PollSocket(NetManager);
}
```

---

### 4. CommonSocket.PollSocket()
**Fichier:** `CommonSocket.cs:149-152`
```csharp
internal void PollSocket(NetManager nm)
{
    nm?.PollEvents();  // ← APPEL LITENETLIB!
}
```

---

### 5. LiteNetLib NetManager.PollEvents()
**Fichier:** `LiteNetLib/NetManager.cs:1401`
```csharp
public void PollEvents(int maxProcessedEvents = 0)
{
    // Process incoming network events
    // Calls callbacks for received packets
}
```
**Ce qui se passe:**
- Lit les sockets UDP
- Décode les packets LiteNetLib
- Appelle les callbacks (NetworkReceiveEvent)
- Les packets sont mis dans la queue `_incoming`

---

## 📊 Séparation Pump vs Process

### A. PUMP (lecture socket) → `PollSocket()` @ Update
**Fréquence:** Chaque frame (~60-120Hz selon FPS)
**Action:** `NetManager.PollEvents()` lit les sockets UDP
**Résultat:** Packets stockés dans `_incoming` queue

### B. PROCESS (traitement) → `IterateIncoming()` @ variable
**Fréquence:** Appelé par FishNet TimeManager (peut être limité)
**Action:** Dépile `_incoming` et dispatch vers handlers
**Fichier:** `ServerSocket.cs:469-511`

```csharp
internal void IterateIncoming()
{
    // Handle packets
    while (_incoming.TryDequeue(out Packet incoming))
    {
        ServerReceivedDataArgs dataArgs = new(...);
        Transport.HandleServerReceivedDataArgs(dataArgs);
    }
}
```

---

## 🔍 Point clé pour le délai 33ms

### Timeline actuelle:
```
t=0ms:   Update → PollSocket() → PollEvents()
         ↓ Packet arrive dans _incoming queue

t=???:   IterateIncoming() est appelé
         ↓ Packet traité et envoyé au handler FishNetAdapter

t=33ms:  [TRANSPORT RECV] log apparaît
```

**Question:** Quand exactement `IterateIncoming()` est-il appelé?

### Options:
1. **Appelé immédiatement après PollSocket** dans TimeManager_OnUpdate
   - Si oui: Pas de délai entre pump et process
   - Logs devraient montrer recv ~immédiatement après socket send

2. **Appelé plus tard** (par ex. dans FixedUpdate via ForceIterateIncoming)
   - Si oui: Délai = temps d'attente jusqu'au prochain appel
   - Expliquerait le 33ms delay

---

## ✅ RÉPONSE TROUVÉE: Cause du délai 33ms

### Call chain complet pour IterateIncoming (PROCESS):

**1. Unity Update**
```
NetworkManager.Update()
```

**2. TimeManager.TickUpdate()**
**Fichier:** `TimeManager.cs:367-392`
```csharp
internal void TickUpdate()
{
    IncreaseTick();  // Ligne 392
}
```

**3. TimeManager.IncreaseTick()**
**Fichier:** `TimeManager.cs:687-774`
```csharp
private void IncreaseTick()
{
    _elapsedTickTime += Time.unscaledDeltaTime;
    FrameTicked = _elapsedTickTime >= timePerSimulation;  // Ligne 702

    do {
        // ⚠️ CONDITION CLÉ:
        if (frameTicked || variableTiming)
            TryIterateData(true);  // Ligne 733 ← ICI!
    } while (_elapsedTickTime >= timePerSimulation);
}
```

**4. TimeManager.TryIterateData(true)**
**Fichier:** `TimeManager.cs:1094-1118`
```csharp
private void TryIterateData(bool incoming)
{
    if (incoming) {
        NetworkManager.TransportManager.IterateIncoming(asServer: true);   // Ligne 1111
        NetworkManager.TransportManager.IterateIncoming(asServer: false);  // Ligne 1112
    }
}
```

**5. Tugboat.IterateIncoming(asServer: true)** → **ServerSocket.IterateIncoming()**

---

## 🔴 LE PROBLÈME EXACT

### Pump vs Process:

| Action | Méthode | Fréquence | Quand |
|--------|---------|-----------|-------|
| **PUMP** (lecture socket) | `PollEvents()` | Chaque frame | ~60-120 FPS |
| **PROCESS** (traitement) | `IterateIncoming()` | Chaque **tick** | ~30 Hz (33ms) |

### Code clé (ligne 733):
```csharp
if (frameTicked || variableTiming)
    TryIterateData(true);
```

**`frameTicked` est `true` seulement quand:**
```csharp
_elapsedTickTime >= timePerSimulation  // Ligne 702
```

→ Si `timePerSimulation` = 33ms (30Hz tick rate)
→ `IterateIncoming()` est appelé seulement **1× toutes les 33ms**

---

## 💥 Résultat

**Timeline réelle:**

```
t=0ms:   Update → PollEvents() lit socket UDP
         ↓ Packet stocké dans _incoming queue
         ↓ MAIS frameTicked = false (pas encore 33ms écoulés)
         ↓ TryIterateData(true) SKIPPÉ!

t=16ms:  Update → PollEvents() (refresh)
         ↓ Packet toujours dans queue
         ↓ frameTicked = false
         ↓ TryIterateData(true) SKIPPÉ!

t=33ms:  Update → frameTicked = TRUE!
         ↓ TryIterateData(true) APPELÉ
         ↓ IterateIncoming() dépile _incoming
         ↓ [TRANSPORT RECV] log apparaît

```

**C'est exactement le délai de 33ms qu'on observe dans les logs!**

---

## ✅ Solution

Notre `ForceIterateIncoming()` dans `ServerGameLoop.FixedUpdate()` devrait forcer le traitement immédiat, mais il semble que la condition `frameTicked` bloque toujours l'itération.

**Options:**
1. Appeler `IterateIncoming()` directement sans passer par `TryIterateData`
2. Augmenter le tick rate à 60Hz pour réduire le délai
3. Bypass la condition `frameTicked` pour les inputs critiques
