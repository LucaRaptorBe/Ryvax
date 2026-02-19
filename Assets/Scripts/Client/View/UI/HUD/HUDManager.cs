// HUDManager.cs - Bootstrap for the HUD system
// Detects local player spawn, initializes ability bar, refreshes each frame
// Manages ClassSelectionUI → sends GameCommand.ClassSelect on selection
// Routes AbilityUsed events to ability bar for smooth cooldown countdown

using UnityEngine;
using MOBANet.GameSim.Data;
using MOBANet.GameSim.Entities;
using MOBANet.NetAdapter.Messages;
using MOBANet.UnityView.Core;
using MOBANet.UnityView.Entities;
using MOBANet.UnityView.Input;
using MOBANet.Client.Animation;
using MOBANet.Client.HUD;

namespace MOBANet.Client.UI
{
    /// <summary>
    /// Bootstraps the HUD. Detects the local player, initializes the ability bar
    /// with the correct CharacterClass, and refreshes cooldowns each frame.
    /// Also manages the class selection screen before spawn.
    /// Routes AbilityUsed events → AbilityBarUI.StartCooldown for smooth sweep.
    /// </summary>
    public class HUDManager : MonoBehaviour
    {
        [Header("HUD Components")]
        [SerializeField] private AbilityBarUI abilityBar;
        [SerializeField] private ClassSelectionUI classSelection;

        [Header("Network")]
        [SerializeField] private NetworkClient networkClient;

        [Header("Data")]
        [Tooltip("Index by CharacterClassType enum value (0=None, 1=Archer, 2=Mage, ...)")]
        [SerializeField] private CharacterClass[] characterClasses;

        private SimPlayer _cachedSimPlayer;
        private bool _initialized;
        private bool _classSelectionShown;
        private TargetInfoUI _targetInfoUI;

        void OnEnable()
        {
            if (classSelection != null)
                classSelection.OnClassSelected += OnClassSelected;

            if (networkClient != null)
                networkClient.OnAbilityCast += OnAbilityCast;
        }

        void OnDisable()
        {
            if (classSelection != null)
                classSelection.OnClassSelected -= OnClassSelected;

            if (networkClient != null)
                networkClient.OnAbilityCast -= OnAbilityCast;
        }

        void Update()
        {
            // Show class selection when NetworkClient is waiting
            if (!_classSelectionShown && networkClient != null && networkClient.IsWaitingForClassSelection)
            {
                if (classSelection != null)
                {
                    classSelection.Show();
                    _classSelectionShown = true;
                }
            }

            if (abilityBar == null) { Debug.LogWarning("[HUDManager] abilityBar is NULL — cannot init"); return; }

            if (!_initialized)
            {
                TryInitialize();
                return;
            }

            // Player disconnected or destroyed
            if (_cachedSimPlayer == null)
            {
                _initialized = false;
                return;
            }

            abilityBar.Refresh(_cachedSimPlayer);
        }

        private void OnClassSelected(int classId)
        {
            // Debug.Log($"[HUDManager] OnClassSelected classId={classId}, networkClient={networkClient != null}");
            if (networkClient == null) return;

            var cmd = GameCommand.ClassSelect(0, (byte)classId);
            uint seq = networkClient.SendEventCommand(cmd);
            Debug.Log($"[HUDManager] ClassSelect sent, assigned seq={seq}");

            classSelection?.Hide();
        }

        /// <summary>
        /// Called when any player casts an ability.
        /// Only starts cooldown if it's the local player.
        /// </summary>
        private void OnAbilityCast(uint entityId, byte slot)
        {
            if (!_initialized) return;
            if (networkClient == null) return;

            // Only start cooldown for local player's abilities
            if (entityId != networkClient.LocalEntityId) return;

            abilityBar.StartCooldown(slot);
        }

        private void TryInitialize()
        {
            var localPlayer = GameManager.Instance?.GetLocalPlayer();
            if (localPlayer == null) { return; }

            var playerView = localPlayer.GetComponent<PlayerView>();
            if (playerView == null) { Debug.Log("[HUDManager] TryInit: no PlayerView"); return; }
            if (playerView.SimPlayer == null) { Debug.Log("[HUDManager] TryInit: SimPlayer is null"); return; }
            if (playerView.SimPlayer.ClassId == 0) { Debug.Log("[HUDManager] TryInit: ClassId=0"); return; }

            _cachedSimPlayer = playerView.SimPlayer;

            CharacterClass charClass = GetCharacterClass(_cachedSimPlayer.ClassId);
            Debug.Log($"[HUDManager] TryInit: ClassId={_cachedSimPlayer.ClassId}, charClass={charClass?.className ?? "NULL"}, classes={characterClasses?.Length ?? 0}");
            abilityBar.Initialize(charClass, _cachedSimPlayer);

            // Initialize target info UI
            var inputCollector = localPlayer.GetComponent<InputCollector>();
            if (inputCollector != null && inputCollector.TargetingSystem != null && _targetInfoUI == null)
            {
                var targetInfoGO = new GameObject("TargetInfoUI");
                targetInfoGO.transform.SetParent(transform);
                _targetInfoUI = targetInfoGO.AddComponent<TargetInfoUI>();
                _targetInfoUI.Initialize(inputCollector.TargetingSystem, networkClient, transform);
            }

            _initialized = true;
            Debug.Log("[HUDManager] Ability bar initialized!");
        }

        private CharacterClass GetCharacterClass(int classId)
        {
            if (characterClasses == null) return null;
            if (classId < 0 || classId >= characterClasses.Length) return null;
            return characterClasses[classId];
        }
    }
}
