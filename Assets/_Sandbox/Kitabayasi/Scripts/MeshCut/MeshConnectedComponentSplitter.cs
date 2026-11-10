using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 1つのMeshに含まれる、幾何学的につながったTriangle群を個別Meshへ分離する。
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
                out List<TriangleRecord> triangles,
                out failureReason))
        {
            return false;
        }

        float positionEpsilon = CalculatePositionEpsilon(sourceMesh.bounds);
        int[] connectedVertexIds = BuildConnectedVertexIds(vertices, positionEpsilon);

        var parent = new int[triangles.Count];
        var rank = new byte[triangles.Count];
        var edgeOwners = new Dictionary<EdgeKey, int>(triangles.Count * 2);

        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            TriangleRecord triangle = triangles[triangleIndex];
            int first = connectedVertexIds[triangle.FirstVertexIndex];
            int second = connectedVertexIds[triangle.SecondVertexIndex];
            int third = connectedVertexIds[triangle.ThirdVertexIndex];

            RegisterSharedEdge(first, second, triangleIndex, edgeOwners, parent, rank);
            RegisterSharedEdge(second, third, triangleIndex, edgeOwners, parent, rank);
            RegisterSharedEdge(third, first, triangleIndex, edgeOwners, parent, rank);
        }

        var triangleIndicesByRoot = new Dictionary<int, List<int>>();
        var componentRoots = new List<int>();

        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            int root = FindRoot(parent, triangleIndex);

            if (!triangleIndicesByRoot.TryGetValue(root, out List<int> componentTriangleIndices))
            {
                componentTriangleIndices = new List<int>();
                triangleIndicesByRoot.Add(root, componentTriangleIndices);

                // Triangle走査順を保持してComponent名と生成順を決定的にする。
                componentRoots.Add(root);
            }

            componentTriangleIndices.Add(triangleIndex);
        }

        for (int componentIndex = 0; componentIndex < componentRoots.Count; componentIndex++)
        {
            Mesh componentMesh = BuildComponentMesh(
                sourceMesh,
                componentIndex,
                triangleIndicesByRoot[componentRoots[componentIndex]],
                triangles,
                vertices,
                normals,
                uv,
                tangents,
                hasTangents);

            if (componentMesh == null)
            {
                DestroyMeshes(componentMeshes);
                componentMeshes.Clear();
                failureReason = "連結成分から有効なMeshを生成できませんでした。";
                return false;
            }

            componentMeshes.Add(componentMesh);
        }

        if (componentMeshes.Count == 0)
        {
            failureReason = "連結成分が1つも検出されませんでした。";
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
        out List<TriangleRecord> triangles,
        out string failureReason)
    {
        vertices = null;
        normals = null;
        uv = null;
        tangents = null;
        hasTangents = false;
        triangles = null;

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

        if (sourceMesh.vertexCount == 0 || sourceMesh.subMeshCount == 0)
        {
            failureReason = "分離対象Meshに頂点またはSubMeshがありません。";
            return false;
        }

        vertices = sourceMesh.vertices;
        normals = sourceMesh.normals;
        uv = sourceMesh.uv;
        tangents = sourceMesh.tangents;

        if (normals.Length != vertices.Length)
        {
            failureReason = "分離対象Meshの頂点数とNormal数が一致していません。";
            return false;
        }

        if (uv.Length != vertices.Length)
        {
            failureReason = "分離対象Meshの頂点数とUV数が一致していません。";
            return false;
        }

        hasTangents = tangents.Length == vertices.Length;
        triangles = new List<TriangleRecord>();

        for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
        {
            if (sourceMesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                failureReason = $"SubMesh {subMeshIndex} がTriangle topologyではありません。";
                return false;
            }

            int[] subMeshTriangles = sourceMesh.GetTriangles(subMeshIndex);

            for (int offset = 0; offset < subMeshTriangles.Length; offset += 3)
            {
                triangles.Add(new TriangleRecord(
                    subMeshIndex,
                    subMeshTriangles[offset],
                    subMeshTriangles[offset + 1],
                    subMeshTriangles[offset + 2]));
            }
        }

        if (triangles.Count == 0)
        {
            failureReason = "分離対象MeshにTriangleがありません。";
            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// 同一位置とみなせる頂点へ共通IDを割り当てる。
    /// </summary>
    /// <remarks>
    /// MeshPlaneSplitterの出力はTriangleごとに頂点が複製されるため、
    /// index一致では連結を判定できない。隣接セルも確認して許容距離内の頂点だけをまとめる。
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
        Mesh sourceMesh,
        int componentIndex,
        IReadOnlyList<int> componentTriangleIndices,
        IReadOnlyList<TriangleRecord> triangles,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents)
    {
        if (componentTriangleIndices == null || componentTriangleIndices.Count == 0)
        {
            return null;
        }

        int vertexCapacity = componentTriangleIndices.Count * 3;
        var vertices = new List<Vector3>(vertexCapacity);
        var normals = new List<Vector3>(vertexCapacity);
        var uv = new List<Vector2>(vertexCapacity);
        List<Vector4> tangents = hasTangents ? new List<Vector4>(vertexCapacity) : null;
        var trianglesBySubMesh = new List<List<int>>(sourceMesh.subMeshCount);

        for (int subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
        {
            trianglesBySubMesh.Add(new List<int>());
        }

        for (int i = 0; i < componentTriangleIndices.Count; i++)
        {
            TriangleRecord triangle = triangles[componentTriangleIndices[i]];
            List<int> destinationTriangles = trianglesBySubMesh[triangle.SubMeshIndex];

            AddVertex(
                triangle.FirstVertexIndex,
                sourceVertices,
                sourceNormals,
                sourceUv,
                sourceTangents,
                hasTangents,
                vertices,
                normals,
                uv,
                tangents,
                destinationTriangles);

            AddVertex(
                triangle.SecondVertexIndex,
                sourceVertices,
                sourceNormals,
                sourceUv,
                sourceTangents,
                hasTangents,
                vertices,
                normals,
                uv,
                tangents,
                destinationTriangles);

            AddVertex(
                triangle.ThirdVertexIndex,
                sourceVertices,
                sourceNormals,
                sourceUv,
                sourceTangents,
                hasTangents,
                vertices,
                normals,
                uv,
                tangents,
                destinationTriangles);
        }

        var mesh = new Mesh
        {
            name = $"{sourceMesh.name}_Component_{componentIndex:D2}",
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

        mesh.subMeshCount = sourceMesh.subMeshCount;

        for (int subMeshIndex = 0; subMeshIndex < trianglesBySubMesh.Count; subMeshIndex++)
        {
            mesh.SetTriangles(trianglesBySubMesh[subMeshIndex], subMeshIndex, false);
        }

        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddVertex(
        int sourceVertexIndex,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents,
        List<Vector3> destinationVertices,
        List<Vector3> destinationNormals,
        List<Vector2> destinationUv,
        List<Vector4> destinationTangents,
        List<int> destinationTriangles)
    {
        int destinationVertexIndex = destinationVertices.Count;

        destinationVertices.Add(sourceVertices[sourceVertexIndex]);
        destinationNormals.Add(sourceNormals[sourceVertexIndex]);
        destinationUv.Add(sourceUv[sourceVertexIndex]);

        if (hasTangents)
        {
            destinationTangents.Add(sourceTangents[sourceVertexIndex]);
        }

        destinationTriangles.Add(destinationVertexIndex);
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

    private readonly struct TriangleRecord
    {
        public TriangleRecord(
            int subMeshIndex,
            int firstVertexIndex,
            int secondVertexIndex,
            int thirdVertexIndex)
        {
            SubMeshIndex = subMeshIndex;
            FirstVertexIndex = firstVertexIndex;
            SecondVertexIndex = secondVertexIndex;
            ThirdVertexIndex = thirdVertexIndex;
        }

        public int SubMeshIndex { get; }
        public int FirstVertexIndex { get; }
        public int SecondVertexIndex { get; }
        public int ThirdVertexIndex { get; }
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
