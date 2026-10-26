using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

/// <summary>
/// 旧型ドローンエネミーのAttack State中に、必要ならA*でPlayerへ直接突進できる地点まで移動し、
/// 初回突進前の待機、ホーミング突進、急ブレーキ、停止、後退を繰り返す攻撃Strategy。
/// 突進中に障害物へ阻まれた場合や進行不能が続いた場合は、
/// 現在の突進を失敗として回復シーケンスへ移行する。
/// Search / Attackを通した実移動はDroneEnemyPlanarMotorへ委譲し、
/// 障害物への新しい侵入防止と、既存の重なりからの復帰を共通化する。
/// </summary>
/// <remarks>
/// ゲームプレイ上の移動はXY平面でRigidbodyを使用する。
/// State遷移とPlayerのHP変更は担当しない。
/// </remarks>
[Serializable]
[MovedFrom(false, sourceClassName: "DroneEnemyAttackStrategy")]
public sealed class OldDroneEnemyAttackStrategy : IEnemyAttackStrategy
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";
    private const float DIRECTION_EPSILON = 0.001f;
    private const float SPEED_EPSILON = 0.01f;
    private const float BLOCKING_NORMAL_THRESHOLD = 0.5f;
    private const float FULL_CIRCLE_DEGREES = 360f;

    private OldDroneEnemyData enemyData;
    private GameObject enemyObject;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private BoxCollider enemyBoxCollider;
    private DroneEnemyPlanarMotor planarMotor;
    private DroneEnemyPathNavigator pathNavigator;

    private EnemyPlayerReference playerReference;
    private BoxCollider playerTargetCollider;

    // 内部フェーズ用の新しいenumを増やさず、現在フェーズの更新処理を保持する。
    private Action updateCurrentPhase;

    private Vector2 lockedPlayerPosition;
    private Vector2 attackPassStartPosition;
    private Vector2 baseAttackDirection;
    private Vector2 currentAttackDirection;
    private Vector2 lastAttackDirection = Vector2.right;
    private Vector2 lastHomingPosition;
    private Vector2 retreatStartPosition;
    private Vector2 retreatDirection;
    private float initialAttackDirectionSign = 1f;
    private float baseAttackAngle;
    private float currentAttackAngle;
    private float lockProgressDistance;
    private float preparationElapsedTime;
    private float brakePauseElapsedTime;
    private float brakingSpeed;
    private float homingStallElapsedTime;
    private float retreatElapsedTime;
    private float expectedRetreatDuration;
    private float effectiveRetreatEaseDuration;
    private bool isAttackActive;
    private bool isHomingActive;
    private bool isRetreatActive;
    private bool hasDamagedPlayerThisPass;
    private bool hasWarnedMissingPlayerReceiver;
    private bool hasWarnedMissingPlayer;
    private bool hasWarnedMissingPlayerTargetCollider;
    private bool navigationCompletesWithInitialPreparation;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData =
            enemyData as OldDroneEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(OldDroneEnemyAttackStrategy)}には" +
                $"{nameof(OldDroneEnemyData)}が必要です。");

        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));

        this.visualRoot = visualRoot
            ? visualRoot
            : throw new InvalidOperationException(
                $"{nameof(OldDroneEnemyAttackStrategy)}を使用するEnemyには" +
                "VisualRootが必要です。");

        enemyRigidbody =
            enemyObject.GetComponent<Rigidbody>();

        if (enemyRigidbody == null)
        {
            throw new InvalidOperationException(
                $"{nameof(OldDroneEnemyAttackStrategy)}を使用するEnemyには" +
                $"{nameof(Rigidbody)}が必要です。");
        }

        if (enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(OldDroneEnemyAttackStrategy)}は" +
                "非KinematicのRigidbodyを使用する前提です。");
        }

        enemyBoxCollider =
            enemyObject.GetComponent<BoxCollider>();

        if (enemyBoxCollider == null ||
            !enemyBoxCollider.enabled)
        {
            throw new InvalidOperationException(
                $"{nameof(OldDroneEnemyAttackStrategy)}を使用するEnemy本体には" +
                $"有効な{nameof(BoxCollider)}が必要です。");
        }

        playerReference =
            new EnemyPlayerReference();

        planarMotor =
            new DroneEnemyPlanarMotor(
                enemyRigidbody,
                enemyBoxCollider,
                this.enemyData.NavigationObstacleMask,
                IsBlockingMovementCollider);

        pathNavigator =
            new DroneEnemyPathNavigator(
                enemyRigidbody,
                enemyBoxCollider,
                planarMotor,
                this.enemyData.NavigationObstacleMask,
                this.enemyData.PathSafetyMargin,
                this.enemyData.PathSearchPadding,
                this.enemyData.MaximumExpandedPathNodes,
                this.enemyData.WaypointArrivalTolerance,
                this.enemyData.NavigationMoveSpeed,
                this.enemyData.NavigationStallMinimumMoveSpeed,
                this.enemyData.NavigationStallDetectionDuration);

        playerReference.TryGetPlayerTransform(
            out _);
    }

    public void BeginAttack(float initialAttackDirectionSign)
    {
        ResetRuntimeState();

        isAttackActive = true;
        this.initialAttackDirectionSign =
            NormalizeDirectionSign(initialAttackDirectionSign);

        if (!TryBeginPlayerApproach(
                useInitialPreparation: true))
        {
            StopPlanarMovement();
            SetPhase(UpdateWaitingForInitialPlayerLock);
        }
    }

    public bool UpdateAttack()
    {
        if (isAttackActive)
        {
            updateCurrentPhase?.Invoke();
        }

        // 旧型DroneはAttack State内で攻撃ループを継続するため、
        // StateMachineへ攻撃サイクル完了を要求しない。
        return false;
    }

    public void HandleCollisionEnter(Collision collision)
    {
        HandleCollision(collision);
    }

    public void HandleCollisionStay(Collision collision)
    {
        HandleCollision(collision);
    }

    public void EndAttack()
    {
        isAttackActive = false;
        isHomingActive = false;
        updateCurrentPhase = null;

        StopPlanarMovement();
        ResetRuntimeState();
    }

    private void UpdateWaitingForInitialPlayerLock()
    {
        StopPlanarMovement();

        TryBeginPlayerApproach(
            useInitialPreparation: true);
    }

    /// <summary>
    /// 現在Playerへ直接突進できるなら従来攻撃へ進み、
    /// 壁に阻まれている場合だけA*経路追従を開始する。
    /// </summary>
    private bool TryBeginPlayerApproach(
        bool useInitialPreparation)
    {
        if (!TryGetCurrentPlayerNavigationTarget(
                out Vector3 playerTarget))
        {
            return false;
        }

        if (pathNavigator.IsDirectTraversalAvailable(
                playerTarget))
        {
            if (!TryLockCurrentPlayerPosition())
            {
                return false;
            }

            if (useInitialPreparation)
            {
                BeginInitialPreparation();
            }
            else
            {
                BeginHomingAttackPass();
            }

            return true;
        }

        navigationCompletesWithInitialPreparation =
            useInitialPreparation;

        BeginNavigationToPlayer(
            playerTarget);

        return true;
    }

    private void BeginNavigationToPlayer(
        Vector3 playerTarget)
    {
        StopPlanarMovement();

        isHomingActive = false;

        SetPhase(
            UpdateNavigationToPlayer);

        bool hasPath =
            pathNavigator.TryBeginPathToDirectTraversalTarget(
                playerTarget,
                enemyData.AttackRepathInterval);

        if (hasPath &&
            !pathNavigator.HasPath)
        {
            CompleteNavigationToAttack();
        }
    }

    private void UpdateNavigationToPlayer()
    {
        if (!TryGetCurrentPlayerNavigationTarget(
                out Vector3 playerTarget))
        {
            StopPlanarMovement();
            return;
        }

        bool canBeginDirectAttack =
            pathNavigator.UpdatePathToDirectTraversalTarget(
                playerTarget,
                enemyData.AttackRepathDistance,
                enemyData.AttackRepathInterval);

        AlignVisualWithPlanarDirection(
            pathNavigator.RequestedMoveDirection);

        if (!canBeginDirectAttack)
        {
            return;
        }

        CompleteNavigationToAttack();
    }

    private void CompleteNavigationToAttack()
    {
        StopPlanarMovement();

        pathNavigator.Reset();

        if (!TryLockCurrentPlayerPosition())
        {
            SetPhase(
                navigationCompletesWithInitialPreparation
                    ? UpdateWaitingForInitialPlayerLock
                    : UpdateWaitingForPlayerRelock);

            return;
        }

        if (navigationCompletesWithInitialPreparation)
        {
            BeginInitialPreparation();
        }
        else
        {
            BeginHomingAttackPass();
        }
    }

    private bool TryGetCurrentPlayerNavigationTarget(
        out Vector3 playerTarget)
    {
        if (!TryGetCurrentPlayerTargetPosition(
                out Vector3 currentTargetPosition))
        {
            playerTarget = default;
            return false;
        }

        playerTarget =
            new Vector3(
                currentTargetPosition.x,
                currentTargetPosition.y,
                enemyRigidbody.position.z);

        return true;
    }

    private void BeginInitialPreparation()
    {
        StopPlanarMovement();

        preparationElapsedTime = 0f;
        SetPhase(UpdateInitialPreparation);

        if (enemyData.InitialPreparationDuration <=
            DIRECTION_EPSILON)
        {
            CompleteInitialPreparation();
        }
    }

    private void UpdateInitialPreparation()
    {
        StopPlanarMovement();

        preparationElapsedTime +=
            Time.fixedDeltaTime;

        if (preparationElapsedTime >=
            enemyData.InitialPreparationDuration)
        {
            CompleteInitialPreparation();
        }
    }

    private void CompleteInitialPreparation()
    {
        // 初回突進前の待機中にもPlayerは移動できるため、
        // 突進開始直前に現在位置への直接経路をもう一度確認する。
        if (!TryBeginPlayerApproach(
                useInitialPreparation: false))
        {
            SetPhase(
                UpdateWaitingForPlayerRelock);
        }
    }

    /// <summary>
    /// このPassで固定したロック位置から基準攻撃方向を一度だけ確定し、
    /// 以降のHomingStrength計算ではこの基準方向を変更しない。
    /// </summary>
    private void BeginHomingAttackPass()
    {
        Vector2 currentPosition =
            GetCurrentPlanarPosition();

        Vector2 toLockedPosition =
            lockedPlayerPosition -
            currentPosition;

        if (toLockedPosition.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            toLockedPosition =
                Vector2.right *
                initialAttackDirectionSign;
        }

        attackPassStartPosition =
            currentPosition;

        lastHomingPosition =
            currentPosition;

        homingStallElapsedTime = 0f;

        baseAttackDirection =
            toLockedPosition.normalized;

        currentAttackDirection =
            baseAttackDirection;

        lastAttackDirection =
            baseAttackDirection;

        baseAttackAngle =
            DirectionToAngle(
                baseAttackDirection);

        currentAttackAngle =
            baseAttackAngle;

        lockProgressDistance =
            Mathf.Max(
                0f,
                Vector2.Dot(
                    lockedPlayerPosition -
                    attackPassStartPosition,
                    baseAttackDirection) -
                enemyData.HomingEndThreshold);

        hasDamagedPlayerThisPass = false;
        hasWarnedMissingPlayerReceiver = false;

        enemyRigidbody.WakeUp();

        SetPhase(
            UpdateHoming,
            homingActive: true);

        if (HasReachedHomingEndLine())
        {
            BeginBraking();
            return;
        }

        UpdateHoming();
    }

    private void UpdateHoming()
    {
        if (HasReachedHomingEndLine())
        {
            BeginBraking();
            return;
        }

        if (HasHomingAttackStalled())
        {
            BeginFailedAttackRecovery();
            return;
        }

        Vector2 targetAttackDirection =
            CalculateHomingTargetDirection();

        float targetAttackAngle =
            DirectionToAngle(
                targetAttackDirection);

        float maximumTurnThisStep =
            enemyData.MaximumTurnSpeed *
            Time.fixedDeltaTime;

        currentAttackAngle =
            Mathf.MoveTowardsAngle(
                currentAttackAngle,
                targetAttackAngle,
                maximumTurnThisStep);

        currentAttackDirection =
            AngleToDirection(
                currentAttackAngle);

        lastAttackDirection =
            currentAttackDirection;

        bool canApplyFullAttackMovement =
            TrySetPlanarVelocity(
                currentAttackDirection *
                enemyData.AttackMoveSpeed);

        if (!canApplyFullAttackMovement)
        {
            // Motorが突進先の障害物を事前検知した場合は、
            // Collision発生を待たず今回の突進を失敗として回復へ移る。
            BeginFailedAttackRecovery();
        }
    }

    /// <summary>
    /// ホーミング突進中に実移動がほぼ発生していない状態が
    /// 設定時間続いたかを判定する。
    /// </summary>
    /// <remarks>
    /// Collisionだけでは拾えない引っ掛かりへのフェイルセーフとして使用する。
    /// 急ブレーキ・停止・後退中はisHomingActiveがfalseのため判定対象にならない。
    /// </remarks>
    private bool HasHomingAttackStalled()
    {
        float detectionDuration =
            enemyData.AttackStallDetectionDuration;

        float minimumMoveSpeed =
            enemyData.AttackStallMinimumMoveSpeed;

        Vector2 currentPosition =
            GetCurrentPlanarPosition();

        if (detectionDuration <=
                DIRECTION_EPSILON ||
            minimumMoveSpeed <=
                DIRECTION_EPSILON)
        {
            lastHomingPosition =
                currentPosition;

            homingStallElapsedTime = 0f;
            return false;
        }

        float movedDistance =
            Vector2.Distance(
                currentPosition,
                lastHomingPosition);

        lastHomingPosition =
            currentPosition;

        float minimumMoveDistance =
            minimumMoveSpeed *
            Time.fixedDeltaTime;

        if (movedDistance >
            minimumMoveDistance)
        {
            homingStallElapsedTime = 0f;
            return false;
        }

        homingStallElapsedTime +=
            Time.fixedDeltaTime;

        return homingStallElapsedTime >=
               detectionDuration;
    }

    /// <summary>
    /// HomingStrengthは「どこを目指すか」だけを決める。
    /// 実際にそこへ向きを変える速さはMaximumTurnSpeedで別に制限する。
    /// </summary>
    private Vector2 CalculateHomingTargetDirection()
    {
        if (!TryGetCurrentPlayerTargetPosition(
                out Vector3 playerTargetPosition))
        {
            return baseAttackDirection;
        }

        Vector2 toCurrentPlayer =
            new Vector2(
                playerTargetPosition.x,
                playerTargetPosition.y) -
            GetCurrentPlanarPosition();

        if (toCurrentPlayer.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return baseAttackDirection;
        }

        float playerAngle =
            DirectionToAngle(
                toCurrentPlayer.normalized);

        float homingStrength01 =
            Mathf.Clamp01(
                enemyData.HomingStrength /
                100f);

        float targetAngle =
            baseAttackAngle +
            Mathf.DeltaAngle(
                baseAttackAngle,
                playerAngle) *
            homingStrength01;

        return AngleToDirection(
            targetAngle);
    }

    /// <summary>
    /// ロック地点そのものとの距離ではなく、Attack開始時の基準方向上で
    /// ロック地点付近の判定ラインまで進んだかを判定する。
    /// </summary>
    private bool HasReachedHomingEndLine()
    {
        Vector2 traveled =
            GetCurrentPlanarPosition() -
            attackPassStartPosition;

        float progress =
            Vector2.Dot(
                traveled,
                baseAttackDirection);

        return progress >=
            lockProgressDistance;
    }

    private void BeginBraking()
    {
        isHomingActive = false;

        if (lastAttackDirection.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            lastAttackDirection =
                currentAttackDirection.sqrMagnitude >
                DIRECTION_EPSILON * DIRECTION_EPSILON
                    ? currentAttackDirection.normalized
                    : baseAttackDirection;
        }

        brakingSpeed =
            Mathf.Max(
                0f,
                Vector2.Dot(
                    GetCurrentPlanarVelocity(),
                    lastAttackDirection.normalized));

        SetPhase(
            UpdateBraking);

        if (brakingSpeed <=
                SPEED_EPSILON ||
            enemyData.BrakingDeceleration <=
                DIRECTION_EPSILON)
        {
            CompleteBraking();
        }
    }

    private void UpdateBraking()
    {
        brakingSpeed =
            Mathf.MoveTowards(
                brakingSpeed,
                0f,
                enemyData.BrakingDeceleration *
                Time.fixedDeltaTime);

        if (brakingSpeed <=
            SPEED_EPSILON)
        {
            CompleteBraking();
            return;
        }

        SetPlanarVelocity(
            lastAttackDirection.normalized *
            brakingSpeed);
    }

    private void CompleteBraking()
    {
        brakingSpeed = 0f;

        StopPlanarMovement();

        brakePauseElapsedTime = 0f;

        SetPhase(
            UpdateBrakePause);

        if (enemyData.BrakePauseDuration <=
            DIRECTION_EPSILON)
        {
            BeginRetreatPreparation();
        }
    }

    private void UpdateBrakePause()
    {
        StopPlanarMovement();

        brakePauseElapsedTime +=
            Time.fixedDeltaTime;

        if (brakePauseElapsedTime >=
            enemyData.BrakePauseDuration)
        {
            BeginRetreatPreparation();
        }
    }

    private void BeginRetreatPreparation()
    {
        StopPlanarMovement();

        retreatStartPosition =
            GetCurrentPlanarPosition();

        retreatDirection =
            lastAttackDirection.sqrMagnitude >
            DIRECTION_EPSILON * DIRECTION_EPSILON
                ? -lastAttackDirection.normalized
                : -baseAttackDirection;

        retreatElapsedTime = 0f;
        isRetreatActive = true;

        if (enemyData.RetreatSpeed >
            DIRECTION_EPSILON)
        {
            float constantSpeedDuration =
                enemyData.RetreatDistance /
                enemyData.RetreatSpeed;

            effectiveRetreatEaseDuration =
                Mathf.Clamp(
                    enemyData.RetreatEaseDuration,
                    0f,
                    constantSpeedDuration);

            // EaseIn / EaseOut区間では平均速度が最大速度より低くなるため、
            // 設定距離を維持できるよう、その分だけ理論移動時間を延ばす。
            expectedRetreatDuration =
                constantSpeedDuration +
                effectiveRetreatEaseDuration;
        }
        else
        {
            effectiveRetreatEaseDuration = 0f;
            expectedRetreatDuration = 0f;
        }

        SetPhase(
            UpdateRetreatPreparation);

        if (enemyData.RetreatDistance <=
                DIRECTION_EPSILON ||
            enemyData.RetreatSpeed <=
                DIRECTION_EPSILON)
        {
            CompleteRetreatPreparation();
        }
    }

    private void UpdateRetreatPreparation()
    {
        retreatElapsedTime +=
            Time.fixedDeltaTime;

        float retreatedDistance =
            Vector2.Dot(
                GetCurrentPlanarPosition() -
                retreatStartPosition,
                retreatDirection);

        if (retreatedDistance >=
            enemyData.RetreatDistance)
        {
            CompleteRetreatPreparation();
            return;
        }

        // 壁などで後退量を満たせない場合にAttackが永久停止しないよう、
        // イージング込みの理論移動時間で後退フェーズを打ち切る。
        if (expectedRetreatDuration >
                DIRECTION_EPSILON &&
            retreatElapsedTime >=
                expectedRetreatDuration)
        {
            CompleteRetreatPreparation();
            return;
        }

        float currentRetreatSpeed =
            CalculateCurrentRetreatSpeed();

        bool canApplyFullRetreatMovement =
            TrySetPlanarVelocity(
                retreatDirection *
                currentRetreatSpeed);

        if (!canApplyFullRetreatMovement)
        {
            // 後退先に障害物がある場合はMotorが侵入直前で移動量を制限する。
            // そのまま後退フェーズを続けて壁へ押し付けない。
            CompleteRetreatPreparation();
        }
    }

    /// <summary>
    /// 後退開始と終了だけをSmoothStepで滑らかにし、
    /// 中間区間ではRetreatSpeedを維持する。
    /// </summary>
    private float CalculateCurrentRetreatSpeed()
    {
        float maximumSpeed =
            enemyData.RetreatSpeed;

        if (effectiveRetreatEaseDuration <=
                DIRECTION_EPSILON ||
            expectedRetreatDuration <=
                DIRECTION_EPSILON)
        {
            return maximumSpeed;
        }

        if (retreatElapsedTime <
            effectiveRetreatEaseDuration)
        {
            float easeInTime =
                retreatElapsedTime /
                effectiveRetreatEaseDuration;

            return Mathf.SmoothStep(
                0f,
                maximumSpeed,
                easeInTime);
        }

        float easeOutStartTime =
            expectedRetreatDuration -
            effectiveRetreatEaseDuration;

        if (retreatElapsedTime >
            easeOutStartTime)
        {
            float easeOutTime =
                (retreatElapsedTime -
                 easeOutStartTime) /
                effectiveRetreatEaseDuration;

            return Mathf.SmoothStep(
                maximumSpeed,
                0f,
                easeOutTime);
        }

        return maximumSpeed;
    }

    private void CompleteRetreatPreparation()
    {
        isRetreatActive = false;

        StopPlanarMovement();

        if (!TryBeginPlayerApproach(
                useInitialPreparation: false))
        {
            SetPhase(
                UpdateWaitingForPlayerRelock);
        }
    }

    private void UpdateWaitingForPlayerRelock()
    {
        StopPlanarMovement();

        TryBeginPlayerApproach(
            useInitialPreparation: false);
    }

    private void HandleCollision(
        Collision collision)
    {
        if (!isAttackActive ||
            collision == null ||
            collision.collider == null)
        {
            return;
        }

        if (isRetreatActive)
        {
            // 事前Sweepと実接触の両方で、同じNavigationObstacleMaskを
            // 「後退を塞ぐStage障害物」の基準として使用する。
            if (IsNavigationObstacleCollider(
                    collision.collider) &&
                TryGetBlockingContact(
                    collision,
                    retreatDirection,
                    out _))
            {
                CompleteRetreatPreparation();
            }

            return;
        }

        if (!isHomingActive)
        {
            return;
        }

        if (playerReference.IsPlayerCollider(
                collision.collider))
        {
            SendPlayerDamageOncePerPass();

            // Playerへ命中した場合もロック地点まで進み続けず、
            // 今回のホーミング区間を終えてブレーキへ移る。
            BeginBraking();
            return;
        }

        if (!TryGetBlockingContact(
                collision,
                out _))
        {
            return;
        }

        // Player以外のColliderが現在の突進方向を塞いだ場合は、
        // Attack State自体は維持しつつ今回の突進だけを失敗扱いにする。
        BeginFailedAttackRecovery();
    }

    /// <summary>
    /// 現在の突進方向に対して十分逆向きの接触法線がある場合だけ、
    /// その接触を「進行を塞ぐ障害物」と判定する。
    /// </summary>
    private bool TryGetBlockingContact(
        Collision collision,
        out ContactPoint blockingContact)
    {
        Vector2 planarMoveDirection =
            currentAttackDirection;

        if (planarMoveDirection.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            planarMoveDirection =
                lastAttackDirection;
        }

        if (planarMoveDirection.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            planarMoveDirection =
                baseAttackDirection;
        }

        return TryGetBlockingContact(
            collision,
            planarMoveDirection,
            out blockingContact);
    }

    /// <summary>
    /// 指定したゲームプレイ平面上の移動方向に対して十分逆向きの接触法線がある場合だけ、
    /// その接触を「進行を塞ぐ障害物」と判定する。
    /// </summary>
    private bool TryGetBlockingContact(
        Collision collision,
        Vector2 planarMoveDirection,
        out ContactPoint blockingContact)
    {
        if (planarMoveDirection.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            blockingContact = default;
            return false;
        }

        Vector3 moveDirection =
            new Vector3(
                planarMoveDirection.x,
                planarMoveDirection.y,
                0f);

        return EnemyBlockingContactEvaluator.TryGetBlockingContact(
            collision,
            moveDirection,
            BLOCKING_NORMAL_THRESHOLD,
            out blockingContact);
    }

    /// <summary>
    /// 攻撃失敗時も通常の突進終了と同じブレーキ・停止・後退を経由し、
    /// 後退完了後に現在のPlayer位置を取り直して次の突進を開始する。
    /// </summary>
    private void BeginFailedAttackRecovery()
    {
        homingStallElapsedTime = 0f;
        BeginBraking();
    }

    private void SendPlayerDamageOncePerPass()
    {
        if (hasDamagedPlayerThisPass)
        {
            return;
        }

        hasDamagedPlayerThisPass = true;

        if (playerReference.TryGetAttackHitReceiver(
                requireActiveAndEnabled: true,
                out IAttackHitReceiver playerAttackHitReceiver,
                out _))
        {
            float damage =
                enemyData.AttackPower *
                (enemyData.AttackContactDamagePercent /
                 100f);

            playerAttackHitReceiver.ReceiveAttackHit(
                new EnemyAttackHitData(
                    enemyData,
                    damage));

            return;
        }

        if (!hasWarnedMissingPlayerReceiver)
        {
            Debug.LogWarning(
                $"{nameof(OldDroneEnemyAttackStrategy)}: " +
                "Playerに有効なIAttackHitReceiverの実装がありません。",
                enemyObject);

            hasWarnedMissingPlayerReceiver = true;
        }
    }

    private bool TryLockCurrentPlayerPosition()
    {
        if (!TryGetCurrentPlayerTargetPosition(
                out Vector3 targetWorldPosition))
        {
            return false;
        }

        lockedPlayerPosition =
            new Vector2(
                targetWorldPosition.x,
                targetWorldPosition.y);

        return true;
    }

    private void SetPlanarVelocity(
        Vector2 targetVelocity)
    {
        TrySetPlanarVelocity(
            targetVelocity);
    }

    private bool TrySetPlanarVelocity(
        Vector2 targetVelocity)
    {
        AlignVisualWithPlanarDirection(
            targetVelocity);

        return planarMotor.TrySetPlanarVelocity(
            targetVelocity);
    }

    private void AlignVisualWithPlanarDirection(
        Vector2 planarDirection)
    {
        if (planarDirection.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return;
        }

        Vector3 worldDirection =
            new Vector3(
                planarDirection.x,
                planarDirection.y,
                0f).normalized;

        visualRoot.rotation =
            Quaternion.LookRotation(
                worldDirection,
                Vector3.up);
    }

    private void StopPlanarMovement()
    {
        planarMotor?.StopPlanarMovement();
    }

    private Vector2 GetCurrentPlanarPosition()
    {
        Vector3 position =
            enemyRigidbody.position;

        return new Vector2(
            position.x,
            position.y);
    }

    private Vector2 GetCurrentPlanarVelocity()
    {
        Vector3 velocity =
            enemyRigidbody.linearVelocity;

        return new Vector2(
            velocity.x,
            velocity.y);
    }

    private bool IsBlockingMovementCollider(
        Collider targetCollider)
    {
        // 現在のNavigationObstacleMaskにはPlayer Colliderも含まれ得るため、
        // 実移動の侵入防止ではPlayerを障害物として扱わない。
        return !playerReference.IsPlayerCollider(
            targetCollider);
    }

    private bool IsNavigationObstacleCollider(
        Collider targetCollider)
    {
        if (targetCollider == null)
        {
            return false;
        }

        Transform targetTransform =
            targetCollider.transform;

        if (targetTransform == enemyObject.transform ||
            targetTransform.IsChildOf(
                enemyObject.transform))
        {
            return false;
        }

        int targetLayerMask =
            1 << targetCollider.gameObject.layer;

        return (enemyData.NavigationObstacleMask.value &
                targetLayerMask) != 0;
    }

    /// <summary>
    /// Enemyの経路探索・攻撃位置ロック・突進中追尾に使用するPlayer側の基準位置を取得する。
    /// Player RootはCollider所属判定とDamage Receiver探索に必要なため保持し、
    /// 狙う位置だけをPlayer Root直下のMid_ColliderのBounds中心へ統一する。
    /// </summary>
    private bool TryGetCurrentPlayerTargetPosition(
        out Vector3 targetWorldPosition)
    {
        if (!playerReference.TryGetPlayerTransform(
                out Transform playerTransform))
        {
            playerTargetCollider = null;
            targetWorldPosition = default;

            if (!hasWarnedMissingPlayer)
            {
                Debug.LogWarning(
                    $"{nameof(OldDroneEnemyAttackStrategy)}: " +
                    "Playerを取得できないため、攻撃Targetを取得できません。",
                    enemyObject);

                hasWarnedMissingPlayer = true;
            }

            return false;
        }

        hasWarnedMissingPlayer = false;

        if (playerTargetCollider == null)
        {
            Transform targetTransform =
                playerTransform.Find(
                    PLAYER_TARGET_COLLIDER_NAME);

            if (targetTransform != null)
            {
                playerTargetCollider =
                    targetTransform.GetComponent<BoxCollider>();
            }
        }

        if (playerTargetCollider == null ||
            !playerTargetCollider.enabled ||
            !playerTargetCollider.gameObject.activeInHierarchy)
        {
            targetWorldPosition = default;

            if (!hasWarnedMissingPlayerTargetCollider)
            {
                Debug.LogWarning(
                    $"{nameof(OldDroneEnemyAttackStrategy)}: " +
                    $"Player Root直下の{PLAYER_TARGET_COLLIDER_NAME}に" +
                    $"有効な{nameof(BoxCollider)}がないため、攻撃位置へ移動できません。",
                    enemyObject);

                hasWarnedMissingPlayerTargetCollider = true;
            }

            return false;
        }

        hasWarnedMissingPlayerTargetCollider = false;
        targetWorldPosition =
            playerTargetCollider.bounds.center;
        return true;
    }

    private void ResetRuntimeState()
    {
        updateCurrentPhase = null;
        isHomingActive = false;
        isRetreatActive = false;

        lockedPlayerPosition = Vector2.zero;
        attackPassStartPosition = Vector2.zero;
        baseAttackDirection = Vector2.zero;
        currentAttackDirection = Vector2.zero;
        lastAttackDirection = Vector2.right;
        lastHomingPosition = Vector2.zero;
        retreatStartPosition = Vector2.zero;
        retreatDirection = Vector2.zero;
        pathNavigator?.Reset();
        navigationCompletesWithInitialPreparation = false;

        baseAttackAngle = 0f;
        currentAttackAngle = 0f;
        lockProgressDistance = 0f;
        preparationElapsedTime = 0f;
        brakePauseElapsedTime = 0f;
        brakingSpeed = 0f;
        homingStallElapsedTime = 0f;
        retreatElapsedTime = 0f;
        expectedRetreatDuration = 0f;
        effectiveRetreatEaseDuration = 0f;

        hasDamagedPlayerThisPass = false;
        hasWarnedMissingPlayerReceiver = false;
        hasWarnedMissingPlayer = false;
        hasWarnedMissingPlayerTargetCollider = false;
    }

    private void SetPhase(
        Action phaseUpdate,
        bool homingActive = false)
    {
        updateCurrentPhase =
            phaseUpdate;

        isHomingActive =
            homingActive;
    }

    private static float NormalizeDirectionSign(
        float directionSign)
    {
        return directionSign < 0f
            ? -1f
            : 1f;
    }

    private static float DirectionToAngle(
        Vector2 direction)
    {
        float angle =
            Mathf.Atan2(
                direction.y,
                direction.x) *
            Mathf.Rad2Deg;

        return Mathf.Repeat(
            angle,
            FULL_CIRCLE_DEGREES);
    }

    private static Vector2 AngleToDirection(
        float angleDegrees)
    {
        float angleRadians =
            angleDegrees *
            Mathf.Deg2Rad;

        return new Vector2(
            Mathf.Cos(angleRadians),
            Mathf.Sin(angleRadians));
    }
}
