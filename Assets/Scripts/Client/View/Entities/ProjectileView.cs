// ProjectileView.cs - Cosmetic projectile on client
// Spawned when AbilityUsed event is received
// Moves independently, not synced with server projectile

using UnityEngine;

namespace MOBANet.UnityView.Entities
{
    public class ProjectileView : MonoBehaviour
    {
        private Vector3 _direction;
        private float _speed;
        private float _maxLifetime;
        private float _elapsed;

        public void Initialize(Vector3 startPos, Vector3 direction, float speed, float range)
        {
            transform.position = startPos;
            _direction = direction.normalized;
            _speed = speed;
            _maxLifetime = range / speed;
            _elapsed = 0f;

            // Orient capsule along direction of travel
            if (_direction.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(_direction);
            }
        }

        void Update()
        {
            _elapsed += Time.deltaTime;
            if (_elapsed >= _maxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            transform.position += _direction * _speed * Time.deltaTime;
        }
    }
}
