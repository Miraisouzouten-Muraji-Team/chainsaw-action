using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// DroneのA*経路を保持し、XYゲームプレイ平面上でWaypoint追従・経路無効化・再探索を管理する。
/// </summary>
/// <remarks>
/// A*アルゴリズム自体はPlanarAStarPathfinderへ、Rigidbodyへの実移動反映は
/// DroneEnemyPlanarMotorへ委譲する。
/// Player索敵、巡回先や攻撃先の決定、Attackフェーズ変更、State遷移は担当しない。
/// </remarks>
public sealed class DroneEnemyPathNavigator
{
    private const float DIRECTION_EPSILON = 0.001f;
    private const float PATH_RETRY_INTERVAL = 0.5f;

    private readonly Rigidbody enemyRigidbody;
    private readonly BoxCollider enemyBoxCollider;
    private readonly DroneEnemyPlanarMotor planarMotor;
    private readonly LayerMask obstacleMask;

    private readonly float pathSafetyMargin;
    private readonly float pathSearchPadding;
    private readonly int maximumExpandedPathNodes;
    private readonly float waypointArrivalTolerance;
    private readonly float navigationMoveSpeed;
    private readonly float navigationStallMinimumMoveSpeed;
    private readonly float navigationStallDetectionDuration;

    private readonly List<Vector3> path =
        new List<Vector3>();

    private Vector3 boxCenterOffsetFromRigidbody;
    private Vector3 bodyBoxHalfExtents;
    private Vector3 navigationBoxHalfExtents;
    private Quaternion boxOrientation = Quaternion.identity;

    private Vector2 lastNavigationPosition;
    private Vector2 lastPathTargetPosition;

    private float navigationStallElapsedTime;
    private float nextPathSearchTime;

    private int pathIndex;

