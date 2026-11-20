
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ステージの外周生成設定を管理する。
/// 実行時には生成済みメッシュを通常のMeshRendererで描画する。
/// </summary>
[DisallowMultipleComponent]
public sealed class StageSurfaceMask : MonoBehaviour
{
    public const string GENERATED_OBJECT_NAME = "__StageEdgeOverlay";

    [Header("マテリアル")]
    [SerializeField] private Material stageMaterial;
    [SerializeField] private Material edgeMaterial;

    [Header("地形")]
    [SerializeField] private Transform stageRoot;
    [SerializeField] private bool includeInactive;

    [Header("外周")]
    [SerializeField, Range(0.01f, 2f)]
    private float edgeWidth = 0.35f;

    [Header("角の丸め")]
    [Tooltip("凸角の内側を丸める半径。")]
    [SerializeField, Min(0f)] private float cornerRadius = 0.15f;

    [Tooltip("円弧を何分割するか。")]
    [SerializeField, Range(2, 16)] private int cornerSegments = 8;

    public float CornerRadius => cornerRadius;
    public int CornerSegments => cornerSegments;

    [Header("深度")]
    [Tooltip("カメラが-Z側から地形を見る場合はON。")]
    [SerializeField] private bool cameraAtNegativeZ = true;

    [Tooltip("元の地形面からカメラ側へ離す距離。")]
    [SerializeField, Range(0f, 0.02f)]
    private float depthOffset = 0.002f;

    [Header("生成済み形状")]
    [SerializeField] private StagePieceProfileLibrary profileLibrary;

    public Material StageMaterial => stageMaterial;
    public Material EdgeMaterial => edgeMaterial;
    public Transform StageRoot =>
        stageRoot != null ? stageRoot : transform;
    public bool IncludeInactive => includeInactive;
    public float EdgeWidth => edgeWidth;
    public bool CameraAtNegativeZ => cameraAtNegativeZ;
    public float DepthOffset => depthOffset;
    public StagePieceProfileLibrary ProfileLibrary => profileLibrary;

    /// <summary>
    /// 外周処理の対象となるMeshFilterを取得する。
    /// 生成した縁メッシュは除外する。
    /// </summary>
    public List<MeshFilter> CollectStageMeshes()
    {
        MeshFilter[] filters =
            StageRoot.GetComponentsInChildren<MeshFilter>(
                includeInactive);

        List<MeshFilter> result = new(filters.Length);

        foreach (MeshFilter filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;

            // 再構築時に生成済みオブジェクトを収集しない。
            if (filter.name.StartsWith(
                GENERATED_OBJECT_NAME,
                StringComparison.Ordinal))
                continue;

            if (!filter.TryGetComponent(
                out MeshRenderer meshRenderer))
                continue;

            if (!meshRenderer.enabled)
                continue;

            result.Add(filter);
        }

        return result;
    }

    /// <summary>
    /// Editorで作成した形状データを登録する。
    /// </summary>
    public void SetProfileLibrary(StagePieceProfileLibrary library)
    {
        profileLibrary = library;
    }

    /// <summary>
    /// Editorで作成した縁用マテリアルを登録する。
    /// </summary>
    public void SetEdgeMaterial(Material material)
    {
        edgeMaterial = material;
    }
}
