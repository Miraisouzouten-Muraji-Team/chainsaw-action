using System;
using UnityEngine;

/// <summary>
/// 固定射撃EnemyのAttack State中に、照準予告と1回分のProjectile連射を実行する。
/// </summary>
/// <remarks>
/// 1回の連射が完了した時点でUpdateAttackからtrueを返し、
/// 次の攻撃可否の再索敵はStateMachineとSearch Strategyへ委譲する。
/// </remarks>
[Serializable]
public sealed class TurretEnemyAttackStrategy :
    IEnemyAttackStrategy
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";

    [Header("射撃")]
    [Tooltip("このEnemyが使用する共通Projectile Shooter。")]
    [SerializeField]
    private EnemyProjectileShooter projectileShooter;

    private TurretEnemyData enemyData;
    private GameObject enemyObject;

    private EnemyPlayerReference playerReference;
    private Collider playerTargetCollider;

    private Vector3 lastAimTargetPosition;
    private float fallbackDirectionSign;
    private float preparationElapsedTime;

    private bool isAttackActive;
    private bool isPreparing;
    private bool isShooting;
    private bool hasAimTarget;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData =
            enemyData as TurretEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(TurretEnemyAttackStrategy)}には" +
                $"{nameof(TurretEnemyData)}が必要です。");

        this.enemyObject =
            enemyObject
            ? enemyObject
            : throw new ArgumentNullException(
                nameof(enemyObject));

        if (projectileShooter == null)
        {
            throw new InvalidOperationException(
                $"{nameof(TurretEnemyAttackStrategy)}の" +
                $"{nameof(EnemyProjectileShooter)}を設定してください。");
        }

        playerReference =
            new EnemyPlayerReference();

        playerReference.TryGetPlayerTransform(
            out _);

        projectileShooter.Initialize(
            enemyObject,
            playerReference);
    }

    public void BeginAttack(
        float initialAttackDirectionSign)
    {
        ResetRuntimeState();

        isAttackActive = true;
        isPreparing = true;
        fallbackDirectionSign =
            initialAttackDirectionSign < 0f
                ? -1f
                : 1f;

        RefreshAimTarget();

        if (!hasAimTarget)
        {
            lastAimTargetPosition =
                projectileShooter.FirePointPosition +
                Vector3.right *
                fallbackDirectionSign;

            hasAimTarget = true;
        }

        projectileShooter.ShowAim(
            lastAimTargetPosition);

        if (this.enemyData.AttackPreparationDuration <= 0f)
        {
            BeginShooting();
        }
    }

    public bool UpdateAttack()
    {
        if (!isAttackActive)
        {
            return false;
        }

        if (isPreparing)
        {
            RefreshAimTarget();

            if (hasAimTarget)
            {
                projectileShooter.ShowAim(
                    lastAimTargetPosition);
            }

            preparationElapsedTime +=
                Time.fixedDeltaTime;

            if (preparationElapsedTime <
                enemyData.AttackPreparationDuration)
            {
                return false;
            }

            BeginShooting();

            return !isShooting;
        }

        if (!isShooting)
        {
            return true;
        }

        if (!projectileShooter.UpdateBurst())
        {
            return false;
        }

        isShooting = false;
        return true;
    }

    public void HandleCollisionEnter(
        Collision collision)
    {
        // Turret本体の接触ダメージは持たない。
    }

    public void HandleCollisionStay(
        Collision collision)
    {
        // Turret本体の接触ダメージは持たない。
    }

    public void EndAttack()
    {
        projectileShooter.CancelBurst();
        projectileShooter.HideAim();

        ResetRuntimeState();
    }

    private void BeginShooting()
    {
        if (!isAttackActive)
        {
            return;
        }

        // 予備動作中はPlayerを追従して照準を更新するが、
        // 発射開始時点で位置をロックし、3発の途中では追尾しない。
        RefreshAimTarget();

        projectileShooter.BeginBurst(
            lastAimTargetPosition,
            enemyData,
            enemyData.AttackPower);

        isPreparing = false;
        isShooting =
            projectileShooter.IsBurstActive;
    }

    private void RefreshAimTarget()
    {
        if (!TryGetCurrentPlayerTargetPosition(
                out Vector3 targetWorldPosition))
        {
            return;
        }

        lastAimTargetPosition =
            targetWorldPosition;

        hasAimTarget = true;
    }

    /// <summary>
    /// 既存Droneと同じPlayer代表Colliderが存在する場合はその中心を狙い、
    /// 存在しない場合はPlayer Root位置へフォールバックする。
    /// </summary>
    private bool TryGetCurrentPlayerTargetPosition(
        out Vector3 targetWorldPosition)
    {
        if (!playerReference.TryGetPlayerTransform(
                out Transform playerTransform))
        {
            playerTargetCollider = null;
            targetWorldPosition = default;
            return false;
        }

        if (playerTargetCollider == null)
        {
            Transform targetTransform =
                playerTransform.Find(
                    PLAYER_TARGET_COLLIDER_NAME);

            if (targetTransform != null)
            {
                playerTargetCollider =
                    targetTransform.GetComponent<Collider>();
            }
        }

        if (playerTargetCollider != null &&
            playerTargetCollider.enabled &&
            playerTargetCollider.gameObject.activeInHierarchy)
        {
            targetWorldPosition =
                playerTargetCollider.bounds.center;

            return true;
        }

        targetWorldPosition =
            playerTransform.position;

        return true;
    }

    private void ResetRuntimeState()
    {
        isAttackActive = false;
        isPreparing = false;
        isShooting = false;
        hasAimTarget = false;

        preparationElapsedTime = 0f;
        fallbackDirectionSign = 1f;
        lastAimTargetPosition = default;
    }
}
