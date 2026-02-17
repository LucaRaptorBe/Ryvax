# Client Outgoing: Call Chain Exact (31ms delay)

## 📍 Call Stack complet - IterateOutgoing côté client

### 1. Unity Update
```
NetworkManager.Update()
```

### 2. TimeManager.TickUpdate()
**Fichier:** `TimeManager.cs:367`
```csharp
internal void TickUpdate()
{
    IncreaseTick();
}
```

### 3. TimeManager.IncreaseTick()
**Fichier:** `TimeManager.cs:687-774`
```csharp
private void IncreaseTick()
{
    _elapsedTickTime += Time.unscaledDeltaTime;
    FrameTicked = _elapsedTickTime >= timePerSimulation;  // Ligne 702

    do {
        // ... OnPreTick, OnTick, Simulation ...

        // ⚠️ CONDITION CLÉ POUR OUTGOING:
        if (frameTicked || variableTiming)
            TryIterateData(false);  // Ligne 765-766 ← ICI!

    } while (_elapsedTickTime >= timePerSimulation);
}
```

### 4. TimeManager.TryIterateData(false)
**Fichier:** `TimeManager.cs:1094-1118`
```csharp
private void TryIterateData(bool incoming)
{
    if (incoming) {
        // ... incoming logic ...
    }
    else {
        NetworkManager.TransportManager.IterateOutgoing(asServer: true);   // Ligne 1116
        NetworkManager.TransportManager.IterateOutgoing(asServer: false);  // Ligne 1117 ← CLIENT!
    }
}
```

### 5. Tugboat.IterateOutgoing(asServer: false)
**Fichier:** `Tugboat.cs:244-249`
```csharp
public override void IterateOutgoing(bool asServer)
{
    if (asServer)
        ServerSocket.IterateOutgoing();
    else
        ClientSocket.IterateOutgoing();  // ← ICI!
}
```

### 6. ClientSocket.IterateOutgoing() → DequeueOutgoing() → peer.Send()
**Fichier:** `ClientSocket.cs:221-224`
```csharp
internal void IterateOutgoing()
{
    DequeueOutgoing();  // ← Flush _outgoing queue vers socket
}
```

---

## 🔴 LE PROBLÈME IDENTIQUE AU SERVEUR

### Code clé (ligne 765-766):
```csharp
// Send out data.
if (frameTicked || variableTiming)
    TryIterateData(false);  // ← OUTGOING GATED PAR TICK!
```

**`frameTicked` est `true` seulement quand:**
```csharp
_elapsedTickTime >= timePerSimulation  // Ligne 702
```

→ Si `timePerSimulation` = 33ms (30Hz tick rate)
→ `IterateOutgoing()` est appelé seulement **1× toutes les 33ms**

---

## 📊 Timeline actuelle (31ms flush delay)

```
t=0ms:    SendInputPacket() → enqueue dans _outgoing
          ↓ (packet en queue)
          ↓ frameTicked = false
          ↓ TryIterateData(false) SKIPPÉ!

t=16ms:   Update
          ↓ frameTicked = false
          ↓ TryIterateData(false) SKIPPÉ!

t=33ms:   Update → frameTicked = TRUE!
          ↓ TryIterateData(false) APPELÉ
          ↓ IterateOutgoing() → DequeueOutgoing()
          ↓ [SOCKET SEND] apparaît
```

**Résultat:** Packet enqueued à t=0, flushed à t=33ms → **31ms delay**

---

## ✅ FIX: Flush outgoing @ frame rate

### Option A: Update dans NetworkClient (recommandé)

**Fichier:** `NetworkClient.cs`
```csharp
private void Update()
{
    if (!_isConnected) return;

    // Deferred local player initialization
    if (_pendingLocalPlayerInit && LocalClientId >= 0 && _localEntityId == 0)
    {
        TryInitializeLocalPlayer();
    }

    // Unified timing
    _timeSync.Update(Time.deltaTime);
    _visualPositionManager?.Update(Time.deltaTime);

    // Send pending intent and event commands
    SendInputUpdate();

    // NOUVEAU: Flush outgoing chaque frame (pas @ tick)
    _netAdapter.ForceIterateOutgoing();
}
```

### Option B: Flush immédiat après enqueue

**Fichier:** `NetworkClient.cs` dans `SendInputUpdate()`
```csharp
private void SendInputUpdate()
{
    // ... create packet ...

    _netAdapter.SendInputPacket(packet);  // Enqueue
    _netAdapter.ForceIterateOutgoing();   // Flush immédiat!

    // Clear pending intent after sending
    _pendingIntent = null;
}
```

### Option C: Hybrid (1× par frame max)

```csharp
private float _lastOutgoingFlushTime;

private void Update()
{
    // ... existing code ...

    SendInputUpdate();

    // Flush outgoing max 1× par frame (évite spam)
    if (Time.time > _lastOutgoingFlushTime)
    {
        _netAdapter.ForceIterateOutgoing();
        _lastOutgoingFlushTime = Time.time;
    }
}
```

---

## 🎯 Résultat attendu

### Avant le fix:
```
[0.000] SEND ENQUEUE ticks=T1
[0.033] SOCKET SEND ticks=T2  ← +33ms delay
```

### Après le fix:
```
[0.000] SEND ENQUEUE ticks=T1
[0.016] SOCKET SEND ticks=T2  ← +16ms (1 frame @ 60 FPS)
```

Ou avec flush immédiat:
```
[0.000] SEND ENQUEUE ticks=T1
[0.000] SOCKET SEND ticks=T1  ← +0ms (immédiat!)
```

---

## 📋 Implémentation requise

### 1. Ajouter ForceIterateOutgoing() dans FishNetAdapter

**Fichier:** `FishNetAdapter.cs`
```csharp
/// <summary>
/// Force immediate flush of outgoing packets (bypass tick gating).
/// Call from Update to flush at frame rate instead of tick rate.
/// </summary>
public void ForceIterateOutgoing()
{
    if (_networkManager.IsClientStarted)
        _networkManager.TransportManager.Transport.IterateOutgoing(asServer: false);
    if (_networkManager.IsServerStarted)
        _networkManager.TransportManager.Transport.IterateOutgoing(asServer: true);
}
```

### 2. Appeler dans NetworkClient.Update()

Choisir Option A, B ou C selon préférence.

---

## ✅ Confirmation

**Problème:** MÊME cause que serveur incoming (gating par `frameTicked`)
**Fix:** MÊME solution (flush @ frame rate ou immédiat)
**Gain attendu:** 31ms → <1ms (flush immédiat) ou ~16ms (@ 60 FPS)

**Call site verrouillé:** `TimeManager.cs:765-766`
