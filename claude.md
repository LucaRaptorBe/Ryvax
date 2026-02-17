# Ryvax - Project Context

## Stack
- Unity 6 (C#), FishNet networking, LoL-style authoritative server
- Architecture: GameSim (simulation, Unity-dependent) / UnityView (rendering) / NetAdapter (network)
- Namespaces: MOBANet.GameSim, MOBANet.UnityView, MOBANet.NetAdapter, MOBANet.Client, MOBANet.Server

## Code Rules

### Verify Before Asserting
Never assume code behavior. Search (Grep/Glob) and Read before explaining or fixing.
When debugging: add logs first, get evidence, THEN fix. Never skip to a fix.

### Show Your Work
Reference file:line. Quote code. Trace execution through actual code.

### Admit Uncertainty
If unsure, say so. Never fill gaps with speculation.
