using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// XYゲームプレイ平面上で、指定したBox形状が通行できる経路をA*で探索する。
/// </summary>
/// <remarks>
/// MonoBehaviourやEnemy固有型には依存せず、開始位置・目的位置・通行判定条件から
/// Waypoint列だけを返す。Rigidbodyの移動、再探索タイミング、攻撃位置の意味付けは担当しない。
///
/// 通行判定は3D PhysicsのBox overlap / Box castを使用し、経路NodeだけをXY平面上へ展開する。
/// 現在のLayer構成ではPlayerとStage障害物をLayerMaskだけで分離できないため、
/// Player Tagを持つ親階層配下のColliderだけは通行障害物から除外する。
///
/// StartだけNavigation用Safety Marginを満たしていない場合は、
/// 実Box相当の小さい形状で最寄りのNavigation Clear地点まで脱出してから通常A*へ接続する。
/// 実際のRigidbody移動時の侵入防止はDroneEnemyPlanarMotor等の移動側が最終責任を持つ。
/// </remarks>
public static class PlanarAStarPathfinder
{
    private const string PLAYER_TAG = "Player";

    private const int INITIAL_PHYSICS_QUERY_BUFFER_SIZE = 16;

    private const float QUERY_EPSILON = 0.001f;
    private const float START_ESCAPE_QUERY_SKIN = 0.001f;
    private const float MINIMUM_NODE_SPACING = 0.25f;
    private const float NODE_SPACING_HALF_EXTENT_RATIO = 0.75f;

    private static RaycastHit[] boxCastHitBuffer =
        new RaycastHit[INITIAL_PHYSICS_QUERY_BUFFER_SIZE];

    private static Collider[] overlapHitBuffer =
        new Collider[INITIAL_PHYSICS_QUERY_BUFFER_SIZE];

