# Debugging Guide

**Troubleshooting Netcode Issues in Ryvax**

This guide covers common netcode problems, debugging techniques, and tools for diagnosing network-related issues.

---

## Common Problems

### High Latency

**Symptoms:**
- Input feels sluggish or delayed
- Visual position updates lag behind expected timing
- Large visual offset corrections

**Possible Causes:**
1. **Network latency:** High ping to server (>100ms)
2. **Server tick overrun:** Server processing taking longer than 16.67ms
3. **Client frame drops:** Client FPS below 60
4. **Input send rate too low:** Check if 120Hz send rate is maintained

**Debugging Steps:**

1. **Measure end-to-end latency:**
   ```csharp
   // In NetworkClient.cs, check:
   float inputLatency = _lastAcknowledgedSeq - _currentSeq;
   Debug.Log($"Input lag: {inputLatency} inputs behind");
   ```

2. **Check network RTT:**
   ```csharp
   // In FishNetAdapter.cs:
   float rtt = NetworkManager.TimeManager.RoundTripTime;
   Debug.Log($"RTT: {rtt}ms");
   ```

3. **Verify server tick time:**
   ```csharp
   // In ServerGameLoop.cs:
   float tickDuration = Time.realtimeSinceStartup - _lastTickTime;
   if (tickDuration > 0.020f) // >20ms (should be 16.67ms)
       Debug.LogWarning($"Tick overrun: {tickDuration * 1000}ms");
   ```

4. **Check client FPS:**
   ```csharp
   float fps = 1.0f / Time.deltaTime;
   if (fps < 50)
       Debug.LogWarning($"Low FPS: {fps}");
   ```

**Solutions:**
- **High ping:** Use region-based matchmaking, deploy servers closer to players
- **Server overrun:** Profile and optimize simulation code, reduce entity count
- **Client drops:** Optimize rendering, reduce visual effects
- **Low send rate:** Verify `INPUT_SEND_RATE` constant is 120Hz

---

### Rubber-Banding

**Symptoms:**
- Character snaps/teleports backward
- Visual position oscillates rapidly
- Large offset corrections visible

**Possible Causes:**
1. **Large visualOffset accumulation:** basePos jumping too much
2. **Packet loss:** Missing snapshots causing extrapolation
3. **Server corrections:** Client intent rejected by server (collision, invalid state)
4. **Buffer underrun:** Interpolation buffer too small for current jitter

**Debugging Steps:**

1. **Check visual offset magnitude:**
   ```csharp
   // In VisualPositionManager:
   Vector3 offset = _corrector.GetVisualOffset();
   float gap = offset.magnitude;

   if (gap > LOL_BASE_LARGE_GAP) // >80 units
       Debug.LogWarning($"Large offset: {gap} units");
   ```

2. **Monitor snap events:**
   ```csharp
   // In VisualOffsetCorrector.cs, add logging in Snap():
   Debug.LogWarning($"[SNAP] gap={gap} threshold={largeGap}");
   ```

3. **Check packet loss:**
   ```csharp
   // In NetworkClient:
   int expectedSeq = _lastServerTick + 1;
   int actualSeq = snapshot.ServerTick;
   if (actualSeq != expectedSeq)
       Debug.LogWarning($"Packet loss detected: expected={expectedSeq}, got={actualSeq}");
   ```

4. **Verify interpolation quality:**
   ```csharp
   // In BaseInterpolator:
   BasePosQuality quality = _interpolator.Quality;
   if (quality == BasePosQuality.Extrapolated)
       Debug.LogWarning("Extrapolating - may cause rubber-banding");
   ```

**Solutions:**
- **Large offsets:** Check if server is rejecting intents, verify collision handling
- **Packet loss:** Use reliable transport, check network stability
- **Server corrections:** Validate client intents match server expectations
- **Buffer underrun:** Increase `ADAPTIVE_BUFFER_TARGET` (currently 100ms)

---

### Desync

**Symptoms:**
- Client position differs significantly from server
- Other players appear in wrong positions
- Character moves through walls client-side but snaps back

