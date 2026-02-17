# Character System - Feature-based Organization

Organisation des personnages du jeu avec une structure feature-based.

## 📁 Structure

```
Character/
├── Shared/                                    ← Ressources communes à toutes les classes
│   ├── Animations/
│   │   ├── HumanoidAnimatorController.controller    ← Controller unique avec layers
│   │   └── Clips/                            ← Animations de base (Idle, Walk, Run, Jump, etc.)
│   ├── Materials/                            ← Matériaux RGB réutilisables
│   └── Textures/                             ← Textures partagées
│
└── Class/                                     ← Classes spécifiques
    ├── Archer/
    │   ├── Models/                           ← M_Archer.fbx, F_Archer.fbx + prefabs
    │   ├── Weapons/                          ← Bow, Arrow, Quiver
    │   ├── Animations/Clips/                 ← Animations spécifiques (Aim, Shoot, Reload)
    │   ├── Materials/                        ← Matériaux spécifiques Archer (si besoin)
    │   └── Textures/                         ← Textures spécifiques Archer (si besoin)
    │
    ├── Mage/
    │   └── (même structure)
    │
    └── Fighter/
        └── (même structure)
```

## 🎭 Animator Controller - Organisation des Layers

Le **HumanoidAnimatorController** contient plusieurs layers:

### Base Layer (Weight: 1.0, toujours actif)
- **Locomotion SubStateMachine**: Idle, Walk, Run, Jump, Fall, Land
- **Combat SubStateMachine**: DrawWeapon, GetHit, Block, etc.
- **Reactions**: Stunned, KnockedDown, Death
- **Emotes**: Sit, Wave, Dance, etc.

### Class Layers (Weight: 0.0 ou 1.0 selon la classe active)
- **Archer Layer** (Upper Body Mask): Aiming, Shoot, Reload
- **Mage Layer** (Upper Body Mask): Casting, Spells
- **Fighter Layer** (Full Body Mask): MeleeCombo, HeavyAttack

## 🎮 Utilisation

### Initialisation d'un personnage

```csharp
// 1. Instancier le modèle de la classe
GameObject archerPrefab = Instantiate(archerMalePrefab);

// 2. Assigner le controller partagé
Animator animator = archerPrefab.GetComponent<Animator>();
animator.runtimeAnimatorController = sharedHumanoidController;

// 3. Activer le layer de la classe
int archerLayerIndex = animator.GetLayerIndex("Archer Layer");
animator.SetLayerWeight(archerLayerIndex, 1f);
```

### Contrôle des animations

```csharp
// Locomotion (Base Layer)
animator.SetFloat("Speed", 1.5f);  // 0=idle, 1=walk, 2=run
animator.SetBool("IsGrounded", true);
animator.SetTrigger("Jump");

// Archer (Archer Layer)
animator.SetBool("IsAiming", true);
animator.SetTrigger("Shoot");

// Mage (Mage Layer)
animator.SetTrigger("CastFireball");
```

## 📋 Checklist pour ajouter une nouvelle classe

1. ✅ Créer `Character/Class/[ClassName]/`
2. ✅ Copier les modèles dans `Models/`
3. ✅ Copier les armes/équipements dans `Weapons/`
4. ✅ Créer/ajouter les animations spécifiques dans `Animations/Clips/`
5. ✅ Ajouter un nouveau layer dans `HumanoidAnimatorController`
6. ✅ Configurer l'Avatar Mask pour ce layer
7. ✅ Créer les états et transitions dans le layer
8. ✅ Tester l'activation/désactivation du layer en code

## 🔧 Migration depuis PolysplitGames

**Règle:** On ne supprime/coupe RIEN de PolysplitGames, on copie uniquement.

### Fichiers copiés pour Archer:
- ✅ Models: M_Archer.fbx, F_Archer.fbx + prefabs
- ✅ Weapons: BowBasic, ArrowBasic, ArrowQuiverBasic (fbx + prefabs)
- ✅ Materials: RGBRecolor_Body.mat, RGBRecolor_Objects.mat, RGBRecolor.shadergraph
- ✅ Textures: genericRGB_medievalTexture.png

### Source originale:
- Modèles: `PolysplitGames/LowPolyMedievalFantasyHeroes/BasicHeroes/`
- Armes: `PolysplitGames/LowPolyMedievalFantasyHeroes/BasicWeapons/`
- Matériaux/Textures: `PolysplitGames/LowPolyMedievalFantasyHeroes/Materials_Shaders_Textures/`

## 📝 Notes

- Le pack PolysplitGames contient uniquement des **poses statiques** (pas d'animations de gameplay)
- Vous devrez créer/acheter des animations pour Walk, Run, Attack, etc.
- Les animations peuvent être de type **Humanoid Generic** pour être partagées entre tous les personnages
- Utilisez des **Avatar Masks** pour permettre les actions simultanées (marcher + tirer)

## 🎯 Prochaines étapes

1. Créer/importer des animations de gameplay
2. Configurer le HumanoidAnimatorController avec les layers
3. Créer les Avatar Masks (UpperBody, FullBody)
4. Migrer les autres classes (Mage, Fighter, Knight, etc.)
5. Implémenter le système de sélection de personnage (genre + classe)
