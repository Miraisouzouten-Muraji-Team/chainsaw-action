
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地形モデル1種類分の形状情報。
/// 元モデルのRead/Write設定には実行時に依存しない。
/// </summary>
[Serializable]
public sealed class StagePieceProfile
{
    [SerializeField] private Mesh sourceMesh;
    [SerializeField] private Vector3[] vertices;
    [SerializeField] private int[] triangles;

    public Mesh SourceMesh => sourceMesh;
    public Vector3[] Vertices => vertices;
    public int[] Triangles => triangles;

    public StagePieceProfile(
        Mesh mesh, Vector3[] meshVertices, int[] meshTriangles)
    {
        SetData(mesh, meshVertices, meshTriangles);
    }

    public void SetData(
        Mesh mesh, Vector3[] meshVertices, int[] meshTriangles)
    {
        sourceMesh = mesh;
        vertices = meshVertices;
        triangles = meshTriangles;
    }
}

/// <summary>
/// ステージで使用する地形形状の一覧。
/// Editorで自動生成し、Assetとして保存する。
/// </summary>
[CreateAssetMenu(
    fileName = "StagePieceProfileLibrary",
    menuName = "Game/Stage Piece Profile Library")]
public sealed class StagePieceProfileLibrary : ScriptableObject
{
    [SerializeField] private List<StagePieceProfile> profiles = new();

    public bool TryGetProfile(
        Mesh mesh, out StagePieceProfile profile)
    {
        foreach (StagePieceProfile entry in profiles)
        {
            if (entry.SourceMesh != mesh)
                continue;

            profile = entry;
            return true;
        }

        profile = null;
        return false;
    }

    public void SetProfile(
        Mesh mesh, Vector3[] vertices, int[] triangles)
    {
        if (TryGetProfile(mesh, out StagePieceProfile profile))
        {
            profile.SetData(mesh, vertices, triangles);
            return;
        }

        profiles.Add(new StagePieceProfile(
            mesh, vertices, triangles));
    }
}
