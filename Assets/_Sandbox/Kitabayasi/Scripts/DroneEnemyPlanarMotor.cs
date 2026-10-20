using System;
using UnityEngine;

/// <summary>
/// Drone本体のXYゲームプレイ平面上の要求速度を、
/// 障害物への侵入を防ぎながらRigidbodyへ適用する。
/// </summary>
/// <remarks>
/// AI判断、経路探索、攻撃フェーズ、State遷移、向き制御は担当しない。
/// 移動前のBox Sweepで新しい侵入を防ぎ、既に重なっている場合は
/// Physics.ComputePenetrationを使ってXY平面内で復帰を試みる。
/// </remarks>
public sealed class DroneEnemyPlanarMotor
{
    private const int PHYSICS_QUERY_BUFFER_SIZE = 32;
    private const int MAX_DEPENETRATION_ITERATIONS = 8;

    private const float DIRECTION_EPSILON = 0.001f;
    private const float MOVEMENT_SKIN = 0.005f;
    private const float DEPENETRATION_SKIN = 0.001f;
    private const float START_OVERLAP_PROBE_DISTANCE = 0.001f;
    private const float BLOCKING_NORMAL_DOT_THRESHOLD = 0.01f;

    private readonly Rigidbody enemyRigidbody;
    private readonly BoxCollider enemyBoxCollider;
    private readonly LayerMask obstacleMask;
    private readonly Func<Collider, bool> isBlockingCollider;

    private readonly RaycastHit[] boxCastHitBuffer =
        new RaycastHit[PHYSICS_QUERY_BUFFER_SIZE];

    private readonly Collider[] overlapBuffer =
        new Collider[PHYSICS_QUERY_BUFFER_SIZE];

