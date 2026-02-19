# GAME_DESIGN.md - Design du jeu

## Vision du prototype

Titre: RYVAX

Concept: Jeu multijoueur coopératif et strategique, 2 equipes de 6 joueurs s'affrontent pour détruire le crystal, situé dans la base adverse. Apres avoir choisi leur classe, ils devront combattre pour des objectifs et des ressources, afin de renforcer leur personnage et contester des objectifs les rapprochant vers la victoire. 

Durée d'une partie: 20-30min

Genre: Moba

Platforme: Steam

Public cible: joueurs de moba/joueurs de MMORPG pvp/Casual

Experiences recherchées: Coopération, appartenance a une communauté, expression de skill, imagination

Références: League of Legends, Albion, SuperVive

Objectif du prototype: Tester combats, camps, teamfights, taille de map, level design, feeling


**----------------------------------------------------------**


## Condition de victoire

Pour accéder au crystal, il faut passer à travers les portes du chateau(la porte intérieure et une des portes extérieures).
Les portes extérieures seront maintenue par un mage chacune, qu'il faudra eliminer pour passer. Ce dernier sera défendu par des archers, artilleurs et soldats. 
La porte intérieure sera maintenue par un mage, qu'il faudra éliminer pour passer. Ce dernier sera défendu par le Roi lui même, un boss.
Les mages sont bel et bien ce qui empeche de passer, donc ce n'est pas obligatoire de tuer les soldats ou le roi pour passer. Néanmoins, les mages ressusciteront au bout de 3min, les défendeurs tels que les soldats ou le roi ne respawneront pas.
Le crystal a un certain nombre de point de vie, assez pour qu'une personne seule mette 40sec à le détruire.

Plus la partie dure, plus les temps de respawn sont longs, et plus le boss du cratère fera de degats.


**----------------------------------------------------------**


## Gameplay Core Loop

Les joueurs spawneront à leur chateau respectif, et seront guidés vers des camps à farmer (ressources ou monstres), ce qui :
- augmentera leur niveau, leur donnant acces à de nouvelles capacités
- leur donnera de l'Or, leur permettant d'acheter des objets
- pourra dropper des items aléatoires/définis
- les guideront vers les points importants de la map

Des evenements sur la map seront déclenchés, qui inciteront les joueurs à combattre pour les récompenses (Or, Objects, avantages)

Farm -> Gagner ressources -> Debloquer -> Objectif -> Progression vers base ennemie -> ect


**----------------------------------------------------------**


## Classes

**Archer**:
    *600 base ranged*
    *passif*: 
        each auto generate a stack of x, at 5 stack, next dmg crit.
    *spell1*: 
        BASE: piercing arrows ez Q (single target skillshot)
        TYPE: PROJECTILE
        RANGE: 1200
        WIDTH: 100
        SPEED: 2000
        CAST TIME: 0.25s
        COOLDOWN: 6/5/4 s
        DMG: Y
        SPE1: reset spell 2 cooldown
        SPE2: reset spell 3 cooldown
    *spell2*:
        BASE: gains bonus atkspd for 4s (can stack) and empowers next auto attack (increased dmg)
        TYPE: BUFF + EMPOWERED ATTACK
        COOLDOWN: 12/10/8 s
        CAST TIME: 0.1s (to be defined with design)
        SPE1: empower attack also mark target, gain 200 range agaisnt marked target
        SPE2: empower attack mark target, attack agaisnt marked target bounce on 3 closed ennemies
    *spell3*: 
        BASE: roulade
        TYPE: MOBILITY
        COOLDOWN: 10/7.5/5 s
        SPE1: buff ms (2.5s, 30%)
        SPE2: invisible (1s) (using attack or spell get you out)
    *spell4*: 
        BASE: Ulti Cait (on target)
        TYPE: PROJECTILE
        COOLDOWN: 50/45/40s
        DMG: Y
        spe1: cannot be blocked (ennemies or obstacle) (Single Target)
        spe2: "explosion" on hit(Multi Target)


**----------------------------------------------------------**


## Mécaniques du jeu

**Déplacement**:
    - clavier ou souris
    - saut
    - rappel
    - monture

**Combat**:
    - auto attack ciblable
    - 4 capacités: dont une capacité de mouvement et un ultimate
    - 2 items consommables (comme une capacité, par exemple: lance un javelot)

**Interactions**:
    - interaction avec certains objets ou capacités


