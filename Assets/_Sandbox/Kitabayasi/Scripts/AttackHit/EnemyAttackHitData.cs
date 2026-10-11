using UnityEngine;

/// <summary>
/// EnemyからPlayerへ渡す攻撃命中情報。
/// </summary>
public sealed class EnemyAttackHitData : AttackHitData
{
    /// <summary>
    /// 今回Playerへ与える最終ダメージ量。
    /// </summary>
    public float Damage { get; }

    public EnemyAttackHitData(
        ScriptableObject attackData,
        float damage)
        : base(attackData)
    {
        Damage = Mathf.Max(
            0f,
            damage);
    }
}
