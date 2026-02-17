
# Flux complet: Appui touche -> Affichage (V3.0 Pure LoL)

══════════════════════════════════════════════════════════════════════════════
                               CLIENT - INPUT
══════════════════════════════════════════════════════════════════════════════

  [1] JOUEUR APPUIE SUR 'W'
           │
           ▼
  [2] InputCollector.Update()                    (InputCollector.cs:94)
           │
           ├─► HandleMovementInput()              (ligne 112)
           │     │
           │     ├─► _intentBuilder.SetImmobilizeLock(_networkClient.IsImmobilized())
           │     │
           │     └─► HandleWASDInput()            (ligne 143)
           │           │
           │           ├─► Keyboard.current.wKey.isPressed → dir.y += 1
           │           │
           │           │   ══════════════════════════════════════════════════
           │           │   V3.0 PURE LOL: LOCAL MOVE INTENT (PAS DE PRÉDICTION)
           │           │   ══════════════════════════════════════════════════
           │           │   Si _currentIsMoving:
           │           │     │
           │           │     └─► _networkClient.SetLocalMoveIntent(moveDir3D)
           │           │           │                            (ligne 182)
           │           │           └─► Stocke direction pour rotation/animation
           │           │               // PAS de modification de position!
           │           │               // Position = 100% serveur (interpolé)
           │           │               // Seuls rotation + animation répondent immédiatement
           │           │
           │           │   Sinon (!_currentIsMoving):
           │           │     │
           │           │     └─► _networkClient.ClearLocalMoveIntent()
           │           │
           │           └─► _intentBuilder.OnKeyboardMove(dir, dt, clientTick)
           │
           ▼
  [3] IntentBuilder.OnKeyboardMove()             (IntentBuilder.cs)
           │
           ├─► Si _isImmobilizeLocked → return Stop @2Hz
           │
           ├─► Rate limit: accumulator < 0.1s (10Hz) → return null
           │
           └─► return InputIntent.MoveDir(dir, seqId++, clientTick)
           │
           ▼
  [4] InputCollector récupère l'intent
           │
           └─► _networkClient.SendInputIntent(intent)
           │
           ▼
  [5] NetworkClient.SendInputIntent()            (NetworkClient.cs:256)
           │
           └─► _pendingIntent = intent  (stocké pour envoi)
           │
           ▼
  [6] NetworkClient.Update() → SendInputUpdate() (NetworkClient.cs:265)
           │
           ├─► Rate limit: accumulator < inputSendInterval → return
           │
           ├─► CreateInputPacketFromIntent(_pendingIntent, eventCommands)
           │     │
           │     └─► Convertit MoveDir en InputPacket:
           │           • IntentType = MoveDir
           │           • Payload = direction quantifiée (×127)
           │
           └─► _netAdapter.SendInputPacket(packet)  ─────────────────────────►


══════════════════════════════════════════════════════════════════════════════
                                  RÉSEAU
══════════════════════════════════════════════════════════════════════════════

           │
           │  UDP: InputPacket {MoveDir, direction, seqId}
           │
           ▼

══════════════════════════════════════════════════════════════════════════════
                               SERVEUR
══════════════════════════════════════════════════════════════════════════════

  [7] SERVEUR REÇOIT InputPacket
           │
           ├─► Décode la direction (Payload / 127)
           │
           ├─► MovementHandler:
           │     • Applique direction × vitesse
           │     • Collisions
           │     • Met à jour SimPlayer.Transform.Position
           │
           └─► Chaque tick serveur (30Hz):
                 │
                 └─► Crée SnapshotDelta avec tous les EntityState
                       │
                       ├─► EntityState.Position = nouvelle position
                       ├─► EntityState.Velocity = vélocité actuelle
                       ├─► EntityState.Speed = vitesse
                       ├─► EntityState.EventFlags = CC, blink, etc.
                       │
                       └─► Envoie SnapshotDelta ─────────────────────────────►


