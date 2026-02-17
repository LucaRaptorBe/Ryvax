// ClientClockSync.cs - Client tick rate adjustment for optimal input buffer
// IMPORTANT: Ce code n'est PAS ACTUELLEMENT UTILISÉ dans l'architecture LoL-style.
// Il est prévu pour une future implémentation FPS-style avec prédiction client.
//
// Architecture actuelle (LoL-style):
// - Le client n'a PAS de SimWorld local (NetworkClient.cs:226-230 FixedUpdate est vide)
// - Pas de ticks client, seulement du rendu visuel (Update() à 60Hz+)
// - Pas besoin de clock sync car pas de simulation locale
//
// Architecture FPS-style (si implémentée):
// - Le client AURAIT un SimWorld local qui tournerait à 30Hz
// - ClientClockSync ajusterait le tick rate de ce SimWorld
// - Pour maintenir ~2 inputs "in flight" (buffer optimal)
//
// Référence: Overwatch GDC 2017, Valorant netcode talks.

using UnityEngine;
using MOBANet.Shared;

namespace MOBANet.UnityView.Core
{
    /// <summary>
    /// [NON UTILISÉ - Prévu pour architecture FPS-style]
    ///
    /// Ajuste le tick rate client pour maintenir un buffer d'inputs optimal.
    /// Basé sur le nombre d'inputs "in flight" (envoyés mais pas encore ackés).
    ///
    /// Concept (si utilisé):
    /// - Si trop d'inputs pending → client tourne trop vite → ralentir tick rate
    /// - Si pas assez d'inputs → client tourne trop lent → accélérer tick rate
    ///
    /// Quelle horloge sync avec quelle horloge?
    /// - Synchronise: Le tick clock du SimWorld CLIENT (qui n'existe pas actuellement)
    /// - Avec: Le serveur (indirectement via nombre d'inputs en vol)
    /// - But: Maintenir le client ~2 ticks derrière le serveur
    ///
    /// Pourquoi pas utilisé?
    /// - Architecture LoL-style = pas de simulation client
    /// - Voir NetworkClient.cs:226-230 - FixedUpdate() est vide
    /// - Le client fait seulement du rendu (Update()) et de l'interpolation visuelle
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
