# Archer Class Animation Documentation

## Overview

The Archer is a **ranged DPS class** specializing in precise shots and mobility. Uses an **Upper Body avatar mask** allowing movement while aiming.

## Layer Information

- **Layer Name**: "Archer Layer"
- **Avatar Mask**: Upper Body
- **Layer Index**: 1
- **Default Weight**: 0.0 (activated when ClassId = 1)

## Parameters

| Parameter | Type | Purpose | Controlled By |
|-----------|------|---------|---------------|
| **IsAiming** | Bool | Whether archer is in aim mode | ArcherController.SetAiming() |
| **Shoot** | Trigger | Fire arrow | ArcherController.TriggerShoot() |
| **Reload** | Trigger | Reload arrows | ArcherController.TriggerReload() |

## Animation States

### State Machine Flow

```
Entry → Idle (layer inactive)

Idle/Move → Aim Start (IsAiming = true)
    ↓
Aim Start → Aim Loop (automatic transition)
    ↓
Aim Loop ──Shoot──→ Shoot → Return to Aim Loop
    │
    └──Reload──→ Reload → Return to Aim Loop
    │
    └──(IsAiming = false)──→ Aim End → Idle
```

### State Details

#### Aim Start
- **Duration**: ~0.2s
- **Purpose**: Transition into aim pose
- **Transitions To**: Aim Loop (automatic)
- **Animation**: Character raises bow

#### Aim Loop
- **Duration**: Looping
- **Purpose**: Hold aim position
- **Transitions To**:
  - Shoot (on Shoot trigger)
  - Reload (on Reload trigger)
  - Aim End (when IsAiming = false)
- **Animation**: Character holds bow drawn

#### Shoot
- **Duration**: ~0.3s
- **Purpose**: Fire arrow
- **Transitions To**: Aim Loop (automatic return)
- **Animation**: Release arrow, recoil
- **Events**:
  - Frame 5: `OnArrowRelease()` - spawn projectile
  - Frame 10: `OnShootComplete()` - reset to aim

#### Reload
- **Duration**: ~1.0s
- **Purpose**: Reload arrows
- **Transitions To**: Aim Loop (automatic return)
- **Animation**: Reach for quiver, reload
- **Events**:
  - Frame 15: `OnReloadComplete()` - restore ammo

#### Aim End
- **Duration**: ~0.2s
- **Purpose**: Lower bow
- **Transitions To**: Idle (automatic)
- **Animation**: Character lowers bow

## Abilities

### Q - Shoot
- **Command**: AbilityCommand with abilityIndex = 0
- **Animation**: Triggers "Shoot" parameter
- **Cooldown**: 0.5s (server-side)
- **Requirements**: Must be in aim mode (IsAiming = true)

### W - Reload
- **Command**: AbilityCommand with abilityIndex = 1
- **Animation**: Triggers "Reload" parameter
- **Cooldown**: 2.0s (server-side)
- **Requirements**: Arrow count < max

### E - Special Arrow (TODO)
- **Command**: AbilityCommand with abilityIndex = 2
- **Animation**: TBD
- **Purpose**: Fire special arrow type (explosive, ice, etc.)

### R - Ultimate (TODO)
- **Command**: AbilityCommand with abilityIndex = 3
- **Animation**: TBD
- **Purpose**: Multi-shot or rain of arrows

## Input Mapping

```
Right Mouse Button (Hold) → SetAiming(true)
Right Mouse Button (Release) → SetAiming(false)
Left Mouse Button (While Aiming) → TriggerShoot()
R Key → TriggerReload()
```

## Network Flow

### Shoot Ability Flow

```
1. Player holds RMB
   → InputCollector sets isAiming = true
   → [Future] Send command to server or set locally
   → ArcherController.SetAiming(true)
   → Animator transitions to Aim Loop

2. Player presses LMB
   → InputCollector.SendAbilityCommand(abilityIndex: 0)
   → NetworkClient sends to server
   → Server validates (cooldown, ammo, etc.)
   → Server executes (raycast for hit, apply damage)
   → Server broadcasts ReliableEvent(AbilityUsed, abilityIndex: 0)
   → All clients receive event
   → NetworkClient.OnAbilityUsed()
   → PlayerView.TriggerAbility(0)
   → ArcherController.TriggerShoot()
   → Animator plays Shoot animation
   → AnimationEvent "OnArrowRelease" spawns visual arrow
```

## Code Integration

### ArcherController.cs

