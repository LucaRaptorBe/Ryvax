// ClassSelectionUI.cs - Pre-spawn class selection screen
// Dynamically creates buttons for each CharacterClassType.
// Fires OnClassSelected(classId) when the player picks a class.

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MOBANet.Client.Animation;

namespace MOBANet.Client.UI
{
    /// <summary>
    /// Full-screen class selection panel shown before the player spawns.
    /// Buttons are created dynamically for each CharacterClassType (Archer..Warrior).
    /// </summary>
    public class ClassSelectionUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Transform buttonContainer;

        /// <summary>
        /// Fired when the player selects a class. Payload is the classId (int).
        /// </summary>
        public event Action<int> OnClassSelected;

        private bool _created;

        public void Show()
        {
            EnsureEventSystem();
            if (!_created) CreateButtons();
            if (panel != null) panel.SetActive(true);
        }

        public void Hide()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        private void CreateButtons()
        {
            _created = true;

            if (buttonContainer == null)
            {
                Debug.LogError("[ClassSelectionUI] buttonContainer not assigned!");
                return;
            }

            // Add layout if not already present
            if (buttonContainer.GetComponent<GridLayoutGroup>() == null)
            {
                var grid = buttonContainer.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(180f, 50f);
                grid.spacing = new Vector2(15f, 15f);
                grid.childAlignment = TextAnchor.MiddleCenter;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 4;
            }

            // Create a button for each playable class (skip None=0)
            foreach (CharacterClassType classType in Enum.GetValues(typeof(CharacterClassType)))
            {
                if (classType == CharacterClassType.None) continue;

                int classId = (int)classType;
                string className = classType.ToString();

                var btnGo = new GameObject($"Btn_{className}");
                btnGo.transform.SetParent(buttonContainer, false);

                var rect = btnGo.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(180f, 50f);

                var image = btnGo.AddComponent<Image>();
                image.color = new Color(0.2f, 0.2f, 0.3f, 0.9f);

                var button = btnGo.AddComponent<Button>();
                var colors = button.colors;
                colors.highlightedColor = new Color(0.3f, 0.3f, 0.5f, 1f);
                colors.pressedColor = new Color(0.15f, 0.15f, 0.25f, 1f);
                button.colors = colors;

                // Text child
                var textGo = new GameObject("Text");
                textGo.transform.SetParent(btnGo.transform, false);

                var textRect = textGo.AddComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = className;
                tmp.fontSize = 22;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
                tmp.raycastTarget = false;

                // Capture for closure
                int capturedId = classId;
                button.onClick.AddListener(() => OnClassSelected?.Invoke(capturedId));
            }
        }
    }
}
