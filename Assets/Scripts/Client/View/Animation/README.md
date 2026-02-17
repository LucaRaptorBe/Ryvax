# Animation System Implementation

This directory contains the character class-based animation system for Ryvax.

## Overview

The animation system manages character animations using a multi-layer Animator Controller architecture. Each character class has its own dedicated layer with specific parameters and animations.

## Architecture

```
PlayerView (MOBANet.UnityView.Entities)
├── AnimatorLayerManager - Activates/deactivates class layers
├── ICharacterClassController - Interface for class-specific controllers
│   ├── ArcherController
│   ├── MageController
│   ├── FighterController
│   ├── AssassinController
│   ├── TankController
│   ├── HealerController
│   ├── SummonerController
│   └── WarriorController
└── UpdateAnimation() - Called every frame
    ├── UpdateBaseLayerAnimations() - Locomotion, jump/fall
    └── classController.UpdateAnimations() - Class-specific
```

## Files

### Core System
- **`CharacterClass.cs`** - Enum defining available character classes (maps to SimPlayer.ClassId)
- **`ICharacterClassController.cs`** - Interface all class controllers implement
- **`AnimatorLayerManager.cs`** - Manages layer activation/deactivation

### Class Controllers
- **`ArcherController.cs`** - Ranged DPS (IsAiming, Shoot, Reload)
- **`MageController.cs`** - Spell caster (IsCasting, CastFireball, CastMeteor, CastHeal)
- **`FighterController.cs`** - Melee DPS (Attack, ComboIndex, HeavyAttack)
- **`AssassinController.cs`** - Stealth melee (Stealth, QuickStrike, Backstab, Dash)
- **`TankController.cs`** - Defensive (IsBlocking, ShieldBash, Taunt, GroundSlam)
- **`HealerController.cs`** - Support (IsCasting, HealSingle, HealAOE, Resurrect)
- **`SummonerController.cs`** - Summoner (IsSummoning, SummonMinion, SummonElemental, SummonDemon)
- **`WarriorController.cs`** - Melee fighter (Charge, Whirlwind, Execute, BattleShout)

## Usage

### Initialization

When a player spawns, `PlayerView.Initialize()` automatically:
1. Reads the `SimPlayer.ClassId`
2. Activates the appropriate Animator layer (via `AnimatorLayerManager`)
3. Creates the matching class controller (via `CreateClassController()`)
4. Initializes the controller with the Animator reference

```csharp
// In PlayerView.Initialize()
_currentClass = (CharacterClass)simPlayer.ClassId;
_layerManager.SetActiveClass(_currentClass);
_classController = CreateClassController(_currentClass);
_classController.Initialize(_animator);
_classController.OnActivated();
```

### Animation Updates

Every frame, `PlayerView.UpdateAnimation()`:
1. Updates base layer parameters (Speed, IsMoving, IsGrounded, IsJumping, IsFalling)
2. Calls `classController.UpdateAnimations()` for class-specific updates

```csharp
protected override void UpdateAnimation()
{
    UpdateBaseLayerAnimations();
    _classController?.UpdateAnimations();
}
```

### Ability Triggers

When the server confirms an ability use, `NetworkClient` calls `PlayerView.TriggerAbility(abilityIndex)`:

```csharp
// Server event received
playerView.TriggerAbility(abilityIndex: 0);

// Routes to class controller
if (_classController is ArcherController archer)
{
    archer.TriggerShoot(); // Triggers "Shoot" animation
}
```

## Base Layer Parameters

All classes share these base parameters (defined in Base Layer):

| Parameter | Type | Purpose |
|-----------|------|---------|
| Speed | Float | Normalized movement speed (0-1) for blend tree |
| IsMoving | Bool | Whether character is moving |
| IsGrounded | Bool | Whether character is on ground |
| IsJumping | Bool | Whether character is jumping (vel.y > 0.5) |
| IsFalling | Bool | Whether character is falling (vel.y < -0.5) |
| Jump | Trigger | Trigger jump animation |
| Die | Trigger | Trigger death animation |
| GetHit | Trigger | Trigger hit reaction |
| Block | Bool | Whether character is blocking |

## Class-Specific Parameters

Each class has its own parameters (see individual controller files for details):

### Archer
- IsAiming, Shoot, Reload

### Mage
- IsCasting, CastFireball, CastMeteor, CastHeal

### Fighter
- Attack, ComboIndex, HeavyAttack

### Assassin
- Stealth, QuickStrike, BackstabAttack, Dash

### Tank
- IsBlocking, ShieldBash, Taunt, GroundSlam

### Healer
- IsCasting, HealSingle, HealAOE, Resurrect

### Summoner
- IsSummoning, SummonMinion, SummonElemental, SummonDemon

### Warrior
- Charge, Whirlwind, Execute, BattleShout

## Adding a New Class

1. **Add to CharacterClass enum**:
   ```csharp
   public enum CharacterClass
   {
       // ...
       NewClass = 9
   }
   ```

2. **Create controller**:
   ```csharp
   public class NewClassController : ICharacterClassController
   {
       public CharacterClass Class => CharacterClass.NewClass;
       // Implement interface methods...
   }
   ```

3. **Update PlayerView.CreateClassController()**:
   ```csharp
   case CharacterClass.NewClass:
       return new NewClassController();
   ```

4. **Update AnimatorLayerManager** (add layer index cache)

5. **Update HumanoidAnimatorBuilder.cs** (add layer + parameters)

## Performance Notes

- All Animator parameter names are hashed at class initialization (StringToHash)
- Layer indices are cached in AnimatorLayerManager constructor
- Only one class layer is active at a time (weight = 1.0), others are disabled (weight = 0.0)
- Base Layer is always active (weight = 1.0)

## Testing

### Verify Layer Activation
```csharp
var animator = playerView.GetComponentInChildren<Animator>();
int archerLayerIndex = animator.GetLayerIndex("Archer Layer");
float weight = animator.GetLayerWeight(archerLayerIndex);
Debug.Log($"Archer Layer Weight: {weight}"); // Should be 1.0 for Archer
```

### Verify Parameters
```csharp
bool isAiming = animator.GetBool("IsAiming");
Debug.Log($"IsAiming: {isAiming}");
```

## Documentation

For complete system documentation, see:
- **`/Assets/Character/Shared/Animations/ANIMATION_SYSTEM.md`** - Full system overview
- **`/Assets/Character/Shared/Animations/PARAMETERS_REFERENCE.md`** - Complete parameter reference
- **`/Assets/Character/Class/Archer/README.md`** - Archer-specific documentation

## Integration Status

✅ **Implemented**:
- CharacterClass enum
- ICharacterClassController interface
- AnimatorLayerManager
- 8 class controllers (Archer, Mage, Fighter, Assassin, Tank, Healer, Summoner, Warrior)
- PlayerView integration
- Base layer jump/fall parameters (IsJumping, IsFalling)
- SimPlayer.ChampionId → ClassId rename
- Documentation

⏳ **Future Work** (requires network system):
- AbilityCommand network message
- AbilityHandler server-side
- NetworkClient.OnAbilityUsed() event handler
- InputCollector ability input handling (Q/W/E/R keys)

## Notes

- The ability trigger system (`TriggerAbility`) is implemented in PlayerView but requires the network event system to be connected
- Animation events (OnArrowRelease, OnSpellRelease, etc.) are defined in controllers but need to be added to animation clips in Unity
- Class controllers are currently stubs - full ability logic will be implemented when animation assets are imported
