using UnityEngine;

/// <summary>
/// ギミック動作確認用の仮スクリプト。
///
/// キーボード入力でEnemyDeathNotifierへ死亡通知を送ります。
/// 本番では使用しません。
/// </summary>
public class TestEnemyKill : MonoBehaviour
{
    [SerializeField]
    private EnemyDeathNotifier enemyDeathNotifier;

    [SerializeField]
    private KeyCode killKey = KeyCode.Alpha1;

    private void Update()
    {
        // 指定したキーが押されたら、
        // このEnemyを死亡扱いにする。
        if (Input.GetKeyDown(killKey))
        {
            enemyDeathNotifier.NotifyDied();

            Debug.Log($"{gameObject.name} を倒しました。");
        }
    }
}
