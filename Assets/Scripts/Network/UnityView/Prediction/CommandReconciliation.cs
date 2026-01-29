// CommandReconciliation.cs - Command-based prediction and reconciliation
// Pattern: Command replay with tick simulation (industry standard for MOBAs)
//
// Key insight: Command-based doesn't eliminate replay, it changes WHAT we replay.
// - Input-based: replay 30 inputs/sec
// - Command-based: replay sparse commands + simulate ticks between them
//
// On snapshot:
// 1. Revert to server state
// 2. Re-apply unacked commands at their issue ticks
// 3. Re-simulate ticks from server tick to predicted tick

using System.Collections.Generic;
using UnityEngine;
using MOBANet.Core;
using MOBANet.GameSim.Core;
using MOBANet.GameSim.Entities;
using MOBANet.GameSim.Commands;
using MOBANet.NetAdapter.Messages;
using MOBANet.NetAdapter.Metrics;
using MOBANet.Shared;

// Alias to avoid ambiguity with GameSim.Entities.EntityState
using NetEntityState = MOBANet.NetAdapter.Messages.EntityState;

namespace MOBANet.UnityView.Prediction
{
    /// <summary>
    /// Pending command awaiting server acknowledgment.
    /// </summary>
    public struct PendingCommand
    {
        public uint Sequence;
        public uint IssueTick;
        public SimCommand Command;
    }

    /// <summary>
    /// Handles command-based prediction and reconciliation for the local player.
    ///
    /// Unlike pure server-authoritative (which causes input lag), this system:
    /// 1. Applies commands locally for instant feedback
    /// 2. Records commands with their issue tick
    /// 3. On server snapshot: replays unacked commands and re-simulates ticks
    ///
    /// This maintains smooth prediction while staying synchronized with the server.
    /// </summary>
    public class CommandReconciliation
    {
        #region Fields

        private readonly SimWorld _predictionWorld;
        private readonly SimConfig _config;
        private readonly int _localClientId;

        // Pending commands buffer (commands sent but not yet acked by server)
        private readonly List<PendingCommand> _pendingCommands = new();
        private const int MAX_PENDING_COMMANDS = 32;

        // Server state tracking
        private uint _lastAckedSeq;
        private uint _lastServerTick;
        private Vector3 _lastServerPosition;

        // Metrics
        private float _lastError;
        private int _reconcileCount;
        private int _replayedCommandCount;
        private int _replayedTickCount;

        // Soft correction state
        private Vector3 _softCorrectionTarget = Vector3.zero;
        private bool _isSoftCorrecting = false;
        private Vector3 _visualCorrectionOffset = Vector3.zero;

        #endregion

        #region Configuration

        /// <summary>
        /// Position error threshold below which we skip reconciliation.
        /// Small errors from float precision don't need correction.
        /// </summary>
        public float ReconcileThreshold { get; set; } = NetcodeConstants.SOFT_RECONCILE_THRESHOLD;

        /// <summary>
        /// Maximum ticks to replay in a single reconciliation.
        /// Prevents spiral of death under bad network conditions.
        /// </summary>
        public int MaxReplayTicks { get; set; } = 30; // ~1 second at 30Hz

        #endregion

        #region Properties

        public uint LastAckedSequence => _lastAckedSeq;
        public uint LastServerTick => _lastServerTick;
        public Vector3 LastServerPosition => _lastServerPosition;
        public float LastError => _lastError;
        public int ReconcileCount => _reconcileCount;
        public int PendingCommandCount => _pendingCommands.Count;

        // Expose for debug UI (compatibility with old names)
        public int HardCorrectionCount => _reconcileCount;
        public int SoftCorrectionCount => 0; // Not used in this implementation

        /// <summary>
        /// Offset visuel de correction (appliqué dans la couche view, pas dans la sim).
        /// Permet une convergence progressive vers la position serveur sans affecter la prédiction.
        /// </summary>
        public Vector3 VisualCorrectionOffset => _visualCorrectionOffset;

        #endregion

        #region Constructor

        public CommandReconciliation(SimWorld predictionWorld, int localClientId)
        {
            _predictionWorld = predictionWorld;
            _config = predictionWorld.Config;
            _localClientId = localClientId;
        }

        #endregion

        #region Command Recording

