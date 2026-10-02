using UnityEngine;

/// <summary>
/// Enemyが外部から攻撃命中情報を受信するための窓口。
/// </summary>
/// <remarks>
/// IAttackHitReceiverを実装し、
/// Playerから受け取ったPlayerAttackHitDataを
/// Enemy側の各機能へ渡す。
///
/// 現在はPlayer側の攻撃情報実装反映待ちのため、
/// 攻撃命中情報の受信のみ行う。
/// </remarks>
[RequireComponent(typeof(EnemyHealth))]
public sealed class EnemyAttackHitReceiver :
    MonoBehaviour,
    IAttackHitReceiver
{
    private EnemyHealth enemyHealth;
    private EnemyDamageFlash enemyDamageFlash;

    private void Awake()
    {
        enemyHealth = GetComponent<EnemyHealth>();
        enemyDamageFlash = GetComponent<EnemyDamageFlash>();
    }

    /// <summary>
    /// 外部から攻撃命中情報を受け取る。
    /// </summary>
    public void ReceiveAttackHit(AttackHitData attackHitData)
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

        if (attackHitData is not PlayerAttackHitData playerAttackHitData)
        {
            Debug.LogWarning(
                $"{nameof(EnemyAttackHitReceiver)}: " +
                $"未対応の攻撃命中情報を受信しました。 " +
                $"Type: {attackHitData.GetType().Name}",
                this);

            return;
        }

        // Player側の最新PlayerAttackHitDataを反映後、
        // 受信した攻撃情報をEnemyHealth・ダメージ演出・
        // メッシュ切断等へ渡す処理をここに追加する。
    }
}
