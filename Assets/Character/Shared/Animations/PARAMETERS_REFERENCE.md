# Animation Parameters Reference

Complete reference for all Animator Controller parameters across all layers.

## Base Layer (Always Active)

### Locomotion Parameters

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **Speed** | Float | 0.0 - 1.0 | Controls Idle → Run blend tree (0.0 = Idle, 1.0 = Run) | Updated every frame from horizontal velocity |
| **IsMoving** | Bool | true/false | Whether character is moving | Updated every frame (velocity > 0.1f) |
| **IsGrounded** | Bool | true/false | Whether character is on ground | Updated every frame from SimPlayer.Transform.IsGrounded |

**Implementation**:
```csharp
float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
float normalizedSpeed = Mathf.Clamp01(horizontalSpeed / 8f);
bool isMoving = horizontalSpeed > 0.1f;

_animator.SetFloat(SpeedHash, normalizedSpeed, 0.05f, Time.deltaTime);
_animator.SetBool(IsMovingHash, isMoving);
_animator.SetBool(IsGroundedHash, _simPlayer.Transform.IsGrounded);
```

**Blend Tree Structure**:
```
Locomotion State (Base Layer)
└── Idle-Run Blend Tree
    ├── Idle animation (threshold: 0.0)
    └── Run animation (threshold: 1.0)
```

The blend tree automatically interpolates between Idle and Run based on the Speed parameter. No separate Walk animation needed.

### Airborne Parameters

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **IsJumping** | Bool | true/false | Character is ascending (jumping) | velocity.y > 0.5f && !isGrounded |
| **IsFalling** | Bool | true/false | Character is descending (falling) | velocity.y < -0.5f && !isGrounded |
| **Jump** | Trigger | - | Trigger jump animation | Server-sent jump event |

**Implementation**:
```csharp
float verticalVelocity = velocity.y;
bool isJumping = !isGrounded && verticalVelocity > 0.5f;
bool isFalling = !isGrounded && verticalVelocity < -0.5f;

_animator.SetBool(IsJumpingHash, isJumping);
_animator.SetBool(IsFallingHash, isFalling);
```

**State Transitions**:
```
Idle/Walk/Run → Jump (IsJumping = true)
Jump → Jump Loop (IsJumping = true, time > 0.2s)
Jump Loop → Fall (IsFalling = true)
Fall → Land (IsGrounded = true)
Land → Idle (animation complete)
```

### Combat Parameters

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **GetHit** | Trigger | - | Trigger hit reaction animation | Server sends damage event |
| **Block** | Bool | true/false | Character is blocking | Player holds block button (Tank) |
| **Die** | Trigger | - | Trigger death animation | SimPlayer.Stats.IsAlive = false |

---

## Archer Layer

**Avatar Mask**: Upper Body (allows movement while aiming)

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **IsAiming** | Bool | true/false | Whether archer is in aim mode | Player holds right-click |
| **Shoot** | Trigger | - | Fire arrow | Player presses left-click while aiming |
| **Reload** | Trigger | - | Reload arrows | Out of ammo or manual reload (R key) |

**Ability Mapping**:
- Q (0): Shoot
- W (1): Reload
- E (2): Special arrow type
- R (3): Ultimate ability

**State Machine**:
```
Idle/Move → Aim Start (IsAiming = true)
Aim Start → Aim Loop (transition time)
Aim Loop → Shoot (Shoot trigger)
Shoot → Aim Loop (return to aiming)
Aim Loop → Aim End (IsAiming = false)
Aim End → Idle/Move

Aim Loop → Reload (Reload trigger)
Reload → Aim Loop (animation complete)
```

