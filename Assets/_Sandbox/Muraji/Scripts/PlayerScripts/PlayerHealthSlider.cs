using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Slider))]
public sealed class PlayerHealthSlider : MonoBehaviour
{
    [Tooltip("HPを表示するプレイヤーのPlayerHealthを登録してください。")]
    [SerializeField] private PlayerHealth playerHealth;

    private Slider healthSlider;
    private bool waitingForInitialization;

    // 同じオブジェクトのSliderを取得し、表示専用に設定する。
    private void Awake()
    {
        healthSlider = GetComponent<Slider>();
        healthSlider.interactable = false;
        healthSlider.wholeNumbers = false;
        healthSlider.minValue = 0f;
        healthSlider.maxValue = 1f;

        if (playerHealth == null)
        {
            Debug.LogError("PlayerHealthSlider：Player Healthにプレイヤーを登録してください。", this);
            enabled = false;
        }
    }

    // HP変更通知を購読し、再表示時も現在のHPへ合わせる。
    private void OnEnable()
    {
        if (playerHealth == null) return;

        playerHealth.HealthChanged += RefreshSlider;
        waitingForInitialization = playerHealth.MaxHealth <= 0f;
        RefreshSlider(playerHealth.CurrentHealth, playerHealth.MaxHealth);
    }

    // 初期化順序によりHPが未設定の場合だけ待ち、初期値を反映する。
    private void FixedUpdate()
    {
        if (!waitingForInitialization || playerHealth == null ||
            playerHealth.MaxHealth <= 0f) return;

        RefreshSlider(playerHealth.CurrentHealth, playerHealth.MaxHealth);
    }

    // 非表示・無効化時に通知の購読を解除する。
    private void OnDisable()
    {
        if (playerHealth != null)
            playerHealth.HealthChanged -= RefreshSlider;
    }

    // 現在HPを0～1の割合へ変換し、Sliderの操作イベントを発生させず反映する。
    private void RefreshSlider(float currentHealth, float maxHealth)
    {
        if (maxHealth <= 0f)
        {
            healthSlider.SetValueWithoutNotify(0f);
            return;
        }

        waitingForInitialization = false;
        healthSlider.SetValueWithoutNotify(Mathf.Clamp01(currentHealth / maxHealth));
    }
}
