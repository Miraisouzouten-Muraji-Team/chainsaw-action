using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// プレイヤーの自動回復開始までの時間をImageで表示する。
/// </summary>
public class RecoveryTimeBarController : MonoBehaviour
{
    [Header("参照設定")]
    [Tooltip("プレイヤーのHPを管理するコンポーネント")]
    [SerializeField] private PlayerHealth playerHealth;

    [Tooltip("自動回復の待ち時間を設定したデータ")]
    [SerializeField] private PlayerHealthData healthData;

    [Tooltip("自動回復タイマーを表示するImage")]
    [SerializeField] private Image timerBar;

    private double timerStartTime;
    private bool isCounting;

    private float previousHealth;

    private void Awake()
    {
        if (playerHealth == null ||
            healthData == null ||
            timerBar == null)
        {
            Debug.LogError(
                "AutoRecoveryBarController：PlayerHealth、" +
                "PlayerHealthData、TimerBarを設定してください。",
                this);

            enabled = false;
            return;
        }

        previousHealth = playerHealth.CurrentHealth;

        // 通常時はバーをMAXにする。
        SetFillAmount(1f);
    }

    private void OnEnable()
    {
        if (playerHealth == null)
            return;

        playerHealth.HealthChanged += OnHealthChanged;
        playerHealth.HealthRestored += OnHealthRestored;
    }

    private void OnDisable()
    {
        if (playerHealth == null)
            return;

        playerHealth.HealthChanged -= OnHealthChanged;
        playerHealth.HealthRestored -= OnHealthRestored;
    }

    private void Update()
    {
        if (!isCounting)
            return;

        float recoveryDelay = healthData.RecoveryDelay;

        if (recoveryDelay <= 0f ||
            healthData.RecoveryAmount <= 0f)
        {
            StopTimer();
            return;
        }

        double elapsedTime = Time.timeAsDouble - timerStartTime;

        // 待ち時間に応じて左から右へバーを伸ばす。
        float progress = Mathf.Clamp01(
            (float)(elapsedTime / recoveryDelay));

        SetFillAmount(progress);

        if (progress >= 1f)
            StopTimer();
    }

    private void OnHealthChanged(float currentHealth, float maxHealth)
    {
        if (currentHealth <= 0f ||
            currentHealth >= maxHealth)
        {
            StopTimer();
        }
        else if (currentHealth < previousHealth)
        {
            StartTimer();
        }
        else if (currentHealth > previousHealth)
        {
            StopTimer();
        }

        previousHealth = currentHealth;
    }

    private void OnHealthRestored()
    {
        StopTimer();
        previousHealth = playerHealth.CurrentHealth;
    }

    private void StartTimer()
    {
        if (healthData.RecoveryDelay <= 0f ||
            healthData.RecoveryAmount <= 0f)
        {
            StopTimer();
            return;
        }

        timerStartTime = Time.timeAsDouble;
        isCounting = true;

        // タイマー開始時は0にする。
        SetFillAmount(0f);
    }

    private void StopTimer()
    {
        isCounting = false;
        SetFillAmount(1f);
    }

    private void SetFillAmount(float amount)
    {
        if (timerBar == null)
            return;

        timerBar.fillAmount = Mathf.Clamp01(amount);
    }
}
