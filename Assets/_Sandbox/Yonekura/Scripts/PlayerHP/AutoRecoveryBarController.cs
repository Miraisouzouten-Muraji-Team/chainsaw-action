using UnityEngine;

/// <summary>
/// プレイヤーの自動回復開始までの時間をバーで表示する。
/// </summary>
public class AutoRecoveryBarController : MonoBehaviour
{
    [Header("参照設定")]
    [Tooltip("プレイヤーのHPを管理するコンポーネント")]
    [SerializeField] private PlayerHealth playerHealth;

    [Tooltip("自動回復の待ち時間を設定したデータ")]
    [SerializeField] private PlayerHealthData healthData;

    [Tooltip("回復タイマーを表示するバー")]
    [SerializeField] private AutoRecoveryBarGraphic timerBar;

    // タイマーの開始時刻と計測状態
    private double timerStartTime;
    private bool isCounting;

    // 前回のHPを記録し、ダメージと回復を判別する
    private float previousHealth;

    private void Awake()
    {
        if (playerHealth == null ||
            healthData == null ||
            timerBar == null)
        {
            Debug.LogError(
                "AutoRecoveryBarController：Player Health、" +
                "Health Data、Timer Barを設定してください。",
                this);

            enabled = false;
            return;
        }

        previousHealth = playerHealth.CurrentHealth;

        // 通常時はバーをMAXにする
        timerBar.SetHPFill(1f);
    }

    private void OnEnable()
    {
        if (playerHealth == null)
            return;

        // HPの変化と全回復を監視する
        playerHealth.HealthChanged += OnHealthChanged;
        playerHealth.HealthRestored += OnHealthRestored;
    }

    private void OnDisable()
    {
        if (playerHealth == null)
            return;

        // イベントの二重登録を防ぐため解除する
        playerHealth.HealthChanged -= OnHealthChanged;
        playerHealth.HealthRestored -= OnHealthRestored;
    }

    private void Update()
    {
        if (!isCounting)
            return;

        float recoveryDelay = healthData.RecoveryDelay;

        // 自動回復が無効、または待ち時間が不要なら終了する
        if (recoveryDelay <= 0f ||
            healthData.RecoveryAmount <= 0f)
        {
            StopTimer();
            return;
        }

        // ゲーム内時間から経過時間を計算する
        double elapsedTime = Time.timeAsDouble - timerStartTime;

        // 待ち時間に応じて0から1までバーを伸ばす
        float progress = Mathf.Clamp01(
            (float)(elapsedTime / recoveryDelay));

        timerBar.SetHPFill(progress);

        // 自動回復の開始時刻に達したらMAXにする
        if (progress >= 1f)
            StopTimer();
    }

    private void OnHealthChanged(float currentHealth, float maxHealth)
    {
        if (currentHealth <= 0f ||
            currentHealth >= maxHealth)
        {
            // 死亡時またはHPが最大になったときは終了する
            StopTimer();
        }
        else if (currentHealth < previousHealth)
        {
            // ダメージを受けたら待ち時間をリセットする
            StartTimer();
        }
        else if (currentHealth > previousHealth)
        {
            // 自動回復が始まったらタイマーを終了する
            StopTimer();
        }

        previousHealth = currentHealth;
    }

    private void OnHealthRestored()
    {
        // リスポーンなどによる全回復時も終了する
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

        // 計測開始時はバーを0にする
        timerBar.SetHPFill(0f);
    }

    private void StopTimer()
    {
        isCounting = false;

        // 計測終了時はバーをMAXに戻す
        if (timerBar != null)
            timerBar.SetHPFill(1f);
    }
}