**Possible Causes:**
1. **Server authority violation:** Client predicting instead of following server
2. **Snapshot processing error:** Incorrect interpolation or state application
3. **Intent mismatch:** Client sending different intents than displayed
4. **Sequence number desync:** Acknowledgments not matching

**Debugging Steps:**

1. **Compare client vs server positions:**
   ```csharp
   // Add to PlayerView.cs Update():
   Vector3 clientPos = transform.position;
   Vector3 serverPos = _networkClient.GetBasePosition();
   float diff = Vector3.Distance(clientPos, serverPos);

   if (diff > 5.0f) // >5 units difference
       Debug.LogError($"Position desync: {diff} units apart");
   ```

2. **Verify intent acknowledgment:**
   ```csharp
   // In NetworkClient.cs, when snapshot arrives:
   uint lastProcessedSeq = snapshot.AckMovementSeq;
   uint currentSeq = _movementSeqId;

   Debug.Log($"Server processed seq={lastProcessedSeq}, client at seq={currentSeq}");
   ```

3. **Check snapshot application:**
   ```csharp
   // In BaseInterpolator.cs:
   Debug.Log($"Snapshot: tick={snapshot.ServerTick} pos={snapshot.Position} vel={snapshot.Velocity}");
   ```

4. **Monitor discontinuity flags:**
   ```csharp
   // In VisualPositionManager:
   if (_interpolator.HadDiscontinuity)
       Debug.LogWarning($"Discontinuity at time={_interpolator.DiscontinuityTime}");
   ```

**Solutions:**
- **Authority violation:** Ensure client never directly sets position from input
- **Snapshot errors:** Verify serialization/deserialization matches server
- **Intent mismatch:** Log intents sent vs displayed feedback
- **Sequence desync:** Reset sequence numbers on reconnect

---

### Packet Loss

**Symptoms:**
- Choppy movement
- Frequent extrapolation fallback
- Missing snapshot warnings in logs

**Possible Causes:**
1. **UDP packet loss:** Unreliable transport dropping packets
2. **Network congestion:** Bandwidth saturation
3. **Server overload:** Snapshots not sent on time
4. **Client receive buffer overflow:** Too many packets queued

**Debugging Steps:**

1. **Track snapshot gaps:**
   ```csharp
   // In NetworkClient.cs:
   int tickGap = snapshot.ServerTick - _lastServerTick;
   if (tickGap > 1)
       Debug.LogWarning($"Missing {tickGap - 1} snapshots");
   ```

2. **Monitor extrapolation frequency:**
   ```csharp
   // In BaseInterpolator.cs:
   if (Quality == BasePosQuality.Extrapolated)
       _extrapolationCounter++;

   // Log every second:
   float extrapolationPercent = _extrapolationCounter / (float)_totalFrames * 100f;
   Debug.Log($"Extrapolation: {extrapolationPercent}%");
   ```

3. **Check FishNet transport stats:**
   ```csharp
   // Via FishNet's built-in stats:
   NetworkTraffic traffic = NetworkManager.TransportManager.GetTraffic();
   Debug.Log($"Packets dropped: {traffic.DroppedPackets}");
   ```

**Solutions:**
- **UDP loss:** Use FishNet's reliable channel for critical data
- **Congestion:** Reduce snapshot rate or entity count (AOI culling)
- **Server overload:** Optimize tick processing, reduce tick rate
- **Buffer overflow:** Increase client receive buffer size

---

## Debugging Tools

### Console Commands

