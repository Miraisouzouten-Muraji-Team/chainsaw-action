using System;
using UnityEngine;

/// <summary>
/// 新型ドローンの照準予備動作と、相対位置追従を伴うProjectile連射を行う。
/// </summary>
/// <remarks>
/// Preparation中は完全停止し、照準だけ更新する。
/// 予備動作開始時のPlayerとのXY相対位置を保持し、射撃中はMotorの安全制限内で追従する。
/// 追従経路探索、後退、接触攻撃、再索敵、State遷移は担当しない。
/// </remarks>
[Serializable]
public sealed class NewDroneEnemyAttackStrategy : IEnemyAttackStrategy
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";
    private const float DIRECTION_EPSILON = 0.001f;

    [Header("射撃")]
    [Tooltip("新型Droneに配置したEnemyProjectileShooter。5発/0.1秒/Accuracy20%はShooter側で設定する。")]
    [SerializeField]
    private EnemyProjectileShooter projectileShooter;

    private NewDroneEnemyData enemyData;
    private GameObject enemyObject;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private DroneEnemyPlanarMotor planarMotor;
    private EnemyPlayerReference playerReference;
    private Collider playerTargetCollider;

    private Vector3 lastAimTargetPosition;
    private Vector2 playerToDroneOffsetAtPreparationStart;
    private float preparationElapsedTime;
    private float fallbackDirectionSign;
    private bool isPreparationActive;
    private bool isShooting;
    private bool hasAimTarget;
    private bool hasFollowTarget;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData = enemyData as NewDroneEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(NewDroneEnemyAttackStrategy)}には{nameof(NewDroneEnemyData)}が必要です。");

        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));

        this.visualRoot = visualRoot
            ? visualRoot
            : throw new InvalidOperationException("新型DroneにはVisualRootが必要です。");

        if (projectileShooter == null ||
            !projectileShooter.gameObject.activeInHierarchy ||
            !projectileShooter.enabled ||
            (projectileShooter.transform != enemyObject.transform &&
             !projectileShooter.transform.IsChildOf(enemyObject.transform)))
        {
            throw new InvalidOperationException(
                "EnemyProjectileShooterは有効なEnemy子階層のComponentを設定してください。");
        }

        enemyRigidbody = enemyObject.GetComponent<Rigidbody>();
        BoxCollider boxCollider = enemyObject.GetComponent<BoxCollider>();

        if (enemyRigidbody == null || enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException("新型Droneには非KinematicのRigidbodyが必要です。");
        }

        if (boxCollider == null || !boxCollider.enabled)
        {
            throw new InvalidOperationException("新型Drone本体には有効なBoxColliderが必要です。");
        }

        if (this.enemyData.NavigationObstacleMask.value == 0)
        {
            throw new InvalidOperationException("NewDroneEnemyDataのNavigation Obstacle MaskにStage障害物を設定してください。");
        }

        playerReference = new EnemyPlayerReference();

        planarMotor = new DroneEnemyPlanarMotor(
            enemyRigidbody,
            boxCollider,
            this.enemyData.NavigationObstacleMask,
            IsBlockingMovementCollider);

        playerReference.TryGetPlayerTransform(out _);
        projectileShooter.Initialize(enemyObject, playerReference);
    }

    public void BeginPreparation(float initialAttackDirectionSign)
    {
        ResetRuntimeState();
        planarMotor.StopPlanarMovement();

        isPreparationActive = true;
        fallbackDirectionSign = initialAttackDirectionSign < 0f ? -1f : 1f;

        RefreshAimTarget(captureFollowReference: true);

        if (!hasAimTarget)
        {
            // Playerが一時的に消えていても、予備動作と連射の進行を保証する。
            lastAimTargetPosition = projectileShooter.FirePointPosition +
                Vector3.right * fallbackDirectionSign;
            hasAimTarget = true;
        }

        projectileShooter.ShowAim(lastAimTargetPosition);
    }

    public bool UpdatePreparation()
    {
        if (!isPreparationActive)
        {
            return true;
        }

        // 外部の慣性やState切替直前の移動指示を引き継がない。
        planarMotor.StopPlanarMovement();
        RefreshAimTarget();
        projectileShooter.ShowAim(lastAimTargetPosition);

        preparationElapsedTime += Time.fixedDeltaTime;
        if (preparationElapsedTime < enemyData.AttackPreparationDuration)
        {
            return false;
        }

        isPreparationActive = false;
        return true;
    }

    public void BeginAttack()
    {
        // 射撃開始時に追従基準を取り直すと、準備中のPlayer移動が相殺される。
        // 準備開始時の基準を維持し、射撃が始まった直後からその移動量を追いかける。
        // ShooterはBeginBurstで方向を固定し、後続弾は追尾弾にならない。
        if (TryGetCurrentPlayerTargetPosition(out Vector3 playerPosition))
        {
            lastAimTargetPosition = playerPosition;
            hasAimTarget = true;

            if (!hasFollowTarget)
            {
                CaptureFollowReference(playerPosition);
            }

            AlignVisualTowardTarget(playerPosition);
        }

        projectileShooter.BeginBurst(
            lastAimTargetPosition,
            enemyData,
            enemyData.AttackPower);

        isShooting = projectileShooter.IsBurstActive;
    }

    public bool UpdateAttack()
    {
        if (!isShooting)
        {
            planarMotor.StopPlanarMovement();
            return true;
        }

        UpdateShootingFollow();

        // 追従でPlayerを見失っても、5発の発射完了はShooterだけで進行する。
        if (!projectileShooter.UpdateBurst())
        {
            return false;
        }

        isShooting = false;
        planarMotor.StopPlanarMovement();
        return true;
    }

    public void HandleCollisionEnter(Collision collision)
    {
        // 射撃中の本体接触にダメージを設定しない。
    }

    public void HandleCollisionStay(Collision collision)
    {
        // 射撃中の本体接触にダメージを設定しない。
    }

    public void EndAttack()
    {
        projectileShooter.CancelBurst();
        projectileShooter.HideAim();
        planarMotor?.StopPlanarMovement();
        ResetRuntimeState();
    }

    private void RefreshAimTarget(bool captureFollowReference = false)
    {
        if (!TryGetCurrentPlayerTargetPosition(out Vector3 targetPosition))
        {
            // 最後に得たAimを保持する。未取得時は初回のフォールバック方向を使用する。
            return;
        }

        if (captureFollowReference)
        {
            CaptureFollowReference(targetPosition);
        }

        lastAimTargetPosition = targetPosition;
        hasAimTarget = true;
        AlignVisualTowardTarget(targetPosition);
    }

    private void CaptureFollowReference(Vector3 playerPosition)
    {
        playerToDroneOffsetAtPreparationStart = new Vector2(
            enemyRigidbody.position.x - playerPosition.x,
            enemyRigidbody.position.y - playerPosition.y);
        hasFollowTarget = true;
    }

    private void UpdateShootingFollow()
    {
        if (!TryGetCurrentPlayerTargetPosition(out Vector3 currentPlayerPosition))
        {
            planarMotor.StopPlanarMovement();
            return;
        }

        if (!hasFollowTarget)
        {
            // 予備動作時にPlayerが存在しなかった場合だけ、現在位置で基準を確保する。
            CaptureFollowReference(currentPlayerPosition);
        }

        Vector2 targetPosition =
            new Vector2(currentPlayerPosition.x, currentPlayerPosition.y) +
            playerToDroneOffsetAtPreparationStart;

        Vector2 currentPosition =
            new Vector2(enemyRigidbody.position.x, enemyRigidbody.position.y);

        Vector2 toTarget = targetPosition - currentPosition;
        float distance = toTarget.magnitude;

        if (distance <= DIRECTION_EPSILON ||
            enemyData.ShootingFollowSpeed <= 0f ||
            Time.fixedDeltaTime <= 0f)
        {
            planarMotor.StopPlanarMovement();
            return;
        }

        // 到達間際のオーバーシュートを防ぎ、速度上限4.0等を厳守する。
        float speed = Mathf.Min(
            enemyData.ShootingFollowSpeed,
            distance / Time.fixedDeltaTime);

        planarMotor.TrySetPlanarVelocity(toTarget / distance * speed);
    }

    private bool TryGetCurrentPlayerTargetPosition(out Vector3 targetWorldPosition)
    {
        if (!playerReference.TryGetPlayerTransform(out Transform playerTransform))
        {
            playerTargetCollider = null;
            targetWorldPosition = default;
            return false;
        }

        if (playerTargetCollider == null)
        {
            Transform targetTransform = playerTransform.Find(PLAYER_TARGET_COLLIDER_NAME);
            if (targetTransform != null)
            {
                playerTargetCollider = targetTransform.GetComponent<Collider>();
            }
        }

        if (playerTargetCollider != null &&
            playerTargetCollider.enabled &&
            playerTargetCollider.gameObject.activeInHierarchy)
        {
            targetWorldPosition = playerTargetCollider.bounds.center;
            return true;
        }

        // タレットと同様、射撃そのものはPlayer Rootでも継続可能にする。
        targetWorldPosition = playerTransform.position;
        return true;
    }

    private bool IsBlockingMovementCollider(Collider targetCollider)
    {
        return !playerReference.IsPlayerCollider(targetCollider);
    }

    private void AlignVisualTowardTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - projectileShooter.FirePointPosition;
        direction.z = 0f;

        if (direction.sqrMagnitude <= DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return;
        }

        // VisualRootのみ回し、Rigidbody/Colliderの軸やモデルのFacingOffsetを保持する。
        visualRoot.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void ResetRuntimeState()
    {
        isPreparationActive = false;
        isShooting = false;
        hasAimTarget = false;
        hasFollowTarget = false;
        preparationElapsedTime = 0f;
        fallbackDirectionSign = 1f;
        lastAimTargetPosition = default;
        playerToDroneOffsetAtPreparationStart = default;
    }
}
