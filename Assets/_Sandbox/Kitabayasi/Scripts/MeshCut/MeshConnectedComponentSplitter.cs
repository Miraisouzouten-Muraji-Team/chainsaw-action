using System;
using System.Buffers;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 1つのMeshまたはMesh化前のTriangle集合に含まれる、幾何学的につながったTriangle群を個別Meshへ分離する。
/// </summary>
/// <remarks>
/// 通常のMesh入力ではPositionからGeometry上の頂点同一性を補正する。
/// MeshPlaneSplitterの直接生成経路では、Plane分割中に伝播したConnectivity IDをそのまま使い、
/// Piece完成後のPosition近傍探索を行わない。
/// GameObject生成、Collider/Rigidbody付与、切断Plane生成、飛散処理は担当しない。
/// </remarks>
public sealed class MeshConnectedComponentSplitter
{
    private static readonly ProfilerMarker splitMeshInputProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.MeshInput.Total");

        private static readonly ProfilerMarker splitWorkingDataProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.WorkingData.Total");

        private static readonly ProfilerMarker splitReadDataProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.GroupTriangles");

        private static readonly ProfilerMarker readSourceMeshProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.ReadSourceMesh");

        private static readonly ProfilerMarker buildConnectedVertexIdsProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.BuildConnectedVertexIds");

        private static readonly ProfilerMarker initializeGroupingProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.GroupTriangles.Initialize");

        private static readonly ProfilerMarker registerEdgesProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.GroupTriangles.RegisterEdges");

        private static readonly ProfilerMarker groupRootsProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.GroupTriangles.GroupRoots");

        private static readonly ProfilerMarker buildComponentMeshProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.ConnectedComponents.BuildComponentMesh");

    private const float POSITION_EPSILON_RATIO = 0.00001f;
    private const float MIN_POSITION_EPSILON = 0.000001f;
    private const double POSITION_CELL_SIZE_EPSILON_MULTIPLIER = 2.0;

    // 1体目と2体目以降のBuildComponentMesh Allocation差分を起動時へ移すための調整値。
    // Enemy頂点数の上限ではなく、Profiler結果に応じてこの1箇所だけを調整する。
    private const int COMPONENT_MESH_SCRATCH_LIST_WARMUP_CAPACITY = 65536;

    // UnityEngine.Pool.ListPool<T>へ返すとuGUI等の別システムが同じListを取得できるため、
    // BuildComponentMesh専用のScratch Bufferはこのクラスが所有し続ける。
    // 現在の呼び出しは同期Main Thread上で完結し、同時・再入実行しないことを前提とする。
    private static readonly List<Vector3> componentVerticesScratch =
        new List<Vector3>();

    private static readonly List<Vector3> componentNormalsScratch =
        new List<Vector3>();

    private static readonly List<Vector2> componentUvScratch =
        new List<Vector2>();

    private static readonly List<Vector4> componentTangentsScratch =
        new List<Vector4>();

    private static bool isComponentMeshScratchInUse;

    // SubMesh別Triangle Listは現在のSplitter内で再利用する。
    // 内側ListまでPool化すると返却責務が複雑になるため、今回のEnemy間Pool化対象には含めない。
    private readonly List<List<int>> componentTrianglesBySubMesh =
        new List<List<int>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void WarmUpComponentMeshScratchBuffers()
    {
        isComponentMeshScratchInUse = false;

        componentVerticesScratch.Clear();
        componentNormalsScratch.Clear();
        componentUvScratch.Clear();
        componentTangentsScratch.Clear();

        // 初回Enemy死亡フレームへ集中していたBacking Arrayの拡張Allocationを
        // Scene内MonoBehaviourのAwakeより前へ移し、その後も専用Scratchとして保持する。
        EnsureWarmupCapacity(componentVerticesScratch);
        EnsureWarmupCapacity(componentNormalsScratch);
        EnsureWarmupCapacity(componentUvScratch);
        EnsureWarmupCapacity(componentTangentsScratch);
    }

