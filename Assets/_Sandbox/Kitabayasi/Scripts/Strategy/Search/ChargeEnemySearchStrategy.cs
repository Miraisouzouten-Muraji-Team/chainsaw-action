using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// 責務:
/// ・配置時の位置を巡回基準として保持し、物理的に帰還できないと確認した場合だけ地面上で更新する。
/// ・着地イベントや単発のRay判定に依存せず、実際の帰還移動の進捗を確認する。
/// ・左右の移動可能距離を比較し、PatrolOffsetを上限とする巡回区間を決める。
/// ・巡回端または平坦面の終端で停止し、VisualRootだけを指定時間かけて180度反転する。
/// ・巡回中に進行方向を塞ぐ壁面へ衝突した場合は反転する。
/// ・巡回前に平坦面を判定し、穴・段差・坂へ進入しない。
/// ・XY平面上でプレイヤーとの距離を判定する。
/// ・EnemyとPlayerのX位置まで水平Rayを飛ばし、区間内の障害物を判定する。
/// ・初期位置への帰還中も索敵を継続し、Player発見時は通常のAlert遷移へ通知する。
/// ・帰還中を含む索敵中にPlayerへ接触した際の攻撃情報を送信する。

[Serializable]
public sealed class ChargeEnemySearchStrategy :
    IEnemySearchStrategy,
    IEnemyDebugRayProvider
{
    private const float ARRIVAL_TOLERANCE = 0.05f;
    private const float DIRECTION_EPSILON = 0.001f;
    private const float WALL_NORMAL_X_THRESHOLD = 0.7f;
    private const int ANIMATION_BASE_LAYER_INDEX = 0;
    private const float ANIMATION_SPEED_EPSILON = 0.0001f;
    private const float WALK_STALL_WARNING_SECONDS = 0.75f;
    private const float WALK_PROGRESS_EPSILON = 0.0001f;
    private const float WALK_REPLAY_WINDOW_SECONDS = 1f;
    private const int WALK_REPLAY_WARNING_COUNT = 12;
    private const float RETURN_STALL_CONFIRM_SECONDS = 0.8f;
    private const float RETURN_PROGRESS_SAMPLE_DISTANCE = 0.06f;

    [Header("個体設定")]
    [Tooltip("このChargeEnemyがPlayerを検知する半径。ChargeEnemyDataのDetectionRadiusは使用しない。")]
    [SerializeField]
    [Min(0f)]
    private float detectionRadius;

    [Tooltip("巡回距離の上限と、左右の移動可能距離が同じ場合の優先方向。正数は+X、負数は-X。実行中は安全な方向・距離に自動調整する。")]
    [FormerlySerializedAs("patrolDistance")]
    [SerializeField]
    private float patrolOffset;

    [Header("巡回アニメーション")]
    [Tooltip("帰還・巡回中に再生するAnimator Stateのフルパス（例: Base Layer.Walk）。")]
    [SerializeField]
    private string walkAnimationStateName = "Base Layer.Walk";

    private ChargeEnemyData enemyData;

    private GameObject enemyObject;
    private Transform enemyTransform;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private Collider enemyCollider;
    private Animator animator;
    private int walkAnimationStateHash;

    // Walkの再生位置が止まった場合、Animator側の状態を1度だけ記録する。
    private bool hasWalkProgressSample;
    private bool hasReportedWalkStall;
    private float lastWalkNormalizedTime;
    private float lastWalkProgressTime;
    private float walkReplayWindowStartTime;
    private int walkReplayCount;
    private bool hasReportedRepeatedWalkReplay;
    private bool hasRestoredStoppedAnimatorSpeed;

    private ChargeEnemyGroundChecker groundChecker;

    private EnemyPlayerReference playerReference;
    private EnemyPlayerDetectionSensor playerDetectionSensor;

    private Vector3 patrolStartPosition;
    private float activePatrolOffset;

    private float currentMoveDirectionSign;

    private bool isMovingToPatrolEnd;
    private bool isTurning;
    private bool isSearchActive;
    private bool isReturningToPatrolStart;
    private bool isAwaitingGroundForPatrol;

    // 進行が止まっただけでは帰還不能にせず、下段との高低差かStage壁接触で裏付ける。
    private float returnProgressSampleX;
    private float returnStallElapsedTime;
    private bool hasStageWallContactDuringReturn;
    private float stageWallContactX;

    private float turnElapsedTime;

    private Quaternion turnStartRotation;
    private Quaternion turnTargetRotation;

    /// <summary>
    /// このChargeEnemy個体に設定された索敵半径。
    /// </summary>
    public float DetectionRadius => detectionRadius;

    /// <summary>
    /// このChargeEnemy個体に設定された巡回開始地点から折り返し地点までの符号付きオフセット。
    /// </summary>
    public float PatrolOffset => patrolOffset;

    /// <summary>Play中に採用している巡回区間の符号付きX距離。</summary>
    public float ActivePatrolOffset => activePatrolOffset;

    /// <summary>Play中に使用している巡回基準位置。</summary>
    public Vector3 PatrolStartPosition => patrolStartPosition;

    /// ゲーム開始時の配置位置を初期巡回基準として記録する。
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

        playerReference =
            new EnemyPlayerReference();

        playerDetectionSensor =
            new EnemyPlayerDetectionSensor(
                enemyTransform,
                playerReference);

        groundChecker =
            new ChargeEnemyGroundChecker(
                this.enemyData,
                enemyTransform,
                enemyRigidbody,
                enemyCollider);

        InitializeWalkAnimation();
        ResetWalkProgressSample();

        // 巡回開始位置はSearchStateへ入るたびではなく、
        // ゲーム開始時の位置として固定する。
        patrolStartPosition = enemyRigidbody.position;
        activePatrolOffset = patrolOffset;

        playerReference.TryGetPlayerTransform(
            out _);
    }

    public void BeginSearch()
    {
        playerDetectionSensor.ClearDebugRay();
        groundChecker?.ClearDebugRays();

        isSearchActive = true;
        isTurning = false;
        isMovingToPatrolEnd = true;
        isReturningToPatrolStart = false;
        isAwaitingGroundForPatrol = false;
        turnElapsedTime = 0f;
        ResetReturnProgressTracking();

        // 着地瞬間の通知ではなく、地面上での帰還移動の進捗を確認する。
        DecideReturnOrPatrol();

        Debug.Log(
            $"[ChargeEnemyReturnDebug] Search開始。 " +
            $"patrolStartX={patrolStartPosition.x:F3}, " +
            $"currentX={enemyRigidbody.position.x:F3}, " +
            $"isReturning={isReturningToPatrolStart}, " +
            $"awaitingGround={isAwaitingGroundForPatrol}, " +
            $"patrolOffset={activePatrolOffset:F3}",
            enemyObject);

        if (enemyData.PatrolMoveSpeed > DIRECTION_EPSILON &&
            (isReturningToPatrolStart || Mathf.Abs(activePatrolOffset) > DIRECTION_EPSILON))
        {
            EnsureWalkAnimation();
        }

        playerReference.TryGetPlayerTransform(out _);
    }

    private void DecideReturnOrPatrol()
    {
        if (!groundChecker.IsStandingOnStageGround())
        {
            // 空中を新しい巡回基準にしない。接地を待った後に帰還可否を判定する。
            isAwaitingGroundForPatrol = true;
            isReturningToPatrolStart = false;
            currentMoveDirectionSign = 0f;
            StopHorizontalMovement();
            return;
        }

        isAwaitingGroundForPatrol = false;

        float deltaX = patrolStartPosition.x - enemyRigidbody.position.x;

        // Xがほぼ同じなのに別の高さにいるなら、水平移動では旧原点に到達できない。
        // 離れた位置ではRayが一度失敗しても原点を変更せず、実際の帰還を試す。
        if (Mathf.Abs(deltaX) <= ARRIVAL_TOLERANCE &&
            !groundChecker.IsAtGroundHeight(patrolStartPosition.y))
        {
            SetCurrentPositionAsPatrolOrigin();
            return;
        }

        isReturningToPatrolStart = Mathf.Abs(deltaX) > ARRIVAL_TOLERANCE;
        if (isReturningToPatrolStart)
        {
            ResetReturnProgressTracking();
            currentMoveDirectionSign = Mathf.Sign(deltaX);
            AlignVisualWithCurrentMoveDirection();
            return;
        }

        SelectAvailablePatrolDirection();
        AlignVisualWithCurrentMoveDirection();
    }

    private void SetCurrentPositionAsPatrolOrigin()
    {
        // 物理的に接地した位置だけを原点にする。旧原点への帰還要求はここで解除する。
        ResetReturnProgressTracking();
        patrolStartPosition = enemyRigidbody.position;
        isReturningToPatrolStart = false;
        isAwaitingGroundForPatrol = false;
        isMovingToPatrolEnd = true;
        isTurning = false;
        SelectAvailablePatrolDirection();
        AlignVisualWithCurrentMoveDirection();

        Debug.Log(
            $"[ChargeEnemyReturnDebug] 帰還不能のため巡回基準を更新。 " +
            $"newStart={patrolStartPosition}, patrolOffset={activePatrolOffset:F3}",
            enemyObject);
    }

    private void SelectAvailablePatrolDirection()
    {
        float maximumDistance = Mathf.Abs(patrolOffset);
        if (maximumDistance <= ARRIVAL_TOLERANCE)
        {
            activePatrolOffset = 0f;
            currentMoveDirectionSign = 0f;
            return;
        }

        float rightDistance = groundChecker.MeasureSafeHorizontalDistance(1f, maximumDistance);
        float leftDistance = groundChecker.MeasureSafeHorizontalDistance(-1f, maximumDistance);
        float preferredDirection = Mathf.Sign(patrolOffset);
        float directionSign = rightDistance > leftDistance + DIRECTION_EPSILON
            ? 1f
            : leftDistance > rightDistance + DIRECTION_EPSILON
                ? -1f
                : preferredDirection;
        float selectedDistance = directionSign > 0f ? rightDistance : leftDistance;

        // 壁際の数cmだけを往復してその場で回転し続けるのを防ぐ。
        float minimumPatrolDistance = Mathf.Max(
            ARRIVAL_TOLERANCE,
            enemyCollider.bounds.size.x * 0.5f);
        activePatrolOffset = selectedDistance > minimumPatrolDistance
            ? directionSign * selectedDistance
            : 0f;
        currentMoveDirectionSign = GetPatrolEndDirectionSign();
    }

    public bool UpdateSearch(
        out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;

        if (!isSearchActive)
        {
            return false;
        }

        // 帰還はSearch中の移動方法の1つとして扱い、
        // 帰還中でも通常と同じ条件でPlayerを索敵し続ける。
        if (TryDetectPlayer(
                out detectedPlayerDirectionSign))
        {
            StopHorizontalMovement();

            // State遷移要求後にSearchStateが一時的に残っても、
            // 帰還・巡回・接触攻撃を継続しないよう停止する。
            isSearchActive = false;

            return true;
        }

        if (isAwaitingGroundForPatrol)
        {
            StopHorizontalMovement();
            if (groundChecker.IsStandingOnStageGround())
            {
                DecideReturnOrPatrol();
            }
            return false;
        }

        if (isReturningToPatrolStart)
        {
            UpdateReturnToPatrolStart();
            return false;
        }

        UpdatePatrol();

        return false;
    }

    public bool TryDetectPlayer(
        out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;

        if (!CanDetectPlayer())
        {
            return false;
        }

        if (!playerReference.TryGetPlayerTransform(
                out Transform playerTransform))
        {
            return false;
        }

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
        else if (Mathf.Abs(currentMoveDirectionSign) >
                 DIRECTION_EPSILON)
        {
            detectedPlayerDirectionSign =
                currentMoveDirectionSign;
        }
        else
        {
            float patrolDirectionSign =
                GetPatrolEndDirectionSign();

            detectedPlayerDirectionSign =
                Mathf.Abs(patrolDirectionSign) > DIRECTION_EPSILON
                    ? patrolDirectionSign
                    : 1f;
        }

        return true;
    }

    /// <summary>
    /// SearchState中のCollisionを処理する。
    /// Player接触時は攻撃情報を送り、
    /// 進行方向を塞ぐ壁との接触時は巡回方向を反転する。
    /// </summary>
    public void HandleCollisionEnter(
        Collision collision)
    {
        if (!isSearchActive ||
            collision == null)
        {
            return;
        }

        Collider collidedCollider =
            collision.collider;

        // 帰還中もSearch中であることに変わりはないため、
        // Player接触時は通常巡回中と同じ接触ダメージを与える。
        if (playerReference.IsPlayerCollider(
                collidedCollider))
        {
            if (!playerReference.TryGetAttackHitReceiver(
                    requireActiveAndEnabled: false,
                    out IAttackHitReceiver playerAttackHitReceiver,
                    out _))
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

        if (isReturningToPatrolStart)
        {
            if (IsWallBlockingCurrentMoveDirection(collision))
            {
                // OnCollisionEnter単発では帰還不能としない。接触しても通過できる地形がある。
                hasStageWallContactDuringReturn = true;
                stageWallContactX = enemyRigidbody.position.x;
            }
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
        if (collision == null ||
            collision.collider == null ||
            !IsStageCollider(collision.collider) ||
            Mathf.Abs(currentMoveDirectionSign) <=
            DIRECTION_EPSILON)
        {
            return false;
        }

        Vector3 moveDirection =
            Vector3.right *
            currentMoveDirectionSign;

        if (!EnemyBlockingContactEvaluator.TryGetBlockingContact(
                collision,
                moveDirection,
                WALL_NORMAL_X_THRESHOLD,
                out ContactPoint blockingContact))
        {
            return false;
        }

        // 許容傾斜内の接触を「壁で帰還不能」と誤認しない。
        return Vector3.Angle(blockingContact.normal, Vector3.up) >
               enemyData.MaxGroundSlopeAngle;
    }

    private bool IsStageCollider(
        Collider targetCollider)
    {
        int targetLayerMask =
            1 << targetCollider.gameObject.layer;

        return (enemyData.StageLayerMask.value &
                targetLayerMask) != 0;
    }

    /// <summary>
    /// 索敵Stateを終了する。
    /// </summary>
    public void EndSearch()
    {
        playerDetectionSensor.ClearDebugRay();
        groundChecker?.ClearDebugRays();

        isSearchActive = false;
        isTurning = false;
        isReturningToPatrolStart = false;
        isAwaitingGroundForPatrol = false;
        ResetReturnProgressTracking();

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

        if (playerDetectionSensor.TryGetDebugRay(
                out EnemyDebugRay detectionDebugRay))
        {
            debugRays.Add(
                detectionDebugRay);
        }

        groundChecker?.CollectDebugRays(
            debugRays);
    }

    /// <summary>
    /// 帰還中は物理移動を試し、進捗が一定時間なく、下段への落下かStage壁接触が
    /// 確認できたときだけ原点を変更する。地面を離れたら再接地まで待つ。
    /// </summary>
    private void UpdateReturnToPatrolStart()
    {
        if (!groundChecker.IsStandingOnStageGround())
        {
            isReturningToPatrolStart = false;
            isAwaitingGroundForPatrol = true;
            ResetReturnProgressTracking();
            StopHorizontalMovement();
            return;
        }

        float deltaX = patrolStartPosition.x - enemyRigidbody.position.x;
        if (Mathf.Abs(deltaX) <= ARRIVAL_TOLERANCE)
        {
            if (groundChecker.IsAtGroundHeight(patrolStartPosition.y))
            {
                CompleteReturnToPatrolStart();
            }
            else
            {
                HandleUnreachablePatrolOrigin("帰還地点の高さが一致しない");
            }
            return;
        }

        float moveSpeed = enemyData.PatrolMoveSpeed;
        if (moveSpeed <= DIRECTION_EPSILON)
        {
            StopHorizontalMovement();
            return;
        }

        float fixedDeltaTime = Time.fixedDeltaTime;
        if (fixedDeltaTime <= DIRECTION_EPSILON)
        {
            StopHorizontalMovement();
            return;
        }

        currentMoveDirectionSign = Mathf.Sign(deltaX);

        if (HasConfirmedUnreachableReturn(moveSpeed, fixedDeltaTime))
        {
            HandleUnreachablePatrolOrigin("Stage壁または下段からの帰還中に進捗がない");
            return;
        }

        float expectedMovementDistance = moveSpeed * fixedDeltaTime;

        // 1回のRayの不発では停止させず、実際に戻れる経路なら移動を継続する。
        float targetVelocityX = Mathf.Abs(deltaX) <= expectedMovementDistance
            ? deltaX / fixedDeltaTime
            : currentMoveDirectionSign * moveSpeed;
        SetHorizontalVelocity(targetVelocityX);
        EnsureWalkAnimation();
    }

    private void ResetReturnProgressTracking()
    {
        returnProgressSampleX = enemyRigidbody != null ? enemyRigidbody.position.x : 0f;
        returnStallElapsedTime = 0f;
        hasStageWallContactDuringReturn = false;
        stageWallContactX = 0f;
    }

    /// <summary>
    /// 物理ステップをまたいでも帰還方向へ進めないときだけ帰還不能を確定する。
    /// Playerなどによる一時的な停滞や緩やかな坂では旧原点を保持する。
    /// </summary>
    private bool HasConfirmedUnreachableReturn(float moveSpeed, float fixedDeltaTime)
    {
        float currentX = enemyRigidbody.position.x;
        float progress = (currentX - returnProgressSampleX) * currentMoveDirectionSign;
        float requiredProgress = Mathf.Min(
            RETURN_PROGRESS_SAMPLE_DISTANCE,
            moveSpeed * RETURN_STALL_CONFIRM_SECONDS * 0.25f);

        if (progress >= requiredProgress)
        {
            returnProgressSampleX = currentX;
            returnStallElapsedTime = 0f;

            // 以前の接触位置を通過したなら、その壁は帰還を妨げていない。
            if (hasStageWallContactDuringReturn &&
                Mathf.Abs(currentX - stageWallContactX) > enemyCollider.bounds.extents.x)
            {
                hasStageWallContactDuringReturn = false;
            }
            return false;
        }

        returnStallElapsedTime += fixedDeltaTime;
        if (returnStallElapsedTime < RETURN_STALL_CONFIRM_SECONDS)
        {
            return false;
        }

        float originHeightDifference = patrolStartPosition.y - enemyRigidbody.position.y;
        bool isBelowOriginalPlatform = originHeightDifference >
            Mathf.Max(enemyData.MaxGroundHeightDifference, ARRIVAL_TOLERANCE);

        // 停滞だけで判断すると、Playerなどに塞がれた際に誤って初期位置を更新する。
        return isBelowOriginalPlatform || hasStageWallContactDuringReturn;
    }

    private void HandleUnreachablePatrolOrigin(string reason)
    {
        StopHorizontalMovement();
        if (!groundChecker.IsStandingOnStageGround())
        {
            isReturningToPatrolStart = false;
            isAwaitingGroundForPatrol = true;
            return;
        }

        Debug.Log(
            $"[ChargeEnemyReturnDebug] 帰還不可: {reason}。 " +
            $"currentX={enemyRigidbody.position.x:F3}, patrolStartX={patrolStartPosition.x:F3}, " +
            $"currentY={enemyRigidbody.position.y:F3}, patrolStartY={patrolStartPosition.y:F3}, " +
            $"noProgressSeconds={returnStallElapsedTime:F2}",
            enemyObject);
        SetCurrentPositionAsPatrolOrigin();
    }

    private void CompleteReturnToPatrolStart()
    {
        ResetReturnProgressTracking();
        StopHorizontalMovement();
        isReturningToPatrolStart = false;
        isMovingToPatrolEnd = true;
        isTurning = false;
        SelectAvailablePatrolDirection();
        AlignVisualWithCurrentMoveDirection();

        Debug.Log(
            $"[ChargeEnemyReturnDebug] 巡回基準への帰還完了。 " +
            $"currentX={enemyRigidbody.position.x:F3}, patrolStartX={patrolStartPosition.x:F3}, " +
            $"patrolOffset={activePatrolOffset:F3}",
            enemyObject);

        LogWalkAnimationSnapshot("帰還完了・巡回へ切り替え");
        if (enemyData.PatrolMoveSpeed > DIRECTION_EPSILON &&
            Mathf.Abs(activePatrolOffset) > DIRECTION_EPSILON)
        {
            PlayWalkAnimationFromStart();
        }
    }

    /// <summary>
    /// 巡回移動または反転処理を更新する。
    /// </summary>
    private void UpdatePatrol()
    {
        if (Mathf.Abs(activePatrolOffset) <=
            DIRECTION_EPSILON)
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
        if (Mathf.Abs(targetVelocityX) > DIRECTION_EPSILON)
        {
            EnsureWalkAnimation();
        }
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
               activePatrolOffset;
    }

    /// <summary>
    /// ワールドX方向の符号付きPatrolOffsetから、
    /// 巡回端へ向かうX方向の符号を取得する。
    /// </summary>
    private float GetPatrolEndDirectionSign()
    {
        if (Mathf.Abs(activePatrolOffset) <=
            DIRECTION_EPSILON)
        {
            return 0f;
        }

        return Mathf.Sign(activePatrolOffset);
    }

    /// <summary>
    /// VisualRootのローカル+Zを、現在要求している左右移動方向へ合わせる。
    /// </summary>
    private void AlignVisualWithCurrentMoveDirection()
    {
        AlignVisualWithHorizontalDirection(
            currentMoveDirectionSign);
    }

    private void AlignVisualWithHorizontalDirection(
        float directionSign)
    {
        if (Mathf.Abs(directionSign) <=
            DIRECTION_EPSILON)
        {
            return;
        }

        visualRoot.rotation =
            Quaternion.LookRotation(
                Vector3.right * Mathf.Sign(directionSign),
                Vector3.up);
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
    /// 既存のAnimator Controllerから帰還・巡回用のWalk Stateを取得する。
    /// </summary>
    private void InitializeWalkAnimation()
    {
        animator = visualRoot.GetComponent<Animator>();
        if (animator == null)
        {
            animator = visualRoot.GetComponentInChildren<Animator>(true);
        }

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemySearchStrategy)}: VisualRoot以下に" +
                "AnimatorとAnimator Controllerを設定してください。" +
                "Walkモーションを再生せず、巡回処理は継続します。",
                enemyObject);
            return;
        }

        if (string.IsNullOrWhiteSpace(walkAnimationStateName))
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemySearchStrategy)}: " +
                "Walk用Animator State名が空です。",
                enemyObject);
            return;
        }

        int stateHash = Animator.StringToHash(walkAnimationStateName);
        if (!animator.HasState(ANIMATION_BASE_LAYER_INDEX, stateHash))
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemySearchStrategy)}: " +
                $"AnimatorにState '{walkAnimationStateName}' が見つかりません。" +
                "Animator ControllerとState名を確認してください。",
                enemyObject);
            return;
        }

        walkAnimationStateHash = stateHash;
    }

    /// <summary>
    /// 巡回移動中にWalkを維持する。再生が停止した原因を隠さないよう、
    /// State遷移や再生位置の停滞はログで確認できるようにする。
    /// </summary>
    private void EnsureWalkAnimation()
    {
        if (!CanPlayWalkAnimation())
        {
            return;
        }

        // 準備モーションではAnimator全体の速度を変更する。
        // Searchでは停止速度を引き継がず、移動中のWalkを再開させる。
        if (Mathf.Abs(animator.speed) <= ANIMATION_SPEED_EPSILON)
        {
            if (!hasRestoredStoppedAnimatorSpeed)
            {
                LogWalkAnimationSnapshot(
                    "Animator.speedが0付近のため、移動中のWalk再生速度を1に復旧");
                hasRestoredStoppedAnimatorSpeed = true;
            }

            animator.speed = 1f;
            ResetWalkProgressSample();
        }

        if (animator.IsInTransition(ANIMATION_BASE_LAYER_INDEX))
        {
            AnimatorStateInfo nextState =
                animator.GetNextAnimatorStateInfo(ANIMATION_BASE_LAYER_INDEX);

            if (nextState.fullPathHash == walkAnimationStateHash)
            {
                // 遷移中の再生時刻は進まない場合があるため、ここでは停滞を判定しない。
                ResetWalkProgressSample();
                return;
            }
        }
        else
        {
            AnimatorStateInfo currentState =
                animator.GetCurrentAnimatorStateInfo(ANIMATION_BASE_LAYER_INDEX);

            if (currentState.fullPathHash == walkAnimationStateHash &&
                (currentState.loop || currentState.normalizedTime < 1f))
            {
                MonitorWalkProgress(currentState);
                return;
            }
        }

        // 遷移先がWalk以外、または非ループWalkが終了した場合だけ再生を指示する。
        PlayWalkAnimationFromStart();
    }

    /// <summary>
    /// 帰還完了・Walk以外への遷移など、再生開始を指示する必要がある場合だけ使用する。
    /// </summary>
    private void PlayWalkAnimationFromStart()
    {
        if (!CanPlayWalkAnimation())
        {
            return;
        }

        TrackWalkReplayCount();
        ResetWalkProgressSample();
        animator.Play(walkAnimationStateHash, ANIMATION_BASE_LAYER_INDEX, 0f);
    }

    private bool CanPlayWalkAnimation()
    {
        return walkAnimationStateHash != 0 &&
               animator != null &&
               animator.isActiveAndEnabled;
    }

    /// <summary>
    /// WalkのStateに留まったままnormalizedTimeが進まないケースを検出する。
    /// ループStateにいるだけでは正常再生中とは判断できないための診断処理。
    /// </summary>
    private void MonitorWalkProgress(AnimatorStateInfo stateInfo)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        float currentTime = Time.realtimeSinceStartup;

        if (!hasWalkProgressSample)
        {
            lastWalkNormalizedTime = stateInfo.normalizedTime;
            lastWalkProgressTime = currentTime;
            hasWalkProgressSample = true;
            return;
        }

        if (Mathf.Abs(stateInfo.normalizedTime - lastWalkNormalizedTime) >
            WALK_PROGRESS_EPSILON)
        {
            lastWalkNormalizedTime = stateInfo.normalizedTime;
            lastWalkProgressTime = currentTime;
            hasReportedWalkStall = false;
            return;
        }

        if (!hasReportedWalkStall &&
            currentTime - lastWalkProgressTime >= WALK_STALL_WARNING_SECONDS)
        {
            hasReportedWalkStall = true;
            LogWalkAnimationSnapshot(
                "Walk State中なのにnormalizedTimeが0.75秒以上進んでいない");
        }
