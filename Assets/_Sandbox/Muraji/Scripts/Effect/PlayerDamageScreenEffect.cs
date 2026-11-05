using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 作成済みの被弾専用Volumeを、Weightだけで表示・非表示にする。
[DisallowMultipleComponent]
[RequireComponent(typeof(Volume))]
public sealed class PlayerDamageScreenEffect : MonoBehaviour
{
    [Tooltip("被弾後の残りHPがこの割合以下のときに赤い縁を表示します。20なら最大HPの20%以下、100なら生存中のすべての被弾が対象です。0なら表示しません。")]
    [SerializeField, Range(0f, 100f)] private float lowHealthPercent = 20f;

    [Tooltip("赤い縁の表示条件を判定するプレイヤーのPlayerHealthを指定します。未指定の場合は自分または親から取得します。独立したVolumeの場合は必ず指定してください。")]
    [SerializeField] private PlayerHealth playerHealth;

    [Tooltip("被弾演出の維持時間と、消えるまでの時間を設定したアセットです。色・濃さ・ぼかしは専用VolumeのProfileで設定します。")]
    [SerializeField] private PlayerDamageScreenData screenData;

    [Tooltip("被弾用Volumeの優先度です。通常のVolumeより大きくしてください。")]
    [SerializeField] private float volumePriority = 100f;

    private Volume damageVolume;
    private float elapsedTime;
    private float holdDuration;
    private float fadeDuration;
    private bool initialized;
    private bool isPlaying;

    // 作成済みのProfileとVignetteを確認し、専用Volumeを非表示で初期化する。
    private void Awake()
    {
        damageVolume = GetComponent<Volume>();
        if (playerHealth == null)
            playerHealth = GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
            Debug.LogWarning("被弾画面演出：Player Healthに対象プレイヤーのPlayerHealthを指定してください。未設定の間は演出を表示しません。", this);
        if (screenData == null || damageVolume.sharedProfile == null)
        {
            Debug.LogError("被弾画面演出：Screen Dataと、被弾専用VolumeのProfileを設定してください。Profileは再生前にNewで作成します。", this);
            enabled = false;
            return;
        }

        // 欠損したOverrideは実行時に削除・作成せず、設定不備として知らせる。
        VolumeProfile profile = damageVolume.sharedProfile;
        foreach (VolumeComponent component in profile.components)
        {
            if (component != null) continue;
            Debug.LogError("被弾画面演出：Profile内に欠損したOverrideがあります。再生停止中に新しい被弾専用Profileを作成し、Vignetteを追加してください。", this);
            enabled = false;
            return;
        }

        if (!profile.TryGet<Vignette>(out Vignette vignette) || !vignette.active ||
            !vignette.intensity.overrideState || vignette.intensity.value <= 0f)
        {
            Debug.LogError("被弾画面演出：専用Profileに有効なVignetteを追加し、IntensityのチェックをONにして0より大きく設定してください。", this);
            enabled = false;
            return;
        }

        // ProfileとOverrideの参照・値は変更しない。専用Volumeの影響度だけを制御する。
        damageVolume.isGlobal = true;
        damageVolume.priority = volumePriority;
        damageVolume.weight = 0f;
        initialized = true;
    }

    // 被ダメージ適用後に呼び、残りHPがInspectorの指定割合以下の場合だけ演出を開始する。
    public void PlayDamageEffect()
    {
        if (!initialized || !isActiveAndEnabled || screenData == null ||
            damageVolume == null || !damageVolume.isActiveAndEnabled)
        {
            return;
        }

        if (!CanShowDamageEffect())
        {
            ResetEffect();
            return;
        }

        holdDuration = screenData.HoldDuration;
        fadeDuration = screenData.FadeDuration;
        elapsedTime = 0f;
        isPlaying = true;
        damageVolume.weight = 1f;
    }

    // 有効なHP情報を使い、指定割合ちょうどを含む低HP状態か判定する。
    private bool CanShowDamageEffect()
    {
        return playerHealth != null && playerHealth.isActiveAndEnabled &&
            playerHealth.MaxHealth > 0f && !playerHealth.IsDead &&
            playerHealth.CurrentHealth <= playerHealth.MaxHealth *
                (Mathf.Clamp(lowHealthPercent, 0f, 100f) / 100f);
    }

    // ゲーム内時間で表示とフェードを進め、回復・死亡・参照喪失時は表示を解除する。
    private void FixedUpdate()
    {
        if (!isPlaying || damageVolume == null) return;

        if (!CanShowDamageEffect())
        {
            ResetEffect();
            return;
        }

        elapsedTime += Time.fixedDeltaTime;
        if (elapsedTime < holdDuration) return;

        if (fadeDuration <= 0f || elapsedTime >= holdDuration + fadeDuration)
        {
            ResetEffect();
            return;
        }

        damageVolume.weight = 1f - Mathf.Clamp01((elapsedTime - holdDuration) / fadeDuration);
    }

    // 死亡・復活時などに表示と経過時間をリセットする。
    public void ResetEffect()
    {
        isPlaying = false;
        elapsedTime = 0f;
        if (initialized && damageVolume != null)
            damageVolume.weight = 0f;
    }

    // 無効化後に赤い縁取りを残さない。Profileの参照は維持する。
    private void OnDisable()
    {
        ResetEffect();
    }
}
