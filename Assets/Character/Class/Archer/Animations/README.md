# Archer Animations

Ce dossier contient les animations spécifiques à la classe Archer.

## Structure

```
Clips/
├── AimStart.anim       - Début de la visée
├── AimIdle.anim        - Maintien de la visée
├── AimEnd.anim         - Fin de la visée
├── ShootArrow.anim     - Tir de flèche
└── Reload.anim         - Recharge du carquois
```

## Animator Controller

L'Archer utilise le **HumanoidAnimatorController** partagé situé dans:
`Assets/Character/Shared/Animations/HumanoidAnimatorController.controller`

### Archer Layer

Le layer "Archer Layer" dans le controller contient:
- **Aiming SubStateMachine**: États de visée (AimStart → AimIdle → AimEnd)
- **ShootArrow**: Animation de tir
- **Reload**: Animation de rechargement

### Avatar Mask

Le layer Archer utilise un **Upper Body Mask** pour permettre de:
- Viser/tirer tout en marchant
- Garder les animations de locomotion pour les jambes

## Utilisation en code

```csharp
// Activer le layer Archer
int archerLayerIndex = animator.GetLayerIndex("Archer Layer");
animator.SetLayerWeight(archerLayerIndex, 1f);

// Commencer à viser
animator.SetBool("IsAiming", true);

// Tirer une flèche
animator.SetTrigger("Shoot");

// Arrêter de viser
animator.SetBool("IsAiming", false);
```

## Notes

- Les animations de locomotion (Walk, Run, Jump, etc.) sont dans `Character/Shared/Animations/Clips/`
- Ces animations sont partagées entre toutes les classes
- Actuellement, ce dossier est vide car le pack PolysplitGames ne contient que des poses statiques
- Vous devrez ajouter vos propres animations de gameplay ici
