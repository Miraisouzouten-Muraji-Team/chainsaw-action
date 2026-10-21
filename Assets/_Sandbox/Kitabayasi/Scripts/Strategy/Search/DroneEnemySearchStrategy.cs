using System;
using System.Collections.Generic;
using UnityEngine;

// 責務:
// ・ゲーム開始時のUnity配置位置を固定した巡回基準として保持し、Search復帰時はA*でそこへ帰還する。
// ・帰還経路を確保できない場合は、その場で停止し、一定間隔でA*を再試行する。
// ・固定した巡回基準を使ってXYゲームプレイ平面上の巡回地点A/Bを再構築して往復する。
// ・Drone本体のBoxCollider形状を使い、巡回地点と巡回経路が安全か検証・補正する。
// ・巡回端で停止し、VisualRootだけを指定時間かけて次の巡回地点へ向ける。
// ・現在のDrone本体に追従する検知範囲と、Drone→Player間のLOS RayからPlayer発見を判定する。
// ・初期位置への帰還中も索敵を継続し、Player発見時は通常のAlert遷移へ通知する。
// ・帰還中を含む索敵中にPlayerへ接触した際の攻撃情報を送信する。
//
// 担当しない責務:
// ・SearchからAlertへのState遷移そのもの。
// ・Alert / Attackの具体処理。
// ・PatrolPointA/BのEditor生成。
[Serializable]
public sealed class DroneEnemySearchStrategy :
    IEnemySearchStrategy,
    IEnemyDebugRayProvider
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";

    private const int BOX_CAST_HIT_BUFFER_SIZE = 32;
    private const int OVERLAP_BUFFER_SIZE = 32;
    private const int PATROL_CORRECTION_ITERATIONS = 12;

    private const float ARRIVAL_TOLERANCE = 0.05f;
    private const float DIRECTION_EPSILON = 0.001f;
    private const float PATROL_SAFETY_MARGIN = 0.02f;
    private const float PATH_RETRY_INTERVAL = 0.5f;

    [Header("巡回地点")]
    [Tooltip("巡回地点A。初期化時にEnemy基準のXYオフセットとして保存する。")]
    [SerializeField]
    private Transform patrolPointA;

    [Tooltip("巡回地点B。初期化時にEnemy基準のXYオフセットとして保存する。")]
    [SerializeField]
    private Transform patrolPointB;

    private DroneEnemyData enemyData;

    private GameObject enemyObject;
    private Transform enemyTransform;
    private Transform visualRoot;
    private Transform facingReference;
    private Rigidbody enemyRigidbody;
    private BoxCollider enemyBoxCollider;
    private DroneEnemyPlanarMotor planarMotor;

    private EnemyPlayerReference playerReference;
    private EnemyPlayerDetectionSensor playerDetectionSensor;
    private BoxCollider playerTargetCollider;

    private RaycastHit[] boxCastHitBuffer;
    private Collider[] overlapBuffer;
    private List<Vector3> returnPath;

    private Vector2 patrolPointAOffset;
    private Vector2 patrolPointBOffset;

    private Vector3 patrolOriginPosition;
    private Vector3 patrolPointAWorldPosition;
    private Vector3 patrolPointBWorldPosition;

    private Vector3 boxCenterOffsetFromRigidbody;
    private Vector3 boxHalfExtents;
    private Quaternion boxOrientation;

    private Vector3 navigationBoxHalfExtents;
    private int returnPathIndex;
    private Vector2 lastNavigationPosition;
    private float navigationStallElapsedTime;
    private float nextReturnPathRetryTime;

    private bool hasValidPatrolRoute;
    private bool hasPhysicsQueryBufferOverflow;
    private bool isMovingToPointB;
    private bool isTurning;
    private bool isSearchActive;
    private bool isReturningToPatrolOrigin;

    private float turnElapsedTime;

    private Quaternion turnStartRotation;
    private Quaternion turnTargetRotation;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot,
        Transform facingReference)
    {
        this.enemyData =
            enemyData as DroneEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}には" +
                $"{nameof(DroneEnemyData)}が必要です。");

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
                $"{nameof(DroneEnemySearchStrategy)}を使用するEnemyには" +
                "VisualRootが必要です。");

        this.facingReference =
            facingReference
            ? facingReference
            : throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}を使用するEnemyには" +
                "FacingReferenceが必要です。");

        enemyRigidbody =
            enemyObject.GetComponent<Rigidbody>();

        if (enemyRigidbody == null)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}を使用するEnemyには" +
                $"{nameof(Rigidbody)}が必要です。");
        }

        if (enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}は" +
                "非KinematicのRigidbodyを使用する前提です。");
        }

        // 巡回安全判定は実際のDrone本体形状を基準にするため、
        // 子階層や別形状Colliderへの暗黙フォールバックは行わない。
        enemyBoxCollider =
            enemyObject.GetComponent<BoxCollider>();

        if (enemyBoxCollider == null)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}を使用するEnemy本体には" +
                $"{nameof(BoxCollider)}が必要です。");
        }

        if (!enemyBoxCollider.enabled)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}を使用するEnemy本体の" +
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

        ValidatePatrolPoints();

        // 巡回原点はSearchへ入るたびに更新せず、
        // Unity上で配置されたゲーム開始時位置として固定する。
        patrolOriginPosition =
            enemyRigidbody.position;

        CacheConfiguredPatrolOffsets();

        boxCastHitBuffer =
            new RaycastHit[BOX_CAST_HIT_BUFFER_SIZE];

        overlapBuffer =
            new Collider[OVERLAP_BUFFER_SIZE];

        returnPath =
            new List<Vector3>();

        CacheBoxQueryGeometry();
        playerReference.TryGetPlayerTransform(
            out _);
    }

    public void BeginSearch()
    {
        playerDetectionSensor.ClearDebugRay();
        hasPhysicsQueryBufferOverflow = false;

        isSearchActive = true;
        isTurning = false;
        hasValidPatrolRoute = false;
        isReturningToPatrolOrigin = false;

        turnElapsedTime = 0f;
        returnPathIndex = 0;
        navigationStallElapsedTime = 0f;
        nextReturnPathRetryTime = 0f;

        returnPath.Clear();

        playerReference.TryGetPlayerTransform(
            out _);
        CacheBoxQueryGeometry();

        // 帰還完了後の巡回順は固定原点→A→B→A...で開始する。
        isMovingToPointB = false;

        if (!TryBuildPatrolRoute(
                out string failureReason))
        {
            StopPlanarMovement();

            Debug.LogWarning(
                $"{nameof(DroneEnemySearchStrategy)}: " +
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

        SyncPatrolPointTransforms();

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

        UpdatePatrol();

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
                $"{nameof(DroneEnemySearchStrategy)}: " +
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
        isTurning = false;
        hasValidPatrolRoute = false;
        isReturningToPatrolOrigin = false;

        returnPathIndex = 0;
        navigationStallElapsedTime = 0f;
        nextReturnPathRetryTime = 0f;
        returnPath.Clear();

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

    private void ValidatePatrolPoints()
    {
        if (patrolPointA == null ||
            patrolPointB == null)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}の" +
                "Patrol Point A/Bを設定してください。");
        }

        Vector2 pointA =
            new Vector2(
                patrolPointA.position.x,
                patrolPointA.position.y);

        Vector2 pointB =
            new Vector2(
                patrolPointB.position.x,
                patrolPointB.position.y);

        if ((pointB - pointA).sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}の" +
                "Patrol Point AとBは異なるXY位置に設定してください。");
        }
    }

    private void CacheConfiguredPatrolOffsets()
    {
        Vector3 initialEnemyPosition =
            enemyRigidbody.position;

        // Pointを子Transformとして保持していても、
        // Attack後のSearch再突入時には初期設定した巡回形状を再利用したいため、
        // ワールド座標そのものではなくEnemy基準のXY差分だけを保存する。
        patrolPointAOffset =
            new Vector2(
                patrolPointA.position.x -
                initialEnemyPosition.x,

                patrolPointA.position.y -
                initialEnemyPosition.y);

        patrolPointBOffset =
            new Vector2(
                patrolPointB.position.x -
                initialEnemyPosition.x,

                patrolPointB.position.y -
                initialEnemyPosition.y);
    }

    private void CacheBoxQueryGeometry()
    {
        Transform boxTransform =
            enemyBoxCollider.transform;

        Vector3 worldBoxCenter =
            boxTransform.TransformPoint(
                enemyBoxCollider.center);

        boxCenterOffsetFromRigidbody =
            worldBoxCenter -
            enemyRigidbody.position;

        Vector3 lossyScale =
            boxTransform.lossyScale;

        Vector3 absoluteScale =
            new Vector3(
                Mathf.Abs(lossyScale.x),
                Mathf.Abs(lossyScale.y),
                Mathf.Abs(lossyScale.z));

        boxHalfExtents =
            Vector3.Scale(
                enemyBoxCollider.size * 0.5f,
                absoluteScale);

        boxOrientation =
            boxTransform.rotation;

        navigationBoxHalfExtents =
            boxHalfExtents +
            Vector3.one * enemyData.PathSafetyMargin;
    }

    private bool TryBuildPatrolRoute(
        out string failureReason)
    {
        Vector3 candidateA =
            BuildPatrolCandidate(
                patrolPointAOffset);

        Vector3 candidateB =
            BuildPatrolCandidate(
                patrolPointBOffset);

        patrolPointAWorldPosition =
            candidateA;

        patrolPointBWorldPosition =
            candidateB;

        SyncPatrolPointTransforms();

        if (!IsBoxPlacementSafe(
                patrolOriginPosition))
        {
            failureReason =
                " Search開始位置でDrone本体のBoxCollider相当形状が" +
                "障害物と重なっています。" +
                GetBufferOverflowMessage();

            return false;
        }

        if (!TryResolveSafeDestinationAlongPath(
                patrolOriginPosition,
                candidateA,
                out Vector3 resolvedA))
        {
            failureReason =
                " Search開始位置→PatrolPointAの安全経路を確保できません。" +
                GetBufferOverflowMessage();

            return false;
        }

        patrolPointAWorldPosition =
            resolvedA;

        if (!TryResolveSafeDestinationAlongPath(
                resolvedA,
                candidateB,
                out Vector3 resolvedB))
        {
            patrolPointBWorldPosition =
                candidateB;

            SyncPatrolPointTransforms();

            failureReason =
                " PatrolPointA→PatrolPointBの安全経路を確保できません。" +
                GetBufferOverflowMessage();

            return false;
        }

        patrolPointBWorldPosition =
            resolvedB;

        if (!ValidateResolvedPatrolRoute())
        {
            SyncPatrolPointTransforms();

            failureReason =
                " 補正後の巡回地点・経路の最終検証に失敗しました。" +
                GetBufferOverflowMessage();

            return false;
        }

        SyncPatrolPointTransforms();

        hasValidPatrolRoute = true;
        failureReason = string.Empty;

        return true;
    }

    private Vector3 BuildPatrolCandidate(
        Vector2 configuredOffset)
    {
        // PatrolPoint A/Bは索敵半径とは独立した巡回設定として扱う。
        // PatrolPoint側のZは巡回形状として扱わず、
        // Searchへ入った瞬間のDrone深度を今回の固定深度にする。
        return new Vector3(
            patrolOriginPosition.x + configuredOffset.x,
            patrolOriginPosition.y + configuredOffset.y,
            patrolOriginPosition.z);
    }

    private bool TryResolveSafeDestinationAlongPath(
        Vector3 pathStart,
        Vector3 desiredDestination,
        out Vector3 resolvedDestination)
    {
        resolvedDestination =
            desiredDestination;

        Vector3 pathVector =
            desiredDestination -
            pathStart;

        float pathDistance =
            pathVector.magnitude;

        if (pathDistance <=
            ARRIVAL_TOLERANCE)
        {
            return false;
        }

        if (IsDestinationAndPathSafe(
                pathStart,
                desiredDestination))
        {
            return true;
        }

        float safeT = 0f;
        float blockedT = 1f;

        // 経路上で「ここまでは安全」を二分探索する。
        // 固定回数で必ず終了させ、障害物配置による無限補正を防ぐ。
        for (int i = 0;
             i < PATROL_CORRECTION_ITERATIONS;
             i++)
        {
            float testT =
                (safeT + blockedT) * 0.5f;

            Vector3 testDestination =
                Vector3.Lerp(
                    pathStart,
                    desiredDestination,
                    testT);

            if (IsDestinationAndPathSafe(
                    pathStart,
                    testDestination))
            {
                safeT = testT;
            }
            else
            {
                blockedT = testT;
            }
        }

        float safeDistance =
            pathDistance * safeT -
            PATROL_SAFETY_MARGIN;

        if (safeDistance <=
            ARRIVAL_TOLERANCE)
        {
            return false;
        }

        resolvedDestination =
            pathStart +
            pathVector.normalized *
            safeDistance;

        resolvedDestination.z =
            patrolOriginPosition.z;

        return IsDestinationAndPathSafe(
            pathStart,
            resolvedDestination);
    }

    private bool ValidateResolvedPatrolRoute()
    {
        if ((ToPlanarPosition(
                    patrolPointAWorldPosition) -
                ToPlanarPosition(
                    patrolOriginPosition)).magnitude <=
            ARRIVAL_TOLERANCE)
        {
            return false;
        }

        if ((ToPlanarPosition(
                    patrolPointBWorldPosition) -
                ToPlanarPosition(
                    patrolPointAWorldPosition)).magnitude <=
            ARRIVAL_TOLERANCE)
        {
            return false;
        }

        return
            IsBoxPlacementSafe(
                patrolPointAWorldPosition) &&
            IsBoxPlacementSafe(
                patrolPointBWorldPosition) &&
            IsBoxPathClear(
                patrolOriginPosition,
                patrolPointAWorldPosition) &&
            IsBoxPathClear(
                patrolPointAWorldPosition,
                patrolPointBWorldPosition);
    }

    private bool IsDestinationAndPathSafe(
        Vector3 pathStart,
        Vector3 destination)
    {
        return
            IsBoxPlacementSafe(
                destination) &&
            IsBoxPathClear(
                pathStart,
                destination);
    }

    private bool IsBoxPlacementSafe(
        Vector3 bodyPosition)
    {
        Vector3 queryCenter =
            GetBoxCenterAtBodyPosition(
                bodyPosition);

        int overlapCount =
            Physics.OverlapBoxNonAlloc(
                queryCenter,
                boxHalfExtents,
                overlapBuffer,
                boxOrientation,
                enemyData.NavigationObstacleMask,
                QueryTriggerInteraction.Ignore);

        if (overlapCount >=
            overlapBuffer.Length)
        {
            hasPhysicsQueryBufferOverflow = true;
            return false;
        }

        for (int i = 0;
             i < overlapCount;
             i++)
        {
            Collider overlappedCollider =
                overlapBuffer[i];

            if (overlappedCollider == null ||
                IsIgnoredPatrolObstacle(
                    overlappedCollider))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private bool IsBoxPathClear(
        Vector3 pathStart,
        Vector3 pathEnd)
    {
        Vector3 pathVector =
            pathEnd -
            pathStart;

        float pathDistance =
            pathVector.magnitude;

        if (pathDistance <=
            DIRECTION_EPSILON)
        {
            return true;
        }

        Vector3 pathDirection =
            pathVector /
            pathDistance;

        Vector3 castCenter =
            GetBoxCenterAtBodyPosition(
                pathStart);

        int hitCount =
            Physics.BoxCastNonAlloc(
                castCenter,
                boxHalfExtents,
                pathDirection,
                boxCastHitBuffer,
                boxOrientation,
                pathDistance,
                enemyData.NavigationObstacleMask,
                QueryTriggerInteraction.Ignore);

        if (hitCount >=
            boxCastHitBuffer.Length)
        {
            hasPhysicsQueryBufferOverflow = true;
            return false;
        }

        for (int i = 0;
             i < hitCount;
             i++)
        {
            Collider hitCollider =
                boxCastHitBuffer[i].collider;

            if (hitCollider == null ||
                IsIgnoredPatrolObstacle(
                    hitCollider))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private Vector3 GetBoxCenterAtBodyPosition(
        Vector3 bodyPosition)
    {
        return
            bodyPosition +
            boxCenterOffsetFromRigidbody;
    }

    private bool IsIgnoredPatrolObstacle(
        Collider targetCollider)
    {
        return
            IsEnemyOwnedCollider(
                targetCollider) ||
            playerReference.IsPlayerCollider(
                targetCollider);
    }

    private bool IsBlockingMovementCollider(
        Collider targetCollider)
    {
        // 現在のNavigationObstacleMaskにはPlayer Colliderも含まれ得るため、
        // 実移動の侵入防止ではPlayerだけを除外する。
        // Enemy自身のColliderはDroneEnemyPlanarMotor側で除外する。
        return !playerReference.IsPlayerCollider(
            targetCollider);
    }

    private string GetBufferOverflowMessage()
    {
        return hasPhysicsQueryBufferOverflow
            ? " Physicsクエリ結果が内部バッファ上限に達したため、安全側へ失敗扱いにしています。"
            : string.Empty;
    }

    private void SyncPatrolPointTransforms()
    {
        if (patrolPointA != null)
        {
            patrolPointA.position =
                patrolPointAWorldPosition;
        }

        if (patrolPointB != null)
        {
            patrolPointB.position =
                patrolPointBWorldPosition;
        }
    }

    /// <summary>
    /// Search開始時に、現在位置からUnity配置時の固定巡回原点へ戻る必要があるか判定する。
    /// </summary>
    private void BeginReturnToPatrolOrigin()
    {
        returnPath.Clear();
        returnPathIndex = 0;
        navigationStallElapsedTime = 0f;
        nextReturnPathRetryTime = 0f;

        Vector2 toOrigin =
            ToPlanarPosition(
                patrolOriginPosition) -
            GetCurrentPlanarPosition();

        if (toOrigin.magnitude <=
            enemyData.WaypointArrivalTolerance)
        {
            CompleteReturnToPatrolOrigin();
            return;
        }

        isReturningToPatrolOrigin = true;
        lastNavigationPosition =
            GetCurrentPlanarPosition();

        if (TryRefreshReturnPath())
        {
            return;
        }

        StopPlanarMovement();

        Debug.LogWarning(
            $"{nameof(DroneEnemySearchStrategy)}: " +
            "初期巡回位置までのA*経路を取得できませんでした。" +
            $"帰還状態を維持し、{PATH_RETRY_INTERVAL:0.##}秒間隔で再探索します。",
            enemyObject);
    }

    private void UpdateReturnToPatrolOrigin()
    {
        Vector2 toOrigin =
            ToPlanarPosition(
                patrolOriginPosition) -
            GetCurrentPlanarPosition();

        if (toOrigin.magnitude <=
            enemyData.WaypointArrivalTolerance)
        {
            CompleteReturnToPatrolOrigin();
            return;
        }

        if (returnPathIndex >=
            returnPath.Count)
        {
            TryRetryReturnPath();
            return;
        }

        AdvanceReachedReturnWaypoints();

        if (returnPathIndex >=
            returnPath.Count)
        {
            TryRetryReturnPath();
            return;
        }

        Vector3 currentBodyPosition =
            enemyRigidbody.position;

        Vector3 currentWaypoint =
            returnPath[returnPathIndex];

        if (!IsReturnPathSegmentAvailable(
                currentBodyPosition,
                currentWaypoint))
        {
            InvalidateReturnPath();
            TryRetryReturnPath();
            return;
        }

        if (HasNavigationStalled())
        {
            InvalidateReturnPath();
            TryRetryReturnPath();
            return;
        }

        MoveTowardNavigationWaypoint(
            currentWaypoint);
    }

    /// <summary>
    /// 通常Clearance内では従来の通行判定を使い、
    /// StartだけSafety Marginへ接触している場合は最初のClear Waypointまで実Box相当で脱出する。
    /// </summary>
    private bool IsReturnPathSegmentAvailable(
        Vector3 currentBodyPosition,
        Vector3 currentWaypoint)
    {
        if (!PlanarAStarPathfinder.IsPositionClear(
                currentWaypoint,
                boxCenterOffsetFromRigidbody,
                navigationBoxHalfExtents,
                boxOrientation,
                enemyData.NavigationObstacleMask))
        {
            return false;
        }

        bool isCurrentPositionNavigationClear =
            PlanarAStarPathfinder.IsPositionClear(
                currentBodyPosition,
                boxCenterOffsetFromRigidbody,
                navigationBoxHalfExtents,
                boxOrientation,
                enemyData.NavigationObstacleMask);

        if (isCurrentPositionNavigationClear)
        {
            return PlanarAStarPathfinder.IsTraversalClear(
                currentBodyPosition,
                currentWaypoint,
                boxCenterOffsetFromRigidbody,
                navigationBoxHalfExtents,
                boxOrientation,
                enemyData.NavigationObstacleMask);
        }

        return PlanarAStarPathfinder.IsStartEscapeTraversalAvailable(
            currentBodyPosition,
            currentWaypoint,
            boxCenterOffsetFromRigidbody,
            boxHalfExtents,
            navigationBoxHalfExtents,
            boxOrientation,
            enemyData.NavigationObstacleMask);
    }

    private bool TryRefreshReturnPath()
    {
        bool hasPath =
            PlanarAStarPathfinder.TryFindPath(
                enemyRigidbody.position,
                patrolOriginPosition,
                boxCenterOffsetFromRigidbody,
                navigationBoxHalfExtents,
                boxHalfExtents,
                boxOrientation,
                enemyData.PathSearchPadding,
                enemyData.MaximumExpandedPathNodes,
                enemyData.NavigationObstacleMask,
                returnPath);

        nextReturnPathRetryTime =
            Time.time +
            PATH_RETRY_INTERVAL;

        returnPathIndex = 0;
        navigationStallElapsedTime = 0f;
        lastNavigationPosition =
            GetCurrentPlanarPosition();

        return hasPath &&
               returnPath.Count > 0;
    }

    private void TryRetryReturnPath()
    {
        StopPlanarMovement();

        if (Time.time <
            nextReturnPathRetryTime)
        {
            return;
        }

        TryRefreshReturnPath();
    }

    private void InvalidateReturnPath()
    {
        returnPath.Clear();
        returnPathIndex = 0;
        navigationStallElapsedTime = 0f;

        nextReturnPathRetryTime =
            Mathf.Max(
                nextReturnPathRetryTime,
                Time.time);

        StopPlanarMovement();
    }

    private void AdvanceReachedReturnWaypoints()
    {
        while (returnPathIndex <
               returnPath.Count)
        {
            Vector2 toWaypoint =
                ToPlanarPosition(
                    returnPath[returnPathIndex]) -
                GetCurrentPlanarPosition();

            if (toWaypoint.magnitude >
                enemyData.WaypointArrivalTolerance)
            {
                break;
            }

            returnPathIndex++;
            navigationStallElapsedTime = 0f;
            lastNavigationPosition =
                GetCurrentPlanarPosition();
        }
    }

    private void MoveTowardNavigationWaypoint(
        Vector3 waypoint)
    {
        float moveSpeed =
            enemyData.NavigationMoveSpeed;

        float fixedDeltaTime =
            Time.fixedDeltaTime;

        if (moveSpeed <=
                DIRECTION_EPSILON ||
            fixedDeltaTime <=
                DIRECTION_EPSILON)
        {
            StopPlanarMovement();
            return;
        }

        Vector2 toWaypoint =
            ToPlanarPosition(
                waypoint) -
            GetCurrentPlanarPosition();

        float remainingDistance =
            toWaypoint.magnitude;

        float expectedMovementDistance =
            moveSpeed * fixedDeltaTime;

        Vector2 targetVelocity =
            remainingDistance <=
            expectedMovementDistance
                ? toWaypoint / fixedDeltaTime
                : toWaypoint.normalized * moveSpeed;

        SetPlanarVelocity(
            targetVelocity);
    }

    private bool HasNavigationStalled()
    {
        float detectionDuration =
            enemyData.NavigationStallDetectionDuration;

        float minimumMoveSpeed =
            enemyData.NavigationStallMinimumMoveSpeed;

        Vector2 currentPosition =
            GetCurrentPlanarPosition();

        if (detectionDuration <=
                DIRECTION_EPSILON ||
            minimumMoveSpeed <=
                DIRECTION_EPSILON)
        {
            lastNavigationPosition =
                currentPosition;

            navigationStallElapsedTime = 0f;
            return false;
        }

        float movedDistance =
            Vector2.Distance(
                currentPosition,
                lastNavigationPosition);

        lastNavigationPosition =
            currentPosition;

        float minimumMoveDistance =
            minimumMoveSpeed *
            Time.fixedDeltaTime;

        if (movedDistance >
            minimumMoveDistance)
        {
            navigationStallElapsedTime = 0f;
            return false;
        }

        navigationStallElapsedTime +=
            Time.fixedDeltaTime;

        return navigationStallElapsedTime >=
               detectionDuration;
    }

    private void CompleteReturnToPatrolOrigin()
    {
        StopPlanarMovement();

        isReturningToPatrolOrigin = false;
        returnPathIndex = 0;
        navigationStallElapsedTime = 0f;
        nextReturnPathRetryTime = 0f;
        returnPath.Clear();

        // 帰還後は固定巡回原点からPoint Aへ向かう通常巡回を開始する。
        isMovingToPointB = false;
    }

    private void UpdatePatrol()
    {
        if (!hasValidPatrolRoute)
        {
            StopPlanarMovement();
            return;
        }

        if (isTurning)
        {
            UpdateTurn();
            return;
        }

        float moveSpeed =
            enemyData.PatrolMoveSpeed;

        if (moveSpeed <= 0f)
        {
            StopPlanarMovement();
            return;
        }

        Vector2 toTarget =
            ToPlanarPosition(
                GetCurrentPatrolTargetPosition()) -
            GetCurrentPlanarPosition();

        float remainingDistance =
            toTarget.magnitude;

        if (remainingDistance <=
            ARRIVAL_TOLERANCE)
        {
            BeginTurn();
            return;
        }

        float fixedDeltaTime =
            Time.fixedDeltaTime;

        if (fixedDeltaTime <= 0f)
        {
            StopPlanarMovement();
            return;
        }

        float expectedMovementDistance =
            moveSpeed * fixedDeltaTime;

        Vector2 targetVelocity =
            remainingDistance <= expectedMovementDistance
                ? toTarget / fixedDeltaTime
                : toTarget.normalized * moveSpeed;

        SetPlanarVelocity(
            targetVelocity);
    }

    private Vector3 GetCurrentPatrolTargetPosition()
    {
        return isMovingToPointB
            ? patrolPointBWorldPosition
            : patrolPointAWorldPosition;
    }

    private void BeginTurn()
    {
        StopPlanarMovement();

        turnElapsedTime = 0f;

        turnStartRotation =
            visualRoot.rotation;

        // 現在到着した巡回地点ではなく、
        // 振り返り完了後に向かう次の巡回地点を正面にする。
        turnTargetRotation =
            CalculatePatrolFacingRotation(
                GetNextPatrolTargetPosition());

        isTurning = true;

        if (enemyData.PatrolTurnDuration <= 0f)
        {
            CompleteTurnImmediately();
        }
    }

    private Vector3 GetNextPatrolTargetPosition()
    {
        return isMovingToPointB
            ? patrolPointAWorldPosition
            : patrolPointBWorldPosition;
    }

    private Quaternion CalculatePatrolFacingRotation(
        Vector3 targetPosition)
    {
        Vector2 toTarget =
            ToPlanarPosition(
                targetPosition) -
            GetCurrentPlanarPosition();

        if (toTarget.sqrMagnitude <=
            DIRECTION_EPSILON *
            DIRECTION_EPSILON)
        {
            return visualRoot.rotation;
        }

        Vector3 targetDirection =
            new Vector3(
                toTarget.x,
                toTarget.y,
                0f).normalized;

        // モデル固有の正面軸ではなくFacingReferenceを次の巡回地点へ向ける。
        // FacingReferenceとVisualRootの現在の相対回転を維持することで、
        // モデルごとの初期向きの違いをStrategyへ持ち込まない。
        Quaternion targetFacingRotation =
            Quaternion.LookRotation(
                targetDirection,
                Vector3.up);

        Quaternion facingRelativeToVisual =
            Quaternion.Inverse(
                visualRoot.rotation) *
            facingReference.rotation;

        return targetFacingRotation *
               Quaternion.Inverse(
                   facingRelativeToVisual);
    }

    private void UpdateTurn()
    {
        StopPlanarMovement();

        float turnDuration =
            enemyData.PatrolTurnDuration;

        if (turnDuration <= 0f)
        {
            CompleteTurnImmediately();
            return;
        }

        turnElapsedTime +=
            Time.fixedDeltaTime;

        float progress =
            Mathf.Clamp01(
                turnElapsedTime /
                turnDuration);

        visualRoot.rotation =
            Quaternion.Slerp(
                turnStartRotation,
                turnTargetRotation,
                progress);

        if (progress >= 1f)
        {
            CompleteTurn();
        }
    }

    private void CompleteTurnImmediately()
    {
        visualRoot.rotation =
            turnTargetRotation;

        CompleteTurn();
    }

    private void CompleteTurn()
    {
        isTurning = false;

        isMovingToPointB =
            !isMovingToPointB;
    }

    private void SetPlanarVelocity(
        Vector2 targetVelocity)
    {
        planarMotor.TrySetPlanarVelocity(
            targetVelocity);
    }

    private void StopPlanarMovement()
    {
        planarMotor?.StopPlanarMovement();
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
                enemyData.DetectionRadius,
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

        float targetDeltaX =
            GetCurrentPatrolTargetPosition().x -
            enemyRigidbody.position.x;

        if (Mathf.Abs(targetDeltaX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                targetDeltaX);
        }

        return GetFacingHorizontalDirectionSign();
    }

    private Vector2 GetCurrentPlanarPosition()
    {
        Vector3 position =
            enemyRigidbody.position;

        return new Vector2(
            position.x,
            position.y);
    }

    private static Vector2 ToPlanarPosition(
        Vector3 position)
    {
        return new Vector2(
            position.x,
            position.y);
    }

    private bool IsEnemyOwnedCollider(
        Collider targetCollider)
    {
        Transform targetTransform =
            targetCollider.transform;

        return
            targetTransform == enemyTransform ||
            targetTransform.IsChildOf(
                enemyTransform);
    }

    private float GetFacingHorizontalDirectionSign()
    {
        float facingX =
            facingReference.forward.x;

        if (Mathf.Abs(facingX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                facingX);
        }

        Debug.LogWarning(
            $"{nameof(DroneEnemySearchStrategy)}: " +
            "FacingReference.forwardがX方向を向いていないため" +
            "+X方向を使用します。",
            facingReference);

        return 1f;
    }
}
