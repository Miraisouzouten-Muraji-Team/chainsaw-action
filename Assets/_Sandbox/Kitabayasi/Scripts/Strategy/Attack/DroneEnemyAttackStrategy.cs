using System;
using UnityEngine;

/// <summary>
/// ドローンエネミーのAttack State中に、予備動作、ホーミング突進、
/// 急ブレーキ、停止、後退を繰り返す攻撃Strategy。
/// 突進中に障害物へ阻まれた場合や進行不能が続いた場合は、
/// 現在の突進を失敗として回復シーケンスへ移行する。
/// </summary>
/// <remarks>
/// Rigidbody / Colliderを見た目の予備動作には使用せず、
/// ゲームプレイ上の移動はXY平面、横揺れはVisualRootのlocalPositionだけで行う。
/// State遷移とPlayerのHP変更は担当しない。
/// </remarks>
[Serializable]
public sealed class DroneEnemyAttackStrategy : IEnemyAttackStrategy
{
    private const string PLAYER_TAG = "Player";
    private const float DIRECTION_EPSILON = 0.001f;
    private const float SPEED_EPSILON = 0.01f;
    private const float BLOCKING_NORMAL_THRESHOLD = 0.5f;
    private const float FULL_CIRCLE_DEGREES = 360f;
    private const float TWO_PI = Mathf.PI * 2f;

    private DroneEnemyData enemyData;
    private GameObject enemyObject;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;

    private Transform playerTransform;
    private MonoBehaviour playerReceiverComponent;
    private IAttackHitReceiver playerAttackHitReceiver;

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

