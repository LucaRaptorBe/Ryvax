// TargetingSystem.cs - Tab-targeting and target lock system
// Attached to the local player's GameObject

using System.Collections.Generic;
using UnityEngine;
using MOBANet.UnityView.Core;
using MOBANet.UnityView.Entities;

namespace MOBANet.Client.Targeting
{
    public class TargetingSystem : MonoBehaviour
    {
        private uint _currentTargetId;
        private NetworkClient _networkClient;
        private TargetIndicator _currentIndicator;

        public uint CurrentTargetId => _currentTargetId;
        public bool HasTarget => _currentTargetId != 0;

        public void Initialize(NetworkClient networkClient)
        {
            _networkClient = networkClient;
        }

        /// <summary>
        /// Tab press: cycle to next enemy sorted by distance.
        /// </summary>
        public void CycleTarget()
        {
            if (_networkClient == null) return;

            byte localTeamId = _networkClient.GetLocalTeamId();
            var enemies = _networkClient.GetEnemyPlayerViews(localTeamId);

            if (enemies.Count == 0)
            {
                ClearTarget();
                return;
            }

            // Sort by distance to local player
            Vector3 localPos = _networkClient.GetVisualPosition();
            enemies.Sort((a, b) =>
            {
                float distA = Vector3.Distance(a.transform.position, localPos);
                float distB = Vector3.Distance(b.transform.position, localPos);
                return distA.CompareTo(distB);
            });

            // Find the next enemy after current target
            int currentIndex = -1;
            if (_currentTargetId != 0)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (enemies[i].EntityId == _currentTargetId)
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }

            int nextIndex = (currentIndex + 1) % enemies.Count;
            LockTarget(enemies[nextIndex].EntityId);
        }

        /// <summary>
        /// Lock onto a specific entity.
        /// </summary>
        public void LockTarget(uint entityId)
        {
            if (entityId == _currentTargetId) return;

            ClearTarget();

            _currentTargetId = entityId;

            // Add TargetIndicator to target's PlayerView
            var view = _networkClient.GetPlayerView(entityId);
            if (view != null)
            {
                _currentIndicator = view.gameObject.AddComponent<TargetIndicator>();
                byte localTeamId = _networkClient.GetLocalTeamId();
                var simPlayer = _networkClient.GetSimPlayer(entityId);
                bool isEnemy = simPlayer != null && simPlayer.TeamId != localTeamId;
                _currentIndicator.Show(isEnemy);
            }
        }

        /// <summary>
        /// Clear current target.
        /// </summary>
        public void ClearTarget()
        {
            if (_currentIndicator != null)
            {
                _currentIndicator.Hide();
                Destroy(_currentIndicator);
                _currentIndicator = null;
            }
            _currentTargetId = 0;
        }

        /// <summary>
        /// Check if current target is still valid (alive and has a view).
        /// </summary>
        void Update()
        {
            if (!HasTarget) return;

            var simPlayer = _networkClient?.GetSimPlayer(_currentTargetId);
            if (simPlayer == null || simPlayer.Stats.IsDead)
            {
                ClearTarget();
            }
        }
    }
}
