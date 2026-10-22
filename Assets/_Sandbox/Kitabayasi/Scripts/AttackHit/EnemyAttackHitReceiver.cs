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
    /// このEnemyが最後に受け取ったPlayerからの攻撃命中情報。
    /// 一度も受信していない場合はnull。
    /// </summary>
    public PlayerAttackHitData LastPlayerAttackHitData { get; private set; }

    /// <summary>
    /// 最後に受けたPlayer攻撃について、左右判定まで反映した実ノックバック初速度。
    /// </summary>
    /// <remarks>
    /// EnemyHealth.TakeDamage内で死亡通知が同期的に発火するため、
    /// 死亡処理から参照できるようダメージ適用前に確定して保持する。
    /// </remarks>
    public Vector2 LastResolvedKnockbackVelocity { get; private set; }

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
        if (!isActiveAndEnabled)
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

        // State遷移より先に保存する。
        // Damage Stateやその他の処理から
        // 今回の命中情報を参照できるようにする。
        LastPlayerAttackHitData =
            playerAttackHitData;

        if (playerAttackHitData.AttackData
            is not AttackData playerAttackData)
        {
            Debug.LogWarning(
                $"{nameof(EnemyAttackHitReceiver)}: " +
                "PlayerAttackHitDataに設定されたAttackDataが " +
                $"{nameof(AttackData)}ではありません。",
                this);

            return;
        }

        // EnemyHealthでは0以下のダメージを無効としているため、
        // 実際にダメージが成立しない場合はノックバック情報も更新しない。
        if (playerAttackData.damage <= 0)
        {
            return;
        }

        float hitSourceDirectionSign =
            ResolveHitSourceDirectionSign();

        LastResolvedKnockbackVelocity =
            EnemyKnockbackMotor
                .CalculateInitialKnockbackVelocity(
                    playerAttackData.attackVector,
                    hitSourceDirectionSign);

        // TakeDamage内でDiedが同期通知されるため、
        // 死亡攻撃の方向をDeath Strategyから参照できる状態にしてからHPへ反映する。
        enemyHealth.TakeDamage(
            playerAttackData.damage);

        // TakeDamageによって死亡した場合は、
        // EnemyHealth.Died経由で死亡処理が行われるため
        // Damage Stateには遷移しない。
        if (enemyHealth.IsDead)
        {
            return;
        }

        enemyStateMachine.RequestDamageState(
            hitSourceDirectionSign,
            playerAttackData.attackVector);
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

        resolvedPlayerTransform =
            playerTransform;

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
