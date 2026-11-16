using System;
using UnityEngine;

/// <summary>
/// PlayerからEnemyへ渡す、1回の攻撃命中に対応する情報。
/// </summary>
/// <remarks>
/// Player側は今回命中したAttackDataと、命中時点で確定したチェンソー回転率を渡すところまでを担当する。
/// Mesh切断用軌跡の取得、Profile選択、切断方向計算、Mesh分割はEnemy側の責務とする。
///
/// 旧Runtime軌跡方式から段階的に移行できるよう、ChainsawAttackTrajectoryを受け取る旧Constructorは
/// 一時的な互換用として残している。
/// </remarks>
public sealed class PlayerAttackHitData : AttackHitData
{
    /// <summary>
    /// 旧Runtime軌跡方式との移行互換用。
    /// Enemyの新しい2分割処理では参照しない。
    /// </summary>
    public ChainsawAttackTrajectory ChainsawTrail { get; }

    /// <summary>
    /// 攻撃が命中した瞬間のチェンソー回転率。
    /// 最低回転を0、最大回転を1とする。
    /// </summary>
    public float ChainsawRotationRatio { get; }

    /// <summary>
    /// 命中時のチェンソー回転率がPlayer側から明示的に渡されているか。
    /// </summary>
    /// <remarks>
    /// 既存Player実装との移行中に、旧Constructor経由の命中と区別するために使用する。
    /// </remarks>
    public bool HasChainsawRotationRatio { get; }

    /// <summary>
    /// 命中ごとのダメージ上書き値。nullならAttackData.damageを使用する。
    /// 共有AttackDataを複製・変更せずに倍率ダメージを渡すために使用する。
    /// </summary>
    public int? DamageOverride { get; }

    /// <summary>
    /// 命中ごとの攻撃ベクトル上書き値。nullならAttackData.attackVectorを使用する。
    /// 食い込み中など、ノックバックを抑制する命中で使用する。
    /// </summary>
    public Vector2? AttackVectorOverride { get; }

    /// <summary>
    /// 既存Player実装との互換用。
    /// </summary>
    /// <remarks>
    /// このConstructorでは命中時のチェンソー回転率を保持できない。
    /// 新規コードでは回転率を受け取るConstructorを使用すること。
    /// </remarks>
    public PlayerAttackHitData(
        ScriptableObject attackData)
        : base(attackData)
    {
        ChainsawTrail = null;
        ChainsawRotationRatio = 0f;
        HasChainsawRotationRatio = false;
    }

    /// <summary>
    /// 攻撃データと、命中時点で確定したチェンソー回転率を保持する命中情報を生成する。
    /// </summary>
    /// <param name="chainsawRotationRatio">
    /// ChainsawAccelerator.PowerRatioなど、最低回転を0、最大回転を1へ正規化した命中時点の値。
    /// 範囲外の値は0～1へ補正して保持する。
    /// </param>
    public PlayerAttackHitData(
        ScriptableObject attackData,
        float chainsawRotationRatio)
        : base(attackData)
    {
        ChainsawTrail = null;
        ChainsawRotationRatio =
            Mathf.Clamp01(
                chainsawRotationRatio);
        HasChainsawRotationRatio = true;
    }

    /// <summary>
    /// 命中の識別用に元AttackDataを保持し、倍率反映後のダメージ・攻撃ベクトルを別に渡す。
    /// </summary>
    public PlayerAttackHitData(
        AttackData attackData,
        int hitDamage,
        Vector2 hitAttackVector)
        : base(attackData)
    {
        ChainsawTrail = null;
        ChainsawRotationRatio = 0f;
        HasChainsawRotationRatio = false;
        DamageOverride = Mathf.Max(0, hitDamage);
        AttackVectorOverride = hitAttackVector;
    }

    /// <summary>
    /// 旧Player実装を即座に壊さないための移行用Constructor。
    /// 渡された軌跡は保持するが、新しいEnemy側2分割処理では使用しない。
    /// </summary>
    [Obsolete(
        "Runtime軌跡引数は廃止予定です。" +
        "PlayerAttackHitData(attackData, chainsawRotationRatio)を使用してください。")]
    public PlayerAttackHitData(
        ScriptableObject attackData,
        ChainsawAttackTrajectory chainsawTrail)
        : base(attackData)
    {
        ChainsawTrail = chainsawTrail;
        ChainsawRotationRatio = 0f;
        HasChainsawRotationRatio = false;
    }
}