══════════════════════════════════════════════════════════════════════════════
                                  RÉSEAU
══════════════════════════════════════════════════════════════════════════════

           │
           │  UDP: SnapshotDelta {serverTick, entities[], ackSeq}
           │
           ▼

══════════════════════════════════════════════════════════════════════════════
                          CLIENT - RÉCEPTION SNAPSHOT
══════════════════════════════════════════════════════════════════════════════

  [8] NetworkClient.OnSnapshotReceived()         (NetworkClient.cs:439)
           │
           ├─► serverTime = TickToTime(snapshot.ServerTick)
           │
           ├─► Pour chaque entité dans snapshot:
           │     │
           │     └─► Si c'est le LOCAL PLAYER:
           │           │
           │           ├─► Crée SnapshotState depuis EntityState:
           │           │     • Position, Velocity, RotationY, Speed
           │           │     • HasBlinkEvent, HasTeleportEvent, HasImmobilizeCC
           │           │
           │           └─► _visualPositionManager.OnSnapshotReceived(state, serverTime)
           │
           ▼
  [9] VisualPositionManager.OnSnapshotReceived() (VisualPositionManager.cs:126)
           │
           ├─► _timeSync.OnSnapshotReceived(serverTime)
           │     │
           │     └─► Calcule jitter, ajuste adaptiveBuffer (80-160ms)
           │
           ├─► _interpolator.OnSnapshotReceived(state, serverTime)
           │     │
           │     └─► _buffer.Add(state, serverTime)  // Ajoute au ring buffer
           │
           ├─► _currentSpeed = state.Speed
           │
           ├─► _corrector.SetAdaptiveBuffer(_timeSync.AdaptiveBuffer)
           │
           ├─► _corrector.OnImmobilizeCC(state.HasImmobilizeCC)
           │
           └─► Si blink/teleport/reset:
                 └─► _corrector.SnapHard(serverTime)
                 └─► _basePrevValid = false


══════════════════════════════════════════════════════════════════════════════
                          CLIENT - UPDATE CHAQUE FRAME
══════════════════════════════════════════════════════════════════════════════

  [10] NetworkClient.Update()                    (NetworkClient.cs:224)
           │
           └─► _visualPositionManager.Update(Time.deltaTime)
           │
           ▼
  [11] VisualPositionManager.Update()            (VisualPositionManager.cs:158)
           │
           ├─► _timeSync.Update(dt)
           │     │
           │     └─► _latestServerTime += dt  // Avance le temps local
           │
           ├─► renderTime = _timeSync.RenderTime  // = serverTime - buffer (~116ms)
           │
           ├─► _basePos = _interpolator.GetBasePos(renderTime)
           │     │
           │     ▼
           │   [12] BaseInterpolator.GetBasePos()  (BaseInterpolator.cs)
           │         │
           │         ├─► FindBracketingSnapshots(renderTime, out A, out B, out alpha)
           │         │     │
           │         │     └─► Cherche 2 snapshots: A.time <= renderTime <= B.time
           │         │
           │         ├─► Si trouvé et pas de discontinuité:
           │         │     │
           │         │     ├─► Quality = Interpolated
           │         │     └─► return Lerp(A.Position, B.Position, alpha)  ◄── INTERPOLATION
           │         │
           │         └─► Sinon: extrapolation ou freeze
           │               • Quality = Extrapolated si dt < 150ms
           │               • Quality = Frozen sinon
           │
           ├─► Si HadDiscontinuity:
           │     └─► SnapHard() + reset basePrev
           │
           ├─► Sinon si _basePrevValid ET Quality == Interpolated:
           │     │
           │     └─► _corrector.AbsorbBasePosJump(_basePrev, _basePos)
           │           │
           │           ▼
           │         [13] VisualOffsetCorrector.AbsorbBasePosJump()
           │               │
           │               ├─► Si _isImmobilized → return (skip!)
           │               │
           │               └─► _visualOffset += basePrev - basePos
           │                   // Absorbe le delta pour continuité visuelle
           │
           ├─► _basePrev = _basePos
           │
           └─► _corrector.Update(dt, _currentSpeed)
                 │
                 ▼
               [14] VisualOffsetCorrector.Update()  (VisualOffsetCorrector.cs)
                     │
                     ├─► Si _isImmobilized → offset = 0, return
                     │
                     ├─► gap = offset.magnitude
                     │
                     ├─► Si gap < epsilon (0.5) → offset = 0
                     │
                     ├─► Calcule seuils dynamiques:
                     │     • smallGap = max(20, speed * 0.06)
                     │     • largeGap = clamp(max(80, speed * 0.25), 80, 250)
                     │
                     └─► Correction exponentielle:
                           • gap < smallGap → offset *= exp(-6 * dt)   // Smooth
                           • gap < largeGap → offset *= exp(-15 * dt)  // Accel
                           • else           → Snap() (offset = 0)