    private static void EnsureWarmupCapacity<T>(List<T> list)
    {
        // 既により大きなCapacityまで成長済みの場合は縮小しない。
        // 現在のUnity/C# API互換範囲ではEnsureCapacityを使わず、同じ目的をCapacityで満たす。
        if (list.Capacity < COMPONENT_MESH_SCRATCH_LIST_WARMUP_CAPACITY)
        {
            list.Capacity = COMPONENT_MESH_SCRATCH_LIST_WARMUP_CAPACITY;
        }
    }

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
        using (splitMeshInputProfilerMarker.Auto())
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
            null,
            out componentMeshes,
            out failureReason);
    
        }
    }

    /// <summary>
    /// Unity Meshへ確定する前の分割作業バッファから、連結成分だけを最終Mesh化する。
    /// </summary>
    /// <remarks>
    /// MeshPlaneSplitterが構築済みの頂点属性、Triangle index、Connectivity ID、Boundsを直接受け取り、
    /// 中間Mesh生成・TriangleRecordへの全コピー・Bounds算出用の頂点再走査・
    /// Piece完成後のPositionベースConnectivity再構築を行わない。
    /// </remarks>
    internal bool TrySplit(
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
        IReadOnlyList<int> connectedVertexIds,
        Bounds sourceBounds,
        string meshNamePrefix,
        out List<Mesh> componentMeshes,
        out string failureReason)
    {
        using (splitWorkingDataProfilerMarker.Auto())
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

        if (connectedVertexIds == null ||
            connectedVertexIds.Count != sourceVertices.Count)
        {
            failureReason =
                "Connected Component用Connectivity ID数が頂点数と一致していません。";
            return false;
        }

        for (int vertexIndex = 0;
             vertexIndex < connectedVertexIds.Count;
             vertexIndex++)
        {
            if (connectedVertexIds[vertexIndex] < 0)
            {
                failureReason =
                    $"頂点 {vertexIndex} に有効なConnectivity IDが設定されていません。";
                return false;
            }
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
            connectedVertexIds,
            out componentMeshes,
            out failureReason);
    
        }
    }

    private bool TrySplitReadData(
        string meshNamePrefix,
        Bounds sourceBounds,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uv,
        IReadOnlyList<Vector4> tangents,
        bool hasTangents,
        IReadOnlyList<List<int>> trianglesBySubMesh,
        int triangleCount,
        IReadOnlyList<int> precomputedConnectedVertexIds,
        out List<Mesh> componentMeshes,
        out string failureReason)
    {
        using (splitReadDataProfilerMarker.Auto())
        {
        componentMeshes = new List<Mesh>();

        IReadOnlyList<int> connectedVertexIds =
            precomputedConnectedVertexIds;

        if (connectedVertexIds == null)
        {
            float positionEpsilon =
                CalculatePositionEpsilon(sourceBounds);

            connectedVertexIds =
                BuildConnectedVertexIds(vertices, positionEpsilon);
        }

        if (!TryCalculateEdgeTableCapacity(
                triangleCount,
                out int edgeTableCapacity))
        {
            failureReason =
                "Connected Component用Edge TableのCapacityを安全に確保できません。";
            return false;
        }

        int[] parent = null;
        byte[] rank = null;
        ulong[] edgeKeys = null;
        int[] edgeOwnerTriangles = null;
        byte[] edgeOccupied = null;
        int[] firstTriangleIndexBySubMesh = null;
        int[] rootToComponent = null;
        int[] componentIndexByTriangle = null;
        int[] componentCounts = null;
        int[] componentStarts = null;
        int[] componentWriteCursor = null;
        int[] groupedTriangleIndices = null;
        int[] destinationVertexIndexBySourceVertexIndex = null;
        int[] destinationVertexGenerationBySourceVertexIndex = null;
        List<Vector3> componentVertices = componentVerticesScratch;
        List<Vector3> componentNormals = componentNormalsScratch;
        List<Vector2> componentUv = componentUvScratch;
        List<Vector4> componentTangents = componentTangentsScratch;

        if (isComponentMeshScratchInUse)
        {
            failureReason =
                "BuildComponentMesh用Scratch Bufferが既に使用中です。" +
                " MeshConnectedComponentSplitterは同期・非再入呼び出しを前提としています。";
            return false;
        }

        isComponentMeshScratchInUse = true;

        try
        {
            using (initializeGroupingProfilerMarker.Auto())
            {
                // 大きな一時配列をEnemy死亡ごとにnewせず、共有Poolから借りて再利用する。
                parent = ArrayPool<int>.Shared.Rent(triangleCount);
                rank = ArrayPool<byte>.Shared.Rent(triangleCount);
                edgeKeys = ArrayPool<ulong>.Shared.Rent(edgeTableCapacity);
                edgeOwnerTriangles = ArrayPool<int>.Shared.Rent(edgeTableCapacity);
                edgeOccupied = ArrayPool<byte>.Shared.Rent(edgeTableCapacity);
                firstTriangleIndexBySubMesh =
                    ArrayPool<int>.Shared.Rent(trianglesBySubMesh.Count + 1);
                rootToComponent = ArrayPool<int>.Shared.Rent(triangleCount);
                componentIndexByTriangle =
                    ArrayPool<int>.Shared.Rent(triangleCount);
                componentCounts = ArrayPool<int>.Shared.Rent(triangleCount);
                componentStarts = ArrayPool<int>.Shared.Rent(triangleCount);
                componentWriteCursor =
                    ArrayPool<int>.Shared.Rent(triangleCount);
                groupedTriangleIndices =
                    ArrayPool<int>.Shared.Rent(triangleCount);
                destinationVertexIndexBySourceVertexIndex =
                    ArrayPool<int>.Shared.Rent(vertices.Count);
                destinationVertexGenerationBySourceVertexIndex =
                    ArrayPool<int>.Shared.Rent(vertices.Count);

                // Pool配列は前回値を保持し得るため、アルゴリズムが初期値に依存する使用範囲だけ初期化する。
                for (int i = 0; i < triangleCount; i++)
                {
                    parent[i] = i;
                    rank[i] = 0;
                    rootToComponent[i] = -1;
                    componentCounts[i] = 0;
                }

                Array.Clear(edgeOccupied, 0, edgeTableCapacity);
                Array.Clear(
                    destinationVertexGenerationBySourceVertexIndex,
                    0,
                    vertices.Count);

                FillFirstTriangleIndicesBySubMesh(
                    trianglesBySubMesh,
                    firstTriangleIndexBySubMesh);
            }

            int triangleIndex = 0;
            int edgeTableMask = edgeTableCapacity - 1;

            using (registerEdgesProfilerMarker.Auto())
            {
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

                        if (!RegisterSharedEdge(
                                first,
                                second,
                                triangleIndex,
                                edgeKeys,
                                edgeOwnerTriangles,
                                edgeOccupied,
                                edgeTableCapacity,
                                edgeTableMask,
                                parent,
                                rank) ||
                            !RegisterSharedEdge(
                                second,
                                third,
                                triangleIndex,
                                edgeKeys,
                                edgeOwnerTriangles,
                                edgeOccupied,
                                edgeTableCapacity,
                                edgeTableMask,
                                parent,
                                rank) ||
                            !RegisterSharedEdge(
                                third,
                                first,
                                triangleIndex,
                                edgeKeys,
                                edgeOwnerTriangles,
                                edgeOccupied,
                                edgeTableCapacity,
                                edgeTableMask,
                                parent,
                                rank))
                        {
                            failureReason =
                                "Connected Component用Edge Tableに空きSlotがありません。";
                            return false;
                        }

                        triangleIndex++;
                    }
                }
            }

            int componentCount = 0;

            using (groupRootsProfilerMarker.Auto())
            {
                for (triangleIndex = 0;
                     triangleIndex < triangleCount;
                     triangleIndex++)
                {
                    int root =
                        FindRoot(parent, triangleIndex);

                    int componentIndex =
                        rootToComponent[root];

                    if (componentIndex < 0)
                    {
                        // 旧実装と同じく、Global Triangle Index走査でRootを最初に見つけた順をComponent順にする。
                        componentIndex = componentCount;
                        rootToComponent[root] = componentIndex;
                        componentCount++;
                    }

                    componentIndexByTriangle[triangleIndex] =
                        componentIndex;
                    componentCounts[componentIndex]++;
                }

                int groupedWriteStart = 0;

                for (int componentIndex = 0;
                     componentIndex < componentCount;
                     componentIndex++)
                {
                    componentStarts[componentIndex] =
                        groupedWriteStart;
                    componentWriteCursor[componentIndex] =
                        groupedWriteStart;
                    groupedWriteStart +=
                        componentCounts[componentIndex];
                }

                // Global Triangle Index昇順で書き込むことで、各Component内部のTriangle順も旧実装と一致させる。
                for (triangleIndex = 0;
                     triangleIndex < triangleCount;
                     triangleIndex++)
                {
                    int componentIndex =
                        componentIndexByTriangle[triangleIndex];

                    groupedTriangleIndices[
                        componentWriteCursor[componentIndex]++] =
                        triangleIndex;
                }
            }

            componentMeshes.Capacity = componentCount;

            PrepareComponentMeshScratchBuffers(
                trianglesBySubMesh.Count,
                componentVertices,
                componentNormals,
                componentUv,
                componentTangents);

            // Source Vertex Indexは0..Count-1の連続整数なのでHash Dictionaryを使わず直接参照する。
            // Generation配列によりComponent切替時の全配列初期化を不要にする。
            for (int componentIndex = 0;
                 componentIndex < componentCount;
                 componentIndex++)
            {
                Mesh componentMesh =
                    BuildComponentMesh(
                        meshNamePrefix,
                        componentIndex,
                        groupedTriangleIndices,
                        componentStarts[componentIndex],
                        componentCounts[componentIndex],
                        trianglesBySubMesh,
                        firstTriangleIndexBySubMesh,
                        vertices,
                        normals,
                        uv,
                        tangents,
                        hasTangents,
                        componentVertices,
                        componentNormals,
                        componentUv,
                        componentTangents,
                        componentTrianglesBySubMesh,
                        destinationVertexIndexBySourceVertexIndex,
                        destinationVertexGenerationBySourceVertexIndex);

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
        finally
        {
            // Return後の配列を参照しないよう、このメソッドの最後で全Scratch Bufferの所有権をPoolへ返す。
            ReturnPooledArray(parent);
            ReturnPooledArray(rank);
            ReturnPooledArray(edgeKeys);
            ReturnPooledArray(edgeOwnerTriangles);
            ReturnPooledArray(edgeOccupied);
            ReturnPooledArray(firstTriangleIndexBySubMesh);
            ReturnPooledArray(rootToComponent);
            ReturnPooledArray(componentIndexByTriangle);
            ReturnPooledArray(componentCounts);
            ReturnPooledArray(componentStarts);
            ReturnPooledArray(componentWriteCursor);
            ReturnPooledArray(groupedTriangleIndices);
            ReturnPooledArray(destinationVertexIndexBySourceVertexIndex);
            ReturnPooledArray(destinationVertexGenerationBySourceVertexIndex);

            // Countだけを破棄し、CapacityとList自体の所有権はSplitter専用Scratchへ残す。
            // 共有Poolへ返さないため、uGUIや他システムに事前確保済みBacking Arrayを取得されない。
            componentVertices.Clear();
            componentNormals.Clear();
            componentUv.Clear();
            componentTangents.Clear();
            isComponentMeshScratchInUse = false;
        }

        }
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
        using (readSourceMeshProfilerMarker.Auto())
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

    private static void FillFirstTriangleIndicesBySubMesh(
        IReadOnlyList<List<int>> trianglesBySubMesh,
        int[] firstTriangleIndexBySubMesh)
    {
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
    }

    private static bool IsValidVertexIndex(
        int vertexIndex,
        int vertexCount)
    {
        return vertexIndex >= 0 &&
               vertexIndex < vertexCount;
    }

    /// <summary>
    /// Source MeshについてだけPositionベースのConnectivity IDを生成する。
    /// Piece生成後に同じ探索を繰り返さないため、MeshPlaneSplitterから1回だけ利用する。
    /// </summary>
    internal static int[] BuildConnectedVertexIds(
        IReadOnlyList<Vector3> vertices,
        Bounds sourceBounds)
    {
        return BuildConnectedVertexIds(
            vertices,
            CalculatePositionEpsilon(sourceBounds));
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
        using (buildConnectedVertexIdsProfilerMarker.Auto())
        {
        int vertexCount = vertices.Count;
        var connectedVertexIds = new int[vertexCount];

        // CellごとのListを持たず、Cellから代表Vertexの連結リスト先頭だけを引く。
        // Candidate本体はsource vertex indexで表現し、別のPosition配列を複製しない。
        var firstCandidateVertexIndexByCell =
            new Dictionary<PositionCellKey, int>(vertexCount);

        var nextCandidateVertexIndex = new int[vertexCount];

        float epsilonSquared = positionEpsilon * positionEpsilon;
        double cellSize =
            positionEpsilon * POSITION_CELL_SIZE_EPSILON_MULTIPLIER;
        double inverseCellSize = 1.0 / cellSize;

        for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
        {
            Vector3 position = vertices[vertexIndex];
            PositionCellKey cell =
                PositionCellKey.Create(position, inverseCellSize);

            GetNeighborCellOffsetRange(
                position.x,
                cell.X,
                cellSize,
                positionEpsilon,
                out int minimumXOffset,
                out int maximumXOffset);

            GetNeighborCellOffsetRange(
                position.y,
                cell.Y,
                cellSize,
                positionEpsilon,
                out int minimumYOffset,
                out int maximumYOffset);

            GetNeighborCellOffsetRange(
                position.z,
                cell.Z,
                cellSize,
                positionEpsilon,
                out int minimumZOffset,
                out int maximumZOffset);

            int matchingCanonicalId = -1;
            float closestSquaredDistance = float.PositiveInfinity;

            for (int xOffset = minimumXOffset;
                 xOffset <= maximumXOffset;
                 xOffset++)
            {
                long neighborX = cell.X + xOffset;

                for (int yOffset = minimumYOffset;
                     yOffset <= maximumYOffset;
                     yOffset++)
                {
                    long neighborY = cell.Y + yOffset;

                    for (int zOffset = minimumZOffset;
                         zOffset <= maximumZOffset;
                         zOffset++)
                    {
                        var neighborCell =
                            new PositionCellKey(
                                neighborX,
                                neighborY,
                                cell.Z + zOffset);

                        if (!firstCandidateVertexIndexByCell.TryGetValue(
                                neighborCell,
                                out int candidateVertexIndex))
                        {
                            continue;
                        }

                        while (candidateVertexIndex >= 0)
                        {
                            float squaredDistance =
                                (vertices[candidateVertexIndex] - position)
                                .sqrMagnitude;

                            if (squaredDistance <= epsilonSquared &&
                                (squaredDistance < closestSquaredDistance ||
                                 (squaredDistance == closestSquaredDistance &&
                                  candidateVertexIndex < matchingCanonicalId)))
                            {
                                matchingCanonicalId =
                                    candidateVertexIndex;
                                closestSquaredDistance =
                                    squaredDistance;
                            }

                            candidateVertexIndex =
                                nextCandidateVertexIndex[
                                    candidateVertexIndex];
                        }
                    }
                }
            }

            if (matchingCanonicalId < 0)
            {
                // 代表Vertex自身のindexをCanonical IDとして使う。
                // 一意性だけが必要なため、別ID配列やCanonical Position Listは不要。
                matchingCanonicalId = vertexIndex;

                if (firstCandidateVertexIndexByCell.TryGetValue(
                        cell,
                        out int previousFirstCandidateVertexIndex))
                {
                    nextCandidateVertexIndex[vertexIndex] =
                        previousFirstCandidateVertexIndex;
                    firstCandidateVertexIndexByCell[cell] =
                        vertexIndex;
                }
                else
                {
                    nextCandidateVertexIndex[vertexIndex] = -1;
                    firstCandidateVertexIndexByCell.Add(
                        cell,
                        vertexIndex);
                }
            }

            connectedVertexIds[vertexIndex] =
                matchingCanonicalId;
        }

        return connectedVertexIds;

        }
    }

    /// <summary>
    /// Positionがepsilon以内で越え得るCell境界だけを探索対象にする。
    /// </summary>
    /// <remarks>
    /// Cell幅をepsilonの2倍にしているため、通常は各軸で現在Cellと片側Neighborの最大2候補となる。
    /// 下側境界はCell自身に含まれるため、境界までちょうどepsilonの場合は下側Neighborを追加しない。
    /// </remarks>
    private static void GetNeighborCellOffsetRange(
        float coordinate,
        long cellCoordinate,
        double cellSize,
        float positionEpsilon,
        out int minimumOffset,
        out int maximumOffset)
    {
        double localCoordinate =
            coordinate - (cellCoordinate * cellSize);

        // Floor計算と境界再計算の丸め差でCell外へごく僅かにはみ出す場合だけ補正する。
        if (localCoordinate < 0.0)
        {
            localCoordinate = 0.0;
        }
        else if (localCoordinate > cellSize)
        {
            localCoordinate = cellSize;
        }

        minimumOffset =
            localCoordinate < positionEpsilon
                ? -1
                : 0;

        maximumOffset =
            cellSize - localCoordinate <= positionEpsilon
                ? 1
                : 0;
    }

    private static bool TryCalculateEdgeTableCapacity(
        int triangleCount,
        out int capacity)
    {
        const int maximumPowerOfTwoCapacity = 1 << 30;

        long maximumRegisteredEdgeCount =
            (long)triangleCount * 3L;

        // 最大Unique Edge数に対してLoad Factorを2/3以下にし、Linear Probingの探索距離を抑える。
        long minimumCapacity =
            maximumRegisteredEdgeCount +
            ((maximumRegisteredEdgeCount + 1L) / 2L) +
            1L;

        if (minimumCapacity > maximumPowerOfTwoCapacity)
        {
            capacity = 0;
            return false;
        }

        int powerOfTwoCapacity = 1;

        while (powerOfTwoCapacity < minimumCapacity)
        {
            powerOfTwoCapacity <<= 1;
        }

        capacity = powerOfTwoCapacity;
        return true;
    }

    private static bool RegisterSharedEdge(
        int first,
        int second,
        int triangleIndex,
        ulong[] edgeKeys,
        int[] edgeOwnerTriangles,
        byte[] edgeOccupied,
        int edgeTableCapacity,
        int edgeTableMask,
        int[] parent,
        byte[] rank)
    {
        // 長さ0の辺は別Triangle同士を誤って接続する原因になるため使わない。
        if (first == second)
        {
            return true;
        }

        ulong edgeKey = PackEdgeKey(first, second);
        int slotIndex =
            GetEdgeHashIndex(edgeKey, edgeTableMask);

        for (int probeCount = 0;
             probeCount < edgeTableCapacity;
             probeCount++)
        {
            if (edgeOccupied[slotIndex] == 0)
            {
                edgeOccupied[slotIndex] = 1;
                edgeKeys[slotIndex] = edgeKey;
                edgeOwnerTriangles[slotIndex] = triangleIndex;
                return true;
            }

            if (edgeKeys[slotIndex] == edgeKey)
            {
                // 最初のOwnerを上書きせず、Non-manifold Edgeも従来通り最初のTriangleへUnionする。
                Union(
                    parent,
                    rank,
                    edgeOwnerTriangles[slotIndex],
                    triangleIndex);
                return true;
            }

            slotIndex =
                (slotIndex + 1) & edgeTableMask;
        }

        return false;
    }

    private static ulong PackEdgeKey(int first, int second)
    {
        uint minimum =
            (uint)(first < second ? first : second);
        uint maximum =
            (uint)(first < second ? second : first);

        return ((ulong)minimum << 32) | maximum;
    }

    private static int GetEdgeHashIndex(
        ulong edgeKey,
        int edgeTableMask)
    {
        unchecked
        {
            // Packed Vertex IDの上位/下位双方を混ぜ、連番IDでもLinear Probingが偏りにくいHashにする。
            ulong hash = edgeKey;
            hash ^= hash >> 33;
            hash *= 0xff51afd7ed558ccdUL;
            hash ^= hash >> 33;
            hash *= 0xc4ceb9fe1a85ec53UL;
            hash ^= hash >> 33;

            return (int)hash & edgeTableMask;
        }
    }

    private void PrepareComponentMeshScratchBuffers(
        int requiredSubMeshCount,
        List<Vector3> componentVertices,
        List<Vector3> componentNormals,
        List<Vector2> componentUv,
        List<Vector4> componentTangents)
    {
        // 前回利用時の要素を持ち越さず、CapacityだけをEnemy間で再利用する。
        // 起動時の事前確保を超えた場合だけ、実際に必要な分まで自然成長させる。
        componentVertices.Clear();
        componentNormals.Clear();
        componentUv.Clear();
        componentTangents.Clear();

        for (int subMeshIndex = 0;
             subMeshIndex < componentTrianglesBySubMesh.Count;
             subMeshIndex++)
        {
            componentTrianglesBySubMesh[subMeshIndex].Clear();
        }

        while (componentTrianglesBySubMesh.Count < requiredSubMeshCount)
        {
            componentTrianglesBySubMesh.Add(new List<int>());
        }
    }

    private static void ReturnPooledArray<T>(T[] array)
    {
        if (array != null)
        {
            ArrayPool<T>.Shared.Return(array);
        }
    }

    private static Mesh BuildComponentMesh(
        string meshNamePrefix,
        int componentIndex,
        int[] groupedTriangleIndices,
        int componentStartIndex,
        int componentTriangleCount,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
        int[] firstTriangleIndexBySubMesh,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uv,
        List<Vector4> tangents,
        List<List<int>> trianglesBySubMesh,
        int[] destinationVertexIndexBySourceVertexIndex,
        int[] destinationVertexGenerationBySourceVertexIndex)
    {
        using (buildComponentMeshProfilerMarker.Auto())
        {
        if (groupedTriangleIndices == null ||
            componentTriangleCount <= 0)
        {
            return null;
        }

        vertices.Clear();
        normals.Clear();
        uv.Clear();

        tangents.Clear();

        for (int subMeshIndex = 0;
             subMeshIndex < sourceTrianglesBySubMesh.Count;
             subMeshIndex++)
        {
            trianglesBySubMesh[subMeshIndex].Clear();
        }

        bool hasBounds = false;
        Vector3 minimum = default;
        Vector3 maximum = default;

        // 0は未使用Generationとして残し、Component Index + 1を現在の世代番号に使う。
        int destinationVertexGeneration = componentIndex + 1;

        // GroupRootsはGlobal Triangle Indexの昇順でComponentへ追加する。
        // その順序を利用し、TriangleごとのSubMesh二分探索を前進カーソルへ置き換える。
        int subMeshCursor = 0;

        for (int i = 0;
             i < componentTriangleCount;
             i++)
        {
            int triangleIndex =
                groupedTriangleIndices[componentStartIndex + i];

            while (triangleIndex >=
                   firstTriangleIndexBySubMesh[subMeshCursor + 1])
            {
                subMeshCursor++;
            }

            int triangleOffset =
                (triangleIndex -
                 firstTriangleIndexBySubMesh[subMeshCursor]) * 3;

            IReadOnlyList<int> sourceTriangles =
                sourceTrianglesBySubMesh[subMeshCursor];

            List<int> destinationTriangles =
                trianglesBySubMesh[subMeshCursor];

            destinationTriangles.Add(
                GetOrAddVertex(
                    sourceTriangles[triangleOffset],
                    sourceVertices,
                    sourceNormals,
                    sourceUv,
                    sourceTangents,
                    hasTangents,
                    destinationVertexGeneration,
                    destinationVertexIndexBySourceVertexIndex,
                    destinationVertexGenerationBySourceVertexIndex,
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
                    destinationVertexGeneration,
                    destinationVertexIndexBySourceVertexIndex,
                    destinationVertexGenerationBySourceVertexIndex,
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
                    destinationVertexGeneration,
                    destinationVertexIndexBySourceVertexIndex,
                    destinationVertexGenerationBySourceVertexIndex,
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
             subMeshIndex < sourceTrianglesBySubMesh.Count;
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
    }

    private static int GetOrAddVertex(
        int sourceVertexIndex,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<Vector3> sourceNormals,
        IReadOnlyList<Vector2> sourceUv,
        IReadOnlyList<Vector4> sourceTangents,
        bool hasTangents,
        int destinationVertexGeneration,
        int[] destinationVertexIndexBySourceVertexIndex,
        int[] destinationVertexGenerationBySourceVertexIndex,
        List<Vector3> destinationVertices,
        List<Vector3> destinationNormals,
        List<Vector2> destinationUv,
        List<Vector4> destinationTangents,
        ref bool hasBounds,
        ref Vector3 minimum,
        ref Vector3 maximum)
    {
        if (destinationVertexGenerationBySourceVertexIndex[sourceVertexIndex] ==
            destinationVertexGeneration)
        {
            return destinationVertexIndexBySourceVertexIndex[sourceVertexIndex];
        }

        int destinationVertexIndex =
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

        destinationVertexIndexBySourceVertexIndex[sourceVertexIndex] =
            destinationVertexIndex;
        destinationVertexGenerationBySourceVertexIndex[sourceVertexIndex] =
            destinationVertexGeneration;

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
        public PositionCellKey(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public long X { get; }
        public long Y { get; }
        public long Z { get; }

        public static PositionCellKey Create(
            Vector3 position,
            double inverseCellSize)
        {
            return new PositionCellKey(
                (long)Math.Floor(position.x * inverseCellSize),
                (long)Math.Floor(position.y * inverseCellSize),
                (long)Math.Floor(position.z * inverseCellSize));
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

}
