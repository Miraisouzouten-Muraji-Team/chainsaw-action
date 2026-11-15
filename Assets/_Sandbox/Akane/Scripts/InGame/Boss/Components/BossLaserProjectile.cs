using System.Collections.Generic;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスのレーザー弾を移動させ、Playerまたは地形への命中を検出する。
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class BossLaserProjectile : MonoBehaviour
    {
        private const int HIT_BUFFER_SIZE = 32;

        [Header("高速移動の判定")]
        [SerializeField, Min(0.01f)] private float sweepRadius = 0.075f;

        private readonly RaycastHit[] hitBuffer =
            new RaycastHit[HIT_BUFFER_SIZE];

        private readonly HashSet<Collider> ownerColliders = new();

        private Rigidbody body;
        private EnemyPlayerReference playerReference;
        private BossAttackDefinition definition;
        private LayerMask stageMask;
        private Vector3 direction;
        private float speed;
        private float damage;
        private bool initialized;
        private bool hitResolved;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// 発射方向・速度・ダメージと衝突対象を設定する。
        /// </summary>
        public void Initialize(
            Vector3 shotDirection,
            BossAttackDefinition attackDefinition,
            float attackDamage,
            float shotSpeed,
            float lifeTime,
            LayerMask collisionStageMask,
            Collider[] ignoredOwnerColliders)
        {
            definition = attackDefinition;
            damage = Mathf.Max(0f, attackDamage);
            stageMask = collisionStageMask;
            playerReference = new EnemyPlayerReference();

            direction = shotDirection.normalized;
            speed = Mathf.Max(0f, shotSpeed);
            ownerColliders.Clear();

            Collider projectileCollider = GetComponent<Collider>();

            if (ignoredOwnerColliders != null)
            {
                foreach (Collider ownerCollider in ignoredOwnerColliders)
                {
                    if (ownerCollider == null) { continue; }

                    ownerColliders.Add(ownerCollider);

                    Physics.IgnoreCollision(
                        projectileCollider,
                        ownerCollider,
                        true);
                }
            }

            body.isKinematic = false;
            body.useGravity = false;
            body.collisionDetectionMode =
                CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = direction * speed;

            initialized = true;
            hitResolved = false;

            Destroy(gameObject, Mathf.Max(0.1f, lifeTime));
        }

        private void FixedUpdate()
        {
            if (!initialized || hitResolved || speed <= 0f)
            {
                return;
            }

            // 次の物理更新までに通過する範囲を調べる。
            float distance = speed * Time.fixedDeltaTime;

            int hitCount = Physics.SphereCastNonAlloc(
                body.position,
                sweepRadius,
                direction,
                hitBuffer,
                distance,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);

            Collider nearestCollider = null;
            float nearestDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                Collider candidate = hitBuffer[i].collider;

                if (candidate == null ||
                    candidate == GetComponent<Collider>() ||
                    ownerColliders.Contains(candidate))
                {
                    continue;
                }

                if (!IsValidTarget(candidate))
                {
                    continue;
                }

                if (hitBuffer[i].distance < nearestDistance)
                {
                    nearestDistance = hitBuffer[i].distance;
                    nearestCollider = candidate;
                }
            }

            if (nearestCollider != null)
            {
                ResolveHit(nearestCollider);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            ResolveHit(other);
        }

        private void OnCollisionEnter(Collision collision)
        {
            ResolveHit(collision.collider);
        }

        /// <summary>
        /// Playerか地形のColliderだけを命中対象にする。
        /// </summary>
        private bool IsValidTarget(Collider other)
        {
            if (other == null || ownerColliders.Contains(other))
            {
                return false;
            }

            bool stageHit =
                (stageMask.value & (1 << other.gameObject.layer)) != 0;

            bool playerHit =
                !other.isTrigger &&
                playerReference.IsPlayerCollider(other);

            return stageHit || playerHit;
        }

        private void ResolveHit(Collider other)
        {
            if (!initialized || hitResolved ||
                !IsValidTarget(other))
            {
                return;
            }

            hitResolved = true;

            bool playerHit = playerReference.IsPlayerCollider(other);

            if (playerHit &&
                playerReference.TryGetAttackHitReceiver(
                    requireActiveAndEnabled: true,
                    out IAttackHitReceiver receiver,
                    out _))
            {
                receiver.ReceiveAttackHit(
                    new EnemyAttackHitData(definition, damage));
            }

            body.linearVelocity = Vector3.zero;
            Destroy(gameObject);
        }
    }
}