**Example Usage**:
```csharp
public class ArcherController : ICharacterClassController
{
    private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
    private static readonly int ShootHash = Animator.StringToHash("Shoot");
    private static readonly int ReloadHash = Animator.StringToHash("Reload");

    public void SetAiming(bool isAiming)
    {
        _animator.SetBool(IsAimingHash, isAiming);
    }

    public void TriggerShoot()
    {
        _animator.SetTrigger(ShootHash);
    }

    public void TriggerReload()
    {
        _animator.SetTrigger(ReloadHash);
    }
}
```

---

## Mage Layer

**Avatar Mask**: Upper Body (allows movement while casting)

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **IsCasting** | Bool | true/false | Whether mage is casting | During any spell cast |
| **CastFireball** | Trigger | - | Cast fireball spell | Player uses fireball ability |
| **CastMeteor** | Trigger | - | Cast meteor spell | Player uses meteor ability |
| **CastHeal** | Trigger | - | Cast heal spell | Player uses heal ability |

**Ability Mapping**:
- Q (0): Fireball (fast projectile)
- W (1): Meteor (AOE delayed)
- E (2): Heal (self or target)
- R (3): Ultimate spell

**State Machine**:
```
Idle/Move → Cast Start (CastFireball/Meteor/Heal trigger)
Cast Start → Cast Loop (IsCasting = true)
Cast Loop → Cast End (spell complete)
Cast End → Idle/Move (IsCasting = false)
```

**Example Usage**:
```csharp
public class MageController : ICharacterClassController
{
    private static readonly int IsCastingHash = Animator.StringToHash("IsCasting");
    private static readonly int CastFireballHash = Animator.StringToHash("CastFireball");
    private static readonly int CastMeteorHash = Animator.StringToHash("CastMeteor");
    private static readonly int CastHealHash = Animator.StringToHash("CastHeal");

    public void CastFireball()
    {
        _animator.SetBool(IsCastingHash, true);
        _animator.SetTrigger(CastFireballHash);
    }

    public void OnCastComplete()
    {
        _animator.SetBool(IsCastingHash, false);
    }
}
```

---

## Fighter Layer

**Avatar Mask**: Full Body (attacks override locomotion)

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **Attack** | Trigger | - | Execute next attack in combo | Player clicks attack |
| **ComboIndex** | Int | 0-2 | Current combo step | Auto-incremented per attack |
| **HeavyAttack** | Trigger | - | Execute heavy attack | Player holds attack button |

**Ability Mapping**:
- Q (0): Attack (combo)
- W (1): Heavy Attack
- E (2): Dash strike
- R (3): Area spin attack

**Combo System**:
```
Idle → Attack 1 (Attack trigger, ComboIndex = 0)
Attack 1 → Attack 2 (Attack trigger within window, ComboIndex = 1)
Attack 2 → Attack 3 (Attack trigger within window, ComboIndex = 2)
Attack 3 → Idle (combo complete, ComboIndex = 0)

Any State → Heavy Attack (HeavyAttack trigger)
Heavy Attack → Idle
```

**Example Usage**:
```csharp
public class FighterController : ICharacterClassController
{
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
    private static readonly int HeavyAttackHash = Animator.StringToHash("HeavyAttack");

    private int _currentCombo = 0;

    public void TriggerAttack()
    {
        _animator.SetTrigger(AttackHash);
        _animator.SetInteger(ComboIndexHash, _currentCombo);
        _currentCombo = (_currentCombo + 1) % 3;
    }

    public void TriggerHeavyAttack()
    {
        _animator.SetTrigger(HeavyAttackHash);
        _currentCombo = 0; // Reset combo
    }

    public void ResetCombo()
    {
        _currentCombo = 0;
        _animator.SetInteger(ComboIndexHash, 0);
    }
}
```

---

## Assassin Layer

**Avatar Mask**: Full Body

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **Stealth** | Bool | true/false | Whether in stealth mode | Player activates stealth |
| **QuickStrike** | Trigger | - | Fast attack | Q ability |
| **BackstabAttack** | Trigger | - | Critical attack from behind | E ability |
| **Dash** | Trigger | - | Quick dash movement | W ability |

