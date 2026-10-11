using UnityEngine;

/// <summary>
/// PlayerからEnemyへ渡す、1回の攻撃命中に対応する情報。
/// </summary>
/// <remarks>
/// このクラスはGameObjectへアタッチして使用するコンポーネントではない。
/// Player側で攻撃がEnemyへ命中した時点で生成し、
/// 命中対象のIAttackHitReceiver.ReceiveAttackHitへ渡す。
///
/// 基本的な使用手順:
/// 1. 攻撃開始時にChainsawBladeTrajectoryRecorder.BeginRecording()を呼ぶ。
/// 2. 攻撃中、武器のTransform更新タイミングに合わせてRecordSample()を継続的に呼ぶ。
/// 3. Enemyへの命中時にTryCreateTrajectorySnapshot()で、その時点までの軌跡を取得する。
/// 4. 今回使用した攻撃のScriptableObjectと軌跡からPlayerAttackHitDataを生成する。
/// 5. 命中対象のIAttackHitReceiverへReceiveAttackHit()で渡す。
/// 6. 攻撃自体が終了した時点でChainsawBladeTrajectoryRecorder.EndRecording()を呼ぶ。
///
/// 注意:
/// ・命中しただけでは軌跡記録を終了しない。
///   1回の攻撃中に複数のEnemyへ命中する可能性があるため、
///   命中時はTryCreateTrajectorySnapshot()を使用する。
/// ・AttackDataには「今回の命中だけの一時的な状態」を書き込まないこと。
///   ScriptableObjectは参照として共有されるため、基本的には攻撃の固定データとして扱う。
/// </remarks>

public sealed class PlayerAttackHitData : AttackHitData
{
    /// <summary>
    /// 今回のチェンソー攻撃で記録された軌跡データ。
    /// </summary>
    public ChainsawAttackTrajectory ChainsawTrail { get; }

    public PlayerAttackHitData(
        ScriptableObject attackData,
        ChainsawAttackTrajectory chainsawTrail)
        : base(attackData)
    {
        ChainsawTrail = chainsawTrail;
    }
}