        /// <summary>
        /// Record a command that was executed locally.
        /// Call this AFTER applying the command to the prediction world.
        /// </summary>
        public void RecordCommand(in SimCommand cmd, uint issueTick)
        {
            _pendingCommands.Add(new PendingCommand
            {
                Sequence = cmd.Sequence,
                IssueTick = issueTick,
                Command = cmd
            });

            // Prevent unbounded growth
            while (_pendingCommands.Count > MAX_PENDING_COMMANDS)
            {
                _pendingCommands.RemoveAt(0);
            }
        }

        #endregion

        #region Snapshot Processing

        /// <summary>
        /// Process incoming snapshot - reconcile if needed.
        /// This is the core of command-based prediction.
        /// </summary>
        public void OnSnapshotReceived(in SnapshotDelta snapshot)
        {
            // DEBUG: Log snapshot reçu
            Debug.Log($"[CommandReconciliation] Snapshot received: ServerTick={snapshot.ServerTick}, AckInputSeq={snapshot.AckInputSeq}, ClientTick={_predictionWorld.Clock.CurrentTick}, PendingCmds={_pendingCommands.Count}");

            // Track RTT
            PingTracker.RecordPongReceived(snapshot.AckInputSeq);

            _lastAckedSeq = snapshot.AckInputSeq;
            _lastServerTick = snapshot.ServerTick;

            // Find local player state in snapshot
            for (int i = 0; i < snapshot.EntityCount; i++)
            {
                ref readonly var state = ref snapshot.Entities[i];

                var entity = _predictionWorld.GetEntity(state.EntityId);
                if (entity is SimPlayer player && player.OwnerClientId == _localClientId)
                {
                    ProcessLocalPlayerState(player, state, snapshot.ServerTick);
                    break;
                }
            }

            // Discard acknowledged commands
            int discardedCount = _pendingCommands.RemoveAll(cmd => cmd.Sequence <= _lastAckedSeq);
            Debug.Log($"[CommandReconciliation] Discarded {discardedCount} acked commands (AckSeq={_lastAckedSeq}), Remaining={_pendingCommands.Count}");
        }

        private void ProcessLocalPlayerState(SimPlayer player, in NetEntityState serverState, uint serverTick)
        {
            _lastServerPosition = serverState.Position;

            // Calculate prediction error
            Vector3 diff = player.Transform.Position - serverState.Position;
            float error = diff.magnitude;
            _lastError = error;

            // Throttled logging for reconciliation debugging
            DebugLogger.LogThrottled(DebugLogger.Category.Reconciliation,
                $"Error={error:F3}m at ServerTick={serverTick}",
                frameInterval: 60);

            // ⚠️ IMPORTANT: Si au sol, ignorer différences uniquement sur Y (gravité)
            // Évite soft correction inutile causée par variations de gravité client/serveur
            if (player.Transform.IsGrounded)
            {
                Vector3 horizontalDiff = new Vector3(diff.x, 0, diff.z);
                float horizontalError = horizontalDiff.magnitude;

                // Si erreur principalement verticale (joueur au sol) → ignorer
                if (horizontalError < NetcodeConstants.SOFT_RECONCILE_THRESHOLD && Mathf.Abs(diff.y) > 0.1f)
                {
                    DebugLogger.LogThrottled(DebugLogger.Category.Reconciliation,
                        $"Ignoring Y-only error (grounded): totalError={error:F3}m, horizontalError={horizontalError:F3}m, Y={diff.y:F3}m",
                        frameInterval: 120);

                    _isSoftCorrecting = false;
                    _visualCorrectionOffset = Vector3.zero;
                    return;
                }

                // Utiliser erreur horizontale si au sol
                error = horizontalError;
            }

            // DEBUG: Log toutes les erreurs > 0.1m pour diagnostiquer
            if (error > 0.1f)
            {
                DebugLogger.LogThrottled(DebugLogger.Category.Reconciliation,
                    $"Prediction error={error:F3}m | Player={player.Transform.Position:F2} | Server={serverState.Position:F2} | Diff={diff:F3}",
                    frameInterval: 60);
            }

            // Branch 1: Erreur négligeable → RAS
            if (error <= NetcodeConstants.SOFT_RECONCILE_THRESHOLD)
            {
                _isSoftCorrecting = false;
                _visualCorrectionOffset = Vector3.zero;
                return;
            }

            // Branch 2: Erreur modérée → Soft correction (nouveau!)
            if (error <= NetcodeConstants.HARD_RECONCILE_THRESHOLD)
            {
                // Démarrer soft correction si pas déjà en cours
                if (!_isSoftCorrecting)
                {
                    _isSoftCorrecting = true;
                    DebugLogger.Log(DebugLogger.Category.Reconciliation,
                        $"SOFT correction started: error={error:F3}m, playerPos={player.Transform.Position:F2}, serverPos={serverState.Position:F2}");
                }

                // ⚠️ IMPORTANT: Mettre à jour la cible à chaque snapshot!
                // Sinon on corrige vers une ancienne position
                _softCorrectionTarget = serverState.Position;

                // UpdateCorrection() gérera la convergence progressive
                return;
            }

            // Branch 3: Erreur importante → Hard reconciliation (existant)
            _isSoftCorrecting = false;
            _visualCorrectionOffset = Vector3.zero;
            Reconcile(player, serverState, serverTick);
        }

