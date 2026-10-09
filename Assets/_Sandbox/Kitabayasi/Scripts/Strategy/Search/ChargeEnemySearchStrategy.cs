using System;
using System.Collections.Generic;
using UnityEngine;

/// 責務:
/// ・ゲーム開始時、またはDamage復帰時に更新された位置を基準に巡回する。
/// ・巡回基準時点のFacing方向へPatrolDistanceだけ移動して往復する。
/// ・巡回端または平坦面の終端で停止し、VisualRootだけを指定時間かけて180度反転する。
/// ・巡回中に進行方向を塞ぐ壁面へ衝突した場合は反転する。
/// ・巡回前に平坦面を判定し、穴・段差・坂へ進入しない。
/// ・XY平面上でプレイヤーとの距離を判定する。
/// ・EnemyとPlayerのX位置まで水平Rayを飛ばし、区間内の障害物を判定する。
/// ・索敵中にPlayerへ接触した際の攻撃情報を送信する。

[Serializable]
public sealed class ChargeEnemySearchStrategy :
    IEnemySearchStrategy,
    IEnemyPatrolOriginUpdater,
    IEnemyDebugRayProvider
{
    private const string PLAYER_TAG = "Player";

    private const int RAYCAST_HIT_BUFFER_SIZE = 16;

    private const float ARRIVAL_TOLERANCE = 0.05f;
    private const float DIRECTION_EPSILON = 0.001f;
    private const float WALL_NORMAL_X_THRESHOLD = 0.7f;

    private ChargeEnemyData enemyData;

    private GameObject enemyObject;
    private Transform enemyTransform;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private Collider enemyCollider;

    private ChargeEnemyGroundChecker groundChecker;

    private Transform playerTransform;
    private IAttackHitReceiver playerAttackHitReceiver;

    private RaycastHit[] raycastHitBuffer;

    private EnemyDebugRay obstacleCheckDebugRay;
    private bool hasObstacleCheckDebugRay;

    private Vector3 patrolStartPosition;

    private float initialPatrolDirectionSign;
    private float currentMoveDirectionSign;

    private bool isMovingToPatrolEnd;
    private bool isTurning;
    private bool isSearchActive;

    private float turnElapsedTime;

    private Quaternion turnStartRotation;
    private Quaternion turnTargetRotation;

    /// ゲーム開始時の巡回開始位置もここで固定する。
    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData = enemyData as ChargeEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(ChargeEnemySearchStrategy)}には" +
                $"{nameof(ChargeEnemyData)}が必要です。");

        this.enemyObject = enemyObject
            ?? throw new ArgumentNullException(nameof(enemyObject));

        enemyTransform = enemyObject.transform;

        this.visualRoot = visualRoot
            ? visualRoot
            : throw new InvalidOperationException(
                $"{nameof(ChargeEnemySearchStrategy)}を使用するEnemyには" +
                "VisualRootの設定が必要です。");

        enemyRigidbody =
            enemyObject.GetComponent<Rigidbody>();

        if (enemyRigidbody == null)
        {
            throw new InvalidOperationException(
                $"{nameof(ChargeEnemySearchStrategy)}を使用するEnemyには" +
                $"{nameof(Rigidbody)}が必要です。");
        }

        if (enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(ChargeEnemySearchStrategy)}は" +
                "非KinematicのRigidbodyを使用する前提です。");
        }

        enemyCollider =
            enemyObject.GetComponentInChildren<Collider>();

        if (enemyCollider == null)
        {
            throw new InvalidOperationException(
                $"{nameof(ChargeEnemySearchStrategy)}を使用するEnemyには" +
                $"{nameof(Collider)}が必要です。");
        }

        groundChecker =
            new ChargeEnemyGroundChecker(
                this.enemyData,
                enemyTransform,
                enemyRigidbody,
                enemyCollider);

        raycastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];

        // 巡回開始位置はSearchStateへ入るたびではなく、
        // ゲーム開始時の位置として固定する。
        patrolStartPosition = enemyRigidbody.position;

        initialPatrolDirectionSign =
            GetHorizontalDirectionSign(
                visualRoot);

        ResolvePlayerReferences();
    }

    public void BeginSearch()
    {
        hasObstacleCheckDebugRay = false;
        groundChecker?.ClearDebugRays();

        isSearchActive = true;
        isTurning = false;
        isMovingToPatrolEnd = true;

        currentMoveDirectionSign =
            initialPatrolDirectionSign;

        turnElapsedTime = 0f;

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
            Vector3 searchOrigin =
                GetSearchOrigin();

            float deltaX =
                playerTransform.position.x -
                searchOrigin.x;

            if (Mathf.Abs(deltaX) >
                DIRECTION_EPSILON)
            {
                detectedPlayerDirectionSign =
                    Mathf.Sign(deltaX);
            }
            else
            {
                detectedPlayerDirectionSign =
                    currentMoveDirectionSign;
            }

            StopHorizontalMovement();

            // State遷移要求後にSearchStateが一時的に残っても、
            // 巡回処理や接触攻撃を継続しないよう停止する。
            isSearchActive = false;

            return true;
        }

        UpdatePatrol();

        return false;
    }

    /// <summary>
    /// SearchState中のCollisionを処理する。
    /// Player接触時は攻撃情報を送り、
    /// 進行方向を塞ぐ壁との接触時は巡回方向を反転する。
    /// /// <summary>
    public void HandleCollisionEnter(
        Collision collision)
    {
        if (!isSearchActive ||
            collision == null)
        {
            return;
        }

        ResolvePlayerReferences();

        Collider collidedCollider =
            collision.collider;

        // Playerとの接触は壁判定とは別に処理する。
        if (playerTransform != null &&
            IsPlayerCollider(collidedCollider))
        {
            if (playerAttackHitReceiver == null)
            {
                Debug.LogWarning(
                    $"{nameof(ChargeEnemySearchStrategy)}: " +
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

            return;
        }

        if (isTurning)
        {
            return;
        }

        if (IsWallBlockingCurrentMoveDirection(
                collision))
        {
            BeginTurn();
        }
    }

    /// <summary>
    /// 現在の巡回方向を正面から塞ぐ接触面が存在するか判定する。
    /// 地面との接触は法線が主に上下方向を向くため、
    /// 壁として扱わない。
    /// </summary>
    private bool IsWallBlockingCurrentMoveDirection(
        Collision collision)
    {
        if (Mathf.Abs(currentMoveDirectionSign) <=
            DIRECTION_EPSILON)
        {
            return false;
        }

        int contactCount =
            collision.contactCount;

        for (int i = 0;
             i < contactCount;
             i++)
        {
            ContactPoint contact =
                collision.GetContact(i);

            float normalAgainstMoveDirection =
                contact.normal.x *
                currentMoveDirectionSign;

            if (normalAgainstMoveDirection <=
                -WALL_NORMAL_X_THRESHOLD)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 索敵Stateを終了する。
    /// </summary>
    public void EndSearch()
    {
        hasObstacleCheckDebugRay = false;
        groundChecker?.ClearDebugRays();

        isSearchActive = false;
        isTurning = false;

        StopHorizontalMovement();
    }

    /// <summary>
    /// 索敵処理で実際に使用した障害物判定Rayと地面判定Rayを提供する。
    /// </summary>
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

        groundChecker?.CollectDebugRays(
            debugRays);
    }

    /// <summary>
    /// Damage終了後の現在位置と見た目の向きを、
    /// 次回Searchの巡回基準として保存する。
    /// </summary>
    public void UpdatePatrolOriginFromCurrentPose()
    {
        patrolStartPosition =
            enemyRigidbody.position;

        initialPatrolDirectionSign =
            GetHorizontalDirectionSign(
                visualRoot);
    }

    /// <summary>
    /// 巡回移動または反転処理を更新する。
    /// </summary>
    private void UpdatePatrol()
    {
        if (enemyData.PatrolDistance <= 0f)
        {
            StopHorizontalMovement();
            return;
        }

        if (isTurning)
        {
            UpdateTurn();
            return;
        }

        float targetPositionX =
            GetCurrentPatrolTargetPositionX();

        if (HasReachedPatrolTarget(
                targetPositionX))
        {
            BeginTurn();
            return;
        }

        float expectedMovementDistance =
            enemyData.PatrolMoveSpeed *
            Time.fixedDeltaTime;

        if (!groundChecker.CanMoveInDirection(
                currentMoveDirectionSign,
                expectedMovementDistance))
        {
            BeginTurn();
            return;
        }

        float targetVelocityX =
            currentMoveDirectionSign *
            enemyData.PatrolMoveSpeed;

        SetHorizontalVelocity(
            targetVelocityX);
    }

    /// <summary>
    /// 現在向かっている巡回地点のX座標を取得する。
    /// </summary>
    private float GetCurrentPatrolTargetPositionX()
    {
        if (!isMovingToPatrolEnd)
        {
            return patrolStartPosition.x;
        }

        return patrolStartPosition.x +
               initialPatrolDirectionSign *
               enemyData.PatrolDistance;
    }

    /// <summary>
    /// 現在向かっている巡回地点へ到達したか判定する。
    /// </summary>
    private bool HasReachedPatrolTarget(
        float targetPositionX)
    {
        float remainingDistance =
            (targetPositionX -
             enemyRigidbody.position.x) *
            currentMoveDirectionSign;

        return remainingDistance <=
               ARRIVAL_TOLERANCE;
    }

    /// <summary>
    /// 巡回端での180度反転を開始する。
    /// </summary>
    private void BeginTurn()
    {
        StopHorizontalMovement();

        turnElapsedTime = 0f;

        turnStartRotation =
            visualRoot.rotation;

        // Rigidbody / Colliderは固定したまま、
        // 既存仕様と同じワールドY軸周りでVisualRootだけを左右反転する。
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

    /// <summary>
    /// 反転処理を更新する。
    /// </summary>
    private void UpdateTurn()
    {
        StopHorizontalMovement();

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

        Quaternion nextRotation =
            Quaternion.Slerp(
                turnStartRotation,
                turnTargetRotation,
                progress);

        visualRoot.rotation =
            nextRotation;

        if (progress < 1f)
        {
            return;
        }

        CompleteTurn();
    }

    /// <summary>
    /// 反転時間が0の場合に即座に反転を完了する。
    /// </summary>
    private void CompleteTurnImmediately()
    {
        visualRoot.rotation =
            turnTargetRotation;

        CompleteTurn();
    }

    /// <summary>
    /// 反転完了後、次の巡回地点へ向かう状態へ切り替える。
    /// </summary>
    private void CompleteTurn()
    {
        isTurning = false;

        isMovingToPatrolEnd =
            !isMovingToPatrolEnd;

        currentMoveDirectionSign *= -1f;
    }

    /// <summary>
    /// RigidbodyのY方向速度を維持しながら、
    /// X方向を指定速度へ補正する。
    /// </summary>
    private void SetHorizontalVelocity(
        float targetVelocityX)
    {
        float currentVelocityX =
            enemyRigidbody.linearVelocity.x;

        float velocityDifference =
            targetVelocityX -
            currentVelocityX;

        if (Mathf.Abs(velocityDifference) <=
            DIRECTION_EPSILON)
        {
            return;
        }

        enemyRigidbody.AddForce(
            new Vector3(
                velocityDifference,
                0f,
                0f),
            ForceMode.VelocityChange);
    }

    /// <summary>
    /// X方向の移動速度のみ停止する。
    /// 重力などによるY方向速度は維持する。
    /// </summary>
    private void StopHorizontalMovement()
    {
        if (enemyRigidbody == null)
        {
            return;
        }

        float currentVelocityX =
            enemyRigidbody.linearVelocity.x;

        if (Mathf.Abs(currentVelocityX) <=
            DIRECTION_EPSILON)
        {
            return;
        }

        enemyRigidbody.AddForce(
            new Vector3(
                -currentVelocityX,
                0f,
                0f),
            ForceMode.VelocityChange);
    }

    /// <summary>
    /// Playerが検知範囲内に存在し、
    /// かつPlayerのX位置までの水平区間に障害物が存在しないか判定する。
    /// </summary>
    private bool CanDetectPlayer()
    {
        // このFixedUpdateで障害物判定Rayを投げなかった場合に、
        // 前回のRayを現在のRayとして表示し続けない。
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
            detectionRadius *
            detectionRadius)
        {
            return false;
        }

        return HasNoBlockingObstacleToPlayer(
            playerPosition.x);
    }

    /// <summary>
    /// EnemyのCollider中央からPlayerのX位置まで水平Rayを飛ばし、
    /// その区間にPlayer以外の障害物Colliderが存在しないか判定する。
    /// </summary>
    private bool HasNoBlockingObstacleToPlayer(
        float playerPositionX)
    {
        Vector3 rayOrigin =
            GetSearchOrigin();

        float deltaX =
            playerPositionX -
            rayOrigin.x;

        float rayDistance =
            Mathf.Abs(deltaX);

        if (rayDistance <=
            DIRECTION_EPSILON)
        {
            return true;
        }

        Vector3 rayDirection =
            deltaX > 0f
                ? Vector3.right
                : Vector3.left;

        int hitCount =
            Physics.RaycastNonAlloc(
                rayOrigin,
                rayDirection,
                raycastHitBuffer,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

        bool hasBlockingObstacle = false;

        for (int i = 0;
             i < hitCount;
             i++)
        {
            Collider hitCollider =
                raycastHitBuffer[i].collider;

            if (hitCollider == null)
            {
                continue;
            }

            // Enemy自身とPlayerのColliderは遮蔽物として扱わない。
            // PlayerそのものへRayを当てることは索敵成立の条件にしない。
            if (IsEnemyOwnedCollider(
                    hitCollider) ||
                IsPlayerCollider(
                    hitCollider))
            {
                continue;
            }

            hasBlockingObstacle = true;
            break;
        }

        bool hasNoBlockingObstacle =
            !hasBlockingObstacle;

        // Physicsへ渡した始点・方向・距離と判定結果をそのまま保持する。
        // Gizmo側では現在位置からRayを再計算しない。
        obstacleCheckDebugRay =
            new EnemyDebugRay(
                rayOrigin,
                rayDirection,
                rayDistance,
                EnemyDebugRayKind.LineOfSight,
                hasNoBlockingObstacle
                    ? EnemyDebugRayResult.Success
                    : EnemyDebugRayResult.Blocked);

        hasObstacleCheckDebugRay = true;

        return hasNoBlockingObstacle;
    }

    /// <summary>
    /// 索敵距離判定と障害物判定Rayで共通使用するEnemy側の基準位置を取得する。
    /// </summary>
    private Vector3 GetSearchOrigin()
    {
        return enemyCollider.bounds.center;
    }

    /// <summary>
    /// 指定ColliderがこのEnemy自身のものか判定する。
    /// </summary>
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

    /// <summary>
    /// 指定ColliderがPlayerのものか判定する。
    /// </summary>
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

    /// <summary>
    /// Player TagからPlayerの参照を取得する。
    /// 一度取得した後は再検索しない。
    /// </summary>
    private void ResolvePlayerReferences()
    {
        if (playerTransform != null)
        {
            return;
        }

        GameObject playerObject =
            GameObject.FindGameObjectWithTag(
                PLAYER_TAG);

        if (playerObject == null)
        {
            return;
        }

        playerTransform =
            playerObject.transform;

        ResolvePlayerAttackHitReceiver(
            playerObject);
    }

    /// <summary>
    /// Player階層からIAttackHitReceiverの実装を取得する。
    /// </summary>
    private void ResolvePlayerAttackHitReceiver(
        GameObject playerObject)
    {
        MonoBehaviour[] behaviours =
            playerObject.GetComponentsInChildren<MonoBehaviour>(
                true);

        playerAttackHitReceiver = null;

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

    /// <summary>
    /// VisualRootが現在向いている方向から、
    /// X方向の符号を取得する。
    /// </summary>
    private static float GetHorizontalDirectionSign(
        Transform targetTransform)
    {
        float forwardX =
            targetTransform.forward.x;

        if (Mathf.Abs(forwardX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(forwardX);
        }

        float rightX =
            targetTransform.right.x;

        if (Mathf.Abs(rightX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(rightX);
        }

        Debug.LogWarning(
            $"{nameof(ChargeEnemySearchStrategy)}: " +
            "VisualRootの向きからX方向を判定できなかったため、" +
            "+X方向を使用します。",
            targetTransform);

        return 1f;
    }
}