    private Vector3 preparationBaseLocalPosition;

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
    private bool isVisualPreparationActive;
    private bool hasDamagedPlayerThisPass;
    private bool hasWarnedMissingPlayerReceiver;
    private bool hasWarnedMissingPlayer;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData = enemyData as DroneEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(DroneEnemyAttackStrategy)}には{nameof(DroneEnemyData)}が必要です。");

        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));

        this.visualRoot = visualRoot
            ? visualRoot
            : throw new InvalidOperationException(
                $"{nameof(DroneEnemyAttackStrategy)}を使用するEnemyにはVisualRootが必要です。");

        enemyRigidbody = enemyObject.GetComponent<Rigidbody>();

        if (enemyRigidbody == null)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemyAttackStrategy)}を使用するEnemyには{nameof(Rigidbody)}が必要です。");
        }

        if (enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(DroneEnemyAttackStrategy)}は非KinematicのRigidbodyを使用する前提です。");
        }

        ResolvePlayerTransform();
    }

    public void BeginAttack(float initialAttackDirectionSign)
    {
        ResetRuntimeState();

        isAttackActive = true;
        this.initialAttackDirectionSign =
            NormalizeDirectionSign(initialAttackDirectionSign);

        if (!TryLockCurrentPlayerPosition())
        {
            StopPlanarMovement();
            SetPhase(UpdateWaitingForInitialPlayerLock);
            return;
        }

        // TODO:
        // 現在のSearch→Alert→Attackの契約では発見時のPlayer座標を渡していないため、
        // 初回のみAttack Stateへ入った瞬間のPlayer位置をロックしている。
        // 仕様どおりSearch発見瞬間に固定するには、発見座標をState間で引き継ぐ必要がある。
        BeginInitialPreparation();
    }

    public void UpdateAttack()
    {
        if (isAttackActive)
        {
            updateCurrentPhase?.Invoke();
        }
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

        RestoreVisualPreparationPosition();
        StopPlanarMovement();
        ResetRuntimeState();
    }

    private void UpdateWaitingForInitialPlayerLock()
    {
        StopPlanarMovement();

        if (!TryLockCurrentPlayerPosition())
        {
            return;
        }

        BeginInitialPreparation();
    }

    private void BeginInitialPreparation()
    {
        StopPlanarMovement();

        preparationElapsedTime = 0f;
        BeginVisualPreparation();
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

        UpdateVisualPreparation(
            preparationElapsedTime);

        if (preparationElapsedTime >=
            enemyData.InitialPreparationDuration)
        {
            CompleteInitialPreparation();
        }
    }

    private void CompleteInitialPreparation()
    {
        RestoreVisualPreparationPosition();
        BeginHomingAttackPass();
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

        SetPlanarVelocity(
            currentAttackDirection *
            enemyData.AttackMoveSpeed);
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
        ResolvePlayerTransform();

        if (playerTransform == null)
        {
            return baseAttackDirection;
        }

        Vector3 playerWorldPosition =
            playerTransform.position;

        Vector2 toCurrentPlayer =
            new Vector2(
                playerWorldPosition.x,
                playerWorldPosition.y) -
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

        RestoreVisualPreparationPosition();

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

        BeginVisualPreparation();

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

        UpdateVisualPreparation(
            retreatElapsedTime);

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

        SetPlanarVelocity(
            retreatDirection *
            CalculateCurrentRetreatSpeed());
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
        RestoreVisualPreparationPosition();
        StopPlanarMovement();

        if (!TryLockCurrentPlayerPosition())
        {
            SetPhase(
                UpdateWaitingForPlayerRelock);

            return;
        }

        BeginHomingAttackPass();
    }

    private void UpdateWaitingForPlayerRelock()
    {
        StopPlanarMovement();

        if (TryLockCurrentPlayerPosition())
        {
            BeginHomingAttackPass();
        }
    }

    private void HandleCollision(
        Collision collision)
    {
        if (!isAttackActive ||
            !isHomingActive ||
            collision == null ||
            collision.collider == null)
        {
            return;
        }

        ResolvePlayerTransform();

        if (IsPlayerCollider(
                collision.collider))
        {
            ResolvePlayerAttackHitReceiver();
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
                0f).normalized;

        int contactCount =
            collision.contactCount;

        for (int i = 0;
             i < contactCount;
             i++)
        {
            ContactPoint contact =
                collision.GetContact(i);

            float blockingDot =
                Vector3.Dot(
                    contact.normal,
                    moveDirection);

            if (blockingDot >
                -BLOCKING_NORMAL_THRESHOLD)
            {
                continue;
            }

            blockingContact = contact;
            return true;
        }

        blockingContact = default;
        return false;
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

        if (playerReceiverComponent != null &&
            playerReceiverComponent.isActiveAndEnabled &&
            playerAttackHitReceiver != null)
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
                $"{nameof(DroneEnemyAttackStrategy)}: " +
                "Playerに有効なIAttackHitReceiverの実装がありません。",
                enemyObject);

            hasWarnedMissingPlayerReceiver = true;
        }
    }

    private void BeginVisualPreparation()
    {
        preparationBaseLocalPosition =
            visualRoot.localPosition;

        isVisualPreparationActive = true;
    }

    private void UpdateVisualPreparation(
        float elapsedTime)
    {
        if (!isVisualPreparationActive)
        {
            return;
        }

        float shakeOffset =
            Mathf.Sin(
                elapsedTime *
                enemyData.PreparationShakeFrequency *
                TWO_PI) *
            enemyData.PreparationShakeAmount;

        Vector3 shakenLocalPosition =
            preparationBaseLocalPosition;

        shakenLocalPosition.x +=
            shakeOffset;

        visualRoot.localPosition =
            shakenLocalPosition;
    }

    private void RestoreVisualPreparationPosition()
    {
        if (!isVisualPreparationActive ||
            visualRoot == null)
        {
            isVisualPreparationActive = false;
            return;
        }

        visualRoot.localPosition =
            preparationBaseLocalPosition;

        isVisualPreparationActive = false;
    }

    private bool TryLockCurrentPlayerPosition()
    {
        ResolvePlayerTransform();

        if (playerTransform == null)
        {
            if (!hasWarnedMissingPlayer)
            {
                Debug.LogWarning(
                    $"{nameof(DroneEnemyAttackStrategy)}: " +
                    "Playerを取得できないため、攻撃位置をロックできません。",
                    enemyObject);

                hasWarnedMissingPlayer = true;
            }

            return false;
        }

        Vector3 playerWorldPosition =
            playerTransform.position;

        lockedPlayerPosition =
            new Vector2(
                playerWorldPosition.x,
                playerWorldPosition.y);

        hasWarnedMissingPlayer = false;

        return true;
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

                -currentVelocity.z);

        if (velocityDifference.sqrMagnitude <=
            DIRECTION_EPSILON *
            DIRECTION_EPSILON)
        {
            return;
        }

        // Droneのゲームプレイ移動はXY平面なので、
        // Attack中に生じたZ速度も打ち消す。
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

        if (currentVelocity.sqrMagnitude <=
            DIRECTION_EPSILON *
            DIRECTION_EPSILON)
        {
            return;
        }

        enemyRigidbody.AddForce(
            -currentVelocity,
            ForceMode.VelocityChange);
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

    private void ResolvePlayerTransform()
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
            playerReceiverComponent = null;
            playerAttackHitReceiver = null;
            return;
        }

        playerTransform =
            playerObject.transform;

        playerReceiverComponent = null;
        playerAttackHitReceiver = null;
    }

    private void ResolvePlayerAttackHitReceiver()
    {
        ResolvePlayerTransform();

        if (playerTransform == null)
        {
            playerReceiverComponent = null;
            playerAttackHitReceiver = null;
            return;
        }

        if (playerReceiverComponent != null &&
            playerReceiverComponent.isActiveAndEnabled &&
            playerAttackHitReceiver != null)
        {
            return;
        }

        playerReceiverComponent = null;
        playerAttackHitReceiver = null;

        MonoBehaviour[] behaviours =
            playerTransform.GetComponentsInChildren<MonoBehaviour>(
                true);

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour is not IAttackHitReceiver receiver ||
                !behaviour.isActiveAndEnabled)
            {
                continue;
            }

            playerReceiverComponent =
                behaviour;

            playerAttackHitReceiver =
                receiver;

            return;
        }
    }

    private void ResetRuntimeState()
    {
        updateCurrentPhase = null;
        isHomingActive = false;

        lockedPlayerPosition = Vector2.zero;
        attackPassStartPosition = Vector2.zero;
        baseAttackDirection = Vector2.zero;
        currentAttackDirection = Vector2.zero;
        lastAttackDirection = Vector2.right;
        lastHomingPosition = Vector2.zero;
        retreatStartPosition = Vector2.zero;
        retreatDirection = Vector2.zero;

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
