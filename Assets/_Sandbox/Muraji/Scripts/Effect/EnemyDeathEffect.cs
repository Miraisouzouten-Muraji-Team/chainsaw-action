using UnityEngine;

/// <summary>敵の死亡通知を受け取り、敵の位置に撃破エフェクトを生成します。</summary>
[DisallowMultipleComponent]
public sealed class EnemyDeathEffect : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("死亡を監視する敵のHP管理です。未指定なら、このオブジェクトと親から取得します。生成位置には、このコンポーネントが付いている敵の座標を使います。")]
    [SerializeField] private EnemyHealth enemyHealth;

    [Tooltip("敵を倒したときに生成するエフェクトのプレハブです。パーティクルは自動再生をオンにしてください。")]
    [SerializeField] private GameObject deathEffectPrefab;

    [Header("エフェクトの寿命")]
    [Tooltip("生成したエフェクトを削除するまでの秒数です。最後のパーティクルが消えるまで再生できる長さにしてください。")]
    [SerializeField, Min(0.01f)] private float effectLifetime = 5f;

    private bool effectSpawned;

    /// <summary>敵のHP管理を取得して、死亡通知の受信を開始します。</summary>
    private void Awake()
    {
        if (enemyHealth == null)
            enemyHealth = GetComponentInParent<EnemyHealth>();

        if (enemyHealth == null)
        {
            Debug.LogError("EnemyDeathEffect：監視するEnemyHealthを設定してください。", this);
            enabled = false;
            return;
        }

        // 死亡処理中に敵が無効化されても通知を受け取れるよう、破棄時まで購読します。
        enemyHealth.Died += SpawnDeathEffect;
    }

    /// <summary>死亡した敵の現在位置に、一度だけ独立したエフェクトを生成します。</summary>
    private void SpawnDeathEffect()
    {
        if (effectSpawned || enemyHealth == null) return;
        effectSpawned = true;

        if (deathEffectPrefab == null) return;

        // 敵の子にしないことで、敵が削除されてもエフェクトの再生を継続します。
        // 位置は敵の座標、回転とスケールはエフェクトのプレハブ設定を使用します。
        GameObject effect = Instantiate(
            deathEffectPrefab,
            enemyHealth.transform.position,
            deathEffectPrefab.transform.rotation);

        Destroy(effect, Mathf.Max(0.01f, effectLifetime));
    }

    /// <summary>コンポーネント破棄時に死亡通知の購読を解除します。</summary>
    private void OnDestroy()
    {
        if (enemyHealth != null)
            enemyHealth.Died -= SpawnDeathEffect;
    }
}
