using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 突進エネミーの攻撃Stateで使用する突進攻撃Strategy。
/// </summary>
/// <remarks>
/// 責務:
/// ・攻撃方向を確定し、VisualRootだけをその方向へ向ける。
/// ・Attack開始時は、確定済みの方向へそのまま突進する。
/// ・Player接触時に攻撃情報を送信し、次回攻撃方向を確定する。
/// ・Player命中後のみ、次回突進前の予備動作として逆方向へ指定時間かけて後退する。
/// ・壁接触時は衝突Effectを生成し、Chargingを維持したまま移動だけを止める。
/// ・段差、穴、坂など移動不可地形ではChargingを維持したまま停止し、進入しない。
/// ・進行不能中にPlayerが反対側へ移動した場合、移動可能性を再評価して攻撃方向を更新する。
///
/// 担当しない責務:
/// ・Stateの保持やState遷移。
/// ・PlayerのHP変更。
/// ・設定値そのものの保持。
/// </remarks>
[Serializable]
public sealed class ChargeEnemyAttackStrategy :
    IEnemyAttackStrategy,
    IEnemyDebugRayProvider
{
    private const string PLAYER_TAG = "Player";

    private const float DIRECTION_EPSILON = 0.001f;
    private const float BLOCKING_NORMAL_THRESHOLD = 0.5f;

    [Tooltip(
        "Playerまたは壁へ衝突した位置に生成するEffect。" +
        "未設定の場合はEffectを生成しない。")]
    [SerializeField]
    private GameObject collisionEffectPrefab;

    private ChargeEnemyData enemyData;

    private GameObject enemyObject;
    private Transform enemyTransform;
    private Transform visualRoot;
    private Rigidbody enemyRigidbody;
    private Collider enemyCollider;

    private Transform playerTransform;
    private MonoBehaviour playerReceiverComponent;
    private IAttackHitReceiver playerAttackHitReceiver;

    private ChargeEnemyGroundChecker groundChecker;

    private AttackPhase currentPhase;

    private float preparationElapsedTime;
    private float preparationStartPositionX;
    private float attackDirectionSign = 1f;

    private bool isAttackActive;
    private bool isPreparationMovementBlocked;
    private bool hasDamagedPlayerThisCycle;
    private bool hasWarnedMissingPlayerReceiver;

    private enum AttackPhase
    {
        Preparation,
        Charging
    }

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData = enemyData as ChargeEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(ChargeEnemyAttackStrategy)}には" +
                $"{nameof(ChargeEnemyData)}が必要です。");

        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));

        enemyTransform = enemyObject.transform;

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

        groundChecker =
            new ChargeEnemyGroundChecker(
                this.enemyData,
                enemyTransform,
                enemyRigidbody,
                enemyCollider);

        ResolvePlayerReferences();
    }

    public void BeginAttack(
        float initialAttackDirectionSign)
    {
        groundChecker?.ClearDebugRays();

        isAttackActive = true;
        hasWarnedMissingPlayerReceiver = false;

        ResolvePlayerReferences();

        BeginCharge(
            NormalizeDirectionSign(
                initialAttackDirectionSign));
    }

    public void UpdateAttack()
    {
        if (!isAttackActive)
        {
            return;
        }

        switch (currentPhase)
        {
            case AttackPhase.Preparation:
                UpdatePreparation();
                break;

            case AttackPhase.Charging:
                UpdateCharge();
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
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
            collision == null ||
            collision.collider == null)
        {
            return;
        }

        if (currentPhase == AttackPhase.Preparation)
        {
            HandlePreparationCollision(
                collision);

            return;
        }

        if (currentPhase != AttackPhase.Charging)
        {
            return;
        }

        ResolvePlayerReferences();

        if (IsPlayerCollider(
                collision.collider))
        {
            HandlePlayerCollision(
                collision);

            return;
        }

        if (!TryGetBlockingContact(
                collision,
                attackDirectionSign,
                out ContactPoint blockingContact))
        {
            return;
        }

        // 壁などPlayer以外に阻まれても予備動作へは移らない。
        // Playerが反対側へ移動していれば、その側へ突進可能かをここで再評価する。
        StopHorizontalMovement();
        TryReevaluateChargeDirection();

        if (spawnBlockingCollisionEffect)
        {
            SpawnCollisionEffect(
                blockingContact.point);
        }
    }

    public void EndAttack()
    {
        groundChecker?.ClearDebugRays();

        isAttackActive = false;

        preparationElapsedTime = 0f;
        preparationStartPositionX = 0f;

        isPreparationMovementBlocked = false;
        hasDamagedPlayerThisCycle = false;
        hasWarnedMissingPlayerReceiver = false;

        StopHorizontalMovement();
    }

    /// <summary>
    /// 突進・予備動作で実際に使用した地面判定Rayを提供する。
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
    /// 指定方向への突進を開始する。
    /// Player命中後の予備動作を経由した場合も、初回Attack開始時も同じ入口を使用する。
    /// </summary>
    private void BeginCharge(
        float directionSign)
    {
        attackDirectionSign =
            NormalizeDirectionSign(
                directionSign);

        currentPhase =
            AttackPhase.Charging;

        isPreparationMovementBlocked = false;
        hasDamagedPlayerThisCycle = false;

        FaceAttackDirection();

        enemyRigidbody.WakeUp();

        UpdateCharge();
    }

    /// <summary>
    /// 確定した次回攻撃方向を保持したまま、
    /// その逆方向への予備動作を開始する。
    /// </summary>
    private void BeginPreparation(
        float directionSign)
    {
        attackDirectionSign =
            NormalizeDirectionSign(
                directionSign);

        preparationElapsedTime = 0f;

        preparationStartPositionX =
            enemyRigidbody.position.x;

        isPreparationMovementBlocked = false;

        currentPhase =
            AttackPhase.Preparation;

        StopHorizontalMovement();

        // 後退中も次に突進する方向を向いておくことで、
        // 「その方向へ勢いをつけるために下がる」という攻撃意図を維持する。
        FaceAttackDirection();

        enemyRigidbody.WakeUp();
    }

    private void UpdatePreparation()
    {
        float preparationDuration =
            enemyData.AttackPreparationDuration;

        if (preparationDuration <=
            DIRECTION_EPSILON)
        {
            CompletePreparation();
            return;
        }

        preparationElapsedTime +=
            Time.fixedDeltaTime;

        if (preparationElapsedTime >=
                preparationDuration ||
            Mathf.Approximately(
                preparationElapsedTime,
                preparationDuration))
        {
            CompletePreparation();
            return;
        }

        if (isPreparationMovementBlocked)
        {
            StopHorizontalMovement();
            return;
        }

        UpdatePreparationRetreat(
            preparationDuration);
    }

    /// <summary>
    /// 予備動作時間内に設定距離を後退できる速度を算出し、
    /// 確定済みの攻撃方向とは逆へ移動する。
    /// </summary>
    private void UpdatePreparationRetreat(
        float preparationDuration)
    {
        float retreatDistance =
            enemyData.AttackPreparationRetreatDistance;

        if (retreatDistance <=
            DIRECTION_EPSILON)
        {
            StopHorizontalMovement();
            return;
        }

        float movedDistance =
            Mathf.Abs(
                enemyRigidbody.position.x -
                preparationStartPositionX);

        float remainingDistance =
            retreatDistance -
            movedDistance;

        if (remainingDistance <=
            DIRECTION_EPSILON)
        {
            StopHorizontalMovement();
            return;
        }

        float retreatDirectionSign =
            -attackDirectionSign;

        float retreatSpeed =
            retreatDistance /
            preparationDuration;

        // FixedUpdate単位で設定距離を大きく超えないよう、
        // 最後の移動だけ残距離に合わせて速度を抑える。
        retreatSpeed =
            Mathf.Min(
                retreatSpeed,
                remainingDistance /
                Time.fixedDeltaTime);

        float expectedMovementDistance =
            retreatSpeed *
            Time.fixedDeltaTime;

        if (!groundChecker.CanMoveInDirection(
                retreatDirectionSign,
                expectedMovementDistance))
        {
            // 平坦面から外れる場合は後退だけを停止する。
            // 予備動作時間自体は継続し、時間終了後に突進へ移る。
            isPreparationMovementBlocked = true;

            StopHorizontalMovement();
            return;
        }

        SetHorizontalVelocity(
            retreatDirectionSign *
            retreatSpeed);
    }

    private void CompletePreparation()
    {
        StopHorizontalMovement();

        BeginCharge(
            attackDirectionSign);
    }

    private void HandlePreparationCollision(
        Collision collision)
    {
        if (isPreparationMovementBlocked)
        {
            return;
        }

        float retreatDirectionSign =
            -attackDirectionSign;

        if (!TryGetBlockingContact(
                collision,
                retreatDirectionSign,
                out _))
        {
            return;
        }

        // 後退側に壁などがある場合、そこへ押し続けず
        // 残りの予備動作時間はその場で待機する。
        isPreparationMovementBlocked = true;

        StopHorizontalMovement();
    }

    private void UpdateCharge()
    {
        if (CanContinueCharge())
        {
            ApplyChargeVelocity();
            return;
        }

        // 崖・段差・坂などで現在方向へ進めない場合もChargingは終了しない。
        // Playerが反対側へ移動しており、その方向へ移動可能なら攻撃方向だけを更新する。
        StopHorizontalMovement();

        if (!TryReevaluateChargeDirection())
        {
            return;
        }

        ApplyChargeVelocity();
    }

    private void ApplyChargeVelocity()
    {
        float targetVelocityX =
            attackDirectionSign *
            enemyData.AttackMoveSpeed;

        SetHorizontalVelocity(
            targetVelocityX);
    }

    private void HandlePlayerCollision(
        Collision collision)
    {
        // Player側の被弾処理やノックバックで位置が変化する前に、
        // 次の突進方向を確定する。
        float nextAttackDirectionSign =
            GetCurrentPlayerDirectionSign();

        if (!hasDamagedPlayerThisCycle)
        {
            hasDamagedPlayerThisCycle = true;

            if (playerReceiverComponent != null &&
                playerReceiverComponent.isActiveAndEnabled &&
                playerAttackHitReceiver != null)
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

        // ここで確定した方向は予備動作中に更新しない。
        // その逆方向へ後退したあと、同じ方向へ突進する。
        BeginPreparation(
            nextAttackDirectionSign);
    }

    private float GetCurrentPlayerDirectionSign()
    {
        ResolvePlayerReferences();

        if (playerTransform == null)
        {
            return attackDirectionSign;
        }

        float deltaX =
            playerTransform.position.x -
            enemyRigidbody.position.x;

        if (Mathf.Abs(deltaX) <=
            DIRECTION_EPSILON)
        {
            return attackDirectionSign;
        }

        return Mathf.Sign(deltaX);
    }

    private void FaceAttackDirection()
    {
        float currentFacingSign =
            GetCurrentHorizontalFacingSign();

        if (Mathf.Abs(currentFacingSign) <=
            DIRECTION_EPSILON ||
            Mathf.Sign(currentFacingSign) ==
            attackDirectionSign)
        {
            return;
        }

        visualRoot.rotation =
            Quaternion.AngleAxis(
                180f,
                Vector3.up) *
            visualRoot.rotation;
    }

    private float GetCurrentHorizontalFacingSign()
    {
        float forwardX =
            visualRoot.forward.x;

        if (Mathf.Abs(forwardX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                forwardX);
        }

        float rightX =
            visualRoot.right.x;

        if (Mathf.Abs(rightX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(
                rightX);
        }

        return 0f;
    }

    /// <summary>
    /// 現在の攻撃方向が移動不能になった場合だけPlayer位置を再評価し、
    /// 反対側へ突進可能なら攻撃方向を更新する。
    /// </summary>
    /// <remarks>
    /// 通常の突進中はPlayerを追尾するように方向変更しない。
    /// 壁・崖などで停止した時だけ再評価することで、
    /// 突進開始時に確定した方向へ走り切る攻撃性質を維持する。
    /// </remarks>
    private bool TryReevaluateChargeDirection()
    {
        float playerDirectionSign =
            GetCurrentPlayerDirectionSign();

        if (Mathf.Sign(playerDirectionSign) ==
            attackDirectionSign)
        {
            return false;
        }

        if (!CanChargeInDirection(
                playerDirectionSign))
        {
            return false;
        }

        attackDirectionSign =
            NormalizeDirectionSign(
                playerDirectionSign);

        FaceAttackDirection();
        enemyRigidbody.WakeUp();

        return true;
    }

    /// <summary>
    /// 次の物理更新分だけ現在の攻撃方向へ突進しても、
    /// 現在の平坦面上に留まれるか判定する。
    /// </summary>
    private bool CanContinueCharge()
    {
        return CanChargeInDirection(
            attackDirectionSign);
    }

    /// <summary>
    /// 指定方向へ次の物理更新分だけ突進しても、
    /// 現在の平坦面上に留まれるか判定する。
    /// </summary>
    private bool CanChargeInDirection(
        float directionSign)
    {
        float expectedMovementDistance =
            enemyData.AttackMoveSpeed *
            Time.fixedDeltaTime;

        return groundChecker.CanMoveInDirection(
            directionSign,
            expectedMovementDistance);
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
                playerReceiverComponent = null;
                playerAttackHitReceiver = null;
                return;
            }

            playerTransform =
                playerObject.transform;

            playerReceiverComponent = null;
            playerAttackHitReceiver = null;
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

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            MonoBehaviour behaviour =
                behaviours[i];

            if (behaviour is not IAttackHitReceiver receiver ||
                !behaviour.isActiveAndEnabled)
            {
                continue;
            }

            playerReceiverComponent = behaviour;
            playerAttackHitReceiver = receiver;
            return;
        }
    }

    private static float NormalizeDirectionSign(
        float directionSign)
    {
        return directionSign < 0f
            ? -1f
            : 1f;
    }
}
