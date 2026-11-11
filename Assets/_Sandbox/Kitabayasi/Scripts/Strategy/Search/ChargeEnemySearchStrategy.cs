using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// 責務:
/// ・ゲーム開始時の配置位置を固定した巡回開始位置として保持し、Search復帰時はそこへ戻ってから巡回する。
/// ・帰還を安全に続行できない場合は、そのSearch中の巡回を停止してPlayer索敵へ戻る。
/// ・符号付きPatrolOffsetをワールドX方向の巡回端として使用し、往復する。
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

    [Header("個体設定")]
    [Tooltip("このChargeEnemyがPlayerを検知する半径。ChargeEnemyDataのDetectionRadiusは使用しない。")]
    [SerializeField]
    [Min(0f)]
    private float detectionRadius;

    [Tooltip("このChargeEnemyの巡回開始地点から折り返し地点までのワールドX方向の符号付きオフセット。正数は+X、負数は-X。ChargeEnemyDataのPatrolDistanceは使用しない。")]
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

    private float currentMoveDirectionSign;

    private bool isMovingToPatrolEnd;
    private bool isTurning;
    private bool isSearchActive;
    private bool isReturningToPatrolStart;
    private bool isReturnBlocked;

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
        isReturnBlocked = false;

        float deltaToPatrolStart =
            patrolStartPosition.x -
            enemyRigidbody.position.x;

        isReturningToPatrolStart =
            Mathf.Abs(deltaToPatrolStart) >
            ARRIVAL_TOLERANCE;

        currentMoveDirectionSign =
            isReturningToPatrolStart
                ? Mathf.Sign(deltaToPatrolStart)
                : GetPatrolEndDirectionSign();

        Debug.Log(
            $"[ChargeEnemyReturnDebug] Search開始。 " +
            $"patrolStartX={patrolStartPosition.x:F3}, " +
            $"currentX={enemyRigidbody.position.x:F3}, " +
            $"deltaX={deltaToPatrolStart:F3}, " +
            $"isReturning={isReturningToPatrolStart}, " +
            $"returnDirection={currentMoveDirectionSign:F0}",
            enemyObject);

        AlignVisualWithCurrentMoveDirection();

        // 攻撃後はAttack Stateのまま残り得るため、Searchへ戻った時点で歩行を再生する。
        // 以降の移動中は、別Stateへの切り替わりや非ループ終端時だけ再指定する。
        if (enemyData.PatrolMoveSpeed > DIRECTION_EPSILON &&
            (isReturningToPatrolStart ||
             Mathf.Abs(patrolOffset) > DIRECTION_EPSILON))
        {
            EnsureWalkAnimation();
        }

        turnElapsedTime = 0f;

        playerReference.TryGetPlayerTransform(
            out _);
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

        if (isReturningToPatrolStart)
        {
            UpdateReturnToPatrolStart();
            return false;
        }

        if (isReturnBlocked)
        {
            StopHorizontalMovement();
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
            // ChargeEnemyは経路探索を行わないため、帰還方向を壁に塞がれた場合は
            // 壁へ押し続けず、そのSearch中の帰還を停止する。
            if (!isReturnBlocked &&
                IsWallBlockingCurrentMoveDirection(
                    collision))
            {
                AbortReturnToPatrolStart();

                Debug.LogWarning(
                    $"[ChargeEnemyReturnDebug] {nameof(ChargeEnemySearchStrategy)}: " +
                    "初期巡回位置への帰還経路を壁に塞がれたため、" +
                    "このSearch中の帰還と巡回を中断します。 " +
                    $"collider={collision.collider.name}, layer={collision.collider.gameObject.layer}, " +
                    $"currentX={enemyRigidbody.position.x:F3}, patrolStartX={patrolStartPosition.x:F3}",
                    enemyObject);
            }

            return;
        }

        // 帰還に失敗したSearch中は通常巡回を再開しない。
        // Playerとの接触処理だけは上で継続する。
        if (isReturnBlocked)
        {
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

        return EnemyBlockingContactEvaluator.TryGetBlockingContact(
            collision,
            moveDirection,
            WALL_NORMAL_X_THRESHOLD,
            out _);
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
        isReturnBlocked = false;

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
    /// Unity配置時に保存した巡回開始X位置へ直接戻る。
    /// </summary>
    /// <remarks>
    /// ChargeEnemyは地上移動Enemyのため、Yは重力・接地へ任せてXだけを戻す。
    /// Droneのような迂回経路探索は担当しない。
    /// </remarks>
    private void UpdateReturnToPatrolStart()
    {
        if (isReturnBlocked)
        {
            // 通常はAbortReturnToPatrolStartでisReturningToPatrolStartも解除される。
            // 状態不整合が起きた場合でも索敵不能へ固定されないよう、ここでも帰還を終了する。
            isReturningToPatrolStart = false;
            StopHorizontalMovement();
            return;
        }

        float deltaX =
            patrolStartPosition.x -
            enemyRigidbody.position.x;

        if (Mathf.Abs(deltaX) <=
            ARRIVAL_TOLERANCE)
        {
            CompleteReturnToPatrolStart();
            return;
        }

        float moveSpeed =
            enemyData.PatrolMoveSpeed;

        if (moveSpeed <=
            DIRECTION_EPSILON)
        {
            AbortReturnToPatrolStart();

            Debug.LogWarning(
                $"[ChargeEnemyReturnDebug] {nameof(ChargeEnemySearchStrategy)}: " +
                "PatrolMoveSpeedが0以下のため初期巡回位置へ帰還できません。 " +
                $"PatrolMoveSpeed={moveSpeed:F3}, currentX={enemyRigidbody.position.x:F3}, " +
                $"patrolStartX={patrolStartPosition.x:F3}",
                enemyObject);

            return;
        }

        float fixedDeltaTime =
            Time.fixedDeltaTime;

        if (fixedDeltaTime <=
            DIRECTION_EPSILON)
        {
            StopHorizontalMovement();
            return;
        }

        currentMoveDirectionSign =
            Mathf.Sign(deltaX);

        float expectedMovementDistance =
            moveSpeed * fixedDeltaTime;

        if (!groundChecker.CanMoveInDirection(
                currentMoveDirectionSign,
                expectedMovementDistance))
        {
            AbortReturnToPatrolStart();

            Debug.LogWarning(
                $"[ChargeEnemyReturnDebug] {nameof(ChargeEnemySearchStrategy)}: " +
                "初期巡回位置への帰還方向に安全な地面がないため、" +
                "このSearch中の帰還と巡回を中断します。 " +
                $"reason={groundChecker.LastFailureReason}, " +
                $"direction={currentMoveDirectionSign:F0}, expectedMove={expectedMovementDistance:F3}, " +
                $"currentX={enemyRigidbody.position.x:F3}, patrolStartX={patrolStartPosition.x:F3}",
                enemyObject);

            return;
        }

        float targetVelocityX =
            Mathf.Abs(deltaX) <=
            expectedMovementDistance
                ? deltaX / fixedDeltaTime
                : currentMoveDirectionSign * moveSpeed;

        SetHorizontalVelocity(
            targetVelocityX);
        EnsureWalkAnimation();
    }

    /// <summary>
    /// 初期巡回位置への安全な帰還を続行できない場合に、
    /// 帰還処理を終了してPlayer索敵へ戻す。
    /// </summary>
    /// <remarks>
    /// 帰還できていない位置から固定巡回範囲をそのまま再開すると、
    /// 本来の巡回区間外から巡回地点へ向かう可能性がある。
    /// そのため、このSearch中は通常巡回も停止し、次のSearch開始時に帰還を再試行する。
    /// </remarks>
    private void AbortReturnToPatrolStart()
    {
        StopHorizontalMovement();

        isReturningToPatrolStart = false;
        isReturnBlocked = true;
        isTurning = false;
    }

    private void CompleteReturnToPatrolStart()
    {
        StopHorizontalMovement();

        isReturningToPatrolStart = false;
        isReturnBlocked = false;
        isMovingToPatrolEnd = true;
        isTurning = false;

        currentMoveDirectionSign =
            GetPatrolEndDirectionSign();

        Debug.Log(
            $"[ChargeEnemyReturnDebug] 初期巡回位置への帰還完了。 " +
            $"currentX={enemyRigidbody.position.x:F3}, patrolStartX={patrolStartPosition.x:F3}",
            enemyObject);

        AlignVisualWithCurrentMoveDirection();

        // このログが出なければ、帰還完了の分岐に到達していない。
        LogWalkAnimationSnapshot("帰還完了・巡回へ切り替え");

        // 帰還終了後は同じSearch State内で巡回を開始する。
        // WalkのStateに留まったまま再生が終了している場合にも対応するため、
        // この切り替え時だけ再生位置を先頭に戻す。
        if (enemyData.PatrolMoveSpeed > DIRECTION_EPSILON &&
            Mathf.Abs(patrolOffset) > DIRECTION_EPSILON)
        {
            PlayWalkAnimationFromStart();
        }
    }

    /// <summary>
    /// 巡回移動または反転処理を更新する。
    /// </summary>
    private void UpdatePatrol()
    {
        if (Mathf.Abs(patrolOffset) <=
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
               patrolOffset;
    }

    /// <summary>
    /// ワールドX方向の符号付きPatrolOffsetから、
    /// 巡回端へ向かうX方向の符号を取得する。
    /// </summary>
    private float GetPatrolEndDirectionSign()
    {
        if (Mathf.Abs(patrolOffset) <=
            DIRECTION_EPSILON)
        {
            return 0f;
        }

        return Mathf.Sign(patrolOffset);
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
