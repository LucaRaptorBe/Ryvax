# Documentation Index

Index complet de toute la documentation Ryvax, centralisée et colocalisée.

**Dernière mise à jour:** 2026-02-04

---

## Quick Start

| Objectif | Document |
|----------|----------|
| Comprendre l'architecture | [Architecture/01_System_Overview.md](Architecture/01_System_Overview.md) |
| Comprendre le netcode LoL-style | [Architecture/02_LoL_Style_Netcode.md](Architecture/02_LoL_Style_Netcode.md) |
| Débugger un problème | [Development/Debugging_Guide.md](Development/Debugging_Guide.md) |
| Voir le status d'implémentation | [/IMPLEMENTATION_STATUS.md](/IMPLEMENTATION_STATUS.md) |

---

## Documentation Centralisée (`/Docs/`)

### Architecture
| Fichier | Description |
|---------|-------------|
| [01_System_Overview.md](Architecture/01_System_Overview.md) | Vue d'ensemble des composants |
| [02_LoL_Style_Netcode.md](Architecture/02_LoL_Style_Netcode.md) | Implémentation netcode LoL-style |
| [03_Design_Rationale.md](Architecture/03_Design_Rationale.md) | Pourquoi cette architecture |

### Network
| Fichier | Description |
|---------|-------------|
| [01_Network_Architecture.md](Network/01_Network_Architecture.md) | FishNet adapter, polling |
| [02_Message_Specifications.md](Network/02_Message_Specifications.md) | Formats InputPacket, SnapshotDelta |
| [03_Data_Flow.md](Network/03_Data_Flow.md) | Flux complet input→visual |
| [04_Performance_Metrics.md](Network/04_Performance_Metrics.md) | Métriques latence (50ms) |

### Server
| Fichier | Description |
|---------|-------------|
| [Server_Loop.md](Server/Server_Loop.md) | Boucle serveur, tick rate |
| [AOI_System.md](Server/AOI_System.md) | Area of Interest, visibilité |

### Client
| Fichier | Description |
|---------|-------------|
| [Client_Architecture.md](Client/Client_Architecture.md) | NetworkClient, visual smoothing |
| [Input_System.md](Client/Input_System.md) | InputCollector, IntentBuilder |

### GameSim
| Fichier | Description |
|---------|-------------|
| [GameSim_Overview.md](GameSim/GameSim_Overview.md) | SimWorld, tick system |
| [Command_System.md](GameSim/Command_System.md) | CommandDispatcher, handlers |
| [Entity_Model.md](GameSim/Entity_Model.md) | SimPlayer, états |

### Development
| Fichier | Description |
|---------|-------------|
| [Debugging_Guide.md](Development/Debugging_Guide.md) | Guide de débugage |
| [Instrumentation.md](Development/Instrumentation.md) | Logs, corrélation |
| [Testing_Checklist.md](Development/Testing_Checklist.md) | Procédures de test |

---

## Documentation Colocalisée (`/Assets/`)

### Système d'Animation
| Fichier | Description |
|---------|-------------|
| [/Assets/Character/README.md](/Assets/Character/README.md) | Vue d'ensemble personnages |
| [/Assets/Character/Shared/Animations/ANIMATION_SYSTEM.md](/Assets/Character/Shared/Animations/ANIMATION_SYSTEM.md) | Architecture animation multi-layer |
| [/Assets/Character/Shared/Animations/PARAMETERS_REFERENCE.md](/Assets/Character/Shared/Animations/PARAMETERS_REFERENCE.md) | Référence paramètres (8 classes) |
| [/Assets/Character/Shared/Animations/README.md](/Assets/Character/Shared/Animations/README.md) | Guide animations partagées |
| [/Assets/Character/Shared/Animations/Editor/README.md](/Assets/Character/Shared/Animations/Editor/README.md) | HumanoidAnimatorBuilder |

### Classes de Personnages
| Fichier | Description |
|---------|-------------|
| [/Assets/Character/Class/Archer/README.md](/Assets/Character/Class/Archer/README.md) | Doc classe Archer |
| [/Assets/Character/Class/Archer/Animations/README.md](/Assets/Character/Class/Archer/Animations/README.md) | Animations Archer |

### Code Animation
| Fichier | Description |
|---------|-------------|
| [/Assets/Scripts/Client/View/Animation/README.md](/Assets/Scripts/Client/View/Animation/README.md) | Controllers d'animation |

---

## Documentation Racine (`/`)

| Fichier | Description |
|---------|-------------|
| [/IMPLEMENTATION_STATUS.md](/IMPLEMENTATION_STATUS.md) | Status d'implémentation animation |
| [/ANIMATION_SYSTEM_SUMMARY.md](/ANIMATION_SYSTEM_SUMMARY.md) | Résumé système animation |
| [/TROUBLESHOOTING.md](/TROUBLESHOOTING.md) | Guide dépannage |
| [/claude.md](/claude.md) | Instructions AI assistant |

---

## Archive

Documentation historique (debugging, anciennes versions) dans [Archive/](Archive/).

Voir [Archive/README.md](Archive/README.md) pour la liste complète.

---

## Convention de Documentation

### Où placer la documentation?

| Type de doc | Emplacement | Exemple |
|-------------|-------------|---------|
| Architecture globale | `/Docs/Architecture/` | Design rationale |
| Flux réseau | `/Docs/Network/` | Data flow |
| Guide développeur | `/Docs/Development/` | Debugging guide |
| Doc technique d'un module | `README.md` dans le dossier | `/Assets/Character/README.md` |
| Référence API/paramètres | Colocalisé avec le code | `PARAMETERS_REFERENCE.md` |

### Règles

1. **Toujours mettre à jour cet index** quand on ajoute un nouveau fichier .md
2. **Doc colocalisée** = technique, spécifique au code adjacent
3. **Doc centralisée** = architecture, concepts, guides transverses
4. **Nommer clairement** : `README.md` pour vue d'ensemble, nom descriptif sinon

---

**Maintenu par:** Équipe Ryvax