#endif
    }

    private void ResetWalkProgressSample()
    {
        hasWalkProgressSample = false;
        hasReportedWalkStall = false;
    }

    /// <summary>
    /// Walkを繰り返し指定しなければ維持できない場合、
    /// Animator Controllerの意図しない遷移を疑えるようにする。
    /// </summary>
    private void TrackWalkReplayCount()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        float currentTime = Time.realtimeSinceStartup;
        if (currentTime - walkReplayWindowStartTime > WALK_REPLAY_WINDOW_SECONDS)
        {
            walkReplayWindowStartTime = currentTime;
            walkReplayCount = 0;
            hasReportedRepeatedWalkReplay = false;
        }

        walkReplayCount++;
        if (!hasReportedRepeatedWalkReplay &&
            walkReplayCount >= WALK_REPLAY_WARNING_COUNT)
        {
            hasReportedRepeatedWalkReplay = true;
            LogWalkAnimationSnapshot(
                "Walkを1秒間に12回以上再指示している。Controller遷移の上書きを疑う");
        }
#endif
    }

    /// <summary>
    /// 停止原因を絞るため、State・速度・再生Clip・カリング状態をまとめて記録する。
    /// </summary>
    private void LogWalkAnimationSnapshot(string reason)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (animator == null)
        {
            Debug.LogWarning($"[ChargeEnemyWalkDebug] {reason}: Animatorが見つかりません。", enemyObject);
            return;
        }

        if (!animator.isActiveAndEnabled)
        {
            Debug.LogWarning($"[ChargeEnemyWalkDebug] {reason}: Animatorが無効です。", enemyObject);
            return;
        }

        AnimatorStateInfo currentState =
            animator.GetCurrentAnimatorStateInfo(ANIMATION_BASE_LAYER_INDEX);
        bool isTransitioning =
            animator.IsInTransition(ANIMATION_BASE_LAYER_INDEX);
        int nextStateHash = isTransitioning
            ? animator.GetNextAnimatorStateInfo(ANIMATION_BASE_LAYER_INDEX).fullPathHash
            : 0;

        AnimatorClipInfo[] clips =
            animator.GetCurrentAnimatorClipInfo(ANIMATION_BASE_LAYER_INDEX);
        string clipName = clips.Length > 0 && clips[0].clip != null
            ? clips[0].clip.name
            : "(なし)";

        Debug.LogWarning(
            $"[ChargeEnemyWalkDebug] {reason}. " +
            $"Animator={animator.name}, Controller={animator.runtimeAnimatorController?.name ?? "null"}, " +
            $"WalkState={walkAnimationStateName}, " +
            $"CurrentStateHash={currentState.fullPathHash}, " +
            $"WalkHash={walkAnimationStateHash}, NextStateHash={nextStateHash}, " +
            $"normalizedTime={currentState.normalizedTime:F3}, " +
            $"loop={currentState.loop}, stateSpeed={currentState.speed:F3}, " +
            $"animatorSpeed={animator.speed:F3}, " +
            $"transition={isTransitioning}, clip={clipName}, " +
            $"updateMode={animator.updateMode}, cullingMode={animator.cullingMode}",
            enemyObject);
#endif
    }

    /// <summary>
    /// RigidbodyのY方向速度を維持しながら、
    /// X方向を指定速度へ補正する。
    /// </summary>
    private void SetHorizontalVelocity(
        float targetVelocityX)
    {
        AlignVisualWithHorizontalDirection(
            targetVelocityX);

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
        if (!playerReference.TryGetPlayerTransform(
                out Transform playerTransform))
        {
            playerDetectionSensor.ClearDebugRay();
            return false;
        }

        Vector3 searchOrigin =
            GetSearchOrigin();

        Vector3 playerPosition =
            playerTransform.position;

        Vector3 lineOfSightEndPosition =
            new Vector3(
                playerPosition.x,
                searchOrigin.y,
                searchOrigin.z);

        return playerDetectionSensor.CanDetectPlayer(
            searchOrigin,
            playerPosition,
            detectionRadius,
            lineOfSightEndPosition,
            playerColliderEndsLineOfSight: false);
    }

    /// <summary>
    /// 索敵距離判定と障害物判定Rayで共通使用するEnemy側の基準位置を取得する。
    /// </summary>
    private Vector3 GetSearchOrigin()
    {
        return enemyCollider.bounds.center;
    }
}
