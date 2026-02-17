# Animation System Documentation

## Overview

The Ryvax animation system uses a **multi-layer Animator Controller** architecture to support 8 different character classes, each with unique abilities and animations while sharing common locomotion.

## Architecture

### Layer Structure

```
HumanoidAnimatorController.controller
├── Base Layer (Weight: 1.0, Always Active)
│   ├── Locomotion (Idle → Run blend tree)
│   ├── Airborne (Jump, Jump Loop, Fall, Land)
│   ├── Combat (GetHit, Block)
│   └── Death
│
├── Archer Layer (Weight: 0.0 or 1.0)
│   ├── Aim Start, Aim Loop, Aim End
│   ├── Shoot
│   └── Reload
│
├── Mage Layer (Weight: 0.0 or 1.0)
│   ├── Cast Start, Cast Loop, Cast End
│   ├── Cast Fireball
│   ├── Cast Meteor
│   └── Cast Heal
│
├── Fighter Layer (Weight: 0.0 or 1.0)
│   ├── Attack 1, Attack 2, Attack 3 (Combo)
│   └── Heavy Attack
│
└── [5 more class layers...]
```

### Avatar Masks

**Upper Body Mask** (Ranged/Casters):
- Classes: Archer, Mage, Healer, Summoner
- Allows locomotion + upper body actions simultaneously
- Player can move while aiming/casting

**Full Body Mask** (Melee):
- Classes: Fighter, Assassin, Tank, Warrior
- Overrides locomotion during attacks
- Player commits to attack animations

## Parameters Reference

### Base Layer Parameters (Always Active)

| Parameter | Type | Purpose | Updated By |
|-----------|------|---------|------------|
| Speed | Float | Normalized movement speed (0-1) | PlayerView.UpdateBaseLayerAnimations() |
| IsMoving | Bool | Whether character is moving | PlayerView.UpdateBaseLayerAnimations() |
| IsGrounded | Bool | Whether character is on ground | PlayerView.UpdateBaseLayerAnimations() |
| IsJumping | Bool | Whether character is jumping (vel.y > 0.5) | PlayerView.UpdateBaseLayerAnimations() |
| IsFalling | Bool | Whether character is falling (vel.y < -0.5) | PlayerView.UpdateBaseLayerAnimations() |
| Jump | Trigger | Trigger jump animation | Server event → PlayerView |
| Die | Trigger | Trigger death animation | Server event → PlayerView |
| GetHit | Trigger | Trigger hit reaction | Server event → PlayerView |
| Block | Bool | Whether character is blocking | Server state → PlayerView |

### Class-Specific Parameters

See [PARAMETERS_REFERENCE.md](./PARAMETERS_REFERENCE.md) for complete list of all class parameters.

## System Flow

### Initialization Flow

```
1. Server spawns player with ClassId
2. Client receives entity spawn snapshot
3. PlayerView.Initialize(SimPlayer)
4. Read SimPlayer.ClassId → CharacterClass enum
5. AnimatorLayerManager.SetActiveClass(class)
   - Disable all class layers (weight = 0.0)
   - Enable selected class layer (weight = 1.0)
6. Create appropriate ICharacterClassController
7. classController.Initialize(animator)
8. classController.OnActivated()
```

### Animation Update Flow (Every Frame)

```
1. EntityView.Update() → UpdateAnimation()
2. PlayerView.UpdateBaseLayerAnimations()
   - Read SimPlayer.Transform.Velocity
   - Calculate Speed, IsMoving, IsGrounded
   - Calculate IsJumping, IsFalling from vertical velocity
   - Update Animator parameters
3. classController.UpdateAnimations()
   - Update class-specific parameters
   - Handle ability states
```

### Ability Animation Flow (Network-Synchronized)

```
1. Player presses ability key (Q/W/E/R)
2. InputCollector.SendAbilityCommand(abilityIndex)
3. NetworkClient sends command to server
4. Server validates ability (cooldown, mana, range, etc.)
5. Server executes ability logic (damage, effects, etc.)
6. Server broadcasts ReliableEvent(AbilityUsed) to all clients
7. All clients receive event → NetworkClient.OnAbilityUsed()
8. PlayerView.TriggerAbility(abilityIndex)
9. ClassController triggers animation
   - Example: ArcherController.TriggerShoot()
   - Sets Animator.SetTrigger("Shoot")
10. Animator plays animation on Class Layer
```

### Jump/Fall Flow

