# Ryvax — MOBA Project Context

## Stack
- Unity 6 (C#), FishNet networking, LoL-style authoritative server
- Architecture: GameSim (simulation, Unity-dependent) / UnityView (rendering) / NetAdapter (network)
- Namespaces: MOBANet.GameSim, MOBANet.UnityView, MOBANet.NetAdapter, MOBANet.Client, MOBANet.Server

## Documentation

All `.md` files live in `/Docs`. Read the relevant docs **before** working on a feature.

| Feature area | Read these |
|-------------|-----------|
| **Gameplay** (abilities, CC, items, mobs, map, combat, classes) | [GDD_Prototype.md](Docs/GDD_Prototype.md) |
| **Architecture** (adding a system, tracing data flow, namespaces) | [Architecture/01_System_Overview.md](Docs/Architecture/01_System_Overview.md) |
| **Client rendering** (entity visuals, smoothing, dead-reckoning, animations) | [Client/01_Client_Architecture.md](Docs/Client/01_Client_Architecture.md) |
| **Input** (keybinds, cast modes, targeting, input packet format) | [Client/02_Input_System.md](Docs/Client/02_Input_System.md) |
| **Simulation** (SimWorld, tick loop, entities, collisions, projectiles) | [GameSim/01_GameSim_Overview.md](Docs/GameSim/01_GameSim_Overview.md) |
| **Commands** (adding command types, command dispatch, handlers) | [GameSim/02_Command_System.md](Docs/GameSim/02_Command_System.md) |
| **Entity state** (components, health, abilities, combat, physics) | [GameSim/03_Entity_Model.md](Docs/GameSim/03_Entity_Model.md) |
| **Networking** (FishNet adapter, transport, messages, polling) | [Network/01_Network_Architecture.md](Docs/Network/01_Network_Architecture.md) |
| **Packet format** (serialization, quantization, adding message fields) | [Network/02_Message_Specifications.md](Docs/Network/02_Message_Specifications.md) |
| **End-to-end flow** (input-to-visual pipeline, interpolation, latency) | [Network/03_Data_Flow.md](Docs/Network/03_Data_Flow.md) |
| **Performance** (latency optimization, bandwidth, tick rate, scaling) | [Network/04_Performance_Metrics.md](Docs/Network/04_Performance_Metrics.md) |
| **Server loop** (input buffering, tick execution, snapshot broadcast) | [Server/01_Server_Loop.md](Docs/Server/01_Server_Loop.md) |
| **AOI / visibility** (spatial hash, vision radius, enter/leave events) | [Server/02_AOI_System.md](Docs/Server/02_AOI_System.md) |
| **Debugging netcode** (latency, rubber-banding, desync, packet loss) | [Development/01_Debugging_Guide.md](Docs/Development/01_Debugging_Guide.md) |
| **Log tracing** (instrumentation, sequence correlation, packet tracing) | [Development/02_Instrumentation.md](Docs/Development/02_Instrumentation.md) |
| **Testing** (manual test checklist, regression, acceptance criteria) | [Development/03_Testing_Checklist.md](Docs/Development/03_Testing_Checklist.md) |
| **Doc index** (finding a doc, adding a new doc file) | [INDEX.md](Docs/INDEX.md) |
| **Project overview** (config constants, version history, reading order) | [README.md](Docs/README.md) |

---

## Development Rules

### 1. Read Before Modifying
- Always read related files (`.cs`, `.prefab`, `.unity`, `.asset`) before touching them. Never assume file contents.
- Search the codebase (Grep/Glob) before asking. If the info exists in the project, read it.
- **Never assume code behavior.** Search and Read before explaining or fixing.

### 2. Read GDD Before Implementing Gameplay
- GDD_Prototype.md contains gameplay rules (abilities, CC, map, mobs).
- If a feature is not documented → ask for clarification.
- Never invent gameplay rules.

### 3. Questions Are Questions
- When the user asks a question, **answer it first** — don't start coding without an explicit request.
- Never make architecture decisions alone. When in doubt → ask.

### 4. Challenge and Flag
- **Always question the user's assumptions** if something seems inconsistent.
- Flag ambiguity **before** implementing, not after.
- Propose alternatives if a request would create technical debt.

### 5. Show Your Work
- Reference `file:line`. Quote code. Trace execution through actual code.
- If unsure, say so. **Never fill gaps with speculation.**

### 6. Root Cause, Not Band-Aids
- **Fix the cause, not the symptom.** No workarounds, no fallbacks to mask errors.
- **Fail fast**: if a value should never be null, don't add a fallback — let the error surface.
- No unnecessary defensive code: `settings?.Value ?? 1.2f` hides a bug if `settings` should never be null.

**What's a band-aid?** A fix that hides the symptom without addressing the cause:
- Namespace error → prepending `UnityEngine.` everywhere (band-aid) vs renaming the bad namespace (fix)
- NullReferenceException → adding `?.` everywhere (band-aid) vs ensuring proper initialization (fix)
- Merge conflict → deleting conflicting code (band-aid) vs understanding why the conflict exists (fix)

**Litmus test**: if the "fix" must be repeated at every similar location, it's a band-aid.

### 7. No Magic Values, ScriptableObjects Are Read-Only
- **Never hardcode** physics values (gravity, speed, etc.) in code. Use `[SerializeField]` with a descriptive tooltip.
- **ScriptableObjects must be read-only at runtime:**
  - Never store mutable state on a ScriptableObject. Pass values as method parameters.
  - Reason: ScriptableObjects are **shared assets** across all players. Mutating them causes **race conditions** with 100 players.

### 8. Clean Code
- English naming, English comments.
- Document public methods with `<summary>`.
- Remove unused code and orphan variables.
- **Clean up Debug.Log after validation** — remove all temporary debug logs once the code is confirmed working.

### 9. Unity Configuration
- Unity config (prefabs, values) belongs in the Inspector.
- Maximize use of Editor scripts and explain what they do.
- **Always list required Unity actions** after a modification (components to add, values to configure, prefabs to assign, etc.).

### 10. Keep Documentation Updated
- Update `.md` files when a change impacts architecture.
- Don't wait for the user to ask.

### 11. Scalability (100 Players)
- Separate server / client / shared logic.
- Avoid singletons except for high-level managers.
- Prefer ScriptableObjects for configuration.

---

## Naming Conventions

| Type | Convention | Example |
|------|-----------|---------|
| Classes | PascalCase | `LocalPlayerController` |
| Methods | PascalCase | `SpawnLocalPlayer()` |
| Private variables | camelCase | `moveSpeed` |
| SerializeField | camelCase | `[SerializeField] private float moveSpeed` |
| Interfaces | IPascalCase | `IDamageable` |
| Spell settings | SpellNameSettings | `FireballSettings` |
| Projectiles | SpellNameProjectile | `FireballProjectile` |

---

## Debugging Protocol

### 1. No Fix Without Diagnosis
Before any fix: propose at least **3 competing hypotheses**. For each: explained symptom, falsifying sign, minimal test.

### 2. Locate Precisely
Every hypothesis must point to a **specific location** (module, function, line, config, data). If it can't, propose tests instead of a fix.

### 3. Evidence Before Patch
No code change without an observation or test that **exactly** motivates it. Add logs first, get evidence from the user, then fix.

### 4. Minimal Experiments
Reduce the problem to an **MRE** (Minimal Reproducible Example). Describe what should be observed if the hypothesis is true vs false.

### 5. Verifiable Predictions
Each hypothesis must produce a **clear prediction**: what disappears or appears if X is applied.

### 6. Reset on Facts
If the analysis drifts: start over. List **only observed facts** (logs, errors, behavior), then derive hypotheses.

### 7. Consider Non-Code Causes
Always examine: config, environment, data, dependencies, recent changes, **execution order**.

### 8. Short Scientific Cycle
Hypothesis → minimal test → result → update hypotheses. No long narratives.

### 9. Self-Critique
Before concluding: attack your own solution and propose the most plausible alternative.

---

## Critical Thinking & Intellectual Honesty

- Never validate assumptions by default. If a hypothesis is weak, say so explicitly and explain why.
- Base every claim on mechanisms, known empirical data, or explicit logical reasoning. No normative language.
- Cite specific authors, papers, datasets, or events. If unknown, say so.
- Distinguish what is well-established, debated, or speculative.
- If information is not known with certainty, do not fill the gap with plausibility.
