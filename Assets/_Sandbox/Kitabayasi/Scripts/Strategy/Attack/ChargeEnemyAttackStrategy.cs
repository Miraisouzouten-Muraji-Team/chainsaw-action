using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 突進エネミーの攻撃Stateで使用する突進攻撃Strategy。
/// </summary>
/// <remarks>
/// 責務:
/// ・Preparation中は停止し、設定時間経過後に1回分の突進を開始できる状態へする。
/// ・突進開始地点からChargeDistanceまでを1回の攻撃として扱う。
/// ・Player命中、Stage壁による進行阻害、崖・段差等の進行不能で現在の攻撃を終了する。
/// ・Player命中時に攻撃情報を1回だけ送信する。
///
/// 担当しない責務:
/// ・Stateの保持やState遷移。
/// ・攻撃完了後に次の攻撃を継続するかの判定。
/// ・PlayerのHP変更。
/// ・共通のEnemy設定値の保持。
/// </remarks>
[Serializable]
public sealed class ChargeEnemyAttackStrategy :
    IEnemyAttackStrategy,
    IEnemyDebugRayProvider
{
    private const float DIRECTION_EPSILON = 0.001f;
    private const float CHARGE_ARRIVAL_TOLERANCE = 0.2f;
    private const float BLOCKING_NORMAL_THRESHOLD = 0.5f;
    private const int ANIMATION_BASE_LAYER_INDEX = 0;
    private const float ANIMATION_SPEED_EPSILON = 0.0001f;

    [Header("攻撃アニメーション")]
    [Tooltip("予備動作開始時に再生するAnimator Stateのフルパス（例: Base Layer.Preparation）。")]
    [SerializeField]
    private string preparationAnimationStateName = "Base Layer.Preparation";

    [Tooltip("突進開始時に再生するAnimator Stateのフルパス（例: Base Layer.Attack）。")]
    [SerializeField]
    private string attackAnimationStateName = "Base Layer.Attack";

    [Tooltip(
        "1回の突進開始地点から進める最大距離。" +
        "初回はChargeEnemyDataのCharge Distanceを初期値として使用し、" +
        "以降はこのEnemy個体の設定値として保持する。")]
    [SerializeField]
    [Min(0f)]
    private float chargeDistance;

    [SerializeField]
    [HideInInspector]
    private bool hasChargeDistanceBeenInitialized;

    [Tooltip(
        "PlayerまたはStage壁へ衝突した位置に生成するEffect。" +
        "未設定の場合はEffectを生成しない。")]
    [SerializeField]
    private GameObject collisionEffectPrefab;

    private ChargeEnemyData enemyData;

    private GameObject enemyObject;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private Collider enemyCollider;
    private Animator animator;

    private int preparationAnimationStateHash;
    private int attackAnimationStateHash;
    private readonly List<AnimatorClipInfo> preparationAnimationClipInfos =
        new List<AnimatorClipInfo>(1);
    private float animatorSpeedBeforePreparation = 1f;
    private AnimatorUpdateMode animatorUpdateModeBeforePreparation;
    private bool hasChangedAnimatorUpdateMode;
    private bool needsPreparationAnimationSpeedAdjustment;
    private bool hasAdjustedPreparationAnimationSpeed;
    private float preparationAnimationClipLength;
    private float preparationAnimationStateSpeed;

    private EnemyPlayerReference playerReference;
    private ChargeEnemyGroundChecker groundChecker;

    private float preparationElapsedTime;
    private float attackDirectionSign = 1f;
    private float chargeStartPositionX;

    private bool isPreparationActive;
    private bool isAttackActive;
    private bool isAttackComplete;
    private bool hasDamagedPlayerThisAttack;
    private bool hasWarnedMissingPlayerReceiver;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData = enemyData as ChargeEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(ChargeEnemyAttackStrategy)}には" +
                $"{nameof(ChargeEnemyData)}が必要です。");

        InitializeChargeDistanceIfNeeded(
            this.enemyData.ChargeDistance);

        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));

        this.visualRoot = visualRoot
            ? visualRoot
            : throw new InvalidOperationException(
                $"{nameof(ChargeEnemyAttackStrategy)}を使用するEnemyには" +
                "VisualRootの設定が必要です。");

        enemyRigidbody =
            enemyObject.GetComponent<Rigidbody>();

        if (enemyRigidbody == null)
        {
            throw new InvalidOperationException(
                $"{nameof(ChargeEnemyAttackStrategy)}を使用するEnemyには" +
                $"{nameof(Rigidbody)}が必要です。");
        }

        if (enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(ChargeEnemyAttackStrategy)}は" +
                "非KinematicのRigidbodyを使用する前提です。");
        }

        enemyCollider =
            enemyObject.GetComponentInChildren<Collider>();

        if (enemyCollider == null)
        {
            throw new InvalidOperationException(
                $"{nameof(ChargeEnemyAttackStrategy)}を使用するEnemyには" +
                $"{nameof(Collider)}が必要です。");
        }

        InitializeAnimations();

        playerReference =
            new EnemyPlayerReference();

        groundChecker =
            new ChargeEnemyGroundChecker(
                this.enemyData,
                enemyObject.transform,
                enemyRigidbody,
                enemyCollider);

        playerReference.TryGetPlayerTransform(
            out _);
    }

    public void BeginPreparation(
        float initialAttackDirectionSign)
    {
        RestoreAnimatorSettings();
        ResetRuntimeState();
        groundChecker?.ClearDebugRays();

        attackDirectionSign =
            NormalizeDirectionSign(
                initialAttackDirectionSign);

        isPreparationActive = true;

        playerReference.TryGetPlayerTransform(
            out _);

        FaceAttackDirection();
        StopHorizontalMovement();

        preparationAnimationClipLength = 0f;
        preparationAnimationStateSpeed = 0f;

        if (preparationAnimationStateHash == 0 ||
            animator == null ||
            !animator.isActiveAndEnabled ||
            enemyData.AttackPreparationDuration <= ANIMATION_SPEED_EPSILON)
        {
            PlayAnimation(preparationAnimationStateHash);
            return;
        }

        animatorSpeedBeforePreparation = animator.speed;
        animatorUpdateModeBeforePreparation = animator.updateMode;

        // 予備動作の経過時間はFixedUpdateで計測するため、
        // この間だけAnimatorも物理更新と同じ周期にそろえる。
        if (animator.updateMode != AnimatorUpdateMode.Fixed)
        {
            animator.updateMode = AnimatorUpdateMode.Fixed;
            hasChangedAnimatorUpdateMode = true;
        }

        // Play直後のAnimatorは前Stateの情報を返す場合がある。
        // 時間を進めずに評価し、最初の再生フレームより前に速度を確定させる。
        PlayAnimation(preparationAnimationStateHash);
        animator.Update(0f);

        needsPreparationAnimationSpeedAdjustment = true;
        TrySetPreparationAnimationSpeed();
    }

    public bool UpdatePreparation()
    {
        StopHorizontalMovement();

        if (!isPreparationActive)
        {
            return true;
        }

        // 再生対象Clipが取得可能になった時点で、予備動作時間に合わせて速度を1度だけ決める。
        TrySetPreparationAnimationSpeed();

        float preparationDuration =
            enemyData.AttackPreparationDuration;

        if (preparationDuration <=
            DIRECTION_EPSILON)
        {
            isPreparationActive = false;
            return true;
        }

        preparationElapsedTime +=
            Time.fixedDeltaTime;

        if (preparationElapsedTime <
                preparationDuration &&
            !Mathf.Approximately(
                preparationElapsedTime,
                preparationDuration))
        {
            return false;
        }

        LogPreparationAnimationCompletion();
        isPreparationActive = false;
        return true;
    }

    public void BeginAttack()
    {
        // Animator全体に適用した予備動作用の速度と更新モードを突進前に戻す。
        RestoreAnimatorSettings();
        isPreparationActive = false;
        isAttackActive = true;
        isAttackComplete = false;
        hasDamagedPlayerThisAttack = false;
        hasWarnedMissingPlayerReceiver = false;

        chargeStartPositionX =
            enemyRigidbody.position.x;

        Debug.Log(
            $"[ChargeEnemyReturnDebug] 突進開始。 " +
            $"startX={chargeStartPositionX:F3}, direction={attackDirectionSign:F0}, " +
            $"chargeDistance={chargeDistance:F3}",
            enemyObject);

        FaceAttackDirection();
        PlayAnimation(attackAnimationStateHash);
        enemyRigidbody.WakeUp();
    }

    public bool UpdateAttack()
    {
        if (isAttackComplete)
        {
            return true;
        }

        if (!isAttackActive)
        {
            return false;
        }

        UpdateCharge();
        return isAttackComplete;
    }

    public void HandleCollisionEnter(
        Collision collision)
    {
        HandleCollision(
            collision,
            spawnBlockingCollisionEffect: true);
    }

    public void HandleCollisionStay(
        Collision collision)
    {
        HandleCollision(
            collision,
            spawnBlockingCollisionEffect: false);
    }

    private void HandleCollision(
        Collision collision,
        bool spawnBlockingCollisionEffect)
    {
        if (!isAttackActive ||
            isAttackComplete ||
            collision == null ||
            collision.collider == null)
        {
            return;
        }

        if (playerReference.IsPlayerCollider(
                collision.collider))
        {
            HandlePlayerCollision(
                collision);
            return;
        }

        if (!IsStageCollider(
                collision.collider) ||
            !TryGetBlockingContact(
                collision,
                attackDirectionSign,
                out ContactPoint blockingContact))
        {
            return;
        }

        // Stage Layerへの接触だけでは床接触も含まれるため、
        // 現在の突進方向を実際に塞ぐContactがある場合だけ攻撃終了とする。
        CompleteAttack(
            $"Stage壁で進行阻害: collider={collision.collider.name}, layer={collision.collider.gameObject.layer}");

        if (spawnBlockingCollisionEffect)
        {
            SpawnCollisionEffect(
                blockingContact.point);
        }
    }

    public void EndAttack()
    {
        groundChecker?.ClearDebugRays();
        RestoreAnimatorSettings();

        StopHorizontalMovement();
        ResetRuntimeState();
    }

    /// <summary>
    /// 突進で実際に使用した地面判定Rayを提供する。
    /// </summary>
    public void CollectDebugRays(
        List<EnemyDebugRay> debugRays)
    {
        if (debugRays == null)
        {
            throw new ArgumentNullException(
                nameof(debugRays));
        }

        groundChecker?.CollectDebugRays(
            debugRays);
    }

    /// <summary>
    /// 個体側の突進距離がまだ初期化されていない場合だけ、
    /// ChargeEnemyDataの値を初期値としてコピーする。
    /// </summary>
    /// <remarks>
    /// 初期化後はScriptableObjectを継続参照せず、
    /// Enemy個体にシリアライズされた値を攻撃距離として使用する。
    /// </remarks>
    private void InitializeChargeDistanceIfNeeded(
        float defaultChargeDistance)
    {
        if (hasChargeDistanceBeenInitialized)
        {
            return;
        }

        chargeDistance =
            Mathf.Max(
                0f,
                defaultChargeDistance);

        hasChargeDistanceBeenInitialized = true;
    }

    private void UpdateCharge()
    {
        if (enemyData.AttackMoveSpeed <=
            DIRECTION_EPSILON)
        {
            CompleteAttack("AttackMoveSpeedが0以下");
            return;
        }

        float traveledDistance =
            Mathf.Max(
                0f,
                (enemyRigidbody.position.x -
                 chargeStartPositionX) *
                attackDirectionSign);

        float remainingDistance =
            chargeDistance -
            traveledDistance;

        if (remainingDistance <=
            CHARGE_ARRIVAL_TOLERANCE)
        {
            CompleteAttack("ChargeDistance到達");
            return;
        }

        float maximumStepDistance =
            enemyData.AttackMoveSpeed *
            Time.fixedDeltaTime;

        float expectedMovementDistance =
            Mathf.Min(
                maximumStepDistance,
                remainingDistance);

        if (!groundChecker.CanMoveInDirection(
                attackDirectionSign,
                expectedMovementDistance))
        {
            // 崖・段差・許容外の坂など、既存GroundCheckerが進行不能とした時点で
            // 同一Attack内の方向再評価は行わず再索敵へ返す。
            CompleteAttack(
                $"GroundChecker移動不可: {groundChecker.LastFailureReason}");
            return;
        }

        float targetSpeed =
            enemyData.AttackMoveSpeed;

        if (Time.fixedDeltaTime >
            DIRECTION_EPSILON)
        {
            targetSpeed =
                Mathf.Min(
                    targetSpeed,
                    remainingDistance /
                    Time.fixedDeltaTime);
        }

        SetHorizontalVelocity(
            attackDirectionSign *
            targetSpeed);
    }

    private void HandlePlayerCollision(
        Collision collision)
    {
        if (!hasDamagedPlayerThisAttack)
        {
            hasDamagedPlayerThisAttack = true;

            if (playerReference.TryGetAttackHitReceiver(
                    requireActiveAndEnabled: true,
                    out IAttackHitReceiver playerAttackHitReceiver,
                    out _))
            {
                float damage =
                    enemyData.AttackPower *
                    (enemyData.AttackContactDamagePercent / 100f);

                playerAttackHitReceiver.ReceiveAttackHit(
                    new EnemyAttackHitData(
                        enemyData,
                        damage));
            }
            else if (!hasWarnedMissingPlayerReceiver)
            {
                Debug.LogWarning(
                    $"{nameof(ChargeEnemyAttackStrategy)}: " +
                    "Playerに有効なIAttackHitReceiverの実装がありません。",
                    enemyObject);

                hasWarnedMissingPlayerReceiver = true;
            }
        }

        SpawnCollisionEffect(
            GetCollisionEffectPosition(
                collision));

        // Player命中は現在の1回分の突進完了条件。
        // 次の突進はStateMachineの再索敵と新しいPreparationを必ず経由する。
        CompleteAttack("Player衝突");
    }

    private void CompleteAttack(
        string completionReason)
    {
        if (isAttackComplete)
        {
            return;
        }

        StopHorizontalMovement();

        float traveledDistance =
            Mathf.Abs(
                enemyRigidbody.position.x -
                chargeStartPositionX);

        Debug.Log(
            $"[ChargeEnemyReturnDebug] 突進完了。 " +
            $"reason={completionReason}, startX={chargeStartPositionX:F3}, " +
            $"currentX={enemyRigidbody.position.x:F3}, traveled={traveledDistance:F3}",
            enemyObject);

        isAttackActive = false;
        isAttackComplete = true;
    }

    /// <summary>
    /// ChargeEnemyの見た目に属するAnimatorを見つけ、再生対象のStateを検証する。
    /// Animatorの設定不備があっても、物理移動と攻撃の進行は妨げない。
    /// </summary>
    private void InitializeAnimations()
    {
        animator = visualRoot.GetComponent<Animator>();
        if (animator == null)
        {
            animator = visualRoot.GetComponentInChildren<Animator>(true);
        }

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemyAttackStrategy)}: VisualRoot以下に" +
                "AnimatorとAnimator Controllerを設定してください。" +
                "アニメーションを再生せず、攻撃処理は継続します。",
                enemyObject);
            return;
        }

        preparationAnimationStateHash = GetAnimationStateHash(
            preparationAnimationStateName);
        attackAnimationStateHash = GetAnimationStateHash(
            attackAnimationStateName);
    }

    private int GetAnimationStateHash(string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName))
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemyAttackStrategy)}: " +
                "Animator State名が空です。対応するモーションを再生できません。",
                enemyObject);
            return 0;
        }

        int stateHash = Animator.StringToHash(stateName);
        if (animator.HasState(ANIMATION_BASE_LAYER_INDEX, stateHash))
        {
            return stateHash;
        }

        Debug.LogWarning(
            $"{nameof(ChargeEnemyAttackStrategy)}: " +
            $"AnimatorにState '{stateName}' が見つかりません。" +
            "Animator ControllerとState名を確認してください。",
            enemyObject);
        return 0;
    }

    private void PlayAnimation(int stateHash)
    {
        if (stateHash == 0 || animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }

        // 再突進する場合も、同じStateを必ず先頭から再生する。
        animator.Play(stateHash, ANIMATION_BASE_LAYER_INDEX, 0f);
    }

    /// <summary>
    /// 現在の予備動作Stateに設定されたClipを、残り予備時間で最後まで再生する速度にする。
    /// </summary>
    /// <remarks>
    /// BeginPreparationで開始フレーム前に速度を決定する。
    /// State情報の取得が遅れた場合も、既に再生した部分を考慮して残り時間へ合わせる。
    /// 対象Stateは単一Clipを再生する構成を前提とする。
    /// </remarks>
    private void TrySetPreparationAnimationSpeed()
    {
        if (!needsPreparationAnimationSpeedAdjustment ||
            animator == null ||
            !animator.isActiveAndEnabled)
        {
            return;
        }

        AnimatorStateInfo stateInfo =
            animator.GetCurrentAnimatorStateInfo(ANIMATION_BASE_LAYER_INDEX);

        if (stateInfo.fullPathHash != preparationAnimationStateHash)
        {
            return;
        }

        preparationAnimationClipInfos.Clear();
        animator.GetCurrentAnimatorClipInfo(
            ANIMATION_BASE_LAYER_INDEX,
            preparationAnimationClipInfos);

        if (preparationAnimationClipInfos.Count == 0)
        {
            return;
        }

        if (preparationAnimationClipInfos.Count != 1 ||
            preparationAnimationClipInfos[0].clip == null)
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemyAttackStrategy)}: " +
                "予備動作Stateには単一のAnimationClipを設定してください。" +
                "Clip長を取得できず速度調整を省略します。",
                enemyObject);
            needsPreparationAnimationSpeedAdjustment = false;
            return;
        }

        preparationAnimationClipLength =
            preparationAnimationClipInfos[0].clip.length;
        preparationAnimationStateSpeed =
            stateInfo.speed * stateInfo.speedMultiplier;

        float remainingPreparationDuration =
            enemyData.AttackPreparationDuration - preparationElapsedTime;
        float remainingClipRatio =
            1f - Mathf.Clamp01(stateInfo.normalizedTime);

        if (preparationAnimationClipLength <= ANIMATION_SPEED_EPSILON ||
            remainingPreparationDuration <= ANIMATION_SPEED_EPSILON ||
            remainingClipRatio <= ANIMATION_SPEED_EPSILON ||
            preparationAnimationStateSpeed <= ANIMATION_SPEED_EPSILON)
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemyAttackStrategy)}: " +
                "Clip長・残り予備時間・State速度・残り再生区間を確認してください。" +
                "再生速度の自動調整を省略します。",
                enemyObject);
            needsPreparationAnimationSpeedAdjustment = false;
            return;
        }

        // 例: Clip長1秒、予備動作0.5秒、State速度1ならAnimator.speedは2。
        // 遅延取得時は進んだ分のClipを除外し、終端で待つ時間を作らない。
        float requiredAnimatorSpeed =
            (preparationAnimationClipLength * remainingClipRatio) /
            (remainingPreparationDuration * preparationAnimationStateSpeed);

        if (float.IsNaN(requiredAnimatorSpeed) ||
            float.IsInfinity(requiredAnimatorSpeed) ||
            requiredAnimatorSpeed <= ANIMATION_SPEED_EPSILON)
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemyAttackStrategy)}: " +
                "予備動作の再生速度を正しく計算できません。",
                enemyObject);
            needsPreparationAnimationSpeedAdjustment = false;
            return;
        }

        animator.speed = requiredAnimatorSpeed;
        hasAdjustedPreparationAnimationSpeed = true;
        needsPreparationAnimationSpeedAdjustment = false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log(
            $"[ChargeEnemyPreparationAnimation] 速度設定。 " +
            $"clipLength={preparationAnimationClipLength:F3}s, " +
            $"preparationDuration={enemyData.AttackPreparationDuration:F3}s, " +
            $"remainingDuration={remainingPreparationDuration:F3}s, " +
            $"normalizedTime={stateInfo.normalizedTime:F3}, " +
            $"stateSpeed={preparationAnimationStateSpeed:F3}, " +
            $"animatorSpeed={animator.speed:F3}",
            enemyObject);