```
1. SimPlayer.TickMovement() applies gravity to velocity.y
2. Server sends snapshot with updated velocity
3. PlayerView.OnSnapshotReceived() updates SimPlayer state
4. PlayerView.UpdateBaseLayerAnimations() reads velocity.y
5. Calculate states:
   - IsJumping = !isGrounded && velocity.y > 0.5f
   - IsFalling = !isGrounded && velocity.y < -0.5f
6. Animator transitions:
   Idle → Jump → Jump Loop → Fall → Land → Idle
```

## Usage Guide

### Activating a Class at Runtime

```csharp
// In PlayerView.Initialize()
CharacterClass playerClass = (CharacterClass)simPlayer.ClassId;
_layerManager.SetActiveClass(playerClass);
```

### Triggering Abilities

```csharp
// In PlayerView (called from NetworkClient event handler)
public void TriggerAbility(byte abilityIndex)
{
    if (_classController is ArcherController archer)
    {
        switch (abilityIndex)
        {
            case 0: archer.TriggerShoot(); break;
            case 1: archer.TriggerReload(); break;
        }
    }
    else if (_classController is MageController mage)
    {
        switch (abilityIndex)
        {
            case 0: mage.CastFireball(); break;
            case 1: mage.CastMeteor(); break;
            case 2: mage.CastHeal(); break;
        }
    }
}
```

### Adding a New Character Class

1. **Create Animation Assets**
   - Import FBX animations
   - Create animation clips
   - Place in `Assets/Character/Class/[ClassName]/Animations/`

2. **Update HumanoidAnimatorBuilder.cs**
   - Add class to `SetupLayers()`
   - Add parameters in `SetupParameters()`
   - Create states and transitions

3. **Create Class Controller**
   ```csharp
   public class NewClassController : ICharacterClassController
   {
       public CharacterClass Class => CharacterClass.NewClass;

       public void Initialize(Animator animator) { }
       public void UpdateAnimations() { }
       public void OnActivated() { }
       public void OnDeactivated() { }
   }
   ```

4. **Update CharacterClass Enum**
   ```csharp
   public enum CharacterClass
   {
       // ...
       NewClass = 9
   }
   ```

5. **Update PlayerView.CreateClassController()**
   ```csharp
   case CharacterClass.NewClass:
       return new NewClassController();
   ```

6. **Document the Class**
   - Create `Assets/Character/Class/NewClass/README.md`
   - Document parameters, abilities, animations

## Performance Considerations

### String Hashing
Always use `Animator.StringToHash()` for parameter names:

```csharp
// ✅ GOOD - Hash computed once at class initialization
private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
_animator.SetBool(IsAimingHash, true);

// ❌ BAD - String lookup every frame
_animator.SetBool("IsAiming", true);
```

### Layer Index Caching
Cache layer indices instead of looking up by name:

```csharp
// ✅ GOOD - Cached in constructor
private readonly int _archerLayerIndex;

public AnimatorLayerManager(Animator animator)
{
    _archerLayerIndex = animator.GetLayerIndex("Archer Layer");
}

// ❌ BAD - Lookup every frame
animator.SetLayerWeight(animator.GetLayerIndex("Archer Layer"), 1f);
```

### Update Frequency
- Base parameters (Speed, IsMoving, etc.): Every frame
- Ability triggers: Event-driven only
- Layer weights: Only on class change

## Debugging

### Verify Layer Activation
```csharp
var animator = playerView.GetComponentInChildren<Animator>();
for (int i = 0; i < animator.layerCount; i++)
{
    Debug.Log($"Layer {i} ({animator.GetLayerName(i)}): Weight = {animator.GetLayerWeight(i)}");
}
```

### Verify Parameters
```csharp
foreach (var param in animator.parameters)
{
    Debug.Log($"{param.name} ({param.type}) = {animator.GetFloat/GetBool/GetInteger(param.nameHash)}");
}
```

### Animation Events
Add AnimationEvent callbacks to sync gameplay with animations:
```csharp
// In animation clip, add event at arrow release frame
public void OnArrowRelease()
{
    // Spawn projectile
}
```

## See Also

- [PARAMETERS_REFERENCE.md](./PARAMETERS_REFERENCE.md) - Complete parameter list for all classes
- [Assets/Character/Class/Archer/README.md](../../Class/Archer/README.md) - Archer-specific documentation
- [Assets/Scripts/Client/View/Animation/](../../../Scripts/Client/View/Animation/) - Animation system code