```csharp
using UnityEngine;

namespace MOBANet.Client.Animation
{
    public class ArcherController : ICharacterClassController
    {
        private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
        private static readonly int ShootHash = Animator.StringToHash("Shoot");
        private static readonly int ReloadHash = Animator.StringToHash("Reload");

        private Animator _animator;
        private bool _isAiming;

        public CharacterClass Class => CharacterClass.Archer;

        public void Initialize(Animator animator)
        {
            _animator = animator;
        }

        public void UpdateAnimations()
        {
            // Currently no per-frame updates needed
            // Future: Update aim direction, arrow draw strength, etc.
        }

        public void OnActivated()
        {
            Debug.Log("Archer animations activated");
        }

        public void OnDeactivated()
        {
            // Reset state when switching classes
            _isAiming = false;
            _animator?.SetBool(IsAimingHash, false);
        }

        // Public API for triggering abilities
        public void SetAiming(bool isAiming)
        {
            _isAiming = isAiming;
            _animator?.SetBool(IsAimingHash, isAiming);
        }

        public void TriggerShoot()
        {
            if (!_isAiming)
            {
                Debug.LogWarning("Cannot shoot while not aiming");
                return;
            }
            _animator?.SetTrigger(ShootHash);
        }

        public void TriggerReload()
        {
            _animator?.SetTrigger(ReloadHash);
        }

        // Animation event callbacks
        public void OnArrowRelease()
        {
            // Called from animation event
            // Spawn visual arrow projectile
        }

        public void OnShootComplete()
        {
            // Called from animation event
            // Reset to aim pose
        }

        public void OnReloadComplete()
        {
            // Called from animation event
            // Restore ammo count
        }
    }
}
```

### Usage in PlayerView.cs

```csharp
// Initialization
public void Initialize(SimPlayer simPlayer, bool isLocal, NetworkClient networkClient)
{
    _simPlayer = simPlayer;
    CharacterClass playerClass = (CharacterClass)simPlayer.ClassId;

    // Activate Archer layer if ClassId = 1
    _layerManager.SetActiveClass(playerClass);

    // Create Archer controller
    _classController = CreateClassController(playerClass);
    _classController.Initialize(_animator);
    _classController.OnActivated();

    base.Initialize(simPlayer.Id, isLocal, networkClient);
}

// Trigger ability from network event
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
}
```

## Animation Assets

### Required Animation Clips

Place in: `Assets/Character/Class/Archer/Animations/`

- `Archer_Aim_Start.fbx` - Raise bow to aim
- `Archer_Aim_Loop.fbx` - Hold bow drawn
- `Archer_Aim_End.fbx` - Lower bow
- `Archer_Shoot.fbx` - Release arrow
- `Archer_Reload.fbx` - Grab arrow from quiver

### Import Settings

- **Loop Time**:
  - Aim Loop: ✅ Enabled
  - All others: ❌ Disabled
- **Root Transform Position (Y)**: ✅ Bake Into Pose
- **Root Transform Rotation**: ✅ Bake Into Pose
- **Root Transform Position (XZ)**: Based on Animation (allow movement)

## Blending with Base Layer

The Archer layer uses an **Upper Body mask**, which means:

✅ **Allowed Simultaneously**:
- Walking/Running while aiming
- Jumping while aiming (upper body stays in aim pose)
- Falling while aiming

❌ **Not Allowed**:
- Shooting overrides base layer temporarily
- Death animation takes priority (Base Layer trigger)

### Example Blending

```
Player is walking → Base Layer plays "Walk" (legs moving)
Player holds RMB → Archer Layer plays "Aim Loop" (upper body aiming)
Result: Character walks while aiming (both animations blend)

Player presses LMB → Archer Layer plays "Shoot"
Result: Upper body shoots, legs continue walking
```

## Debugging

### Verify Layer Active

```csharp
var animator = playerView.GetComponentInChildren<Animator>();
int archerLayerIndex = animator.GetLayerIndex("Archer Layer");
float weight = animator.GetLayerWeight(archerLayerIndex);

Debug.Log($"Archer Layer Weight: {weight}");
// Should be 1.0 for Archer players, 0.0 for others
```

### Verify Parameters

```csharp
bool isAiming = animator.GetBool("IsAiming");
Debug.Log($"IsAiming: {isAiming}");

// Check current state
var currentState = animator.GetCurrentAnimatorStateInfo(archerLayerIndex);
Debug.Log($"Current State: {currentState.shortNameHash}");
```

### Common Issues

**Issue**: Shoot animation doesn't play
- Check if IsAiming = true
- Verify layer weight = 1.0
- Check if Shoot trigger is being set
- Verify transition conditions in Animator window

**Issue**: Can't move while aiming
- Verify Avatar Mask is set to "Upper Body"
- Check Base Layer weight = 1.0
- Verify mask includes only upper body bones

**Issue**: Animations are jerky
- Check if Speed parameter uses damping (0.05f)
- Verify animation clips have proper frame rate
- Check network interpolation is enabled

## Future Enhancements

- [ ] Charged shot (hold LMB for power)
- [ ] Aim sway based on movement speed
- [ ] Arrow type selection (fire, ice, poison)
- [ ] Multi-shot ultimate ability
- [ ] Quiver visibility based on arrow count
- [ ] Procedural aim IK for precise targeting