#endif
    }

    /// <summary>
    /// 再生が実際にどこまで進んだかを、攻撃開始直前に確認するための診断ログ。
    /// </summary>
    private void LogPreparationAnimationCompletion()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (animator == null ||
            preparationAnimationStateHash == 0 ||
            !animator.isActiveAndEnabled)
        {
            return;
        }

        AnimatorStateInfo stateInfo =
            animator.GetCurrentAnimatorStateInfo(ANIMATION_BASE_LAYER_INDEX);
        bool isPreparationState =
            stateInfo.fullPathHash == preparationAnimationStateHash;

        Debug.Log(
            $"[ChargeEnemyPreparationAnimation] 終了。 " +
            $"elapsed={preparationElapsedTime:F3}s, " +
            $"duration={enemyData.AttackPreparationDuration:F3}s, " +
            $"normalizedTime={stateInfo.normalizedTime:F3}, " +
            $"isPreparationState={isPreparationState}, " +
            $"isTransitioning={animator.IsInTransition(ANIMATION_BASE_LAYER_INDEX)}, " +
            $"animatorSpeed={animator.speed:F3}",
            enemyObject);
#endif
    }

    private void RestoreAnimatorSettings()
    {
        needsPreparationAnimationSpeedAdjustment = false;

        if (animator != null)
        {
            if (hasAdjustedPreparationAnimationSpeed)
            {
                animator.speed = animatorSpeedBeforePreparation;
            }

            if (hasChangedAnimatorUpdateMode)
            {
                animator.updateMode = animatorUpdateModeBeforePreparation;
            }
        }

        hasAdjustedPreparationAnimationSpeed = false;
        hasChangedAnimatorUpdateMode = false;
    }

    private void FaceAttackDirection()
    {
        AlignVisualWithHorizontalDirection(
            attackDirectionSign);
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

    private bool TryGetBlockingContact(
        Collision collision,
        float moveDirectionSign,
        out ContactPoint blockingContact)
    {
        Vector3 moveDirection =
            Vector3.right *
            NormalizeDirectionSign(
                moveDirectionSign);

        return EnemyBlockingContactEvaluator.TryGetBlockingContact(
            collision,
            moveDirection,
            BLOCKING_NORMAL_THRESHOLD,
            out blockingContact);
    }

    private bool IsStageCollider(
        Collider targetCollider)
    {
        int targetLayerMask =
            1 << targetCollider.gameObject.layer;

        return (enemyData.StageLayerMask.value &
                targetLayerMask) != 0;
    }

    private void SpawnCollisionEffect(
        Vector3 position)
    {
        if (collisionEffectPrefab == null)
        {
            return;
        }

        UnityEngine.Object.Instantiate(
            collisionEffectPrefab,
            position,
            Quaternion.identity);
    }

    private Vector3 GetCollisionEffectPosition(
        Collision collision)
    {
        if (collision.contactCount > 0)
        {
            return collision.GetContact(0).point;
        }

        return enemyCollider.bounds.center;
    }

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

    private void ResetRuntimeState()
    {
        isPreparationActive = false;
        isAttackActive = false;
        isAttackComplete = false;
        hasDamagedPlayerThisAttack = false;
        hasWarnedMissingPlayerReceiver = false;

        preparationElapsedTime = 0f;
        chargeStartPositionX = 0f;
    }

    private static float NormalizeDirectionSign(
        float directionSign)
    {
        return directionSign < 0f
            ? -1f
            : 1f;
    }
}
