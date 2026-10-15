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
    private const float HIT_SIDE_EPSILON = 0.001f;

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

        if (enemyHealth.IsDead)
        {
            return;
        }

        if (playerAttackData.damage <= 0)
        {
            return;
        }

        float hitSourceDirectionSign =
            ResolveHitSourceDirectionSign(
                playerAttackHitData);

        enemyStateMachine.RequestDamageState(
            hitSourceDirectionSign);
    }

    /// <summary>
    /// 命中時点のチェンソー刃中心から、
    /// Enemyの左右どちら側から攻撃されたかを求める。
    /// </summary>
    /// <remarks>
    /// 新方式ではPlayerから渡されたBladeCenterWorldを使用する。
    /// 旧Runtime Trajectoryしか渡されていない場合だけ移行互換として
    /// ChainsawTrailの最終サンプルへフォールバックする。
    /// 既存実装と同様、左右判定軸としてWorld X座標だけを使う。
    /// 移動平面のもう一軸や奥行き方向はこの判定には使用しない。
    /// </remarks>
    private float ResolveHitSourceDirectionSign(
        PlayerAttackHitData playerAttackHitData)
    {
        if (playerAttackHitData.ChainsawCutKinematics.HasValue)
        {
            return ResolveHorizontalDirectionSign(
                playerAttackHitData
                    .ChainsawCutKinematics
                    .Value
                    .BladeCenterWorld.x);
        }

        ChainsawAttackTrajectory chainsawTrail =
            playerAttackHitData.ChainsawTrail;

        if (chainsawTrail == null ||
            chainsawTrail.SampleCount == 0)
        {
            return 1f;
        }

        ChainsawBladeTrajectorySample latestSample =
            chainsawTrail.Samples[
                chainsawTrail.SampleCount - 1];

        float legacyBladeCenterX =
            (latestSample.TipPosition.x +
             latestSample.RootPosition.x) * 0.5f;

        return ResolveHorizontalDirectionSign(
            legacyBladeCenterX);
    }

    private float ResolveHorizontalDirectionSign(
        float hitSourceWorldX)
    {
        float horizontalOffset =
            hitSourceWorldX - transform.position.x;

        if (Mathf.Abs(horizontalOffset) <=
            HIT_SIDE_EPSILON)
        {
            return 1f;
        }

        return Mathf.Sign(horizontalOffset);
    }
}
