using UnityEngine;

/// <summary>
/// Enemyが外部から攻撃命中情報を受信するための窓口。
/// </summary>
/// <remarks>
/// IAttackHitReceiverを実装し、
/// Playerから受け取ったPlayerAttackHitDataを保持して
/// Enemy側の各機能から参照できるようにする。
///
/// Playerの攻撃データからダメージ量を取得し、
/// EnemyHealthへ適用する。
///
/// やられ演出そのものは実行せず、
/// 生存している場合はEnemyStateMachineへ
/// Damage Stateへの遷移を要求する。
/// </remarks>
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(EnemyStateMachine))]
public sealed class EnemyAttackHitReceiver :
    MonoBehaviour,
    IAttackHitReceiver
{
    private const string PLAYER_TAG = "Player";
    private const float HIT_SIDE_EPSILON = 0.001f;

    private EnemyHealth enemyHealth;
    private EnemyStateMachine enemyStateMachine;
    private Transform playerTransform;

    /// <summary>
    /// このEnemyが最後に受けた有効なPlayer攻撃の命中情報。
    /// </summary>
    public PlayerAttackHitData LastPlayerAttackHitData { get; private set; }

    /// <summary>
    /// 最後の命中について、敵側で判定した攻撃種別に対応するAttackData。
    /// 弱攻撃の食い込み中はPlayerの通常攻撃データを使うが、元の命中情報は変更しない。
    /// </summary>
    public AttackData LastEffectiveAttackData { get; private set; }

    /// <summary>
    /// 最後の有効命中が、弱攻撃アニメーション中の食い込みダメージだったか。
    /// </summary>
    public bool LastHitWasWeakAttackDigging { get; private set; }

    /// <summary>
    /// 弱攻撃食い込み命中時のチェンソー回転率。取得できた場合のみ有効。
    /// </summary>
    public bool HasLastWeakAttackRotationRatio { get; private set; }
    public float LastWeakAttackRotationRatio { get; private set; }

    /// <summary>
    /// 最後に受けたPlayer攻撃について、左右判定まで反映した初速度。
    /// 被弾時に位置移動を禁止する攻撃でも、死亡時の切断片飛散に使用するため保持する。
    /// </summary>
    /// <remarks>
    /// EnemyHealth.TakeDamage内で死亡通知が同期的に発火するため、
    /// 死亡処理から参照できるようダメージ適用前に確定して保持する。
    /// </remarks>
    public Vector2 LastResolvedKnockbackVelocity { get; private set; }

    /// <summary>
    /// 最後に受けたPlayer攻撃について、Enemyから見た攻撃元の左右方向。
    /// -1は左側、+1は右側を表す。
    /// </summary>
    /// <remarks>
    /// ノックバック方向と切断片の回転方向で同じ左右判定結果を共有するため、
    /// ダメージ適用前に確定して保持する。
    /// </remarks>
    public float LastHitSourceDirectionSign { get; private set; }

    private void Awake()
    {
        enemyHealth =
            GetComponent<EnemyHealth>();

        enemyStateMachine =
            GetComponent<EnemyStateMachine>();

        ResolvePlayerTransform();
    }

    /// <summary>
    /// 外部から攻撃命中情報を受け取る。
    /// </summary>
    public void ReceiveAttackHit(
        AttackHitData attackHitData)
    {
        if (!isActiveAndEnabled || enemyHealth.IsDead)
        {
            return;
        }

        if (attackHitData == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyAttackHitReceiver)}: " +
                "受信したAttackHitDataがnullです。",
                this);

            return;
        }

        if (attackHitData
            is not PlayerAttackHitData playerAttackHitData)
        {
            Debug.LogWarning(
                $"{nameof(EnemyAttackHitReceiver)}: " +
                $"未対応の攻撃命中情報を受信しました。 " +
                $"Type: {attackHitData.GetType().Name}",
                this);

            return;
        }

        Debug.Log(
            $"{nameof(EnemyAttackHitReceiver)}: " +
            $"Playerの攻撃命中情報を受信しました。 " +
            $"Enemy: {gameObject.name}",
            this);

        if (playerAttackHitData.AttackData
            is not AttackData playerAttackData || playerAttackData == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyAttackHitReceiver)}: " +
                "PlayerAttackHitDataに設定されたAttackDataが " +
                $"{nameof(AttackData)}ではありません。",
                this);

            return;
        }

        // 攻撃の識別には元AttackData、ダメージ・ノックバックには命中ごとの値を使用する。
        int hitDamage = playerAttackHitData.DamageOverride ?? playerAttackData.damage;
        Vector2 hitAttackVector =
            playerAttackHitData.AttackVectorOverride ?? playerAttackData.attackVector;

        // EnemyHealthでは0以下のダメージを無効としているため、
        // 実際にダメージが成立しない場合はノックバック情報も更新しない。
        if (hitDamage <= 0)
        {
            return;
        }

        float hitSourceDirectionSign =
            ResolveHitSourceDirectionSign();

        // Playerの送信データは変更しない。弱攻撃の食い込み中だけ、敵側で
        // 通常攻撃用AttackDataを選び直してから同期的な死亡通知へ進める。
        bool isWeakAttackDiggingHit = TryResolveWeakAttackDigging(
            out AttackData weakAttackData,
            out float? weakAttackRotationRatio);
        AttackData effectiveAttackData = isWeakAttackDiggingHit
            ? weakAttackData
            : playerAttackData;

        LastPlayerAttackHitData =
            playerAttackHitData;
        LastEffectiveAttackData = effectiveAttackData;
        LastHitWasWeakAttackDigging = isWeakAttackDiggingHit;
        HasLastWeakAttackRotationRatio = weakAttackRotationRatio.HasValue;
        LastWeakAttackRotationRatio = weakAttackRotationRatio ?? 0f;
        LastHitSourceDirectionSign = hitSourceDirectionSign;

        // 食い込み中は本体をノックバックさせないが、死亡後の切断片は
        // 通常攻撃のAttackVectorで飛散させるため、別々の値を保持する。
        Vector2 deathPieceAttackVector = isWeakAttackDiggingHit
            ? effectiveAttackData.attackVector
            : hitAttackVector;
        LastResolvedKnockbackVelocity =
            EnemyKnockbackMotor
                .CalculateInitialKnockbackVelocity(
                    deathPieceAttackVector,
                    hitSourceDirectionSign);

        // TakeDamage内でDiedが同期通知されるため、
        // 死亡攻撃の方向をDeath Strategyから参照できる状態にしてからHPへ反映する。
        enemyHealth.TakeDamage(
            hitDamage,
            playerAttackHitData);

        // TakeDamageによって死亡した場合は、
        // EnemyHealth.Died経由で死亡処理が行われるため
        // Damage Stateには遷移しない。
        if (enemyHealth.IsDead)
        {
            return;
        }

        enemyStateMachine.RequestDamageState(
            hitSourceDirectionSign,
            isWeakAttackDiggingHit ? effectiveAttackData.attackVector : hitAttackVector,
            effectiveAttackData,
            isWeakAttackDiggingHit);
    }

    /// <summary>
    /// 既存のPlayer状態だけで、通常弱攻撃に含まれる食い込みを識別する。
    /// 通常のRキー食い込みはIsWeakEnemyAttack=falseなので従来どおり処理する。
    /// </summary>
    private bool TryResolveWeakAttackDigging(
        out AttackData normalAttackData,
        out float? chainsawRotationRatio)
    {
        normalAttackData = null;
        chainsawRotationRatio = null;

        if (!TryGetPlayerTransform(out Transform playerRoot))
        {
            return false;
        }

        SensorChainsawDigging digging = playerRoot.GetComponent<SensorChainsawDigging>();
        SensorPlayerController controller = playerRoot.GetComponent<SensorPlayerController>();
        if (digging == null || !digging.isActiveAndEnabled ||
            !digging.IsWeakEnemyAttack || controller == null ||
            !controller.isActiveAndEnabled || controller.CurrentAttackData == null)
        {
            return false;
        }

        normalAttackData = controller.CurrentAttackData;
        ChainsawAccelerator accelerator =
            playerRoot.GetComponentInChildren<ChainsawAccelerator>(true);
        if (accelerator != null)
        {
            chainsawRotationRatio = Mathf.Clamp01(accelerator.PowerRatio);
        }
        return true;
    }

    /// <summary>
    /// Player Tagから取得したPlayer本体Transformを返す。
    /// </summary>
    /// <remarks>
    /// 通常攻撃のBake済み軌跡をWorld空間へ復元する際にも同じPlayer基準を使用する。
    /// PlayerがAwake後に生成された場合は呼び出し時に再取得を試みる。
    /// </remarks>
    public bool TryGetPlayerTransform(
        out Transform resolvedPlayerTransform)
    {
        if (playerTransform == null)
        {
            ResolvePlayerTransform();
        }

        // 現行Prefabでは子ColliderにもPlayer Tagがある。軌跡の復元には本体を使う。
        SensorPlayerController playerController =
            playerTransform != null
                ? playerTransform.GetComponentInParent<SensorPlayerController>()
                : null;

        resolvedPlayerTransform =
            playerController != null ? playerController.transform : playerTransform;

        return resolvedPlayerTransform != null;
    }

    /// <summary>
    /// Player本体の現在位置から、
    /// Enemyの左右どちら側から攻撃されたかを求める。
    /// </summary>
    /// <remarks>
    /// チェンソーのTipやRoot位置ではなくPlayer本体を基準にすることで、
    /// 武器がEnemy中心を通過しても被弾方向が反転しないようにする。
    /// 現在のゲームプレイ上の左右方向に合わせ、X座標だけを比較する。
    /// </remarks>
    private float ResolveHitSourceDirectionSign()
    {
        // PlayerがEnemyより後から生成された場合にも対応できるよう、
        // 初期取得に失敗している場合だけ命中時に再取得する。
        if (playerTransform == null)
        {
            ResolvePlayerTransform();
        }

        if (playerTransform == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyAttackHitReceiver)}: " +
                $"{PLAYER_TAG} TagのPlayerが見つからないため、" +
                "攻撃方向を正しく判定できません。",
                this);

            return 1f;
        }

        float horizontalOffset =
            playerTransform.position.x -
            transform.position.x;

        if (Mathf.Abs(horizontalOffset) <=
            HIT_SIDE_EPSILON)
        {
            return 1f;
        }

        return Mathf.Sign(horizontalOffset);
    }

    /// <summary>
    /// Player Tagが付いたGameObjectのTransformを取得して保持する。
    /// </summary>
    /// <remarks>
    /// 攻撃のたびに検索せず、一度取得した参照を再利用する。
    /// 左右判定にはこのTransformの位置を使用するため、
    /// Player TagはPlayer本体の位置を表すGameObjectに設定する必要がある。
    /// </remarks>
    private void ResolvePlayerTransform()
    {
        if (playerTransform != null)
        {
            return;
        }

        GameObject playerObject =
            GameObject.FindWithTag(
                PLAYER_TAG);

        if (playerObject == null)
        {
            return;
        }

        playerTransform =
            playerObject.transform;
    }
}
