• League of Legends utilise une architecture client serveur autoritaire
• Le serveur est source unique de vérité pour l’état du jeu
• Les clients sont des terminaux de rendu et d’input, jamais décisionnaires

• Connexion réseau
• Transport principal via UDP custom (pas TCP)
• Fiabilité partielle implémentée au-dessus d’UDP pour certains messages critiques
• Priorisation des paquets selon importance gameplay

• Modèle de simulation
• Simulation lockstep côté serveur, pas côté client
• Tick rate serveur ≈ 30 Hz
• Tous les calculs de combat, collisions, RNG, cooldowns sont serveur-side

• Inputs joueurs
• Le client envoie uniquement des intentions (move, cast, target)
• Timestamp + séquence pour réordonner et rejeter les inputs obsolètes

• Latence et compensation
• Pas de client-side prediction complète comme dans les FPS
• Légère input buffering pour masquer le jitter
• Hit validation strictement serveur, pas de lag compensation rétroactive

• Synchronisation état
• Snapshots d’état envoyés régulièrement aux clients
• Delta compression pour réduire la bande passante
• Interpolation côté client pour lisser le rendu

• Anti-cheat structurel
• Impossible pour le client de forcer dégâts, positions ou stats
• Toute divergence est corrigée par le serveur
• Cheats possibles limités à vision, scripts, automation

• Scalabilité
• Un serveur par partie
• Datacenters régionaux pour réduire la latence physique
• Matchmaking et services hors-game séparés du serveur de match

• Trade-offs assumés
• Gameplay très déterministe et équitable
• Sensation parfois “lourde” à haute latence
• Moins réactif qu’un FPS mais beaucoup plus robuste contre la triche

le serveur et le client (network) tick en même temps?

Non.

• Serveur
• Tick fixe et autoritaire ≈ 30 Hz
• Cadence stable, indépendante des clients
• Avance en temps discret global de la partie

• Client
• Pas de tick réseau strictement synchronisé
• Rendu à fréquence variable (FPS)
• Réseau traité de façon asynchrone
• Interpolation entre snapshots serveur

• Synchronisation temporelle
• Le client se cale sur le temps serveur, pas l’inverse
• Les messages serveur contiennent des timestamps de tick
• Le client maintient volontairement un retard constant (buffer) pour lisser jitter et pertes

• Inputs
• Le client envoie des inputs avec timestamp
• Le serveur les consomme au tick courant ou suivant
• Aucun mécanisme de lockstep client↔serveur

• Conséquence clé
• Serveur et client ne “tickent” pas ensemble
• Ils sont désynchronisés mais cohérents, via interpolation et correction
• Toute divergence est tranchée par le serveur

• Comparaison
• Contrairement à certains RTS lockstep purs
• Contrairement aux FPS avec client-side prediction agressive
• League of Legends privilégie équité et déterminisme à la simultanéité perçue

lol a un client-side prediction + réconciliation ? ou juste interpolation?

• League of Legends
• Pas de client-side prediction complète + réconciliation au sens FPS
• Principalement interpolation, avec micro-prédiction très limitée

• Ce qui existe réellement
• Interpolation entre snapshots serveur pour le rendu
• Input buffering pour réduire la sensation de latence
• Animation locale immédiate de certaines actions (feedback visuel)

• Ce qui n’existe pas
• Pas de simulation client autoritaire rollbackable
• Pas de réconciliation client après correction serveur
• Pas de rewind/lag compensation serveur

• Mouvements
• Le client affiche le déplacement avant confirmation serveur
• Si divergence, correction silencieuse (snap ou glissement)
• C’est une approximation visuelle, pas une prédiction physique

• Sorts et combats
• Validation strictement serveur
• Aucun dégât confirmé côté client
• Les projectiles et impacts peuvent être annulés ou ajustés

• Pourquoi ce choix
• Top-down, click-to-move, contraintes faibles de précision instantanée
• Anti-cheat prioritaire
• Déterminisme du gameplay > sensation immédiate

• Résumé net
• Interpolation oui
• Client-side prediction marginale, non généralisée
• Réconciliation structurée non
• Architecture volontairement conservatrice et robuste

• Le client affiche le déplacement avant confirmation serveur:
a quel degré? ce n'est pas de la prédicition+réconciliation?

Oui, mais au degré minimal. Ce n’est pas le couple prediction + reconciliation tel qu’utilisé dans les FPS.

• Ce que fait réellement le client LoL
• Dès le clic, le client extrapole localement une trajectoire simple
• Modèle ultra pauvre: vitesse connue, pathfinding grossier, pas de collisions fines
• Objectif unique: feedback visuel immédiat

• Ce que le serveur fait
• Recalcule le path réel (collisions, unités, terrain, buffs)
• Applique la position autoritaire à chaque tick
• Envoie des snapshots

• Réconciliation?
• Il n’y a pas de rollback client
• Pas de re-simulation des ticks passés
• Le client corrige vers l’état serveur, sans rejouer l’historique

