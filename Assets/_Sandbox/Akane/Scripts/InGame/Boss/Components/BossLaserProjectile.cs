using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスレーザーを直進させ、Playerへ共通命中情報を送る。
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    public sealed class BossLaserProjectile : MonoBehaviour
    {
        private Rigidbody body;
        private EnemyPlayerReference playerReference;
        private BossAttackDefinition definition;
        private LayerMask stageMask;
        private float damage;
        private bool initialized;
        private bool hitResolved;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        public void Initialize(
            Vector3 direction,
            BossAttackDefinition attackDefinition,
            float attackDamage,
            float speed,
            float lifeTime,
            LayerMask collisionStageMask,
            Collider[] ownerColliders)
        {
            definition = attackDefinition;
            damage = Mathf.Max(0f, attackDamage);
            stageMask = collisionStageMask;
            playerReference = new EnemyPlayerReference();

            Collider projectileCollider = GetComponent<Collider>();

            if (ownerColliders != null)
            {
                foreach (Collider ownerCollider in ownerColliders)
                {
                    if (ownerCollider != null)
                    {
                        Physics.IgnoreCollision(projectileCollider, ownerCollider, true);
                    }
                }
            }

            body.isKinematic = false;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = direction.normalized * Mathf.Max(0f, speed);
            initialized = true;
            Destroy(gameObject, Mathf.Max(0.1f, lifeTime));
        }

        private void OnTriggerEnter(Collider other)
        {
            ResolveHit(other);
        }

        private void OnCollisionEnter(Collision collision)
        {
            ResolveHit(collision?.collider);
        }

        private void ResolveHit(Collider other)
        {
            if (!initialized || hitResolved || other == null) { return; }

            bool playerHit = playerReference.IsPlayerCollider(other);
            bool stageHit = (stageMask.value & (1 << other.gameObject.layer)) != 0;

            if (!playerHit && !stageHit) { return; }

            hitResolved = true;

            if (playerHit && playerReference.TryGetAttackHitReceiver(
                    requireActiveAndEnabled: true,
                    out IAttackHitReceiver receiver,
                    out _))
            {
                receiver.ReceiveAttackHit(new EnemyAttackHitData(definition, damage));
            }

            Destroy(gameObject);
        }
    }
}
