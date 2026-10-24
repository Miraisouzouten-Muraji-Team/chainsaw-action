using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;


[Serializable]
[MovedFrom(false, sourceClassName: "DroneEnemySearchStrategy")]
public sealed class OldDroneEnemySearchStrategy :
    IEnemySearchStrategy,
    IEnemyDebugRayProvider
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";

    private const float DIRECTION_EPSILON = 0.001f;

    [Header("個体設定")]
    [Tooltip("このOldDroneEnemyがPlayerを検知する半径。OldDroneEnemyDataのDetectionRadiusは使用しない。")]
    [SerializeField]
    [Min(0f)]
    private float detectionRadius = 2f;

    // 既存PrefabのSerializeReference内に保存されたPatrolPoint参照を維持するため、
    // Inspector入力だけはStrategy側に残し、巡回処理本体へ初期化時に渡す。
    [Header("巡回地点")]
    [Tooltip("巡回地点A。初期化時にEnemy基準のXYオフセットとして保存する。")]
    [SerializeField]
    private Transform patrolPointA;

    [Tooltip("巡回地点B。初期化時にEnemy基準のXYオフセットとして保存する。")]
    [SerializeField]
    private Transform patrolPointB;

    private OldDroneEnemyData enemyData;

    private GameObject enemyObject;
    private Transform enemyTransform;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private BoxCollider enemyBoxCollider;
    private DroneEnemyPlanarMotor planarMotor;
    private DroneEnemyPathNavigator pathNavigator;
    private DroneEnemyTwoPointPatrol twoPointPatrol;

    private EnemyPlayerReference playerReference;
    private EnemyPlayerDetectionSensor playerDetectionSensor;
    private BoxCollider playerTargetCollider;

    private Vector3 patrolOriginPosition;

    private bool isSearchActive;
    private bool isReturningToPatrolOrigin;

    /// <summary>
    /// このOldDroneEnemy個体に設定された索敵半径。
    /// </summary>
    public float DetectionRadius => detectionRadius;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData =
            enemyData as OldDroneEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(OldDroneEnemySearchStrategy)}には" +
                $"{nameof(OldDroneEnemyData)}が必要です。");

        this.enemyObject =
            enemyObject
            ?? throw new ArgumentNullException(
                nameof(enemyObject));

        enemyTransform =
            enemyObject.transform;

        this.visualRoot =
            visualRoot
            ? visualRoot
            : throw new InvalidOperationException(
                $"{nameof(OldDroneEnemySearchStrategy)}を使用するEnemyには" +
                "VisualRootが必要です。");


        enemyRigidbody =
            enemyObject.GetComponent<Rigidbody>();

        if (enemyRigidbody == null)
        {
            throw new InvalidOperationException(
                $"{nameof(OldDroneEnemySearchStrategy)}を使用するEnemyには" +
                $"{nameof(Rigidbody)}が必要です。");
        }

        if (enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(OldDroneEnemySearchStrategy)}は" +
                "非KinematicのRigidbodyを使用する前提です。");
        }

        // 巡回安全判定は実際のDrone本体形状を基準にするため、
        // 子階層や別形状Colliderへの暗黙フォールバックは行わない。
        enemyBoxCollider =
            enemyObject.GetComponent<BoxCollider>();

        if (enemyBoxCollider == null)
        {
            throw new InvalidOperationException(
                $"{nameof(OldDroneEnemySearchStrategy)}を使用するEnemy本体には" +
                $"{nameof(BoxCollider)}が必要です。");
        }

        if (!enemyBoxCollider.enabled)
        {
            throw new InvalidOperationException(
                $"{nameof(OldDroneEnemySearchStrategy)}を使用するEnemy本体の" +
                $"{nameof(BoxCollider)}を有効にしてください。");
        }

        playerReference =
            new EnemyPlayerReference();

        playerDetectionSensor =
            new EnemyPlayerDetectionSensor(
                enemyTransform,
                playerReference);

        planarMotor =
            new DroneEnemyPlanarMotor(
                enemyRigidbody,
                enemyBoxCollider,
                this.enemyData.NavigationObstacleMask,
                IsBlockingMovementCollider);

        pathNavigator =
            new DroneEnemyPathNavigator(
                enemyRigidbody,
                enemyBoxCollider,
                planarMotor,
                this.enemyData.NavigationObstacleMask,
                this.enemyData.PathSafetyMargin,
                this.enemyData.PathSearchPadding,
                this.enemyData.MaximumExpandedPathNodes,
                this.enemyData.WaypointArrivalTolerance,
                this.enemyData.NavigationMoveSpeed,
                this.enemyData.NavigationStallMinimumMoveSpeed,
                this.enemyData.NavigationStallDetectionDuration);

        twoPointPatrol =
            new DroneEnemyTwoPointPatrol(
                patrolPointA,
                patrolPointB,
                enemyTransform,
                this.visualRoot,
                enemyRigidbody,
                enemyBoxCollider,
                planarMotor,
                playerReference,
                this.enemyData.NavigationObstacleMask);

        // 帰還処理は共通巡回クラスが固定したゲーム開始時位置を
        // 同じ巡回原点として利用する。
        patrolOriginPosition =
            twoPointPatrol.PatrolOriginPosition;

        playerReference.TryGetPlayerTransform(
            out _);
    }

    public void BeginSearch()
    {
        playerDetectionSensor.ClearDebugRay();

        isSearchActive = true;
        isReturningToPatrolOrigin = false;

        pathNavigator.Reset();

        playerReference.TryGetPlayerTransform(
            out _);

        // 帰還完了後の巡回順は固定原点→A→B→A...で開始する。
        twoPointPatrol.ResetToPointA();

        if (!twoPointPatrol.TryPrepareRoute(
                out string failureReason))
        {
            StopPlanarMovement();

            Debug.LogWarning(
                $"{nameof(OldDroneEnemySearchStrategy)}: " +
                "安全な巡回地点・経路を確定できないため、" +
                $"このSearch中の巡回を停止します。{failureReason}",
                enemyObject);

            return;
        }

        BeginReturnToPatrolOrigin();
    }

    public bool UpdateSearch(
        out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;

        if (!isSearchActive)
        {
            return false;
        }

        twoPointPatrol.SyncPointTransforms();

        // 帰還はSearch中の移動方法の1つとして扱い、
        // 帰還中でも通常と同じ条件でPlayerを索敵し続ける。
        if (TryDetectPlayer(
                out detectedPlayerDirectionSign))
        {
            StopPlanarMovement();

            // State遷移要求後にSearchが一時的に残っても、
            // 帰還・巡回・接触攻撃を続けない。
            isSearchActive = false;

            return true;
        }

        if (isReturningToPatrolOrigin)
        {
            UpdateReturnToPatrolOrigin();
            return false;
        }

        twoPointPatrol.Update(
            enemyData.PatrolMoveSpeed,
            enemyData.PatrolTurnDuration);

        return false;
    }

    public bool TryDetectPlayer(
        out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;

        if (!CanDetectPlayer(
                out Vector3 detectedPlayerPosition))
        {
            return false;
        }

        detectedPlayerDirectionSign =
            GetDetectedPlayerDirectionSign(
                detectedPlayerPosition);

        return true;
    }

    public void HandleCollisionEnter(
        Collision collision)
    {
        if (!isSearchActive ||
            collision == null)
        {
            return;
        }

        if (!playerReference.IsPlayerCollider(
                collision.collider))
        {
            return;
        }

        if (!playerReference.TryGetAttackHitReceiver(
                requireActiveAndEnabled: false,
                out IAttackHitReceiver playerAttackHitReceiver,
                out _))
        {
            Debug.LogWarning(
                $"{nameof(OldDroneEnemySearchStrategy)}: " +
                "PlayerにIAttackHitReceiverの実装が見つかりません。",
                enemyObject);

            return;
        }

        float damage =
            enemyData.AttackPower *
            (enemyData.PatrolContactDamagePercent / 100f);

        EnemyAttackHitData attackHitData =
            new EnemyAttackHitData(
                enemyData,
                damage);

        playerAttackHitReceiver.ReceiveAttackHit(
            attackHitData);
    }

    public void EndSearch()
    {
        playerDetectionSensor.ClearDebugRay();

        isSearchActive = false;
        isReturningToPatrolOrigin = false;

        twoPointPatrol.End();

        pathNavigator.Reset();
        StopPlanarMovement();
    }

    public void CollectDebugRays(
        List<EnemyDebugRay> debugRays)
    {
        if (debugRays == null)
        {
            throw new ArgumentNullException(
                nameof(debugRays));
        }

        if (playerDetectionSensor.TryGetDebugRay(
                out EnemyDebugRay detectionDebugRay))
        {
            debugRays.Add(
                detectionDebugRay);
        }
    }

    /// <summary>
    /// Sceneビューの索敵範囲表示が実処理と同じ位置へ追従するため、
    /// 現在のDrone本体Collider中心をEditor描画側へ提供する。
    /// </summary>
    public bool TryGetSearchCenter(
        out Vector3 currentSearchCenter)
    {
        if (enemyBoxCollider == null ||
            !enemyBoxCollider.enabled ||
            enemyObject == null ||
            !enemyObject.activeInHierarchy)
        {
            currentSearchCenter = default;
            return false;
        }

        currentSearchCenter =
            GetCurrentDetectionCenter();

        return true;
    }

    /// <summary>
    /// Search開始時に、現在位置からUnity配置時の固定巡回原点へ戻る必要があるか判定する。
    /// </summary>
    private void BeginReturnToPatrolOrigin()
    {
        pathNavigator.Reset();

        if (pathNavigator.IsWithinArrivalTolerance(
                patrolOriginPosition))
        {
            CompleteReturnToPatrolOrigin();
            return;
        }

        isReturningToPatrolOrigin = true;

        if (pathNavigator.TryBeginPathToPosition(
                patrolOriginPosition))
        {
            return;
        }

        StopPlanarMovement();

        Debug.LogWarning(
            $"{nameof(OldDroneEnemySearchStrategy)}: " +
            "初期巡回位置までのA*経路を取得できませんでした。" +
            "帰還状態を維持し、一定間隔で再探索します。",
            enemyObject);
    }

    private void UpdateReturnToPatrolOrigin()
    {
        bool hasReachedPatrolOrigin =
            pathNavigator.UpdatePathToPosition(
                patrolOriginPosition);

        AlignVisualWithPlanarDirection(
            pathNavigator.RequestedMoveDirection);

        if (!hasReachedPatrolOrigin)
        {
            return;
        }

        CompleteReturnToPatrolOrigin();
    }

    private void CompleteReturnToPatrolOrigin()
    {
        StopPlanarMovement();
        pathNavigator.Reset();
        isReturningToPatrolOrigin = false;

        // 帰還後は固定巡回原点からPoint Aへ向かう通常巡回を開始する。
        twoPointPatrol.ResetToPointA();
    }

    private void StopPlanarMovement()
    {
        planarMotor?.StopPlanarMovement();
    }

    private bool IsBlockingMovementCollider(
        Collider targetCollider)
    {
        // NavigationObstacleMaskにPlayer Colliderが含まれる場合でも、
        // Droneの実移動ではPlayerを通行障害物として扱わない。
        return !playerReference.IsPlayerCollider(
            targetCollider);
    }

    private bool CanDetectPlayer(
        out Vector3 detectedPlayerPosition)
    {
        detectedPlayerPosition = default;

        if (!TryGetCurrentPlayerTargetPosition(
                out Vector3 playerPosition))
        {
            playerDetectionSensor.ClearDebugRay();
            return false;
        }

        Vector3 detectionCenter =
            GetCurrentDetectionCenter();

        if (!playerDetectionSensor.CanDetectPlayer(
                detectionCenter,
                playerPosition,
                detectionRadius,
                playerPosition,
                playerColliderEndsLineOfSight: true))
        {
            return false;
        }

        detectedPlayerPosition =
            playerPosition;

        return true;
    }

    private Vector3 GetCurrentDetectionCenter()
    {
        return enemyBoxCollider.bounds.center;
    }

    /// <summary>
    /// 索敵距離判定とLOS判定に使用するPlayer側の代表位置を取得する。
    /// Player Rootは所属判定とDamage Receiver探索に使用し、
    /// 索敵位置だけをPlayer Root直下のMid_ColliderのBounds中心へ統一する。
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
                    targetTransform.GetComponent<BoxCollider>();
            }
        }

        if (playerTargetCollider == null ||
            !playerTargetCollider.enabled ||
            !playerTargetCollider.gameObject.activeInHierarchy)
        {
            targetWorldPosition = default;
            return false;
        }

        targetWorldPosition =
            playerTargetCollider.bounds.center;

        return true;
    }

    private float GetDetectedPlayerDirectionSign(
        Vector3 detectedPlayerPosition)
    {
        float deltaX =
            detectedPlayerPosition.x -
            enemyRigidbody.position.x;

        if (Mathf.Abs(deltaX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                deltaX);
        }

        float navigationDirectionX =
            pathNavigator.RequestedMoveDirection.x;

        if (Mathf.Abs(navigationDirectionX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                navigationDirectionX);
        }

        float targetDeltaX =
            twoPointPatrol.CurrentTargetPosition.x -
            enemyRigidbody.position.x;

        if (Mathf.Abs(targetDeltaX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                targetDeltaX);
        }

        return 1f;
    }

    private void AlignVisualWithPlanarDirection(
        Vector2 planarDirection)
    {
        if (planarDirection.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return;
        }

        Vector3 worldDirection =
            new Vector3(
                planarDirection.x,
                planarDirection.y,
                0f).normalized;

        visualRoot.rotation =
            Quaternion.LookRotation(
                worldDirection,
                Vector3.up);
    }
}
