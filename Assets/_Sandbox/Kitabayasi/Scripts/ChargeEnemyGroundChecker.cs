using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 突進エネミーの地面・斜面・進行方向の通行可否を判定する。
/// </summary>
/// <remarks>
/// 現在足元と進行方向前方へ下向きRayを飛ばし、
/// 地面の存在、高さ差、傾斜から移動可否を判定する。
///
/// 巡回時の反転や突進停止など、
/// 移動不可判定後の行動は各Strategyへ委ねる。
/// Stage Geometryの対象LayerはChargeEnemyDataから受け取り、
/// 判定対象をStage系Colliderへ限定する。
/// </remarks>
public sealed class ChargeEnemyGroundChecker : IEnemyDebugRayProvider
{
    private const int RAYCAST_HIT_BUFFER_SIZE = 16;
    private const int MAX_DEBUG_RAY_COUNT = 3;
    private const float DIRECTION_EPSILON = 0.001f;
    private const float PATH_SAMPLE_SPACING = 0.1f;
    private const int MAX_PATH_SAMPLE_COUNT = 512;
    private const float STANDING_HEIGHT_TOLERANCE = 0.12f;
    private const float PROBE_SKIN = 0.025f;
    private const float PATH_DISTANCE_EPSILON = 0.001f;
    private const float GAP_SEARCH_SPACING = 0.025f;
    private const float MIN_POSITION_CORRECTION = 0.008f;
    private const float POSITION_CLEARANCE_SKIN = 0.003f;
    private const float MIN_UPWARD_SEPARATION_NORMAL = 0.35f;
    private const int OVERLAP_BUFFER_SIZE = 32;

    private readonly ChargeEnemyData enemyData;
    private readonly Transform enemyTransform;
    private readonly Rigidbody enemyRigidbody;
    private readonly Collider enemyCollider;
    // Attackから共有する判定は従来仕様のままにし、接続誤差の許容はSearchでだけ有効化する。
    private readonly bool allowMinorStageConnections;
    private readonly RaycastHit[] raycastHitBuffer;
    private readonly RaycastHit[] wallCastHitBuffer;
    private readonly Collider[] nearbyStageColliders;
    private readonly EnemyDebugRay[] debugRays;

    private int debugRayCount;

    /// <summary>
    /// 直近の移動可否判定が失敗した理由。成功時は空文字。
    /// 帰還・突進側の診断ログで使用する。
    /// </summary>
    public string LastFailureReason { get; private set; } = string.Empty;

    /// <summary>直近の支持地面検査が失敗した理由。最後に検査したRayの結果を含む。</summary>
    public string LastSupportFailureReason { get; private set; } = string.Empty;

    /// <summary>最後の巡回距離計測が停止した理由。距離計測のたびに更新する。</summary>
    public string LastPathScanFailureReason { get; private set; } = string.Empty;

    private string lastPathStepFailureReason = string.Empty;
    private string lastBlockingWallHitDetails = string.Empty;

    // 直前に成功した移動可否判定の前方支持面。Search内でのみ段差乗り越えに使用する。
    public Collider LastTraversableGroundCollider { get; private set; }
    public float LastUpwardConnectionRise { get; private set; }

    public ChargeEnemyGroundChecker(
        ChargeEnemyData enemyData,
        Transform enemyTransform,
        Rigidbody enemyRigidbody,
        Collider enemyCollider,
        bool allowMinorStageConnections = false)
    {
        this.enemyData = enemyData
            ?? throw new ArgumentNullException(nameof(enemyData));

        this.enemyTransform = enemyTransform
            ? enemyTransform
            : throw new ArgumentNullException(nameof(enemyTransform));

        this.enemyRigidbody = enemyRigidbody
            ? enemyRigidbody
            : throw new ArgumentNullException(nameof(enemyRigidbody));

        this.enemyCollider = enemyCollider
            ? enemyCollider
            : throw new ArgumentNullException(nameof(enemyCollider));

        this.allowMinorStageConnections = allowMinorStageConnections;

        raycastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];

        wallCastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];

        debugRays =
            new EnemyDebugRay[MAX_DEBUG_RAY_COUNT];
        nearbyStageColliders = new Collider[OVERLAP_BUFFER_SIZE];
    }

    /// <summary>
    /// 次の物理更新分の進行経路が、接続した通行可能な地面の上にあるか判定する。
    /// 既存の呼び出し元向けAPI。
    /// </summary>
    public bool CanMoveInDirection(
        float moveDirectionSign,
        float expectedMovementDistance)
    {
        return TryGetTraversableGroundNormal(
            moveDirectionSign,
            expectedMovementDistance,
            out _);
    }

    /// <summary>
    /// 現在の接地点のワールド法線を取得する。
    /// Stage上に支持されていない場合はfalseを返し、法線をVector3.zeroにする。
    /// </summary>
    public bool TryGetCurrentGroundNormal(out Vector3 groundNormal)
    {
        groundNormal = Vector3.zero;
        Bounds bounds = enemyCollider.bounds;
        if (bounds.size.sqrMagnitude <= DIRECTION_EPSILON)
        {
            LastSupportFailureReason = $"ColliderのBoundsが無効: size={bounds.size}";
            return false;
        }

        if (!TryGetSupportingGroundHit(bounds, out RaycastHit groundHit))
        {
            return false;
        }

        groundNormal = groundHit.normal;
        return true;
    }

    /// <summary>
    /// 現在足元から次の移動で到達する前端までの地面を検証し、
    /// 通行可能なら最前方の地面法線をワールド座標で返す。
    /// 失敗時に前回の法線を使わせないため、必ずzeroを返す。
    /// </summary>
    /// <remarks>
    /// Strategy側ではCanMoveInDirectionと別々に呼ばず、
    /// このメソッド一回で移動可否と進行先の法線を取得する。
    /// 法線から移動接線を作る際はXY成分だけを使い、Z速度を与えない。
    /// </remarks>
    public bool TryGetTraversableGroundNormal(
        float moveDirectionSign,
        float expectedMovementDistance,
        out Vector3 groundNormal)
    {
        groundNormal = Vector3.zero;
        ClearDebugRays();
        LastFailureReason = string.Empty;
        LastTraversableGroundCollider = null;
        LastUpwardConnectionRise = 0f;

        if (Mathf.Abs(moveDirectionSign) <= DIRECTION_EPSILON)
        {
            LastFailureReason =
                $"移動方向が0に近いため判定できません。 moveDirectionSign={moveDirectionSign:F4}";
            return false;
        }

        Bounds bounds = enemyCollider.bounds;
        if (bounds.size.sqrMagnitude <= DIRECTION_EPSILON)
        {
            LastFailureReason =
                $"Enemy ColliderのBoundsが無効です。 bounds.size={bounds.size}";
            return false;
        }

        float currentRayDistance = bounds.extents.y + enemyData.GroundCheckDownDistance;
        if (!TryGetNearestGroundHit(bounds.center, currentRayDistance, out RaycastHit currentGround))
        {
            LastFailureReason =
                $"現在足元の地面RayがStage LayerにHitしませんでした。 " +
                $"origin={bounds.center}, distance={currentRayDistance:F3}, " +
                $"stageLayerMask={enemyData.StageLayerMask.value}";
            return false;
        }

        if (!IsSlopeAllowed(currentGround.normal))
        {
            LastFailureReason =
                $"現在足元の傾斜角が許容値を超えています。 " +
                $"angle={Vector3.Angle(currentGround.normal, Vector3.up):F2}, " +
                $"max={enemyData.MaxGroundSlopeAngle:F2}, " +
                $"collider={currentGround.collider?.name}";
            return false;
        }

        float sign = Mathf.Sign(moveDirectionSign);
        float minimumForwardDistance = enemyData.GroundCheckForwardOffset;
        float projectedForwardDistance = Mathf.Max(
            minimumForwardDistance,
            Mathf.Max(0f, expectedMovementDistance));

        // Collider前端直後を最初に確認し、長い移動量が直近の穴を飛び越えないようにする。
        float immediateFrontX = bounds.center.x + sign *
            (bounds.extents.x + minimumForwardDistance);
        if (!TryTraceGroundPath(currentGround, immediateFrontX, bounds.center.z,
                out RaycastHit immediateGround, true))
        {
            return false;
        }

        RaycastHit frontGround = immediateGround;
        if (projectedForwardDistance > minimumForwardDistance + DIRECTION_EPSILON)
        {
            float projectedFrontX = bounds.center.x + sign *
                (bounds.extents.x + projectedForwardDistance);
            if (!TryTraceGroundPath(immediateGround, projectedFrontX, bounds.center.z,
                    out frontGround, true))
            {
                return false;
            }
        }

        // 地面を前方まで辿れても、胴体の進行経路にある実際の壁は通過させない。
        float moveDistance = Mathf.Max(0f, expectedMovementDistance);
        Vector3 startCenter = bounds.center;
        // 移動先の前端高さは胴体中心の次フレーム高さではないため、
        // 壁用の掃引は現在の胴体を水平方向に動かして行う。
        Vector3 endCenter = startCenter + Vector3.right * sign * moveDistance;
        if (HasBlockingStageWall(
                startCenter,
                endCenter,
                bounds.extents,
                sign,
                currentGround,
                frontGround))
        {
            LastFailureReason = $"進行方向のStage壁に阻まれています。 {lastBlockingWallHitDetails}";
            return false;
        }

        groundNormal = frontGround.normal;
        LastTraversableGroundCollider = frontGround.collider;
        return true;
    }

    /// <summary>
    /// 現在位置がStageに支えられているかを判定する。
    /// 帰還不能時の原点更新にだけ使い、落下中の座標を原点として確定しない。
    /// </summary>
    public bool IsStandingOnStageGround()
    {
        Bounds bounds = enemyCollider.bounds;
        if (bounds.size.sqrMagnitude <= DIRECTION_EPSILON)
        {
            LastSupportFailureReason = $"ColliderのBoundsが無効: size={bounds.size}";
            return false;
        }

        return TryGetSupportingGroundHit(bounds, out _);
    }

    private bool TryGetSupportingGroundHit(Bounds bounds, out RaycastHit supportingHit)
    {
        supportingHit = default;
        float rayDistance = bounds.extents.y +
            Mathf.Max(enemyData.GroundCheckDownDistance, STANDING_HEIGHT_TOLERANCE);

        // 中央だけでは、斜面上でColliderの片側が接触するケースを見逃す。
        // 実際のCollider表面と地面の距離を見ることで、bounds.min.yとの差を使わない。
        if (TryGetStandingHitAt(bounds.center, rayDistance, out supportingHit))
        {
            return true;
        }

        float supportOffset = Mathf.Max(0f, bounds.extents.x - PROBE_SKIN);
        if (supportOffset <= DIRECTION_EPSILON)
        {
            supportingHit = default;
            return false;
        }

        if (TryGetStandingHitAt(
                bounds.center + Vector3.left * supportOffset, rayDistance, out supportingHit) ||
            TryGetStandingHitAt(
                bounds.center + Vector3.right * supportOffset, rayDistance, out supportingHit))
        {
            return true;
        }

        supportingHit = default;
        return false;
    }

    private bool TryGetStandingHitAt(
        Vector3 rayOrigin,
        float rayDistance,
        out RaycastHit supportingHit)
    {
        supportingHit = default;
        if (!TryGetNearestGroundHit(rayOrigin, rayDistance, out RaycastHit hit))
        {
            LastSupportFailureReason =
                $"支持Ray未Hit: origin={rayOrigin}, distance={rayDistance:F3}";
            return false;
        }

        if (!IsSlopeAllowed(hit.normal))
        {
            LastSupportFailureReason =
                $"支持面の傾斜が通行不可: collider={hit.collider?.name}, " +
                $"angle={Vector3.Angle(hit.normal, Vector3.up):F2}, " +
                $"max={enemyData.MaxGroundSlopeAngle:F2}, origin={rayOrigin}";
            return false;
        }

        Vector3 closestBodyPoint = enemyCollider.ClosestPoint(hit.point);
        float supportSeparationSquared = (closestBodyPoint - hit.point).sqrMagnitude;
        if (supportSeparationSquared >
            STANDING_HEIGHT_TOLERANCE * STANDING_HEIGHT_TOLERANCE)
        {
            LastSupportFailureReason =
                $"支持面までの間隔が超過: collider={hit.collider?.name}, " +
                $"separation={Mathf.Sqrt(supportSeparationSquared):F3}, " +
                $"tolerance={STANDING_HEIGHT_TOLERANCE:F3}, " +
                $"hit={hit.point}, origin={rayOrigin}";
            return false;
        }

        // 接地面法線方向の下降速度は、支持面へ接近していることも意味する。
        // 速度だけで非接地とすると、下り坂の継ぎ目や初期着地時に
        // 近くの通行可能な地面を検出できても巡回が停止する。
        // 接地の許容範囲は上記の地面法線とCollider表面までの距離で判断する。

        LastSupportFailureReason = string.Empty;
        supportingHit = hit;
        return true;
    }

    /// <summary>
    /// 既に巡回基準のX座標付近まで来ている場合、同じ高さに帰還したか確認する。
    /// 経路を一括判定するものではなく、実際に移動した結果の到着判定に使う。
    /// </summary>
    public bool IsAtGroundHeight(float targetBodyY)
    {
        return IsStandingOnStageGround() &&
               Mathf.Abs(targetBodyY - enemyRigidbody.position.y) <=
               Mathf.Max(enemyData.MaxGroundHeightDifference, STANDING_HEIGHT_TOLERANCE);
    }

    /// <summary>
    /// 指定方向へ直線移動できる距離を、地面の連続性・高低差・傾斜・壁から測定する。
    /// 値は設定距離を超えず、敵の移動そのものは行わない。
    /// </summary>
    public float MeasureSafeHorizontalDistance(float directionSign, float maximumDistance)
    {
        return MeasureSafeHorizontalDistance(directionSign, maximumDistance, out _);
    }

    private float MeasureSafeHorizontalDistance(
        float directionSign,
        float maximumDistance,
        out float reachedBodyY)
    {
        reachedBodyY = enemyRigidbody.position.y;
        LastPathScanFailureReason = string.Empty;
        if (Mathf.Abs(directionSign) <= DIRECTION_EPSILON ||
            maximumDistance <= PATH_DISTANCE_EPSILON ||
            !IsStandingOnStageGround())
        {
            LastPathScanFailureReason =
                $"開始条件不成立: direction={directionSign:F2}, maxDistance={maximumDistance:F3}, " +
                $"support={LastSupportFailureReason}";
            return 0f;
        }

        // 探索距離が極端に長い場合は未検証区間へ進めない。
        float scanLimit = Mathf.Min(maximumDistance, PATH_SAMPLE_SPACING * MAX_PATH_SAMPLE_COUNT);
        Bounds bounds = enemyCollider.bounds;
        if (!TryGetNearestGroundHit(
                bounds.center,
                bounds.extents.y + enemyData.GroundCheckDownDistance,
                out RaycastHit startGround))
        {
            LastPathScanFailureReason = $"開始地点の地面Ray未Hit: center={bounds.center}";
            return 0f;
        }

        if (!IsSlopeAllowed(startGround.normal))
        {
            LastPathScanFailureReason =
                $"開始地面の傾斜角が許容外: collider={startGround.collider?.name}, " +
                $"angle={Vector3.Angle(startGround.normal, Vector3.up):F2}";
            return 0f;
        }

        float sign = Mathf.Sign(directionSign);
        float initialFrontX = bounds.center.x + sign *
            (bounds.extents.x + enemyData.GroundCheckForwardOffset);
        if (!TryTraceGroundPath(startGround, initialFrontX, bounds.center.z,
                out RaycastHit frontGround, false))
        {
            LastPathScanFailureReason =
                $"開始地点から前端への地面追跡失敗: targetX={initialFrontX:F3}, " +
                lastPathStepFailureReason;
            return 0f;
        }

        float startGroundHeight = startGround.point.y;
        float measuredDistance = 0f;
        RaycastHit centerGround = startGround;
        int sampleCount = Mathf.CeilToInt(scanLimit / PATH_SAMPLE_SPACING);

        for (int i = 1; i <= sampleCount; i++)
        {
            float nextDistance = Mathf.Min(i * PATH_SAMPLE_SPACING, scanLimit);
            float stepDistance = nextDistance - measuredDistance;
            if (stepDistance <= PATH_DISTANCE_EPSILON)
            {
                break;
            }

            float nextX = bounds.center.x + sign * nextDistance;
            float nextFrontX = nextX + sign *
                (bounds.extents.x + enemyData.GroundCheckForwardOffset);

            // 中央と前端の双方を前回の接地点から辿るため、下り坂や継ぎ目を
            // 「元の平地Y座標から届かない」という理由で切り捨てない。
            if (!TryGetNextGroundHit(centerGround, nextX, bounds.center.z,
                    out RaycastHit nextGround))
            {
                LastPathScanFailureReason =
                    $"中心の地面追跡失敗: sampleX={nextX:F3}, " + lastPathStepFailureReason;
                break;
            }

            if (!TryGetNextGroundHit(frontGround, nextFrontX, bounds.center.z,
                    out RaycastHit nextFrontGround))
            {
                LastPathScanFailureReason =
                    $"前端の地面追跡失敗: sampleX={nextFrontX:F3}, " + lastPathStepFailureReason;
                break;
            }

            Vector3 fromCenter = new Vector3(
                bounds.center.x + sign * measuredDistance,
                bounds.center.y + (centerGround.point.y - startGroundHeight),
                bounds.center.z);
            Vector3 toCenter = new Vector3(
                nextX,
                bounds.center.y + (nextGround.point.y - startGroundHeight),
                bounds.center.z);

            if (HasBlockingStageWall(
                    fromCenter,
                    toCenter,
                    bounds.extents,
                    sign,
                    centerGround,
                    nextFrontGround))
            {
                LastPathScanFailureReason =
                    $"BoxCastが壁判定: sampleX={nextX:F3}, " + lastBlockingWallHitDetails;
                break;
            }

            measuredDistance = nextDistance;
            centerGround = nextGround;
            frontGround = nextFrontGround;
            reachedBodyY = enemyRigidbody.position.y +
                (nextGround.point.y - startGroundHeight);
        }

        return measuredDistance;
    }

    /// <summary>
    /// 既に検証済みの接地点から指定X座標まで、短い区間に分けて地面を辿る。
    /// 大きな1本のRayだけで遠くの足場を「連続した坂」と誤認しないための処理。
    /// </summary>
    private bool TryTraceGroundPath(
        RaycastHit startGround,
        float targetX,
        float targetZ,
        out RaycastHit lastGround,
        bool setFailureReason)
    {
        lastGround = startGround;
        float totalDistance = Mathf.Abs(targetX - startGround.point.x);
        if (totalDistance <= PATH_DISTANCE_EPSILON)
        {
            return true;
        }

        int sampleCount = Mathf.CeilToInt(totalDistance / PATH_SAMPLE_SPACING);
        if (sampleCount > MAX_PATH_SAMPLE_COUNT)
        {
            if (setFailureReason)
            {
                LastFailureReason = "地面検査距離が一度に検証可能な上限を超えています。";
            }
            lastGround = default;
            return false;
        }

        float startX = startGround.point.x;
        float traversalSign = Mathf.Sign(targetX - startX);
        for (int i = 1; i <= sampleCount; i++)
        {
            float nextX = Mathf.Lerp(startX, targetX, (float)i / sampleCount);
            // 隙間を越えて先の支持面を見つけた場合、次サンプルで後退しない。
            if ((nextX - lastGround.point.x) * traversalSign < -PATH_DISTANCE_EPSILON)
            {
                continue;
            }
            if (!TryGetNextGroundHit(lastGround, nextX, targetZ, out RaycastHit nextGround))
            {
                if (setFailureReason)
                {
                    LastFailureReason =
                        $"進行経路の地面が未検出・許容角超過・高さ不連続です。 " +
                        $"sampleX={nextX:F3}, lastGroundY={lastGround.point.y:F3}, " +
                        $"maxSlope={enemyData.MaxGroundSlopeAngle:F2}, " +
                        $"maxHeightDifference={enemyData.MaxGroundHeightDifference:F3}, " +
                        $"detail={lastPathStepFailureReason}";
                }
                lastGround = default;
                return false;
            }

            lastGround = nextGround;
        }

        return true;
    }

    private bool TryGetNextGroundHit(
        RaycastHit previousGround,
        float nextX,
        float nextZ,
        out RaycastHit nextGround)
    {
        if (!allowMinorStageConnections)
        {
            return TryGetStrictNextGroundHit(previousGround, nextX, nextZ,
                out nextGround);
        }

        nextGround = default;
        lastPathStepFailureReason = string.Empty;
        float directionSign = Mathf.Sign(nextX - previousGround.point.x);
        if (Mathf.Abs(directionSign) <= DIRECTION_EPSILON)
        {
            nextGround = previousGround;
            return true;
        }

        // 欠けた地面Rayを無条件に崖とはみなさず、ごく短い範囲だけ次の上面を探す。
        // 検索に失敗した場合、未確認の空間を通行可能とは扱わない。
        float gapLimit = Mathf.Max(0f, enemyData.GroundConnectionGapTolerance);
        int gapSamples = Mathf.CeilToInt(gapLimit / GAP_SEARCH_SPACING);
        for (int sample = 0; sample <= gapSamples; sample++)
        {
            float extraDistance = sample == 0 ? 0f :
                Mathf.Min(sample * GAP_SEARCH_SPACING, gapLimit);
            float sampleX = nextX + directionSign * extraDistance;
            if (!TryGetGroundAtPathSample(previousGround, sampleX, nextZ,
                    out RaycastHit hit))
            {
                // 斜面上面が見つかっても、許容外の高さ/角度ならその先を探さない。
                // 隣接する別の安全な地面を探して本物の壁を飛ばさないため。
                if (!string.IsNullOrEmpty(lastPathStepFailureReason) &&
                    !lastPathStepFailureReason.StartsWith("地面Ray未Hit"))
                {
                    return false;
                }
                continue;
            }

            if (sample > 0 &&
                Mathf.Abs(hit.point.x - previousGround.point.x) >
                    gapLimit + PATH_SAMPLE_SPACING * 0.5f + PATH_DISTANCE_EPSILON)
            {
                lastPathStepFailureReason =
                    $"隙間の長さが許容外: fromX={previousGround.point.x:F3}, " +
                    $"toX={hit.point.x:F3}, maxGap={gapLimit:F3}";
                return false;
            }

            nextGround = hit;
            return true;
        }

        return false;
    }

    private bool TryGetGroundAtPathSample(
        RaycastHit previousGround,
        float sampleX,
        float sampleZ,
        out RaycastHit nextGround)
    {
        nextGround = default;
        float horizontalDistance = Mathf.Abs(sampleX - previousGround.point.x);
        float slopeHeightAllowance = GetSlopeHeightAllowance(horizontalDistance);
        float connectionAllowance = Mathf.Min(
            enemyData.MaxGroundHeightDifference,
            enemyData.GroundConnectionHeightTolerance);
        float rayRise = slopeHeightAllowance + connectionAllowance + PROBE_SKIN;
        float rayOriginY = previousGround.point.y + rayRise;
        float rayDistance = rayRise + Mathf.Max(
            enemyData.GroundCheckDownDistance,
            slopeHeightAllowance + connectionAllowance) + PROBE_SKIN;
        Vector3 rayOrigin = new Vector3(sampleX, rayOriginY, sampleZ);
        if (!TryGetNearestGroundHit(rayOrigin, rayDistance, out RaycastHit hit))
        {
            lastPathStepFailureReason =
                $"地面Ray未Hit: from={previousGround.point}, origin={rayOrigin}, " +
                $"distance={rayDistance:F3}";
            return false;
        }

        if (!IsSlopeAllowed(hit.normal))
        {
            lastPathStepFailureReason =
                $"角度・法線が通行不可: collider={hit.collider?.name}, " +
                $"normal={hit.normal}, max={enemyData.MaxGroundSlopeAngle:F2}";
            return false;
        }

        bool isColliderTransition = hit.collider != previousGround.collider;
        float naturalRise = GetGroundTangentRiseLimit(
            previousGround.normal, hit.normal, horizontalDistance);
        float permittedHeightDifference = naturalRise + PROBE_SKIN +
            (isColliderTransition ? connectionAllowance : 0f);
        float heightDifference = Mathf.Abs(hit.point.y - previousGround.point.y);
        if (heightDifference > permittedHeightDifference + PATH_DISTANCE_EPSILON)
        {
            lastPathStepFailureReason =
                $"高さ差が許容外: collider={hit.collider?.name}, " +
                $"difference={heightDifference:F3}, permitted={permittedHeightDifference:F3}, " +
                $"from={previousGround.point}, hit={hit.point}";
            return false;
        }

        if (isColliderTransition)
        {
            // 自然な斜面勾配を超えた上向きの接続誤差だけを、段差補正の候補とする。
            float deltaX = hit.point.x - previousGround.point.x;
            float previousRise = -previousGround.normal.x * deltaX /
                Mathf.Max(previousGround.normal.y, DIRECTION_EPSILON);
            float nextRise = -hit.normal.x * deltaX /
                Mathf.Max(hit.normal.y, DIRECTION_EPSILON);
            float expectedRise = Mathf.Max(0f, Mathf.Max(previousRise, nextRise));
            float extraRise = hit.point.y - previousGround.point.y - expectedRise;
            if (extraRise > MIN_POSITION_CORRECTION)
            {
                LastUpwardConnectionRise = Mathf.Max(
                    LastUpwardConnectionRise, Mathf.Min(extraRise, connectionAllowance));
            }
        }

        nextGround = hit;
        return true;
    }

    private bool TryGetStrictNextGroundHit(
        RaycastHit previousGround,
        float nextX,
        float nextZ,
        out RaycastHit nextGround)
    {
        nextGround = default;
        lastPathStepFailureReason = string.Empty;
        float horizontalDistance = Mathf.Abs(nextX - previousGround.point.x);
        float slopeHeightAllowance = GetSlopeHeightAllowance(horizontalDistance);

        // 直前の接地点を基準に次のRayを配置する。
        // 上り坂ではRay開始点が地形に埋まらず、下り坂では前フレームの高さに固定されない。
        // MaxGroundHeightDifferenceだけで広大な落下距離を許容しないよう、
        // 1サンプルの高度差はGroundCheckDownDistanceと斜面角からも制限する。
        float localStepAllowance = Mathf.Max(
            slopeHeightAllowance,
            Mathf.Min(enemyData.MaxGroundHeightDifference, enemyData.GroundCheckDownDistance));
        float rayOriginY = previousGround.point.y + localStepAllowance + PROBE_SKIN;
        float rayDistance = localStepAllowance * 2f +
            enemyData.GroundCheckDownDistance + PROBE_SKIN;
        Vector3 rayOrigin = new Vector3(nextX, rayOriginY, nextZ);
        if (!TryGetNearestGroundHit(rayOrigin, rayDistance, out RaycastHit hit))
        {
            lastPathStepFailureReason =
                $"地面Ray未Hit: from={previousGround.point}, origin={rayOrigin}, " +
                $"distance={rayDistance:F3}, allowance={localStepAllowance:F3}";
            return false;
        }

        if (!IsSlopeAllowed(hit.normal))
        {
            lastPathStepFailureReason =
                $"角度・法線が通行不可: collider={hit.collider?.name}, " +
                $"normal={hit.normal}, angle={Vector3.Angle(hit.normal, Vector3.up):F2}, " +
                $"max={enemyData.MaxGroundSlopeAngle:F2}, hit={hit.point}";
            return false;
        }

        float heightDifference = Mathf.Abs(hit.point.y - previousGround.point.y);
        float permittedHeightDifference = Mathf.Max(
            Mathf.Min(enemyData.MaxGroundHeightDifference, enemyData.GroundCheckDownDistance),
            Mathf.Min(slopeHeightAllowance, GetGroundTangentRiseLimit(
                previousGround.normal, hit.normal, horizontalDistance) + PROBE_SKIN));
        if (heightDifference > permittedHeightDifference + PATH_DISTANCE_EPSILON)
        {
            lastPathStepFailureReason =
                $"高さ差が許容外: collider={hit.collider?.name}, " +
                $"difference={heightDifference:F3}, permitted={permittedHeightDifference:F3}, " +
                $"from={previousGround.point}, hit={hit.point}";
            return false;
        }

        nextGround = hit;
        return true;
    }

    private float GetSlopeHeightAllowance(float horizontalDistance)
    {
        // 90度指定時にtan(90°)が発散しないよう、1区間で探索する高さを
        // Colliderの高さに制限する。垂直面自体はIsSlopeAllowedで拒否する。
        float slopeRadians = enemyData.MaxGroundSlopeAngle * Mathf.Deg2Rad;
        float maximumGrade = Mathf.Sin(slopeRadians) /
            Mathf.Max(Mathf.Cos(slopeRadians), DIRECTION_EPSILON);
        float maximumProbeRise = enemyCollider.bounds.size.y +
            enemyData.GroundCheckDownDistance;
        return Mathf.Min(horizontalDistance * maximumGrade, maximumProbeRise);
    }

    private float GetGroundTangentRiseLimit(
        Vector3 previousNormal,
        Vector3 nextNormal,
        float horizontalDistance)
    {
        // 同じ高さにある離れた足場や段差の垂直側面を、
        // 設定上限角だけを理由に滑らかな斜面とはみなさない。
        float previousGrade = Mathf.Abs(previousNormal.x) /
            Mathf.Max(previousNormal.y, DIRECTION_EPSILON);
        float nextGrade = Mathf.Abs(nextNormal.x) /
            Mathf.Max(nextNormal.y, DIRECTION_EPSILON);
        return Mathf.Max(previousGrade, nextGrade) * horizontalDistance;
    }

    private bool HasBlockingStageWall(
        Vector3 startCenter,
        Vector3 endCenter,
        Vector3 bodyExtents,
        float directionSign,
        RaycastHit currentGround,
        RaycastHit traversableGroundAhead)
    {
        lastBlockingWallHitDetails = string.Empty;
        // 接地床を壁として拾いにくくするため、足元を少し縮めたBoxを掃引する。
        Vector3 castExtents = new Vector3(
            Mathf.Max(bodyExtents.x - PROBE_SKIN, PROBE_SKIN),
            Mathf.Max(bodyExtents.y - PROBE_SKIN, PROBE_SKIN),
            Mathf.Max(bodyExtents.z - PROBE_SKIN, PROBE_SKIN));
        Vector3 movement = endCenter - startCenter;
        float castDistance = movement.magnitude;
        if (castDistance <= PATH_DISTANCE_EPSILON)
        {
            return false;
        }

        int hitCount = Physics.BoxCastNonAlloc(
            startCenter + Vector3.up * PROBE_SKIN,
            castExtents,
            movement / castDistance,
            wallCastHitBuffer,
            Quaternion.identity,
            castDistance,
            enemyData.StageLayerMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = wallCastHitBuffer[i];
            if (hit.collider == null || IsEnemyOwnedCollider(hit.collider))
            {
                continue;
            }

            // 回転BoxColliderで作られた坂は、上面が許容角内でも入口に
            // 通行不可角度の端面を持つ。直前の地面追跡が同じColliderの
            // 斜面上面まで連続している場合だけ、その端面は坂入口として除外する。
            // 別Colliderの微小段差は上面の連続性と高さ条件を満たす場合だけ通す。
            // 通行可能な接続先以外の壁は従来どおり拒否する。
            if (allowMinorStageConnections
                    ? IsTraversableGroundEntrance(
                        hit.collider, currentGround, traversableGroundAhead)
                    : IsSameTraversableSlope(hit.collider, traversableGroundAhead))
            {
                continue;
            }

            // 正面を向いた接触だけを障害物とし、許容斜面の斜め法線を壁扱いしない。
            if (hit.normal.x * directionSign < -DIRECTION_EPSILON &&
                !IsSlopeAllowed(hit.normal))
            {
                lastBlockingWallHitDetails =
                    $"collider={hit.collider.name}, normal={hit.normal}, " +
                    $"angle={Vector3.Angle(hit.normal, Vector3.up):F2}, " +
                    $"hitPoint={hit.point}, " +
                    $"frontCollider={traversableGroundAhead.collider?.name}";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Collisionが検出した通行不可法線が、回転Colliderで作られた
    /// 通行可能な坂の入口端面かを確認する。
    /// </summary>
    /// <remarks>
    /// 接触法線だけでは坂の端面と壁を区別できないため、現在の接地点から
    /// Collider前端直後まで地面を辿り、同じColliderの斜面上面が連続している
    /// 場合だけ坂入口とみなす。壁判定そのものを無効にはしない。
    /// </remarks>
    public bool IsTraversableSlopeEntrance(
        Collider candidateCollider,
        float moveDirectionSign)
    {
        if (candidateCollider == null ||
            Mathf.Abs(moveDirectionSign) <= DIRECTION_EPSILON)
        {
            return false;
        }

        Bounds bounds = enemyCollider.bounds;
        if (bounds.size.sqrMagnitude <= DIRECTION_EPSILON ||
            !TryGetNearestGroundHit(
                bounds.center,
                bounds.extents.y + enemyData.GroundCheckDownDistance,
                out RaycastHit currentGround) ||
            !IsSlopeAllowed(currentGround.normal))
        {
            return false;
        }

        float directionSign = Mathf.Sign(moveDirectionSign);
        float immediateFrontX = bounds.center.x + directionSign *
            (bounds.extents.x + enemyData.GroundCheckForwardOffset);
        if (!TryTraceGroundPath(
                currentGround,
                immediateFrontX,
                bounds.center.z,
                out RaycastHit traversableGroundAhead,
                false))
        {
            return false;
        }

        return allowMinorStageConnections
            ? IsTraversableGroundEntrance(
                candidateCollider, currentGround, traversableGroundAhead)
            : IsSameTraversableSlope(candidateCollider, traversableGroundAhead);
    }

    private bool IsTraversableGroundEntrance(
        Collider candidateCollider,
        RaycastHit currentGround,
        RaycastHit traversableGroundAhead)
    {
        if (IsSameTraversableSlope(candidateCollider, traversableGroundAhead))
        {
            return true;
        }

        // 上面を連続追跡できた別Colliderの微小段差だけ例外とする。
        // 接続先と異なるColliderの壁、同一Colliderの真正面の壁は通過させない。
        if (candidateCollider == null ||
            candidateCollider != traversableGroundAhead.collider ||
            candidateCollider == currentGround.collider)
        {
            return false;
        }

        float heightDifference = traversableGroundAhead.point.y - currentGround.point.y;
        float tolerance = Mathf.Min(
            enemyData.MaxGroundHeightDifference,
            enemyData.GroundConnectionHeightTolerance);
        return heightDifference >= -tolerance &&
               heightDifference <= tolerance + GetGroundTangentRiseLimit(
                   currentGround.normal, traversableGroundAhead.normal,
                   Mathf.Abs(traversableGroundAhead.point.x - currentGround.point.x));
    }

    private bool IsSameTraversableSlope(
        Collider candidateCollider,
        RaycastHit traversableGroundAhead)
    {
        if (candidateCollider == null ||
            traversableGroundAhead.collider != candidateCollider ||
            !IsSlopeAllowed(traversableGroundAhead.normal))
        {
            return false;
        }

        // 平坦な箱の垂直端面は小段差であり、坂入口として除外しない。
        Vector3 planarNormal = new Vector3(
            traversableGroundAhead.normal.x,
            traversableGroundAhead.normal.y,
            0f);
        return planarNormal.sqrMagnitude > DIRECTION_EPSILON * DIRECTION_EPSILON &&
               Mathf.Abs(planarNormal.normalized.x) > DIRECTION_EPSILON;
    }

    private bool TryGetNearestGroundHit(
        Vector3 rayOrigin,
        float rayDistance,
        out RaycastHit nearestGroundHit)
    {
        RecordDebugRay(
            rayOrigin,
            Vector3.down,
            rayDistance);

        int hitCount =
            Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                raycastHitBuffer,
                rayDistance,
                enemyData.StageLayerMask,
                QueryTriggerInteraction.Ignore);

        float nearestDistance =
            float.PositiveInfinity;

        nearestGroundHit = default;

        bool hasGroundHit = false;

        for (int i = 0;
             i < hitCount;
             i++)
        {
            RaycastHit hit =
                raycastHitBuffer[i];

            Collider hitCollider =
                hit.collider;

            if (hitCollider == null ||
                IsEnemyOwnedCollider(hitCollider) ||
                hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance =
                hit.distance;

            nearestGroundHit =
                hit;

            hasGroundHit = true;
        }

        return hasGroundHit;
    }

    /// <summary>
    /// 地面に浅く食い込んだColliderについて、上方向だけの安全な分離量を求める。
    /// 水平方向の押し出しが必要な壁・大きな重なりは補正しない。
    /// </summary>
    public bool TryGetGroundPenetrationRecovery(out float upwardDistance)
    {
        upwardDistance = 0f;
        float maxRecovery = enemyData.MaxGroundPenetrationRecovery;
        if (maxRecovery <= MIN_POSITION_CORRECTION ||
            (enemyRigidbody.constraints & RigidbodyConstraints.FreezePositionY) != 0 ||
            !TryCollectNeighbours(Vector3.zero, enemyData.StageLayerMask, out int count))
        {
            return false;
        }

        Vector3 currentPosition = enemyCollider.transform.position;
        Quaternion currentRotation = enemyCollider.transform.rotation;
        for (int i = 0; i < count; i++)
        {
            Collider stage = nearbyStageColliders[i];
            if (!Physics.ComputePenetration(
                    enemyCollider, currentPosition, currentRotation,
                    stage, stage.transform.position, stage.transform.rotation,
                    out Vector3 separationNormal, out float penetration))
            {
                continue;
            }

            if (penetration <= MIN_POSITION_CORRECTION)
            {
                continue;
            }

            if (separationNormal.y < MIN_UPWARD_SEPARATION_NORMAL)
            {
                return false;
            }

            float requiredRise = penetration /
                separationNormal.y + POSITION_CLEARANCE_SKIN;
            if (requiredRise > maxRecovery)
            {
                return false;
            }
            upwardDistance = Mathf.Max(upwardDistance, requiredRise);
        }

        return upwardDistance >= MIN_POSITION_CORRECTION &&
               HasNearbySupportingSurface() &&
               CanLiftBodySafely(upwardDistance);
    }

    private bool HasNearbySupportingSurface()
    {
        Bounds bounds = enemyCollider.bounds;
        float rayLength = bounds.size.y +
            enemyData.MaxGroundPenetrationRecovery + STANDING_HEIGHT_TOLERANCE;
        float supportOffset = Mathf.Max(0f, bounds.extents.x - PROBE_SKIN);
        for (int i = -1; i <= 1; i++)
        {
            Vector3 origin = new Vector3(
                bounds.center.x + i * supportOffset,
                bounds.max.y + PROBE_SKIN,
                bounds.center.z);
            if (TryGetNearestGroundHit(origin, rayLength, out RaycastHit hit) &&
                IsSlopeAllowed(hit.normal) &&
                hit.point.y <= bounds.center.y &&
                hit.point.y >= bounds.min.y -
                    enemyData.MaxGroundPenetrationRecovery - STANDING_HEIGHT_TOLERANCE)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Searchの微小段差越えで使用する事前検証。Colliderの最終位置に
    /// 許容外の重なりがなく、頭上のStageを突き抜けない場合だけ許可する。
    /// </summary>
    public bool CanLiftBodySafely(float upwardDistance)
    {
        if (upwardDistance <= MIN_POSITION_CORRECTION ||
            upwardDistance > enemyData.MaxGroundPenetrationRecovery ||
            (enemyRigidbody.constraints & RigidbodyConstraints.FreezePositionY) != 0 ||
            !TryCollectNeighbours(Vector3.up * upwardDistance, Physics.AllLayers, out int count))
        {
            return false;
        }

        Vector3 candidatePosition = enemyCollider.transform.position +
            Vector3.up * upwardDistance;
        Quaternion candidateRotation = enemyCollider.transform.rotation;
        for (int i = 0; i < count; i++)
        {
            Collider stage = nearbyStageColliders[i];
            if (Physics.ComputePenetration(
                    enemyCollider, candidatePosition, candidateRotation,
                    stage, stage.transform.position, stage.transform.rotation,
                    out _, out float penetration) &&
                penetration > POSITION_CLEARANCE_SKIN)
            {
                return false;
            }
        }

        Bounds bounds = enemyCollider.bounds;
        Vector3 castExtents = bounds.extents -
            Vector3.one * PROBE_SKIN;
        castExtents = Vector3.Max(castExtents,
            Vector3.one * POSITION_CLEARANCE_SKIN);
        int hits = Physics.BoxCastNonAlloc(
            bounds.center, castExtents, Vector3.up,
            wallCastHitBuffer, Quaternion.identity,
            upwardDistance, enemyData.StageLayerMask,
            QueryTriggerInteraction.Ignore);
        if (hits >= wallCastHitBuffer.Length)
        {
            return false;
        }
        for (int i = 0; i < hits; i++)
        {
            RaycastHit hit = wallCastHitBuffer[i];
            if (hit.collider != null &&
                !IsEnemyOwnedCollider(hit.collider) &&
                hit.normal.y < -MIN_UPWARD_SEPARATION_NORMAL)
            {
                return false;
            }
        }
        return true;
    }

    private bool TryCollectNeighbours(Vector3 offset, int collisionMask, out int count)
    {
        Bounds bounds = enemyCollider.bounds;
        count = Physics.OverlapBoxNonAlloc(
            bounds.center + offset,
            bounds.extents + Vector3.one * POSITION_CLEARANCE_SKIN,
            nearbyStageColliders, Quaternion.identity,
            collisionMask, QueryTriggerInteraction.Ignore);
        if (count >= nearbyStageColliders.Length)
        {
            // 対象を取りこぼしたまま安全判定を通さない。
            return false;
        }

        int write = 0;
        for (int i = 0; i < count; i++)
        {
            Collider stage = nearbyStageColliders[i];
            if (stage != null && !IsEnemyOwnedCollider(stage))
            {
                nearbyStageColliders[write++] = stage;
            }
        }
        count = write;
        return true;
    }

    /// <summary>
    /// State切り替えなどで、直近のRayを現在のRayとして残したくない場合に破棄する。
    /// </summary>
    public void ClearDebugRays()
    {
        debugRayCount = 0;
    }

    /// <summary>
    /// 直近の判定で使用した地面Rayのうち、開始側2本と最終1本を提供する。
    /// </summary>
    public void CollectDebugRays(
        List<EnemyDebugRay> targetDebugRays)
    {
        if (targetDebugRays == null)
        {
            throw new ArgumentNullException(
                nameof(targetDebugRays));
        }

        for (int i = 0;
             i < debugRayCount;
             i++)
        {
            targetDebugRays.Add(
                debugRays[i]);
        }
    }

    private void RecordDebugRay(
        Vector3 origin,
        Vector3 direction,
        float distance)
    {
        // サンプル増加時も表示用メモリを増やさず、最後のRayを確認できるようにする。
        int recordIndex = Mathf.Min(debugRayCount, debugRays.Length - 1);
        debugRays[recordIndex] = new EnemyDebugRay(
            origin,
            direction,
            distance,
            EnemyDebugRayKind.GroundCheck);
        if (debugRayCount < debugRays.Length)
        {
            debugRayCount++;
        }
    }

    private bool IsSlopeAllowed(
        Vector3 groundNormal)
    {
        float slopeAngle =
            Vector3.Angle(
                groundNormal,
                Vector3.up);

        return groundNormal.y > DIRECTION_EPSILON &&
               slopeAngle <= enemyData.MaxGroundSlopeAngle;
    }

    private bool IsEnemyOwnedCollider(
        Collider targetCollider)
    {
        if (targetCollider.attachedRigidbody ==
            enemyRigidbody)
        {
            return true;
        }

        Transform targetTransform =
            targetCollider.transform;

        return
            targetTransform == enemyTransform ||
            targetTransform.IsChildOf(enemyTransform);
    }
}
