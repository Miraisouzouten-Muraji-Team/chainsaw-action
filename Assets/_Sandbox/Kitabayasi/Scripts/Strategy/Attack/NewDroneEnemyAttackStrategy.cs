using System;
using UnityEngine;

/// <summary>
/// 新型ドローンの射撃地点探索、照準予備動作、相対位置追従を伴う連射を行う。
/// </summary>
/// <remarks>
/// 攻撃前だけ射線が通る地点をA*経路コスト順に探索し、NavigatorとMotorへ移動を委譲する。
/// 射撃可能になってから予備動作を開始し、その間は停止する。
/// 射撃中は再探索・中断せず既存の追従とShooterの連射を維持する。
/// Playerの範囲索敵とState遷移は担当しない。
/// </remarks>
[Serializable]
public sealed class NewDroneEnemyAttackStrategy : IEnemyAttackStrategy
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";
    private const float DIRECTION_EPSILON = 0.001f;
    private const float SHOOTING_POSITION_RETRY_INTERVAL = 0.5f;
    private const int SIGHT_HIT_BUFFER_SIZE = 32;
    private const float MIN_PROJECTILE_RADIUS = 0.0001f;

    [Header("射撃")]
    [Tooltip("新型Droneに配置したEnemyProjectileShooter。5発/0.1秒/Accuracy20%はShooter側で設定する。")]
    [SerializeField]
    private EnemyProjectileShooter projectileShooter;

    private NewDroneEnemyData enemyData;
    private GameObject enemyObject;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private DroneEnemyPlanarMotor planarMotor;
    private DroneEnemyPathNavigator pathNavigator;
    private EnemyPlayerReference playerReference;
    private RaycastHit[] sightHitBuffer;
    private Collider[] muzzleOverlapBuffer;
    private Collider playerTargetCollider;

    private Vector3 lastAimTargetPosition;
    private Vector2 playerToDroneOffsetAtPreparationStart;
    private float preparationElapsedTime;
    private float fallbackDirectionSign;
    private bool isPreparationActive;
    private bool isShooting;
    private bool hasAimTarget;
    private bool hasFollowTarget;
    private bool isSeekingShootingPosition;
    private bool hasShootingDestination;
    private bool hasUnsuccessfulSearch;
    private Vector3 shootingDestination;
    private Vector2 lastPositioningProgressPosition;
    private float lastPositioningProgressTime;
    private float nextShootingPositionSearchTime;
    private float firstUnsuccessfulSearchTime;

    /// <summary>射撃位置を制限時間内に発見できず、帰還が必要な場合true。</summary>
    public bool HasAbandonedAttack { get; private set; }

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData = enemyData as NewDroneEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(NewDroneEnemyAttackStrategy)}には{nameof(NewDroneEnemyData)}が必要です。");

        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));

        this.visualRoot = visualRoot
            ? visualRoot
            : throw new InvalidOperationException("新型DroneにはVisualRootが必要です。");

        if (projectileShooter == null ||
            !projectileShooter.gameObject.activeInHierarchy ||
            !projectileShooter.enabled ||
            (projectileShooter.transform != enemyObject.transform &&
             !projectileShooter.transform.IsChildOf(enemyObject.transform)))
        {
            throw new InvalidOperationException(
                "EnemyProjectileShooterは有効なEnemy子階層のComponentを設定してください。");
        }

        enemyRigidbody = enemyObject.GetComponent<Rigidbody>();
        BoxCollider boxCollider = enemyObject.GetComponent<BoxCollider>();

        if (enemyRigidbody == null || enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException("新型Droneには非KinematicのRigidbodyが必要です。");
        }

        if (boxCollider == null || !boxCollider.enabled)
        {
            throw new InvalidOperationException("新型Drone本体には有効なBoxColliderが必要です。");
        }

        if (this.enemyData.NavigationObstacleMask.value == 0)
        {
            throw new InvalidOperationException("NewDroneEnemyDataのNavigation Obstacle MaskにStage障害物を設定してください。");
        }

        playerReference = new EnemyPlayerReference();
        sightHitBuffer = new RaycastHit[SIGHT_HIT_BUFFER_SIZE];
        muzzleOverlapBuffer = new Collider[SIGHT_HIT_BUFFER_SIZE];

        planarMotor = new DroneEnemyPlanarMotor(
            enemyRigidbody,
            boxCollider,
            this.enemyData.NavigationObstacleMask,
            IsBlockingMovementCollider);

        pathNavigator = new DroneEnemyPathNavigator(
            enemyRigidbody,
            boxCollider,
            planarMotor,
            this.enemyData.NavigationObstacleMask,
            this.enemyData.PathSafetyMargin,
            this.enemyData.PathSearchPadding,
            this.enemyData.MaximumExpandedPathNodes,
            this.enemyData.WaypointArrivalTolerance,
            this.enemyData.NavigationMoveSpeed,
            this.enemyData.NavigationStallMinimumMoveSpeed,
            this.enemyData.NavigationStallDetectionDuration);

        playerReference.TryGetPlayerTransform(out _);
        projectileShooter.Initialize(enemyObject, playerReference);

        if (!projectileShooter.HasAttackWarningLine)
        {
            Debug.LogWarning(
                $"{nameof(NewDroneEnemyAttackStrategy)}: EnemyProjectileShooterの" +
                "Attack Warning Lineが未設定です。射撃は継続しますが攻撃予告は表示できません。",
                enemyObject);
        }

        if (projectileShooter.ProjectileStageCollisionMask.value == 0)
        {
            throw new InvalidOperationException(
                "Projectile PrefabのStage Collision Maskに弾を遮るStageレイヤーを設定してください。");
        }
    }

    public void BeginPreparation(float initialAttackDirectionSign)
    {
        ResetRuntimeState();
        projectileShooter.HideAttackWarning();
        pathNavigator.Reset();
        planarMotor.StopPlanarMovement();

        fallbackDirectionSign = initialAttackDirectionSign < 0f ? -1f : 1f;

        if (!TryGetCurrentPlayerTargetPosition(out Vector3 targetPosition) ||
            !HasValidPlayerTargetCollider())
        {
            AbandonAttack();
            return;
        }

        if (CanShootFromPosition(enemyRigidbody.position, playerTargetCollider))
        {
            StartAimingPreparation();
            return;
        }

        // 既存の予備動作時間は、射撃可能地点へ到着してから計測する。
        isSeekingShootingPosition = true;
        lastAimTargetPosition = targetPosition;
        lastPositioningProgressPosition = new Vector2(
            enemyRigidbody.position.x, enemyRigidbody.position.y);
        lastPositioningProgressTime = Time.time;
        projectileShooter.HideAim();
        projectileShooter.HideAttackWarning();
    }

    public bool UpdatePreparation()
    {
        if (HasAbandonedAttack)
        {
            return true;
        }

        if (isSeekingShootingPosition)
        {
            UpdateShootingPositionSearch();
            return HasAbandonedAttack;
        }

        if (!isPreparationActive)
        {
            return true;
        }

        // 外部の慣性やState切替直前の移動指示を引き継がない。
        planarMotor.StopPlanarMovement();
        RefreshAimTarget();
        projectileShooter.ShowAim(lastAimTargetPosition);
        projectileShooter.ShowAttackWarning(lastAimTargetPosition, playerTargetCollider);

        preparationElapsedTime += Time.fixedDeltaTime;
        if (preparationElapsedTime < enemyData.AttackPreparationDuration)
        {
            return false;
        }

        isPreparationActive = false;
        projectileShooter.HideAttackWarning();
        return true;
    }

    private void StartAimingPreparation()
    {
        isSeekingShootingPosition = false;
        hasShootingDestination = false;
        pathNavigator.Reset();
        planarMotor.StopPlanarMovement();
        isPreparationActive = true;
        preparationElapsedTime = 0f;

        RefreshAimTarget(captureFollowReference: true);
        if (!hasAimTarget)
        {
            lastAimTargetPosition = projectileShooter.FirePointPosition +
                Vector3.right * fallbackDirectionSign;
            hasAimTarget = true;
        }

        projectileShooter.ShowAim(lastAimTargetPosition);
        projectileShooter.ShowAttackWarning(lastAimTargetPosition, playerTargetCollider);
    }

    private void UpdateShootingPositionSearch()
    {
        if (!TryGetCurrentPlayerTargetPosition(out Vector3 playerPosition) ||
            !HasValidPlayerTargetCollider())
        {
            AbandonAttack();
            return;
        }

        if (CanShootFromPosition(enemyRigidbody.position, playerTargetCollider))
        {
            StartAimingPreparation();
            return;
        }

        // 移動中にPlayerが射撃位置から隠れた場合、次の許可時刻にだけ再探索する。
        if (hasShootingDestination &&
            !CanShootFromPosition(shootingDestination, playerTargetCollider))
        {
            hasShootingDestination = false;
            pathNavigator.Reset();
            planarMotor.StopPlanarMovement();
        }

        if (hasShootingDestination)
        {
            bool arrived = pathNavigator.UpdatePathToPosition(shootingDestination);
            AlignVisualTowardTarget(playerPosition);
            if (arrived || !pathNavigator.HasPath)
            {
                hasShootingDestination = false;
                planarMotor.StopPlanarMovement();
            }
        }
        else
        {
            planarMotor.StopPlanarMovement();
        }

        // 有効なDestinationがある間はPlayerの微小な移動では再探索しない。
        if (!hasShootingDestination &&
            Time.time >= nextShootingPositionSearchTime)
        {
            SearchForShootingDestination(playerPosition);
        }

        Vector2 currentPosition = new Vector2(
            enemyRigidbody.position.x, enemyRigidbody.position.y);
        if (Vector2.Distance(currentPosition, lastPositioningProgressPosition) > 0.01f)
        {
            lastPositioningProgressPosition = currentPosition;
            lastPositioningProgressTime = Time.time;
        }

        // 経路自体は取得できても物理的に移動できない場合の無限リトライも止める。
        bool hasPositioningStalled =
            Time.time - lastPositioningProgressTime >=
            Mathf.Max(1f, enemyData.ShootingPositionSearchTimeout);

        if (hasPositioningStalled ||
            hasUnsuccessfulSearch &&
            Time.time - firstUnsuccessfulSearchTime >= enemyData.ShootingPositionSearchTimeout)
        {
            AbandonAttack();
        }
    }

    private void SearchForShootingDestination(Vector3 playerPosition)
    {
        nextShootingPositionSearchTime = Time.time + SHOOTING_POSITION_RETRY_INTERVAL;

        Collider targetCollider = playerTargetCollider;
        bool found = pathNavigator.TryBeginPathToNearestMatchingPosition(
            playerPosition,
            position => CanShootFromPosition(position, targetCollider),
            out Vector3 selectedPosition);

        if (found)
        {
            hasShootingDestination = true;
            shootingDestination = selectedPosition;
            hasUnsuccessfulSearch = false;
            return;
        }

        hasShootingDestination = false;
        planarMotor.StopPlanarMovement();
        if (!hasUnsuccessfulSearch)
        {
            hasUnsuccessfulSearch = true;
            firstUnsuccessfulSearchTime = Time.time;
        }
    }

    private bool HasValidPlayerTargetCollider()
    {
        return playerTargetCollider != null &&
            playerTargetCollider.enabled &&
            playerTargetCollider.gameObject.activeInHierarchy;
    }

    /// <summary>
    /// 発射時の弾PrefabのColliderをXY平面の照準方向にスイープし、
    /// Playerの対象Colliderに触れるまでにStageへ衝突しない地点だけを許可する。
    /// 命中率・拡散は位置選択に含めない。
    /// </summary>
    private bool CanShootFromPosition(Vector3 bodyPosition, Collider targetCollider)
    {
        if (targetCollider == null || !targetCollider.enabled ||
            !targetCollider.gameObject.activeInHierarchy)
        {
            return false;
        }

        Vector3 muzzlePosition = GetPredictedFirePointPosition(
            bodyPosition, targetCollider.bounds.center);

        Vector3 planarTarget = new Vector3(
            targetCollider.bounds.center.x,
            targetCollider.bounds.center.y,
            muzzlePosition.z);
        Vector3 shotVector = planarTarget - muzzlePosition;
        float shotDistance = shotVector.magnitude;
        Vector3 shotDirection = shotDistance > DIRECTION_EPSILON
            ? shotVector / shotDistance
            : Vector3.right;

        // Stageと対象Playerを同時に調べ、最初の接触がPlayerであるかで判定する。
        // SphereCast等は開始位置の重なりを返さないため、Overlapも必ず先に行う。
        int collisionMask = projectileShooter.ProjectileStageCollisionMask.value |
            (1 << targetCollider.gameObject.layer);
        float nearestPlayerHit = float.PositiveInfinity;
        float nearestStageHit = float.PositiveInfinity;
        bool hasProjectileCollider = false;

        Collider[] projectileColliders = projectileShooter.ProjectilePrefabColliders;
        if (projectileColliders == null)
        {
            return false;
        }

        for (int colliderIndex = 0; colliderIndex < projectileColliders.Length; colliderIndex++)
        {
            Collider projectileCollider = projectileColliders[colliderIndex];
            if (projectileCollider == null || !projectileCollider.enabled ||
                !IsActiveProjectileCollider(projectileCollider))
            {
                continue;
            }

            hasProjectileCollider = true;
            int overlapCount = GetProjectileOverlaps(
                projectileCollider, muzzlePosition, collisionMask,
                out Collider[] overlaps);
            for (int i = 0; i < overlapCount; i++)
            {
                Collider overlap = overlaps[i];
                if (overlap == targetCollider)
                {
                    nearestPlayerHit = 0f;
                }
                else if (IsBlockingShotCollider(overlap))
                {
                    // 発射時点でStageに埋まる弾は、スイープでは検出できない。
                    return false;
                }
            }

            if (shotDistance <= DIRECTION_EPSILON)
            {
                continue;
            }

            int hitCount = CastProjectileCollider(
                projectileCollider, muzzlePosition, shotDirection,
                shotDistance, collisionMask, out RaycastHit[] hits);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == targetCollider)
                {
                    nearestPlayerHit = Mathf.Min(nearestPlayerHit, hit.distance);
                }
                else if (IsBlockingShotCollider(hit.collider))
                {
                    nearestStageHit = Mathf.Min(nearestStageHit, hit.distance);
                }
            }
        }

        // 複数Colliderを持つ弾では、どれか一つがStageへ先に触れたら発射不可。
        // 同時接触も壁への命中を優先して不可とする。
        return hasProjectileCollider &&
            nearestPlayerHit < float.PositiveInfinity &&
            nearestPlayerHit + DIRECTION_EPSILON < nearestStageHit;
    }

    private bool IsActiveProjectileCollider(Collider projectileCollider)
    {
        Transform root = projectileShooter.ProjectilePrefabRoot;
        for (Transform current = projectileCollider.transform;
             current != null;
             current = current.parent)
        {
            if (!current.gameObject.activeSelf)
            {
                return false;
            }

            if (current == root)
            {
                return true;
            }
        }

        return false;
    }

    private int GetProjectileOverlaps(
        Collider projectileCollider, Vector3 muzzlePosition,
        int collisionMask, out Collider[] overlaps)
    {
        int count;
        overlaps = muzzleOverlapBuffer;

        if (projectileCollider is SphereCollider sphere)
        {
            Vector3 center = GetProjectileShapeCenter(
                sphere.transform, sphere.center, muzzlePosition);
            float radius = GetSphereRadius(sphere);
            count = Physics.OverlapSphereNonAlloc(
                center, radius, muzzleOverlapBuffer,
                collisionMask, QueryTriggerInteraction.Collide);
            if (count == muzzleOverlapBuffer.Length)
            {
                overlaps = Physics.OverlapSphere(
                    center, radius, collisionMask, QueryTriggerInteraction.Collide);
            }
        }
        else if (projectileCollider is BoxCollider box)
        {
            Vector3 center = GetProjectileShapeCenter(
                box.transform, box.center, muzzlePosition);
            Vector3 halfExtents = GetBoxHalfExtents(box);
            Quaternion orientation = box.transform.rotation;
            count = Physics.OverlapBoxNonAlloc(
                center, halfExtents, muzzleOverlapBuffer,
                orientation, collisionMask, QueryTriggerInteraction.Collide);
            if (count == muzzleOverlapBuffer.Length)
            {
                overlaps = Physics.OverlapBox(
                    center, halfExtents, orientation,
                    collisionMask, QueryTriggerInteraction.Collide);
            }
        }
        else if (projectileCollider is CapsuleCollider capsule)
        {
            GetCapsuleWorldGeometry(capsule, muzzlePosition,
                out Vector3 point1, out Vector3 point2, out float radius);
            count = Physics.OverlapCapsuleNonAlloc(
                point1, point2, radius, muzzleOverlapBuffer,
                collisionMask, QueryTriggerInteraction.Collide);
            if (count == muzzleOverlapBuffer.Length)
            {
                overlaps = Physics.OverlapCapsule(
                    point1, point2, radius,
                    collisionMask, QueryTriggerInteraction.Collide);
            }
        }
        else
        {
            // MeshCollider等は任意形状のSweepがUnity Physicsにないため、
            // メッシュ全体を包む球で保守的に判定する（通れる地点を広く認めない）。
            GetProjectileBoundingSphere(projectileCollider, muzzlePosition,
                out Vector3 center, out float radius);
            count = Physics.OverlapSphereNonAlloc(
                center, radius, muzzleOverlapBuffer,
                collisionMask, QueryTriggerInteraction.Collide);
            if (count == muzzleOverlapBuffer.Length)
            {
                overlaps = Physics.OverlapSphere(
                    center, radius, collisionMask, QueryTriggerInteraction.Collide);
            }
        }

        return overlaps == muzzleOverlapBuffer ? count : overlaps.Length;
    }

    private int CastProjectileCollider(
        Collider projectileCollider, Vector3 muzzlePosition,
        Vector3 shotDirection, float shotDistance, int collisionMask,
        out RaycastHit[] hits)
    {
        int count;
        hits = sightHitBuffer;

        if (projectileCollider is SphereCollider sphere)
        {
            Vector3 center = GetProjectileShapeCenter(
                sphere.transform, sphere.center, muzzlePosition);
            float radius = GetSphereRadius(sphere);
            count = Physics.SphereCastNonAlloc(
                center, radius, shotDirection, sightHitBuffer,
                shotDistance, collisionMask, QueryTriggerInteraction.Collide);
            if (count == sightHitBuffer.Length)
            {
                hits = Physics.SphereCastAll(
                    center, radius, shotDirection, shotDistance,
                    collisionMask, QueryTriggerInteraction.Collide);
            }
        }
        else if (projectileCollider is BoxCollider box)
        {
            Vector3 center = GetProjectileShapeCenter(
                box.transform, box.center, muzzlePosition);
            Vector3 halfExtents = GetBoxHalfExtents(box);
            Quaternion orientation = box.transform.rotation;
            count = Physics.BoxCastNonAlloc(
                center, halfExtents, shotDirection, sightHitBuffer,
                orientation, shotDistance,
                collisionMask, QueryTriggerInteraction.Collide);
            if (count == sightHitBuffer.Length)
            {
                hits = Physics.BoxCastAll(
                    center, halfExtents, shotDirection, orientation,
                    shotDistance, collisionMask, QueryTriggerInteraction.Collide);
            }
        }
        else if (projectileCollider is CapsuleCollider capsule)
        {
            GetCapsuleWorldGeometry(capsule, muzzlePosition,
                out Vector3 point1, out Vector3 point2, out float radius);
            count = Physics.CapsuleCastNonAlloc(
                point1, point2, radius, shotDirection, sightHitBuffer,
                shotDistance, collisionMask, QueryTriggerInteraction.Collide);
            if (count == sightHitBuffer.Length)
            {
                hits = Physics.CapsuleCastAll(
                    point1, point2, radius, shotDirection,
                    shotDistance, collisionMask, QueryTriggerInteraction.Collide);
            }
        }
        else
        {
            GetProjectileBoundingSphere(projectileCollider, muzzlePosition,
                out Vector3 center, out float radius);
            count = Physics.SphereCastNonAlloc(
                center, radius, shotDirection, sightHitBuffer,
                shotDistance, collisionMask, QueryTriggerInteraction.Collide);
            if (count == sightHitBuffer.Length)
            {
                hits = Physics.SphereCastAll(
                    center, radius, shotDirection,
                    shotDistance, collisionMask, QueryTriggerInteraction.Collide);
            }
        }

        return hits == sightHitBuffer ? count : hits.Length;
    }

    private Vector3 GetProjectileShapeCenter(
        Transform colliderTransform, Vector3 localCenter, Vector3 muzzlePosition)
    {
        // ShooterはPrefabの回転を維持してFirePointに生成するため、
        // Prefab上のColliderオフセットをそのまま発射位置へ移せる。
        return muzzlePosition +
            colliderTransform.TransformPoint(localCenter) -
            projectileShooter.ProjectilePrefabRoot.position;
    }

    private static Vector3 GetBoxHalfExtents(BoxCollider box)
    {
        Vector3 scale = box.transform.lossyScale;
        return new Vector3(
            Mathf.Max(MIN_PROJECTILE_RADIUS, box.size.x * Mathf.Abs(scale.x) * 0.5f),
            Mathf.Max(MIN_PROJECTILE_RADIUS, box.size.y * Mathf.Abs(scale.y) * 0.5f),
            Mathf.Max(MIN_PROJECTILE_RADIUS, box.size.z * Mathf.Abs(scale.z) * 0.5f));
    }

    private static float GetSphereRadius(SphereCollider sphere)
    {
        Vector3 scale = sphere.transform.lossyScale;
        return Mathf.Max(MIN_PROJECTILE_RADIUS, sphere.radius *
            Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }

    private void GetCapsuleWorldGeometry(
        CapsuleCollider capsule, Vector3 muzzlePosition,
        out Vector3 point1, out Vector3 point2, out float radius)
    {
        Vector3 scale = capsule.transform.lossyScale;
        Vector3 axis;
        float heightScale;
        float radiusScale;
        switch (capsule.direction)
        {
            case 0:
                axis = capsule.transform.right;
                heightScale = Mathf.Abs(scale.x);
                radiusScale = Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                break;
            case 1:
                axis = capsule.transform.up;
                heightScale = Mathf.Abs(scale.y);
                radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                break;
            default:
                axis = capsule.transform.forward;
                heightScale = Mathf.Abs(scale.z);
                radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                break;
        }

        radius = Mathf.Max(MIN_PROJECTILE_RADIUS, capsule.radius * radiusScale);
        float halfSegment = Mathf.Max(0f, capsule.height * heightScale * 0.5f - radius);
        Vector3 center = GetProjectileShapeCenter(
            capsule.transform, capsule.center, muzzlePosition);
        point1 = center + axis * halfSegment;
        point2 = center - axis * halfSegment;
    }

    private void GetProjectileBoundingSphere(
        Collider collider, Vector3 muzzlePosition,
        out Vector3 center, out float radius)
    {
        if (collider is MeshCollider mesh && mesh.sharedMesh != null)
        {
            Bounds localBounds = mesh.sharedMesh.bounds;
            center = GetProjectileShapeCenter(
                mesh.transform, localBounds.center, muzzlePosition);
            Vector3 scale = mesh.transform.lossyScale;
            radius = Mathf.Max(MIN_PROJECTILE_RADIUS, new Vector3(
                localBounds.extents.x * Mathf.Abs(scale.x),
                localBounds.extents.y * Mathf.Abs(scale.y),
                localBounds.extents.z * Mathf.Abs(scale.z)).magnitude);
            return;
        }

        // 未対応Colliderでは誤って射撃可能としないため、初期化時に設定を検出する。
        throw new InvalidOperationException(
            $"Projectile PrefabのCollider '{collider.GetType().Name}' は射撃位置判定に未対応です。");
    }

    private bool IsBlockingShotCollider(Collider hitCollider)
    {
        if (hitCollider == null ||
            hitCollider.transform == enemyObject.transform ||
            hitCollider.transform.IsChildOf(enemyObject.transform) ||
            playerReference.IsPlayerCollider(hitCollider))
        {
            return false;
        }

        // PlayerのLayerを検査マスクに加えているが、Stage Maskに含まれない
        // 他の同Layerオブジェクトを障害物と誤認しない。
        int layerBit = 1 << hitCollider.gameObject.layer;
        return (projectileShooter.ProjectileStageCollisionMask.value & layerBit) != 0;
    }

    private Vector3 GetPredictedFirePointPosition(
        Vector3 candidateBodyPosition, Vector3 playerPosition)
    {
        Transform firePoint = projectileShooter.FirePointTransform;
        Vector3 deltaBody = candidateBodyPosition - enemyRigidbody.position;
        if (firePoint == null ||
            (firePoint != visualRoot && !firePoint.IsChildOf(visualRoot)))
        {
            return projectileShooter.FirePointPosition + deltaBody;
        }

        // VisualRootに追従するFirePointは、現在の回転オフセットではなく
        // 候補地点でPlayer側を向いたときの相対位置を計算する。
        Vector3 rootAtCandidate = visualRoot.position + deltaBody;
        Vector3 fireOffsetInRootRotation =
            Quaternion.Inverse(visualRoot.rotation) *
            (firePoint.position - visualRoot.position);
        Vector3 predictedFirePoint = firePoint.position + deltaBody;

        for (int i = 0; i < 2; i++)
        {
            Vector3 aimDirection = playerPosition - predictedFirePoint;
            aimDirection.z = 0f;
            if (aimDirection.sqrMagnitude <= DIRECTION_EPSILON * DIRECTION_EPSILON)
            {
                break;
            }

            Quaternion aimedRotation = Quaternion.LookRotation(
                aimDirection.normalized, Vector3.up);
            predictedFirePoint = rootAtCandidate +
                aimedRotation * fireOffsetInRootRotation;
        }

        return predictedFirePoint;
    }

    private void AbandonAttack()
    {
        HasAbandonedAttack = true;
        isSeekingShootingPosition = false;
        isPreparationActive = false;
        hasShootingDestination = false;
        pathNavigator.Reset();
        planarMotor.StopPlanarMovement();
        projectileShooter.HideAim();
        projectileShooter.HideAttackWarning();
    }

    public void BeginAttack()
    {
        projectileShooter.HideAttackWarning();

        if (HasAbandonedAttack)
        {
            return;
        }

        // 射撃開始時に追従基準を取り直すと、準備中のPlayer移動が相殺される。
        // 準備開始時の基準を維持し、射撃が始まった直後からその移動量を追いかける。
        // ShooterはBeginBurstで方向を固定し、後続弾は追尾弾にならない。
        if (TryGetCurrentPlayerTargetPosition(out Vector3 playerPosition))
        {
            lastAimTargetPosition = playerPosition;
            hasAimTarget = true;

            if (!hasFollowTarget)
            {
                CaptureFollowReference(playerPosition);
            }

            AlignVisualTowardTarget(playerPosition);
        }

        projectileShooter.BeginBurst(
            lastAimTargetPosition,
            enemyData,
            enemyData.AttackPower);

        isShooting = projectileShooter.IsBurstActive;
    }

    public bool UpdateAttack()
    {
        if (HasAbandonedAttack)
        {
            return true;
        }

        if (!isShooting)
        {
            planarMotor.StopPlanarMovement();
            return true;
        }

        UpdateShootingFollow();

        // 追従でPlayerを見失っても、5発の発射完了はShooterだけで進行する。
        if (!projectileShooter.UpdateBurst())
        {
            return false;
        }

        isShooting = false;
        planarMotor.StopPlanarMovement();
        return true;
    }

    public void HandleCollisionEnter(Collision collision)
    {
        // 射撃中の本体接触にダメージを設定しない。
    }

    public void HandleCollisionStay(Collision collision)
    {
        // 射撃中の本体接触にダメージを設定しない。
    }

    public void EndAttack()
    {
        projectileShooter.CancelBurst();
        projectileShooter.HideAim();
        projectileShooter.HideAttackWarning();
        pathNavigator?.Reset();
        planarMotor?.StopPlanarMovement();
        ResetRuntimeState();
    }

    private void RefreshAimTarget(bool captureFollowReference = false)
    {
        if (!TryGetCurrentPlayerTargetPosition(out Vector3 targetPosition))
        {
            // 最後に得たAimを保持する。未取得時は初回のフォールバック方向を使用する。
            return;
        }

        if (captureFollowReference)
        {
            CaptureFollowReference(targetPosition);
        }

        lastAimTargetPosition = targetPosition;
        hasAimTarget = true;
        AlignVisualTowardTarget(targetPosition);
    }

    private void CaptureFollowReference(Vector3 playerPosition)
    {
        playerToDroneOffsetAtPreparationStart = new Vector2(
            enemyRigidbody.position.x - playerPosition.x,
            enemyRigidbody.position.y - playerPosition.y);
        hasFollowTarget = true;
    }

    private void UpdateShootingFollow()
    {
        if (!TryGetCurrentPlayerTargetPosition(out Vector3 currentPlayerPosition))
        {
            planarMotor.StopPlanarMovement();
            return;
        }

        if (!hasFollowTarget)
        {
            // 予備動作時にPlayerが存在しなかった場合だけ、現在位置で基準を確保する。
            CaptureFollowReference(currentPlayerPosition);
        }

        Vector2 targetPosition =
            new Vector2(currentPlayerPosition.x, currentPlayerPosition.y) +
            playerToDroneOffsetAtPreparationStart;

        Vector2 currentPosition =
            new Vector2(enemyRigidbody.position.x, enemyRigidbody.position.y);

        Vector2 toTarget = targetPosition - currentPosition;
        float distance = toTarget.magnitude;

        if (distance <= DIRECTION_EPSILON ||
            enemyData.ShootingFollowSpeed <= 0f ||
            Time.fixedDeltaTime <= 0f)
        {
            planarMotor.StopPlanarMovement();
            return;
        }

        // 到達間際のオーバーシュートを防ぎ、速度上限4.0等を厳守する。
        float speed = Mathf.Min(
            enemyData.ShootingFollowSpeed,
            distance / Time.fixedDeltaTime);

        planarMotor.TrySetPlanarVelocity(toTarget / distance * speed);
    }

    private bool TryGetCurrentPlayerTargetPosition(out Vector3 targetWorldPosition)
    {
        if (!playerReference.TryGetPlayerTransform(out Transform playerTransform))
        {
            playerTargetCollider = null;
            targetWorldPosition = default;
            return false;
        }

        if (playerTargetCollider != null &&
            playerTargetCollider.transform != playerTransform &&
            !playerTargetCollider.transform.IsChildOf(playerTransform))
        {
            playerTargetCollider = null;
        }

        if (playerTargetCollider == null)
        {
            Transform targetTransform = playerTransform.Find(PLAYER_TARGET_COLLIDER_NAME);
            if (targetTransform != null)
            {
                playerTargetCollider = targetTransform.GetComponent<Collider>();
            }
        }

        if (playerTargetCollider != null &&
            playerTargetCollider.enabled &&
            playerTargetCollider.gameObject.activeInHierarchy)
        {
            targetWorldPosition = playerTargetCollider.bounds.center;
            return true;
        }

        // タレットと同様、射撃そのものはPlayer Rootでも継続可能にする。
        targetWorldPosition = playerTransform.position;
        return true;
    }

    private bool IsBlockingMovementCollider(Collider targetCollider)
    {
        return !playerReference.IsPlayerCollider(targetCollider);
    }

    private void AlignVisualTowardTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - projectileShooter.FirePointPosition;
        direction.z = 0f;

        if (direction.sqrMagnitude <= DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return;
        }

        // VisualRootのみ回し、Rigidbody/Colliderの軸やモデルのFacingOffsetを保持する。
        visualRoot.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void ResetRuntimeState()
    {
        isPreparationActive = false;
        isShooting = false;
        hasAimTarget = false;
        hasFollowTarget = false;
        preparationElapsedTime = 0f;
        fallbackDirectionSign = 1f;
        lastAimTargetPosition = default;
        playerToDroneOffsetAtPreparationStart = default;
        isSeekingShootingPosition = false;
        hasShootingDestination = false;
        hasUnsuccessfulSearch = false;
        shootingDestination = default;
        lastPositioningProgressPosition = default;
        lastPositioningProgressTime = 0f;
        nextShootingPositionSearchTime = 0f;
        firstUnsuccessfulSearchTime = 0f;
        HasAbandonedAttack = false;
    }
}
