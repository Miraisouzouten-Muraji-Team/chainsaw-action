using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 指定されたEnemyHealthの状態を1本のHPバーとして表示する。
/// </summary>
/// <remarks>
/// 共通Canvas上で生成され、Bindで指定されたEnemyHealthを購読して
/// 現在HPと直前のダメージ量を表示する。
/// HPの保持・変更、対象Enemyの探索、画面上の追従位置管理は担当しない。
/// </remarks>
public class EnemyHealthBar : MonoBehaviour
{
    [Header("参照")]
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

    private EnemyHealth enemyHealth;
    private Coroutine damageDisplayCoroutine;
    private bool isInitialized;
    private bool isSubscribed;

    private void Awake()
    {
        if (currentHealthImage == null || damageHealthImage == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBar)}: HPバー用のImageが設定されていません。",
                this);

            enabled = false;
            return;
        }

        isInitialized = true;
    }

    private void OnEnable()
    {
        if (!isInitialized || enemyHealth == null)
        {
            return;
        }

        SubscribeToHealth();
        SetHealthImmediately();
    }

    private void OnDisable()
    {
        UnsubscribeFromHealth();
        StopDamageDisplayCoroutine();
    }

    /// <summary>
    /// このHPバーに表示するEnemyHealthを設定する。
    /// </summary>
    /// <remarks>
    /// 共通Canvasから生成直後に呼び出す。
    /// HPバーは対象EnemyのTransform階層からEnemyHealthを探索しない。
    /// </remarks>
    public void Bind(EnemyHealth targetHealth)
    {
        if (targetHealth == null)
        {
            Debug.LogError(
                $"{nameof(EnemyHealthBar)}: " +
                $"{nameof(EnemyHealth)} が指定されていません。",
                this);
            return;
        }

        if (ReferenceEquals(enemyHealth, targetHealth))
        {
            if (isActiveAndEnabled && isInitialized)
            {
                SubscribeToHealth();
                SetHealthImmediately();
            }

            return;
        }

        UnsubscribeFromHealth();
        StopDamageDisplayCoroutine();

        enemyHealth = targetHealth;

        if (!isActiveAndEnabled || !isInitialized)
        {
            return;
        }

        SubscribeToHealth();
        SetHealthImmediately();
    }

    private void SubscribeToHealth()
    {
        if (isSubscribed || enemyHealth == null)
        {
            return;
        }

        enemyHealth.HealthChanged += HandleHealthChanged;
        isSubscribed = true;
    }

    private void UnsubscribeFromHealth()
    {
        if (!isSubscribed || enemyHealth == null)
        {
            return;
        }

        enemyHealth.HealthChanged -= HandleHealthChanged;
        isSubscribed = false;
    }

    private void HandleHealthChanged(
        int previousHealth,
        int currentHealth)
    {
        float previousHealthRatio =
            CalculateHealthRatio(previousHealth);

        float currentHealthRatio =
            CalculateHealthRatio(currentHealth);

        currentHealthImage.fillAmount = currentHealthRatio;

        // 赤ゲージをダメージ前のHPまで残し、
        // 緑ゲージとの差分を今回失ったHPとして表示する。
        damageHealthImage.fillAmount = previousHealthRatio;

        StopDamageDisplayCoroutine();

        damageDisplayCoroutine = StartCoroutine(
            HideDamageAfterDelay(currentHealthRatio));
    }

    private IEnumerator HideDamageAfterDelay(
        float currentHealthRatio)
    {
        yield return new WaitForSeconds(damageDisplayDuration);

        damageHealthImage.fillAmount = currentHealthRatio;
        damageDisplayCoroutine = null;
    }

    private void SetHealthImmediately()
    {
        if (enemyHealth == null)
        {
            return;
        }

        float currentHealthRatio =
            CalculateHealthRatio(enemyHealth.CurrentHealth);

        currentHealthImage.fillAmount = currentHealthRatio;
        damageHealthImage.fillAmount = currentHealthRatio;
    }

    private float CalculateHealthRatio(int health)
    {
        return Mathf.Clamp01(
            (float)health / enemyHealth.MaxHealth);
    }

    private void StopDamageDisplayCoroutine()
    {
        if (damageDisplayCoroutine == null)
        {
            return;
        }

        StopCoroutine(damageDisplayCoroutine);
        damageDisplayCoroutine = null;
    }
}
