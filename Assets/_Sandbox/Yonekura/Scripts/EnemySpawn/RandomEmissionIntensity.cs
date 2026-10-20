using UnityEngine;

// 発光強度をランダムに変化させる
public class RandomEmissionIntensity : MonoBehaviour
{
    [Header("発光対象")]
    [SerializeField] private Renderer targetRenderer;

    [Header("発光強度の範囲")]
    [SerializeField, Min(0f)] private float minIntensity = 1f;
    [SerializeField, Min(0f)] private float maxIntensity = 3f;

    [Header("変化する時間")]
    [SerializeField, Min(0.05f)] private float minDuration = 0.2f;
    [SerializeField, Min(0.05f)] private float maxDuration = 0.8f;

    // マテリアルを直接変更せず、オブジェクトごとの値を設定する
    private MaterialPropertyBlock propertyBlock;

    // Shader GraphのReference名
    private static readonly int EmissionIntensityId =
        Shader.PropertyToID("_EmissionIntensity");

    private float startIntensity;
    private float targetIntensity;
    private float elapsed;
    private float duration;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();

        // Rendererが未設定なら、このオブジェクトから取得
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        if (targetRenderer == null)
        {
            Debug.LogWarning(
                "発光対象のRendererが設定されていません。", this);
            enabled = false;
            return;
        }

        // 最初の発光強度を決める
        startIntensity = Random.Range(minIntensity, maxIntensity);
        targetIntensity = Random.Range(minIntensity, maxIntensity);
        duration = Random.Range(minDuration, maxDuration);

        ApplyIntensity(startIntensity);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        // 0から1まで進行度を計算
        float t = Mathf.Clamp01(elapsed / duration);

        // 急に切り替わらないよう、滑らかに変化させる
        float easedT = Mathf.SmoothStep(0f, 1f, t);

        float intensity = Mathf.Lerp(
            startIntensity,
            targetIntensity,
            easedT
        );

        ApplyIntensity(intensity);

        // 目標に到達したら、次の強度をランダムに決める
        if (elapsed >= duration)
        {
            elapsed = 0f;
            startIntensity = targetIntensity;
            targetIntensity = Random.Range(minIntensity, maxIntensity);
            duration = Random.Range(minDuration, maxDuration);
        }
    }

    private void ApplyIntensity(float intensity)
    {
        // 既存のDissolveAmountなどを保持してから変更する
        targetRenderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetFloat(EmissionIntensityId, intensity);

        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}
