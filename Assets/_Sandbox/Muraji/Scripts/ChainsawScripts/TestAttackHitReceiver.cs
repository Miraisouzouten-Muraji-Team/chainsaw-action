using UnityEngine;

/// <summary>
/// プレイヤーからの攻撃情報を受信し、テスト用HPと受信回数を更新する。
/// </summary>
public class TestAttackHitReceiver : MonoBehaviour, IAttackHitReceiver
{
    [Tooltip("テスト開始時のHPです。4回の命中を確認できるように十分大きな値を設定してください。")]
    [SerializeField, Min(1)] private int initialHealth = 10000;

    [Tooltip("実行中の残りHPです。攻撃データのダメージ分だけ減少します。")]
    [SerializeField] private int currentHealth;

    [Tooltip("有効なプレイヤー攻撃情報を受け取った回数です。")]
    [SerializeField] private int receivedHitCount;

    [Tooltip("最後に受け取った攻撃のダメージです。")]
    [SerializeField] private int lastDamage;

    [Tooltip("最後に受け取った攻撃ベクトルです。ボスへの攻撃ではゼロになることを確認してください。")]
    [SerializeField] private Vector2 lastAttackVector;

    [Tooltip("ダメージ数字を表示するコンポーネントです。未設定なら起動時にシーン内から取得します。")]
    [SerializeField] private DamagePopupPresenter damagePopupPresenter;

    [Tooltip("ダメージ数字を表示する位置です。未設定ならこのオブジェクトの位置を使用します。")]
    [SerializeField] private Transform damagePopupPoint;

    // テスト開始時にHPと受信記録を初期化する。
    private void Awake()
    {
        ResetTest();
        if (damagePopupPresenter == null)
            damagePopupPresenter = FindAnyObjectByType<DamagePopupPresenter>();
    }

    // 攻撃情報を受信し、テスト用HPを減らしてダメージ数字と受信ログを表示する。
    public void ReceiveAttackHit(AttackHitData attackHitData)
    {
        if (!isActiveAndEnabled ||
            !(attackHitData is PlayerAttackHitData playerHit) ||
            !(playerHit.AttackData is AttackData data))
        {
            return;
        }

        lastDamage = Mathf.Max(0, data.damage);
        lastAttackVector = data.attackVector;
        receivedHitCount++;
        int previousHealth = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - lastDamage);
        int actualDamage = previousHealth - currentHealth;
        if (actualDamage > 0 && data.showDamagePopup && damagePopupPresenter != null)
        {
            Vector3 point = damagePopupPoint != null ? damagePopupPoint.position : transform.position;
            damagePopupPresenter.Show(point, actualDamage, data);
        }

        Debug.Log(
            $"攻撃受信テスト {name} / {receivedHitCount}撃目 / " +
            $"ダメージ {lastDamage} / 残りHP {currentHealth} / " +
            $"攻撃ベクトル {lastAttackVector}", this);
    }

    // コンポーネントのメニューからHPと受信記録を初期状態へ戻す。
    [ContextMenu("テストのHPと受信記録をリセット")]
    private void ResetTest()
    {
        currentHealth = Mathf.Max(1, initialHealth);
        receivedHitCount = 0;
        lastDamage = 0;
        lastAttackVector = Vector2.zero;
    }
}
