using Assets.Scripts.Ecs;
using UnityEngine;
using UnityEngine.AI;

namespace Assets.Scripts
{
    /// <summary>Procedural attack gesture that stays independent from the imported enemy model.</summary>
    public sealed class EnemyAttackPresentation : MonoBehaviour
    {
        private const float MeleeDurationSeconds = 0.32f;
        private const float RangedDurationSeconds = 0.62f;
        private const float MeleeNodDegrees = 28f;
        private const float MoveTurnDegreesPerSecond = 540f;

        private NavMeshAgent _agent;
        private Transform _cachedTransform;
        private EnemyAttackPresentationKind _kind;
        private Quaternion _baseYaw = Quaternion.identity;
        private float _elapsed;
        private float _duration;
        private bool _playing;

        private void Awake()
        {
            CacheComponents();
        }

        public void ResetForReuse()
        {
            CacheComponents();
            _playing = false;
            _elapsed = 0f;
            _duration = 0f;
            _baseYaw = Quaternion.Euler(0f, _cachedTransform.eulerAngles.y, 0f);
            _cachedTransform.rotation = _baseYaw;

            if (_agent != null)
            {
                _agent.updateRotation = false;
            }
        }

        public void Play(EnemyAttackPresentationKind kind, Vector3 target)
        {
            CacheComponents();
            Vector3 toTarget = target - _cachedTransform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                _baseYaw = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            }

            _kind = kind;
            _elapsed = 0f;
            _duration = kind == EnemyAttackPresentationKind.RangedBarrelRoll
                ? RangedDurationSeconds
                : MeleeDurationSeconds;
            _playing = true;
            _cachedTransform.rotation = _baseYaw;
        }

        private void LateUpdate()
        {
            if (Time.timeScale <= 0f || Application.isFocused == false)
            {
                return;
            }

            if (_playing)
            {
                UpdateGesture();
                return;
            }

            UpdateMovementFacing();
        }

        private void UpdateGesture()
        {
            _elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(_elapsed / Mathf.Max(0.01f, _duration));

            if (_kind == EnemyAttackPresentationKind.RangedBarrelRoll)
            {
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                _cachedTransform.rotation = _baseYaw * Quaternion.AngleAxis(360f * eased, Vector3.forward);
            }
            else
            {
                float pitch = Mathf.Sin(progress * Mathf.PI) * MeleeNodDegrees;
                _cachedTransform.rotation = _baseYaw * Quaternion.Euler(pitch, 0f, 0f);
            }

            if (progress >= 1f)
            {
                _cachedTransform.rotation = _baseYaw;
                _playing = false;
            }
        }

        private void UpdateMovementFacing()
        {
            if (_agent == null || _agent.isActiveAndEnabled == false)
            {
                return;
            }

            Vector3 direction = _agent.desiredVelocity;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
            {
                return;
            }

            Quaternion desiredYaw = Quaternion.LookRotation(direction.normalized, Vector3.up);
            _baseYaw = Quaternion.RotateTowards(_baseYaw, desiredYaw, MoveTurnDegreesPerSecond * Time.deltaTime);
            _cachedTransform.rotation = _baseYaw;
        }

        private void CacheComponents()
        {
            _cachedTransform ??= transform;
            _agent ??= GetComponent<NavMeshAgent>();
            if (_agent != null)
            {
                _agent.updateRotation = false;
            }
        }
    }
}
