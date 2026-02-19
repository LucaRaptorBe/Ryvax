// AbilitySlotUI.cs - Individual ability slot in the HUD ability bar
// Displays icon, cooldown overlay (radial fill), keybind label, and cooldown text
//
// Cooldown display uses hybrid approach:
// - AbilityUsed event starts local countdown (smooth 60fps)
// - Snapshot only corrects drift (reconnection, CDR changes)

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MOBANet.GameSim.Data;

namespace MOBANet.Client.UI
{
    /// <summary>
    /// UI component for a single ability slot.
    /// Shows icon, radial cooldown overlay, key label, and cooldown timer text.
    ///
    /// Cooldown architecture (hybrid event + snapshot):
    /// 1. AbilityUsed event → StartCooldown(total) → smooth local countdown at 60fps
    /// 2. Snapshot → UpdateCooldown(remaining) → only corrects if drift > threshold
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
        private static Sprite _whiteSprite;

        // Local interpolation: tick down at 60fps
        private float _displayRemaining;
        private float _totalCooldown;
        private bool _countingDown; // true when local countdown is active

        /// <summary>
        /// Max allowed drift between local countdown and server snapshot before correction.
        /// Byte quantization step is ~0.235s, so threshold must be above that.
        /// </summary>
        private const float DRIFT_THRESHOLD = 0.5f;

        private void Awake()
        {
            // Radial360 fill requires a sprite to render the sweep correctly.
            // Without a sprite, Unity renders a solid rectangle ignoring fillAmount.
            EnsureOverlaySprite();
        }

        private void EnsureOverlaySprite()
        {
            if (cooldownOverlay == null || cooldownOverlay.sprite != null) return;

            if (_whiteSprite == null)
            {
                var tex = new Texture2D(4, 4);
                var pixels = new Color[16];
                for (int i = 0; i < 16; i++) pixels[i] = Color.white;
                tex.SetPixels(pixels);
                tex.Apply();
                _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
            }
            cooldownOverlay.sprite = _whiteSprite;
        }

        private void Update()
        {
            if (_isEmpty || _isLocked || !_countingDown) return;

            // Locally tick down at frame rate for smooth sweep
            _displayRemaining -= Time.deltaTime;
            if (_displayRemaining <= 0f)
            {
                _displayRemaining = 0f;
                _countingDown = false;
            }

            float fill = _totalCooldown > 0f ? Mathf.Clamp01(_displayRemaining / _totalCooldown) : 0f;
            string text = _displayRemaining > 0f ? Mathf.CeilToInt(_displayRemaining).ToString() : "";
            SetCooldownVisuals(fill, text);
        }

        /// <summary>
        /// Initialize this slot with an ability definition and key label.
        /// </summary>
        public void Initialize(IAbilityDefinition ability, string keyLabel)
        {
            _isEmpty = false;
            _isLocked = false;
            _displayRemaining = 0f;
            _totalCooldown = 0f;
            _countingDown = false;

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
        /// Start a fresh cooldown countdown from an AbilityUsed event.
        /// This is the primary source — drives smooth 60fps local countdown.
        /// </summary>
        public void StartCooldown(float total)
        {
            if (_isEmpty || _isLocked) return;

            _totalCooldown = total;
            _displayRemaining = total;
            _countingDown = true;
        }

        /// <summary>
        /// Correct cooldown from server snapshot.
        /// Only snaps when drift exceeds threshold or when cooldown state changes
        /// unexpectedly (reset, CDR, reconnection).
        /// </summary>
        public void UpdateCooldown(float remaining, float total)
        {
            if (_isEmpty || _isLocked) return;

            // Server says cooldown ended — clear immediately
            if (remaining <= 0f)
            {
                if (_countingDown)
                {
                    _displayRemaining = 0f;
                    _countingDown = false;
                    SetCooldownVisuals(0f, "");
                }
                return;
            }

            // Server says cooldown started but we're not counting — fallback start
            // (reconnection, missed event, etc.)
            if (!_countingDown)
            {
                _totalCooldown = total;
                _displayRemaining = remaining;
                _countingDown = true;
                return;
            }

            // Already counting down — only correct if drift exceeds threshold
            float drift = Mathf.Abs(_displayRemaining - remaining);
            if (drift > DRIFT_THRESHOLD)
            {
                _displayRemaining = remaining;
                _totalCooldown = total;
            }
        }

        /// <summary>
        /// Mark this slot as empty (no ability assigned).
        /// </summary>
        public void SetEmpty()
        {
            _isEmpty = true;
            _isLocked = false;
            _displayRemaining = 0f;
            _countingDown = false;

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
            _displayRemaining = 0f;
            _countingDown = false;

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