    /// <summary>
    /// 経路探索・Waypoint追従に必要な物理参照と調整値を受け取る。
    /// </summary>
    /// <remarks>
    /// OldDroneEnemyDataそのものには依存せず、呼び出し側がDataから必要値だけを渡す。
    /// これによりNavigatorは旧型Drone固有の攻撃・巡回データ所有責務を持たない。
    /// </remarks>
    public DroneEnemyPathNavigator(
        Rigidbody enemyRigidbody,
        BoxCollider enemyBoxCollider,
        DroneEnemyPlanarMotor planarMotor,
        LayerMask obstacleMask,
        float pathSafetyMargin,
        float pathSearchPadding,
        int maximumExpandedPathNodes,
        float waypointArrivalTolerance,
        float navigationMoveSpeed,
        float navigationStallMinimumMoveSpeed,
        float navigationStallDetectionDuration)
    {
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

        if (!enemyBoxCollider.enabled)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemyPathNavigator)}には有効な" +
                $"{nameof(BoxCollider)}が必要です。");
        }

        this.planarMotor =
            planarMotor
            ?? throw new ArgumentNullException(
                nameof(planarMotor));

        if (pathSafetyMargin < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pathSafetyMargin));
        }

        if (pathSearchPadding < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pathSearchPadding));
        }

        if (maximumExpandedPathNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumExpandedPathNodes));
        }

        if (waypointArrivalTolerance < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(waypointArrivalTolerance));
        }

        if (navigationMoveSpeed < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(navigationMoveSpeed));
        }

        if (navigationStallMinimumMoveSpeed < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(navigationStallMinimumMoveSpeed));
        }

        if (navigationStallDetectionDuration < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(navigationStallDetectionDuration));
        }

        this.obstacleMask =
            obstacleMask;
        this.pathSafetyMargin =
            pathSafetyMargin;
        this.pathSearchPadding =
            pathSearchPadding;
        this.maximumExpandedPathNodes =
            maximumExpandedPathNodes;
        this.waypointArrivalTolerance =
            waypointArrivalTolerance;
        this.navigationMoveSpeed =
            navigationMoveSpeed;
        this.navigationStallMinimumMoveSpeed =
            navigationStallMinimumMoveSpeed;
        this.navigationStallDetectionDuration =
            navigationStallDetectionDuration;

        RefreshQueryGeometry();
        ClearRuntimeState();
    }

    /// <summary>
    /// 現在保持している有効Waypointがあるかを返す。
    /// </summary>
    public bool HasPath =>
        pathIndex < path.Count;

    /// <summary>
    /// このPhysics StepでMotorへ要求したXYゲームプレイ平面上の移動方向。
    /// 移動要求がない場合はVector2.zero。
    /// </summary>
    public Vector2 RequestedMoveDirection { get; private set; }

    /// <summary>
    /// 現在位置が指定位置への到達許容距離内かをXYゲームプレイ平面で判定する。
    /// </summary>
    public bool IsWithinArrivalTolerance(
        Vector3 targetPosition)
    {
        return Vector2.Distance(
                   GetCurrentPlanarPosition(),
                   ToPlanarPosition(targetPosition)) <=
               waypointArrivalTolerance;
    }

    /// <summary>
    /// 現在位置からTargetへ、Navigation用Box形状で直接進めるか確認する。
    /// TargetそのものへDrone中心を配置できることは要求しない。
    /// </summary>
    public bool IsDirectTraversalAvailable(
        Vector3 targetPosition)
    {
        RefreshQueryGeometry();

        return PlanarAStarPathfinder.IsDirectTraversalAvailable(
            enemyRigidbody.position,
            targetPosition,
            boxCenterOffsetFromRigidbody,
            navigationBoxHalfExtents,
            boxOrientation,
            obstacleMask);
    }

    /// <summary>
    /// 指定位置そのものへ到達するA*経路を取得し、追従準備を行う。
    /// </summary>
    /// <returns>有効な経路を取得できた場合はtrue。</returns>
    public bool TryBeginPathToPosition(
        Vector3 targetPosition)
    {
        Reset();

        return TryRefreshPath(
                   targetPosition,
                   useDirectTraversalGoal: false,
                   successfulRepathInterval: PATH_RETRY_INTERVAL) &&
               HasPath;
    }

    /// <summary>
    /// Targetへ直接進める地点まで到達するA*経路を取得し、追従準備を行う。
    /// </summary>
    /// <remarks>
    /// A*成功時にPathが空なら、現在位置から既にTargetへ直接進めることを表す。
    /// </remarks>
    /// <returns>A*探索自体が成功した場合はtrue。</returns>
    public bool TryBeginPathToDirectTraversalTarget(
        Vector3 targetPosition,
        float successfulRepathInterval)
    {
        Reset();

        return TryRefreshPath(
            targetPosition,
            useDirectTraversalGoal: true,
            successfulRepathInterval);
    }

    /// <summary>
    /// 固定Targetへの経路追従を1 Physics Step分更新する。
    /// Pathが失効・Stall・未取得の場合は再探索待機もここで管理する。
    /// </summary>
    /// <returns>Targetへの到達許容距離内に入った場合はtrue。</returns>
    public bool UpdatePathToPosition(
        Vector3 targetPosition)
    {
        if (IsWithinArrivalTolerance(
                targetPosition))
        {
            StopAndReset();
            return true;
        }

        UpdatePathFollowing(
            targetPosition,
            useDirectTraversalGoal: false,
            successfulRepathInterval: PATH_RETRY_INTERVAL);

        return false;
    }

    /// <summary>
    /// 移動するTargetへ直接進める地点までの経路追従を1 Physics Step分更新する。
    /// Targetが一定距離動いた場合の再探索も汎用的なTarget移動として扱う。
    /// </summary>
    /// <returns>現在位置からTargetへ直接進めるようになった場合はtrue。</returns>
    public bool UpdatePathToDirectTraversalTarget(
        Vector3 targetPosition,
        float repathDistance,
        float successfulRepathInterval)
    {
        if (repathDistance < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(repathDistance));
        }

        if (successfulRepathInterval < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(successfulRepathInterval));
        }

        if (IsDirectTraversalAvailable(
                targetPosition))
        {
            StopAndReset();
            return true;
        }

        Vector2 currentTargetPosition =
            ToPlanarPosition(
                targetPosition);

        bool targetMovedEnoughForRepath =
            Vector2.Distance(
                currentTargetPosition,
                lastPathTargetPosition) >=
            repathDistance;

        if (targetMovedEnoughForRepath &&
            Time.time >= nextPathSearchTime)
        {
            bool hasPath =
                TryRefreshPath(
                    targetPosition,
                    useDirectTraversalGoal: true,
                    successfulRepathInterval);

            if (hasPath &&
                !HasPath)
            {
                StopAndReset();
                return true;
            }

            return false;
        }

        bool pathCompleted =
            UpdatePathFollowing(
                targetPosition,
                useDirectTraversalGoal: true,
                successfulRepathInterval);

        if (pathCompleted)
        {
            StopAndReset();
            return true;
        }

        return false;
    }

    /// <summary>
    /// 保持Pathと再探索待機状態を破棄する。
    /// </summary>
    /// <remarks>
    /// Strategyや別の移動処理へ切り替える際の速度停止は呼び出し側が担当する。
    /// Navigator自身が到達・経路無効化を判断した場合は内部で停止要求を出す。
    /// </remarks>
    public void Reset()
    {
        ClearRuntimeState();
    }

    private bool UpdatePathFollowing(
        Vector3 targetPosition,
        bool useDirectTraversalGoal,
        float successfulRepathInterval)
    {
        if (!HasPath)
        {
            return TryRetryPath(
                targetPosition,
                useDirectTraversalGoal,
                successfulRepathInterval);
        }

        AdvanceReachedWaypoints();

        if (!HasPath)
        {
            return TryRetryPath(
                targetPosition,
                useDirectTraversalGoal,
                successfulRepathInterval);
        }

        Vector3 currentBodyPosition =
            enemyRigidbody.position;

        Vector3 currentWaypoint =
            path[pathIndex];

        RefreshQueryGeometry();

        if (!IsPathSegmentAvailable(
                currentBodyPosition,
                currentWaypoint))
        {
            InvalidatePath();

            return TryRetryPath(
                targetPosition,
                useDirectTraversalGoal,
                successfulRepathInterval);
        }

        if (HasNavigationStalled())
        {
            InvalidatePath();

            return TryRetryPath(
                targetPosition,
                useDirectTraversalGoal,
                successfulRepathInterval);
        }

        MoveTowardWaypoint(
            currentWaypoint);

        return false;
    }

    private bool TryRefreshPath(
        Vector3 targetPosition,
        bool useDirectTraversalGoal,
        float successfulRepathInterval)
    {
        if (successfulRepathInterval < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(successfulRepathInterval));
        }

        RefreshQueryGeometry();
        path.Clear();

        bool hasPath =
            useDirectTraversalGoal
                ? PlanarAStarPathfinder.TryFindPathToDirectTraversal(
                    enemyRigidbody.position,
                    targetPosition,
                    boxCenterOffsetFromRigidbody,
                    navigationBoxHalfExtents,
                    bodyBoxHalfExtents,
                    boxOrientation,
                    pathSearchPadding,
                    maximumExpandedPathNodes,
                    obstacleMask,
                    path)
                : PlanarAStarPathfinder.TryFindPath(
                    enemyRigidbody.position,
                    targetPosition,
                    boxCenterOffsetFromRigidbody,
                    navigationBoxHalfExtents,
                    bodyBoxHalfExtents,
                    boxOrientation,
                    pathSearchPadding,
                    maximumExpandedPathNodes,
                    obstacleMask,
                    path);

        pathIndex = 0;
        navigationStallElapsedTime = 0f;
        lastNavigationPosition =
            GetCurrentPlanarPosition();
        lastPathTargetPosition =
            ToPlanarPosition(
                targetPosition);

        nextPathSearchTime =
            Time.time +
            (hasPath
                ? successfulRepathInterval
                : PATH_RETRY_INTERVAL);

        return hasPath;
    }

    /// <summary>
    /// 通常Clearance内ではNavigation用Boxで通行確認し、
    /// StartだけSafety Marginへ接触している場合は実Box相当で最初のClear Waypointまで脱出する。
    /// </summary>
    private bool IsPathSegmentAvailable(
        Vector3 currentBodyPosition,
        Vector3 currentWaypoint)
    {
        if (!PlanarAStarPathfinder.IsPositionClear(
                currentWaypoint,
                boxCenterOffsetFromRigidbody,
                navigationBoxHalfExtents,
                boxOrientation,
                obstacleMask))
        {
            return false;
        }

        bool isCurrentPositionNavigationClear =
            PlanarAStarPathfinder.IsPositionClear(
                currentBodyPosition,
                boxCenterOffsetFromRigidbody,
                navigationBoxHalfExtents,
                boxOrientation,
                obstacleMask);

        if (isCurrentPositionNavigationClear)
        {
            return PlanarAStarPathfinder.IsTraversalClear(
                currentBodyPosition,
                currentWaypoint,
                boxCenterOffsetFromRigidbody,
                navigationBoxHalfExtents,
                boxOrientation,
                obstacleMask);
        }

        return PlanarAStarPathfinder.IsStartEscapeTraversalAvailable(
            currentBodyPosition,
            currentWaypoint,
            boxCenterOffsetFromRigidbody,
            bodyBoxHalfExtents,
            navigationBoxHalfExtents,
            boxOrientation,
            obstacleMask);
    }

    private bool TryRetryPath(
        Vector3 targetPosition,
        bool useDirectTraversalGoal,
        float successfulRepathInterval)
    {
        RequestedMoveDirection = Vector2.zero;
        planarMotor.StopPlanarMovement();

        if (Time.time <
            nextPathSearchTime)
        {
            return false;
        }

        bool hasPath =
            TryRefreshPath(
                targetPosition,
                useDirectTraversalGoal,
                successfulRepathInterval);

        return useDirectTraversalGoal &&
               hasPath &&
               !HasPath;
    }

    private void InvalidatePath()
    {
        path.Clear();
        pathIndex = 0;
        navigationStallElapsedTime = 0f;

        nextPathSearchTime =
            Mathf.Max(
                nextPathSearchTime,
                Time.time);

        RequestedMoveDirection = Vector2.zero;
        planarMotor.StopPlanarMovement();
    }

    private void AdvanceReachedWaypoints()
    {
        while (HasPath)
        {
            Vector2 toWaypoint =
                ToPlanarPosition(
                    path[pathIndex]) -
                GetCurrentPlanarPosition();

            if (toWaypoint.magnitude >
                waypointArrivalTolerance)
            {
                break;
            }

            pathIndex++;
            navigationStallElapsedTime = 0f;
            lastNavigationPosition =
                GetCurrentPlanarPosition();
        }
    }

    private void MoveTowardWaypoint(
        Vector3 waypoint)
    {
        float fixedDeltaTime =
            Time.fixedDeltaTime;

        if (navigationMoveSpeed <=
                DIRECTION_EPSILON ||
            fixedDeltaTime <=
                DIRECTION_EPSILON)
        {
            RequestedMoveDirection = Vector2.zero;
            planarMotor.StopPlanarMovement();
            return;
        }

        Vector2 toWaypoint =
            ToPlanarPosition(
                waypoint) -
            GetCurrentPlanarPosition();

        float remainingDistance =
            toWaypoint.magnitude;

        float expectedMovementDistance =
            navigationMoveSpeed *
            fixedDeltaTime;

        Vector2 targetVelocity =
            remainingDistance <=
            expectedMovementDistance
                ? toWaypoint / fixedDeltaTime
                : toWaypoint.normalized *
                  navigationMoveSpeed;

        RequestedMoveDirection =
            targetVelocity.sqrMagnitude >
            DIRECTION_EPSILON * DIRECTION_EPSILON
                ? targetVelocity.normalized
                : Vector2.zero;

        planarMotor.TrySetPlanarVelocity(
            targetVelocity);
    }

    private bool HasNavigationStalled()
    {
        Vector2 currentPosition =
            GetCurrentPlanarPosition();

        if (navigationStallDetectionDuration <=
                DIRECTION_EPSILON ||
            navigationStallMinimumMoveSpeed <=
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
            navigationStallMinimumMoveSpeed *
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
               navigationStallDetectionDuration;
    }

    private void RefreshQueryGeometry()
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

        bodyBoxHalfExtents =
            Vector3.Scale(
                enemyBoxCollider.size * 0.5f,
                absoluteScale);

        navigationBoxHalfExtents =
            bodyBoxHalfExtents +
            Vector3.one *
            pathSafetyMargin;

        boxOrientation =
            boxTransform.rotation;
    }

    private void StopAndReset()
    {
        ClearRuntimeState();
        planarMotor.StopPlanarMovement();
    }

    private void ClearRuntimeState()
    {
        path.Clear();
        pathIndex = 0;
        navigationStallElapsedTime = 0f;
        nextPathSearchTime = 0f;
        lastNavigationPosition =
            GetCurrentPlanarPosition();
        lastPathTargetPosition = Vector2.zero;
        RequestedMoveDirection = Vector2.zero;
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
}
