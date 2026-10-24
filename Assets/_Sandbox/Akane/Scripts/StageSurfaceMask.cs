using UnityEngine;

/// <summary>
/// ステージ全体の外周距離画像と描画Materialを一括管理する。
/// Cube側に専用スクリプトを追加する必要はない。
/// 現在のShaderはグローバル変数を使うため、同時に有効にするMaskは原則1つとする。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class StageSurfaceMask : MonoBehaviour
{
    private static readonly int STAGE_EDGE_DISTANCE_TEX_ID = Shader.PropertyToID("_StageEdgeDistanceTex");
    private static readonly int STAGE_MASK_UV_TRANSFORM_ID = Shader.PropertyToID("_StageMaskUvTransform");
    private static readonly int STAGE_MASK_MAX_DISTANCE_ID = Shader.PropertyToID("_StageMaskMaxDistance");
    private static readonly int STAGE_MASK_ENABLED_ID = Shader.PropertyToID("_StageMaskEnabled");

    [Header("一括設定")]
    [Tooltip("子のCubeへ一括適用するステージ用Material。")]
    [SerializeField] private Material stageMaterial;
    [Tooltip("外周からテクスチャを表示する幅（ワールド単位）。Apply & Bake時にMaterialへ反映する。")]
    [SerializeField, Range(0f, 2f)] private float edgeWidth = 0.5f;
    [Tooltip("対象の地形の親。空欄ならこのオブジェクト以下から自動取得する。")]
    [SerializeField] private Transform stageRoot;
    [Tooltip("非アクティブなCubeも対象にするか。")]
    [SerializeField] private bool includeInactive;
    [Tooltip("Apply & Bake時に旧StageSurfacePieceを取り除く。操作はUndo可能。")]
    [SerializeField] private bool removeLegacyPiecesOnApply = true;

    [Header("外周距離画像の生成")]
    [Tooltip("1ワールド単位あたりの画素数。通常は16～32程度。")]
    [SerializeField, Range(4, 64)] private int pixelsPerUnit = 24;
    [Tooltip("画像に保存する最大距離。Edge Widthより大きくする。")]
    [SerializeField, Min(0.1f)] private float maxDistance = 2f;
    [Tooltip("ステージの外側へ確保する余白（ワールド単位）。")]
    [SerializeField, Min(0.1f)] private float padding = 1f;

    [Header("生成結果（自動設定）")]
    [SerializeField] private Texture2D distanceTexture;
    [SerializeField] private Vector4 uvTransform;
    [SerializeField] private float bakedMaxDistance;

    public Material StageMaterial => stageMaterial;
    public float EdgeWidth => edgeWidth;
    public Transform StageRoot => stageRoot != null ? stageRoot : transform;
    public bool IncludeInactive => includeInactive;
    public bool RemoveLegacyPiecesOnApply => removeLegacyPiecesOnApply;
    public int PixelsPerUnit => pixelsPerUnit;
    public float MaxDistance => maxDistance;
    public float Padding => padding;
    public Texture2D DistanceTexture => distanceTexture;

    private void OnEnable()
    {
        ApplyGlobalShaderData();
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(STAGE_MASK_ENABLED_ID, 0f);
    }

    /// <summary>
    /// 焼き込み結果を登録し、Shaderが参照する値を更新する。
    /// </summary>
    public void SetBakedData(Texture2D texture, Vector4 maskUvTransform, float bakedDistance)
    {
        distanceTexture = texture;
        uvTransform = maskUvTransform;
        bakedMaxDistance = bakedDistance;
        ApplyGlobalShaderData();
    }

    /// <summary>
    /// Shaderへ共通の距離画像を渡す。各CubeにはMaterialPropertyBlockを使わない。
    /// </summary>
    public void ApplyGlobalShaderData()
    {
        if (!isActiveAndEnabled || distanceTexture == null || bakedMaxDistance <= 0f)
        {
            Shader.SetGlobalFloat(STAGE_MASK_ENABLED_ID, 0f);
            return;
        }

        Shader.SetGlobalTexture(STAGE_EDGE_DISTANCE_TEX_ID, distanceTexture);
        Shader.SetGlobalVector(STAGE_MASK_UV_TRANSFORM_ID, uvTransform);
        Shader.SetGlobalFloat(STAGE_MASK_MAX_DISTANCE_ID, bakedMaxDistance);
        Shader.SetGlobalFloat(STAGE_MASK_ENABLED_ID, 1f);
    }
}
