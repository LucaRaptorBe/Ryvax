// ClientClockSync.cs - Client tick rate adjustment for optimal input buffer
// Ajuste le tick rate client pour maintenir ~2 inputs "in flight"
// OPTIONNEL: Peut être désactivé facilement si problématique

using UnityEngine;
using MOBANet.Shared;

namespace MOBANet.UnityView.Core
{
    /// <summary>
    /// Ajuste le tick rate client pour maintenir un buffer d'inputs optimal.
    /// Basé sur le nombre d'inputs "in flight" (envoyés mais pas encore ackés).
    ///
    /// Concept:
    /// - Si trop d'inputs pending → client tourne trop vite → ralentir tick rate
    /// - Si pas assez d'inputs → client tourne trop lent → accélérer tick rate
    ///
    /// Référence: Overwatch GDC 2017, Valorant netcode talks.
    /// </summary>
    public class ClientClockSync
    {
        private float _tickRateMultiplier = 1f;

        /// <summary>
        /// Tick rate ajusté (peut être légèrement différent du TICK_RATE nominal).
        /// </summary>
        public float AdjustedTickRate => NetcodeConstants.TICK_RATE * _tickRateMultiplier;

        /// <summary>
        /// Multiplicateur de tick rate (1.0 = nominal, 0.8-1.2 = plage d'ajustement).
        /// </summary>
        public float TickRateMultiplier => _tickRateMultiplier;

        /// <summary>
        /// Ajuste le tick rate basé sur le nombre d'inputs pending.
        /// Appelé chaque tick AVANT de step la simulation.
        /// </summary>
        /// <param name="pendingCommandCount">Nombre de commandes en attente d'acknowledgment</param>
        public void UpdateClockSync(int pendingCommandCount)
        {
            int target = NetcodeConstants.TARGET_INPUTS_IN_FLIGHT;
            int tolerance = NetcodeConstants.INPUT_BUFFER_TOLERANCE;

            // Si hors de la tolérance, ajuster
            int deviation = pendingCommandCount - target;
            if (Mathf.Abs(deviation) > tolerance)
            {
                // Trop d'inputs pending → ralentir (client va trop vite)
                // Pas assez d'inputs → accélérer (client va trop lent)
                // Négatif car: si deviation > 0 (trop d'inputs), on veut ralentir (multiplier < 1)
                float adjustment = -deviation * NetcodeConstants.CLOCK_SYNC_AGGRESSION;
                adjustment = Mathf.Clamp(adjustment,
                    -NetcodeConstants.MAX_TICK_RATE_ADJUSTMENT,
                    NetcodeConstants.MAX_TICK_RATE_ADJUSTMENT);

                _tickRateMultiplier = Mathf.Clamp(_tickRateMultiplier + adjustment, 0.8f, 1.2f);
            }
            else
            {
                // Dans la tolérance → converger doucement vers 1.0
                _tickRateMultiplier = Mathf.Lerp(_tickRateMultiplier, 1f, 0.01f);
            }
        }

        /// <summary>
        /// Obtient le multiplicateur de tick rate pour ajuster l'accumulateur.
        /// Utilisé dans ClientGameLoop.Update(): _tickAccumulator += Time.deltaTime * GetTickRateMultiplier()
        /// </summary>
        public float GetTickRateMultiplier()
        {
            return _tickRateMultiplier;
        }

        /// <summary>
        /// Reset le clock sync (retour à tick rate nominal).
        /// </summary>
        public void Reset()
        {
            _tickRateMultiplier = 1f;
        }

        /// <summary>
        /// Informations de debug.
        /// </summary>
        public string GetDebugInfo()
        {
            return $"ClockSync: Rate={AdjustedTickRate:F1}Hz, Mult={_tickRateMultiplier:F3}x";
        }
    }
}
