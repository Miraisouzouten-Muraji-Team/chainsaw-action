using UnityEngine;

/// <summary>
/// 1回の攻撃命中で、攻撃側から受信側へ渡す情報の基底データ。
/// </summary>
public abstract class AttackHitData
{
    /// <summary>
    /// 今回命中した攻撃の固定データを保持するScriptableObject。
    /// </summary>
    public ScriptableObject AttackData { get; }

    protected AttackHitData(ScriptableObject attackData)
    {
        AttackData = attackData;
    }
}
