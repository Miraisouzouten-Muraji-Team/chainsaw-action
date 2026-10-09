using System;
using System.Collections.Generic;
using UnityEngine;

// 責務:
// ・XYゲームプレイ平面上の巡回地点A/Bを往復する。
// ・巡回端で停止し、VisualRootだけを指定時間かけて左右反転する。
// ・検知範囲と障害物RayからPlayer発見を判定する。
// ・索敵中にPlayerへ接触した際の攻撃情報を送信する。
//
// 担当しない責務:
// ・SearchからAlertへのState遷移そのもの。
// ・Alert / Attackの具体処理。
// ・巡回地点のScene編集Gizmo。
[Serializable]
public sealed class DroneEnemySearchStrategy :
    IEnemySearchStrategy,
    IEnemyDebugRayProvider
{
    private const string PLAYER_TAG = "Player";
    private const int RAYCAST_HIT_BUFFER_SIZE = 16;
    private const float ARRIVAL_TOLERANCE = 0.05f;
    private const float DIRECTION_EPSILON = 0.001f;

    [Header("巡回地点")]
    [Tooltip("巡回地点A。実行開始時のワールド位置を巡回地点として固定する。")]
    [SerializeField]
    private Transform patrolPointA;

    [Tooltip("巡回地点B。実行開始時のワールド位置を巡回地点として固定する。")]
    [SerializeField]
    private Transform patrolPointB;

    private DroneEnemyData enemyData;

    private GameObject enemyObject;
    private Transform enemyTransform;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private Collider enemyCollider;

    private Transform playerTransform;
    private IAttackHitReceiver playerAttackHitReceiver;

    private RaycastHit[] raycastHitBuffer;

    private EnemyDebugRay obstacleCheckDebugRay;
    private bool hasObstacleCheckDebugRay;

    private Vector3 patrolPointAWorldPosition;
    private Vector3 patrolPointBWorldPosition;

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

        // メインColliderを暗黙に子階層から選ばないため、
        // Enemy本体のColliderを使用する。
        enemyCollider =
            enemyObject.GetComponent<Collider>();

        if (enemyCollider == null)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemySearchStrategy)}を使用するEnemy本体には" +
                $"{nameof(Collider)}が必要です。");
        }

        ValidatePatrolPoints();
        CachePatrolPointWorldPositions();

        raycastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];

        ResolvePlayerReferences();
    }

    public void BeginSearch()
    {
        hasObstacleCheckDebugRay = false;

        isSearchActive = true;
        isTurning = false;

        turnElapsedTime = 0f;

        SelectInitialPatrolTarget();
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
                "Patrol Point AとBは異なる位置に設定してください。");
        }
    }

    private void CachePatrolPointWorldPositions()
    {
        // PointをEnemyの子にしても巡回範囲までEnemyに追従しないよう、
        // 初期位置を固定する。
        float gameplayDepth =
            enemyRigidbody.position.z;

        patrolPointAWorldPosition =
            new Vector3(
                patrolPointA.position.x,
                patrolPointA.position.y,
                gameplayDepth);

        patrolPointBWorldPosition =
            new Vector3(
                patrolPointB.position.x,
                patrolPointB.position.y,
                gameplayDepth);
    }

    private void SelectInitialPatrolTarget()
    {
        Vector2 currentPosition =
            GetCurrentPlanarPosition();

        Vector2 pointA =
            ToPlanarPosition(
                patrolPointAWorldPosition);

        Vector2 pointB =
            ToPlanarPosition(
                patrolPointBWorldPosition);

        float squaredDistanceToA =
            (pointA - currentPosition).sqrMagnitude;

        float squaredDistanceToB =
            (pointB - currentPosition).sqrMagnitude;

        // A側にいるならBへ、
        // B側にいるならAへ向かう。
        isMovingToPointB =
            squaredDistanceToA <= squaredDistanceToB;
    }

    private void UpdatePatrol()
    {
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

                0f);

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
                0f);

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

        if (playerTransform == null)
        {
            return false;
        }

        Vector3 searchOrigin =
            GetSearchOrigin();

        Vector3 playerPosition =
            playerTransform.position;

        float deltaX =
            playerPosition.x -
            searchOrigin.x;

        float deltaY =
            playerPosition.y -
            searchOrigin.y;

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

        return HasNoBlockingObstacleToPlayer(
            playerPosition);
    }

    private bool HasNoBlockingObstacleToPlayer(
        Vector3 playerPosition)
    {
        Vector3 rayOrigin =
            GetSearchOrigin();

        Vector3 planarPlayerPosition =
            new Vector3(
                playerPosition.x,
                playerPosition.y,
                rayOrigin.z);

        Vector3 rayVector =
            planarPlayerPosition -
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

        bool hasBlockingObstacle =
            false;

        for (int i = 0;
             i < hitCount;
             i++)
        {
            Collider hitCollider =
                raycastHitBuffer[i].collider;

            if (hitCollider == null ||
                IsEnemyOwnedCollider(hitCollider) ||
                IsPlayerCollider(hitCollider))
            {
                continue;
            }

            hasBlockingObstacle = true;
            break;
        }

        bool hasNoBlockingObstacle =
            !hasBlockingObstacle;

        obstacleCheckDebugRay =
            new EnemyDebugRay(
                rayOrigin,
                rayDirection,
                rayDistance,
                EnemyDebugRayKind.LineOfSight,
                hasNoBlockingObstacle
                    ? EnemyDebugRayResult.Success
                    : EnemyDebugRayResult.Blocked);

        hasObstacleCheckDebugRay =
            true;

        return hasNoBlockingObstacle;
    }

    private float GetDetectedPlayerDirectionSign()
    {
        float deltaX =
            playerTransform.position.x -
            GetSearchOrigin().x;

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

    private Vector3 GetSearchOrigin()
    {
        return enemyCollider.bounds.center;
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