Add these to a debug console (e.g., via Unity's Debug class or in-game console):

**Network Stats:**
```csharp
/netstats           // Show RTT, packet loss, bandwidth
/latency            // Display input-to-visual latency
/snapstats          // Snapshot receive rate, gaps
```

**Visual Debugging:**
```csharp
/showoffset         // Draw visual offset as debug line
/showinterp         // Display interpolation buffer state
/showquality        // Color-code basePos quality (green=interp, yellow=extrap, red=frozen)
```

**Position Tracking:**
```csharp
/comparepos         // Log client vs server position each frame
/trackintent        // Log all sent intents with timestamps
```

**Implementation Example:**
```csharp
// Add to NetworkClient.cs:
public void ExecuteDebugCommand(string command)
{
    switch (command)
    {
        case "/netstats":
            Debug.Log($"RTT: {GetRTT()}ms | Loss: {GetPacketLoss()}% | BW: {GetBandwidth()}KB/s");
            break;

        case "/latency":
            float latency = (_movementSeqId - _lastAckSeq) * (1000f / INPUT_SEND_RATE);
            Debug.Log($"Input latency: {latency}ms");
            break;

        case "/showoffset":
            _visualDebug.ShowOffsetLine = !_visualDebug.ShowOffsetLine;
            break;
    }
}
```

### Log Analysis

See [Instrumentation.md](Instrumentation.md) for detailed log format documentation.

**Key log patterns to look for:**

**1. Flush delay (client outgoing):**
```
[SEND ENQUEUE] C→S seq=1 ticks=T1
[SOCKET SEND] C→S seq=1 ticks=T2
Delta = T2-T1 should be <2ms (same frame)
```

**2. Polling delay (server incoming):**
```
[SOCKET SEND] C→S seq=1 ticks=T2
[TRANSPORT RECV] C→S seq=1 ticks=T3
Delta = T3-T2 should be <20ms (network + 1 tick)
```

**3. Snapshot broadcast timing:**
```
[SERVER BROADCAST] tick=26 vel=(8.0,0.0) ticks=T4
[SERVER SOCKET SEND] S→C0 tick=26 ticks=T5
Delta = T5-T4 should be <2ms (same tick)
```

### Profiling

**Unity Profiler Markers:**

Add these to critical paths for performance analysis:

```csharp
// In ServerGameLoop.cs:
using (new ProfilerMarker("ServerGameLoop.Tick").Auto())
{
    _simWorld.Tick(Time.fixedDeltaTime);
}

// In BaseInterpolator.cs:
using (new ProfilerMarker("BaseInterpolator.GetBasePos").Auto())
{
    return Lerp(snapshotA, snapshotB, alpha);
}

// In VisualPositionManager.cs:
using (new ProfilerMarker("VisualPositionManager.Update").Auto())
{
    _corrector.Update(dt, _currentSpeed);
}
```

**What to look for:**
- **ServerGameLoop.Tick:** Should be <16ms (60Hz target)
- **BaseInterpolator.GetBasePos:** Should be <0.1ms
- **VisualPositionManager.Update:** Should be <0.2ms

### Visual Debugging Gizmos

**Draw visual offset in Scene view:**

```csharp
// Add to PlayerView.cs:
void OnDrawGizmos()
{
    if (!Application.isPlaying) return;

    Vector3 basePos = _networkClient.GetBasePosition();
    Vector3 visualPos = _networkClient.GetVisualPosition();
    Vector3 offset = visualPos - basePos;

    // Base position (green sphere)
    Gizmos.color = Color.green;
    Gizmos.DrawWireSphere(basePos, 0.5f);

    // Visual position (blue sphere)
    Gizmos.color = Color.blue;
    Gizmos.DrawWireSphere(visualPos, 0.3f);

    // Offset line (red if large)
    float gap = offset.magnitude;
    Gizmos.color = gap > 80 ? Color.red : Color.yellow;
    Gizmos.DrawLine(basePos, visualPos);

    // Label
    #if UNITY_EDITOR
    UnityEditor.Handles.Label(visualPos, $"gap={gap:F1}");
    #endif
}
```

**Show interpolation buffer state:**

```csharp
// Add to BaseInterpolator.cs:
void OnDrawGizmos()
{
    if (_snapshotBuffer.Count < 2) return;

    // Draw each snapshot in buffer
    for (int i = 0; i < _snapshotBuffer.Count - 1; i++)
    {
        Vector3 posA = _snapshotBuffer[i].Position;
        Vector3 posB = _snapshotBuffer[i + 1].Position;

        // Interpolated section (green)
        Gizmos.color = Color.green;
        Gizmos.DrawLine(posA, posB);
    }

    // Current renderTime (cyan sphere)
    Gizmos.color = Color.cyan;
    Gizmos.DrawSphere(_currentBasePos, 0.2f);
}
```

---

## Metrics & Monitoring

### Key Metrics to Track

**1. Input-to-Visual Latency**
```csharp
float latency = (seqSent - seqAck) * (1000f / INPUT_SEND_RATE) + bufferTime;
```
**Target:** <100ms (localhost: ~50ms)

**2. Visual Offset Gap**
```csharp
float gap = Vector3.Distance(visualPos, basePos);
```
**Target:** <20 units (smooth correction)

**3. Extrapolation Ratio**
```csharp
float extrapolationPercent = (framesExtrapolated / totalFrames) * 100f;
```
**Target:** <5% (should mostly interpolate)

**4. Snap Frequency**
```csharp
float snapRate = snapsPerSecond;
```
**Target:** <1/sec (rare large corrections)

**5. Buffer Utilization**
```csharp
int bufferedSnapshots = _snapshotBuffer.Count;
```
**Target:** 6-10 snapshots (100-160ms @ 60Hz)

### Logging Best Practices

**1. Use structured logs:**
```csharp
Debug.Log($"[NetworkClient] seq={seq} gap={gap:F2} quality={quality}");
```

**2. Add timestamps:**
```csharp
long ticks = Stopwatch.GetTimestamp();
Debug.Log($"[{ticks}] Event occurred");
```

**3. Correlate sequences:**
```csharp
Debug.Log($"[SEND] seq={seq} intent={type}");
// Later...
Debug.Log($"[ACK] seq={seq} processed");
```

**4. Rate-limit verbose logs:**
```csharp
if (Time.frameCount % 60 == 0) // Log once per second @ 60fps
    Debug.Log($"Stats: {stats}");
```

---

## Troubleshooting Workflow

When encountering a netcode issue:

1. **Identify the symptom** - Latency? Rubber-banding? Desync? Packet loss?
2. **Enable relevant logs** - See [Instrumentation.md](Instrumentation.md)
3. **Collect metrics** - Input latency, visual offset, extrapolation ratio
4. **Reproduce consistently** - Find minimal steps to trigger issue
5. **Isolate the layer** - Input? Network? Simulation? Rendering?
6. **Profile the hotspot** - Use Unity Profiler to find bottleneck
7. **Verify the fix** - Re-test with metrics, ensure no regression

---

## Common Fixes Reference

| Problem | Quick Fix | File to Edit |
|---------|-----------|--------------|
| High input latency | Increase send rate to 120Hz | `NetworkClient.cs:54` |
| Rubber-banding | Increase adaptive buffer | `NetcodeConstants.cs:381` |
| Choppy interpolation | Reduce horizon thresholds | `NetcodeConstants.cs:370-374` |
| Visual stuttering | Enable double-flush | `FishNetAdapter.cs:LateUpdate()` |
| Large offsets | Check server collision handling | `MovementHandler.cs` |
| Frequent snaps | Increase `MAX_LARGE_GAP` | `NetcodeConstants.cs:362` |

---

## Advanced Debugging

### Time Travel Debugging

Record all snapshots and intents, then replay with variable speed:

```csharp
// SnapshotRecorder.cs (create new file)
public class SnapshotRecorder
{
    private List<(float time, SnapshotDelta snap)> _recording;

    public void StartRecording() { _recording = new List<...>(); }

    public void RecordSnapshot(float time, SnapshotDelta snap)
    {
        _recording.Add((time, snap));
    }

    public void Replay(float speed = 1.0f)
    {
        // Step through recording at custom speed
    }
}
```

### Determinism Verification

Compare client-side "shadow simulation" with server:

```csharp
// Run same simulation on client (no authority)
SimWorld _shadowSim = new SimWorld();
_shadowSim.Tick(dt);

Vector3 shadowPos = _shadowSim.GetPlayer().Position;
Vector3 serverPos = _networkClient.GetBasePosition();

if (Vector3.Distance(shadowPos, serverPos) > 0.1f)
    Debug.LogError("Simulation desync - non-deterministic!");
```

---

**Last Updated:** 2026-02-03
