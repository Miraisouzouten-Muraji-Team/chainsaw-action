using System;
using System.Collections.Generic;
using UnityEngine;

// 責務:
// ・Search開始位置を基準に、XYゲームプレイ平面上の巡回地点A/Bを再配置して往復する。
// ・Drone本体のBoxCollider形状を使い、巡回地点と巡回経路が安全か検証・補正する。
// ・巡回端で停止し、VisualRootだけを指定時間かけて左右反転する。
// ・固定したSearchCenterの検知範囲と、現在のDrone→Player間のLOS RayからPlayer発見を判定する。
// ・索敵中にPlayerへ接触した際の攻撃情報を送信する。
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
    private const string PLAYER_TAG = "Player";

    private const int RAYCAST_HIT_BUFFER_SIZE = 32;
    private const int BOX_CAST_HIT_BUFFER_SIZE = 32;
    private const int OVERLAP_BUFFER_SIZE = 32;
    private const int PATROL_CORRECTION_ITERATIONS = 12;

    private const float ARRIVAL_TOLERANCE = 0.05f;
    private const float DIRECTION_EPSILON = 0.001f;
    private const float PATROL_SAFETY_MARGIN = 0.02f;

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
    private Rigidbody enemyRigidbody;
    private BoxCollider enemyBoxCollider;

    private Transform playerTransform;
    private IAttackHitReceiver playerAttackHitReceiver;

    private RaycastHit[] raycastHitBuffer;
    private RaycastHit[] boxCastHitBuffer;
    private Collider[] overlapBuffer;

    private EnemyDebugRay obstacleCheckDebugRay;
    private bool hasObstacleCheckDebugRay;

    private Vector2 patrolPointAOffset;
    private Vector2 patrolPointBOffset;

    private Vector3 searchCenter;
    private Vector3 patrolPointAWorldPosition;
    private Vector3 patrolPointBWorldPosition;

    private Vector3 boxCenterOffsetFromRigidbody;
    private Vector3 boxHalfExtents;
    private Quaternion boxOrientation;

    private bool hasSearchCenter;
    private bool hasValidPatrolRoute;
    private bool hasPhysicsQueryBufferOverflow;
    private bool isMovingToPointB;
    private bool isTurning;
    private bool isSearchActive;

    private float turnElapsedTime;

    private Quaternion turnStartRotation;
    private Quaternion turnTargetRotation;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
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

        ValidatePatrolPoints();
        CacheConfiguredPatrolOffsets();

        raycastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];

        boxCastHitBuffer =
            new RaycastHit[BOX_CAST_HIT_BUFFER_SIZE];

        overlapBuffer =
            new Collider[OVERLAP_BUFFER_SIZE];

        ResolvePlayerReferences();
    }

    public void BeginSearch()
    {
        hasObstacleCheckDebugRay = false;
        hasPhysicsQueryBufferOverflow = false;

        isSearchActive = true;
        isTurning = false;
        hasValidPatrolRoute = false;

        turnElapsedTime = 0f;

        // Attack等でDroneが移動した後も同じ巡回形状を使えるよう、
        // Searchへ入った瞬間の位置を今回の固定基準として取り直す。
        searchCenter =
            enemyRigidbody.position;

        hasSearchCenter = true;

        ResolvePlayerReferences();
        CacheBoxQueryGeometry();

        // 巡回順はSearchCenter→A→B→A...で固定する。
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
        }

        ResolvePlayerReferences();
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

        if (CanDetectPlayer())
        {
            detectedPlayerDirectionSign =
                GetDetectedPlayerDirectionSign();

            StopPlanarMovement();

            // State遷移要求後にSearchが一時的に残っても、
            // 巡回・接触攻撃を続けない。
            isSearchActive = false;

            return true;
        }

        UpdatePatrol();

        return false;
    }

    public void HandleCollisionEnter(
        Collision collision)
    {
        if (!isSearchActive ||
            collision == null)
        {
            return;
        }

        ResolvePlayerReferences();

        if (playerTransform == null ||
            !IsPlayerCollider(collision.collider))
        {
            return;
        }

        if (playerAttackHitReceiver == null)
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
        hasObstacleCheckDebugRay = false;

        isSearchActive = false;
        isTurning = false;
        hasValidPatrolRoute = false;

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

        if (hasObstacleCheckDebugRay)
        {
            debugRays.Add(
                obstacleCheckDebugRay);
        }
    }

    /// <summary>
    /// Sceneビューの索敵範囲表示が実処理と同じ固定中心を使うため、
    /// 現在のSearchCenterをEditor描画側へ提供する。
    /// </summary>
    public bool TryGetSearchCenter(
        out Vector3 currentSearchCenter)
    {
        currentSearchCenter =
            searchCenter;

        return hasSearchCenter;
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
    }

    private bool TryBuildPatrolRoute(
        out string failureReason)
    {
        float detectionRadius =
            enemyData.DetectionRadius;

        if (detectionRadius <=
            DIRECTION_EPSILON)
        {
            failureReason =
                " DetectionRadiusが0以下です。";

            return false;
        }

        Vector3 candidateA =
            BuildPatrolCandidate(
                patrolPointAOffset,
                detectionRadius);

        Vector3 candidateB =
            BuildPatrolCandidate(
                patrolPointBOffset,
                detectionRadius);

        patrolPointAWorldPosition =
            candidateA;

        patrolPointBWorldPosition =
            candidateB;

        SyncPatrolPointTransforms();

        if (!IsBoxPlacementSafe(
                searchCenter))
        {
            failureReason =
                " Search開始位置でDrone本体のBoxCollider相当形状が" +
                "障害物と重なっています。" +
                GetBufferOverflowMessage();

            return false;
        }

        if (!TryResolveSafeDestinationAlongPath(
                searchCenter,
                candidateA,
                out Vector3 resolvedA))
        {
            failureReason =
                " SearchCenter→PatrolPointAの安全経路を確保できません。" +
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
        Vector2 configuredOffset,
        float detectionRadius)
    {
        Vector2 planarCandidate =
            new Vector2(
                searchCenter.x +
                configuredOffset.x,

                searchCenter.y +
                configuredOffset.y);

        Vector2 center =
            new Vector2(
                searchCenter.x,
                searchCenter.y);

        Vector2 fromCenter =
            planarCandidate -
            center;

        float squaredRadius =
            detectionRadius *
            detectionRadius;

        if (fromCenter.sqrMagnitude >
            squaredRadius)
        {
            planarCandidate =
                center +
                fromCenter.normalized *
                detectionRadius;
        }

        // PatrolPoint側のZは巡回形状として扱わず、
        // Searchへ入った瞬間のDrone深度を今回の固定深度にする。
        return new Vector3(
            planarCandidate.x,
            planarCandidate.y,
            searchCenter.z);
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
            searchCenter.z;

        return
            IsPositionWithinDetectionRadius(
                resolvedDestination) &&
            IsDestinationAndPathSafe(
                pathStart,
                resolvedDestination);
    }

    private bool ValidateResolvedPatrolRoute()
    {
        if (!IsPositionWithinDetectionRadius(
                patrolPointAWorldPosition) ||
            !IsPositionWithinDetectionRadius(
                patrolPointBWorldPosition))
        {
            return false;
        }

        if ((ToPlanarPosition(
                    patrolPointAWorldPosition) -
                ToPlanarPosition(
                    searchCenter)).magnitude <=
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
                searchCenter,
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
                Physics.DefaultRaycastLayers,
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
                Physics.DefaultRaycastLayers,
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

    private bool IsPositionWithinDetectionRadius(
        Vector3 position)
    {
        Vector2 delta =
            ToPlanarPosition(position) -
            ToPlanarPosition(searchCenter);

        float radius =
            enemyData.DetectionRadius;

        return
            delta.sqrMagnitude <=
            radius * radius +
            DIRECTION_EPSILON *
            DIRECTION_EPSILON;
    }

    private bool IsIgnoredPatrolObstacle(
        Collider targetCollider)
    {
        return
            IsEnemyOwnedCollider(
                targetCollider) ||
            IsPlayerCollider(
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

        // Rigidbody / Colliderは回転させず、
        // 見た目だけを左右反転する。
        turnTargetRotation =
            Quaternion.AngleAxis(
                180f,
                Vector3.up) *
            turnStartRotation;

        isTurning = true;

        if (enemyData.PatrolTurnDuration <= 0f)
        {
            CompleteTurnImmediately();
        }
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
        Vector3 currentVelocity =
            enemyRigidbody.linearVelocity;

        Vector3 velocityDifference =
            new Vector3(
                targetVelocity.x -
                currentVelocity.x,

                targetVelocity.y -
                currentVelocity.y,

                -currentVelocity.z);

        if (velocityDifference.sqrMagnitude <=
            DIRECTION_EPSILON *
            DIRECTION_EPSILON)
        {
            return;
        }

        // Zは奥行き方向であり巡回軸ではないため、
        // Search中に外力で生じたZ速度も打ち消してXY移動へ戻す。
        enemyRigidbody.AddForce(
            velocityDifference,
            ForceMode.VelocityChange);
    }

    private void StopPlanarMovement()
    {
        if (enemyRigidbody == null)
        {
            return;
        }

        Vector3 currentVelocity =
            enemyRigidbody.linearVelocity;

        Vector3 velocityDifference =
            new Vector3(
                -currentVelocity.x,
                -currentVelocity.y,
                -currentVelocity.z);

        if (velocityDifference.sqrMagnitude <=
            DIRECTION_EPSILON *
            DIRECTION_EPSILON)
        {
            return;
        }

        enemyRigidbody.AddForce(
            velocityDifference,
            ForceMode.VelocityChange);
    }

    private bool CanDetectPlayer()
    {
        hasObstacleCheckDebugRay = false;

        ResolvePlayerReferences();

        if (!hasSearchCenter ||
            playerTransform == null)
        {
            return false;
        }

        // DetectionRadiusの中心は現在のDroneではなく、
        // このSearchへ入った瞬間に固定したSearchCenterを使用する。
        Vector3 playerPosition =
            playerTransform.position;

        float deltaX =
            playerPosition.x -
            searchCenter.x;

        float deltaY =
            playerPosition.y -
            searchCenter.y;

        float squaredDistance =
            deltaX * deltaX +
            deltaY * deltaY;

        float detectionRadius =
            enemyData.DetectionRadius;

        if (squaredDistance >
            detectionRadius * detectionRadius)
        {
            return false;
        }

        return HasDirectLineOfSightToPlayer();
    }

    private bool HasDirectLineOfSightToPlayer()
    {
        Vector3 rayOrigin =
            enemyBoxCollider.bounds.center;

        // Playerの位置は毎回Transformから読み、
        // 巡回中にPlayerが動いてもLOSを継続再評価する。
        Vector3 playerPosition =
            playerTransform.position;

        Vector3 rayVector =
            playerPosition -
            rayOrigin;

        float rayDistance =
            rayVector.magnitude;

        if (rayDistance <=
            DIRECTION_EPSILON)
        {
            return true;
        }

        Vector3 rayDirection =
            rayVector /
            rayDistance;

        int hitCount =
            Physics.RaycastNonAlloc(
                rayOrigin,
                rayDirection,
                raycastHitBuffer,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

        // バッファが満杯の場合、RaycastNonAllocでは最近傍Hitが
        // 結果に含まれる保証がないため、誤検知を避けて失敗扱いにする。
        if (hitCount >=
            raycastHitBuffer.Length)
        {
            obstacleCheckDebugRay =
                new EnemyDebugRay(
                    rayOrigin,
                    rayDirection,
                    rayDistance,
                    EnemyDebugRayKind.LineOfSight,
                    EnemyDebugRayResult.Blocked);

            hasObstacleCheckDebugRay = true;

            return false;
        }

        Collider firstValidHitCollider =
            null;

        float firstValidHitDistance =
            float.PositiveInfinity;

        // RaycastNonAllocの結果順序は保証されないため、
        // Enemy自身を除いたHitのうち最短距離を明示的に選ぶ。
        for (int i = 0;
             i < hitCount;
             i++)
        {
            RaycastHit hit =
                raycastHitBuffer[i];

            Collider hitCollider =
                hit.collider;

            if (hitCollider == null ||
                IsEnemyOwnedCollider(
                    hitCollider) ||
                hit.distance >=
                firstValidHitDistance)
            {
                continue;
            }

            firstValidHitCollider =
                hitCollider;

            firstValidHitDistance =
                hit.distance;
        }

        bool hasDetectedPlayer =
            firstValidHitCollider != null &&
            IsPlayerCollider(
                firstValidHitCollider);

        obstacleCheckDebugRay =
            new EnemyDebugRay(
                rayOrigin,
                rayDirection,
                rayDistance,
                EnemyDebugRayKind.LineOfSight,
                hasDetectedPlayer
                    ? EnemyDebugRayResult.Success
                    : EnemyDebugRayResult.Blocked);

        hasObstacleCheckDebugRay =
            true;

        return hasDetectedPlayer;
    }

    private float GetDetectedPlayerDirectionSign()
    {
        float deltaX =
            playerTransform.position.x -
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

        return GetVisualHorizontalDirectionSign(
            visualRoot);
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

    private bool IsPlayerCollider(
        Collider targetCollider)
    {
        if (targetCollider == null ||
            playerTransform == null)
        {
            return false;
        }

        Transform targetTransform =
            targetCollider.transform;

        return
            targetTransform == playerTransform ||
            targetTransform.IsChildOf(
                playerTransform);
    }

    private void ResolvePlayerReferences()
    {
        if (playerTransform == null)
        {
            GameObject playerObject =
                GameObject.FindGameObjectWithTag(
                    PLAYER_TAG);

            if (playerObject == null)
            {
                return;
            }

            playerTransform =
                playerObject.transform;
        }

        if (playerAttackHitReceiver == null)
        {
            ResolvePlayerAttackHitReceiver(
                playerTransform.gameObject);
        }
    }

    private void ResolvePlayerAttackHitReceiver(
        GameObject playerObject)
    {
        MonoBehaviour[] behaviours =
            playerObject.GetComponentsInChildren<MonoBehaviour>(
                true);

        playerAttackHitReceiver =
            null;

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            if (behaviours[i] is
                IAttackHitReceiver receiver)
            {
                playerAttackHitReceiver =
                    receiver;

                return;
            }
        }
    }

    private static float GetVisualHorizontalDirectionSign(
        Transform targetTransform)
    {
        float forwardX =
            targetTransform.forward.x;

        if (Mathf.Abs(forwardX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                forwardX);
        }

        float rightX =
            targetTransform.right.x;

        if (Mathf.Abs(rightX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                rightX);
        }

        Debug.LogWarning(
            $"{nameof(DroneEnemySearchStrategy)}: " +
            "VisualRootの向きからX方向を判定できないため" +
            "+X方向を使用します。",
            targetTransform);

        return 1f;
    }
}
