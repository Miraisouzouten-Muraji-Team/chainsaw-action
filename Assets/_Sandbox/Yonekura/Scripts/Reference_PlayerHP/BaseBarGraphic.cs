using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HPバーや回復バーに共通する描画処理を管理する基底クラス
/// マテリアル、色、角度、縦横比、頂点生成を担当する
/// </summary>
[ExecuteAlways]
public abstract class BaseBarGraphic : MaskableGraphic
{
    // Shader Graphで使用する共通プロパティ名をIDとして保持する
    private static readonly int HPFillId =
        Shader.PropertyToID("_HPFill");
    private static readonly int AspectRatioId =
        Shader.PropertyToID("_AspectRatio");
    private static readonly int BackgroundColorId =
        Shader.PropertyToID("_BackgroundColor");
    private static readonly int AngleAId =
        Shader.PropertyToID("_AngleA");
    private static readonly int AngleBId =
        Shader.PropertyToID("_AngleB");

    [InspectorName("Background Color")]
    [SerializeField] private Color backgroundColor = Color.gray;

    [Tooltip("左上と右下の角度")]
    [SerializeField, Range(0f, 180f)]
    private float angleA = 60f;

    [Tooltip("右上と左下の角度")]
    [SerializeField, Range(0f, 180f)]
    private float angleB = 120f;

    // このUI専用のマテリアル。
    // 元のマテリアルを直接変更しないように複製する
    private Material runtimeMaterial;

    /// <summary>
    /// UIが有効になったときに、マテリアルと描画設定を初期化する
    /// </summary>
    protected override void OnEnable()
    {
        base.OnEnable();

        InitializeMaterial();
        ApplySettings();
        UpdateAspectRatio();
    }

    /// <summary>
    /// Inspectorの値が変更されたときに、描画設定を更新する
    /// </summary>
    protected virtual void OnValidate()
    {
        if (!isActiveAndEnabled)
            return;

        InitializeMaterial();
        ApplySettings();
        UpdateAspectRatio();
        SetVerticesDirty();
    }

    // このUI専用のマテリアルを作成する
    private void InitializeMaterial()
    {
        if (runtimeMaterial != null)
            return;

        Material sourceMaterial = material;

        if (sourceMaterial == null)
            return;

        runtimeMaterial = new Material(sourceMaterial)
        {
            name = sourceMaterial.name + " (Bar Instance)"
        };

        material = runtimeMaterial;
    }

    // 共通設定と、派生クラス固有の設定をマテリアルに反映する
    private void ApplySettings()
    {
        if (runtimeMaterial == null)
            return;

        SetColorIfAvailable(BackgroundColorId, backgroundColor);
        SetFloatIfAvailable(AngleAId, angleA);
        SetFloatIfAvailable(AngleBId, angleB);

        // HPバーや回復バーそれぞれの色設定を反映する
        ApplySpecificSettings();
    }

    /// <summary>
    /// 派生クラス固有の色などを設定する
    /// </summary>
    protected abstract void ApplySpecificSettings();

    /// <summary>
    /// 指定したプロパティが存在する場合に色を設定する
    /// </summary>
    protected void SetColorIfAvailable(int propertyId, Color color)
    {
        if (runtimeMaterial != null &&
            runtimeMaterial.HasProperty(propertyId))
        {
            runtimeMaterial.SetColor(propertyId, color);
        }
    }

    /// <summary>
    /// 指定したプロパティが存在する場合に数値を設定する
    /// </summary>
    protected void SetFloatIfAvailable(int propertyId, float value)
    {
        if (runtimeMaterial != null &&
            runtimeMaterial.HasProperty(propertyId))
        {
            runtimeMaterial.SetFloat(propertyId, value);
        }
    }

    /// <summary>
    /// HPや回復タイマーの表示割合を設定する
    /// </summary>
    /// <param name="ratio">表示割合。0で空、1で満タン</param>
    public void SetHPFill(float ratio)
    {
        SetMaterialFloat(HPFillId, ratio);
    }

    /// <summary>
    /// 指定した数値をマテリアルに設定する
    /// </summary>
    protected void SetMaterialFloat(int propertyId, float value)
    {
        if (runtimeMaterial == null)
            InitializeMaterial();

        if (runtimeMaterial == null)
            return;

        SetFloatIfAvailable(propertyId, Mathf.Clamp01(value));
    }

    /// <summary>
    /// UIのサイズ変更時に縦横比を更新する
    /// </summary>
    protected override void OnRectTransformDimensionsChange()
    {
        base.OnRectTransformDimensionsChange();
        UpdateAspectRatio();
    }

    // UIの幅と高さから縦横比を計算し、マテリアルに設定する
    private void UpdateAspectRatio()
    {
        if (runtimeMaterial == null)
            return;

        Rect rect = rectTransform.rect;

        if (rect.height <= 0f)
            return;

        SetFloatIfAvailable(AspectRatioId, rect.width / rect.height);
    }

    /// <summary>
    /// UIを描画するための四角形の頂点を生成する
    /// </summary>
    /// <param name="vh">頂点と三角形を管理するVertexHelper。</param>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();

        if (rect.width <= 0f || rect.height <= 0f)
            return;

        // UI全体を四角形として生成する
        // HPの割合や平行四辺形の形状はShader Graph側で制御する
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = Color.white;

        vertex.position = new Vector3(rect.xMin, rect.yMin, 0f);
        vertex.uv0 = new Vector2(0f, 0f);
        vh.AddVert(vertex);

        vertex.position = new Vector3(rect.xMin, rect.yMax, 0f);
        vertex.uv0 = new Vector2(0f, 1f);
        vh.AddVert(vertex);

        vertex.position = new Vector3(rect.xMax, rect.yMax, 0f);
        vertex.uv0 = new Vector2(1f, 1f);
        vh.AddVert(vertex);

        vertex.position = new Vector3(rect.xMax, rect.yMin, 0f);
        vertex.uv0 = new Vector2(1f, 0f);
        vh.AddVert(vertex);

        // 四角形を2つの三角形に分割する
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);
    }

    /// <summary>
    /// UI専用のマテリアルを破棄する
    /// </summary>
    protected override void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeMaterial);
            else
                DestroyImmediate(runtimeMaterial);

            runtimeMaterial = null;
        }

        base.OnDestroy();
    }
}
