# Documentation Index

Index complet de toute la documentation Ryvax, centralisée et colocalisée.

**Dernière mise à jour:** 2026-02-18

---

## Quick Start

| Objectif | Document |
|----------|----------|
| Comprendre l'architecture | [Architecture/01_System_Overview.md](Architecture/01_System_Overview.md) |
| Débugger un problème | [Development/01_Debugging_Guide.md](Development/01_Debugging_Guide.md) |

---

## Documentation Centralisée (`/Docs/`)

### Architecture
| Fichier | Description |
|---------|-------------|
| [01_System_Overview.md](Architecture/01_System_Overview.md) | Vue d'ensemble des composants, namespaces, data flow |

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
| [01_Server_Loop.md](Server/01_Server_Loop.md) | Boucle serveur, tick rate |
| [02_AOI_System.md](Server/02_AOI_System.md) | Area of Interest, visibilité |

### Client
| Fichier | Description |
|---------|-------------|
| [01_Client_Architecture.md](Client/01_Client_Architecture.md) | NetworkClient, visual smoothing |
| [02_Input_System.md](Client/02_Input_System.md) | InputCollector, IntentBuilder |

### GameSim
| Fichier | Description |
|---------|-------------|
| [01_GameSim_Overview.md](GameSim/01_GameSim_Overview.md) | SimWorld, tick system |
| [02_Command_System.md](GameSim/02_Command_System.md) | CommandDispatcher, handlers |
| [03_Entity_Model.md](GameSim/03_Entity_Model.md) | SimPlayer, états |

### Development
| Fichier | Description |
|---------|-------------|
| [01_Debugging_Guide.md](Development/01_Debugging_Guide.md) | Guide de débugage |
| [02_Instrumentation.md](Development/02_Instrumentation.md) | Logs, corrélation |
| [03_Testing_Checklist.md](Development/03_Testing_Checklist.md) | Procédures de test |

---

## Documentation Colocalisée (`/Assets/`)

### Système d'Animation
| Fichier | Description |
|---------|-------------|
| [/Assets/Character/README.md](/Assets/Character/README.md) | Vue d'ensemble personnages |
| [/Assets/Character/Shared/Animations/01_Animation_System.md](/Assets/Character/Shared/Animations/01_Animation_System.md) | Architecture animation multi-layer |
| [/Assets/Character/Shared/Animations/02_Parameters_Reference.md](/Assets/Character/Shared/Animations/02_Parameters_Reference.md) | Référence paramètres (8 classes) |
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
| [/TROUBLESHOOTING.md](/TROUBLESHOOTING.md) | Guide dépannage |
| [/claude.md](/claude.md) | Instructions AI assistant |

---

---

## Convention de Documentation

### Où placer la documentation?

| Type de doc | Emplacement | Exemple |
|-------------|-------------|---------|
| Architecture globale | `/Docs/Architecture/` | Design rationale |
| Flux réseau | `/Docs/Network/` | Data flow |
| Guide développeur | `/Docs/Development/` | Debugging guide |
| Doc technique d'un module | `README.md` dans le dossier | `/Assets/Character/README.md` |
| Référence API/paramètres | Colocalisé avec le code | `02_Parameters_Reference.md` |

### Règles

1. **Toujours mettre à jour cet index** quand on ajoute un nouveau fichier .md
2. **Doc colocalisée** = technique, spécifique au code adjacent
3. **Doc centralisée** = architecture, concepts, guides transverses
4. **Nommer clairement** : `README.md` pour vue d'ensemble, nom descriptif sinon

---

**Maintenu par:** Équipe Ryvax
