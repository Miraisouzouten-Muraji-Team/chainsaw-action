using UnityEngine;

/// <summary>
/// HPゲージの表示とダメージアニメーションを管理するクラス。
/// </summary>
public class ArcHPBarController : MonoBehaviour
{
    [Header("ゲージの参照")]
    [SerializeField] private HPBarGraphic hpBar;

    [Header("アニメーション設定")]
    [SerializeField] private float damageFadeDuration = 1.8f;

    [Header("HP管理")]
    [SerializeField] private PlayerHealth playerHealth;

    // 現在HPの割合
    private float currentRatio = 1f;

    // ダメージゲージの終点となるHPの割合
    private float damageEndRatio = 1f;

    // 前回のHPと最大HP
    private float previousHP = -1f;
    private float previousMaxHP = -1f;

    // アニメーション開始時のダメージゲージの位置
    private float damageStartRatio = 1f;

    // アニメーションの経過時間
    private float damageElapsed = 0f;

    private void Start()
    {
        if (playerHealth == null)
        {
            Debug.LogError(
                "PlayerHealthが設定されていません。",
                this);
            return;
        }

        // HP変更イベントを登録
        playerHealth.HealthChanged += OnHealthChanged;

        // 初期HPをゲージに反映
        SetHP(
            playerHealth.CurrentHealth,
            playerHealth.MaxHealth);
    }

    private void OnDestroy()
    {
        // 登録したイベントを解除
        if (playerHealth != null)
        {
            playerHealth.HealthChanged -= OnHealthChanged;
        }
    }

    private void OnHealthChanged(float currentHP, float maxHP)
    {
        // HPが変更されたらゲージを更新
        SetHP(currentHP, maxHP);
    }

    private void Update()
    {
        // ダメージゲージが現在HP以下ならアニメーション不要
        if (damageEndRatio <= currentRatio)
        {
            return;
        }

        damageElapsed += Time.deltaTime;

        // アニメーション時間が0以下にならないようにする
        float duration = Mathf.Max(0.01f, damageFadeDuration);

        // 経過時間からアニメーションの進行度を計算
        float t = Mathf.Clamp01(damageElapsed / duration);

        // ダメージゲージを現在HPの位置まで徐々に移動
        damageEndRatio = Mathf.Lerp(
            damageStartRatio,
            currentRatio,
            t);

        UpdateBars();
    }

    /// <summary>
    /// 現在HPと最大HPをもとに、HPゲージを更新する。
    /// </summary>
    /// <param name="currentHP">現在のHP。</param>
    /// <param name="maxHP">最大HP。</param>
    public void SetHP(float currentHP, float maxHP)
    {
        // 最大HPが0以下ならゲージを空にする
        if (maxHP <= 0f)
        {
            currentRatio = 0f;
            damageEndRatio = 0f;
            damageStartRatio = 0f;

            UpdateBars();
            return;
        }

        // HPを0から最大HPまでの範囲に収める
        float newHP = Mathf.Clamp(currentHP, 0f, maxHP);
        float newRatio = newHP / maxHP;

        // 初回設定、または最大HPが変更された場合
        if (previousHP < 0f || previousMaxHP != maxHP)
        {
            currentRatio = newRatio;
            damageEndRatio = newRatio;
            damageStartRatio = newRatio;
        }
        else if (newHP < previousHP)
        {
            // ダメージを受けた場合
            // 現在のダメージゲージの位置をアニメーション開始位置として保持
            damageStartRatio = Mathf.Max(
                damageEndRatio,
                currentRatio);

            damageEndRatio = damageStartRatio;
            damageElapsed = 0f;

            // HPゲージを新しいHPの位置へ更新
            currentRatio = newRatio;
        }
        else if (newHP > previousHP)
        {
            // 回復した場合はダメージゲージもHPの位置へ戻す
            currentRatio = newRatio;
            damageEndRatio = newRatio;
            damageStartRatio = newRatio;
        }

        // 次回のHP変更判定に使う値を保存
        previousHP = newHP;
        previousMaxHP = maxHP;

        UpdateBars();
    }

    private void UpdateBars()
    {
        if (hpBar == null)
        {
            return;
        }

        // HP本体とダメージゲージの表示割合を反映
        hpBar.SetHPFill(currentRatio);
        hpBar.SetDamageFill(damageEndRatio);
    }
}
