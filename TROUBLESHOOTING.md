# Troubleshooting - Erreurs Courantes Quantum

Ce document liste les erreurs courantes lors de l'intégration de Photon Quantum et leurs solutions.

---

## Erreurs de Compilation C#

### ❌ `The name 'FPVector3' does not exist in the current context`

**Cause** : Imports manquants

**Solution** :
```csharp
using Photon.Deterministic; // Ajouter cet import
```

Tous les types déterministes (`FP`, `FPVector3`, `FPQuaternion`, `FPMath`) viennent de `Photon.Deterministic`.

---

### ❌ `'Input' is an ambiguous reference between 'Quantum.Input' and 'UnityEngine.Input'`

**Cause** : Conflit de noms entre le Input struct de Quantum et la classe Input de Unity

**Solution 1** : Utiliser le nom complet
```csharp
// Pour Unity Input
if (UnityEngine.Input.GetKey(KeyCode.W)) { ... }

// Pour Quantum Input
Quantum.Input input = new Quantum.Input();
```

**Solution 2** : Mettre le script dans le namespace `Quantum`
```csharp
namespace Quantum
{
    using UnityEngine;

    public class LocalInput : MonoBehaviour
    {
        public void PollInput(CallbackPollInput callback)
        {
            Quantum.Input input = new Quantum.Input();
            // ...
            if (UnityEngine.Input.GetKey(KeyCode.W)) { ... }
        }
    }
}
```

---

### ❌ `The name 'DeterministicInputFlags' does not exist in the current context`

**Cause** : Import manquant

**Solution** :
```csharp
using Photon.Deterministic;
```

`DeterministicInputFlags` vient de `Photon.Deterministic`.

---

### ❌ `The name 'PlayerStats' does not exist in the current context`

**Cause** : Le code Quantum n'a pas été généré, ou Unity n'a pas encore compilé

**Solution** :
1. **Dans Unity** : Menu **Quantum** → **Code Generation** → **Run Codegen**
2. **Attendre** que Unity recompile (10-30 secondes)
3. **Vérifier** que les fichiers sont générés dans `Assets/QuantumUser/Simulation/Generated/`

