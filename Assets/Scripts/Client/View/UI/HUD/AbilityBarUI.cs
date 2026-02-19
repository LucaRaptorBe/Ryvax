// AbilityBarUI.cs - Container for 4 ability slots (Q/W/E/R)
// Initializes slots from CharacterClass data, refreshes cooldowns each frame
// StartCooldown() called from AbilityUsed event for smooth countdown

using UnityEngine;
using MOBANet.GameSim.Data;
using MOBANet.GameSim.Entities;

namespace MOBANet.Client.UI
{
    /// <summary>
    /// Container for the 4 ability slots (Q, W, E, R).
    /// Reads ability definitions from CharacterClass and cooldowns from SimPlayer.
    /// </summary>
    public class AbilityBarUI : MonoBehaviour
    {
        [SerializeField] private AbilitySlotUI[] slots = new AbilitySlotUI[4];

        private static readonly string[] KeyLabels = { "1", "2", "3", "4" };

        private float[] _baseCooldowns = new float[4];
        private bool _initialized;

        /// <summary>
        /// Initialize all slots from character class data.
        /// </summary>
        public void Initialize(CharacterClass charClass, SimPlayer simPlayer)
        {
            _initialized = false;
            Debug.Log($"[AbilityBarUI] Initialize: charClass={charClass?.className ?? "NULL"}, simPlayer={simPlayer != null}");

            for (int i = 0; i < 4; i++)
            {
                if (i >= slots.Length || slots[i] == null)
                    continue;

                var ability = charClass != null ? charClass.GetAbilityDefinition(i) : null;
                bool isLearned = simPlayer.Abilities.IsLearned(i);
                Debug.Log($"[AbilityBarUI] Slot {i} ({KeyLabels[i]}): ability={ability?.Name ?? "NULL"}, icon={ability?.Icon != null}, isLearned={isLearned}");

                if (ability == null)
                {
                    slots[i].SetEmpty();
                    _baseCooldowns[i] = 0f;
                    continue;
                }

                _baseCooldowns[i] = ability.BaseCooldown;
                slots[i].Initialize(ability, KeyLabels[i]);

                // If ability not yet learned, show locked state
                if (!isLearned)
                {
                    slots[i].SetLocked();
                }
            }

            _initialized = true;
        }

        /// <summary>
        /// Start cooldown on a specific slot from AbilityUsed event.
        /// Uses the base cooldown from the ability definition.
        /// </summary>
        public void StartCooldown(int slot)
        {
            if (!_initialized) return;
            if (slot < 0 || slot >= slots.Length || slots[slot] == null) return;
            if (_baseCooldowns[slot] <= 0f) return;

            slots[slot].StartCooldown(_baseCooldowns[slot]);
        }

        /// <summary>
        /// Refresh cooldown displays from SimPlayer state. Call each frame.
        /// Snapshot values only correct drift — local countdown drives the display.
        /// </summary>
        public void Refresh(SimPlayer simPlayer)
        {
            if (!_initialized) return;

            for (int i = 0; i < 4; i++)
            {
                if (i >= slots.Length || slots[i] == null)
                    continue;

                // Update locked state if ability was just learned
                if (!simPlayer.Abilities.IsLearned(i))
                {
                    slots[i].SetLocked();
                    continue;
                }

                float remaining = simPlayer.Abilities.Cooldowns[i];
                float total = _baseCooldowns[i];
                slots[i].UpdateCooldown(remaining, total);
            }
        }
    }
}
