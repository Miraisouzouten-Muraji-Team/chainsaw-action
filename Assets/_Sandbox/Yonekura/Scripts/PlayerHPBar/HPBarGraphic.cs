using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 1つのUIオブジェクトでHPバー全体を描画する。
/// </summary>
public class HPBarGraphic : MaskableGraphic
{
    private static readonly int HPFillId =
        Shader.PropertyToID("_HPFill");

    private static readonly int DamageFillId =
        Shader.PropertyToID("_DamageFill");

    private static readonly int AspectRatioId =
        Shader.PropertyToID("_AspectRatio");

    // 実行中に使用する、このUI専用のマテリアル
    private Material runtimeMaterial;

    protected override void Awake()
    {
        base.Awake();
        InitializeMaterial();
    }

    private void InitializeMaterial()
    {
        if (runtimeMaterial != null)
            return;

        // Unity標準のMaterial欄に設定されたマテリアルを取得する
        Material sourceMaterial = material;

        if (sourceMaterial == null)
        {
            Debug.LogError(
                "HPバー用のマテリアルが設定されていません。",
                this);
            return;
        }

        // このUI専用のマテリアルを作成する
        runtimeMaterial = new Material(sourceMaterial);
        material = runtimeMaterial;

        SetHPFill(1f);
        SetDamageFill(1f);
        UpdateAspectRatio();
    }

    /// <summary>
    /// HPの表示割合を設定する。
    /// </summary>
    /// <param name="ratio">HPの割合。0から1の範囲。</param>
    public void SetHPFill(float ratio)
    {
        if (runtimeMaterial == null)
            InitializeMaterial();

        if (runtimeMaterial == null)
            return;

        runtimeMaterial.SetFloat(
            HPFillId,
            Mathf.Clamp01(ratio));
    }

    /// <summary>
    /// ダメージ表示の終端位置を設定する。
    /// </summary>
    /// <param name="ratio">ダメージ表示の割合。0から1の範囲。</param>
    public void SetDamageFill(float ratio)
    {
        if (runtimeMaterial == null)
            InitializeMaterial();

        if (runtimeMaterial == null)
            return;

        runtimeMaterial.SetFloat(
            DamageFillId,
            Mathf.Clamp01(ratio));
    }

    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        UpdateAspectRatio();
    }

    private void UpdateAspectRatio()
    {
        if (runtimeMaterial == null)
            return;

        Rect rect = rectTransform.rect;

        if (rect.height <= 0f)
            return;

        runtimeMaterial.SetFloat(
            AspectRatioId,
            rect.width / rect.height);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();

        if (rect.width <= 0f || rect.height <= 0f)
            return;

        // UI領域全体を1枚の四角形として描画する
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = Color.white;

        vertex.position = new Vector3(
            rect.xMin, rect.yMin, 0f);
        vertex.uv0 = new Vector2(0f, 0f);
        vh.AddVert(vertex);

        vertex.position = new Vector3(
            rect.xMin, rect.yMax, 0f);
        vertex.uv0 = new Vector2(0f, 1f);
        vh.AddVert(vertex);

        vertex.position = new Vector3(
            rect.xMax, rect.yMax, 0f);
        vertex.uv0 = new Vector2(1f, 1f);
        vh.AddVert(vertex);

        vertex.position = new Vector3(
            rect.xMax, rect.yMin, 0f);
        vertex.uv0 = new Vector2(1f, 0f);
        vh.AddVert(vertex);

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);
    }

    protected override void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeMaterial);
            else
                DestroyImmediate(runtimeMaterial);
        }

        base.OnDestroy();
    }
}
