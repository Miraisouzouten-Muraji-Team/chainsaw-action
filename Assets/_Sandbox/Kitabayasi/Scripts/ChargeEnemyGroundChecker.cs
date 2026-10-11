using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 突進エネミーが現在の平坦面上を進行可能か判定する。
/// </summary>
/// <remarks>
/// 現在足元と進行方向前方へ下向きRayを飛ばし、
/// 地面の存在、高さ差、傾斜から移動可否を判定する。
///
/// 巡回時の反転や突進停止など、
/// 移動不可判定後の行動は各Strategyへ委ねる。
/// 特定のステージ領域や特定Colliderへの参照は保持しない。
/// </remarks>
public sealed class ChargeEnemyGroundChecker : IEnemyDebugRayProvider
{
    private const int RAYCAST_HIT_BUFFER_SIZE = 16;
    private const int MAX_DEBUG_RAY_COUNT = 3;
    private const float DIRECTION_EPSILON = 0.001f;

    private readonly ChargeEnemyData enemyData;
    private readonly Transform enemyTransform;
    private readonly Rigidbody enemyRigidbody;
    private readonly Collider enemyCollider;
    private readonly RaycastHit[] raycastHitBuffer;
    private readonly EnemyDebugRay[] debugRays;

    private int debugRayCount;

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

        debugRays =
            new EnemyDebugRay[MAX_DEBUG_RAY_COUNT];
    }

    /// <summary>
    /// 指定方向へ次の物理更新分だけ進んでも、
    /// 現在と同等の平坦面上に留まれるか判定する。
    /// </summary>
    /// <param name="moveDirectionSign">
    /// -1が-X方向、1が+X方向。
    /// </param>
    /// <param name="expectedMovementDistance">
    /// 次の物理更新で進む想定距離。
    /// 高速移動時に判定位置を移動量より手前へ置かないために使用する。
    /// </param>
    public bool CanMoveInDirection(
        float moveDirectionSign,
        float expectedMovementDistance)
    {
        // 1回の移動可否判定で実際に投げるRayだけを残す。
        // Gizmo側で位置や距離を再計算しないため、判定開始時に前回分を破棄する。
        ClearDebugRays();

        if (Mathf.Abs(moveDirectionSign) <=
            DIRECTION_EPSILON)
        {
            return false;
        }

        Bounds bounds =
            enemyCollider.bounds;

        if (bounds.size.sqrMagnitude <=
            DIRECTION_EPSILON)
        {
            return false;
        }

        float directionSign =
            Mathf.Sign(moveDirectionSign);

        float minimumForwardDistance =
            enemyData.GroundCheckForwardOffset;

        float projectedForwardDistance =
            Mathf.Max(
                minimumForwardDistance,
                Mathf.Max(0f, expectedMovementDistance));

        float rayDistance =
            bounds.extents.y +
            enemyData.GroundCheckDownDistance;

        Vector3 currentGroundRayOrigin =
            bounds.center;

        if (!TryGetNearestGroundHit(
                currentGroundRayOrigin,
                rayDistance,
                out RaycastHit currentGroundHit) ||
            !IsSlopeAllowed(currentGroundHit.normal))
        {
            return false;
        }

        // Collider前端直後を必ず確認し、
        // 高速移動用の先読みだけで直近の穴や段差を飛び越えて判定しない。
        Vector3 immediateForwardRayOrigin =
            GetForwardRayOrigin(
                bounds,
                directionSign,
                minimumForwardDistance);

        if (!IsGroundCompatible(
                currentGroundHit,
                immediateForwardRayOrigin,
                rayDistance))
        {
            return false;
        }

        if (projectedForwardDistance <=
            minimumForwardDistance + DIRECTION_EPSILON)
        {
            return true;
        }

        // 次の物理更新で前端が到達し得る位置も確認し、
        // 高速突進が1回のFixedUpdateで地形境界を越えることを抑える。
        Vector3 projectedForwardRayOrigin =
            GetForwardRayOrigin(
                bounds,
                directionSign,
                projectedForwardDistance);

        return IsGroundCompatible(
            currentGroundHit,
            projectedForwardRayOrigin,
            rayDistance);
    }

    private Vector3 GetForwardRayOrigin(
        Bounds bounds,
        float directionSign,
        float forwardDistance)
    {
        return bounds.center +
               Vector3.right *
               directionSign *
               (bounds.extents.x + forwardDistance);
    }

    private bool IsGroundCompatible(
        RaycastHit currentGroundHit,
        Vector3 targetRayOrigin,
        float rayDistance)
    {
        if (!TryGetNearestGroundHit(
                targetRayOrigin,
                rayDistance,
                out RaycastHit targetGroundHit))
        {
            return false;
        }

        if (!IsSlopeAllowed(targetGroundHit.normal))
        {
            return false;
        }

        float heightDifference =
            Mathf.Abs(
                targetGroundHit.point.y -
                currentGroundHit.point.y);

        return heightDifference <=
               enemyData.MaxGroundHeightDifference;
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
                Physics.DefaultRaycastLayers,
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
    /// 直近の移動可否判定で実際に使用した地面判定Rayを提供する。
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
        if (debugRayCount >=
            debugRays.Length)
        {
            return;
        }

        debugRays[debugRayCount] =
            new EnemyDebugRay(
                origin,
                direction,
                distance,
                EnemyDebugRayKind.GroundCheck);

        debugRayCount++;
    }

    private bool IsSlopeAllowed(
        Vector3 groundNormal)
    {
        float slopeAngle =
            Vector3.Angle(
                groundNormal,
                Vector3.up);

        return slopeAngle <=
               enemyData.MaxGroundSlopeAngle;
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
