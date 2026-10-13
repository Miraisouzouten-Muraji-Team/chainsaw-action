using UnityEngine;

/// <summary>
/// 敵からの攻撃命中情報を受け取り、プレイヤーのHPへ反映する。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerHealth))]
public sealed class PlayerAttackHitReceiver :
    MonoBehaviour,
    IAttackHitReceiver
{
    [Tooltip(
        "回避中にダメージを受けないよう、プレイヤーの操作コンポーネントを指定します。" +
        "未設定の場合は同じオブジェクトから取得します。")]
    [SerializeField]
    private SensorPlayerController playerController;

    private PlayerHealth playerHealth;

    /// <summary>
    /// 同じオブジェクトのHP管理と、未設定の操作コンポーネントを取得する。
    /// </summary>
    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();

        if (playerController == null)
        {
            playerController =
                GetComponent<SensorPlayerController>();
        }
    }

    /// <summary>
    /// 敵の命中情報を確認し、受けられる状態なら最終ダメージをHPへ適用する。
    /// </summary>
    public void ReceiveAttackHit(AttackHitData attackHitData)
    {
        if (!isActiveAndEnabled ||
            playerHealth == null ||
            !playerHealth.isActiveAndEnabled ||
            playerHealth.IsDead)
        {
            return;
        }

        // 敵からの攻撃情報だけを処理する。
        if (attackHitData is not EnemyAttackHitData enemyAttackHitData)
        {
            Debug.LogWarning(
                "PlayerAttackHitReceiver：敵の攻撃命中情報ではありません。",
                this);
            return;
        }

        // 既存の回避判定がある場合は、その判定を優先する。
        if (playerController != null &&
            !playerController.CanTakeDamage)
        {
            return;
        }

        // 最終ダメージをそのまま渡す。
        // 無効な数値の除外、HP変更通知、死亡通知はPlayerHealthが担当する。
        playerHealth.TakeDamage(enemyAttackHitData.Damage);
    }
}
