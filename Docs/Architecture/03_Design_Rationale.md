# Design Rationale: Why LoL-Style Netcode?

**Understanding the Trade-offs Between FPS and MOBA Network Architectures**

This document explains why Ryvax uses League of Legends-style authoritative server architecture instead of FPS-style client-side prediction, and when each approach is appropriate.

---

## The Fundamental Question

**Should the client predict future positions, or display delayed truth?**

This question drives all netcode architecture decisions. The answer depends on your game's priorities.

---

## Architecture Comparison

### FPS-Style (Client-Side Prediction + Reconciliation)

**How it works:**
1. Client simulates movement locally (predicts future)
2. Client sends inputs to server
3. Server simulates authoritatively
4. Server sends corrections back
5. Client **rollbacks** to server tick and **replays** inputs to reconcile

**Example:**
```csharp
// Client (FPS-style)
void Update()
{
    // Predict locally
    localPosition += input.direction * speed * dt;
    SendToServer(input);
}

void OnServerCorrection(serverPos, serverTick)
{
    if (Vector3.Distance(localPos, serverPos) > threshold)
    {
        // Rollback to server state
        localPosition = serverPos;
        localTick = serverTick;

        // Replay all inputs since server tick
        foreach (var input in inputBuffer.Since(serverTick))
            localPosition += input.direction * speed * FIXED_DT;
    }
}
```

**Used by:** Counter-Strike, Overwatch, Valorant, most FPS games

---

### LoL-Style (Server Authority + Visual Smoothing)

**How it works:**
1. Client sends **intents** (MoveTo, Stop, Follow), NOT position
2. Server simulates authoritatively
3. Server sends snapshots back
4. Client **interpolates** between snapshots (delayed truth)
5. Client **smooths** visual discontinuities with offset correction

**Example:**
```csharp
// Client (LoL-style)
void Update()
{
    // NO local prediction of position
    // Only send intent
    if (Input.GetKeyDown(KeyCode.W))
        SendToServer(new InputIntent { Type = MoveDir, Direction = forward });
}

void OnServerSnapshot(snapshot)
{
    // Add to interpolation buffer
    interpolator.AddSnapshot(snapshot);

    // Render interpolated position (delayed ~100ms)
    basePos = interpolator.GetBasePos(Time.time - bufferDelay);
    visualPos = basePos + visualOffset;  // Smooth corrections
}
```

**Used by:** League of Legends, Dota 2, most MOBA/RTS games

---

## Detailed Comparison

### 1. Input Handling

| Aspect | FPS-Style | LoL-Style |
|--------|-----------|-----------|
| **Client sends** | Raw input (WASD direction) | Intent (MoveTo position, Stop, Follow) |
| **Frequency** | Every frame or tick (30-60Hz) | Rate-limited (10-120Hz) |
| **Semantic level** | Low (direction vectors) | High (game actions) |

**Why it matters:**
- **FPS:** Needs raw input for precise movement prediction
- **LoL:** Higher-level intents enable server-side pathfinding, ability queueing

---

### 2. Position Authority

| Aspect | FPS-Style | LoL-Style |
|--------|-----------|-----------|
| **Who decides position?** | Client predicts, server validates | Server decides, client displays |
| **Client position source** | Local prediction | Interpolated server snapshots |
| **Correction mechanism** | Rollback + replay | Smooth offset correction |

**Example scenario:** Player walks into a wall

**FPS-Style:**
```
Client: Predict forward movement (no collision)
Server: Corrects position (hit wall)
Client: Rollback → Replay → Snap to wall
Result: Visible "rubber-band" if prediction was wrong
```

**LoL-Style:**
```
Client: Sends MoveTo(target beyond wall)
Server: Applies collision, stops at wall
Client: Interpolates to stopped position smoothly
Result: Smooth stop (but delayed ~100ms)
```

---

### 3. Perceived Latency

| Aspect | FPS-Style | LoL-Style |
|--------|-----------|-----------|
| **Position update** | Immediate (0ms) | Delayed (~50-100ms) |
| **Rotation update** | Immediate (0ms) | Immediate (0ms, local feedback) |
| **Animation update** | Immediate (0ms) | Immediate (0ms, local feedback) |

