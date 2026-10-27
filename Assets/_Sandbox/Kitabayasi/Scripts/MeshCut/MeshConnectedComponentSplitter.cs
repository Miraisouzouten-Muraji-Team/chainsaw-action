using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 1つのMeshまたはMesh化前のTriangle集合に含まれる、幾何学的につながったTriangle群を個別Meshへ分離する。
/// </summary>
/// <remarks>
/// UV seam等で同じ位置の頂点が別indexになっていても連結判定できるよう、
/// 頂点indexではなくローカル座標上の辺共有でTriangle同士を接続する。
/// GameObject生成、Collider/Rigidbody付与、切断Plane生成、飛散処理は担当しない。
/// </remarks>
public sealed class MeshConnectedComponentSplitter
{
    private const float POSITION_EPSILON_RATIO = 0.00001f;
    private const float MIN_POSITION_EPSILON = 0.000001f;

    /// <summary>
    /// 入力Meshを連結成分単位のMeshへ分離する。
    /// </summary>
    /// <param name="sourceMesh">Triangle topologyで構成された読み取り可能なRuntime Mesh。</param>
    /// <param name="componentMeshes">成功時、連結成分ごとに新規生成したMesh。呼び出し側が寿命を管理する。</param>
    public bool TrySplit(
        Mesh sourceMesh,
        out List<Mesh> componentMeshes,
        out string failureReason)
    {
        componentMeshes = new List<Mesh>();

        if (!TryReadSourceMesh(
                sourceMesh,
                out Vector3[] vertices,
                out Vector3[] normals,
                out Vector2[] uv,
                out Vector4[] tangents,
                out bool hasTangents,
                out List<List<int>> trianglesBySubMesh,
                out int triangleCount,
                out failureReason))
        {
            return false;
        }

        return TrySplitReadData(
            sourceMesh.name,
            sourceMesh.bounds,
            vertices,
            normals,
            uv,
            tangents,
            hasTangents,
            trianglesBySubMesh,
            triangleCount,
            out componentMeshes,
            out failureReason);
    }

    /// <summary>
    /// Unity Meshへ確定する前の分割作業バッファから、連結成分だけを最終Mesh化する。
    /// </summary>
    /// <remarks>
    /// MeshPlaneSplitterが構築済みの頂点属性、Triangle index、Boundsを直接受け取り、
    /// 中間Mesh生成・TriangleRecordへの全コピー・Bounds算出用の頂点再走査を行わない。
    /// </remarks>
    internal bool TrySplit(
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
        Bounds sourceBounds,
        string meshNamePrefix,
        out List<Mesh> componentMeshes,
        out string failureReason)
    {
        componentMeshes = new List<Mesh>();

        if (!TryValidateWorkingData(
                sourceVertices,
                sourceNormals,
                sourceUv,
                sourceTangents,
                hasTangents,
                sourceTrianglesBySubMesh,
                out int triangleCount,
                out failureReason))
        {
            return false;
        }

        return TrySplitReadData(
            meshNamePrefix,
            sourceBounds,
            sourceVertices,
            sourceNormals,
            sourceUv,
            sourceTangents,
            hasTangents,
            sourceTrianglesBySubMesh,
            triangleCount,
            out componentMeshes,
            out failureReason);
    }

