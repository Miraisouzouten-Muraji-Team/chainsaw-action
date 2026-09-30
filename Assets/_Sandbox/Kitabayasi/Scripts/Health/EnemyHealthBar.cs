using System;
using System.Threading;
using Cysharp.Threading.Tasks;
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

    [Tooltip("赤ゲージが1秒間に減少する割合。1ならHPバー全体を1秒で減少する速度。")]
    [SerializeField, Min(0.01f)]
    private float damageDecreaseSpeed = 1f;

    // ダメージ表示の待機・減少処理をキャンセルするために使用する。
    private CancellationTokenSource damageDisplayCts;

    // 赤ゲージが最終的に到達するHP割合。
    // 追加ダメージを受けた場合は、この値だけを最新HPへ更新する。
    private float targetDamageHealthRatio;

    // 赤ゲージが現在滑らかに減少しているかを示す。
    // 減少中の追加ダメージでは処理を再開始せず、
    // 現在位置から新しい目標HPへそのまま減少させるために使用する。
    private bool isDamageGaugeDecreasing;

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

        // 無効化後にUniTaskの続きが実行されないよう、
        // 実行中のダメージ表示処理をキャンセルする。
        CancelDamageDisplay();
    }

    // EnemyHealthからHP変更通知を受け取ったときのHPバー更新処理。
    private void HandleHealthChanged(int previousHealth, int currentHealth)
    {
        float currentHealthRatio = CalculateHealthRatio(currentHealth);

        // 現在HPはダメージを受けた瞬間に減らす。
        currentHealthImage.fillAmount = currentHealthRatio;

        // HPが増加した場合はダメージ表示演出を行わず、
        // 緑・赤ゲージの両方を現在HPへ即座に合わせる。
        if (currentHealth >= previousHealth)
        {
            CancelDamageDisplay();

            targetDamageHealthRatio = currentHealthRatio;
            damageHealthImage.fillAmount = currentHealthRatio;

            return;
        }

        // 赤ゲージが最終的に到達する位置を、
        // 最新の現在HPへ更新する。
        targetDamageHealthRatio = currentHealthRatio;

        // 赤ゲージが既に減少中の場合は処理を再開始しない。
        // 現在の赤ゲージ位置から、更新された最新HPへそのまま減少を続ける。
        if (isDamageGaugeDecreasing)
        {
            return;
        }

        // まだ減少開始前の待機中に追加ダメージを受けた場合は、
        // 古い待機処理を止め、最新のダメージから表示時間を数え直す。
        CancelDamageDisplay();

        damageDisplayCts = new CancellationTokenSource();

        UpdateDamageGaugeAsync(damageDisplayCts).Forget();
    }

    // 一定時間ダメージ量を表示した後、
    // 赤ゲージを最新の現在HPまで滑らかに減少させる。
    private async UniTask UpdateDamageGaugeAsync(
        CancellationTokenSource cts)
    {
        // 元のWaitForSecondsと同様にTime.timeScaleの影響を受ける時間で待機する。
        // 待機中に新しいダメージを受けた場合や、
        // GameObjectが無効化された場合はキャンセルされる。
        bool isCanceled = await UniTask.Delay(
                TimeSpan.FromSeconds(damageDisplayDuration),
                ignoreTimeScale: false,
                cancellationToken: cts.Token)
            .SuppressCancellationThrow();

        // キャンセルされた処理、または既に新しい表示処理へ
        // 切り替わっている場合は以降の処理を行わない。
        if (isCanceled ||
            !ReferenceEquals(damageDisplayCts, cts))
        {
            return;
        }

        isDamageGaugeDecreasing = true;

        // 赤ゲージの現在位置から最新HPまで毎フレーム少しずつ減らす。
        // 減少中に追加ダメージを受けた場合は、
        // targetDamageHealthRatioが更新されるため、
        // その時点の赤ゲージ位置から新しい目標へそのまま減少を続ける。
        while (damageHealthImage.fillAmount > targetDamageHealthRatio)
        {
            damageHealthImage.fillAmount = Mathf.MoveTowards(
                damageHealthImage.fillAmount,
                targetDamageHealthRatio,
                damageDecreaseSpeed * Time.deltaTime);

            // 次のフレームまで待機する。
            // GameObjectの無効化などでキャンセルされた場合は処理を終了する。
            isCanceled = await UniTask.NextFrame(
                    cts.Token)
                .SuppressCancellationThrow();

            if (isCanceled ||
                !ReferenceEquals(damageDisplayCts, cts))
            {
                return;
            }
        }

        // 浮動小数点の誤差が残らないよう、
        // 最後に赤ゲージを目標HPへ正確に合わせる。
        damageHealthImage.fillAmount = targetDamageHealthRatio;

        isDamageGaugeDecreasing = false;

        damageDisplayCts = null;
        cts.Dispose();
    }

    // 実行中のダメージ表示処理があればキャンセルする。
    private void CancelDamageDisplay()
    {
        if (damageDisplayCts == null)
        {
            isDamageGaugeDecreasing = false;
            return;
        }

        damageDisplayCts.Cancel();
        damageDisplayCts.Dispose();
        damageDisplayCts = null;

        isDamageGaugeDecreasing = false;
    }

    // 現在のHPを待ち時間や演出なしでHPバーへ反映する。
    private void SetHealthImmediately()
    {
        float currentHealthRatio =
            CalculateHealthRatio(enemyHealth.CurrentHealth);

        currentHealthImage.fillAmount = currentHealthRatio;
        damageHealthImage.fillAmount = currentHealthRatio;

        targetDamageHealthRatio = currentHealthRatio;
        isDamageGaugeDecreasing = false;
    }

    // HPをImage.fillAmountで使用できる0～1の割合へ変換する。
    private float CalculateHealthRatio(int health)
    {
        return Mathf.Clamp01(
            (float)health / enemyHealth.MaxHealth);
    }
}