**LoL-style mitigation:**
Ryvax uses **local intent feedback** for rotation and animation:
```csharp
// PlayerView.cs:158-169
if (HasLocalMoveIntent())
{
    // Rotate immediately toward input direction
    transform.rotation = LocalIntentFeedback.GetSmoothedRotationY();
    // Play run animation immediately
    animator.SetBool("IsMoving", true);
}
```

**Result:** Player **sees** immediate response (rotation/animation), even though position is delayed.

---

### 4. Rubber-Banding

| Aspect | FPS-Style | LoL-Style |
|--------|-----------|-----------|
| **When it happens** | Misprediction (collision, ability hit) | Large desync (packet loss, high ping) |
| **Appearance** | Sudden snap back | Smooth correction (or snap if > 80 units) |
| **Frequency** | Common (every misprediction) | Rare (only extreme cases) |

**FPS-Style rubber-band:**
```
Player predicts: Run through door
Server says: Door was closed, you bounced back
Visual: Snap backward (jarring)
```

**LoL-Style rubber-band:**
```
Player sees: Smooth movement toward door (delayed)
Server says: Door was closed, stopped early
Visual: Smooth deceleration (but delayed)
```

---

### 5. Anti-Cheat Robustness

| Attack Vector | FPS-Style | LoL-Style |
|---------------|-----------|-----------|
| **Position manipulation** | Client can fake prediction | Impossible (server decides) |
| **Speed hacks** | Detectable, but client-side | Impossible (server controls velocity) |
| **Wallhacks** | Vision hack (still possible) | Vision hack (still possible) |
| **Teleport hacks** | Detectable via validation | Impossible (no client position) |

**LoL-Style advantage:**
```csharp
// Client can send ANY intent, server decides if valid
SendToServer(new InputIntent { Type = MoveTo, WorldPos = (1000, 0, 1000) });
// Server: "That's outside the map, ignored."
// Client position never changes.
```

**FPS-Style vulnerability:**
```csharp
// Client predicts position locally
localPosition = Vector3(1000, 0, 1000);  // Hack injects position
SendToServer(input);
// Server must validate, but client already "saw" teleport
```

---

### 6. Determinism & Replay

| Aspect | FPS-Style | LoL-Style |
|--------|-----------|-----------|
| **Server-side determinism** | Yes (with effort) | Yes (easier) |
| **Replay from inputs** | Difficult (need client prediction logic) | Easy (just replay server ticks) |
| **Debugging** | Complex (client + server states) | Simpler (server state only) |

**LoL-Style replay:**
```csharp
// Record server inputs + initial state
// Replay = re-run server simulation with same inputs
// Client rendering doesn't matter for replay
```

**FPS-Style replay:**
```csharp
// Must record client inputs + server corrections
// Replay = re-run client prediction + reconciliation
// Complex to match original playback
```

---

## When to Use Each Architecture

### Use FPS-Style When:

✅ **Twitch gameplay:** Sub-100ms reaction times matter (CS:GO, Overwatch)
✅ **Hitscan weapons:** Need instant feedback for aiming
✅ **High movement precision:** Bunny-hopping, wall-running, rocket jumping
✅ **Competitive FPS:** Genre expectations demand instant response

**Trade-off:** Accept complexity and rubber-banding for perceived responsiveness.

---

### Use LoL-Style When:

✅ **MOBA/RTS gameplay:** Strategic, ability-based, not twitch
✅ **Anti-cheat priority:** Competitive integrity > perceived latency
✅ **Deterministic replays:** eSports, training mode, debugging
✅ **Server-side complexity:** Pathfinding, ability interactions, RNG
✅ **Top-down perspective:** Less sensitive to latency than first-person

**Trade-off:** Accept perceived latency for simplicity and robustness.

---

## Why Ryvax Uses LoL-Style

