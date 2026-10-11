using System;
using UnityEngine;

/// ギミックを作動させるための「条件」の共通クラス。
///
/// 例：
/// ・敵を全滅させる
/// ・スイッチを押す
/// ・鍵を取得する
/// ・ボスを倒す
///
/// 新しい条件を作る場合は、このクラスを継承
public abstract class GimmickConditionBase : MonoBehaviour
{
    /// この条件が達成済みかどうか。
    public bool IsCompleted { get; private set; }

    /// 条件が達成された瞬間に通知するイベント。
    public event Action<GimmickConditionBase> Completed;

    /// 条件を達成状態にする。
    /// 継承したクラス側から呼び出します。
    /// すでに達成済みの場合は何もしない
    protected void CompleteCondition()
    {
        if (IsCompleted)
        {
            return;
        }

        IsCompleted = true;

        // この条件を監視しているギミックへ通知する。
        Completed?.Invoke(this);
    }
}
