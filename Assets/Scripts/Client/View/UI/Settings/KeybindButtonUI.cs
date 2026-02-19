using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace MOBANet.Client.Settings
{
    /// <summary>
    /// A button that displays the current keybind and listens for a new key press when clicked.
    /// Shows "Press a key..." in yellow while listening.
    /// </summary>
    public class KeybindButtonUI : MonoBehaviour
    {
        public event Action<KeyCode> OnBindingChanged;

        private Button _button;
        private TextMeshProUGUI _label;
        private Image _background;

        private KeyCode _currentKey;
        private bool _isListening;

        private static readonly Color NormalBgColor = new Color(0.2f, 0.2f, 0.3f, 0.9f);
        private static readonly Color ListeningBgColor = new Color(0.4f, 0.35f, 0.1f, 0.9f);
        private static readonly Color NormalTextColor = Color.white;
        private static readonly Color ListeningTextColor = new Color(1f, 0.9f, 0.3f, 1f);

        /// <summary>Current bound KeyCode.</summary>
        public KeyCode CurrentKey => _currentKey;

        /// <summary>
        /// New Input System Key → legacy KeyCode mapping for rebinding.
        /// Covers the most common keys used in MOBA keybinds.
        /// </summary>
        private static readonly Dictionary<Key, KeyCode> KeyToKeyCodeMap = new()
        {
            { Key.A, KeyCode.A }, { Key.B, KeyCode.B }, { Key.C, KeyCode.C }, { Key.D, KeyCode.D },
            { Key.E, KeyCode.E }, { Key.F, KeyCode.F }, { Key.G, KeyCode.G }, { Key.H, KeyCode.H },
            { Key.I, KeyCode.I }, { Key.J, KeyCode.J }, { Key.K, KeyCode.K }, { Key.L, KeyCode.L },
            { Key.M, KeyCode.M }, { Key.N, KeyCode.N }, { Key.O, KeyCode.O }, { Key.P, KeyCode.P },
            { Key.Q, KeyCode.Q }, { Key.R, KeyCode.R }, { Key.S, KeyCode.S }, { Key.T, KeyCode.T },
            { Key.U, KeyCode.U }, { Key.V, KeyCode.V }, { Key.W, KeyCode.W }, { Key.X, KeyCode.X },
            { Key.Y, KeyCode.Y }, { Key.Z, KeyCode.Z },
            { Key.Digit0, KeyCode.Alpha0 }, { Key.Digit1, KeyCode.Alpha1 },
            { Key.Digit2, KeyCode.Alpha2 }, { Key.Digit3, KeyCode.Alpha3 },
            { Key.Digit4, KeyCode.Alpha4 }, { Key.Digit5, KeyCode.Alpha5 },
            { Key.Digit6, KeyCode.Alpha6 }, { Key.Digit7, KeyCode.Alpha7 },
            { Key.Digit8, KeyCode.Alpha8 }, { Key.Digit9, KeyCode.Alpha9 },
            { Key.Space, KeyCode.Space }, { Key.Tab, KeyCode.Tab },
            { Key.LeftShift, KeyCode.LeftShift }, { Key.RightShift, KeyCode.RightShift },
            { Key.LeftCtrl, KeyCode.LeftControl }, { Key.RightCtrl, KeyCode.RightControl },
            { Key.LeftAlt, KeyCode.LeftAlt }, { Key.RightAlt, KeyCode.RightAlt },
            { Key.Backquote, KeyCode.BackQuote }, { Key.Minus, KeyCode.Minus },
            { Key.Equals, KeyCode.Equals },
            { Key.F1, KeyCode.F1 }, { Key.F2, KeyCode.F2 }, { Key.F3, KeyCode.F3 },
            { Key.F4, KeyCode.F4 }, { Key.F5, KeyCode.F5 }, { Key.F6, KeyCode.F6 },
        };

        /// <summary>
        /// Create the button UI elements as children of this GameObject.
        /// </summary>
        public void Build(float width, float height)
        {
            var rect = UIHelper.EnsureRectTransform(gameObject);
            rect.sizeDelta = new Vector2(width, height);

            _background = gameObject.AddComponent<Image>();
            _background.color = NormalBgColor;

            _button = gameObject.AddComponent<Button>();
            var colors = _button.colors;
            colors.highlightedColor = new Color(0.3f, 0.3f, 0.45f, 1f);
            colors.pressedColor = new Color(0.15f, 0.15f, 0.25f, 1f);
            _button.colors = colors;
            _button.onClick.AddListener(StartListening);

            // Text child
            var textGo = UIHelper.CreateUIObject("KeyText", transform);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            _label = textGo.AddComponent<TextMeshProUGUI>();
            _label.fontSize = 16;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = NormalTextColor;
            _label.raycastTarget = false;
        }

        /// <summary>
        /// Set the displayed key without triggering the change event.
        /// </summary>
        public void SetKey(KeyCode key)
        {
            _currentKey = key;
            _isListening = false;
            UpdateDisplay();
        }

        private void StartListening()
        {
            _isListening = true;
            _label.text = "Press a key...";
            _label.color = ListeningTextColor;
            _background.color = ListeningBgColor;
        }

        void Update()
        {
            if (!_isListening) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            // Cancel on Escape
            if (kb.escapeKey.wasPressedThisFrame)
            {
                CancelListening();
                return;
            }

            // Check all keys via New Input System
            if (!kb.anyKey.wasPressedThisFrame) return;

            foreach (var entry in KeyToKeyCodeMap)
            {
                if (kb[entry.Key].wasPressedThisFrame)
                {
                    _currentKey = entry.Value;
                    _isListening = false;
                    UpdateDisplay();
                    OnBindingChanged?.Invoke(entry.Value);
                    return;
                }
            }
        }

        private void CancelListening()
        {
            _isListening = false;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            if (_label == null) return;

            _label.text = KeyCodeToDisplayName(_currentKey);
            _label.color = NormalTextColor;

            if (_background != null)
                _background.color = NormalBgColor;
        }

        /// <summary>
        /// Convert KeyCode to a readable display name.
        /// </summary>
        private static string KeyCodeToDisplayName(KeyCode kc)
        {
            return kc switch
            {
                KeyCode.Alpha0 => "0",
                KeyCode.Alpha1 => "1",
                KeyCode.Alpha2 => "2",
                KeyCode.Alpha3 => "3",
                KeyCode.Alpha4 => "4",
                KeyCode.Alpha5 => "5",
                KeyCode.Alpha6 => "6",
                KeyCode.Alpha7 => "7",
                KeyCode.Alpha8 => "8",
                KeyCode.Alpha9 => "9",
                KeyCode.LeftShift => "L-Shift",
                KeyCode.RightShift => "R-Shift",
                KeyCode.LeftControl => "L-Ctrl",
                KeyCode.RightControl => "R-Ctrl",
                KeyCode.LeftAlt => "L-Alt",
                KeyCode.RightAlt => "R-Alt",
                _ => kc.ToString()
            };
        }
    }
}
