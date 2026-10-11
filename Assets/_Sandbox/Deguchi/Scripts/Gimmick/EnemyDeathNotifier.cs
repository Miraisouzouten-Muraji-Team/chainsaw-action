using System;
using UnityEngine;

/// 敵が死亡したことを他のシステムへ通知するためのコンポーネント。
/// 敵本体のHP管理とは分離しておくことで、
/// 敵スクリプトの構造が変わってもギミック側への影響を減らします。
public class EnemyDeathNotifier : MonoBehaviour
{
    /// この敵が死亡済みかどうか。
    public bool IsDead { get; private set; }

    /// この敵が死亡したときに通知されるイベント。
    public event Action<EnemyDeathNotifier> Died;

    /// 敵が死亡したときに呼び出してください。
    /// 二重に呼ばれても死亡通知は1回しか発生しません。
    public void NotifyDied()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;

        // この敵の死亡を監視しているシステムへ通知する。
        Died?.Invoke(this);
    }
}
