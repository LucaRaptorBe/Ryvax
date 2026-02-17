# Shared Animations

Ce dossier contient le système d'animation partagé entre toutes les classes de personnages.

## 📁 Contenu

- **HumanoidAnimatorController.controller** - Controller unique avec tous les layers
- **UpperBodyMask.mask** - Masque pour animer uniquement le haut du corps (Archer, Mage)
- **FullBodyMask.mask** - Masque pour animer tout le corps (Fighter, réactions)
- **Clips/** - Animations de base partagées (à créer)

## 🎭 Structure du Controller

### Base Layer (Weight: 1.0, toujours actif)

**Locomotion SubStateMachine:**
- Idle
- Walk/Run (BlendTree avec paramètre Speed)
- Jump
- Fall
- Land

**Combat SubStateMachine:**
- Combat Idle
- DrawWeapon
- SheathWeapon
- GetHit
- Block

**Réactions (accessible via Any State):**
- Stunned
- KnockedDown
- GetUp
- Death

### Archer Layer (Weight: 0.0 par défaut, UpperBodyMask)

**Aiming SubStateMachine:**
- AimStart
- AimIdle
- AimEnd

**Actions:**
- Shoot
- Reload

### Mage Layer (Weight: 0.0 par défaut, UpperBodyMask)
*(À ajouter)*

**Casting SubStateMachine:**
- CastStart
- CastLoop
- CastEnd

**Spells:**
- CastFireball
- CastMeteor
- CastHeal

### Fighter Layer (Weight: 0.0 par défaut, FullBodyMask)
*(À ajouter)*

**MeleeCombo SubStateMachine:**
- Attack1
- Attack2
- Attack3

**Actions:**
- HeavyAttack
- ShieldBash

## 📋 Paramètres de l'Animator

### Base Layer
| Nom | Type | Description |
|-----|------|-------------|
| Speed | Float | Vitesse de locomotion (0=idle, 1=walk, 2=run) |
| IsGrounded | Bool | Si le personnage touche le sol |
| InCombat | Bool | Si en mode combat |
| Jump | Trigger | Déclenche le saut |
| GetHit | Trigger | Prendre un coup |
| Block | Trigger | Bloquer |
| Die | Trigger | Mort |

### Archer Layer
| Nom | Type | Description |
|-----|------|-------------|
| IsAiming | Bool | Si en mode visée |
| Shoot | Trigger | Tirer une flèche |
| Reload | Trigger | Recharger |

### Mage Layer (à ajouter)
| Nom | Type | Description |
|-----|------|-------------|
| IsCasting | Bool | Si en incantation |
| CastFireball | Trigger | Lancer Fireball |
| CastMeteor | Trigger | Lancer Meteor |
| CastHeal | Trigger | Lancer Heal |

### Fighter Layer (à ajouter)
| Nom | Type | Description |
|-----|------|-------------|
| Attack | Trigger | Attaquer |
| ComboIndex | Int | Index du combo (1, 2, 3) |
| HeavyAttack | Trigger | Attaque lourde |

## 🎮 Utilisation en code

### Initialisation

```csharp
using UnityEngine;

public class CharacterSetup : MonoBehaviour
{
    [SerializeField] private RuntimeAnimatorController sharedController;
    [SerializeField] private CharacterClass characterClass;

    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
        animator.runtimeAnimatorController = sharedController;

        // Activer le layer de la classe
        ActivateClassLayer(characterClass);
    }

    void ActivateClassLayer(CharacterClass charClass)
    {
        // Désactiver tous les layers de classe
        animator.SetLayerWeight(animator.GetLayerIndex("Archer Layer"), 0f);
        animator.SetLayerWeight(animator.GetLayerIndex("Mage Layer"), 0f);
        animator.SetLayerWeight(animator.GetLayerIndex("Fighter Layer"), 0f);

        // Activer le layer correspondant
        switch (charClass)
        {
            case CharacterClass.Archer:
                animator.SetLayerWeight(animator.GetLayerIndex("Archer Layer"), 1f);
                break;
            case CharacterClass.Mage:
                animator.SetLayerWeight(animator.GetLayerIndex("Mage Layer"), 1f);
                break;
            case CharacterClass.Fighter:
                animator.SetLayerWeight(animator.GetLayerIndex("Fighter Layer"), 1f);
                break;
        }
    }
}

public enum CharacterClass
{
    Archer,
    Mage,
    Fighter,
    Knight,
    Rogue
}
```

### Contrôle de locomotion

```csharp
public class CharacterMovement : MonoBehaviour
{
    private Animator animator;

    void Update()
    {
        // Définir la vitesse (0=idle, 1=walk, 2=run)
        float speed = CalculateSpeed();
        animator.SetFloat("Speed", speed);

        // Saut
        if (Input.GetButtonDown("Jump") && IsGrounded())
        {
            animator.SetTrigger("Jump");
        }

        // État au sol
        animator.SetBool("IsGrounded", IsGrounded());
    }
}
```

### Contrôle Archer

```csharp
public class ArcherController : MonoBehaviour
{
    private Animator animator;

    void Update()
    {
        // Commencer/arrêter la visée
        if (Input.GetMouseButtonDown(1)) // Right click
        {
            animator.SetBool("IsAiming", true);
        }

        if (Input.GetMouseButtonUp(1))
        {
            animator.SetBool("IsAiming", false);
        }

        // Tirer
        if (Input.GetMouseButtonDown(0) && IsAiming()) // Left click
        {
            animator.SetTrigger("Shoot");
        }

        // Recharger
        if (Input.GetKeyDown(KeyCode.R))
        {
            animator.SetTrigger("Reload");
        }
    }
}
```

### Contrôle Mage (exemple)

```csharp
public class MageController : MonoBehaviour
{
    private Animator animator;

    void Update()
    {
        // Spell 1: Fireball
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            animator.SetTrigger("CastFireball");
        }

        // Spell 2: Meteor
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            animator.SetTrigger("CastMeteor");
        }

        // Spell 3: Heal
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            animator.SetTrigger("CastHeal");
        }
    }
}
```

### Contrôle Fighter (exemple)

```csharp
public class FighterController : MonoBehaviour
{
    private Animator animator;
    private int comboIndex = 1;

    void Update()
    {
        // Attaque normale (combo)
        if (Input.GetMouseButtonDown(0))
        {
            animator.SetInteger("ComboIndex", comboIndex);
            animator.SetTrigger("Attack");

            comboIndex = (comboIndex % 3) + 1; // Cycle 1->2->3->1
        }

        // Attaque lourde
        if (Input.GetKeyDown(KeyCode.LeftShift))
        {
            animator.SetTrigger("HeavyAttack");
            comboIndex = 1; // Reset combo
        }
    }
}
```

## 🎨 Avatar Masks

### UpperBodyMask
Utilisé pour: **Archer Layer**, **Mage Layer**

**Activé (Weight: 1):**
- ✅ Tête
- ✅ Colonne vertébrale
- ✅ Bras gauche
- ✅ Bras droit

**Désactivé (Weight: 0):**
- ❌ Pelvis/Hanches
- ❌ Jambes

**Résultat:** Le personnage peut viser/caster tout en marchant/courant

### FullBodyMask
Utilisé pour: **Fighter Layer**, **Réactions** (Stunned, Death, etc.)

**Activé (Weight: 1):**
- ✅ Tout le corps

**Résultat:** L'animation override complètement les animations de locomotion

## ⚠️ Important

1. **Ce controller est un template de base** - Vous devrez l'ouvrir dans Unity et:
   - Ajouter les clips d'animation dans chaque état
   - Créer les transitions entre états
   - Configurer les BlendTrees
   - Ajouter les layers Mage et Fighter

2. **Les animations ne sont pas incluses** - Le pack PolysplitGames contient uniquement des poses statiques. Vous devez:
   - Créer vos propres animations
   - Acheter un pack d'animations
   - Utiliser Mixamo ou d'autres sources

3. **Les Avatar Masks doivent être assignés** - Dans Unity:
   - Sélectionner le layer dans l'Animator
   - Assigner le mask approprié dans le champ "Mask"

## 📚 Ressources

- [Unity Animator Documentation](https://docs.unity3d.com/Manual/class-AnimatorController.html)
- [Animation Layers](https://docs.unity3d.com/Manual/AnimationLayers.html)
- [Avatar Masks](https://docs.unity3d.com/Manual/class-AvatarMask.html)
- [Mixamo (animations gratuites)](https://www.mixamo.com/)