        /// <summary>
        /// Core reconciliation: revert to server state, replay commands, re-simulate ticks.
        /// </summary>
        private void Reconcile(SimPlayer player, in NetEntityState serverState, uint serverTick)
        {
            uint predictedTick = _predictionWorld.Clock.CurrentTick;

            // Sanity check: don't replay if server is ahead of us (shouldn't happen)
            if (serverTick >= predictedTick)
            {
                // Just apply server state directly
                ApplyServerState(player, serverState);
                _predictionWorld.Clock.SetTick(serverTick);
                return;
            }

            // Calculate ticks to replay
            int ticksToReplay = (int)(predictedTick - serverTick);

            // Cap replay to prevent spiral of death
            if (ticksToReplay > MaxReplayTicks)
            {
                Debug.LogWarning($"[CommandReconciliation] Capping replay from {ticksToReplay} to {MaxReplayTicks} ticks");
                ticksToReplay = MaxReplayTicks;
                // Adjust server tick to match capped replay
                serverTick = predictedTick - (uint)ticksToReplay;
            }

            // 1. Apply server authoritative state (position, health, etc.)
            // Note: This does NOT include movement state (IsMoving, MoveDirection)
            ApplyServerState(player, serverState);
            _predictionWorld.Clock.SetTick(serverTick);

            // 2. Get commands to replay (sequence > last acked, ordered by tick)
            var commandsToReplay = GetCommandsToReplay(serverTick);
            _replayedCommandCount = commandsToReplay.Count;
            _replayedTickCount = ticksToReplay;

            // 3. Apply "past" commands (IssueTick <= serverTick) to restore movement state
            // These commands already affected the server position, but we need their STATE effects
            // Example: MoveStart at tick 95 means player should be moving at serverTick 100
            int commandIndex = 0;
            while (commandIndex < commandsToReplay.Count &&
                   commandsToReplay[commandIndex].IssueTick <= serverTick)
            {
                var cmd = commandsToReplay[commandIndex].Command;
                _predictionWorld.CommandDispatcher.Execute(player, cmd);
                commandIndex++;
            }

            // 4. Replay ticks from serverTick to predictedTick
            for (int t = 0; t < ticksToReplay; t++)
            {
                uint currentTick = _predictionWorld.Clock.CurrentTick;

                // Apply commands issued at this tick
                while (commandIndex < commandsToReplay.Count &&
                       commandsToReplay[commandIndex].IssueTick == currentTick)
                {
                    var cmd = commandsToReplay[commandIndex].Command;
                    _predictionWorld.CommandDispatcher.Execute(player, cmd);
                    commandIndex++;
                }

                // Step simulation for this tick
                // PreTick removed - no longer needed;
                player.Tick(_predictionWorld.Clock.TickDelta, _config);
                _predictionWorld.Clock.Advance();
            }

            // 5. Apply any remaining commands (edge case: issued after predicted tick)
            while (commandIndex < commandsToReplay.Count)
            {
                var cmd = commandsToReplay[commandIndex].Command;
                _predictionWorld.CommandDispatcher.Execute(player, cmd);
                commandIndex++;
            }

            _reconcileCount++;

            Debug.Log($"[CommandReconciliation] Reconciled: error={_lastError:F3}, replayed {_replayedCommandCount} cmds over {_replayedTickCount} ticks");
        }

