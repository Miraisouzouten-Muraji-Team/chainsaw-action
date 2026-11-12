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
    private const float STANDING_NORMAL_SPEED_TOLERANCE = 0.4f;
    private const float PROBE_SKIN = 0.025f;
    private const float PATH_DISTANCE_EPSILON = 0.001f;

    private readonly ChargeEnemyData enemyData;
    private readonly Transform enemyTransform;
    private readonly Rigidbody enemyRigidbody;
    private readonly Collider enemyCollider;
    private readonly RaycastHit[] raycastHitBuffer;
    private readonly RaycastHit[] wallCastHitBuffer;
    private readonly EnemyDebugRay[] debugRays;

    private int debugRayCount;

    /// <summary>
    /// 直近の移動可否判定が失敗した理由。成功時は空文字。
    /// 帰還・突進側の診断ログで使用する。
    /// </summary>
    public string LastFailureReason { get; private set; } = string.Empty;

    public ChargeEnemyGroundChecker(
        ChargeEnemyData enemyData,
        Transform enemyTransform,
        Rigidbody enemyRigidbody,
        Collider enemyCollider)
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

        raycastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];

        wallCastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];

        debugRays =
            new EnemyDebugRay[MAX_DEBUG_RAY_COUNT];
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
                frontGround))
        {
            LastFailureReason = "進行方向のStage壁に阻まれています。";
            return false;
        }

        groundNormal = frontGround.normal;
        return true;
    }

    /// <summary>
    /// 現在位置がStageに支えられているかを判定する。
    /// 帰還不能時の原点更新にだけ使い、落下中の座標を原点として確定しない。
    /// </summary>
    public bool IsStandingOnStageGround()
    {
        Bounds bounds = enemyCollider.bounds;
        return bounds.size.sqrMagnitude > DIRECTION_EPSILON &&
               TryGetSupportingGroundHit(bounds, out _);
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
        if (!TryGetNearestGroundHit(rayOrigin, rayDistance, out RaycastHit hit) ||
            !IsSlopeAllowed(hit.normal))
        {
            return false;
        }

        Vector3 closestBodyPoint = enemyCollider.ClosestPoint(hit.point);
        float supportSeparationSquared = (closestBodyPoint - hit.point).sqrMagnitude;
        if (supportSeparationSquared >
            STANDING_HEIGHT_TOLERANCE * STANDING_HEIGHT_TOLERANCE)
        {
            return false;
        }

        // 先読みした上り坂へ向かう上向き速度は、支持面がまだ平地の間、
        // 正の法線速度になる。上昇速度だけで離地扱いにせず、上で検証した
        // Colliderと地面の距離を優先して坂入口での待機状態への移行を防ぐ。
        // 高速で下降中かつSkinより離れている場合の非接地判定は維持する。
        Vector3 planarVelocity = enemyRigidbody.linearVelocity;
        planarVelocity.z = 0f;
        float normalSpeed = Vector3.Dot(planarVelocity, hit.normal);
        if (normalSpeed < -STANDING_NORMAL_SPEED_TOLERANCE &&
            supportSeparationSquared > PROBE_SKIN * PROBE_SKIN)
        {
            return false;
        }

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
        if (Mathf.Abs(directionSign) <= DIRECTION_EPSILON ||
            maximumDistance <= PATH_DISTANCE_EPSILON ||
            !IsStandingOnStageGround())
        {
            return 0f;
        }

        // 探索距離が極端に長い場合は未検証区間へ進めない。
        float scanLimit = Mathf.Min(maximumDistance, PATH_SAMPLE_SPACING * MAX_PATH_SAMPLE_COUNT);
        Bounds bounds = enemyCollider.bounds;
        if (!TryGetNearestGroundHit(
                bounds.center,
                bounds.extents.y + enemyData.GroundCheckDownDistance,
                out RaycastHit startGround) ||
            !IsSlopeAllowed(startGround.normal))
        {
            return 0f;
        }

        float sign = Mathf.Sign(directionSign);
        float initialFrontX = bounds.center.x + sign *
            (bounds.extents.x + enemyData.GroundCheckForwardOffset);
        if (!TryTraceGroundPath(startGround, initialFrontX, bounds.center.z,
                out RaycastHit frontGround, false))
        {
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
                    out RaycastHit nextGround) ||
                !TryGetNextGroundHit(frontGround, nextFrontX, bounds.center.z,
                    out RaycastHit nextFrontGround))
            {
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
                    nextFrontGround))
            {
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
        for (int i = 1; i <= sampleCount; i++)
        {
            float nextX = Mathf.Lerp(startX, targetX, (float)i / sampleCount);
            if (!TryGetNextGroundHit(lastGround, nextX, targetZ, out RaycastHit nextGround))
            {
                if (setFailureReason)
                {
                    LastFailureReason =
                        $"進行経路の地面が未検出・許容角超過・高さ不連続です。 " +
                        $"sampleX={nextX:F3}, lastGroundY={lastGround.point.y:F3}, " +
                        $"maxSlope={enemyData.MaxGroundSlopeAngle:F2}, " +
                        $"maxHeightDifference={enemyData.MaxGroundHeightDifference:F3}";
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
        nextGround = default;
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
        if (!TryGetNearestGroundHit(rayOrigin, rayDistance, out RaycastHit hit) ||
            !IsSlopeAllowed(hit.normal))
        {
            return false;
        }

        float heightDifference = Mathf.Abs(hit.point.y - previousGround.point.y);
        float permittedHeightDifference = Mathf.Max(
            Mathf.Min(enemyData.MaxGroundHeightDifference, enemyData.GroundCheckDownDistance),
            Mathf.Min(slopeHeightAllowance, GetGroundTangentRiseLimit(
                previousGround.normal, hit.normal, horizontalDistance) + PROBE_SKIN));
        if (heightDifference > permittedHeightDifference + PATH_DISTANCE_EPSILON)
        {
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
        RaycastHit traversableGroundAhead)
    {
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
            // 別Colliderの壁、平坦な箱の段差、坂の終端はこの条件を満たさない。
            if (IsSameTraversableSlope(
                    hit.collider,
                    traversableGroundAhead))
            {
                continue;
            }

            // 正面を向いた接触だけを障害物とし、許容斜面の斜め法線を壁扱いしない。
            if (hit.normal.x * directionSign < -DIRECTION_EPSILON &&
                !IsSlopeAllowed(hit.normal))
            {
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

        return IsSameTraversableSlope(
            candidateCollider,
            traversableGroundAhead);
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
