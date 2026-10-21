using System;
using UnityEngine;

/// <summary>
/// EnemyからPlayerへのゲームプレイ平面上の距離判定とLOS判定を共通化する。
/// </summary>
/// <remarks>
/// 検知後のState遷移やEnemy固有の移動・攻撃判断は担当しない。
/// 現在のDrone / Charge実装に合わせ、距離判定はXYゲームプレイ平面で行う。
/// </remarks>
public sealed class EnemyPlayerDetectionSensor
{
    private const int RAYCAST_HIT_BUFFER_SIZE = 32;
    private const float DIRECTION_EPSILON = 0.001f;

    private readonly Transform enemyTransform;
    private readonly EnemyPlayerReference playerReference;
    private readonly RaycastHit[] raycastHitBuffer;

    private EnemyDebugRay lineOfSightDebugRay;
    private bool hasLineOfSightDebugRay;

    public EnemyPlayerDetectionSensor(
        Transform enemyTransform,
        EnemyPlayerReference playerReference)
    {
        this.enemyTransform =
            enemyTransform
            ? enemyTransform
            : throw new ArgumentNullException(
                nameof(enemyTransform));

        this.playerReference =
            playerReference
            ?? throw new ArgumentNullException(
                nameof(playerReference));

        raycastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];
    }

    /// <summary>
    /// 指定したEnemy側基準位置とPlayer代表位置が索敵半径内にあり、
    /// さらにLOS条件を満たすか判定する。
    /// </summary>
    /// <param name="detectionCenter">距離判定とLOSの始点に使うEnemy側ワールド座標。</param>
    /// <param name="playerDistancePosition">索敵距離判定に使うPlayer側ワールド座標。</param>
    /// <param name="detectionRadius">XYゲームプレイ平面上の索敵半径。</param>
    /// <param name="lineOfSightEndPosition">LOS Rayの終点に使うワールド座標。</param>
    /// <param name="playerColliderEndsLineOfSight">
    /// trueの場合、Enemy自身を除いた最短HitがPlayerならLOS成功とする。
    /// falseの場合、Player Colliderは遮蔽物から除外して終点まで確認する。
    /// </param>
    public bool CanDetectPlayer(
        Vector3 detectionCenter,
        Vector3 playerDistancePosition,
        float detectionRadius,
        Vector3 lineOfSightEndPosition,
        bool playerColliderEndsLineOfSight)
    {
        ClearDebugRay();

        if (detectionRadius <=
            DIRECTION_EPSILON)
        {
            return false;
        }

        float deltaX =
            playerDistancePosition.x -
            detectionCenter.x;

        float deltaY =
            playerDistancePosition.y -
            detectionCenter.y;

        float squaredDistance =
            deltaX * deltaX +
            deltaY * deltaY;

        if (squaredDistance >
            detectionRadius * detectionRadius)
        {
            return false;
        }

        return HasLineOfSight(
            detectionCenter,
            lineOfSightEndPosition,
            playerColliderEndsLineOfSight);
    }

    public void ClearDebugRay()
    {
        hasLineOfSightDebugRay = false;
    }

    public bool TryGetDebugRay(
        out EnemyDebugRay debugRay)
    {
        debugRay =
            lineOfSightDebugRay;

        return hasLineOfSightDebugRay;
    }

    private bool HasLineOfSight(
        Vector3 rayOrigin,
        Vector3 rayEndPosition,
        bool playerColliderEndsLineOfSight)
    {
        Vector3 rayVector =
            rayEndPosition -
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

        // RaycastNonAllocはバッファ満杯時に最近傍Hitが含まれる保証がないため、
        // 見落としによる誤検知を避けて安全側へ失敗扱いにする。
        if (hitCount >=
            raycastHitBuffer.Length)
        {
            SetDebugRay(
                rayOrigin,
                rayDirection,
                rayDistance,
                false);

            return false;
        }

        bool hasLineOfSight =
            playerColliderEndsLineOfSight
                ? IsNearestValidHitPlayerOrEmpty(
                    hitCount)
                : HasNoBlockingObstacle(
                    hitCount);

        SetDebugRay(
            rayOrigin,
            rayDirection,
            rayDistance,
            hasLineOfSight);

        return hasLineOfSight;
    }

    private bool IsNearestValidHitPlayerOrEmpty(
        int hitCount)
    {
        Collider nearestValidHitCollider =
            null;

        float nearestValidHitDistance =
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
                nearestValidHitDistance)
            {
                continue;
            }

            nearestValidHitCollider =
                hitCollider;

            nearestValidHitDistance =
                hit.distance;
        }

        return
            nearestValidHitCollider == null ||
            playerReference.IsPlayerCollider(
                nearestValidHitCollider);
    }

    private bool HasNoBlockingObstacle(
        int hitCount)
    {
        for (int i = 0;
             i < hitCount;
             i++)
        {
            Collider hitCollider =
                raycastHitBuffer[i].collider;

            if (hitCollider == null ||
                IsEnemyOwnedCollider(
                    hitCollider) ||
                playerReference.IsPlayerCollider(
                    hitCollider))
            {
                continue;
            }

            return false;
        }

        return true;
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

    private void SetDebugRay(
        Vector3 rayOrigin,
        Vector3 rayDirection,
        float rayDistance,
        bool hasLineOfSight)
    {
        lineOfSightDebugRay =
            new EnemyDebugRay(
                rayOrigin,
                rayDirection,
                rayDistance,
                EnemyDebugRayKind.LineOfSight,
                hasLineOfSight
                    ? EnemyDebugRayResult.Success
                    : EnemyDebugRayResult.Blocked);

        hasLineOfSightDebugRay = true;
    }
}
