using System;
using UnityEngine;

/// <summary>
/// Enemy Mesh分割に成功したときの2つのMeshを保持する。
/// </summary>
public sealed class MeshCutResult
{
    /// <summary>
    /// 切断Planeの正側に生成されたMesh。
    /// </summary>
    public Mesh FirstPieceMesh { get; }

    /// <summary>
    /// 切断Planeの負側に生成されたMesh。
    /// </summary>
    public Mesh SecondPieceMesh { get; }

    public MeshCutResult(
        Mesh firstPieceMesh,
        Mesh secondPieceMesh)
    {
        FirstPieceMesh = firstPieceMesh
            ?? throw new ArgumentNullException(nameof(firstPieceMesh));
        SecondPieceMesh = secondPieceMesh
            ?? throw new ArgumentNullException(nameof(secondPieceMesh));
    }
}