    private static readonly Vector2Int[] NEIGHBOR_OFFSETS =
    {
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(-1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(1, 1)
    };

    /// <summary>
    /// StartからGoalそのものまで到達する経路を探索する。
    /// GoalへClearance Boxを配置できない場合は失敗する。
    /// </summary>
    /// <param name="queryCenterOffsetFromBody">
    /// Rigidbody位置等のBody基準位置から、通行判定Box中心までのワールド空間オフセット。
    /// </param>
    /// <param name="queryHalfExtents">
    /// Safety Marginを含めた、通行判定Boxのワールド空間Half Extents。
    /// </param>
    /// <param name="queryOrientation">
    /// 通行判定Boxのワールド回転。
    /// </param>
    /// <param name="resultPath">
    /// 成功時のWaypoint列。Startは含めず、最後に実Goal位置を含む。
    /// 呼び出し時点の内容は必ず消去される。
    /// </param>
    public static bool TryFindPath(
        Vector3 startBodyPosition,
        Vector3 goalBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        float searchPadding,
        int maximumExpandedNodes,
        LayerMask obstacleMask,
        List<Vector3> resultPath)
    {
        return TryFindPath(
            startBodyPosition,
            goalBodyPosition,
            queryCenterOffsetFromBody,
            queryHalfExtents,
            queryHalfExtents,
            queryOrientation,
            searchPadding,
            maximumExpandedNodes,
            obstacleMask,
            resultPath);
    }

    /// <summary>
    /// StartだけNavigation Clearanceを満たさない場合に、実Box相当形状で
    /// 最寄りのClear地点へ脱出してからGoalまで探索する。
    /// </summary>
    /// <param name="startEscapeHalfExtents">
    /// Start脱出区間だけに使う実Box相当のHalf Extents。
    /// 通常のqueryHalfExtents以下である必要がある。
    /// </param>
    public static bool TryFindPath(
        Vector3 startBodyPosition,
        Vector3 goalBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Vector3 startEscapeHalfExtents,
        Quaternion queryOrientation,
        float searchPadding,
        int maximumExpandedNodes,
        LayerMask obstacleMask,
        List<Vector3> resultPath)
    {
        return TryFindPathInternal(
            startBodyPosition,
            goalBodyPosition,
            queryCenterOffsetFromBody,
            queryHalfExtents,
            startEscapeHalfExtents,
            queryOrientation,
            searchPadding,
            maximumExpandedNodes,
            obstacleMask,
            useDirectTraversalGoal: false,
            includeGoalInPath: true,
            resultPath);
    }

    /// <summary>
    /// TargetそのものへBox中心を到着させるのではなく、
    /// BoxのTarget側の面がTarget位置まで到達できる地点までの経路を探索する。
    /// </summary>
    /// <remarks>
    /// Target位置へのBox配置は要求しない。
    /// 突進前の位置取りなど「Targetへ攻撃開始可能な距離まで直接移動できれば
    /// 経路追従を終える」用途を想定する。
    /// 戻り値がtrueかつresultPathが空の場合、Startから既に直接進める。
    /// </remarks>
    public static bool TryFindPathToDirectTraversal(
        Vector3 startBodyPosition,
        Vector3 targetBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        float searchPadding,
        int maximumExpandedNodes,
        LayerMask obstacleMask,
        List<Vector3> resultPath)
    {
        return TryFindPathToDirectTraversal(
            startBodyPosition,
            targetBodyPosition,
            queryCenterOffsetFromBody,
            queryHalfExtents,
            queryHalfExtents,
            queryOrientation,
            searchPadding,
            maximumExpandedNodes,
            obstacleMask,
            resultPath);
    }

    /// <summary>
    /// StartだけNavigation Clearanceを満たさない場合に、実Box相当形状で
    /// Clear地点へ脱出してからTargetへの直接突進可能地点まで探索する。
    /// </summary>
    public static bool TryFindPathToDirectTraversal(
        Vector3 startBodyPosition,
        Vector3 targetBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Vector3 startEscapeHalfExtents,
        Quaternion queryOrientation,
        float searchPadding,
        int maximumExpandedNodes,
        LayerMask obstacleMask,
        List<Vector3> resultPath)
    {
        return TryFindPathInternal(
            startBodyPosition,
            targetBodyPosition,
            queryCenterOffsetFromBody,
            queryHalfExtents,
            startEscapeHalfExtents,
            queryOrientation,
            searchPadding,
            maximumExpandedNodes,
            obstacleMask,
            useDirectTraversalGoal: true,
            includeGoalInPath: false,
            resultPath);
    }

    /// <summary>
    /// 指定Body位置へClearance Boxを配置できるか確認する。
    /// </summary>
    public static bool IsPositionClear(
        Vector3 bodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask)
    {
        ValidateQueryGeometry(
            queryHalfExtents);

        Vector3 queryCenter =
            bodyPosition +
            queryCenterOffsetFromBody;

        return !HasBlockingOverlap(
            queryCenter,
            queryHalfExtents,
            queryOrientation,
            obstacleMask);
    }

    /// <summary>
    /// StartからEndまでClearance Boxを並進させたとき、障害物へ接触しないか確認する。
    /// </summary>
    /// <remarks>
    /// このメソッド単体では始点・終点のOverlapを検証しない。
    /// A*内部では配置可能なNodeだけを接続するため問題ないが、外部から使用する場合は
    /// 必要に応じてIsPositionClearと組み合わせること。
    /// </remarks>
    public static bool IsTraversalClear(
        Vector3 startBodyPosition,
        Vector3 endBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask)
    {
        ValidateQueryGeometry(
            queryHalfExtents);

        Vector3 pathVector =
            endBodyPosition -
            startBodyPosition;

        float pathDistance =
            pathVector.magnitude;

        if (pathDistance <=
            QUERY_EPSILON)
        {
            return true;
        }

        Vector3 castOrigin =
            startBodyPosition +
            queryCenterOffsetFromBody;

        return !HasBlockingTraversal(
            castOrigin,
            queryHalfExtents,
            queryOrientation,
            pathVector / pathDistance,
            pathDistance,
            obstacleMask);
    }

    /// <summary>
    /// Navigation Clearanceを満たしていないStartから、Navigation ClearなEndまでを
    /// 実Box相当形状で脱出できるか確認する。
    /// </summary>
    /// <remarks>
    /// Start接触面をOverlapとして拾って永久停止しないよう、脱出用Boxだけ微小に縮めてSweepする。
    /// この判定はStart脱出区間専用で、通常経路のSafety Marginを代替しない。
    /// </remarks>
    public static bool IsStartEscapeTraversalAvailable(
        Vector3 startBodyPosition,
        Vector3 endBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 startEscapeHalfExtents,
        Vector3 navigationHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask)
    {
        ValidateStartEscapeGeometry(
            navigationHalfExtents,
            startEscapeHalfExtents);

        if (!IsPositionClear(
                endBodyPosition,
                queryCenterOffsetFromBody,
                navigationHalfExtents,
                queryOrientation,
                obstacleMask))
        {
            return false;
        }

        Vector3 escapeQueryHalfExtents =
            CreateStartEscapeQueryHalfExtents(
                startEscapeHalfExtents);

        return IsTraversalClear(
            startBodyPosition,
            endBodyPosition,
            queryCenterOffsetFromBody,
            escapeQueryHalfExtents,
            queryOrientation,
            obstacleMask);
    }

    /// <summary>
    /// Startが安全で、BoxのTarget側の面がTarget位置まで届く区間を直接Sweepできるか確認する。
    /// TargetそのものへのBox中心の配置は要求しない。
    /// </summary>
    public static bool IsDirectTraversalAvailable(
        Vector3 startBodyPosition,
        Vector3 targetBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask)
    {
        if (!IsPositionClear(
                startBodyPosition,
                queryCenterOffsetFromBody,
                queryHalfExtents,
                queryOrientation,
                obstacleMask))
        {
            return false;
        }

        Vector3 traversalEndBodyPosition =
            CalculateDirectTraversalEndBodyPosition(
                startBodyPosition,
                targetBodyPosition,
                queryCenterOffsetFromBody,
                queryHalfExtents,
                queryOrientation);

        return IsTraversalClear(
            startBodyPosition,
            traversalEndBodyPosition,
            queryCenterOffsetFromBody,
            queryHalfExtents,
            queryOrientation,
            obstacleMask);
    }

    private static bool TryFindPathInternal(
        Vector3 startBodyPosition,
        Vector3 goalBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Vector3 startEscapeHalfExtents,
        Quaternion queryOrientation,
        float searchPadding,
        int maximumExpandedNodes,
        LayerMask obstacleMask,
        bool useDirectTraversalGoal,
        bool includeGoalInPath,
        List<Vector3> resultPath)
    {
        if (resultPath == null)
        {
            throw new ArgumentNullException(
                nameof(resultPath));
        }

        resultPath.Clear();

        ValidateSearchArguments(
            queryHalfExtents,
            startEscapeHalfExtents,
            searchPadding,
            maximumExpandedNodes);

        // Planar Pathfinderなので、呼び出し側のGoal Zは経路形状へ反映しない。
        goalBodyPosition.z =
            startBodyPosition.z;

        if (!useDirectTraversalGoal &&
            !IsPositionClear(
                goalBodyPosition,
                queryCenterOffsetFromBody,
                queryHalfExtents,
                queryOrientation,
                obstacleMask))
        {
            return false;
        }

        float nodeSpacing =
            CalculateNodeSpacing(
                queryHalfExtents,
                queryOrientation);

        Vector3 pathSearchStartPosition =
            startBodyPosition;

        bool requiresStartEscape =
            !IsPositionClear(
                startBodyPosition,
                queryCenterOffsetFromBody,
                queryHalfExtents,
                queryOrientation,
                obstacleMask);

        if (requiresStartEscape &&
            !TryFindStartEscapePosition(
                startBodyPosition,
                goalBodyPosition,
                queryCenterOffsetFromBody,
                startEscapeHalfExtents,
                queryHalfExtents,
                queryOrientation,
                nodeSpacing,
                obstacleMask,
                out pathSearchStartPosition))
        {
            return false;
        }

        if (HasReachedGoalFromPosition(
                pathSearchStartPosition,
                goalBodyPosition,
                queryCenterOffsetFromBody,
                queryHalfExtents,
                queryOrientation,
                obstacleMask,
                useDirectTraversalGoal))
        {
            if (requiresStartEscape)
            {
                resultPath.Add(
                    pathSearchStartPosition);
            }

            if (includeGoalInPath &&
                GetPlanarDistance(
                    pathSearchStartPosition,
                    goalBodyPosition) > QUERY_EPSILON)
            {
                resultPath.Add(
                    goalBodyPosition);
            }

            return true;
        }

        float minimumX =
            Mathf.Min(
                pathSearchStartPosition.x,
                goalBodyPosition.x) -
            searchPadding;

        float maximumX =
            Mathf.Max(
                pathSearchStartPosition.x,
                goalBodyPosition.x) +
            searchPadding;

        float minimumY =
            Mathf.Min(
                pathSearchStartPosition.y,
                goalBodyPosition.y) -
            searchPadding;

        float maximumY =
            Mathf.Max(
                pathSearchStartPosition.y,
                goalBodyPosition.y) +
            searchPadding;

        Vector2Int startNode =
            Vector2Int.zero;

        List<Vector2Int> openNodes =
            new List<Vector2Int>();

        HashSet<Vector2Int> openNodeSet =
            new HashSet<Vector2Int>();

        HashSet<Vector2Int> closedNodes =
            new HashSet<Vector2Int>();

        Dictionary<Vector2Int, float> costFromStart =
            new Dictionary<Vector2Int, float>();

        Dictionary<Vector2Int, Vector2Int> parentByNode =
            new Dictionary<Vector2Int, Vector2Int>();

        // 同じGrid Nodeへ複数方向から到達しても、配置判定は1探索につき1回にする。
        Dictionary<Vector2Int, bool> placementClearByNode =
            new Dictionary<Vector2Int, bool>();

        openNodes.Add(
            startNode);

        openNodeSet.Add(
            startNode);

        costFromStart[startNode] = 0f;
        placementClearByNode[startNode] = true;

        int expandedNodeCount = 0;

        while (openNodes.Count > 0 &&
               expandedNodeCount <
               maximumExpandedNodes)
        {
            int currentOpenIndex =
                SelectBestOpenNodeIndex(
                    openNodes,
                    costFromStart,
                    pathSearchStartPosition,
                    goalBodyPosition,
                    nodeSpacing);

            Vector2Int currentNode =
                openNodes[currentOpenIndex];

            openNodes.RemoveAt(
                currentOpenIndex);

            openNodeSet.Remove(
                currentNode);

            if (!closedNodes.Add(
                    currentNode))
            {
                continue;
            }

            expandedNodeCount++;

            Vector3 currentWorldPosition =
                NodeToWorldPosition(
                    currentNode,
                    pathSearchStartPosition,
                    nodeSpacing);

            // Goal自身がGrid上に乗るとは限らない。
            // 通常Goalは実Goalへ接続できた時点、DirectTraversal Goalは
            // BoxのTarget側の面がTargetへ届く区間を通せた時点で探索成功とする。
            if (HasReachedGoalFromPosition(
                    currentWorldPosition,
                    goalBodyPosition,
                    queryCenterOffsetFromBody,
                    queryHalfExtents,
                    queryOrientation,
                    obstacleMask,
                    useDirectTraversalGoal))
            {
                List<Vector3> pathAfterEscape =
                    requiresStartEscape
                        ? new List<Vector3>()
                        : resultPath;

                BuildSmoothedPath(
                    startNode,
                    currentNode,
                    parentByNode,
                    pathSearchStartPosition,
                    goalBodyPosition,
                    nodeSpacing,
                    queryCenterOffsetFromBody,
                    queryHalfExtents,
                    queryOrientation,
                    obstacleMask,
                    includeGoalInPath,
                    pathAfterEscape);

                if (requiresStartEscape)
                {
                    resultPath.Add(
                        pathSearchStartPosition);

                    resultPath.AddRange(
                        pathAfterEscape);
                }

                return true;
            }

            for (int i = 0;
                 i < NEIGHBOR_OFFSETS.Length;
                 i++)
            {
                Vector2Int neighborNode =
                    currentNode +
                    NEIGHBOR_OFFSETS[i];

                if (closedNodes.Contains(
                        neighborNode))
                {
                    continue;
                }

                Vector3 neighborWorldPosition =
                    NodeToWorldPosition(
                        neighborNode,
                        pathSearchStartPosition,
                        nodeSpacing);

                if (!IsWithinSearchBounds(
                        neighborWorldPosition,
                        minimumX,
                        maximumX,
                        minimumY,
                        maximumY))
                {
                    continue;
                }

                if (!TryGetNodePlacementClear(
                        neighborNode,
                        neighborWorldPosition,
                        queryCenterOffsetFromBody,
                        queryHalfExtents,
                        queryOrientation,
                        obstacleMask,
                        placementClearByNode))
                {
                    continue;
                }

                if (!IsTraversalClear(
                        currentWorldPosition,
                        neighborWorldPosition,
                        queryCenterOffsetFromBody,
                        queryHalfExtents,
                        queryOrientation,
                        obstacleMask))
                {
                    continue;
                }

                float tentativeCost =
                    costFromStart[currentNode] +
                    Vector3.Distance(
                        currentWorldPosition,
                        neighborWorldPosition);

                if (costFromStart.TryGetValue(
                        neighborNode,
                        out float knownCost) &&
                    tentativeCost >= knownCost)
                {
                    continue;
                }

                costFromStart[neighborNode] =
                    tentativeCost;

                parentByNode[neighborNode] =
                    currentNode;

                if (openNodeSet.Add(
                        neighborNode))
                {
                    openNodes.Add(
                        neighborNode);
                }
            }
        }

        resultPath.Clear();
        return false;
    }

    /// <summary>
    /// Safety Marginだけが原因でStartを通常Nodeとして使えない場合に、
    /// 実Box相当形状で直接到達できる最寄りのNavigation Clear地点を探す。
    /// </summary>
    private static bool TryFindStartEscapePosition(
        Vector3 startBodyPosition,
        Vector3 goalBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 startEscapeHalfExtents,
        Vector3 navigationHalfExtents,
        Quaternion queryOrientation,
        float nodeSpacing,
        LayerMask obstacleMask,
        out Vector3 escapeBodyPosition)
    {
        Vector3 escapeQueryHalfExtents =
            CreateStartEscapeQueryHalfExtents(
                startEscapeHalfExtents);

        float clearanceDifferenceX =
            Mathf.Max(
                0f,
                CalculateBoxSupportDistance(
                    navigationHalfExtents,
                    queryOrientation,
                    Vector3.right) -
                CalculateBoxSupportDistance(
                    escapeQueryHalfExtents,
                    queryOrientation,
                    Vector3.right));

        float clearanceDifferenceY =
            Mathf.Max(
                0f,
                CalculateBoxSupportDistance(
                    navigationHalfExtents,
                    queryOrientation,
                    Vector3.up) -
                CalculateBoxSupportDistance(
                    escapeQueryHalfExtents,
                    queryOrientation,
                    Vector3.up));

        float maximumClearanceDifference =
            Mathf.Max(
                clearanceDifferenceX,
                clearanceDifferenceY);

        int requiredRingCount =
            Mathf.CeilToInt(
                maximumClearanceDifference /
                nodeSpacing) + 1;

        int maximumRingCount =
            Mathf.Max(
                1,
                requiredRingCount);

        for (int ring = 1;
             ring <= maximumRingCount;
             ring++)
        {
            bool foundCandidate = false;
            float bestStartDistance =
                float.PositiveInfinity;
            float bestGoalDistance =
                float.PositiveInfinity;
            Vector3 bestCandidate = default;

            for (int offsetX = -ring;
                 offsetX <= ring;
                 offsetX++)
            {
                for (int offsetY = -ring;
                     offsetY <= ring;
                     offsetY++)
                {
                    bool isRingEdge =
                        Mathf.Abs(offsetX) == ring ||
                        Mathf.Abs(offsetY) == ring;

                    if (!isRingEdge)
                    {
                        continue;
                    }

                    Vector3 candidate =
                        new Vector3(
                            startBodyPosition.x +
                            offsetX * nodeSpacing,
                            startBodyPosition.y +
                            offsetY * nodeSpacing,
                            startBodyPosition.z);

                    if (!IsPositionClear(
                            candidate,
                            queryCenterOffsetFromBody,
                            navigationHalfExtents,
                            queryOrientation,
                            obstacleMask) ||
                        !IsTraversalClear(
                            startBodyPosition,
                            candidate,
                            queryCenterOffsetFromBody,
                            escapeQueryHalfExtents,
                            queryOrientation,
                            obstacleMask))
                    {
                        continue;
                    }

                    float startDistance =
                        GetPlanarDistance(
                            startBodyPosition,
                            candidate);

                    float goalDistance =
                        GetPlanarDistance(
                            candidate,
                            goalBodyPosition);

                    if (foundCandidate &&
                        (startDistance >
                            bestStartDistance + QUERY_EPSILON ||
                         Mathf.Abs(
                             startDistance -
                             bestStartDistance) <= QUERY_EPSILON &&
                         goalDistance >=
                            bestGoalDistance))
                    {
                        continue;
                    }

                    foundCandidate = true;
                    bestStartDistance =
                        startDistance;
                    bestGoalDistance =
                        goalDistance;
                    bestCandidate =
                        candidate;
                }
            }

            if (foundCandidate)
            {
                escapeBodyPosition =
                    bestCandidate;

                return true;
            }
        }

        escapeBodyPosition = default;
        return false;
    }

    private static Vector3 CreateStartEscapeQueryHalfExtents(
        Vector3 startEscapeHalfExtents)
    {
        return new Vector3(
            Mathf.Max(
                QUERY_EPSILON,
                startEscapeHalfExtents.x -
                START_ESCAPE_QUERY_SKIN),
            Mathf.Max(
                QUERY_EPSILON,
                startEscapeHalfExtents.y -
                START_ESCAPE_QUERY_SKIN),
            Mathf.Max(
                QUERY_EPSILON,
                startEscapeHalfExtents.z -
                START_ESCAPE_QUERY_SKIN));
    }

    private static bool HasReachedGoalFromPosition(
        Vector3 currentBodyPosition,
        Vector3 goalBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask,
        bool useDirectTraversalGoal)
    {
        if (useDirectTraversalGoal)
        {
            return IsDirectTraversalAvailable(
                currentBodyPosition,
                goalBodyPosition,
                queryCenterOffsetFromBody,
                queryHalfExtents,
                queryOrientation,
                obstacleMask);
        }

        return IsTraversalClear(
            currentBodyPosition,
            goalBodyPosition,
            queryCenterOffsetFromBody,
            queryHalfExtents,
            queryOrientation,
            obstacleMask);
    }

    private static int SelectBestOpenNodeIndex(
        List<Vector2Int> openNodes,
        Dictionary<Vector2Int, float> costFromStart,
        Vector3 startBodyPosition,
        Vector3 goalBodyPosition,
        float nodeSpacing)
    {
        int bestIndex = 0;
        float bestEstimatedTotalCost =
            float.PositiveInfinity;
        float bestHeuristicCost =
            float.PositiveInfinity;

        for (int i = 0;
             i < openNodes.Count;
             i++)
        {
            Vector2Int node =
                openNodes[i];

            Vector3 worldPosition =
                NodeToWorldPosition(
                    node,
                    startBodyPosition,
                    nodeSpacing);

            float heuristicCost =
                GetPlanarDistance(
                    worldPosition,
                    goalBodyPosition);

            float estimatedTotalCost =
                costFromStart[node] +
                heuristicCost;

            if (estimatedTotalCost <
                    bestEstimatedTotalCost ||
                Mathf.Approximately(
                    estimatedTotalCost,
                    bestEstimatedTotalCost) &&
                heuristicCost <
                    bestHeuristicCost)
            {
                bestIndex = i;
                bestEstimatedTotalCost =
                    estimatedTotalCost;
                bestHeuristicCost =
                    heuristicCost;
            }
        }

        return bestIndex;
    }

    private static bool TryGetNodePlacementClear(
        Vector2Int node,
        Vector3 worldPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask,
        Dictionary<Vector2Int, bool> placementClearByNode)
    {
        if (placementClearByNode.TryGetValue(
                node,
                out bool cachedResult))
        {
            return cachedResult;
        }

        bool isClear =
            IsPositionClear(
                worldPosition,
                queryCenterOffsetFromBody,
                queryHalfExtents,
                queryOrientation,
                obstacleMask);

        placementClearByNode[node] =
            isClear;

        return isClear;
    }

    private static void BuildSmoothedPath(
        Vector2Int startNode,
        Vector2Int reachedNode,
        Dictionary<Vector2Int, Vector2Int> parentByNode,
        Vector3 startBodyPosition,
        Vector3 goalBodyPosition,
        float nodeSpacing,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask,
        bool includeGoalInPath,
        List<Vector3> resultPath)
    {
        List<Vector3> rawPath =
            new List<Vector3>();

        Vector2Int currentNode =
            reachedNode;

        while (currentNode !=
               startNode)
        {
            rawPath.Add(
                NodeToWorldPosition(
                    currentNode,
                    startBodyPosition,
                    nodeSpacing));

            if (!parentByNode.TryGetValue(
                    currentNode,
                    out Vector2Int parentNode))
            {
                resultPath.Clear();
                return;
            }

            currentNode =
                parentNode;
        }

        rawPath.Reverse();

        if (includeGoalInPath)
        {
            rawPath.Add(
                goalBodyPosition);
        }

        if (rawPath.Count == 0)
        {
            return;
        }

        Vector3 smoothingStart =
            startBodyPosition;

        int nextRawIndex = 0;

        while (nextRawIndex <
               rawPath.Count)
        {
            int furthestReachableIndex =
                nextRawIndex;

            for (int candidateIndex =
                     rawPath.Count - 1;
                 candidateIndex >=
                     nextRawIndex;
                 candidateIndex--)
            {
                if (!IsTraversalClear(
                        smoothingStart,
                        rawPath[candidateIndex],
                        queryCenterOffsetFromBody,
                        queryHalfExtents,
                        queryOrientation,
                        obstacleMask))
                {
                    continue;
                }

                furthestReachableIndex =
                    candidateIndex;
                break;
            }

            Vector3 nextWaypoint =
                rawPath[furthestReachableIndex];

            resultPath.Add(
                nextWaypoint);

            smoothingStart =
                nextWaypoint;

            nextRawIndex =
                furthestReachableIndex + 1;
        }
    }

    private static Vector3 CalculateDirectTraversalEndBodyPosition(
        Vector3 startBodyPosition,
        Vector3 targetBodyPosition,
        Vector3 queryCenterOffsetFromBody,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation)
    {
        Vector3 startQueryCenter =
            startBodyPosition +
            queryCenterOffsetFromBody;

        // 経路移動はXY平面なので、Targetの奥行きは通行判定方向へ含めない。
        Vector3 planarTargetPosition =
            new Vector3(
                targetBodyPosition.x,
                targetBodyPosition.y,
                startQueryCenter.z);

        Vector3 toTarget =
            planarTargetPosition -
            startQueryCenter;

        float targetDistance =
            toTarget.magnitude;

        if (targetDistance <=
            QUERY_EPSILON)
        {
            return startBodyPosition;
        }

        Vector3 direction =
            toTarget / targetDistance;

        float boxSupportDistance =
            CalculateBoxSupportDistance(
                queryHalfExtents,
                queryOrientation,
                direction);

        float traversalDistance =
            Mathf.Max(
                0f,
                targetDistance -
                boxSupportDistance);

        return startBodyPosition +
               direction * traversalDistance;
    }

    /// <summary>
    /// 指定方向へ投影したBox中心から外周までの距離を求める。
    /// </summary>
    private static float CalculateBoxSupportDistance(
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        Vector3 direction)
    {
        Vector3 normalizedDirection =
            direction.normalized;

        Vector3 boxRight =
            queryOrientation * Vector3.right;

        Vector3 boxUp =
            queryOrientation * Vector3.up;

        Vector3 boxForward =
            queryOrientation * Vector3.forward;

        return
            queryHalfExtents.x *
            Mathf.Abs(
                Vector3.Dot(
                    boxRight,
                    normalizedDirection)) +
            queryHalfExtents.y *
            Mathf.Abs(
                Vector3.Dot(
                    boxUp,
                    normalizedDirection)) +
            queryHalfExtents.z *
            Mathf.Abs(
                Vector3.Dot(
                    boxForward,
                    normalizedDirection));
    }

    private static float CalculateNodeSpacing(
        Vector3 queryHalfExtents,
        Quaternion queryOrientation)
    {
        float projectedHalfExtentX =
            CalculateBoxSupportDistance(
                queryHalfExtents,
                queryOrientation,
                Vector3.right);

        float projectedHalfExtentY =
            CalculateBoxSupportDistance(
                queryHalfExtents,
                queryOrientation,
                Vector3.up);

        float minimumPlanarHalfExtent =
            Mathf.Min(
                projectedHalfExtentX,
                projectedHalfExtentY);

        return Mathf.Max(
            minimumPlanarHalfExtent *
            NODE_SPACING_HALF_EXTENT_RATIO,
            MINIMUM_NODE_SPACING);
    }

    private static Vector3 NodeToWorldPosition(
        Vector2Int node,
        Vector3 startBodyPosition,
        float nodeSpacing)
    {
        return new Vector3(
            startBodyPosition.x +
            node.x * nodeSpacing,
            startBodyPosition.y +
            node.y * nodeSpacing,
            startBodyPosition.z);
    }

    private static bool IsWithinSearchBounds(
        Vector3 position,
        float minimumX,
        float maximumX,
        float minimumY,
        float maximumY)
    {
        return
            position.x >= minimumX &&
            position.x <= maximumX &&
            position.y >= minimumY &&
            position.y <= maximumY;
    }

    private static float GetPlanarDistance(
        Vector3 firstPosition,
        Vector3 secondPosition)
    {
        float deltaX =
            secondPosition.x -
            firstPosition.x;

        float deltaY =
            secondPosition.y -
            firstPosition.y;

        return Mathf.Sqrt(
            deltaX * deltaX +
            deltaY * deltaY);
    }

    /// <summary>
    /// 指定Box範囲内に、Player以外の障害物Colliderが存在するか確認する。
    /// </summary>
    private static bool HasBlockingOverlap(
        Vector3 queryCenter,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask)
    {
        int hitCount =
            CollectOverlapHits(
                queryCenter,
                queryHalfExtents,
                queryOrientation,
                obstacleMask);

        for (int i = 0;
             i < hitCount;
             i++)
        {
            Collider hitCollider =
                overlapHitBuffer[i];

            if (hitCollider == null ||
                IsPlayerCollider(
                    hitCollider))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 指定Sweep区間内に、Player以外の障害物Colliderが存在するか確認する。
    /// </summary>
    private static bool HasBlockingTraversal(
        Vector3 castOrigin,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        Vector3 direction,
        float pathDistance,
        LayerMask obstacleMask)
    {
        int hitCount =
            CollectBoxCastHits(
                castOrigin,
                queryHalfExtents,
                queryOrientation,
                direction,
                pathDistance,
                obstacleMask);

        for (int i = 0;
             i < hitCount;
             i++)
        {
            Collider hitCollider =
                boxCastHitBuffer[i].collider;

            if (hitCollider == null ||
                IsPlayerCollider(
                    hitCollider))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// OverlapBoxの結果を、欠落がないと判断できる容量までバッファを拡張して取得する。
    /// </summary>
    /// <remarks>
    /// NonAlloc APIはバッファ容量までしか結果を格納しない。
    /// Player Colliderだけがバッファを埋めてStage障害物が欠落すると通行可能と誤判定するため、
    /// 戻り値がバッファ長と一致した場合は容量を増やして再取得する。
    /// </remarks>
    private static int CollectOverlapHits(
        Vector3 queryCenter,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        LayerMask obstacleMask)
    {
        while (true)
        {
            int hitCount =
                Physics.OverlapBoxNonAlloc(
                    queryCenter,
                    queryHalfExtents,
                    overlapHitBuffer,
                    queryOrientation,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore);

            if (hitCount <
                overlapHitBuffer.Length)
            {
                return hitCount;
            }

            Array.Resize(
                ref overlapHitBuffer,
                overlapHitBuffer.Length * 2);
        }
    }

    /// <summary>
    /// BoxCastの結果を、欠落がないと判断できる容量までバッファを拡張して取得する。
    /// </summary>
    /// <remarks>
    /// BoxCastNonAllocの結果順序には依存しない。
    /// Player以外のHitが1件でも存在すれば通行不可とするため、全Hitを検査する。
    /// </remarks>
    private static int CollectBoxCastHits(
        Vector3 castOrigin,
        Vector3 queryHalfExtents,
        Quaternion queryOrientation,
        Vector3 direction,
        float pathDistance,
        LayerMask obstacleMask)
    {
        while (true)
        {
            int hitCount =
                Physics.BoxCastNonAlloc(
                    castOrigin,
                    queryHalfExtents,
                    direction,
                    boxCastHitBuffer,
                    queryOrientation,
                    pathDistance,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore);

            if (hitCount <
                boxCastHitBuffer.Length)
            {
                return hitCount;
            }

            Array.Resize(
                ref boxCastHitBuffer,
                boxCastHitBuffer.Length * 2);
        }
    }

    /// <summary>
    /// Collider自身または親階層にPlayer Tagが存在するか確認する。
    /// </summary>
    /// <remarks>
    /// Player TagはPlayer Rootだけに設定される前提。
    /// transform.rootはPlayerより上位の管理用GameObjectを返す構成があるため使用しない。
    /// </remarks>
    private static bool IsPlayerCollider(
        Collider targetCollider)
    {
        if (targetCollider == null)
        {
            return false;
        }

        for (Transform current =
                 targetCollider.transform;
             current != null;
             current = current.parent)
        {
            if (current.CompareTag(
                    PLAYER_TAG))
            {
                return true;
            }
        }

        return false;
    }

    private static void ValidateSearchArguments(
        Vector3 queryHalfExtents,
        Vector3 startEscapeHalfExtents,
        float searchPadding,
        int maximumExpandedNodes)
    {
        ValidateStartEscapeGeometry(
            queryHalfExtents,
            startEscapeHalfExtents);

        if (searchPadding < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(searchPadding),
                "Search Paddingは0以上が必要です。");
        }

        if (maximumExpandedNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumExpandedNodes),
                "Maximum Expanded Nodesは1以上が必要です。");
        }
    }

    private static void ValidateStartEscapeGeometry(
        Vector3 navigationHalfExtents,
        Vector3 startEscapeHalfExtents)
    {
        ValidateQueryGeometry(
            navigationHalfExtents);

        ValidateQueryGeometry(
            startEscapeHalfExtents);

        if (startEscapeHalfExtents.x >
                navigationHalfExtents.x + QUERY_EPSILON ||
            startEscapeHalfExtents.y >
                navigationHalfExtents.y + QUERY_EPSILON ||
            startEscapeHalfExtents.z >
                navigationHalfExtents.z + QUERY_EPSILON)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startEscapeHalfExtents),
                "Start Escape Half ExtentsはNavigation Half Extents以下である必要があります。");
        }
    }

    private static void ValidateQueryGeometry(
        Vector3 queryHalfExtents)
    {
        if (queryHalfExtents.x <= 0f ||
            queryHalfExtents.y <= 0f ||
            queryHalfExtents.z <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(queryHalfExtents),
                "Query Half Extentsは全軸で0より大きい値が必要です。");
        }
    }
}
