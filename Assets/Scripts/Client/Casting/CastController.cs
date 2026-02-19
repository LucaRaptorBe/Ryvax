using System;
using UnityEngine;
using MOBANet.Client.Settings;
using MOBANet.GameSim.Data;

namespace MOBANet.Client.Casting
{
    /// <summary>
    /// Manages cast state (Idle / Aiming) for NormalCast and QuickCastWithIndicator modes.
    /// Plain C# class — owned and driven by InputCollector.
    /// </summary>
    public class CastController
    {
        public enum CastState { Idle, Aiming }

        private CastState _state = CastState.Idle;
        private byte _activeSlot;
        private CastMode _activeMode;
        private SkillshotIndicator _indicator;
        private Func<Vector3> _getPlayerPosition;
        private Func<Vector3> _getMouseWorldPos;

        public bool IsAiming => _state == CastState.Aiming;
        public CastMode ActiveMode => _activeMode;
        public byte ActiveSlot => _activeSlot;

        public CastController(SkillshotIndicator indicator,
                               Func<Vector3> getPlayerPos,
                               Func<Vector3> getMouseWorldPos)
        {
            _indicator = indicator;
            _getPlayerPosition = getPlayerPos;
            _getMouseWorldPos = getMouseWorldPos;
        }

        /// <summary>
        /// Try to start aiming for a non-QuickCast ability.
        /// Returns true if aiming started (caller should NOT fire immediately).
        /// Returns false if this mode should fire immediately (QuickCast, Self).
        /// </summary>
        public bool TryStartCast(byte slot, CastMode mode, AbilityTargetType targetType, float range)
        {
            if (_state != CastState.Idle) return false;

            // QuickCast and Self → fire immediately, don't enter aiming
            if (mode == CastMode.QuickCast || targetType == AbilityTargetType.Self)
                return false;

            // Only Skillshot is supported for now
            if (targetType != AbilityTargetType.Skillshot)
                return false;

            _state = CastState.Aiming;
            _activeSlot = slot;
            _activeMode = mode;
            _indicator.Initialize(range);

            Vector3 origin = _getPlayerPosition();
            Vector3 mousePos = _getMouseWorldPos();
            Vector3 dir = mousePos - origin;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                dir.Normalize();
            else
                dir = Vector3.forward;

            _indicator.Show(origin, dir);
            return true;
        }

        /// <summary>
        /// Called every frame while aiming to update the indicator direction.
        /// </summary>
        public void UpdateAiming()
        {
            if (_state != CastState.Aiming) return;

            Vector3 origin = _getPlayerPosition();
            Vector3 mousePos = _getMouseWorldPos();
            Vector3 dir = mousePos - origin;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                dir.Normalize();
            else
                dir = Vector3.forward;

            _indicator.UpdateIndicator(origin, dir);
        }

        /// <summary>
        /// Confirm the cast. Returns (slot, targetPos) or null if not aiming.
        /// </summary>
        public (byte slot, Vector3 targetPos)? ConfirmCast()
        {
            if (_state != CastState.Aiming) return null;

            Vector3 mousePos = _getMouseWorldPos();
            byte slot = _activeSlot;
            CancelAiming();
            return (slot, mousePos);
        }

        /// <summary>
        /// Cancel the current aiming without casting.
        /// </summary>
        public void CancelCast()
        {
            if (_state != CastState.Aiming) return;
            CancelAiming();
        }

        private void CancelAiming()
        {
            _state = CastState.Idle;
            _indicator.Hide();
        }
    }
}
