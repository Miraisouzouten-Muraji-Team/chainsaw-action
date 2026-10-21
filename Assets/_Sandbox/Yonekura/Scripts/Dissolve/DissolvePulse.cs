using UnityEngine;

// ディゾルブの進行量を不規則に変化させる
public class DissolvePulse : MonoBehaviour
{
    [SerializeField] private Renderer targetRenderer;

    [Header("ディゾルブ範囲")]
    [SerializeField, Range(0f, 1f)]
    private float minAmount = 0.36f;

    [SerializeField, Range(0f, 1f)]
    private float maxAmount = 0.55f;

    [Header("通常の変化時間")]
    [SerializeField, Min(0.1f)]
    private float minDuration = 0.6f;

    [SerializeField, Min(0.1f)]
    private float maxDuration = 1.5f;

    [Header("急な変化")]
    [SerializeField, Range(0f, 1f)]
    private float suddenChangeChance = 0.2f;

    [SerializeField, Min(0.05f)]
    private float suddenMinDuration = 0.4f;

    [SerializeField, Min(0.05f)]
    private float suddenMaxDuration = 0.8f;

    private MaterialPropertyBlock propertyBlock;

    private static readonly int DissolveAmountId =
        Shader.PropertyToID("_DissolveAmount");

    private float currentAmount;
    private float startAmount;
    private float targetAmount;
    private float elapsed;
    private float duration;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();

        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
        }

        currentAmount = Random.Range(minAmount, maxAmount);
        startAmount = currentAmount;
        targetAmount = Random.Range(minAmount, maxAmount);
        duration = Random.Range(minDuration, maxDuration);

        ApplyDissolve(currentAmount);
    }

    private void Update()
    {
        if (targetRenderer == null) return;

        elapsed += Time.deltaTime;

        float t = Mathf.Clamp01(elapsed / duration);

        // 通常は滑らかに変化させる
        float easedT = Mathf.SmoothStep(0f, 1f, t);

        currentAmount = Mathf.Lerp(
            startAmount, targetAmount, easedT);

        ApplyDissolve(currentAmount);

        if (elapsed >= duration)
        {
            // 到達した値を次の変化の出発点にする
            elapsed = 0f;
            startAmount = targetAmount;

            // 毎回違う値を目標にする
            targetAmount = Random.Range(minAmount, maxAmount);

            // ときどき短い時間で急に変化させる
            if (Random.value < suddenChangeChance)
            {
                duration = Random.Range(
                    suddenMinDuration, suddenMaxDuration);
            }
            else
            {
                duration = Random.Range(
                    minDuration, maxDuration);
            }
        }
    }

    private void ApplyDissolve(float amount)
    {
        targetRenderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetFloat(DissolveAmountId, amount);

        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}
