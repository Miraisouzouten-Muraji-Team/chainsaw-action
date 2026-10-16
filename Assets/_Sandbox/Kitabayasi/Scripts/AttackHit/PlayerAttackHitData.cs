using System;
using UnityEngine;

/// <summary>
/// PlayerからEnemyへ渡す、1回の攻撃命中に対応する情報。
/// </summary>
/// <remarks>
/// Player側は今回命中したAttackDataを渡すところまでを担当する。
/// Mesh切断用軌跡の取得、Profile選択、切断方向計算、Mesh分割はEnemy側の責務とする。
///
/// 旧Runtime軌跡方式から段階的に移行できるよう、ChainsawAttackTrajectoryを受け取る旧Constructorは
/// 一時的な互換用として残している。新規コードではAttackDataだけを渡すConstructorを使用すること。
/// </remarks>
public sealed class PlayerAttackHitData : AttackHitData
{
    /// <summary>
    /// 旧Runtime軌跡方式との移行互換用。
    /// Enemyの新しい2分割処理では参照しない。
    /// </summary>
    public ChainsawAttackTrajectory ChainsawTrail { get; }

    /// <summary>
    /// 新しい事前Bake方式で使用する命中情報を生成する。
    /// </summary>
    public PlayerAttackHitData(
        ScriptableObject attackData)
        : base(attackData)
    {
        ChainsawTrail = null;
    }

    /// <summary>
    /// 旧Player実装を即座に壊さないための移行用Constructor。
    /// 渡された軌跡は保持するが、新しいEnemy側2分割処理では使用しない。
    /// </summary>
    [Obsolete(
        "Runtime軌跡引数は廃止予定です。" +
        "PlayerAttackHitData(attackData)を使用してください。")]
    public PlayerAttackHitData(
        ScriptableObject attackData,
        ChainsawAttackTrajectory chainsawTrail)
        : base(attackData)
    {
        ChainsawTrail = chainsawTrail;
    }
}
