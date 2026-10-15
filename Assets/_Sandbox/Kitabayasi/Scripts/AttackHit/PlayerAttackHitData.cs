using UnityEngine;

/// <summary>
/// PlayerからEnemyへ渡す、1回の攻撃命中に対応する情報。
/// </summary>
/// <remarks>
/// 通常チェンソー攻撃では、AttackDataと今回の命中時点で確定した
/// ChainsawCutKinematicsを渡す。
/// ChainsawCutKinematicsのBladeCenterWorldは実測値、
/// BladeDirectionWorld / BladeMovementDirectionWorldは
/// 事前生成済みChainsawAttackTrajectoryProfileから解決した値を使用する。
///
/// 食い込み攻撃は通常攻撃用Trajectory Profileへ依存しないため、
/// 必要なCut Kinematicsがない場合はAttackDataだけを渡せる。
///
/// ChainsawTrailは旧Runtime Trajectory方式から段階移行するための互換情報。
/// Player側を新方式へ切り替えて検証が完了した後に削除する。
/// </remarks>
public sealed class PlayerAttackHitData : AttackHitData
{
    /// <summary>
    /// 通常チェンソー攻撃のMeshCutに必要な、今回の命中時点の情報。
    /// 食い込み攻撃など通常切断情報を持たない場合はnull。
    /// </summary>
    public ChainsawCutKinematics? ChainsawCutKinematics { get; }

    /// <summary>
    /// 旧Runtime Trajectory方式との移行互換用データ。
    /// 新方式への移行完了後に削除する。
    /// </summary>
    public ChainsawAttackTrajectory ChainsawTrail { get; }

    public bool HasChainsawCutKinematics =>
        ChainsawCutKinematics.HasValue;

    /// <summary>
    /// 通常攻撃以外など、通常切断用Kinematicsを持たない命中情報を生成する。
    /// </summary>
    public PlayerAttackHitData(
        ScriptableObject attackData)
        : base(attackData)
    {
        ChainsawCutKinematics = null;
        ChainsawTrail = null;
    }

    /// <summary>
    /// 新方式の通常チェンソー命中情報を生成する。
    /// </summary>
    public PlayerAttackHitData(
        ScriptableObject attackData,
        ChainsawCutKinematics chainsawCutKinematics)
        : base(attackData)
    {
        ChainsawCutKinematics =
            chainsawCutKinematics;

        ChainsawTrail = null;
    }

    /// <summary>
    /// 新旧方式を比較する移行期間中の通常チェンソー命中情報を生成する。
    /// </summary>
    public PlayerAttackHitData(
        ScriptableObject attackData,
        ChainsawAttackTrajectory chainsawTrail,
        ChainsawCutKinematics chainsawCutKinematics)
        : base(attackData)
    {
        ChainsawTrail = chainsawTrail;
        ChainsawCutKinematics =
            chainsawCutKinematics;
    }

    /// <summary>
    /// 旧Runtime Trajectory方式からの呼び出しを壊さないための互換Constructor。
    /// Player側の新方式移行完了後に削除する。
    /// </summary>
    public PlayerAttackHitData(
        ScriptableObject attackData,
        ChainsawAttackTrajectory chainsawTrail)
        : base(attackData)
    {
        ChainsawTrail = chainsawTrail;
        ChainsawCutKinematics = null;
    }
}