### 1. Game Genre
Ryvax is a **MOBA/action-RPG**, not an FPS. Gameplay is:
- **Ability-based** (cooldowns, combos) not twitch-shooting
- **Top-down** perspective (less latency-sensitive)
- **Strategic** positioning over frame-perfect reactions

**Conclusion:** 50-100ms latency is acceptable, like in League of Legends.

---

### 2. Anti-Cheat Requirements

**Requirement:** Competitive integrity for ranked play.

**FPS-Style risk:**
```csharp
// Client predicts locally
localPosition += cheatEngine.InjectVelocity();  // Speed hack
// Server detects and corrects, but client already "saw" it
```

**LoL-Style immunity:**
```csharp
// Client sends intent
SendToServer(MoveTo(target));
// Server applies ONLY validated movement
// Client position = 100% server, unhackable
```

**Conclusion:** LoL-style eliminates entire classes of cheats.

---

### 3. Server-Side Complexity

**Ryvax server features:**
- Pathfinding (A* on navmesh)
- Ability interactions (knock-ups, dashes, projectiles)
- Area of Interest culling
- RNG (critical hits, proc chances)

**FPS-Style problem:**
```csharp
// Client must replicate ALL server logic for prediction
// Pathfinding? Ability physics? RNG?
// Massive client-side complexity, likely to desync
```

**LoL-Style solution:**
```csharp
// Client: "I want to go here"
// Server: "Okay, I'll pathfind and handle collisions"
// Client: "Show me the result"
// Simple, impossible to desync
```

**Conclusion:** LoL-style keeps complex logic server-only.

---

### 4. Determinism & Debugging

**Requirement:** Reproducible bugs, replay system for eSports.

**LoL-Style advantage:**
```
Record: Server inputs + initial SimWorld state
Replay: Re-run SimWorld.Tick() with same inputs
Result: Bit-identical replay (if simulation deterministic)
```

**FPS-Style challenge:**
```
Record: Client inputs + server corrections + prediction state
Replay: Re-run client prediction + reconciliation
Result: Approximate replay (prediction logic may differ)
```

**Conclusion:** LoL-style enables deterministic replays.

---

### 5. Development Complexity

**Team size:** Small indie team.

**FPS-Style complexity:**
- Client-side physics simulation
- Rollback/replay system
- Desync detection and correction
- Prediction tuning (threshold, rewind depth)
- Server validation of client predictions

**LoL-Style complexity:**
- Server simulation only
- Interpolation (standard algorithm)
- Offset correction (exponential decay)
- No rollback, no replay, no prediction validation

**Conclusion:** LoL-style is simpler to implement and maintain.

---

## The "Feels Heavy" Problem

### The Challenge

LoL-style netcode can feel "heavy" or "laggy" because position updates are delayed ~50-100ms.

**Example:**
```
T=0ms:   Player presses W
T=0ms:   Intent sent to server
T=17ms:  Server receives, applies movement
T=33ms:  Server broadcasts snapshot
T=50ms:  Client receives, updates position ← 50ms delay!
```

### The Solution: Local Feedback

Ryvax mitigates this with **immediate local feedback** for rotation and animation:

```csharp
// PlayerView.cs
void Update()
{
    // POSITION: Server-authoritative (delayed)
    transform.position = networkClient.GetVisualPosition();

    // ROTATION: Immediate local feedback
    if (networkClient.HasLocalMoveIntent())
    {
        Vector3 intent = networkClient.GetLocalMoveIntent();
        targetRotY = Mathf.Atan2(intent.x, intent.z) * Mathf.Rad2Deg;
        currentRotY = Mathf.LerpAngle(currentRotY, targetRotY, Time.deltaTime * 15f);
        transform.rotation = Quaternion.Euler(0, currentRotY, 0);
    }

    // ANIMATION: Immediate local feedback
    bool localIsMoving = networkClient.HasLocalMoveIntent();
    animator.SetBool("IsMoving", localIsMoving);
    animator.SetFloat("Speed", localIsMoving ? 1f : 0f);
}
```