        /// <summary>
        /// Apply server authoritative state to player.
        /// Velocity is replicated from snapshot - movement state is derived from it.
        /// </summary>
        private void ApplyServerState(SimPlayer player, in NetEntityState state)
        {
            player.Transform.Position = state.Position;
            player.Transform.RotationY = state.Rotation;
            player.Stats.Health = state.Health;
            // IsAlive is computed from Health > 0, no need to assign

            // Restore velocity from snapshot (server-authoritative)
            // Movement state (IsMoving) is derived from velocity magnitude in SimPlayer
            player.Transform.Velocity = state.Velocity;
        }

        /// <summary>
        /// Get commands to replay, sorted by issue tick.
        /// Only returns commands with sequence > lastAckedSeq.
        /// </summary>
        private List<PendingCommand> GetCommandsToReplay(uint fromTick)
        {
            var result = new List<PendingCommand>();

            foreach (var cmd in _pendingCommands)
            {
                // Only replay unacked commands
                if (cmd.Sequence > _lastAckedSeq)
                {
                    result.Add(cmd);
                }
            }

            // Sort by issue tick (should already be mostly sorted, but ensure it)
            result.Sort((a, b) => a.IssueTick.CompareTo(b.IssueTick));

            // DEBUG: Log commandes à rejouer
            Debug.Log($"[CommandReconciliation] GetCommandsToReplay: PendingTotal={_pendingCommands.Count}, LastAckedSeq={_lastAckedSeq}, ToReplay={result.Count}");
            if (_pendingCommands.Count > 0 && result.Count == 0)
            {
                Debug.LogWarning($"[CommandReconciliation] ⚠️ Buffer has {_pendingCommands.Count} cmds but 0 to replay! First cmd seq={_pendingCommands[0].Sequence}, LastAckedSeq={_lastAckedSeq}");
            }

            return result;
        }

        /// <summary>
        /// Remove commands that have been acknowledged by the server.
        /// </summary>
        private void DiscardAckedCommands()
        {
            _pendingCommands.RemoveAll(cmd => cmd.Sequence <= _lastAckedSeq);
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get the local player from prediction world.
        /// </summary>
        public SimPlayer GetLocalPlayer()
        {
            return _predictionWorld.GetPlayerByClient(_localClientId);
        }

        /// <summary>
        /// Reset reconciliation state.
        /// </summary>
        public void Reset()
        {
            _pendingCommands.Clear();
            _lastAckedSeq = 0;
            _lastServerTick = 0;
            _lastServerPosition = Vector3.zero;
            _lastError = 0f;
            _reconcileCount = 0;
        }

        /// <summary>
        /// Debug info string.
        /// </summary>
        public string GetDebugInfo()
        {
            return $"CmdRecon: Pending={_pendingCommands.Count} Error={_lastError:F3} Reconciles={_reconcileCount}";
        }

        #endregion

        #region Continuous Correction (Optional)

        /// <summary>
        /// Applique une correction progressive (soft correction) vers la position serveur.
        /// L'offset est stocké séparément et appliqué dans la couche visuelle (NetPlayerController).
        /// Cela évite de modifier directement player.Transform.Position (qui casserait la prédiction).
        /// </summary>
        public void UpdateCorrection(float deltaTime)
        {
            if (!_isSoftCorrecting) return;

            var player = GetLocalPlayer();
            if (player == null)
            {
                _isSoftCorrecting = false;
                return;
            }

            // Calculer l'offset entre position sim et cible serveur
            Vector3 targetOffset = _softCorrectionTarget - player.Transform.Position;

            // Appliquer correction exponentielle sur l'OFFSET (framerate-independent)
            _visualCorrectionOffset = Vector3.Lerp(
                _visualCorrectionOffset,
                targetOffset,
                1f - Mathf.Exp(-NetcodeConstants.POSITION_SMOOTHING_K * deltaTime)
            );

            // DEBUG: Log l'évolution de la correction
            DebugLogger.LogThrottled(DebugLogger.Category.Reconciliation,
                $"SOFT correction: offset={_visualCorrectionOffset:F4}, targetOffset={targetOffset:F4}, playerPos={player.Transform.Position:F2}, target={_softCorrectionTarget:F2}",
                frameInterval: 60);

            // Vérifier si la correction est terminée (convergence)
            if (_visualCorrectionOffset.magnitude < 0.01f) // 1cm
            {
                _isSoftCorrecting = false;
                _visualCorrectionOffset = Vector3.zero;
                DebugLogger.Log(DebugLogger.Category.Reconciliation,
                    "SOFT correction completed");
            }
        }

        #endregion
    }
}
