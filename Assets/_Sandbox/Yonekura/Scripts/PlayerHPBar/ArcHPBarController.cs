using UnityEngine;

// HPゲージの表示とダメージアニメーションを管理
public class ArcHPBarController : MonoBehaviour
{
    [Header("ゲージの参照")]
    [SerializeField] private ArcHPBarGraphic background;
    [SerializeField] private ArcHPBarGraphic greenBar;
    [SerializeField] private ArcHPBarGraphic damageBar;

    [Header("アニメーション設定")]
    [SerializeField] private float damageFadeDuration = 1.8f;

    [Header("テスト用HP")]
    [SerializeField] private float testMaxHP = 100f;
    [SerializeField] private float testCurrentHP = 100f;

    // 現在HPの割合
    private float currentRatio = 1f;

    // 赤ゲージの終点。ダメージ前のHP位置を保持する
    private float damageEndRatio = 1f;

    private float previousHP = -1f;
    private float previousMaxHP = -1f;

    // アニメーション開始時の赤ゲージの終点
    private float damageStartRatio = 1f;

    // アニメーションの経過時間
    private float damageElapsed = 0f;

    private void Start()
    {
        // 初期HPをゲージに反映
        SetHP(testCurrentHP, testMaxHP);
    }

    private void Update()
    {
        if (damageEndRatio <= currentRatio)
            return;

        // アニメーションの経過時間を加算
        damageElapsed += Time.deltaTime;

        // 0～1の範囲に変換
        float duration = Mathf.Max(0.01f, damageFadeDuration);
        float t = Mathf.Clamp01(damageElapsed / duration);

        // 赤ゲージを一定時間かけて縮める
        damageEndRatio = Mathf.Lerp(
            damageStartRatio,
            currentRatio,
            t
        );

        UpdateDamageBar();
    }

    // 外部のHP管理スクリプトから呼び出す
    public void SetHP(float currentHP, float maxHP)
    {
        if (maxHP <= 0f)
        {
            currentRatio = 0f;
            damageEndRatio = 0f;
            UpdateBars();
            return;
        }

        float newHP = Mathf.Clamp(currentHP, 0f, maxHP);
        float newRatio = newHP / maxHP;

        // 初回設定、または最大HPの変更時
        if (previousHP < 0f || previousMaxHP != maxHP)
        {
            currentRatio = newRatio;
            damageEndRatio = newRatio;

            damageStartRatio = newRatio;
        }
        else if (newHP < previousHP)
        {
            // ダメージを受ける前のHP位置を保存
            damageStartRatio = currentRatio;
            damageEndRatio = currentRatio;

            // アニメーションを最初から開始
            damageElapsed = 0f;

            // 緑ゲージを新しいHPまで減らす
            currentRatio = newRatio;
        }
        else if (newHP > previousHP)
        {
            // 回復時：赤ゲージを回復後のHP位置まで戻す
            currentRatio = newRatio;
            damageEndRatio = newRatio;

            damageStartRatio = newRatio;
        }

        previousHP = newHP;
        previousMaxHP = maxHP;

        UpdateBars();
    }

    private void UpdateBars()
    {
        if (background != null)
            background.SetRange(0f, 1f);

        if (greenBar != null)
            greenBar.SetRange(0f, currentRatio);

        UpdateDamageBar();
    }

    private void UpdateDamageBar()
    {
        if (damageBar != null)
            damageBar.SetRange(currentRatio, damageEndRatio);
    }

    // Inspectorのコンテキストメニューから動作をテスト
    [ContextMenu("テスト：20ダメージ")]
    private void TestDamage()
    {
        SetHP(testCurrentHP - 20f, testMaxHP);
        testCurrentHP = Mathf.Max(0f, testCurrentHP - 20f);
    }

    [ContextMenu("テスト：HP全回復")]
    private void TestHeal()
    {
        testCurrentHP = testMaxHP;
        SetHP(testCurrentHP, testMaxHP);
    }
}