**Result:**
- Player **sees** character turn immediately (0ms)
- Player **sees** run animation start immediately (0ms)
- Player **sees** position update delayed (50ms)

**Perception:** Feels responsive because visual feedback is instant, even though position lags.

**Why this works:**
- Humans perceive **rotation/animation** as "responsiveness"
- Position delay is less noticeable in top-down games
- Similar to how League of Legends feels responsive despite ~50-100ms latency

---

## Real-World Example: League of Legends

### LoL's Approach

**Confirmed facts** (from network analysis and Riot engineer talks):

1. **No client-side prediction** of position
2. **Tick rate:** ~30 Hz server, variable client render
3. **Input model:** Click-to-move (MoveTo intents)
4. **Latency:** Visible at 150+ ms, playable at 50-100 ms
5. **Correction:** Small gaps smoothed, large gaps snap

**Why it works:**
- Top-down perspective (latency less noticeable)
- Strategic gameplay (positioning > twitch reflexes)
- Immediate animation feedback (champion turns instantly)
- Anti-cheat robustness (impossible to fake position)

### Measured Behavior

**Small gap (5-20 units):**
```
Server position diverges slightly
→ Client smooths over ~100-200ms
→ Player doesn't notice
```

**Medium gap (20-80 units):**
```
Server position diverges (collision, stun)
→ Client corrects aggressively
→ Player sees "rubber-band" effect
```

**Large gap (>80-100 units):**
```
Server position diverges significantly
→ Client snaps immediately
→ Player sees teleport (high ping, packet loss)
```

**Ryvax uses the same thresholds** (from LOL.md analysis):
- Small: < 20 units (smooth, k=6)
- Large: 20-80 units (accelerated, k=15)
- Snap: > 80 units (instant)

---

## Performance Characteristics

### Bandwidth Comparison

| Architecture | Upload (per client) | Download (per client) |
|--------------|---------------------|------------------------|
| **FPS-Style** | ~5-10 KB/s (raw inputs) | ~10-30 KB/s (full world state) |
| **LoL-Style** | ~1.5 KB/s (intents @ 120Hz) | ~2.4 KB/s (snapshots @ 60Hz, AOI-culled) |

**LoL-Style advantage:** Lower bandwidth due to intent-based input and AOI culling.

### CPU Comparison

| Architecture | Client CPU | Server CPU |
|--------------|------------|------------|
| **FPS-Style** | High (prediction + reconciliation) | Medium (validation) |
| **LoL-Style** | Low (interpolation only) | High (full simulation) |

**LoL-Style trade-off:** Client is lighter, server does all work.

---

## Conclusion

### Why LoL-Style for Ryvax?

1. ✅ **Game genre:** MOBA/action-RPG, not FPS
2. ✅ **Anti-cheat:** Competitive integrity priority
3. ✅ **Complexity:** Simpler to implement and debug
4. ✅ **Determinism:** Replay system for eSports
5. ✅ **Server features:** Pathfinding, abilities, RNG

### When Would We Use FPS-Style?

If Ryvax were:
- First-person shooter with hitscan weapons
- Twitch-based gameplay (sub-100ms reactions)
- No competitive anti-cheat requirements
- No server-side pathfinding/RNG

**But it's not.** So LoL-style is the right choice.

---

## Further Reading

- **League of Legends Netcode Analysis:** [LOL.md (original French doc)](/Docs/Archive/LOL.md)
- **Implementation Details:** [02_LoL_Style_Netcode.md](02_LoL_Style_Netcode.md)
- **Performance Metrics:** [Network/04_Performance_Metrics.md](../Network/04_Performance_Metrics.md)

**External Resources:**
- [Riot Games: League of Legends Netcode](https://technology.riotgames.com/news/determinism-league-legends-unified-clock)
- [Gabriel Gambetta: Fast-Paced Multiplayer](https://www.gabrielgambetta.com/client-server-game-architecture.html)
- [Valve: Source Multiplayer Networking](https://developer.valvesoftware.com/wiki/Source_Multiplayer_Networking)

---

**Last Updated:** 2026-02-03

#rules-verified
