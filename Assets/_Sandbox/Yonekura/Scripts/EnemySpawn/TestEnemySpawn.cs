using UnityEngine;
using UnityEngine.InputSystem;

//スポーンギミックのテスト用。
//キー入力で初期配置された敵にダメージを与える。
public class TestEnemySpawn : MonoBehaviour
{
    [Header("テスト対象")]
    [SerializeField]
    private EnemyHealth[] targetEnemies;

    [Header("ダメージ設定")]
    [SerializeField]
    private int damage = 999;

    [Header("キー設定")]
    [SerializeField]
    private Key damageKey = Key.K;

    private void Update()
    {
        //キーボードが接続されていて、指定したキーが押されたら実行
        if (Keyboard.current != null &&
            Keyboard.current[damageKey].wasPressedThisFrame)
        {
            DamageAllEnemies();
        }
    }

    //登録された敵全員にダメージを与える。
    private void DamageAllEnemies()
    {
        foreach (EnemyHealth enemy in targetEnemies)
        {
            if (enemy == null || enemy.IsDead)
            {
                continue;
            }

            enemy.TakeDamage(damage);

            Debug.Log(
                $"{enemy.name} に {damage} ダメージを与えました。",
                enemy);
        }
    }
}
