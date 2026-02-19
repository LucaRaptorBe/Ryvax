using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MOBANet.Client.Settings
{
    /// <summary>
    /// Singleton that owns GameSettings, handles JSON persistence,
    /// and manages the settings menu lifecycle (ESC toggle).
    /// </summary>
    public class SettingsManager : MonoBehaviour
    {
        // --- Singleton ---
        public static SettingsManager Instance { get; private set; }

        // --- Settings ---
        public GameSettings CurrentSettings { get; private set; }

        // --- Events ---
        public static event Action OnSettingsChanged;

        // --- UI blocking ---
        /// <summary>True when the settings menu is open. InputCollector checks this.</summary>
        public static bool IsUIBlockingInput { get; private set; }

        // --- Settings UI ---
        private SettingsUI _settingsUI;
        private Canvas _settingsCanvas;

        // --- Persistence ---
        private const string SETTINGS_FILE = "ryvax_settings.json";

        private string SettingsPath =>
            Path.Combine(Application.persistentDataPath, SETTINGS_FILE);

        #region Lifecycle

        /// <summary>
        /// Auto-create SettingsManager on game start if none exists in scene.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;

            var go = new GameObject("[SettingsManager]");
            go.AddComponent<SettingsManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            CurrentSettings = LoadSettings();
        }

        void Update()
        {
            // ESC toggles settings menu
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ToggleSettingsMenu();
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                IsUIBlockingInput = false;
            }
        }

        #endregion

        #region Menu Toggle

        /// <summary>
        /// Toggle the settings menu open/closed.
        /// </summary>
        public void ToggleSettingsMenu()
        {
            if (_settingsUI == null)
            {
                CreateSettingsUI();
            }

            if (_settingsUI.IsVisible)
            {
                _settingsUI.Hide();
            }
            else
            {
                _settingsUI.Show(CurrentSettings);
            }
        }

        /// <summary>
        /// Block game input (called when settings menu opens).
        /// </summary>
        public static void BlockGameInput()
        {
            IsUIBlockingInput = true;
        }

        /// <summary>
        /// Unblock game input (called when settings menu closes).
        /// </summary>
        public static void UnblockGameInput()
        {
            IsUIBlockingInput = false;
        }

        #endregion

        #region Apply / Save

        /// <summary>
        /// Apply new settings without saving to disk.
        /// </summary>
        public void ApplySettings(GameSettings newSettings)
        {
            CurrentSettings.CopyFrom(newSettings);
            CurrentSettings.ApplyDefaultKeybinds();
            OnSettingsChanged?.Invoke();
            Debug.Log("[SettingsManager] Settings applied");
        }

        /// <summary>
        /// Apply new settings and save to disk.
        /// </summary>
        public void ApplyAndSaveSettings(GameSettings newSettings)
        {
            ApplySettings(newSettings);
            SaveSettings();
        }

        #endregion

        #region JSON Persistence

        /// <summary>
        /// Load settings from JSON file, or return defaults if not found.
        /// </summary>
        private GameSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    var settings = JsonUtility.FromJson<GameSettings>(json);
                    if (settings != null)
                    {
                        Debug.Log($"[SettingsManager] Loaded settings from {SettingsPath}");
                        return settings;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SettingsManager] Failed to load settings: {e.Message}");
            }

            Debug.Log("[SettingsManager] Using default settings");
            var defaults = GameSettings.CreateDefault();
            defaults.ApplyDefaultKeybinds();
            return defaults;
        }

        /// <summary>
        /// Save current settings to JSON file.
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                string json = JsonUtility.ToJson(CurrentSettings, true);
                File.WriteAllText(SettingsPath, json);
                Debug.Log($"[SettingsManager] Settings saved to {SettingsPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SettingsManager] Failed to save settings: {e.Message}");
            }
        }

        #endregion

        #region UI Creation

        private void CreateSettingsUI()
        {
            // Create dedicated canvas for settings (renders on top)
            var canvasGo = new GameObject("SettingsCanvas");
            canvasGo.transform.SetParent(transform);

            _settingsCanvas = canvasGo.AddComponent<Canvas>();
            _settingsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _settingsCanvas.sortingOrder = 100; // On top of everything

            var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            // Ensure EventSystem exists
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // Create the settings UI panel
            var panelGo = new GameObject("SettingsUI");
            panelGo.transform.SetParent(canvasGo.transform, false);

            _settingsUI = panelGo.AddComponent<SettingsUI>();
            _settingsUI.BuildUI(canvasGo.GetComponent<RectTransform>());
        }

        #endregion
    }
}