══════════════════════════════════════════════════════════════════════════════
                          CLIENT - RENDU VISUEL
══════════════════════════════════════════════════════════════════════════════

  [15] EntityView.Update() → PlayerView.UpdatePosition()  (PlayerView.cs:128)
           │
           ├─► Si _isLocalPlayer ET _networkClient.HasVisualPosition:
           │     │
           │     ├─► POSITION: Toujours du serveur (interpolé)
           │     │     │
           │     │     └─► transform.position = _networkClient.GetVisualPosition()
           │     │                                    │
           │     │                                    └─► return _basePos + _corrector.VisualOffset
           │     │
           │     │   ══════════════════════════════════════════════════
           │     │   V3.0 PURE LOL: ROTATION IMMÉDIATE VIA LOCAL INTENT
           │     │   ══════════════════════════════════════════════════
           │     │
           │     ├─► ROTATION: Utilise intent local pour feedback immédiat
           │     │     │
           │     │     ├─► Si _networkClient.HasLocalMoveIntent():
           │     │     │     │
           │     │     │     ├─► intent = _networkClient.GetLocalMoveIntent()
           │     │     │     ├─► targetRotY = Atan2(intent.x, intent.z) * Rad2Deg
           │     │     │     └─► smoothedRotY = LerpAngle(current, target, dt * 15f)
           │     │     │         → Rotation IMMÉDIATE vers direction input!
           │     │     │
           │     │     └─► Sinon (pas d'intent):
           │     │           └─► Utilise rotation serveur interpolée
           │     │
           │     └─► ANIMATION: Utilise intent local pour feedback immédiat
           │           │
           │           ├─► localIsMoving = _networkClient.HasLocalMoveIntent()
           │           ├─► _animator.SetBool(IsMoving, localIsMoving)
           │           └─► _animator.SetFloat(Speed, localIsMoving ? 1f : 0f)
           │               → Animation "run" démarre IMMÉDIATEMENT!
           │
           └─► Sinon (remote): base.UpdatePosition() avec InterpolationBuffer


══════════════════════════════════════════════════════════════════════════════
                         RÉSUMÉ V3.0 PURE LOL STYLE
══════════════════════════════════════════════════════════════════════════════

  Position affichée = basePos + visualOffset
                        │            │
                        │            └─► Offset d'absorption UNIQUEMENT
                        │                (absorbe sauts de basePos pour continuité)
                        │                PAS de prédiction locale!
                        │
                        └─► Interpolé entre 2 snapshots serveur
                            (retardé de ~100ms = adaptiveBuffer)


  Feedback immédiat (0ms):
  ═════════════════════════

  ┌─────────────────┐
  │   LOCAL INTENT  │◄── SetLocalMoveIntent(direction)
  └────────┬────────┘
           │
           ├─► ROTATION: Tourne vers direction (LerpAngle ×15f)
           │
           └─► ANIMATION: IsMoving=true, Speed=1.0
               → Feedback visuel INSTANTANÉ sans bouger la position!


  Position (66-100ms de latence):
  ═══════════════════════════════

  ┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
  │  Input Client   │────►│     Serveur     │────►│   Snapshots     │
  └─────────────────┘     └─────────────────┘     └────────┬────────┘
                                                           │
                                                           ▼
                                                  ┌─────────────────┐
                                                  │  Interpolation  │
                                                  │   (basePos)     │
                                                  └────────┬────────┘
                                                           │
                                                           ▼
                                                  ┌─────────────────┐
                                                  │ transform.pos   │
                                                  └─────────────────┘


  Flux V3.0 Pure LoL pour un appui touche:
  ═════════════════════════════════════════

  Frame 0: Joueur appuie 'W'
           │
           ├─► SetLocalMoveIntent(forward)
           │   → Rotation tourne IMMÉDIATEMENT vers l'avant
           │   → Animation "run" démarre IMMÉDIATEMENT
           │   → Position = inchangée (attend serveur)
           │
           └─► Intent MoveDir envoyé au serveur (@10Hz)

  Frame 1-3: Joueur maintient 'W'
           │
           ├─► Local intent maintenu → rotation/animation actives
           │
           └─► Pendant ce temps, serveur traite le mouvement

  Frame ~4 (après ~66-100ms): Premier snapshot avec nouvelle position arrive
           │
           ├─► basePos avance (via interpolation des snapshots)
           │
           ├─► AbsorbBasePosJump maintient continuité visuelle
           │
           └─► transform.position commence à bouger
               → Rotation + animation DÉJÀ actives depuis frame 0!


  Comparaison Avant/Après V3.0:
  ═════════════════════════════

  │ Aspect        │ Avant (prédiction)    │ Après (Pure LoL)        │
  ├───────────────┼───────────────────────┼─────────────────────────┤
  │ Position      │ Prédit (offset push)  │ Serveur (interpolé)     │
  │ Rotation      │ Serveur (retardée)    │ Intent local (immédiat) │
  │ Animation     │ Serveur (retardée)    │ Intent local (immédiat) │
  │ Latence pos   │ ~0ms (mais risques)   │ ~66ms (fiable)          │
  │ Divergence    │ Possible (mur/CC)     │ Impossible              │
  │ Rubber-band   │ Fréquent              │ Jamais                  │


  Deux systèmes d'interpolation:
  ═════════════════════════════════

  LOCAL PLAYER (BaseInterpolator + VisualOffsetCorrector):
  - Quality tracking (Interpolated/Extrapolated/Frozen)
  - Détection de discontinuités (blink, teleport)
  - V3.0: PAS de prédiction locale, seulement absorption
  - Feedback via rotation/animation (intent local)
  - visualPos = basePos + visualOffset (absorption only)

  REMOTE PLAYERS (InterpolationBuffer):
  - Simple Lerp entre snapshots
  - Pas de Quality tracking
  - Pas d'offset (position directe)
  - Plus léger, précision moindre acceptable


  Pourquoi ça fonctionne (comme League of Legends):
  ═══════════════════════════════════════════════════

  1. ROTATION IMMÉDIATE: Le personnage regarde dans la direction voulue
     → Le joueur VOIT que son input a été pris en compte

  2. ANIMATION IMMÉDIATE: L'animation "run" démarre tout de suite
     → Confirme visuellement l'action

  3. POSITION SERVEUR: Le personnage se déplace où le serveur dit
     → JAMAIS dans un mur, JAMAIS de rubber-band

  4. LATENCE ACCEPTABLE: ~66ms pour un MOBA c'est normal
     → Moins perceptible qu'on le pense car feedback rotation/anim

  5. COHÉRENCE: Ce que tu vois = ce qui se passe vraiment
     → Pas de "je me voyais là mais le serveur dit non"

