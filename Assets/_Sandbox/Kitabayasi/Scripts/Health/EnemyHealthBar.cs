using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EnemyHealthの状態をHPバーとして表示する。
/// </summary>
/// <remarks>
/// 現在HP、直前のダメージ量、失われたHPの視覚表現を担当する。
/// HPの保持・変更やダメージ計算は担当しない。
/// </remarks>
public class EnemyHealthBar : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("表示対象となるEnemyHealth。")]
    [SerializeField]
    private EnemyHealth enemyHealth;

    [Tooltip("現在HPを表示する緑色のImage。")]
    [SerializeField]
    private Image currentHealthImage;

    [Tooltip("直前のダメージ量を表示する赤色のImage。")]
    [SerializeField]
    private Image damageHealthImage;

    [Header("ダメージ表示")]
    [Tooltip("直前に受けたダメージを赤色で表示しておく時間。")]
    [SerializeField, Min(0f)]
    private float damageDisplayDuration = 0.5f;

    // ダメージ表示用のコルーチンを保持するための変数
    private Coroutine damageDisplayCoroutine;

    private void Awake()
    {
        if (enemyHealth == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBar)}: {nameof(EnemyHealth)} が設定されていません。",
                this);

            enabled = false;
            return;
        }

        if (currentHealthImage == null || damageHealthImage == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBar)}: HPバー用のImageが設定されていません。",
                this);

            enabled = false;
        }
    }

    private void OnEnable()
    {
        // ハンドルの登録
        enemyHealth.HealthChanged += HandleHealthChanged;

        SetHealthImmediately();
    }

    private void OnDisable()
    {
        // HP変更通知を受け取らないよう、イベントハンドラーを解除する。
        if (enemyHealth != null)
        {
            enemyHealth.HealthChanged -= HandleHealthChanged;
        }

        // 無効化後にCoroutineの続きが実行されないよう停止する。
        if (damageDisplayCoroutine != null)
        {
            StopCoroutine(damageDisplayCoroutine);
            damageDisplayCoroutine = null;
        }
    }

    // EnemyHealthからHP変更通知を受け取ったときのHPバー更新処理。
    private void HandleHealthChanged(int previousHealth, int currentHealth)
    {
        float previousHealthRatio = CalculateHealthRatio(previousHealth);
        float currentHealthRatio  = CalculateHealthRatio(currentHealth);

        // 現在HPはダメージを受けた瞬間に減らす。
        currentHealthImage.fillAmount = currentHealthRatio;

        // 赤ゲージをダメージ前のHPまで残すことで、
        // 緑ゲージとの差分を「今回失ったHP」として表示する。
        damageHealthImage.fillAmount = previousHealthRatio;

        // 連続ダメージ時は古い表示終了処理を止め、
        // 最新のダメージから表示時間を数え直す。
        if (damageDisplayCoroutine != null)
        {
            StopCoroutine(damageDisplayCoroutine);
        }

        damageDisplayCoroutine = StartCoroutine(
            HideDamageAfterDelay(currentHealthRatio));
    }

    // ダメージ表示時間の経過後、赤ゲージを現在HPまで減らす。
    private IEnumerator HideDamageAfterDelay(float currentHealthRatio)
    {
        yield return new WaitForSeconds(damageDisplayDuration);

        // 赤い部分を現在HPまで減らすことで、
        // 背面にある灰色のバーを表示する。
        damageHealthImage.fillAmount = currentHealthRatio;

        damageDisplayCoroutine = null;
    }

    // 現在のHPを待ち時間や演出なしでHPバーへ反映する。
    private void SetHealthImmediately()
    {
        float currentHealthRatio =
            CalculateHealthRatio(enemyHealth.CurrentHealth);

        currentHealthImage.fillAmount = currentHealthRatio;
        damageHealthImage.fillAmount = currentHealthRatio;
    }

    // HPをImage.fillAmountで使用できる0～1の割合へ変換する。
    private float CalculateHealthRatio(int health)
    {
        return Mathf.Clamp01(
            (float)health / enemyHealth.MaxHealth);
    }
}