    private static bool TrySplitReadData(
        string meshNamePrefix,
        Bounds sourceBounds,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uv,
        IReadOnlyList<Vector4> tangents,
        bool hasTangents,
        IReadOnlyList<List<int>> trianglesBySubMesh,
        int triangleCount,
        out List<Mesh> componentMeshes,
        out string failureReason)
    {
        componentMeshes = new List<Mesh>();

        float positionEpsilon =
            CalculatePositionEpsilon(sourceBounds);

        int[] connectedVertexIds =
            BuildConnectedVertexIds(vertices, positionEpsilon);

        var parent = new int[triangleCount];
        var rank = new byte[triangleCount];
        var edgeOwners =
            new Dictionary<EdgeKey, int>(triangleCount * 2);

        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        int[] firstTriangleIndexBySubMesh =
            BuildFirstTriangleIndicesBySubMesh(trianglesBySubMesh);

        int triangleIndex = 0;

        for (int subMeshIndex = 0;
             subMeshIndex < trianglesBySubMesh.Count;
             subMeshIndex++)
        {
            IReadOnlyList<int> subMeshTriangles =
                trianglesBySubMesh[subMeshIndex];

            for (int offset = 0;
                 offset < subMeshTriangles.Count;
                 offset += 3)
            {
                int first =
                    connectedVertexIds[subMeshTriangles[offset]];

                int second =
                    connectedVertexIds[subMeshTriangles[offset + 1]];

                int third =
                    connectedVertexIds[subMeshTriangles[offset + 2]];

                RegisterSharedEdge(
                    first,
                    second,
                    triangleIndex,
                    edgeOwners,
                    parent,
                    rank);

                RegisterSharedEdge(
                    second,
                    third,
                    triangleIndex,
                    edgeOwners,
                    parent,
                    rank);

                RegisterSharedEdge(
                    third,
                    first,
                    triangleIndex,
                    edgeOwners,
                    parent,
                    rank);

                triangleIndex++;
            }
        }

        var triangleIndicesByRoot =
            new Dictionary<int, List<int>>();

        var componentRoots =
            new List<int>();

        for (triangleIndex = 0;
             triangleIndex < triangleCount;
             triangleIndex++)
        {
            int root =
                FindRoot(parent, triangleIndex);

            if (!triangleIndicesByRoot.TryGetValue(
                    root,
                    out List<int> componentTriangleIndices))
            {
                componentTriangleIndices =
                    new List<int>();

                triangleIndicesByRoot.Add(
                    root,
                    componentTriangleIndices);

                // Triangle走査順を保持してComponent名と生成順を決定的にする。
                componentRoots.Add(root);
            }

            componentTriangleIndices.Add(triangleIndex);
        }

        for (int componentIndex = 0;
             componentIndex < componentRoots.Count;
             componentIndex++)
        {
            Mesh componentMesh =
                BuildComponentMesh(
                    meshNamePrefix,
                    componentIndex,
                    triangleIndicesByRoot[
                        componentRoots[componentIndex]],
                    trianglesBySubMesh,
                    firstTriangleIndexBySubMesh,
                    vertices,
                    normals,
                    uv,
                    tangents,
                    hasTangents);

            if (componentMesh == null)
            {
                DestroyMeshes(componentMeshes);
                componentMeshes.Clear();

                failureReason =
                    "連結成分から有効なMeshを生成できませんでした。";
                return false;
            }

            componentMeshes.Add(componentMesh);
        }

        if (componentMeshes.Count == 0)
        {
            failureReason =
                "連結成分が1つも検出されませんでした。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryReadSourceMesh(
        Mesh sourceMesh,
        out Vector3[] vertices,
        out Vector3[] normals,
        out Vector2[] uv,
        out Vector4[] tangents,
        out bool hasTangents,
        out List<List<int>> trianglesBySubMesh,
        out int triangleCount,
        out string failureReason)
    {
        vertices = null;
        normals = null;
        uv = null;
        tangents = null;
        hasTangents = false;
        trianglesBySubMesh = null;
        triangleCount = 0;

        if (sourceMesh == null)
        {
            failureReason = "分離対象Meshがnullです。";
            return false;
        }

        if (!sourceMesh.isReadable)
        {
            failureReason = "分離対象Meshが読み取り不可です。";
            return false;
        }

        if (sourceMesh.vertexCount == 0 ||
            sourceMesh.subMeshCount == 0)
        {
            failureReason =
                "分離対象Meshに頂点またはSubMeshがありません。";
            return false;
        }

        vertices = sourceMesh.vertices;
        normals = sourceMesh.normals;
        uv = sourceMesh.uv;
        tangents = sourceMesh.tangents;

        if (normals.Length != vertices.Length)
        {
            failureReason =
                "分離対象Meshの頂点数とNormal数が一致していません。";
            return false;
        }

        if (uv.Length != vertices.Length)
        {
            failureReason =
                "分離対象Meshの頂点数とUV数が一致していません。";
            return false;
        }

        hasTangents =
            tangents.Length == vertices.Length;

        trianglesBySubMesh =
            new List<List<int>>(sourceMesh.subMeshCount);

        for (int subMeshIndex = 0;
             subMeshIndex < sourceMesh.subMeshCount;
             subMeshIndex++)
        {
            if (sourceMesh.GetTopology(subMeshIndex) !=
                MeshTopology.Triangles)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} がTriangle topologyではありません。";
                return false;
            }

            var subMeshTriangles = new List<int>();
            sourceMesh.GetTriangles(subMeshTriangles, subMeshIndex);

            if (subMeshTriangles.Count % 3 != 0)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} のTriangle index数が不正です。";
                return false;
            }

            trianglesBySubMesh.Add(subMeshTriangles);
            triangleCount += subMeshTriangles.Count / 3;
        }

        if (triangleCount == 0)
        {
            failureReason =
                "分離対象MeshにTriangleがありません。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryValidateWorkingData(
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
        out int triangleCount,
        out string failureReason)
    {
        triangleCount = 0;

        if (sourceVertices == null ||
            sourceVertices.Count == 0 ||
            sourceNormals == null ||
            sourceNormals.Count != sourceVertices.Count ||
            sourceUv == null ||
            sourceUv.Count != sourceVertices.Count ||
            sourceTrianglesBySubMesh == null ||
            sourceTrianglesBySubMesh.Count == 0)
        {
            failureReason =
                "連結成分判定用の分割作業データが不正です。";
            return false;
        }

        if (hasTangents &&
            (sourceTangents == null ||
             sourceTangents.Count != sourceVertices.Count))
        {
            failureReason =
                "連結成分判定用のTangent数が頂点数と一致していません。";
            return false;
        }

        for (int subMeshIndex = 0;
             subMeshIndex < sourceTrianglesBySubMesh.Count;
             subMeshIndex++)
        {
            IReadOnlyList<int> subMeshTriangles =
                sourceTrianglesBySubMesh[subMeshIndex];

            if (subMeshTriangles == null ||
                subMeshTriangles.Count % 3 != 0)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} の作業Triangle index数が不正です。";
                return false;
            }

            for (int offset = 0;
                 offset < subMeshTriangles.Count;
                 offset += 3)
            {
                if (!IsValidVertexIndex(
                        subMeshTriangles[offset],
                        sourceVertices.Count) ||
                    !IsValidVertexIndex(
                        subMeshTriangles[offset + 1],
                        sourceVertices.Count) ||
                    !IsValidVertexIndex(
                        subMeshTriangles[offset + 2],
                        sourceVertices.Count))
                {
                    failureReason =
                        $"SubMesh {subMeshIndex} の作業Triangleに範囲外の頂点indexがあります。";
                    return false;
                }
            }

            triangleCount += subMeshTriangles.Count / 3;
        }

        if (triangleCount == 0)
        {
            failureReason =
                "連結成分判定用のTriangleがありません。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static int[] BuildFirstTriangleIndicesBySubMesh(
        IReadOnlyList<List<int>> trianglesBySubMesh)
    {
        var firstTriangleIndexBySubMesh =
            new int[trianglesBySubMesh.Count + 1];

        int triangleIndex = 0;

        for (int subMeshIndex = 0;
             subMeshIndex < trianglesBySubMesh.Count;
             subMeshIndex++)
        {
            firstTriangleIndexBySubMesh[subMeshIndex] =
                triangleIndex;

            triangleIndex +=
                trianglesBySubMesh[subMeshIndex].Count / 3;
        }

        firstTriangleIndexBySubMesh[trianglesBySubMesh.Count] =
            triangleIndex;

        return firstTriangleIndexBySubMesh;
    }

    private static bool IsValidVertexIndex(
        int vertexIndex,
        int vertexCount)
    {
        return vertexIndex >= 0 &&
               vertexIndex < vertexCount;
    }

    /// <summary>
    /// 同一位置とみなせる頂点へ共通IDを割り当てる。
    /// </summary>
    /// <remarks>
    /// UV seam / hard edgeや切断で生成された頂点は同じ位置でも別indexになり得るため、
    /// index一致だけでは連結を判定しない。隣接セルも確認して許容距離内の頂点だけをまとめる。
    /// </remarks>
    private static int[] BuildConnectedVertexIds(
        IReadOnlyList<Vector3> vertices,
        float positionEpsilon)
    {
        var connectedVertexIds = new int[vertices.Count];
        var canonicalPositions = new List<Vector3>();
        var canonicalIdsByCell = new Dictionary<PositionCellKey, List<int>>();
        float epsilonSqr = positionEpsilon * positionEpsilon;

        for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
        {
            Vector3 position = vertices[vertexIndex];
            PositionCellKey cell = PositionCellKey.Create(position, positionEpsilon);
            int matchingCanonicalId = -1;

            for (int xOffset = -1; xOffset <= 1 && matchingCanonicalId < 0; xOffset++)
            {
                for (int yOffset = -1; yOffset <= 1 && matchingCanonicalId < 0; yOffset++)
                {
                    for (int zOffset = -1; zOffset <= 1; zOffset++)
                    {
                        PositionCellKey neighborCell = cell.Offset(xOffset, yOffset, zOffset);

                        if (!canonicalIdsByCell.TryGetValue(neighborCell, out List<int> candidateIds))
                        {
                            continue;
                        }

                        for (int candidateIndex = 0; candidateIndex < candidateIds.Count; candidateIndex++)
                        {
                            int candidateId = candidateIds[candidateIndex];

                            if ((canonicalPositions[candidateId] - position).sqrMagnitude <= epsilonSqr)
                            {
                                matchingCanonicalId = candidateId;
                                break;
                            }
                        }

                        if (matchingCanonicalId >= 0)
                        {
                            break;
                        }
                    }
                }
            }

            if (matchingCanonicalId < 0)
            {
                matchingCanonicalId = canonicalPositions.Count;
                canonicalPositions.Add(position);

                if (!canonicalIdsByCell.TryGetValue(cell, out List<int> cellCanonicalIds))
                {
                    cellCanonicalIds = new List<int>();
                    canonicalIdsByCell.Add(cell, cellCanonicalIds);
                }

                cellCanonicalIds.Add(matchingCanonicalId);
            }

            connectedVertexIds[vertexIndex] = matchingCanonicalId;
        }

        return connectedVertexIds;
    }

    private static void RegisterSharedEdge(
        int first,
        int second,
        int triangleIndex,
        Dictionary<EdgeKey, int> edgeOwners,
        int[] parent,
        byte[] rank)
    {
        // 長さ0の辺は別Triangle同士を誤って接続する原因になるため使わない。
        if (first == second)
        {
            return;
        }

        var edge = new EdgeKey(first, second);

        if (edgeOwners.TryGetValue(edge, out int ownerTriangleIndex))
        {
            Union(parent, rank, ownerTriangleIndex, triangleIndex);
            return;
        }

        edgeOwners.Add(edge, triangleIndex);
    }

    private static Mesh BuildComponentMesh(
        string meshNamePrefix,
        int componentIndex,
        IReadOnlyList<int> componentTriangleIndices,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
        IReadOnlyList<int> firstTriangleIndexBySubMesh,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents)
    {
        if (componentTriangleIndices == null ||
            componentTriangleIndices.Count == 0)
        {
            return null;
        }

        int vertexCapacity =
            Math.Min(
                sourceVertices.Count,
                componentTriangleIndices.Count * 3);

        var vertices = new List<Vector3>(vertexCapacity);
        var normals = new List<Vector3>(vertexCapacity);
        var uv = new List<Vector2>(vertexCapacity);
        List<Vector4> tangents =
            hasTangents
                ? new List<Vector4>(vertexCapacity)
                : null;

        var trianglesBySubMesh =
            new List<List<int>>(sourceTrianglesBySubMesh.Count);

        var destinationVertexIndexBySourceVertexIndex =
            new Dictionary<int, int>(vertexCapacity);

        for (int subMeshIndex = 0;
             subMeshIndex < sourceTrianglesBySubMesh.Count;
             subMeshIndex++)
        {
            trianglesBySubMesh.Add(new List<int>());
        }

        bool hasBounds = false;
        Vector3 minimum = default;
        Vector3 maximum = default;

        for (int i = 0;
             i < componentTriangleIndices.Count;
             i++)
        {
            int triangleIndex =
                componentTriangleIndices[i];

            ResolveTriangleLocation(
                triangleIndex,
                firstTriangleIndexBySubMesh,
                out int subMeshIndex,
                out int triangleOffset);

            IReadOnlyList<int> sourceTriangles =
                sourceTrianglesBySubMesh[subMeshIndex];

            List<int> destinationTriangles =
                trianglesBySubMesh[subMeshIndex];

            destinationTriangles.Add(
                GetOrAddVertex(
                    sourceTriangles[triangleOffset],
                    sourceVertices,
                    sourceNormals,
                    sourceUv,
                    sourceTangents,
                    hasTangents,
                    destinationVertexIndexBySourceVertexIndex,
                    vertices,
                    normals,
                    uv,
                    tangents,
                    ref hasBounds,
                    ref minimum,
                    ref maximum));

            destinationTriangles.Add(
                GetOrAddVertex(
                    sourceTriangles[triangleOffset + 1],
                    sourceVertices,
                    sourceNormals,
                    sourceUv,
                    sourceTangents,
                    hasTangents,
                    destinationVertexIndexBySourceVertexIndex,
                    vertices,
                    normals,
                    uv,
                    tangents,
                    ref hasBounds,
                    ref minimum,
                    ref maximum));

            destinationTriangles.Add(
                GetOrAddVertex(
                    sourceTriangles[triangleOffset + 2],
                    sourceVertices,
                    sourceNormals,
                    sourceUv,
                    sourceTangents,
                    hasTangents,
                    destinationVertexIndexBySourceVertexIndex,
                    vertices,
                    normals,
                    uv,
                    tangents,
                    ref hasBounds,
                    ref minimum,
                    ref maximum));
        }

        if (!hasBounds)
        {
            return null;
        }

        var mesh = new Mesh
        {
            name = $"{meshNamePrefix}_Component_{componentIndex:D2}",
            indexFormat = vertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16
        };

        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);

        if (hasTangents)
        {
            mesh.SetTangents(tangents);
        }

        mesh.subMeshCount = sourceTrianglesBySubMesh.Count;

        for (int subMeshIndex = 0;
             subMeshIndex < trianglesBySubMesh.Count;
             subMeshIndex++)
        {
            mesh.SetTriangles(
                trianglesBySubMesh[subMeshIndex],
                subMeshIndex,
                false);
        }

        mesh.bounds =
            new Bounds(
                (minimum + maximum) * 0.5f,
                maximum - minimum);

        return mesh;
    }

    private static int GetOrAddVertex(
        int sourceVertexIndex,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents,
        Dictionary<int, int> destinationVertexIndexBySourceVertexIndex,
        List<Vector3> destinationVertices,
        List<Vector3> destinationNormals,
        List<Vector2> destinationUv,
        List<Vector4> destinationTangents,
        ref bool hasBounds,
        ref Vector3 minimum,
        ref Vector3 maximum)
    {
        if (destinationVertexIndexBySourceVertexIndex.TryGetValue(
                sourceVertexIndex,
                out int destinationVertexIndex))
        {
            return destinationVertexIndex;
        }

        destinationVertexIndex =
            destinationVertices.Count;

        Vector3 position =
            sourceVertices[sourceVertexIndex];

        destinationVertices.Add(position);
        destinationNormals.Add(sourceNormals[sourceVertexIndex]);
        destinationUv.Add(sourceUv[sourceVertexIndex]);

        if (hasTangents)
        {
            destinationTangents.Add(
                sourceTangents[sourceVertexIndex]);
        }

        destinationVertexIndexBySourceVertexIndex.Add(
            sourceVertexIndex,
            destinationVertexIndex);

        if (!hasBounds)
        {
            minimum = position;
            maximum = position;
            hasBounds = true;
        }
        else
        {
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }

        return destinationVertexIndex;
    }

    private static void ResolveTriangleLocation(
        int triangleIndex,
        IReadOnlyList<int> firstTriangleIndexBySubMesh,
        out int subMeshIndex,
        out int triangleOffset)
    {
        int low = 0;
        int high = firstTriangleIndexBySubMesh.Count - 2;

        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int firstTriangleIndex =
                firstTriangleIndexBySubMesh[middle];
            int nextSubMeshFirstTriangleIndex =
                firstTriangleIndexBySubMesh[middle + 1];

            if (triangleIndex < firstTriangleIndex)
            {
                high = middle - 1;
                continue;
            }

            if (triangleIndex >= nextSubMeshFirstTriangleIndex)
            {
                low = middle + 1;
                continue;
            }

            subMeshIndex = middle;
            triangleOffset =
                (triangleIndex - firstTriangleIndex) * 3;
            return;
        }

        throw new ArgumentOutOfRangeException(
            nameof(triangleIndex),
            triangleIndex,
            "Triangle indexからSubMeshを解決できませんでした。");
    }

    private static float CalculatePositionEpsilon(Bounds bounds)
    {
        float largestExtent = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        return Mathf.Max(MIN_POSITION_EPSILON, largestExtent * POSITION_EPSILON_RATIO);
    }

    private static int FindRoot(int[] parent, int value)
    {
        int root = value;

        while (parent[root] != root)
        {
            root = parent[root];
        }

        while (parent[value] != value)
        {
            int next = parent[value];
            parent[value] = root;
            value = next;
        }

        return root;
    }

    private static void Union(int[] parent, byte[] rank, int first, int second)
    {
        int firstRoot = FindRoot(parent, first);
        int secondRoot = FindRoot(parent, second);

        if (firstRoot == secondRoot)
        {
            return;
        }

        if (rank[firstRoot] < rank[secondRoot])
        {
            parent[firstRoot] = secondRoot;
            return;
        }

        if (rank[firstRoot] > rank[secondRoot])
        {
            parent[secondRoot] = firstRoot;
            return;
        }

        parent[secondRoot] = firstRoot;
        rank[firstRoot]++;
    }

    private static void DestroyMeshes(IReadOnlyList<Mesh> meshes)
    {
        if (meshes == null)
        {
            return;
        }

        for (int i = 0; i < meshes.Count; i++)
        {
            Mesh mesh = meshes[i];

            if (mesh == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }

    private readonly struct PositionCellKey : IEquatable<PositionCellKey>
    {
        private PositionCellKey(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        private long X { get; }
        private long Y { get; }
        private long Z { get; }

        public static PositionCellKey Create(Vector3 position, float cellSize)
        {
            double inverseCellSize = 1.0 / cellSize;

            return new PositionCellKey(
                (long)Math.Floor(position.x * inverseCellSize),
                (long)Math.Floor(position.y * inverseCellSize),
                (long)Math.Floor(position.z * inverseCellSize));
        }

        public PositionCellKey Offset(int x, int y, int z)
        {
            return new PositionCellKey(X + x, Y + y, Z + z);
        }

        public bool Equals(PositionCellKey other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is PositionCellKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Z.GetHashCode();
                return hash;
            }
        }
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public EdgeKey(int first, int second)
        {
            if (first <= second)
            {
                First = first;
                Second = second;
            }
            else
            {
                First = second;
                Second = first;
            }
        }

        private int First { get; }
        private int Second { get; }

        public bool Equals(EdgeKey other)
        {
            return First == other.First && Second == other.Second;
        }

        public override bool Equals(object obj)
        {
            return obj is EdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (First * 397) ^ Second;
            }
        }
    }
}
