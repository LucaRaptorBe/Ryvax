// AbilitySlotUI.cs - Individual ability slot in the HUD ability bar
// Displays icon, cooldown overlay (radial fill), keybind label, and cooldown text

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MOBANet.GameSim.Data;

namespace MOBANet.Client.UI
{
    /// <summary>
    /// UI component for a single ability slot.
    /// Shows icon, radial cooldown overlay, key label, and cooldown timer text.
    /// </summary>
    public class AbilitySlotUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image cooldownOverlay;
        [SerializeField] private TextMeshProUGUI keyText;
        [SerializeField] private TextMeshProUGUI cooldownText;

        [Header("Colors")]
        [SerializeField] private Color lockedTint = new Color(0.3f, 0.3f, 0.3f, 1f);
        [SerializeField] private Color normalTint = Color.white;

        private bool _isEmpty;
        private bool _isLocked;

        /// <summary>
        /// Initialize this slot with an ability definition and key label.
        /// </summary>
        public void Initialize(IAbilityDefinition ability, string keyLabel)
        {
            _isEmpty = false;
            _isLocked = false;

            if (keyText != null)
                keyText.text = keyLabel;

            if (ability != null && ability.Icon != null)
            {
                iconImage.sprite = ability.Icon;
                iconImage.color = normalTint;
                iconImage.enabled = true;
            }
            else
            {
                // Ability exists but has no icon
                iconImage.color = normalTint;
                iconImage.enabled = ability != null;
            }

            // Start with no cooldown
            SetCooldownVisuals(0f, "");
        }

        /// <summary>
        /// Update cooldown display each frame.
        /// </summary>
        /// <param name="remaining">Seconds remaining on cooldown</param>
        /// <param name="total">Total cooldown duration (BaseCooldown)</param>
        public void UpdateCooldown(float remaining, float total)
        {
            if (_isEmpty || _isLocked) return;

            if (remaining <= 0f || total <= 0f)
            {
                SetCooldownVisuals(0f, "");
                return;
            }

            float fill = Mathf.Clamp01(remaining / total);
            string text = Mathf.CeilToInt(remaining).ToString();
            SetCooldownVisuals(fill, text);
        }

        /// <summary>
        /// Mark this slot as empty (no ability assigned).
        /// </summary>
        public void SetEmpty()
        {
            _isEmpty = true;
            _isLocked = false;

            if (iconImage != null)
                iconImage.enabled = false;

            SetCooldownVisuals(0f, "");
        }

        /// <summary>
        /// Mark this slot as locked (ability not yet learned).
        /// </summary>
        public void SetLocked()
        {
            _isLocked = true;
            _isEmpty = false;

            if (iconImage != null)
                iconImage.color = lockedTint;

            SetCooldownVisuals(1f, "");
        }

        private void SetCooldownVisuals(float fillAmount, string text)
        {
            if (cooldownOverlay != null)
            {
                cooldownOverlay.fillAmount = fillAmount;
                cooldownOverlay.enabled = fillAmount > 0f;
            }

            if (cooldownText != null)
            {
                cooldownText.text = text;
                cooldownText.enabled = !string.IsNullOrEmpty(text);
            }
        }
    }
}
