using UnityEngine;

[DisallowMultipleComponent]
public sealed class ChainsawAcceleratorEmission : MonoBehaviour
{
    private static readonly int EMISSION_COLOR_ID = Shader.PropertyToID("_EmissionColor");

    [SerializeField]
    [Tooltip("現在のアクセル値を取得するコンポーネントです。未指定なら自身と親階層から探します。")]
    private ChainsawAccelerator accelerator;

    [SerializeField]
    [Tooltip("発光を変える Mesh Renderer または Skinned Mesh Renderer です。未指定なら同じオブジェクトから取得します。")]
    private Renderer targetRenderer;

    [SerializeField, Min(0)]
    [Tooltip("変更するマテリアルの番号です。Renderer の Materials にある Element 0、Element 1 などの番号を指定します。")]
    private int materialIndex;

    [SerializeField, ColorUsage(false, true)]
    [Tooltip("アクセルが通常の上限に達したとき、元の Emission に加える色と強さです。HDR カラーピッカーの Intensity で強さを調整できます。")]
    private Color maxEmissionBoost = new Color(2f, 2f, 2f);

    private Renderer appliedRenderer;
    private int appliedIndex;
    private Material originalMaterial;
    private Material runtimeMaterial;
    private Color baseEmission;
    private Color previousEmission;

    // 発光させるマテリアル枠を確認し、その枠だけに実行時コピーを割り当てる。
    private void OnEnable()
    {
        if (accelerator == null) accelerator = GetComponentInParent<ChainsawAccelerator>();
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();

        if (accelerator == null || targetRenderer == null)
        {
            Debug.LogWarning("Chainsaw Accelerator Emission に Accelerator と Target Renderer を設定してください。", this);
            return;
        }

        Material[] materials = targetRenderer.sharedMaterials;
        if (materialIndex < 0 || materialIndex >= materials.Length ||
            materials[materialIndex] == null || !materials[materialIndex].HasProperty(EMISSION_COLOR_ID))
        {
            Debug.LogWarning("Material Index に、_EmissionColor を持つマテリアルの番号を指定してください。", this);
            return;
        }

        appliedRenderer = targetRenderer;
        appliedIndex = materialIndex;
        originalMaterial = materials[appliedIndex];
        baseEmission = originalMaterial.GetColor(EMISSION_COLOR_ID);
        runtimeMaterial = new Material(originalMaterial);
        materials[appliedIndex] = runtimeMaterial;
        appliedRenderer.sharedMaterials = materials;
        previousEmission = baseEmission;
    }

    // 描画前に、最低値から通常上限までのアクセル割合で発光の増加量を決める。
    private void LateUpdate()
    {
        if (runtimeMaterial == null) return;

        float ratio = accelerator != null && accelerator.isActiveAndEnabled
            ? accelerator.PowerRatio
            : 0f;

        Color emission = baseEmission;
        emission.r += maxEmissionBoost.r * ratio;
        emission.g += maxEmissionBoost.g * ratio;
        emission.b += maxEmissionBoost.b * ratio;
        if (emission.Equals(previousEmission)) return;

        runtimeMaterial.SetColor(EMISSION_COLOR_ID, emission);
        previousEmission = emission;
    }

    // 自分が割り当てた枠を元に戻し、実行時に作ったマテリアルを破棄する。
    private void OnDisable()
    {
        if (runtimeMaterial == null) return;

        if (appliedRenderer != null)
        {
            Material[] materials = appliedRenderer.sharedMaterials;
            if (appliedIndex < materials.Length && materials[appliedIndex] == runtimeMaterial)
            {
                materials[appliedIndex] = originalMaterial;
                appliedRenderer.sharedMaterials = materials;
            }
        }

        Destroy(runtimeMaterial);
        runtimeMaterial = null;
        originalMaterial = null;
        appliedRenderer = null;
    }
}
