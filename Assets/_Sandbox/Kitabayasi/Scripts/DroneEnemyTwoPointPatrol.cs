using System;
using UnityEngine;

/// <summary>
/// Droneの2つの巡回地点を検証・補正し、A/B間の往復移動と巡回端の振り返りを担当する。
/// </summary>
/// <remarks>
/// Player索敵、State遷移、巡回原点への帰還、A*経路探索は担当しない。
/// 巡回地点は初期Enemy位置からのXYオフセットとして保存し、巡回開始時に固定深度へ再構築する。
/// </remarks>
public sealed class DroneEnemyTwoPointPatrol
{
    private const int BOX_CAST_HIT_BUFFER_SIZE = 32;
    private const int OVERLAP_BUFFER_SIZE = 32;
    private const int PATROL_CORRECTION_ITERATIONS = 12;

    private const float ARRIVAL_TOLERANCE = 0.05f;
    private const float DIRECTION_EPSILON = 0.001f;
    private const float PATROL_SAFETY_MARGIN = 0.02f;

    private readonly Transform patrolPointA;
    private readonly Transform patrolPointB;
    private readonly Transform enemyTransform;
    private readonly Transform visualRoot;
    private readonly Rigidbody enemyRigidbody;
    private readonly BoxCollider enemyBoxCollider;
    private readonly DroneEnemyPlanarMotor planarMotor;
    private readonly EnemyPlayerReference playerReference;
    private readonly LayerMask navigationObstacleMask;

    private readonly RaycastHit[] boxCastHitBuffer;
    private readonly Collider[] overlapBuffer;

    private readonly Vector2 patrolPointAOffset;
    private readonly Vector2 patrolPointBOffset;

    private Vector3 patrolPointAWorldPosition;
    private Vector3 patrolPointBWorldPosition;

    private Vector3 boxCenterOffsetFromRigidbody;
    private Vector3 boxHalfExtents;
    private Quaternion boxOrientation;

    private bool hasValidPatrolRoute;
    private bool hasPhysicsQueryBufferOverflow;
    private bool isMovingToPointB;
    private bool isTurning;

    private float turnElapsedTime;

    private Quaternion turnStartRotation;
    private Quaternion turnTargetRotation;

    public DroneEnemyTwoPointPatrol(
        Transform patrolPointA,
        Transform patrolPointB,
        Transform enemyTransform,
        Transform visualRoot,
        Rigidbody enemyRigidbody,
        BoxCollider enemyBoxCollider,
        DroneEnemyPlanarMotor planarMotor,
        EnemyPlayerReference playerReference,
        LayerMask navigationObstacleMask)
    {
        this.patrolPointA =
            patrolPointA
            ? patrolPointA
            : throw new ArgumentNullException(
                nameof(patrolPointA));

        this.patrolPointB =
            patrolPointB
            ? patrolPointB
            : throw new ArgumentNullException(
                nameof(patrolPointB));

        this.enemyTransform =
            enemyTransform
            ? enemyTransform
            : throw new ArgumentNullException(
                nameof(enemyTransform));

        this.visualRoot =
            visualRoot
            ? visualRoot
            : throw new ArgumentNullException(
                nameof(visualRoot));


        this.enemyRigidbody =
            enemyRigidbody
            ? enemyRigidbody
            : throw new ArgumentNullException(
                nameof(enemyRigidbody));

        this.enemyBoxCollider =
            enemyBoxCollider
            ? enemyBoxCollider
            : throw new ArgumentNullException(
                nameof(enemyBoxCollider));

        this.planarMotor =
            planarMotor
            ?? throw new ArgumentNullException(
                nameof(planarMotor));

        this.playerReference =
            playerReference
            ?? throw new ArgumentNullException(
                nameof(playerReference));

        this.navigationObstacleMask =
            navigationObstacleMask;

        ValidatePatrolPoints();

        PatrolOriginPosition =
            enemyRigidbody.position;

        patrolPointAOffset =
            GetConfiguredPatrolOffset(
                patrolPointA);

        patrolPointBOffset =
            GetConfiguredPatrolOffset(
                patrolPointB);

        boxCastHitBuffer =
            new RaycastHit[BOX_CAST_HIT_BUFFER_SIZE];

        overlapBuffer =
            new Collider[OVERLAP_BUFFER_SIZE];

        CacheBoxQueryGeometry();
    }

    /// <summary>
    /// Unity上で配置されたゲーム開始時のDrone位置。
    /// 帰還処理はこの位置を巡回原点として利用できる。
    /// </summary>
    public Vector3 PatrolOriginPosition { get; }

    /// <summary>
    /// 現在向かっている巡回地点のワールド位置。
    /// </summary>
    public Vector3 CurrentTargetPosition =>
        isMovingToPointB
            ? patrolPointBWorldPosition
            : patrolPointAWorldPosition;