**----------------------------------------------------------**


## Systèmes

**Systeme d'Experience**:
    *Acquisition* => tuer camp, tuer ennemi, farmer ressource, realisation d'objectif sur la map
    *Partage* => Tous les membres de l'equipe reçoivent l'exp
    *Montée de level* => donne plus de stats et des points de compétence

**Systeme de Loot**:
    *Acquisition* => 
        L'or est automatiquement attribué au joueur 
        De meme pour l'experience 
        Et les ressources
        Les objets tombés seront ramassable avec la touche d'interaction si on dispose d'une place dans un des slots objet
    *Drop table* =>
        Chaque objectif/camp/coffre aura un liste d'objets droppable avec une probabilité pour chaque objet
        Les resources et l'experience auront un drop fix, peut etre qui augmentera au fil de la partie
    *Loot individuel* =>
        Les drops d'objet seront individuel, mais l'objet recupéré peut etre droppé apres avoir été ramassé, si un allié le veut
    *Loot partagé* =>
        Les golds, et l'experience sont partagé à tous les alliés, mais sont individuelles.
        Exemple: Si un allié tue un camp, tous les alliés gagnent 100 d'or chacun, si un d'eux achete un objet à la boutique, seul leur or est décompté.

**Systeme de compétences**:
    *Déblocage des compétences* => 
        on commence la partie 
            level 1 avec ability 1, 
        Puis se débloque
            level 2: ability 2, 
            level 3: ability 3,
            level 4: ability 4
    *Amelioration des compétences* =>
        Puis chaque niveau donnera un point de compétence, utilisé pour améliorer une compétence de son choix, pour     augmenter les stats de cette derniere
        Max niveau de compétence : 4 (le niveau de base + les 3 ameliorations)
    *Changement de specialisation* =>
        Tous les 2 niveaux (commence à level 6), en plus du point de compétence, on gagnera un point de specialisation, utilisé pour améliorer le sort de base de 2 manieres différentes.
        *Un point de specialisation peut etre acheter dans la boutique si on souhaite changer de gameplay*

**Systeme de targetting**
    *targetting* =>
        Le ciblage d'une cible se fait en cliquant sur elle. Cela affiche ses informations.
    *Attaque et sorts ciblable* =>
        Les attaques et sorts ciblables partent par defaut sur l'unité ciblée. Si au moment de le projectile touche un obstacle, il sera détruit.

**Systeme d'achat**:
    *Où acheter* => 
        Les achats se pourront se faire que depuis la base, l'interface d'achat sera neanmoins disponible partout
    *Que vend la boutique* =>
        Des Objets
        Des points de specialization
    *Coûts* =>
        Acheter un item demandera de l'or.
        Un partage d'Or sera possible si un allié en a besoin.

**Systeme d'évènements**:
    *Quand* =>
        Les évènement pourront etre déclenchés par des actions ou de maniere définis
        L'*Invasion de monstre* serait par exemple déclenché lorsque la *Cage* est détruite.
        La *Bataille de boule de neige* aura par exemple une chance sur deux (choix entre volcan et montagne) d'etre déclenchée à la Xème minute de jeu.
        Les évènement déclenchés de manière définis seront annoncés Xmin avant.
        Ils dureront une période de temps défini ou jusqu'à complétion.
    *Ces derniers octoieront des récompenses*

**Systeme de Mort et de Respawn**:
    *Gains* => tuer un ennemi donne gold et experience, et fait tomber ses items
    *Perte* => en cas de mort, les items seront drop et un le joueur devra attendre un temps avant de respawn. Il ne perd rien d'autre.
    *Spawn Innacessible* => Les joueurs spawnent à un endroit en hauteur, innacessible des autres joueurs.

**Systeme de dégats**:
    *Calcul* => 
        Il n'y aura pas de degats magiques/physiques, ni de resistances. Juste des degats de bases qui seront réduits ou augmentés par les sorts eux meme, pas d'item qui donnent des stats, la seule source sera les degats indiqués dans le sort, idem pour l'auto attack.

**Systeme de brouillard de guerre**
    *Vision* =>
        On ne pourra pas voir les unités située plus loin que nous. Les allies partagent leur vision.
    *Gain de vision temporaire* =>
        Des objectifs ou objects pourraient donner de la vision.

