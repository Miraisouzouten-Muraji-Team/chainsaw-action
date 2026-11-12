using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// 責務:
/// ・配置時の位置を巡回基準として保持し、物理的に帰還できないと確認した場合だけ地面上で更新する。
/// ・着地イベントや単発のRay判定に依存せず、実際の帰還移動の進捗を確認する。
/// ・左右の移動可能距離を比較し、PatrolOffsetを上限とする巡回区間を決める。
/// ・巡回端または通行可能な地面の終端で停止し、VisualRootだけを指定時間かけて180度反転する。
/// ・巡回中に進行方向を塞ぐ壁面へ衝突した場合は反転する。
/// ・通行可能な斜面に沿って移動し、穴・大きな段差・許容角を超える坂へは進入しない。
/// ・XY平面上でプレイヤーとの距離を判定する。
/// ・EnemyとPlayerのX位置まで水平Rayを飛ばし、区間内の障害物を判定する。
/// ・初期位置への帰還中も索敵を継続し、Player発見時は通常のAlert遷移へ通知する。
/// ・帰還中を含む索敵中にPlayerへ接触した際の攻撃情報を送信する。

[Serializable]
public sealed class ChargeEnemySearchStrategy :
    IEnemySearchStrategy,
    IEnemyDebugRayProvider,
    IEnemyCollisionStayReceiver
{
    private const float ARRIVAL_TOLERANCE = 0.05f;
    private const float DIRECTION_EPSILON = 0.001f;
    private const float GROUND_NORMAL_TRANSITION_ANGLE = 2f;
    private const float WALL_NORMAL_X_THRESHOLD = 0.7f;
    private const int ANIMATION_BASE_LAYER_INDEX = 0;
    private const float ANIMATION_SPEED_EPSILON = 0.0001f;
    private const float WALK_STALL_WARNING_SECONDS = 0.75f;
    private const float WALK_PROGRESS_EPSILON = 0.0001f;
    private const float WALK_REPLAY_WINDOW_SECONDS = 1f;
    private const int WALK_REPLAY_WARNING_COUNT = 12;
    private const float RETURN_STALL_CONFIRM_SECONDS = 0.8f;
    private const float RETURN_PROGRESS_SAMPLE_DISTANCE = 0.06f;
    private const float MOVEMENT_DIAGNOSTIC_STALL_SECONDS = 0.25f;
    private const float MOVEMENT_DIAGNOSTIC_LOG_INTERVAL = 1f;
    private const float MOVEMENT_COMMAND_LOG_INTERVAL = 0.5f;
    private const float MOVEMENT_DIAGNOSTIC_MINIMUM_SPEED = 0.1f;
    private const float MOVEMENT_DIAGNOSTIC_PROGRESS_RATIO = 0.1f;
    private const float CONTACT_DIAGNOSTIC_MAX_AGE = 0.25f;
    private const float GROUND_STEP_CONTACT_MAX_AGE = 0.15f;
    private const float GROUND_STEP_RETRY_INTERVAL = 0.15f;
    private const float MIN_GROUND_STEP_RISE = 0.025f;
    private const float GROUND_STEP_SKIN = 0.005f;

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
    private bool hasPausedWalkAnimation;
    private bool isPhysicalMovementStalled;
    private bool hasPendingInitialGroundRecovery;
    private Collider recentBlockingStageCollider;
    private float recentBlockingStageContactTime = float.NegativeInfinity;
    private float lastGroundStepTime = float.NegativeInfinity;

    private ChargeEnemyGroundChecker groundChecker;

    // 巡回移動が失敗した理由を、直後の折り返しログに渡すために保持する。
    private string lastMovementFailureReason = string.Empty;

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

    // 停滞だけでは帰還不能にせず、下段との高低差・壁接触・通行不可の地形で裏付ける。
    private float returnProgressSampleX;
    private float returnStallElapsedTime;
    private bool hasStageWallContactDuringReturn;
    private float blockedGroundPathElapsedTime;
    private float stageWallContactX;

    private float turnElapsedTime;

    // 移動指示が物理結果に変換されない停滞だけを診断する。
    // 移動判定や速度制御には使用しない。
    private bool hasPendingMovementDiagnostic;
    private Vector3 diagnosticCommandPosition;
    private Vector3 diagnosticPreCommandVelocity;
    private Vector3 diagnosticTargetVelocity;
    private Vector3 diagnosticMovementDirection;
    private Vector3 diagnosticSupportNormal;
    private Vector3 diagnosticForwardNormal;
    private float diagnosticStallElapsedTime;
    private float lastMovementStallLogTime = float.NegativeInfinity;
    private bool hasLoggedMovementNormal;
    private Vector3 lastLoggedMovementNormal;
    private float lastMovementCommandLogTime = float.NegativeInfinity;

    private bool hasRecentContactDiagnostic;
    private int diagnosticContactFixedFrame;
    private float diagnosticContactTime;
    private string diagnosticContactColliderName = string.Empty;
    private string diagnosticContactMaterialName = string.Empty;
    private Vector3 diagnosticContactPoint;
    private Vector3 diagnosticContactNormal;
    private Vector3 diagnosticRelativeVelocity;
    private Vector3 diagnosticCollisionImpulse;
    private int diagnosticContactCount;
    private float diagnosticContactBlockingDot = float.PositiveInfinity;

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
                enemyCollider,
                allowMinorStageConnections: true);

        InitializeWalkAnimation();
        ResetWalkProgressSample();

        // Awake時点の初期めり込みを先に補正し、巡回原点が補正前の高さにならないようにする。
        // Rigidbodyの姿勢が物理世界へ反映される次回FixedUpdateまで、地面経路は再判定しない。
        if (groundChecker.TryGetGroundPenetrationRecovery(out float initialLift))
        {
            enemyRigidbody.position += Vector3.up * initialLift;
            RemoveDownwardVelocity();
            hasPendingInitialGroundRecovery = true;
            LogSlopeDiagnostic("InitialGroundRecovery", $"upwardDistance={initialLift:F4}");
        }

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
        ResetMovementDiagnostics();
        recentBlockingStageCollider = null;

        if (hasPendingInitialGroundRecovery)
        {
            isAwaitingGroundForPatrol = true;
            currentMoveDirectionSign = 0f;
            return;
        }

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
            LogSlopeDiagnostic("GroundWaitStarted",
                $"開始時に非接地: {groundChecker.LastSupportFailureReason}", true);
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
            LogSlopeDiagnostic("PatrolDistanceSelection",
                $"巡回距離が閾値以下: patrolOffset={patrolOffset:F3}");
            return;
        }

        float rightDistance = groundChecker.MeasureSafeHorizontalDistance(1f, maximumDistance);
        string rightStopReason = groundChecker.LastPathScanFailureReason;
        float leftDistance = groundChecker.MeasureSafeHorizontalDistance(-1f, maximumDistance);
        string leftStopReason = groundChecker.LastPathScanFailureReason;
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
        LogSlopeDiagnostic("PatrolDistanceSelection",
            $"requested={patrolOffset:F3}, right={rightDistance:F3}, " +
            $"rightStop=({rightStopReason}), left={leftDistance:F3}, " +
            $"leftStop=({leftStopReason}), selected={activePatrolOffset:F3}, " +
            $"minDistance={minimumPatrolDistance:F3}");
    }

    public bool UpdateSearch(
        out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;

        if (!isSearchActive)
        {
            return false;
        }

        UpdateMovementDiagnostics();

        if (hasPendingInitialGroundRecovery)
        {
            hasPendingInitialGroundRecovery = false;
            DecideReturnOrPatrol();
        }

        // 軽微なめり込みは物理更新前に最小限の上方向だけへ分離する。
        // 修正したRigidbody位置が反映されるまでは、旧Collider姿勢を使って経路検査しない。
        if (groundChecker.TryGetGroundPenetrationRecovery(out float overlapLift))
        {
            enemyRigidbody.position += Vector3.up * overlapLift;
            RemoveDownwardVelocity();
            recentBlockingStageCollider = null;
            ResetMovementDiagnostics();
            LogSlopeDiagnostic("GroundOverlapRecovered", $"upwardDistance={overlapLift:F4}");
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

        CaptureCollisionDiagnostic(collision);
        CaptureBlockingStageContact(collision);

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
    /// 移動指示中の物理停止を切り分けるため、
    /// 継続接触のColliderと最も進行を塞ぐ法線を保存する。
    /// この情報は診断ログにのみ使用する。
    /// </summary>
    public void HandleCollisionStay(Collision collision)
    {
        if (isSearchActive)
        {
            CaptureCollisionDiagnostic(collision);
            CaptureBlockingStageContact(collision);
        }
    }

    private void CaptureBlockingStageContact(Collision collision)
    {
        if (collision == null || collision.collider == null ||
            !IsStageCollider(collision.collider) ||
            Mathf.Abs(currentMoveDirectionSign) <= DIRECTION_EPSILON)
        {
            return;
        }

        if (EnemyBlockingContactEvaluator.TryGetBlockingContact(
                collision, Vector3.right * currentMoveDirectionSign,
                WALL_NORMAL_X_THRESHOLD, out _))
        {
            recentBlockingStageCollider = collision.collider;
            recentBlockingStageContactTime = Time.fixedTime;
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
        if (Vector3.Angle(blockingContact.normal, Vector3.up) <=
            enemyData.MaxGroundSlopeAngle)
        {
            return false;
        }

        // 回転BoxColliderの坂入口では、上面とは別に通行不可角度の端面Contactが
        // 発生する。同じColliderの通行可能な斜面上面が直前方へ連続する場合だけ、
        // その端面ContactによるBeginTurnを抑止する。
        if (groundChecker.IsTraversableSlopeEntrance(
                collision.collider,
                currentMoveDirectionSign))
        {
            return false;
        }

        LogSlopeDiagnostic("BlockingCollision",
            $"collider={collision.collider.name}, point={blockingContact.point}, " +
            $"normal={blockingContact.normal}, " +
            $"angle={Vector3.Angle(blockingContact.normal, Vector3.up):F2}, " +
            $"returning={isReturningToPatrolStart}", true);
        return true;
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
        ResetMovementDiagnostics();
        recentBlockingStageCollider = null;

        StopHorizontalMovement();
        ResumeWalkAnimationIfPaused();
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
    /// 帰還中は通行可能な地面上を物理移動し、停滞に加えて下段への落下・Stage壁接触・
    /// 通行不可の地形が確認されたときだけ原点を変更する。地面を離れたら再接地を待つ。
    /// </summary>
    private void UpdateReturnToPatrolStart()
    {
        if (!groundChecker.IsStandingOnStageGround())
        {
            LogSlopeDiagnostic("GroundLostDuringReturn",
                groundChecker.LastSupportFailureReason, true);
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
            HandleUnreachablePatrolOrigin("Stage壁・通行不可の地形・下段からの帰還中に進捗がない");
            return;
        }

        float expectedMovementDistance = Mathf.Min(
            moveSpeed * fixedDeltaTime,
            Mathf.Abs(deltaX));

        // 帰還可能な進行先だけを使用する。通行不可なら停止して既存の進捗監視を継続する。
        if (!TryApplyGroundMovement(
                currentMoveDirectionSign,
                moveSpeed,
                expectedMovementDistance,
                Mathf.Abs(deltaX)))
        {
            // 一時的なRayの不発では帰還不能とせず、通行不可が継続した時間を測る。
            if (blockedGroundPathElapsedTime <= 0f)
            {
                LogSlopeDiagnostic("ReturnMovementBlocked", lastMovementFailureReason, true);
            }
            blockedGroundPathElapsedTime += fixedDeltaTime;
            StopHorizontalMovement();
            return;
        }

        blockedGroundPathElapsedTime = 0f;
        EnsureWalkAnimation();
    }

    private void ResetReturnProgressTracking()
    {
        returnProgressSampleX = enemyRigidbody != null ? enemyRigidbody.position.x : 0f;
        returnStallElapsedTime = 0f;
        hasStageWallContactDuringReturn = false;
        blockedGroundPathElapsedTime = 0f;
        stageWallContactX = 0f;
    }

    /// <summary>
    /// 物理ステップをまたいでも帰還方向へ進めず、地形による障害が確認されたときだけ
    /// 帰還不能を確定する。Playerなどによる一時的な停滞では旧原点を保持する。
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

        // 一時的なPlayerによる停滞だけでは原点更新しない。
        // 通行不能な地形が0.8秒続く場合は、衝突前でも帰還不能を確定する。
        return isBelowOriginalPlatform || hasStageWallContactDuringReturn ||
               blockedGroundPathElapsedTime >= RETURN_STALL_CONFIRM_SECONDS;
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
            LogSlopeDiagnostic("PatrolTurnReachedTarget",
                $"targetX={targetPositionX:F3}, remainingX={targetPositionX - enemyRigidbody.position.x:F3}");
            BeginTurn();
            return;
        }

        // 空中では地面に沿うY速度を与えず、再接地まで既存の待機処理へ戻す。
        if (!groundChecker.IsStandingOnStageGround())
        {
            LogSlopeDiagnostic("GroundLostDuringPatrol",
                groundChecker.LastSupportFailureReason, true);
            isAwaitingGroundForPatrol = true;
            StopHorizontalMovement();
            return;
        }

        float expectedMovementDistance =
            enemyData.PatrolMoveSpeed *
            Time.fixedDeltaTime;

        if (!TryApplyGroundMovement(
                currentMoveDirectionSign,
                enemyData.PatrolMoveSpeed,
                expectedMovementDistance,
                float.PositiveInfinity))
        {
            LogSlopeDiagnostic("PatrolTurnMovementRejected", lastMovementFailureReason, true);
            BeginTurn();
            return;
        }

        if (enemyData.PatrolMoveSpeed > DIRECTION_EPSILON)
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

        if (isPhysicalMovementStalled)
        {
            PauseWalkAnimationWhenStopped();
            return;
        }

        ResumeWalkAnimationIfPaused();

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
    /// 接地している間だけ、通行可能な地面の接線方向へ巡回・帰還速度を与える。
    /// 戻り先が近い場合はX座標を超えないよう、接線上の速度を制限する。
    /// </summary>
    private bool TryApplyGroundMovement(
        float directionSign,
        float moveSpeed,
        float expectedMovementDistance,
        float remainingHorizontalDistance)
    {
        lastMovementFailureReason = string.Empty;
        if (!groundChecker.TryGetCurrentGroundNormal(
                out Vector3 supportingGroundNormal))
        {
            lastMovementFailureReason =
                $"現在の支持面法線を取得できない: {groundChecker.LastSupportFailureReason}";
            return false;
        }

        if (!groundChecker.TryGetTraversableGroundNormal(
                directionSign,
                expectedMovementDistance,
                out Vector3 forwardGroundNormal))
        {
            lastMovementFailureReason =
                $"進行方向の地面・壁検査に失敗: {groundChecker.LastFailureReason}";
            return false;
        }

        // Stage接触が前方の通行可能な地面の「端面」に由来する場合だけ、
        // 位置を少し上げて物理的な引っ掛かりを解く。一般の壁には適用しない。
        if (TryStepOverGroundConnection(directionSign))
        {
            return true;
        }

        Vector3 forwardNormal = new Vector3(
            forwardGroundNormal.x, forwardGroundNormal.y, 0f).normalized;
        Vector3 supportNormal = new Vector3(
            supportingGroundNormal.x, supportingGroundNormal.y, 0f).normalized;

        // 上り入口は前方の斜面に沿って登る必要がある。
        // 下り入口で「まだ平地に乗っている胴体」を先読みした斜面方向へ押し下げると、
        // BoxColliderの端面へ食い込むため、実際の支持面が切り替わるまで下降を急がない。
        float forwardRise = -forwardNormal.x * directionSign;
        float supportRise = -supportNormal.x * directionSign;
        bool isSteeperUphillAhead = forwardRise > DIRECTION_EPSILON &&
            forwardRise > supportRise + DIRECTION_EPSILON;
        bool isDownhillTransition = forwardRise < supportRise - DIRECTION_EPSILON;
        Vector3 movementNormal = isSteeperUphillAhead ||
            (!isDownhillTransition &&
             Vector3.Angle(forwardNormal, supportNormal) <= GROUND_NORMAL_TRANSITION_ANGLE)
                ? forwardNormal : supportNormal;
        Vector3 movementDirection = GetGroundTangent(directionSign, movementNormal);
        if (movementDirection.sqrMagnitude <= DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            lastMovementFailureReason = $"斜面接線の移動方向がほぼ0: normal={movementNormal}";
            return false;
        }

        float targetSpeed = moveSpeed;
        if (!float.IsPositiveInfinity(remainingHorizontalDistance))
        {
            float horizontalSpeedRatio = Mathf.Abs(movementDirection.x);
            if (horizontalSpeedRatio <= DIRECTION_EPSILON ||
                Time.fixedDeltaTime <= DIRECTION_EPSILON)
            {
                lastMovementFailureReason =
                    $"水平速度比またはFixedDeltaTimeが不正: xRatio={horizontalSpeedRatio:F4}, " +
                    $"fixedDeltaTime={Time.fixedDeltaTime:F4}";
                return false;
            }

            targetSpeed = Mathf.Min(
                moveSpeed,
                remainingHorizontalDistance / (horizontalSpeedRatio * Time.fixedDeltaTime));
        }

        // 接地中の残留下降速度を毎ステップ持ち越すと、下り坂の継ぎ目へ押し込む。
        // 支持面の接線速度だけを指定し、重力と接触解決は物理エンジンへ委ねる。
        Vector3 currentVelocity = enemyRigidbody.linearVelocity;
        Vector3 targetVelocity = movementDirection * targetSpeed;
        Vector3 velocityChange = targetVelocity - currentVelocity;
        velocityChange.z = 0f;

        if (velocityChange.sqrMagnitude > DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            enemyRigidbody.AddForce(velocityChange, ForceMode.VelocityChange);
        }

        RecordMovementCommandDiagnostic(
            currentVelocity,
            targetVelocity,
            movementDirection,
            supportNormal,
            forwardNormal,
            movementNormal,
            targetSpeed);

        if (targetSpeed > DIRECTION_EPSILON)
        {
            AlignVisualWithHorizontalDirection(directionSign);
        }
        return true;
    }

    private bool TryStepOverGroundConnection(float directionSign)
    {
        Collider blockingCollider = recentBlockingStageCollider;
        if (blockingCollider == null ||
            Time.fixedTime - recentBlockingStageContactTime > GROUND_STEP_CONTACT_MAX_AGE ||
            Time.fixedTime - lastGroundStepTime < GROUND_STEP_RETRY_INTERVAL ||
            blockingCollider != groundChecker.LastTraversableGroundCollider ||
            !groundChecker.IsTraversableSlopeEntrance(blockingCollider, directionSign))
        {
            return false;
        }

        float minimumRise = Mathf.Max(
            MIN_GROUND_STEP_RISE,
            groundChecker.LastUpwardConnectionRise + GROUND_STEP_SKIN);
        float maximumRise = Mathf.Min(
            enemyData.MaxGroundPenetrationRecovery,
            enemyData.GroundConnectionHeightTolerance + MIN_GROUND_STEP_RISE);
        if (maximumRise < minimumRise)
        {
            return false;
        }

        // 端面の高さがRayから計算した接続誤差より大きい場合もあるため、
        // 上限内の候補を小さい順に試し、最初に安全と確認できた量だけ持ち上げる。
        float stepRise = 0f;
        for (float candidateRise = minimumRise;
             candidateRise <= maximumRise + DIRECTION_EPSILON;
             candidateRise += 0.015f)
        {
            float testedRise = Mathf.Min(candidateRise, maximumRise);
            if (groundChecker.CanLiftBodySafely(testedRise))
            {
                stepRise = testedRise;
                break;
            }
        }
        if (stepRise <= DIRECTION_EPSILON &&
            groundChecker.CanLiftBodySafely(maximumRise))
        {
            stepRise = maximumRise;
        }
        if (stepRise <= DIRECTION_EPSILON)
        {
            return false;
        }

        enemyRigidbody.position += Vector3.up * stepRise;
        RemoveDownwardVelocity();
        recentBlockingStageCollider = null;
        lastGroundStepTime = Time.fixedTime;
        ResetMovementDiagnostics();
        LogSlopeDiagnostic("GroundConnectionStep", $"collider={blockingCollider.name}, rise={stepRise:F4}");
        return true;
    }

    private void RemoveDownwardVelocity()
    {
        Vector3 velocity = enemyRigidbody.linearVelocity;
        if (velocity.y < 0f)
        {
            velocity.y = 0f;
            enemyRigidbody.linearVelocity = velocity;
        }
    }

    private void PauseWalkAnimationWhenStopped()
    {
        if (!isSearchActive || hasPausedWalkAnimation || !CanPlayWalkAnimation())
        {
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(ANIMATION_BASE_LAYER_INDEX);
        if (state.fullPathHash != walkAnimationStateHash)
        {
            return;
        }

        // 姿勢が動かないのにWalkだけ動く状態を避ける。Search終了時には復帰させる。
        animator.speed = 0f;
        hasPausedWalkAnimation = true;
        ResetWalkProgressSample();
    }

    private void ResumeWalkAnimationIfPaused()
    {
        if (hasPausedWalkAnimation && animator != null)
        {
            animator.speed = 1f;
            hasPausedWalkAnimation = false;
            ResetWalkProgressSample();
        }
    }

    // 診断ログはEditor/Development Buildのみ。移動・折り返し判定には影響させない。
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogSlopeDiagnostic(string eventName, string details, bool isWarning = false)
    {
        string message =
            $"[ChargeEnemySlopeDebug] event={eventName}, enemy={enemyObject.name}, " +
            $"position={enemyRigidbody.position}, velocity={enemyRigidbody.linearVelocity}, " +
            $"direction={currentMoveDirectionSign:F0}, startX={patrolStartPosition.x:F3}, " +
            $"activeOffset={activePatrolOffset:F3}, " +
            $"toEnd={isMovingToPatrolEnd}, returning={isReturningToPatrolStart}. {details}";

        if (isWarning)
        {
            Debug.LogWarning(message, enemyObject);
        }
        else
        {
            Debug.Log(message, enemyObject);
        }
    }

    private static Vector3 GetGroundTangent(float directionSign, Vector3 groundNormal)
    {
        return Vector3.ProjectOnPlane(
            Vector3.right * directionSign,
            groundNormal).normalized;
    }

    /// <summary>
    /// 接地中は斜面に沿う移動成分だけを止め、地面法線方向の速度を維持する。
    /// 空中ではX速度だけを止め、重力によるY速度を変えない。
    /// </summary>
    private void StopHorizontalMovement()
    {
        if (enemyRigidbody == null)
        {
            return;
        }

        hasPendingMovementDiagnostic = false;
        diagnosticStallElapsedTime = 0f;
        isPhysicalMovementStalled = false;
        PauseWalkAnimationWhenStopped();

        Vector3 currentVelocity = enemyRigidbody.linearVelocity;
        if (groundChecker != null &&
            groundChecker.TryGetCurrentGroundNormal(out Vector3 supportingGroundNormal))
        {
            Vector3 planarNormal = new Vector3(
                supportingGroundNormal.x, supportingGroundNormal.y, 0f).normalized;
            Vector3 groundTangent = GetGroundTangent(1f, planarNormal);
            float tangentSpeed = Vector3.Dot(currentVelocity, groundTangent);
            if (Mathf.Abs(tangentSpeed) > DIRECTION_EPSILON)
            {
                enemyRigidbody.AddForce(
                    -groundTangent * tangentSpeed,
                    ForceMode.VelocityChange);
            }
            return;
        }

        if (Mathf.Abs(currentVelocity.x) > DIRECTION_EPSILON)
        {
            enemyRigidbody.AddForce(
                new Vector3(-currentVelocity.x, 0f, 0f),
                ForceMode.VelocityChange);
        }
    }

    private void RecordMovementCommandDiagnostic(
        Vector3 preCommandVelocity,
        Vector3 targetVelocity,
        Vector3 movementDirection,
        Vector3 supportNormal,
        Vector3 forwardNormal,
        Vector3 movementNormal,
        float targetSpeed)
    {
        diagnosticCommandPosition = enemyRigidbody.position;
        diagnosticPreCommandVelocity = preCommandVelocity;
        diagnosticTargetVelocity = targetVelocity;
        diagnosticMovementDirection = movementDirection;
        diagnosticSupportNormal = supportNormal;
        diagnosticForwardNormal = forwardNormal;
        hasPendingMovementDiagnostic = targetSpeed > MOVEMENT_DIAGNOSTIC_MINIMUM_SPEED;

        bool hasMeaningfulNormalChange = !hasLoggedMovementNormal ||
            Vector3.Angle(lastLoggedMovementNormal, movementNormal) >
            GROUND_NORMAL_TRANSITION_ANGLE;
        if (hasMeaningfulNormalChange &&
            Time.fixedTime - lastMovementCommandLogTime >=
            MOVEMENT_COMMAND_LOG_INTERVAL)
        {
            hasLoggedMovementNormal = true;
            lastLoggedMovementNormal = movementNormal;
            lastMovementCommandLogTime = Time.fixedTime;
            LogSlopeDiagnostic(
                "MovementCommandIssued",
                $"targetVelocity={targetVelocity}, preVelocity={preCommandVelocity}, " +
                $"movementDirection={movementDirection}, supportNormal={supportNormal}, " +
                $"forwardNormal={forwardNormal}, movementNormal={movementNormal}");
        }
    }

    private void UpdateMovementDiagnostics()
    {
        if (!hasPendingMovementDiagnostic || enemyRigidbody == null)
        {
            return;
        }

        Vector3 positionDelta =
            enemyRigidbody.position - diagnosticCommandPosition;
        float commandedForwardSpeed = Mathf.Max(
            0f,
            Vector3.Dot(diagnosticTargetVelocity, diagnosticMovementDirection));
        float actualForwardDistance = Mathf.Max(
            0f,
            Vector3.Dot(positionDelta, diagnosticMovementDirection));
        float minimumExpectedDistance = commandedForwardSpeed *
            Time.fixedDeltaTime * MOVEMENT_DIAGNOSTIC_PROGRESS_RATIO;

        if (commandedForwardSpeed <= MOVEMENT_DIAGNOSTIC_MINIMUM_SPEED ||
            actualForwardDistance >= minimumExpectedDistance)
        {
            diagnosticStallElapsedTime = 0f;
            isPhysicalMovementStalled = false;
            return;
        }

        diagnosticStallElapsedTime += Time.fixedDeltaTime;
        if (diagnosticStallElapsedTime >= MOVEMENT_DIAGNOSTIC_STALL_SECONDS)
        {
            isPhysicalMovementStalled = true;
            PauseWalkAnimationWhenStopped();
        }
        if (diagnosticStallElapsedTime < MOVEMENT_DIAGNOSTIC_STALL_SECONDS ||
            Time.fixedTime - lastMovementStallLogTime <
            MOVEMENT_DIAGNOSTIC_LOG_INTERVAL)
        {
            return;
        }

        lastMovementStallLogTime = Time.fixedTime;
        string contactDetails = HasFreshContactDiagnostic()
            ? $"contactCollider={diagnosticContactColliderName}, " +
              $"contactMaterial={diagnosticContactMaterialName}, " +
              $"contactPoint={diagnosticContactPoint}, " +
              $"contactNormal={diagnosticContactNormal}, " +
              $"contactCount={diagnosticContactCount}, " +
              $"relativeVelocity={diagnosticRelativeVelocity}, " +
              $"collisionImpulse={diagnosticCollisionImpulse}, " +
              $"contactFixedFrame={diagnosticContactFixedFrame}"
            : "contact=none-or-callback-not-forwarded";

        LogSlopeDiagnostic(
            "MovementCommandStalled",
            $"commandIssued=true, commandPosition={diagnosticCommandPosition}, " +
            $"currentPosition={enemyRigidbody.position}, positionDelta={positionDelta}, " +
            $"targetVelocity={diagnosticTargetVelocity}, " +
            $"preCommandVelocity={diagnosticPreCommandVelocity}, " +
            $"actualVelocity={enemyRigidbody.linearVelocity}, " +
            $"movementDirection={diagnosticMovementDirection}, " +
            $"actualForwardDistance={actualForwardDistance:F4}, " +
            $"minimumExpectedDistance={minimumExpectedDistance:F4}, " +
            $"stallSeconds={diagnosticStallElapsedTime:F2}, " +
            $"supportNormal={diagnosticSupportNormal}, " +
            $"forwardNormal={diagnosticForwardNormal}, {contactDetails}, " +
            $"constraints={enemyRigidbody.constraints}, " +
            $"collisionDetection={enemyRigidbody.collisionDetectionMode}, " +
            $"interpolation={enemyRigidbody.interpolation}",
            true);
    }

    private void CaptureCollisionDiagnostic(Collision collision)
    {
        if (collision == null ||
            collision.collider == null ||
            collision.contactCount <= 0 ||
            Mathf.Abs(currentMoveDirectionSign) <= DIRECTION_EPSILON)
        {
            return;
        }

        Vector3 moveDirection =
            Vector3.right * currentMoveDirectionSign;
        ContactPoint selectedContact = collision.GetContact(0);
        float selectedBlockingDot =
            Vector3.Dot(selectedContact.normal, moveDirection);

        for (int i = 1; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            float blockingDot =
                Vector3.Dot(contact.normal, moveDirection);
            if (blockingDot < selectedBlockingDot)
            {
                selectedContact = contact;
                selectedBlockingDot = blockingDot;
            }
        }

        // 同じ物理ステップで床と坂の両方から通知された場合は、
        // 最も進行方向に逆らう法線を残す。
        if (hasRecentContactDiagnostic &&
            Mathf.Approximately(diagnosticContactTime, Time.fixedTime) &&
            selectedBlockingDot >= diagnosticContactBlockingDot)
        {
            return;
        }

        hasRecentContactDiagnostic = true;
        diagnosticContactFixedFrame = Time.frameCount;
        diagnosticContactTime = Time.fixedTime;
        diagnosticContactColliderName = collision.collider.name;
        diagnosticContactMaterialName = collision.collider.sharedMaterial != null
            ? collision.collider.sharedMaterial.name
            : "none";
        diagnosticContactPoint = selectedContact.point;
        diagnosticContactNormal = selectedContact.normal;
        diagnosticRelativeVelocity = collision.relativeVelocity;
        diagnosticCollisionImpulse = collision.impulse;
        diagnosticContactCount = collision.contactCount;
        diagnosticContactBlockingDot = selectedBlockingDot;
    }

    private bool HasFreshContactDiagnostic()
    {
        return hasRecentContactDiagnostic &&
               Time.fixedTime - diagnosticContactTime <=
               CONTACT_DIAGNOSTIC_MAX_AGE;
    }

    private void ResetMovementDiagnostics()
    {
        hasPendingMovementDiagnostic = false;
        diagnosticStallElapsedTime = 0f;
        isPhysicalMovementStalled = false;
        lastMovementStallLogTime = float.NegativeInfinity;
        hasLoggedMovementNormal = false;
        lastMovementCommandLogTime = float.NegativeInfinity;
        hasRecentContactDiagnostic = false;
        diagnosticContactFixedFrame = 0;
        diagnosticContactTime = 0f;
        diagnosticContactColliderName = string.Empty;
        diagnosticContactMaterialName = string.Empty;
        diagnosticContactBlockingDot = float.PositiveInfinity;
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
