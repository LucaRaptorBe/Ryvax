using System;
using UnityEngine;

namespace MOBANet.Client.Settings
{
    /// <summary>
    /// Serializable game settings. Persisted as JSON.
    /// Contains keybinds, cast mode, and movement mode.
    /// </summary>
    [Serializable]
    public class GameSettings
    {
        // --- Movement ---
        public MovementMode movementMode = MovementMode.WASD;
        public KeyboardLayout keyboardLayout = KeyboardLayout.QWERTY;

        // --- Ability keybinds (KeyCode for easy JSON serialization) ---
        public KeyCode ability1Key = KeyCode.Q;
        public KeyCode ability2Key = KeyCode.W;
        public KeyCode ability3Key = KeyCode.E;
        public KeyCode ability4Key = KeyCode.R;

        // --- Cast mode ---
        public CastMode defaultCastMode = CastMode.QuickCast;

        /// <summary>
        /// Create a new GameSettings with default values.
        /// </summary>
        public static GameSettings CreateDefault() => new GameSettings();

        /// <summary>
        /// Get the correct ability keybinds based on current movement mode + layout.
        /// Call this after changing movementMode or keyboardLayout to update ability keys.
        /// </summary>
        public void ApplyDefaultKeybinds()
        {
            bool azerty = keyboardLayout == KeyboardLayout.AZERTY;

            if (movementMode == MovementMode.ClickToMove)
            {
                // ClickToMove: no WASD conflict, all letter keys available
                // QWERTY: Q W E R  |  AZERTY: A Z E R
                ability1Key = azerty ? KeyCode.A : KeyCode.Q;
                ability2Key = azerty ? KeyCode.Z : KeyCode.W;
                ability3Key = KeyCode.E;
                ability4Key = KeyCode.R;
            }
            else
            {
                // WASD mode: W/A/S/D (or Z/Q/S/D) are movement
                // QWERTY: Q Shift E R  |  AZERTY: A Shift E R
                ability1Key = azerty ? KeyCode.A : KeyCode.Q;
                ability2Key = KeyCode.LeftShift;
                ability3Key = KeyCode.E;
                ability4Key = KeyCode.R;
            }
        }

        /// <summary>
        /// Deep clone this settings object.
        /// </summary>
        public GameSettings Clone()
        {
            var copy = new GameSettings();
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>
        /// Copy all values from another GameSettings instance.
        /// </summary>
        public void CopyFrom(GameSettings other)
        {
            if (other == null) return;

            movementMode = other.movementMode;
            keyboardLayout = other.keyboardLayout;
            ability1Key = other.ability1Key;
            ability2Key = other.ability2Key;
            ability3Key = other.ability3Key;
            ability4Key = other.ability4Key;
            defaultCastMode = other.defaultCastMode;
        }

        /// <summary>
        /// Get ability key by slot index (0-3).
        /// </summary>
        public KeyCode GetAbilityKey(int slot)
        {
            return slot switch
            {
                0 => ability1Key,
                1 => ability2Key,
                2 => ability3Key,
                3 => ability4Key,
                _ => KeyCode.None
            };
        }

        /// <summary>
        /// Set ability key by slot index (0-3).
        /// </summary>
        public void SetAbilityKey(int slot, KeyCode key)
        {
            switch (slot)
            {
                case 0: ability1Key = key; break;
                case 1: ability2Key = key; break;
                case 2: ability3Key = key; break;
                case 3: ability4Key = key; break;
            }
        }
    }

    /// <summary>
    /// Movement mode: WASD keyboard or click-to-move.
    /// Replaces the enum previously in InputCollector.
    /// </summary>
    [Serializable]
    public enum MovementMode
    {
        WASD,
        ClickToMove
    }

    [Serializable]
    public enum KeyboardLayout
    {
        QWERTY,
        AZERTY
    }
}