**Ability Mapping**:
- Q (0): Quick Strike
- W (1): Dash
- E (2): Backstab
- R (3): Shadow clone

---

## Tank Layer

**Avatar Mask**: Full Body

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **IsBlocking** | Bool | true/false | Holding shield up | Player holds block |
| **ShieldBash** | Trigger | - | Shield attack | Q ability |
| **Taunt** | Trigger | - | Taunt enemies | W ability |
| **GroundSlam** | Trigger | - | AOE stun | E ability |

**Ability Mapping**:
- Q (0): Shield Bash
- W (1): Taunt
- E (2): Ground Slam
- R (3): Last Stand

---

## Healer Layer

**Avatar Mask**: Upper Body

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **IsCasting** | Bool | true/false | Casting heal spell | Any heal ability |
| **HealSingle** | Trigger | - | Single target heal | Q ability |
| **HealAOE** | Trigger | - | Area heal | W ability |
| **Resurrect** | Trigger | - | Revive ally | R ability |

**Ability Mapping**:
- Q (0): Single Heal
- W (1): AOE Heal
- E (2): Cleanse
- R (3): Resurrect

---

## Summoner Layer

**Avatar Mask**: Upper Body

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **IsSummoning** | Bool | true/false | Summoning creature | Any summon ability |
| **SummonMinion** | Trigger | - | Summon basic minion | Q ability |
| **SummonElemental** | Trigger | - | Summon elemental | W ability |
| **SummonDemon** | Trigger | - | Summon demon | R ability |

**Ability Mapping**:
- Q (0): Summon Minion
- W (1): Summon Elemental
- E (2): Command minions
- R (3): Summon Demon

---

## Warrior Layer

**Avatar Mask**: Full Body

| Parameter | Type | Range/Values | Purpose | When to Use |
|-----------|------|--------------|---------|-------------|
| **Charge** | Trigger | - | Charge forward | Q ability |
| **Whirlwind** | Trigger | - | Spinning attack | W ability |
| **Execute** | Trigger | - | Execute low HP target | E ability |
| **BattleShout** | Trigger | - | Buff allies | R ability |

**Ability Mapping**:
- Q (0): Charge
- W (1): Whirlwind
- E (2): Execute
- R (3): Battle Shout

---

## Parameter Naming Conventions

### Bools
- `Is[State]` - Current state (IsMoving, IsGrounded, IsAiming)
- `Can[Action]` - Ability check (CanJump, CanAttack)

### Triggers
- `[Action]` - Simple action name (Jump, Attack, Shoot)
- `[Action][Target]` - Action with target (CastFireball, SummonMinion)

### Floats
- `[Property]` - Direct property (Speed, Health)
- `[Property]Normalized` - 0-1 range (SpeedNormalized)

### Ints
- `[Property]Index` - Index counter (ComboIndex, SpellIndex)
- `[Property]Count` - Count value (ArrowCount, ManaCount)

---

## Performance Best Practices

1. **Use StringToHash**
   ```csharp
   private static readonly int ParamHash = Animator.StringToHash("ParamName");
   animator.SetBool(ParamHash, value);
   ```

2. **Cache Layer Indices**
   ```csharp
   private readonly int _layerIndex;
   _layerIndex = animator.GetLayerIndex("Layer Name");
   ```

3. **Batch Parameter Updates**
   ```csharp
   // Update all parameters in same frame
   animator.SetFloat(SpeedHash, speed);
   animator.SetBool(IsMovingHash, isMoving);
   animator.SetBool(IsGroundedHash, isGrounded);
   ```

4. **Use Triggers Sparingly**
   - Triggers are consumed after one frame
   - Use Bools for persistent states
   - Use Triggers only for one-shot animations

5. **Damping for Smooth Transitions**
   ```csharp
   // Smooth speed changes
   animator.SetFloat(SpeedHash, speed, dampTime: 0.05f, Time.deltaTime);
   ```