**Système d’intelligence artificielle des PNJ**
    *Comportement* =>
        Tous les PNJ utilisent le même moteur d’IA.
        Chaque unité est définie par :
            un profil de mobilité (Static ou Mobile)
            des schémas d’attaque
    *Fonctionnement* =>
        L’IA sélectionne une attaque valide et tente de satisfaire ses préconditions (portée, etc.).
        Si l’unité est Mobile, elle se déplace pour entrer en portée.
        Si elle est Static, elle n’effectue aucun déplacement.
    *Targetting* =>
        Les ia attaqueront les cibles les plus proches.
        Ils perdront l'aggro si la cible est innateignable pendant plus de 1sec.
        Ils auront une range d'aggro, et une zone limite.

**Système de Statut**
    *CC* => stun, displaced (mouvement + stun si collision avec un mur), slow, root
    *Superposition des CC* => Si un meme CC est appliqué sur la cible, la valeur la plus élevée au moment de l'application est prise en compte.
    *Invisibility* => Invisible pour les ennemis, sort de l'invisibilité en attaquant ou en utilisant une ability.
    *Superposition des buffs* => Si un meme buff est appliqué sur une cible, les deux durées sont additionnées.

**Systeme de ping**
    *Comme sur Lol basically*

**Systeme d'Emotes**
    *Mandatory.*

**----------------------------------------------------------**


## Map Design

**Verticalité**:
    Le monde ne sera pas un terrain plat comme la plupart des Moba, il aura du dénivelé, créant des hauteurs et des points strategiques comme sur Albion. Les projectiles étant pour la plupart bloqué par le décor, il faut design la map en pensant à ça. La hauteur sera donc tres contestée.

**Equipes**:
    L'équipe bleu se situera en bas à gauche de la map, et l'équipe rouge en haut à droite.

**Biomes**:
    *Il sera composé de plusieurs biomes, chaque biomes possédant ses monstres et ressources, les biomes aux centres étants les plus contestés par les joueurs*:
    - Sur la diagonal qui relie topright et bottomleft se trouvent le Chateau de chaque équipe, composé d'une cour intérieure et extérieure, proche de la *Cage*.
    - Sur la diagonale qui relie topleft et bottomright: *Le Cratère*, la zone qui sépare les deux camps, necessitant de sauter dedans pour pouvoir passer de l'autre coté. Un ojectif mid/lategame s'y trouve
    - Dans les 4 zones restantes séparées par le *Cratère*: les *Forêts*.

**Position des camps**:
    *Forêts* =>
        Des camps/ressources seront disposés dans les forets.
    *Cratère* =>
        Des camps/ressources seront disposés dans le cratère, pour sortir du cratère, il faudra éliminer le camps adjacents à ces zones.
        Un objectif/Boss sera présent au centre du cratère. Tuer le boss l'enverra attaquer la base adverse.

**Raccourcis**:
    Des raccourcis pour vite faire le trajet *Chateau*-*topleft* ou *Chateau*-*bottomright*, seront disposés sur les bords de map, le sens du raccourci sera déterminé par qui a eliminé le Mini-boss se situant à coté de leur zone respective. 
    Par exemple du BlueSide, si l'équipe bleu élimine le mini-boss proche du topleft, le raccourci s'activera pour *Chateau* vers *topleft*, si l'équipe red l'élimine, le raccourci s'activera pour *topleft* vers *Chateau*.

**Respawn des Camps/Ressources**:
    Les camps/ressources respawneront toutes les Xmin.



**----------------------------------------------------------**


### Direction Artistique

**Style**


**----------------------------------------------------------**


### Lore 


Nous sommes des mercenaires engagés par un roi pour combattre contre un autre.
Le crystal au centre récupere l'âme des monstres que l'équipe tue pour les convertir en pouvoir, nous octroyant à tous de l'expérience. 
Ce pouvoir est également utilisé pour contenir le monstres situé dans la cage, si une équipe nous vole des camps, la balance est brisé.


**----------------------------------------------------------**


### Interface et UX

**Gameplay**:
    *Barre de sorts*
    *Barre d'items*
    *Ciblage*
    *Barre de vie*
    *Stats*
    *Description des sorts/items*
    *Map*

**Interfaces**:
    *Settings*
    *Boutique*
    *Score*
    *Construction*
    *Map en grand*?


**----------------------------------------------------------**


### Monétisation Free to play

**Cosmétiques**:
    *Skins* =>
        Weapons
        Clothes
    *Mounts*


**----------------------------------------------------------**


## Outils
    Unity version 6000.0.61f1