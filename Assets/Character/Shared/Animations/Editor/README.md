# Animator Controller Setup Script

Ce script d'édition automatise la configuration complète du HumanoidAnimatorController.

## 🚀 Utilisation

### 1. Ouvrir le setup
Dans Unity:
```
Menu: Character > Setup Animator Controller
```

### 2. Configuration automatique (sans animations)

Si vous n'avez pas encore d'animations, cliquez sur:
```
"Setup Controller (Without Animations)"
```

Cela va créer:
- ✅ Locomotion SubStateMachine (Idle, Walk/Run BlendTree, Jump, Fall, Land)
- ✅ Combat SubStateMachine (Combat Idle, Get Hit, Block)
- ✅ Death state
- ✅ Archer Layer avec Aiming SubStateMachine
- ✅ Toutes les transitions avec conditions
- ✅ Avatar Masks assignés

### 3. Configuration avec animations (optionnel)

Si vous avez des animations, assignez-les dans l'interface puis cliquez:
```
"Setup Controller (With Animations)"
```

### 4. Ajouter les autres layers

Cliquez sur:
- `"Add Mage Layer"` - Ajoute le layer Mage avec casting et spells
- `"Add Fighter Layer"` - Ajoute le layer Fighter avec combos

## 📋 Ce que le script fait automatiquement

### Base Layer
✅ **Locomotion SubStateMachine:**
- Idle state
- Walk/Run BlendTree (paramètre: Speed)
- Jump Start → Jump Loop → Fall → Land
- Transitions automatiques avec conditions

✅ **Combat SubStateMachine:**
- Combat Idle
- Get Hit (trigger: GetHit)
- Block (trigger: Block)
- Transitions automatiques

✅ **Death State:**
- Any State → Death (trigger: Die)

### Archer Layer
✅ **Aiming SubStateMachine:**
- Aim Start → Aim Idle → Aim End
- Transition avec paramètre IsAiming

✅ **Actions:**
- Shoot (trigger depuis Aim Idle)
- Reload (trigger depuis Archer Idle)

✅ **Avatar Mask:**
- UpperBodyMask automatiquement assigné

### Mage Layer (optionnel)
✅ **Casting SubStateMachine:**
- Cast Start → Cast Loop → Cast End

✅ **Spells:**
- Cast Fireball (trigger: CastFireball)
- Cast Meteor (trigger: CastMeteor)
- Cast Heal (trigger: CastHeal)

✅ **Paramètres ajoutés:**
- IsCasting (bool)
- CastFireball, CastMeteor, CastHeal (triggers)

### Fighter Layer (optionnel)
✅ **Combo System:**
- Attack 1 → Attack 2 → Attack 3
- Paramètre ComboIndex pour choisir l'attaque

✅ **Heavy Attack:**
- Attaque chargée indépendante

✅ **Paramètres ajoutés:**
- Attack (trigger)
- ComboIndex (int)
- HeavyAttack (trigger)

## 🎯 Workflow recommandé

### Sans animations (développement initial)
1. Lancer "Setup Controller (Without Animations)"
2. Tester la logique des transitions
3. Assigner les animations plus tard

### Avec animations
1. Importer vos animations dans `Character/Shared/Animations/Clips/`
2. Assigner les clips dans l'interface du setup
3. Lancer "Setup Controller (With Animations)"

## 🔧 Personnalisation

Le script est un point de départ. Vous pouvez:
- Modifier les durées de transition (actuellement 0.1-0.2s)
- Ajouter des conditions supplémentaires
- Créer vos propres états
- Ajuster les thresholds des BlendTrees

## ⚙️ Paramètres créés

Le script crée automatiquement tous les paramètres nécessaires:

**Base:**
- Speed (float)
- IsGrounded (bool)
- InCombat (bool)
- Jump (trigger)
- GetHit (trigger)
- Block (trigger)
- Die (trigger)

**Archer:**
- IsAiming (bool)
- Shoot (trigger)
- Reload (trigger)

**Mage (si ajouté):**
- IsCasting (bool)
- CastFireball, CastMeteor, CastHeal (triggers)

**Fighter (si ajouté):**
- Attack (trigger)
- ComboIndex (int)
- HeavyAttack (trigger)

## 🐛 Troubleshooting

### Le script ne trouve pas le controller
→ Vérifiez que `HumanoidAnimatorController.controller` existe dans:
`Assets/Character/Shared/Animations/`

### Les Avatar Masks ne sont pas assignés
→ Relancez le script, il devrait les assigner automatiquement

### Je veux réinitialiser le controller
→ Le script écrase le contenu existant. Faites une backup avant!

## 📝 Notes

- Le script utilise l'API `UnityEditor.Animations`
- Les modifications sont sauvegardées automatiquement
- Un message de confirmation apparaît à la fin
- Consultez la Console Unity pour les logs détaillés
