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
    private EnemyHealth enemyHealth;
    private EnemyStateMachine enemyStateMachine;

    /// <summary>
    /// このEnemyが最後に受け取ったPlayerからの攻撃命中情報。
    /// 一度も受信していない場合はnull。
    /// </summary>
    public PlayerAttackHitData LastPlayerAttackHitData { get; private set; }

    private void Awake()
    {
        enemyHealth =
            GetComponent<EnemyHealth>();

        enemyStateMachine =
            GetComponent<EnemyStateMachine>();
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

        enemyHealth.TakeDamage(
            playerAttackData.damage);

        // TakeDamageによって死亡した場合は、
        // EnemyHealth.Died経由で死亡処理が行われるため
        // Damage Stateには遷移しない。
        if (enemyHealth.IsDead)
        {
            return;
        }

        // EnemyHealthでは0以下のダメージを無効としているため、
        // 実際にダメージが成立しない場合は
        // やられStateにも遷移させない。
        if (playerAttackData.damage <= 0)
        {
            return;
        }

        enemyStateMachine.RequestDamageState();
    }
}
