using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 新型ドローンの2点巡回・Player索敵・初期位置へのA*帰還を行う。
/// </summary>
/// <remarks>
/// 障害物を考慮した移動、巡回点補正、帰還経路探索、LOSは既存の共通クラスに委譲する。
/// Alert・Attackの動作、接触攻撃、State遷移は担当しない。
/// </remarks>
[Serializable]
public sealed class NewDroneEnemySearchStrategy :
    IEnemySearchStrategy,
    IEnemyDebugRayProvider
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";
    private const float DIRECTION_EPSILON = 0.001f;

    [Header("個体設定")]
    [SerializeField, Min(0f)]
    [Tooltip("新型ドローン個体の索敵半径。Colliderの最近接点で距離を測る。")]
    private float detectionRadius = 3f;

    [Header("巡回地点")]
    [SerializeField]
    [Tooltip("巡回地点A。A/Bとも未設定なら巡回せず待機・索敵する。")]
    private Transform patrolPointA;

    [SerializeField]
    [Tooltip("巡回地点B。巡回する場合はA/B両方設定する。")]
    private Transform patrolPointB;

    private NewDroneEnemyData enemyData;
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
    private Collider playerTargetCollider;

    private Vector3 patrolOriginPosition;
    private Quaternion initialVisualRootRotation;
    private bool isSearchActive;
    private bool isReturningToPatrolOrigin;
    private bool canPatrolInCurrentSearch;
    private bool isDetectionSuppressedAfterFailedShot;
    private bool hasPlayerLeftRangeAfterFailedShot;

    public float DetectionRadius => detectionRadius;

    /// <summary>
    /// 最後の検知時に確定したPlayer方向。VisualRootを向けるStateMachineが使用する。
    /// </summary>
    public Vector2 DetectedPlayerPlanarDirection { get; private set; }

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData =
            enemyData as NewDroneEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(NewDroneEnemySearchStrategy)}には{nameof(NewDroneEnemyData)}が必要です。");

        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));

        enemyTransform = enemyObject.transform;
        this.visualRoot = visualRoot
            ? visualRoot
            : throw new InvalidOperationException("新型DroneにはVisualRootが必要です。");

        enemyRigidbody = enemyObject.GetComponent<Rigidbody>();
        enemyBoxCollider = enemyObject.GetComponent<BoxCollider>();

        if (enemyRigidbody == null || enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException("新型Droneには非KinematicのRigidbodyが必要です。");
        }

        if (enemyBoxCollider == null || !enemyBoxCollider.enabled)
        {
            throw new InvalidOperationException("新型Drone本体には有効なBoxColliderが必要です。");
        }

        if (this.enemyData.NavigationObstacleMask.value == 0)
        {
            throw new InvalidOperationException("NewDroneEnemyDataのNavigation Obstacle MaskにStage障害物を設定してください。");
        }

        playerReference = new EnemyPlayerReference();
        playerDetectionSensor = new EnemyPlayerDetectionSensor(
            enemyTransform,
            playerReference);

        planarMotor = new DroneEnemyPlanarMotor(
            enemyRigidbody,
            enemyBoxCollider,
            this.enemyData.NavigationObstacleMask,
            IsBlockingMovementCollider);

        pathNavigator = new DroneEnemyPathNavigator(
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

        // 巡回なしでも帰還位置と初期のモデル向きが必要なので、巡回処理とは独立して保存する。
        patrolOriginPosition = enemyRigidbody.position;
        initialVisualRootRotation = this.visualRoot.rotation;

        if (DroneEnemyTwoPointPatrol.TryValidatePatrolPoints(
                patrolPointA,
                patrolPointB,
                out string failureReason))
        {
            twoPointPatrol = new DroneEnemyTwoPointPatrol(
                patrolPointA,
                patrolPointB,
                enemyTransform,
                this.visualRoot,
                enemyRigidbody,
                enemyBoxCollider,
                planarMotor,
                playerReference,
                this.enemyData.NavigationObstacleMask);
        }
        else
        {
            twoPointPatrol = null;

            if (patrolPointA != null || patrolPointB != null)
            {
                Debug.LogWarning(
                    $"{nameof(NewDroneEnemySearchStrategy)}: " +
                    $"{failureReason} 巡回を無効にし、索敵・攻撃を継続します。",
                    enemyObject);
            }
        }
        playerReference.TryGetPlayerTransform(out _);
    }

    public void BeginSearch()
    {
        playerDetectionSensor.ClearDebugRay();
        isSearchActive = true;
        isReturningToPatrolOrigin = false;
        canPatrolInCurrentSearch = false;
        pathNavigator.Reset();
        playerReference.TryGetPlayerTransform(out _);
        if (twoPointPatrol != null)
        {
            twoPointPatrol.ResetToPointA();

            if (twoPointPatrol.TryPrepareRoute(out string failureReason))
            {
                canPatrolInCurrentSearch = true;
            }
            else
            {
                planarMotor.StopPlanarMovement();
                Debug.LogWarning(
                    $"{nameof(NewDroneEnemySearchStrategy)}: " +
                    $"巡回経路を安全に確定できないため、巡回だけ停止します。{failureReason}",
                    enemyObject);
            }
        }

        // 巡回地点がなくても、攻撃後に初期位置へ帰還できるようにする。
        BeginReturnToPatrolOrigin();
    }

    public bool UpdateSearch(out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;
        if (!isSearchActive)
        {
            return false;
        }

        if (canPatrolInCurrentSearch)
        {
            twoPointPatrol.SyncPointTransforms();
        }

        // 帰還経路の追従中も、攻撃完了後と同じ検知条件を毎物理フレーム評価する。
        if (TryDetectPlayer(out detectedPlayerDirectionSign))
        {
            planarMotor.StopPlanarMovement();
            isSearchActive = false;
            return true;
        }

        if (isReturningToPatrolOrigin)
        {
            UpdateReturnToPatrolOrigin();
        }
        else if (canPatrolInCurrentSearch)
        {
            twoPointPatrol.Update(
                enemyData.PatrolMoveSpeed,
                enemyData.PatrolTurnDuration);
        }
        else
        {
            // 巡回なしは初期位置で待機する。索敵はこのメソッド内で継続する。
            planarMotor.StopPlanarMovement();
        }

        return false;
    }

    /// <summary>
    /// 射撃地点が見つからず帰還する場合、帰還完了とPlayerの検知範囲離脱まで再攻撃を禁止する。
    /// 探索失敗後、帰還開始直後に同じ攻撃を再試行し続けることを防ぐ。
    /// </summary>
    public void SuppressDetectionUntilReturnAndRangeExit()
    {
        isDetectionSuppressedAfterFailedShot = true;
        hasPlayerLeftRangeAfterFailedShot = false;
    }

    public bool TryDetectPlayer(out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;
        DetectedPlayerPlanarDirection = Vector2.zero;
        playerDetectionSensor.ClearDebugRay();

        if (!TryGetCurrentPlayerTargetPosition(out Vector3 playerTargetPosition))
        {
            if (isDetectionSuppressedAfterFailedShot)
            {
                hasPlayerLeftRangeAfterFailedShot = true;
            }

            playerDetectionSensor.ClearDebugRay();
            return false;
        }

        Vector3 detectionCenter = enemyBoxCollider.bounds.center;

        // Collider中心を半径内へ要求せず、Collider表面の最寄点までのXY距離で判定する。
        Vector3 closestPlayerPosition = playerTargetCollider.ClosestPoint(
            new Vector3(
                detectionCenter.x,
                detectionCenter.y,
                playerTargetPosition.z));

        // 距離判定はCollider表面。射線が遮られている場合は発見させない。
        Vector2 planarDistance = new Vector2(
            closestPlayerPosition.x - detectionCenter.x,
            closestPlayerPosition.y - detectionCenter.y);

        bool isPlayerWithinDetectionRange = detectionRadius > 0f &&
            planarDistance.sqrMagnitude <= detectionRadius * detectionRadius;

        if (isDetectionSuppressedAfterFailedShot)
        {
            if (!isPlayerWithinDetectionRange)
            {
                hasPlayerLeftRangeAfterFailedShot = true;
            }

            if (isReturningToPatrolOrigin || !hasPlayerLeftRangeAfterFailedShot)
            {
                return false;
            }

            isDetectionSuppressedAfterFailedShot = false;
        }

        if (!isPlayerWithinDetectionRange ||
            !playerDetectionSensor.CanDetectPlayer(
                detectionCenter,
                closestPlayerPosition,
                detectionRadius,
                playerTargetPosition,
                playerColliderEndsLineOfSight: true))
        {
            return false;
        }

        Vector3 planarDelta = playerTargetPosition - enemyRigidbody.position;
        DetectedPlayerPlanarDirection = new Vector2(planarDelta.x, planarDelta.y);
        detectedPlayerDirectionSign = GetDetectedPlayerDirectionSign(playerTargetPosition);
        return true;
    }

    public void HandleCollisionEnter(Collision collision)
    {
        // 新型Droneの巡回中はPlayerへの接触ダメージなし。
    }

    public void EndSearch()
    {
        isSearchActive = false;
        isReturningToPatrolOrigin = false;
        canPatrolInCurrentSearch = false;
        playerDetectionSensor.ClearDebugRay();
        twoPointPatrol?.End();
        pathNavigator.Reset();
        planarMotor.StopPlanarMovement();
    }

    public void CollectDebugRays(List<EnemyDebugRay> debugRays)
    {
        if (debugRays == null)
        {
            throw new ArgumentNullException(nameof(debugRays));
        }

        if (playerDetectionSensor.TryGetDebugRay(out EnemyDebugRay ray))
        {
            debugRays.Add(ray);
        }
    }

    public bool TryGetSearchCenter(out Vector3 currentSearchCenter)
    {
        if (enemyBoxCollider == null ||
            !enemyBoxCollider.enabled ||
            enemyObject == null ||
            !enemyObject.activeInHierarchy)
        {
            currentSearchCenter = default;
            return false;
        }

        currentSearchCenter = enemyBoxCollider.bounds.center;
        return true;
    }

    private void BeginReturnToPatrolOrigin()
    {
        pathNavigator.Reset();

        if (pathNavigator.IsWithinArrivalTolerance(patrolOriginPosition))
        {
            CompleteReturnToPatrolOrigin();
            return;
        }

        isReturningToPatrolOrigin = true;

        if (!pathNavigator.TryBeginPathToPosition(patrolOriginPosition))
        {
            planarMotor.StopPlanarMovement();
            Debug.LogWarning(
                $"{nameof(NewDroneEnemySearchStrategy)}: 初期位置への経路を取得できません。経路を再探索します。",
                enemyObject);
        }
    }

    private void UpdateReturnToPatrolOrigin()
    {
        bool hasArrived = pathNavigator.UpdatePathToPosition(patrolOriginPosition);
        AlignVisualWithPlanarDirection(pathNavigator.RequestedMoveDirection);
        if (hasArrived)
        {
            CompleteReturnToPatrolOrigin();
        }
    }

    private void CompleteReturnToPatrolOrigin()
    {
        planarMotor.StopPlanarMovement();
        pathNavigator.Reset();
        isReturningToPatrolOrigin = false;

        if (canPatrolInCurrentSearch)
        {
            twoPointPatrol.ResetToPointA();
        }
        else
        {
            // 巡回できない場合は、初期配置時の向きに戻して待機する。
            visualRoot.rotation = initialVisualRootRotation;
        }
    }

    private bool IsBlockingMovementCollider(Collider collider)
    {
        // 既存Droneと同じく、PlayerはStage障害物として扱わない。
        return !playerReference.IsPlayerCollider(collider);
    }

    private bool TryGetCurrentPlayerTargetPosition(out Vector3 targetWorldPosition)
    {
        if (!playerReference.TryGetPlayerTransform(out Transform playerTransform))
        {
            playerTargetCollider = null;
            targetWorldPosition = default;
            return false;
        }

        if (playerTargetCollider != null &&
            playerTargetCollider.transform != playerTransform &&
            !playerTargetCollider.transform.IsChildOf(playerTransform))
        {
            playerTargetCollider = null;
        }

        if (playerTargetCollider == null)
        {
            Transform targetTransform = playerTransform.Find(PLAYER_TARGET_COLLIDER_NAME);
            if (targetTransform != null)
            {
                playerTargetCollider = targetTransform.GetComponent<Collider>();
            }
        }

        if (playerTargetCollider == null ||
            !playerTargetCollider.enabled ||
            !playerTargetCollider.gameObject.activeInHierarchy)
        {
            targetWorldPosition = default;
            return false;
        }

        targetWorldPosition = playerTargetCollider.bounds.center;
        return true;
    }

    private float GetDetectedPlayerDirectionSign(Vector3 playerPosition)
    {
        float deltaX = playerPosition.x - enemyRigidbody.position.x;
        if (Mathf.Abs(deltaX) > DIRECTION_EPSILON)
        {
            return Mathf.Sign(deltaX);
        }

        float navigationX = pathNavigator.RequestedMoveDirection.x;
        if (Mathf.Abs(navigationX) > DIRECTION_EPSILON)
        {
            return Mathf.Sign(navigationX);
        }

        if (canPatrolInCurrentSearch)
        {
            float patrolDeltaX = twoPointPatrol.CurrentTargetPosition.x - enemyRigidbody.position.x;
            return Mathf.Abs(patrolDeltaX) > DIRECTION_EPSILON
                ? Mathf.Sign(patrolDeltaX)
                : 1f;
        }

        float initialFacingX = (initialVisualRootRotation * Vector3.forward).x;
        return Mathf.Abs(initialFacingX) > DIRECTION_EPSILON
            ? Mathf.Sign(initialFacingX)
            : 1f;
    }

    private void AlignVisualWithPlanarDirection(Vector2 direction)
    {
        if (direction.sqrMagnitude <= DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return;
        }

        visualRoot.rotation = Quaternion.LookRotation(
            new Vector3(direction.x, direction.y, 0f).normalized,
            Vector3.up);
    }
}