    /// <summary>
    /// Drone本体の移動適用に必要な物理参照と障害物判定を受け取る。
    /// </summary>
    /// <param name="isBlockingCollider">
    /// obstacleMaskに含まれるColliderのうち、実移動を阻害する対象ならtrueを返す。
    /// Player等、現在のLayer構成上Maskには含まれるが通行障害物として扱わない対象の除外に使う。
    /// </param>
    public DroneEnemyPlanarMotor(
        Rigidbody enemyRigidbody,
        BoxCollider enemyBoxCollider,
        LayerMask obstacleMask,
        Func<Collider, bool> isBlockingCollider)
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
                $"{nameof(DroneEnemyPlanarMotor)}には有効な" +
                $"{nameof(BoxCollider)}が必要です。");
        }

        if (enemyBoxCollider.transform !=
            enemyRigidbody.transform)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemyPlanarMotor)}は" +
                "Rigidbodyと同じGameObjectにあるBoxColliderを使用する前提です。");
        }

        this.obstacleMask =
            obstacleMask;

        this.isBlockingCollider =
            isBlockingCollider
            ?? throw new ArgumentNullException(
                nameof(isBlockingCollider));
    }

    /// <summary>
    /// 要求されたXY速度を、次のPhysics更新で障害物へ侵入しない範囲へ制限して適用する。
    /// </summary>
    /// <returns>
    /// 要求速度を制限せず適用できた場合はtrue。
    /// 障害物によって移動量を制限した場合、または既存の重なりを解消できなかった場合はfalse。
    /// </returns>
    public bool TrySetPlanarVelocity(
        Vector2 targetVelocity)
    {
        if (!TryResolveExistingPenetration())
        {
            ApplyPlanarVelocity(
                Vector2.zero);

            return false;
        }

        float targetSpeed =
            targetVelocity.magnitude;

        if (targetSpeed <=
            DIRECTION_EPSILON)
        {
            ApplyPlanarVelocity(
                Vector2.zero);

            return true;
        }

        float fixedDeltaTime =
            Time.fixedDeltaTime;

        if (fixedDeltaTime <=
            DIRECTION_EPSILON)
        {
            ApplyPlanarVelocity(
                Vector2.zero);

            return false;
        }

        Vector2 planarDirection =
            targetVelocity /
            targetSpeed;

        Vector3 movementDirection =
            new Vector3(
                planarDirection.x,
                planarDirection.y,
                0f);

        float requestedDistance =
            targetSpeed *
            fixedDeltaTime;

        float allowedDistance =
            GetAllowedMovementDistance(
                movementDirection,
                requestedDistance);

        bool canApplyFullMovement =
            allowedDistance >=
            requestedDistance -
            DIRECTION_EPSILON;

        float allowedSpeed =
            allowedDistance /
            fixedDeltaTime;

        Vector2 safeVelocity =
            planarDirection *
            Mathf.Min(
                targetSpeed,
                allowedSpeed);

        ApplyPlanarVelocity(
            safeVelocity);

        return canApplyFullMovement;
    }

    /// <summary>
    /// XY移動を停止する。既に障害物へ重なっている場合は停止前に復帰を試みる。
    /// </summary>
    public void StopPlanarMovement()
    {
        TryResolveExistingPenetration();

        ApplyPlanarVelocity(
            Vector2.zero);
    }

    private float GetAllowedMovementDistance(
        Vector3 movementDirection,
        float requestedDistance)
    {
        if (requestedDistance <=
            DIRECTION_EPSILON)
        {
            return 0f;
        }

        GetBoxQueryGeometry(
            enemyRigidbody.position,
            out Vector3 queryCenter,
            out Vector3 halfExtents,
            out Quaternion orientation);

        float castDistance =
            requestedDistance +
            MOVEMENT_SKIN;

        int hitCount =
            Physics.BoxCastNonAlloc(
                queryCenter,
                halfExtents,
                movementDirection,
                boxCastHitBuffer,
                orientation,
                castDistance,
                obstacleMask,
                QueryTriggerInteraction.Ignore);

        RaycastHit[] hitInfos =
            hitCount < boxCastHitBuffer.Length
                ? boxCastHitBuffer
                : Physics.BoxCastAll(
                    queryCenter,
                    halfExtents,
                    movementDirection,
                    orientation,
                    castDistance,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore);

        int resultCount =
            hitCount < boxCastHitBuffer.Length
                ? hitCount
                : hitInfos.Length;

        float allowedDistance =
            requestedDistance;

        for (int i = 0;
             i < resultCount;
             i++)
        {
            RaycastHit hitInfo =
                hitInfos[i];

            if (!IsBlockingCandidate(
                    hitInfo.collider))
            {
                continue;
            }

            if (hitInfo.distance <=
                DIRECTION_EPSILON)
            {
                // BoxCastは開始時点で重なっているColliderに対して、
                // 実際の面法線ではなくSweep方向の逆向きを返す。
                // 接触中の床を壁と誤認しないよう、XY方向へ微小に進めた仮想姿勢で
                // 本当にPenetrationが発生する場合だけBlockingとみなす。
                if (!WouldPenetrateAfterSmallPlanarMove(
                        hitInfo.collider,
                        movementDirection,
                        orientation))
                {
                    continue;
                }
            }
            else
            {
                float blockingDot =
                    Vector3.Dot(
                        hitInfo.normal,
                        movementDirection);

                // 床や壁に沿う平行移動まで止めない。
                // 移動方向に対して逆向き成分を持つ面だけを侵入候補として扱う。
                if (blockingDot >
                    -BLOCKING_NORMAL_DOT_THRESHOLD)
                {
                    continue;
                }
            }

            float candidateDistance =
                Mathf.Max(
                    0f,
                    hitInfo.distance -
                    MOVEMENT_SKIN);

            if (candidateDistance <
                allowedDistance)
            {
                allowedDistance =
                    candidateDistance;
            }
        }

        return allowedDistance;
    }

    private bool WouldPenetrateAfterSmallPlanarMove(
        Collider obstacleCollider,
        Vector3 movementDirection,
        Quaternion orientation)
    {
        Vector3 probeBodyPosition =
            enemyRigidbody.position +
            movementDirection *
            START_OVERLAP_PROBE_DISTANCE;

        return Physics.ComputePenetration(
            enemyBoxCollider,
            probeBodyPosition,
            orientation,
            obstacleCollider,
            obstacleCollider.transform.position,
            obstacleCollider.transform.rotation,
            out _,
            out _);
    }

    /// <summary>
    /// 物理ソルバーや外力等で既に障害物へ重なっていた場合に、
    /// ComputePenetrationの最小分離方向をXY平面へ変換して復帰する。
    /// </summary>
    /// <remarks>
    /// Droneのゲームプレイ平面を維持するためZ位置は変更しない。
    /// Z方向にしか分離できない重なりは、このMotorでは安全に解消できないためfalseを返す。
    /// </remarks>
    private bool TryResolveExistingPenetration()
    {
        Vector3 originalBodyPosition =
            enemyRigidbody.position;

        Vector3 resolvedBodyPosition =
            originalBodyPosition;

        bool positionChanged = false;

        for (int iteration = 0;
             iteration < MAX_DEPENETRATION_ITERATIONS;
             iteration++)
        {
            GetBoxQueryGeometry(
                resolvedBodyPosition,
                out Vector3 queryCenter,
                out Vector3 halfExtents,
                out Quaternion orientation);

            int overlapCount =
                Physics.OverlapBoxNonAlloc(
                    queryCenter,
                    halfExtents,
                    overlapBuffer,
                    orientation,
                    obstacleMask,
                    QueryTriggerInteraction.Ignore);

            Collider[] overlappedColliders =
                overlapCount < overlapBuffer.Length
                    ? overlapBuffer
                    : Physics.OverlapBox(
                        queryCenter,
                        halfExtents,
                        orientation,
                        obstacleMask,
                        QueryTriggerInteraction.Ignore);

            int resultCount =
                overlapCount < overlapBuffer.Length
                    ? overlapCount
                    : overlappedColliders.Length;

            bool foundPenetration = false;

            for (int i = 0;
                 i < resultCount;
                 i++)
            {
                Collider obstacleCollider =
                    overlappedColliders[i];

                if (!IsBlockingCandidate(
                        obstacleCollider))
                {
                    continue;
                }

                if (!Physics.ComputePenetration(
                        enemyBoxCollider,
                        resolvedBodyPosition,
                        orientation,
                        obstacleCollider,
                        obstacleCollider.transform.position,
                        obstacleCollider.transform.rotation,
                        out Vector3 separationDirection,
                        out float separationDistance))
                {
                    continue;
                }

                foundPenetration = true;

                Vector2 planarSeparation =
                    new Vector2(
                        separationDirection.x,
                        separationDirection.y);

                float planarDirectionMagnitude =
                    planarSeparation.magnitude;

                if (planarDirectionMagnitude <=
                    DIRECTION_EPSILON)
                {
                    ApplyResolvedBodyPosition(
                        originalBodyPosition,
                        resolvedBodyPosition,
                        positionChanged);

                    return false;
                }

                Vector2 planarDirection =
                    planarSeparation /
                    planarDirectionMagnitude;

                // ComputePenetrationが返す3D分離ベクトルと同じ法線方向成分を
                // XY移動だけで確保するため、XY成分の大きさで距離を補正する。
                float planarSeparationDistance =
                    separationDistance /
                    planarDirectionMagnitude;

                resolvedBodyPosition +=
                    new Vector3(
                        planarDirection.x,
                        planarDirection.y,
                        0f) *
                    (planarSeparationDistance +
                     DEPENETRATION_SKIN);

                positionChanged = true;
            }

            if (!foundPenetration)
            {
                ApplyResolvedBodyPosition(
                    originalBodyPosition,
                    resolvedBodyPosition,
                    positionChanged);

                return true;
            }
        }

        ApplyResolvedBodyPosition(
            originalBodyPosition,
            resolvedBodyPosition,
            positionChanged);

        return !HasBlockingPenetration(
            resolvedBodyPosition);
    }

    private bool HasBlockingPenetration(
        Vector3 bodyPosition)
    {
        GetBoxQueryGeometry(
            bodyPosition,
            out Vector3 queryCenter,
            out Vector3 halfExtents,
            out Quaternion orientation);

        Collider[] overlappedColliders =
            Physics.OverlapBox(
                queryCenter,
                halfExtents,
                orientation,
                obstacleMask,
                QueryTriggerInteraction.Ignore);

        for (int i = 0;
             i < overlappedColliders.Length;
             i++)
        {
            Collider obstacleCollider =
                overlappedColliders[i];

            if (!IsBlockingCandidate(
                    obstacleCollider))
            {
                continue;
            }

            if (Physics.ComputePenetration(
                    enemyBoxCollider,
                    bodyPosition,
                    orientation,
                    obstacleCollider,
                    obstacleCollider.transform.position,
                    obstacleCollider.transform.rotation,
                    out _,
                    out _))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsBlockingCandidate(
        Collider targetCollider)
    {
        if (targetCollider == null ||
            targetCollider == enemyBoxCollider ||
            targetCollider.attachedRigidbody ==
                enemyRigidbody)
        {
            return false;
        }

        Transform targetTransform =
            targetCollider.transform;

        Transform enemyTransform =
            enemyRigidbody.transform;

        if (targetTransform == enemyTransform ||
            targetTransform.IsChildOf(
                enemyTransform))
        {
            return false;
        }

        return isBlockingCollider(
            targetCollider);
    }

    private void GetBoxQueryGeometry(
        Vector3 bodyPosition,
        out Vector3 queryCenter,
        out Vector3 halfExtents,
        out Quaternion orientation)
    {
        Transform boxTransform =
            enemyBoxCollider.transform;

        Vector3 centerOffset =
            boxTransform.TransformVector(
                enemyBoxCollider.center);

        queryCenter =
            bodyPosition +
            centerOffset;

        Vector3 lossyScale =
            boxTransform.lossyScale;

        Vector3 absoluteScale =
            new Vector3(
                Mathf.Abs(lossyScale.x),
                Mathf.Abs(lossyScale.y),
                Mathf.Abs(lossyScale.z));

        halfExtents =
            Vector3.Scale(
                enemyBoxCollider.size * 0.5f,
                absoluteScale);

        orientation =
            boxTransform.rotation;
    }

    private void ApplyResolvedBodyPosition(
        Vector3 originalBodyPosition,
        Vector3 resolvedBodyPosition,
        bool positionChanged)
    {
        if (!positionChanged)
        {
            return;
        }

        // Depenetrationは異常状態からの復帰なので、次のPhysics更新を待たず
        // Rigidbody位置を直接補正する。Zは計算中に変更していない。
        resolvedBodyPosition.z =
            originalBodyPosition.z;

        enemyRigidbody.position =
            resolvedBodyPosition;
    }

    private void ApplyPlanarVelocity(
        Vector2 targetVelocity)
    {
        Vector3 targetWorldVelocity =
            new Vector3(
                targetVelocity.x,
                targetVelocity.y,
                0f);

        if ((enemyRigidbody.linearVelocity -
             targetWorldVelocity).sqrMagnitude <=
            DIRECTION_EPSILON *
            DIRECTION_EPSILON)
        {
            return;
        }

        // このMotorは物理的な加速を再現するのではなく、Strategyが決めた
        // ゲームプレイ上の速度を安全な値へ制限して適用する責務を持つ。
        // 同一Physics Step内で停止やフェーズ変更が続いても速度変更が累積しないよう、
        // 最終的なXY速度を直接確定し、奥行き方向の速度は0へ戻す。
        enemyRigidbody.linearVelocity =
            targetWorldVelocity;
    }
}
