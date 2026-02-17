# Archive

**Historical Debugging Documents**

This directory contains investigative documentation from the netcode latency optimization work. These documents represent the debugging process and discoveries made while reducing input-to-visual latency from ~130ms to ~50ms.

---

## Purpose

These files are **archived for historical reference only**. They document the step-by-step investigation into network timing issues and are preserved to:

1. **Show the debugging methodology** used to diagnose complex netcode problems
2. **Provide context** for architectural decisions made during optimization
3. **Preserve knowledge** about FishNet/Tugboat transport internals
4. **Document solutions** to specific timing issues that may recur

**For current debugging:** See [../Development/Debugging_Guide.md](../Development/Debugging_Guide.md) instead.

---

## Contents

### Latency Investigation Documents

**NETCODE_INPUT_LATENCY.md**
- Comprehensive analysis of input-to-visual latency
- Timeline breakdowns showing 130ms → 50ms reduction
- Final latency budget and measurement methodology

**NETCODE_FLOW_AS_IS.md**
- Network flow analysis as of early implementation
- Timing diagrams for client→server→client flow
- Identified bottlenecks and delays

**NETCODE_DIAGRAM.md**
- Visual diagrams of packet flow timing
- Outdated timings (pre-optimization)
- Conceptual understanding of the architecture

---

### Transport-Layer Debugging

**TUGBOAT_POLL_CHAIN.md**
- Investigation into FishNet's Tugboat transport polling behavior
- IterateIncoming/IterateOutgoing call chain analysis
- Discovery of 33ms server-side polling delay

**FLUSH_DELAY_INSTRUMENTATION.md**
- Analysis of client-side packet flushing delays
- Instrumentation showing 32ms flush delays in FixedUpdate
- Led to LateUpdate double-flush solution

**CLIENT_OUTGOING_CHAIN.md**
- Client-side packet send path analysis
- Queue → Flush → Socket send timing
- Identified IterateOutgoing bottleneck

---

### Specific Fixes

**FIX_INCOMING_PROCESSING.md**
- Server-side IterateIncoming optimization
- Moved packet processing to earlier in frame
- Reduced server-side latency by ~16ms

**FIX_CLIENT_OUTGOING.md**
- Client-side send path optimization
- Ensured packets flush in same frame as creation
- Eliminated 32ms client flush delay

**FIX_LATEUPDATE_FLUSH.md**
- Double-flush implementation in LateUpdate
- Ensures packets send immediately, not queued for next FixedUpdate
- Critical fix for achieving <100ms latency

---

### Enhanced Logging

**ENHANCED_SOCKET_LOGS.md**
- Instrumented logging system design (French)
- Sequence number correlation methodology
- Timestamp precision tracking (Stopwatch ticks)
- **Note:** Concepts from this doc are now in [../Development/Instrumentation.md](../Development/Instrumentation.md)

---

### Architecture Evolution

**LOL_STYLE_NETCODE.md** (V2.3)
- Earlier version of LoL-style netcode spec
- Intent-based input, visual offset correction
- BasePosQuality tracking, CC handling
- **Note:** Updated version is in [../Architecture/02_LoL_Style_Netcode.md](../Architecture/02_LoL_Style_Netcode.md)

**LOL.md** (V1.0)
- Original LoL-style netcode design document
- Conceptual foundation for current architecture
- Trade-offs vs FPS-style prediction

**InputCommandSpec.md**
- Input packet format specification (early version)
- Intent types and serialization
- **Note:** Current spec is in [../Network/02_Message_Specifications.md](../Network/02_Message_Specifications.md)

**FLUX_DIAGRAM_VISUEL.md**
- Visual flow diagrams (French)
- Early conceptual flow charts
- Useful for understanding initial architecture

---

## Key Discoveries

From these investigations, we learned:

1. **FishNet's IterateIncoming/Outgoing run in FixedUpdate**, causing 16-33ms delays
   - **Solution:** Manual double-flush in LateUpdate

2. **Packets queued client-side weren't flushing immediately**
   - **Solution:** Explicit flush after every send

3. **Server-side polling happened too late in FixedUpdate**
   - **Solution:** Flush pending inputs at start of FixedUpdate, before sim

4. **Sequence number correlation is essential for debugging**
   - **Solution:** Enhanced logging with Stopwatch timestamps

5. **Adaptive buffering prevents jitter-induced stuttering**
   - **Solution:** 80-160ms adaptive buffer based on jitter estimation

---

## Timeline of Optimizations

| Date | Document | Latency Before | Latency After | Fix |
|------|----------|----------------|---------------|-----|
| Jan 31 | NETCODE_FLOW_AS_IS | ~130ms | ~130ms | Initial analysis |
| Feb 1 | FLUSH_DELAY_INSTRUMENTATION | ~130ms | ~98ms | Identified client flush delay |
| Feb 1 | FIX_CLIENT_OUTGOING | ~98ms | ~82ms | Client double-flush |
| Feb 1 | TUGBOAT_POLL_CHAIN | ~82ms | ~66ms | Server IterateIncoming fix |
| Feb 2 | FIX_LATEUPDATE_FLUSH | ~66ms | ~50ms | Final double-flush optimization |
| Feb 2 | NETCODE_INPUT_LATENCY | ~50ms | ~50ms | Final measurement & documentation |

**Final Result:** **~50ms input-to-visual latency** (localhost, 120Hz input rate)

---

## Why These Are Archived

The current documentation structure (as of 2026-02-03) provides:

- **Architecture/** - High-level design and rationale
- **Network/** - Message formats and data flow
- **GameSim/** - Simulation layer documentation
- **Server/** - Server-side systems
- **Client/** - Client-side systems
- **Development/** - **Debugging, testing, and instrumentation** (replaces these archived docs)

The archive preserves the **investigative process**, while the main docs provide the **current best practices**.

---

## Using These Documents

**If you're debugging a new latency issue:**
1. Start with [../Development/Debugging_Guide.md](../Development/Debugging_Guide.md)
2. Use [../Development/Instrumentation.md](../Development/Instrumentation.md) for logging
3. Only consult these archives if you need:
   - Historical context on why a decision was made
   - Transport-layer internals (FishNet/Tugboat)
   - Step-by-step debugging methodology examples

**If you're learning the architecture:**
- Skip these archives
- Start with [../README.md](../README.md) → [../Architecture/01_System_Overview.md](../Architecture/01_System_Overview.md)

---

## Backups

The `Backups/` subdirectory contains earlier snapshots of critical files during the optimization work. These are preserved for disaster recovery but should not be referenced for current development.

---

**Last Updated:** 2026-02-03
**Status:** Archived - Read-only reference