    /// <summary>
    /// 保存済みのA/Bオフセットから今回の巡回地点を再構築し、
    /// Drone本体形状で安全に往復できる巡回経路を確定する。
    /// </summary>
    public bool TryPrepareRoute(
        out string failureReason)
    {
        hasPhysicsQueryBufferOverflow = false;
        hasValidPatrolRoute = false;
        isTurning = false;
        isMovingToPointB = false;
        turnElapsedTime = 0f;

        CacheBoxQueryGeometry();

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

        SyncPointTransforms();

        if (!IsBoxPlacementSafe(
                PatrolOriginPosition))
        {
            failureReason =
                " Search開始位置でDrone本体のBoxCollider相当形状が" +
                "障害物と重なっています。" +
                GetBufferOverflowMessage();

            return false;
        }

        if (!TryResolveSafeDestinationAlongPath(
                PatrolOriginPosition,
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

            SyncPointTransforms();

            failureReason =
                " PatrolPointA→PatrolPointBの安全経路を確保できません。" +
                GetBufferOverflowMessage();

            return false;
        }

        patrolPointBWorldPosition =
            resolvedB;

        if (!ValidateResolvedPatrolRoute())
        {
            SyncPointTransforms();

            failureReason =
                " 補正後の巡回地点・経路の最終検証に失敗しました。" +
                GetBufferOverflowMessage();

            return false;
        }

        SyncPointTransforms();

        hasValidPatrolRoute = true;
        failureReason = string.Empty;

        return true;
    }

    /// <summary>
    /// A/B間の通常巡回を1物理更新分進める。
    /// </summary>
    public void Update(
        float moveSpeed,
        float turnDuration)
    {
        if (!hasValidPatrolRoute)
        {
            StopPlanarMovement();
            return;
        }

        if (isTurning)
        {
            UpdateTurn(
                turnDuration);
            return;
        }

        if (moveSpeed <= 0f)
        {
            StopPlanarMovement();
            return;
        }

        Vector2 toTarget =
            ToPlanarPosition(
                CurrentTargetPosition) -
            GetCurrentPlanarPosition();

        float remainingDistance =
            toTarget.magnitude;

        if (remainingDistance <=
            ARRIVAL_TOLERANCE)
        {
            BeginTurn(
                turnDuration);
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

        AlignVisualWithPlanarDirection(
            targetVelocity);

        planarMotor.TrySetPlanarVelocity(
            targetVelocity);
    }

    /// <summary>
    /// 帰還完了後など、次の通常巡回をPoint Aから再開するための状態へ戻す。
    /// </summary>
    public void ResetToPointA()
    {
        isMovingToPointB = false;
        isTurning = false;
        turnElapsedTime = 0f;
    }

    /// <summary>
    /// Search終了時に巡回中の回転状態と経路有効状態を破棄する。
    /// </summary>
    public void End()
    {
        hasValidPatrolRoute = false;
        isTurning = false;
        turnElapsedTime = 0f;
    }

    /// <summary>
    /// 補正済みの巡回位置をInspector上のPatrolPoint Transformへ反映する。
    /// </summary>
    public void SyncPointTransforms()
    {
        patrolPointA.position =
            patrolPointAWorldPosition;

        patrolPointB.position =
            patrolPointBWorldPosition;
    }

    private void ValidatePatrolPoints()
    {
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
                $"{nameof(DroneEnemyTwoPointPatrol)}の" +
                "Patrol Point AとBは異なるXY位置に設定してください。");
        }
    }

    private Vector2 GetConfiguredPatrolOffset(
        Transform patrolPoint)
    {
        Vector3 initialEnemyPosition =
            enemyRigidbody.position;

        // Attack後のSearch再突入でもUnity配置時の巡回形状を再利用するため、
        // ワールド位置ではなく初期Enemy位置からのXY差分を固定して保持する。
        return new Vector2(
            patrolPoint.position.x -
            initialEnemyPosition.x,

            patrolPoint.position.y -
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

    private Vector3 BuildPatrolCandidate(
        Vector2 configuredOffset)
    {
        // PatrolPoint側の奥行きは巡回形状として扱わず、
        // 初期Drone位置の深度へ固定してゲームプレイ平面内の巡回を維持する。
        return new Vector3(
            PatrolOriginPosition.x + configuredOffset.x,
            PatrolOriginPosition.y + configuredOffset.y,
            PatrolOriginPosition.z);
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

        // 経路上で最後に安全な位置を固定回数の二分探索で求める。
        // 障害物配置によって補正が終わらなくなることを避ける。
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
            PatrolOriginPosition.z;

        return IsDestinationAndPathSafe(
            pathStart,
            resolvedDestination);
    }

    private bool ValidateResolvedPatrolRoute()
    {
        if ((ToPlanarPosition(
                    patrolPointAWorldPosition) -
                ToPlanarPosition(
                    PatrolOriginPosition)).magnitude <=
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
                PatrolOriginPosition,
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
                navigationObstacleMask,
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
                navigationObstacleMask,
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

    private string GetBufferOverflowMessage()
    {
        return hasPhysicsQueryBufferOverflow
            ? " Physicsクエリ結果が内部バッファ上限に達したため、安全側へ失敗扱いにしています。"
            : string.Empty;
    }

    private void BeginTurn(
        float turnDuration)
    {
        StopPlanarMovement();

        turnElapsedTime = 0f;

        turnStartRotation =
            visualRoot.rotation;

        // 到着した地点ではなく、振り返り完了後に向かう次の地点を正面にする。
        turnTargetRotation =
            CalculatePatrolFacingRotation(
                GetNextPatrolTargetPosition());

        isTurning = true;

        if (turnDuration <= 0f)
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

        return CalculatePlanarFacingRotation(
            toTarget);
    }


    /// <summary>
    /// VisualRootのローカル+ZをXYゲームプレイ平面上の要求移動方向へ合わせる。
    /// </summary>
    private void AlignVisualWithPlanarDirection(
        Vector2 planarDirection)
    {
        if (planarDirection.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return;
        }

        visualRoot.rotation =
            CalculatePlanarFacingRotation(
                planarDirection);
    }

    private static Quaternion CalculatePlanarFacingRotation(
        Vector2 planarDirection)
    {
        Vector3 worldDirection =
            new Vector3(
                planarDirection.x,
                planarDirection.y,
                0f).normalized;

        return Quaternion.LookRotation(
            worldDirection,
            Vector3.up);
    }

    private void UpdateTurn(
        float turnDuration)
    {
        StopPlanarMovement();

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

    private void StopPlanarMovement()
    {
        planarMotor.StopPlanarMovement();
    }
}