• Différence clé avec une vraie prediction+reconciliation
• FPS: le client simule N ticks → reçoit l’état serveur → rollback → rejoue inputs
• League of Legends:
• le client ne maintient pas d’historique cohérent
• il affiche une trajectoire provisoire
• il accepte passivement la vérité serveur

• Pourquoi on ne parle pas de “vraie” prédiction
• La simulation client n’est ni complète ni déterministe
• Les erreurs ne sont pas corrigées par ré-exécution
• Les écarts sont absorbés visuellement

• Formulation rigoureuse
• Extrapolation visuelle locale
• Correction autoritaire unidirectionnelle
• Pas de prédiction réconciliée bidirectionnelle

• Conclusion nette
• Techniquement: oui, il y a anticipation client
• Architecturalement: non, ce n’est pas prediction+reconciliation
• Le serveur reste l’unique timeline du jeu

si la trajectoire est provisoire, je présume la position l'est aussi.
si la différence est elevée (a cause d'un ping élevé) et que le client ramene le champion vers une position tres differente que la position calculée du serveur, qu'est ce qui se passe ?


Exact. La position client est provisoire. Quand l’écart devient grand, il y a correction autoritaire. Pas de magie, juste des règles simples.

• Détection de l’écart
• Le client compare sa position locale à la position serveur reçue
• Seuils internes: petit écart ≠ gros écart
• L’écart augmente avec ping, path invalide, collisions non prédites

• Petit écart
• Correction lissée sur quelques frames
• Glissement progressif vers la position serveur
• Le joueur ne le perçoit presque pas

• Écart moyen
• Lissage plus agressif
• Accélération artificielle vers la position serveur
• Sensation de “tiré en arrière”

• Gros écart
• Snap immédiat à la position serveur
• Téléportation visible
• Fréquent à très haut ping ou perte de paquets

• Pourquoi ils acceptent le snap
• Pas de rollback possible
• Priorité à la cohérence globale
• Mieux vaut un snap que des désync prolongés

• Cas typiques de gros écarts
• Ping > 200–250 ms
• Path bloqué par une unité ou un mur non anticipé
• Root / stun appliqué serveur-side
• Cancel de déplacement par un sort

• Ce qui n’arrive jamais
• Le serveur n’essaie pas d’adapter sa timeline au client
• Le client ne “négocie” pas la position
• Pas de correction rétroactive des combats

• Effet gameplay
• Le joueur subit la latence de façon visible
• Avantage structurel aux bas pings
• League of Legends préfère injustice perceptible à incohérence cachée

• Résumé brutal
• Position client = illusion temporaire
• Serveur tranche toujours
• Plus le ping monte, plus la vérité arrive violemment

petit écart, c'est quoi exactement, avec des chiffres si possible, meme chose pour moyen et gros écart

Il n’existe aucun chiffre officiel public. Riot ne documente pas ces seuils. Ce qui suit est une reconstruction technique crédible basée sur analyses réseau, comportements observables in-game et comparaisons avec moteurs similaires. Les ordres de grandeur sont robustes, les valeurs exactes peuvent varier selon patch.

Contexte
• Tick serveur ≈ 30 Hz → 33 ms par tick
• Vitesse typique d’un champion sans boots ≈ 325 unités/s
• Donc ≈ 10.8 unités par tick serveur

Ordres de grandeur des écarts

• Petit écart
• ≈ 5 à 20 unités monde
• ≈ 0.5 à 2 ticks serveur
• Correspond à
• jitter réseau
• interpolation imparfaite
• micro divergence de path
• Traitement
• lissage invisible sur 100–200 ms
• aucune rupture perceptible

• Écart moyen
• ≈ 20 à 80 unités
• ≈ 2 à 7 ticks serveur
• Correspond à
• ping 100–180 ms
• collision non prédite
• stun/root appliqué serveur-side
• Traitement
• correction accélérée
• glissement visible
• sensation de rubber banding

• Gros écart
• > 80–100 unités
• > 7–10 ticks serveur
• Correspond à
• ping > 200–250 ms
• packet loss
• path complètement invalide
• désync prolongée
• Traitement
• snap immédiat à la position serveur
• téléportation nette du champion

Pourquoi ces seuils sont plausibles

• En dessous de ~20 unités
• l’œil humain ne perçoit pas la correction
• cohérent avec interpolation classique

• Au-delà de ~100 unités
• lissage prendrait trop de temps
• laisserait le champion “fantôme” trop longtemps
• casserait la cohérence des combats et skillshots

Point clé architectural

• Ces seuils sont purement visuels
• Le serveur n’utilise jamais ces catégories
• Le serveur envoie simplement la vérité
• Le client décide comment “rattraper la honte”

Conclusion nette

• Oui, la position est provisoire
• Oui, elle peut être brutalement invalidée
• Plus l’écart dépasse ~3–5 ticks serveur, plus la correction devient violente
• League of Legends assume ce coût pour préserver une timeline unique et non négociable