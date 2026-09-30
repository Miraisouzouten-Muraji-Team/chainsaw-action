using UnityEngine;

/// <summary>
/// EnemyからPlayerへ渡す攻撃命中情報。
/// </summary>
public sealed class EnemyAttackHitData : AttackHitData
{
    public EnemyAttackHitData(ScriptableObject attackData)
        : base(attackData)
    {
    }
}