Si le problème persiste :
- Vérifier que `Components.qtn` est bien formaté (pas d'erreurs de syntaxe)
- Vérifier la console Unity pour des erreurs de codegen

---

### ❌ `Cannot assign to 'StopMoving' because it is a 'Quantum.Button'`

**Cause** : `Button` est un struct, pas un bool. Il faut utiliser `.Set = true` au lieu de `= true`

**Mauvais** :
```csharp
input.StopMoving = true; // ❌ Erreur
```

**Bon** :
```csharp
input.StopMoving.Set = true; // ✅ Correct
```

Ou utiliser les méthodes de Button :
```csharp
input.StopMoving.SetPressed(true);
```

---

## Erreurs de Runtime

### ❌ `NullReferenceException: QuantumRunner.Default.Game is null`

**Cause** : Quantum n'a pas démarré, ou le script s'exécute avant que Quantum soit initialisé

**Solution** :
1. Vérifier que `QuantumRunner` existe dans la scène
2. Vérifier que `QuantumRunner` a un `SimulationConfig` et `SessionConfig` assignés
3. Dans vos scripts, toujours vérifier si `QuantumRunner.Default` est null avant de l'utiliser :

```csharp
public override void OnActivate(Frame frame)
{
    _game = QuantumRunner.Default?.Game;

    if (_game == null)
    {
        Debug.LogError("QuantumRunner.Default.Game is null!");
        return;
    }
}
```

---

### ❌ `Failed to spawn player! Check playerPrototype assignment.`

**Cause** : Le `AssetRef<EntityPrototype>` n'est pas assigné dans l'Inspector

**Solution** :
1. Créer un Entity Prototype dans Unity (Create → Quantum → Entity Prototype)
2. Assigner le prototype dans l'Inspector du script `GameBootstrap`
3. Vérifier que le prototype a bien les components requis (Transform3D, PlayerStats, MovementState)

---

### ❌ `Player entity missing Transform3D component!`

**Cause** : L'Entity Prototype ne contient pas le component Transform3D

**Solution** :
1. Sélectionner le `PlayerPrototype.asset`
2. Dans l'Inspector, cliquer **Add Component** → **Transform3D**
3. Sauvegarder (Ctrl+S)

---

### ❌ Movement ne fonctionne pas (player ne bouge pas)

**Causes possibles** :

**1. LocalInput pas attaché au QuantumRunner**
- Solution : Ajouter le script `LocalInput` sur le GameObject `QuantumRunner`

**2. MovementSystem pas enregistré**
- Solution : Vérifier `SystemSetup.User.cs` :
```csharp
systems.Add(new MovementSystem());
```

**3. Input pas assigné au bon player**
- Solution : Vérifier dans `MovementSystem.cs` :
```csharp
var input = f.GetPlayerInput(0); // Pour prototype solo
```

**4. MovementSpeed = 0**
- Solution : Vérifier `PlayerStats.MovementSpeed` dans le prototype ou dans `GameBootstrap` :
```csharp
stats.MovementSpeed = 5; // 5 units/second
```

**5. CanMove = false**
- Solution : Vérifier `MovementState.CanMove` dans le prototype :
```csharp
movement.CanMove = true;
```

---

### ❌ Player ne s'affiche pas (pas de visuel)

**Causes possibles** :

**1. Entity View Asset pas créé**
- Solution : Créer un Entity View Asset (Create → Quantum → Entity View Asset)
- Assigner le prototype ET le prefab Unity

**2. Prefab pas assigné**
- Solution : Assigner le prefab PlayerView dans l'Entity View Asset

**3. EntityView pas sur le prefab**
- Solution : Ajouter le component `Quantum Entity View` sur le prefab PlayerView

**4. PlayerEntityView pas sur le prefab**
- Solution : Ajouter le component `Player Entity View` (votre script) sur le prefab

---

### ❌ `Quantum codegen failed`

**Cause** : Erreur de syntaxe dans les fichiers `.qtn`

**Solution** :
1. Menu **Quantum** → **Code Generation** → **Show Codegen Errors**
2. Lire l'erreur dans la console Unity
3. Corriger le fichier `.qtn` incriminé

**Erreurs courantes** :
- Oublier le point-virgule `;` à la fin des lignes
- Utiliser des types invalides (utiliser `FP` au lieu de `float`, `Int32` au lieu de `int`)
- Mauvaise indentation

**Exemple correct** :
```qtn
component PlayerStats {
    FP MovementSpeed;  // ✅ Correct
    FP MaxHP;          // ✅ Correct
}
```

**Exemple incorrect** :
```qtn
component PlayerStats {
    float MovementSpeed  // ❌ Erreur: utiliser FP, pas float + manque ;
    FP MaxHP             // ❌ Erreur: manque ;
}
```

---

## Erreurs de Déterminisme

### ❌ Replay diverge (mouvement différent au replay)

**Cause** : Code non-déterministe dans la simulation

**Erreurs courantes** :
1. Utilisation de `float` au lieu de `FP`
2. Utilisation de `Vector3` au lieu de `FPVector3`
3. Utilisation de `Mathf` au lieu de `FPMath`
4. Utilisation de `Random` ou `UnityEngine.Random` au lieu de `frame.RNG`
5. Utilisation de `Time.deltaTime` au lieu de `frame.DeltaTime`
6. Accès à des objets Unity dans les Systems

**Solution** : Vérifier tous les Systems et s'assurer d'utiliser uniquement des types déterministes.

**Checklist déterminisme** :
- ✅ `FP` (Fixed Point) au lieu de `float`
- ✅ `FPVector3` au lieu de `Vector3`
- ✅ `FPQuaternion` au lieu de `Quaternion`
- ✅ `FPMath` au lieu de `Mathf`
- ✅ `frame.RNG` au lieu de `Random`
- ✅ `frame.DeltaTime` au lieu de `Time.deltaTime`
- ❌ Pas d'accès à `GameObject`, `Transform`, etc. dans les Systems

---

## Erreurs de Performance

### ❌ FPS faibles / lags

**Causes possibles** :

**1. Trop d'allocations dans les Systems**
- Solution : Éviter `new`, `List.Add()`, etc. dans les Systems
- Utiliser des structures de données Quantum (`QList`, `QHashMap`)

**2. Boucles inefficaces**
- Solution : Utiliser `SystemMainThreadFilter` pour itérer uniquement les entités avec les components requis

**3. Debug logs dans les Systems**
- Solution : Retirer les `Debug.Log()` dans le code de simulation (ralentissent énormément)

---

## Outils de Debug

### Quantum Profiler
- Menu **Quantum** → **Show Profiler**
- Affiche : frame rate, tick rate, latency, rollbacks

### Quantum Inspector
- Sélectionner une entité dans la scène
- L'Inspector affiche les components Quantum en temps réel

### Console Logging
```csharp
// Dans les Systems (simulation)
Log.Debug($"Player position: {transform->Position}");

// Dans Unity (view)
Debug.Log($"Player position: {transform.position}");
```

---

## Ressources

- [Documentation Quantum officielle](https://doc.photonengine.com/quantum/current/getting-started/quantum-intro)
- [Forum Photon](https://forum.photonengine.com/)
- [Discord Photon](https://discord.gg/photonengine)

---

## Checklist Rapide

Avant de demander de l'aide :

1. ✅ Quantum Codegen exécuté (Quantum → Run Codegen)
2. ✅ Unity recompilé sans erreurs
3. ✅ QuantumRunner présent dans la scène
4. ✅ SimulationConfig et SessionConfig assignés
5. ✅ Entity Prototype créé avec tous les components
6. ✅ Entity View Asset créé et assigné
7. ✅ LocalInput attaché au QuantumRunner
8. ✅ Systems enregistrés dans SystemSetup.User.cs
9. ✅ Console Unity vérifiée pour des erreurs
10. ✅ Quantum Profiler vérifié (Quantum → Show Profiler)
