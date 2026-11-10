using System;
using UnityEngine;

/// <summary>
/// Enemyが発射する共通Projectile。
/// 発射時に確定した方向へ直進し、Player命中時のダメージ通知と命中後の消滅を担当する。
/// </summary>
/// <remarks>
/// Player以外のColliderとの接触では消滅しない。
/// 発射数、発射間隔、命中精度、照準表示は担当しない。
/// </remarks>
[RequireComponent(typeof(Rigidbody))]
public sealed class EnemyProjectile : MonoBehaviour
{
    private const float DIRECTION_EPSILON = 0.001f;

    [Header("移動")]
    [Tooltip("Projectileの直進速度。ワールド単位/秒。")]
    [SerializeField]
    [Min(0f)]
    private float movementSpeed = 12f;

    [Tooltip("何にも命中しなかったProjectileを自動消滅させる秒数。")]
    [SerializeField]
    [Min(0f)]
    private float lifeTime = 5f;

    private Rigidbody projectileRigidbody;
    private Collider[] projectileColliders;

    private EnemyPlayerReference playerReference;
    private ScriptableObject attackData;

    private float damage;
    private bool isInitialized;
    private bool hasResolvedHit;

    private void Awake()
    {
        projectileRigidbody =
            GetComponent<Rigidbody>();

        projectileColliders =
            GetComponentsInChildren<Collider>(
                true);
    }

    /// <summary>
    /// 発射方向と今回の攻撃情報を確定し、Projectileを発射する。
    /// </summary>
    /// <param name="direction">
    /// 発射時に確定したワールド方向。発射後はPlayerを追尾しない。
    /// </param>
    /// <param name="ignoredOwnerColliders">
    /// 発射元Enemy自身との衝突だけを無効化するためのCollider一覧。
    /// </param>
    public void Initialize(
        Vector3 direction,
        ScriptableObject attackData,
        float damage,
        EnemyPlayerReference playerReference,
        Collider[] ignoredOwnerColliders)
    {
        if (direction.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            throw new ArgumentException(
                "Projectileの発射方向が0です。",
                nameof(direction));
        }

        this.attackData =
            attackData
            ? attackData
            : throw new ArgumentNullException(
                nameof(attackData));

        this.playerReference =
            playerReference
            ?? throw new ArgumentNullException(
                nameof(playerReference));

        this.damage =
            Mathf.Max(
                0f,
                damage);

        if (projectileColliders == null ||
            projectileColliders.Length == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyProjectile)}には{nameof(Collider)}が必要です。");
        }

        IgnoreOwnerCollisions(
            ignoredOwnerColliders);

        // Projectileは仕様上直進弾なので、PrefabのGravity / Kinematic設定に依存させない。
        projectileRigidbody.isKinematic = false;
        projectileRigidbody.useGravity = false;
        projectileRigidbody.collisionDetectionMode =
            CollisionDetectionMode.ContinuousDynamic;
        projectileRigidbody.linearVelocity =
            direction.normalized *
            movementSpeed;

        isInitialized = true;

        if (lifeTime <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        Destroy(
            gameObject,
            lifeTime);
    }

    private void OnTriggerEnter(
        Collider other)
    {
        ResolveHit(
            other);
    }

    private void OnCollisionEnter(
        Collision collision)
    {
        ResolveHit(
            collision?.collider);
    }

    private void ResolveHit(
        Collider hitCollider)
    {
        if (!isInitialized ||
            hasResolvedHit ||
            hitCollider == null)
        {
            return;
        }

        // Stageやエリア判定など、Player以外との接触では命中扱いにしない。
        if (!playerReference.IsPlayerCollider(
                hitCollider))
        {
            return;
        }

        hasResolvedHit = true;

        if (playerReference.TryGetAttackHitReceiver(
                requireActiveAndEnabled: true,
                out IAttackHitReceiver playerAttackHitReceiver,
                out _))
        {
            playerAttackHitReceiver.ReceiveAttackHit(
                new EnemyAttackHitData(
                    attackData,
                    damage));
        }

        Destroy(gameObject);
    }

    private void IgnoreOwnerCollisions(
        Collider[] ignoredOwnerColliders)
    {
        if (ignoredOwnerColliders == null ||
            projectileColliders == null)
        {
            return;
        }

        for (int projectileIndex = 0;
             projectileIndex < projectileColliders.Length;
             projectileIndex++)
        {
            Collider projectileCollider =
                projectileColliders[projectileIndex];

            if (projectileCollider == null)
            {
                continue;
            }

            for (int ownerIndex = 0;
                 ownerIndex < ignoredOwnerColliders.Length;
                 ownerIndex++)
            {
                Collider ownerCollider =
                    ignoredOwnerColliders[ownerIndex];

                if (ownerCollider == null)
                {
                    continue;
                }

                Physics.IgnoreCollision(
                    projectileCollider,
                    ownerCollider,
                    true);
            }
        }
    }
}
