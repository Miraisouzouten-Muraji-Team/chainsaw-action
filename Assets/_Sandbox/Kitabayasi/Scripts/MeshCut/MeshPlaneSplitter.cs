using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 指定されたPlaneでMeshを2つへ分割し、切断面を生成する。
/// </summary>
/// <remarks>
/// Enemy、攻撃、チェンソー、死亡処理、Rigidbody、Collider、飛散、分割回数は扱わない。
/// 入力MeshとPlaneが同じ座標空間で表現されていることを前提とする。
/// </remarks>
public sealed class MeshPlaneSplitter
{
    private static readonly ProfilerMarker splitTotalProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.Total");

        private static readonly ProfilerMarker splitGeometryProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles");

        private static readonly ProfilerMarker buildBoundaryLoopsProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.BuildBoundaryLoops");

        private static readonly ProfilerMarker addCutSurfaceCapsProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.BuildCutSurfaceCaps");

        private static readonly ProfilerMarker triangulateCapsProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.TriangulateCaps");

        private static readonly ProfilerMarker buildFinalMeshProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.BuildFinalMesh");

        private static readonly ProfilerMarker readTriangleDataProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.ReadTriangleData");

        private static readonly ProfilerMarker readSourceAttributesProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.ReadTriangleData.ReadSourceAttributes");

        private static readonly ProfilerMarker classifyTriangleProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.ClassifyTriangle");

        private static readonly ProfilerMarker addUncutTriangleProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.AddUncutTriangle");

        private static readonly ProfilerMarker processIntersectingTriangleProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.ProcessIntersectingTriangle");

        private static readonly ProfilerMarker calculateIntersectionsProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.CalculateIntersections");

        private static readonly ProfilerMarker buildClippedGeometryProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.BuildClippedGeometry");

        private static readonly ProfilerMarker addClippedTrianglesProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.AddClippedTriangles");

        private static readonly ProfilerMarker registerBoundaryProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.ProcessTriangles.RegisterBoundary");

        private static readonly ProfilerMarker builderResolveVerticesProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.PieceBuilder.ResolveOrAddVertices");

        private static readonly ProfilerMarker builderAppendTriangleIndicesProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.PieceBuilder.AppendTriangleIndices");

    private const float GEOMETRY_EPSILON_RATIO = 0.00001f;
    private const float MIN_GEOMETRY_EPSILON = 0.000001f;
    private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.00000001f;

    internal const byte TRIANGLE_CLASSIFICATION_POSITIVE = 1;
    internal const byte TRIANGLE_CLASSIFICATION_NEGATIVE = 2;
    internal const byte TRIANGLE_CLASSIFICATION_INTERSECTING = 3;

    private const int MAX_BOUNDARY_DIAGNOSTIC_NODE_DETAILS = 20;
    private const int MAX_DEGENERATE_SEGMENT_DIAGNOSTIC_DETAILS = 30;
    private const int MAX_NEARBY_ENDPOINT_DIAGNOSTIC_DETAILS = 20;
    private const int MAX_SOURCE_TOPOLOGY_DIAGNOSTIC_TRIANGLES = 20;
    private const float NEARBY_BOUNDARY_ENDPOINT_SEARCH_SCALE = 4.0f;
    private const float SOURCE_SPLIT_VERTEX_MATCH_EPSILON_SCALE = 0.1f;

    private readonly MeshConnectedComponentSplitter connectedComponentSplitter =
        new MeshConnectedComponentSplitter();

    /// <summary>
    /// 指定PlaneでMeshを分割する。
    /// </summary>
    /// <param name="sourceMesh">分割対象。Planeと同じローカル空間で頂点を保持しているMesh。</param>
    /// <param name="cutPlane">sourceMeshと同じ空間で定義された切断Plane。</param>
    /// <param name="cutSurfaceUvScale">新しく生成する切断面UVのスケール。0より大きい値を指定する。</param>
    public bool TrySplit(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out string failureReason)
    {
        return TrySplit(
            sourceMesh,
            cutPlane,
            cutSurfaceUvScale,
            "MeshPlaneSplit_FirstPiece",
            "MeshPlaneSplit_SecondPiece",
            null,
            false,
            out firstPieceMesh,
            out secondPieceMesh,
            out failureReason);
    }

    /// <summary>
    /// Mesh分割に加えて、生成された切断境界へ利用側固有の制約を適用する。
    /// </summary>
    /// <remarks>
    /// 境界検証はCutSegment生成後かつCap生成前に行う。通常攻撃のCut Rangeのような
    /// 利用側固有ルールをMesh分割アルゴリズムへ依存させず、従来の失敗タイミングを維持するための内部入口。
    /// </remarks>
    internal bool TrySplit(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        string firstPieceMeshName,
        string secondPieceMeshName,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out string failureReason)
    {
        return TrySplitInternal(
            sourceMesh,
            cutPlane,
            cutSurfaceUvScale,
            null,
            null,
            null,
            null,
            firstPieceMeshName,
            secondPieceMeshName,
            cutBoundaryValidator,
            enableDiagnostics,
            false,
            out firstPieceMesh,
            out secondPieceMesh,
            out _,
            out failureReason);
    }

    /// <summary>
    /// 事前に同じPlaneで検証済みのTriangle情報を再利用してMeshを分割する。
    /// </summary>
    /// <remarks>
    /// 通常攻撃のCut Plane検証で確定済みの頂点距離とTriangle分類を再利用し、
    /// 採用Planeに対するGetTriangles・Plane距離計算・Triangle再分類を繰り返さない。
    /// Mesh構築、頂点属性補間、切断面生成は従来どおりこのクラスが担当する。
    /// </remarks>
    internal bool TrySplit(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        Vector3[] sourceVertices,
        IReadOnlyList<int[]> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        string firstPieceMeshName,
        string secondPieceMeshName,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out string failureReason)
    {
        return TrySplitInternal(
            sourceMesh,
            cutPlane,
            cutSurfaceUvScale,
            sourceVertices,
            sourceTrianglesBySubMesh,
            sourceVertexPlaneDistances,
            triangleClassificationsBySubMesh,
            firstPieceMeshName,
            secondPieceMeshName,
            cutBoundaryValidator,
            enableDiagnostics,
            false,
            out firstPieceMesh,
            out secondPieceMesh,
            out _,
            out failureReason);
    }

    /// <summary>
    /// Plane分割結果を中間Unity Meshへ確定せず、連結成分単位の最終Meshを直接生成する。
    /// </summary>
    /// <remarks>
    /// 通常攻撃死亡経路専用。Plane分割中に構築した頂点属性とTriangle indexを
    /// MeshConnectedComponentSplitterへ直接渡し、First / Secondの中間Mesh読戻しを発生させない。
    /// </remarks>
    internal bool TrySplitConnectedPieces(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        Vector3[] sourceVertices,
        IReadOnlyList<int[]> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        string firstSideMeshNamePrefix,
        string secondSideMeshNamePrefix,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        out List<Mesh> pieceMeshes,
        out string failureReason)
    {
        bool succeeded =
            TrySplitInternal(
                sourceMesh,
                cutPlane,
                cutSurfaceUvScale,
                sourceVertices,
                sourceTrianglesBySubMesh,
                sourceVertexPlaneDistances,
                triangleClassificationsBySubMesh,
                firstSideMeshNamePrefix,
                secondSideMeshNamePrefix,
                cutBoundaryValidator,
                enableDiagnostics,
                true,
                out _,
                out _,
                out pieceMeshes,
                out failureReason);

        if (!succeeded && pieceMeshes == null)
        {
            pieceMeshes = new List<Mesh>();
        }

        return succeeded;
    }

    private bool TrySplitInternal(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        Vector3[] sourceVertices,
        IReadOnlyList<int[]> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        string firstPieceMeshName,
        string secondPieceMeshName,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        bool splitConnectedComponents,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out List<Mesh> connectedPieceMeshes,
        out string failureReason)
    {
        using (splitTotalProfilerMarker.Auto())
        {
        firstPieceMesh = null;
        secondPieceMesh = null;
        connectedPieceMeshes = null;

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        enableDiagnostics = false;
#endif

        if (!ValidateInputs(
                sourceMesh,
                cutPlane,
                cutSurfaceUvScale,
                out failureReason))
        {
            return false;
        }

        bool hasCachedTriangleValidation =
            sourceVertices != null ||
            sourceTrianglesBySubMesh != null ||
            sourceVertexPlaneDistances != null ||
            triangleClassificationsBySubMesh != null;

        if (hasCachedTriangleValidation &&
            !ValidateCachedTriangleValidation(
                sourceMesh,
                sourceVertices,
                sourceTrianglesBySubMesh,
                sourceVertexPlaneDistances,
                triangleClassificationsBySubMesh,
                out failureReason))
        {
            if (splitConnectedComponents)
            {
                failureReason =
                    "通常攻撃の事前検証済みTriangle情報が不正なため、" +
                    "Source Meshの再走査へフォールバックせず分割を中止します。 " +
                    failureReason;
                return false;
            }

            // 従来の2Mesh分割経路では互換性を維持し、キャッシュ不整合時のみ再走査へ戻す。
            sourceVertices = null;
            sourceTrianglesBySubMesh = null;
            sourceVertexPlaneDistances = null;
            triangleClassificationsBySubMesh = null;
            failureReason = null;
        }

        float geometryEpsilon =
            CalculateGeometryEpsilon(sourceMesh.bounds);

        return TrySplitMesh(
            sourceMesh,
            cutPlane,
            cutSurfaceUvScale,
            geometryEpsilon,
            sourceVertices,
            sourceTrianglesBySubMesh,
            sourceVertexPlaneDistances,
            triangleClassificationsBySubMesh,
            firstPieceMeshName,
            secondPieceMeshName,
            cutBoundaryValidator,
            enableDiagnostics,
            splitConnectedComponents,
            out firstPieceMesh,
            out secondPieceMesh,
            out connectedPieceMeshes,
            out failureReason);
    
        }
    }

    internal static float CalculateGeometryEpsilon(Bounds bounds)
    {
        return Mathf.Max(
            bounds.size.magnitude * GEOMETRY_EPSILON_RATIO,
            MIN_GEOMETRY_EPSILON);
    }

    internal static byte ClassifyTriangle(
        float distanceA,
        float distanceB,
        float distanceC,
        float geometryEpsilon)
    {
        bool hasPositiveVertex =
            distanceA > geometryEpsilon ||
            distanceB > geometryEpsilon ||
            distanceC > geometryEpsilon;

        bool hasNegativeVertex =
            distanceA < -geometryEpsilon ||
            distanceB < -geometryEpsilon ||
            distanceC < -geometryEpsilon;

        // 従来処理と同じ優先順で、Plane上だけのTriangleはPositive側へ残す。
        if (!hasNegativeVertex)
        {
            return TRIANGLE_CLASSIFICATION_POSITIVE;
        }

        if (!hasPositiveVertex)
        {
            return TRIANGLE_CLASSIFICATION_NEGATIVE;
        }

        return TRIANGLE_CLASSIFICATION_INTERSECTING;
    }

    private static bool ValidateCachedTriangleValidation(
        Mesh sourceMesh,
        Vector3[] sourceVertices,
        IReadOnlyList<int[]> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        out string failureReason)
    {
        if (sourceVertices == null ||
            sourceVertices.Length != sourceMesh.vertexCount ||
            sourceVertexPlaneDistances == null ||
            sourceVertexPlaneDistances.Length != sourceVertices.Length ||
            sourceTrianglesBySubMesh == null ||
            sourceTrianglesBySubMesh.Count != sourceMesh.subMeshCount ||
            triangleClassificationsBySubMesh == null ||
            triangleClassificationsBySubMesh.Count != sourceMesh.subMeshCount)
        {
            failureReason =
                "事前検証済みTriangle情報がSource Meshの構成と一致していません。";
            return false;
        }

        for (int subMeshIndex = 0;
             subMeshIndex < sourceMesh.subMeshCount;
             subMeshIndex++)
        {
            int[] triangles =
                sourceTrianglesBySubMesh[subMeshIndex];

            byte[] triangleClassifications =
                triangleClassificationsBySubMesh[subMeshIndex];

            if (triangles == null ||
                triangles.Length % 3 != 0 ||
                triangleClassifications == null ||
                triangleClassifications.Length != triangles.Length / 3)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} の事前検証済みTriangle情報が不正です。";
                return false;
            }
        }

        failureReason = null;
        return true;
    }

    private static bool ValidateInputs(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        out string failureReason)
    {
        if (sourceMesh == null)
        {
            failureReason = "分割対象Meshがnullです。";
            return false;
        }

        if (sourceMesh.vertexCount == 0)
        {
            failureReason = "分割対象Meshに頂点がありません。";
            return false;
        }

        if (sourceMesh.subMeshCount == 0)
        {
            failureReason = "分割対象MeshにSubMeshがありません。";
            return false;
        }

        if (!IsFinite(cutPlane.normal) ||
            !IsFinite(cutPlane.distance) ||
            cutPlane.normal.sqrMagnitude <= MIN_DIRECTION_SQR_MAGNITUDE)
        {
            failureReason = "切断Planeに有効なNormal / Distanceがありません。";
            return false;
        }

        if (cutSurfaceUvScale <= 0.0f)
        {
            failureReason = "cutSurfaceUvScaleは0より大きい値である必要があります。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }

    private bool TrySplitMesh(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        float geometryEpsilon,
        Vector3[] cachedSourceVertices,
        IReadOnlyList<int[]> cachedSourceTrianglesBySubMesh,
        float[] cachedSourceVertexPlaneDistances,
        IReadOnlyList<byte[]> cachedTriangleClassificationsBySubMesh,
        string firstPieceMeshName,
        string secondPieceMeshName,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        bool splitConnectedComponents,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out List<Mesh> connectedPieceMeshes,
        out string failureReason)
    {
        using (splitGeometryProfilerMarker.Auto())
        {
        firstPieceMesh = null;
        secondPieceMesh = null;
        connectedPieceMeshes = null;

        bool useCachedTriangleValidation =
            cachedSourceVertices != null;

        Vector3[] sourceVertices;
        Vector3[] sourceNormals;
        Vector2[] sourceUv;
        Vector4[] sourceTangents;

        using (readTriangleDataProfilerMarker.Auto())
        {
            using (readSourceAttributesProfilerMarker.Auto())
            {
                sourceVertices =
                    useCachedTriangleValidation
                        ? cachedSourceVertices
                        : sourceMesh.vertices;

                sourceNormals = sourceMesh.normals;
                sourceUv = sourceMesh.uv;
                sourceTangents = sourceMesh.tangents;
            }
        }

        if (sourceNormals == null ||
            sourceNormals.Length != sourceVertices.Length)
        {
            failureReason =
                "切断対象Meshに頂点数と一致するNormalがありません。";
            return false;
        }

        bool hasUv = sourceUv != null && sourceUv.Length > 0;
        if (hasUv && sourceUv.Length != sourceVertices.Length)
        {
            failureReason =
                "切断対象MeshのUV0数が頂点数と一致していません。";
            return false;
        }

        bool hasTangents =
            sourceTangents != null && sourceTangents.Length > 0;
        if (hasTangents && sourceTangents.Length != sourceVertices.Length)
        {
            failureReason =
                "切断対象MeshのTangent数が頂点数と一致していません。";
            return false;
        }

        int originalSubMeshCount = sourceMesh.subMeshCount;
        int cutSurfaceSubMeshIndex = originalSubMeshCount;

        int[] sourceConnectivityVertexIds =
            splitConnectedComponents
                ? MeshConnectedComponentSplitter.BuildConnectedVertexIds(
                    sourceVertices,
                    sourceMesh.bounds)
                : null;

        int nextConnectivityVertexId =
            sourceConnectivityVertexIds != null
                ? GetNextConnectivityVertexId(sourceConnectivityVertexIds)
                : 0;

        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge =
            splitConnectedComponents
                ? new Dictionary<long, int>()
                : null;

        int estimatedPieceVertexCapacity =
            CalculateInitialPieceVertexCapacity(
                sourceVertices.Length);

        var firstBuilder = new PieceMeshBuilder(
            originalSubMeshCount + 1,
            hasTangents,
            estimatedPieceVertexCapacity);

        var secondBuilder = new PieceMeshBuilder(
            originalSubMeshCount + 1,
            hasTangents,
            estimatedPieceVertexCapacity);

        var cutSegments = new List<CutSegment>();

        // Triangle単位で一時Collectionを生成しないため、分割処理中だけ使う作業領域を再利用する。
        // List.Clear()後もCapacityは維持されるため、交差Triangle数に比例したManaged Allocationを避けられる。
        var triangleIndices = new List<int>();
        var clippedPolygon = new List<VertexData>(4);
        var cutPoints = new List<Vector3>(3);
        var cutPointSourceEdgeVertexIndexA = new List<int>(3);
        var cutPointSourceEdgeVertexIndexB = new List<int>(3);
        var cutPointConnectivityVertexIds = new List<int>(3);

        int positiveOnlyTriangleCount = 0;
        int negativeOnlyTriangleCount = 0;
        int intersectingTriangleCount = 0;
        int degenerateCutSegmentCount = 0;
        int loggedDegenerateCutSegmentCount = 0;

        for (int subMeshIndex = 0;
             subMeshIndex < originalSubMeshCount;
             subMeshIndex++)
        {
            if (sourceMesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} がTriangles topologyではありません。";
                return false;
            }

            IReadOnlyList<int> sourceTriangleIndices;

            if (useCachedTriangleValidation)
            {
                // Cut Plane検証で取得済みのSubMesh index配列をそのまま使い、
                // 採用Planeの分割開始時にGetTrianglesを再実行しない。
                sourceTriangleIndices =
                    cachedSourceTrianglesBySubMesh[subMeshIndex];
            }
            else
            {
                // 通常経路では同じListへ書き込み、SubMeshごとの配列Allocationを避ける。
                using (readTriangleDataProfilerMarker.Auto())
                {
                    sourceMesh.GetTriangles(
                        triangleIndices,
                        subMeshIndex);
                }

                sourceTriangleIndices =
                    triangleIndices;
            }

            if (sourceTriangleIndices.Count % 3 != 0)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} のTriangle index数が不正です。";
                return false;
            }

            int estimatedSubMeshIndexCapacity =
                CalculateInitialPieceTriangleIndexCapacity(
                    sourceTriangleIndices.Count);

            firstBuilder.SetInitialSubMeshIndexCapacity(
                subMeshIndex,
                estimatedSubMeshIndexCapacity);

            secondBuilder.SetInitialSubMeshIndexCapacity(
                subMeshIndex,
                estimatedSubMeshIndexCapacity);

            for (int triangleIndex = 0;
                 triangleIndex < sourceTriangleIndices.Count;
                 triangleIndex += 3)
            {
                int indexA;
                int indexB;
                int indexC;
                VertexData vertexA;
                VertexData vertexB;
                VertexData vertexC;

                using (readTriangleDataProfilerMarker.Auto())
                {
                    indexA = sourceTriangleIndices[triangleIndex];
                    indexB = sourceTriangleIndices[triangleIndex + 1];
                    indexC = sourceTriangleIndices[triangleIndex + 2];

                    if (!IsValidVertexIndex(indexA, sourceVertices.Length) ||
                        !IsValidVertexIndex(indexB, sourceVertices.Length) ||
                        !IsValidVertexIndex(indexC, sourceVertices.Length))
                    {
                        failureReason =
                            $"SubMesh {subMeshIndex} に範囲外の頂点indexがあります。";
                        return false;
                    }

                    vertexA =
                        CreateVertexData(
                            indexA,
                            sourceVertices,
                            sourceNormals,
                            sourceUv,
                            sourceTangents,
                            hasUv,
                            hasTangents,
                            sourceConnectivityVertexIds);

                    vertexB =
                        CreateVertexData(
                            indexB,
                            sourceVertices,
                            sourceNormals,
                            sourceUv,
                            sourceTangents,
                            hasUv,
                            hasTangents,
                            sourceConnectivityVertexIds);

                    vertexC =
                        CreateVertexData(
                            indexC,
                            sourceVertices,
                            sourceNormals,
                            sourceUv,
                            sourceTangents,
                            hasUv,
                            hasTangents,
                            sourceConnectivityVertexIds);
                }

                float distanceA;
                float distanceB;
                float distanceC;
                byte triangleClassification;

                using (classifyTriangleProfilerMarker.Auto())
                {
                    if (useCachedTriangleValidation)
                    {
                        // Cut Plane検証時と同じ距離・分類を使う。
                        // ここではGetDistanceToPointとPositive/Negative判定を再実行しない。
                        distanceA =
                            cachedSourceVertexPlaneDistances[indexA];

                        distanceB =
                            cachedSourceVertexPlaneDistances[indexB];

                        distanceC =
                            cachedSourceVertexPlaneDistances[indexC];

                        triangleClassification =
                            cachedTriangleClassificationsBySubMesh
                                [subMeshIndex]
                                [triangleIndex / 3];
                    }
                    else
                    {
                        distanceA =
                            cutPlane.GetDistanceToPoint(
                                vertexA.Position);

                        distanceB =
                            cutPlane.GetDistanceToPoint(
                                vertexB.Position);

                        distanceC =
                            cutPlane.GetDistanceToPoint(
                                vertexC.Position);

                        triangleClassification =
                            ClassifyTriangle(
                                distanceA,
                                distanceB,
                                distanceC,
                                geometryEpsilon);
                    }

                    if (!IsFinite(distanceA) ||
                        !IsFinite(distanceB) ||
                        !IsFinite(distanceC))
                    {
                        failureReason =
                            "PlaneとTriangleの距離計算結果にNaN / Infinityを検出しました。";
                        return false;
                    }
                }

                if (triangleClassification ==
                    TRIANGLE_CLASSIFICATION_POSITIVE)
                {
                    if (enableDiagnostics)
                    {
                        positiveOnlyTriangleCount++;
                    }

                    using (addUncutTriangleProfilerMarker.Auto())
                    {
                        firstBuilder.AddTriangle(
                            vertexA,
                            vertexB,
                            vertexC,
                            subMeshIndex,
                            false);
                    }

                    continue;
                }

                if (triangleClassification ==
                    TRIANGLE_CLASSIFICATION_NEGATIVE)
                {
                    if (enableDiagnostics)
                    {
                        negativeOnlyTriangleCount++;
                    }

                    using (addUncutTriangleProfilerMarker.Auto())
                    {
                        secondBuilder.AddTriangle(
                            vertexA,
                            vertexB,
                            vertexC,
                            subMeshIndex,
                            false);
                    }

                    continue;
                }

                if (triangleClassification !=
                    TRIANGLE_CLASSIFICATION_INTERSECTING)
                {
                    failureReason =
                        "Triangle分類結果に未定義値が含まれています。";
                    return false;
                }

                if (enableDiagnostics)
                {
                    intersectingTriangleCount++;
                }

                using (processIntersectingTriangleProfilerMarker.Auto())
                {
                    using (buildClippedGeometryProfilerMarker.Auto())
                    {
                        ClipTriangle(
                            vertexA,
                            vertexB,
                            vertexC,
                            distanceA,
                            distanceB,
                            distanceC,
                            true,
                            geometryEpsilon,
                            clippedPolygon,
                            cutConnectivityVertexIdsBySourceEdge,
                            ref nextConnectivityVertexId);
                    }

                    using (addClippedTrianglesProfilerMarker.Auto())
                    {
                        firstBuilder.AddConvexPolygon(
                            clippedPolygon,
                            subMeshIndex,
                            false);
                    }

                    using (buildClippedGeometryProfilerMarker.Auto())
                    {
                        ClipTriangle(
                            vertexA,
                            vertexB,
                            vertexC,
                            distanceA,
                            distanceB,
                            distanceC,
                            false,
                            geometryEpsilon,
                            clippedPolygon,
                            cutConnectivityVertexIdsBySourceEdge,
                            ref nextConnectivityVertexId);
                    }

                    using (addClippedTrianglesProfilerMarker.Auto())
                    {
                        secondBuilder.AddConvexPolygon(
                            clippedPolygon,
                            subMeshIndex,
                            false);
                    }

                    using (registerBoundaryProfilerMarker.Auto())
                    {
                        if (!TryGetCutSegment(
                                vertexA,
                                vertexB,
                                vertexC,
                                indexA,
                                indexB,
                                indexC,
                                distanceA,
                                distanceB,
                                distanceC,
                                geometryEpsilon,
                                cutPoints,
                                cutPointSourceEdgeVertexIndexA,
                                cutPointSourceEdgeVertexIndexB,
                                cutPointConnectivityVertexIds,
                                cutConnectivityVertexIdsBySourceEdge,
                                ref nextConnectivityVertexId,
                                out CutSegment cutSegment,
                                out bool isDegenerateSegment))
                        {
                            // 交点が1点しか得られないTriangleは、Planeが頂点へ接触しただけ等で
                            // 境界Edgeを構成しない局所的な縮退として扱う。
                            // 2交点が得られている場合は、距離が短くてもTopologyを維持するため
                            // TryGetCutSegment側でCutSegmentとして残す。
                            if (isDegenerateSegment)
                            {
                                if (enableDiagnostics)
                                {
                                    degenerateCutSegmentCount++;

                                    if (loggedDegenerateCutSegmentCount <
                                        MAX_DEGENERATE_SEGMENT_DIAGNOSTIC_DETAILS)
                                    {
                                        LogDegenerateCutSegmentDiagnostic(
                                            subMeshIndex,
                                            triangleIndex / 3,
                                            triangleIndex,
                                            vertexA,
                                            vertexB,
                                            vertexC,
                                            indexA,
                                            indexB,
                                            indexC,
                                            distanceA,
                                            distanceB,
                                            distanceC,
                                            geometryEpsilon);

                                        loggedDegenerateCutSegmentCount++;
                                    }
                                }

                                continue;
                            }

                            failureReason =
                                "交差Triangleの切断線分計算中にNaN / Infinity等の不正値を検出しました。";
                            return false;
                        }

                        // CutSegmentの各端点には、生成元の元Mesh Vertex / Edgeを保持する。
                        // Boundary Nodeの同一性はこの生成元Topologyを主条件として判定し、
                        // 近接した別Edge由来の交点を誤結合しない。
                        cutSegment =
                            new CutSegment(
                                cutSegment.First,
                                cutSegment.Second,
                                cutSegment.FirstSourceEdgeVertexIndexA,
                                cutSegment.FirstSourceEdgeVertexIndexB,
                                cutSegment.SecondSourceEdgeVertexIndexA,
                                cutSegment.SecondSourceEdgeVertexIndexB,
                                cutSegment.FirstConnectivityVertexId,
                                cutSegment.SecondConnectivityVertexId,
                                subMeshIndex,
                                triangleIndex / 3,
                                triangleIndex,
                                indexA,
                                indexB,
                                indexC);

                        cutSegments.Add(cutSegment);
                    }
                }
            }
        }

        if (enableDiagnostics)
        {
            LogSplitDiagnostic(
                geometryEpsilon,
                positiveOnlyTriangleCount,
                negativeOnlyTriangleCount,
                intersectingTriangleCount,
                cutSegments.Count,
                degenerateCutSegmentCount,
                firstBuilder.OriginalTriangleCount,
                secondBuilder.OriginalTriangleCount);
        }

        if (firstBuilder.OriginalTriangleCount == 0)
        {
            failureReason = "切断後のFirst Pieceが空です。";
            return false;
        }

        if (secondBuilder.OriginalTriangleCount == 0)
        {
            failureReason = "切断後のSecond Pieceが空です。";
            return false;
        }

        if (cutSegments.Count == 0)
        {
            failureReason = "有効な切断境界が存在しません。";
            return false;
        }

        // 切断境界に対する利用側固有の制約は、Mesh分割処理へ持ち込まず呼び出し側で検証する。
        // 通常攻撃ではCut Range検証をここで差し込み、従来と同じくCap生成前に失敗させる。
        if (cutBoundaryValidator != null)
        {
            var cutBoundaryPoints =
                new List<Vector3>(cutSegments.Count * 2);

            for (int segmentIndex = 0;
                 segmentIndex < cutSegments.Count;
                 segmentIndex++)
            {
                CutSegment segment =
                    cutSegments[segmentIndex];

                cutBoundaryPoints.Add(segment.First);
                cutBoundaryPoints.Add(segment.Second);
            }

            string boundaryValidationFailure =
                cutBoundaryValidator(cutBoundaryPoints);

            if (!string.IsNullOrEmpty(boundaryValidationFailure))
            {
                failureReason =
                    boundaryValidationFailure;
                return false;
            }
        }

        if (!TryBuildBoundaryLoops(
                cutSegments,
                sourceMesh,
                sourceVertices,
                cutPlane,
                geometryEpsilon,
                enableDiagnostics,
                out List<List<Vector3>> boundaryLoops,
                out List<List<int>> boundaryConnectivityVertexIds,
                out failureReason))
        {
            return false;
        }

        int estimatedCutSurfaceIndexCapacity =
            cutSegments.Count * 3;

        firstBuilder.SetInitialSubMeshIndexCapacity(
            cutSurfaceSubMeshIndex,
            estimatedCutSurfaceIndexCapacity);

        secondBuilder.SetInitialSubMeshIndexCapacity(
            cutSurfaceSubMeshIndex,
            estimatedCutSurfaceIndexCapacity);

        if (!TryAddCutSurfaceCaps(
                boundaryLoops,
                boundaryConnectivityVertexIds,
                cutPlane,
                cutSurfaceUvScale,
                geometryEpsilon,
                cutSurfaceSubMeshIndex,
                firstBuilder,
                secondBuilder,
                out failureReason))
        {
            return false;
        }

        if (splitConnectedComponents)
        {
            if (!connectedComponentSplitter.TrySplit(
                    firstBuilder.Vertices,
                    firstBuilder.Normals,
                    firstBuilder.Uv,
                    firstBuilder.Tangents,
                    firstBuilder.HasTangents,
                    firstBuilder.TrianglesBySubMesh,
                    firstBuilder.ConnectivityVertexIds,
                    firstBuilder.BuilderBounds,
                    firstPieceMeshName,
                    out List<Mesh> firstSideComponents,
                    out string firstSideFailureReason))
            {
                failureReason =
                    "Positive側のConnected Component判定に失敗しました。" +
                    $" {firstSideFailureReason}";
                return false;
            }

            if (!connectedComponentSplitter.TrySplit(
                    secondBuilder.Vertices,
                    secondBuilder.Normals,
                    secondBuilder.Uv,
                    secondBuilder.Tangents,
                    secondBuilder.HasTangents,
                    secondBuilder.TrianglesBySubMesh,
                    secondBuilder.ConnectivityVertexIds,
                    secondBuilder.BuilderBounds,
                    secondPieceMeshName,
                    out List<Mesh> secondSideComponents,
                    out string secondSideFailureReason))
            {
                DestroyMeshes(firstSideComponents);

                failureReason =
                    "Negative側のConnected Component判定に失敗しました。" +
                    $" {secondSideFailureReason}";
                return false;
            }

            connectedPieceMeshes =
                new List<Mesh>(
                    firstSideComponents.Count +
                    secondSideComponents.Count);

            connectedPieceMeshes.AddRange(firstSideComponents);
            connectedPieceMeshes.AddRange(secondSideComponents);

            if (connectedPieceMeshes.Count < 2)
            {
                DestroyMeshes(connectedPieceMeshes);
                connectedPieceMeshes.Clear();

                failureReason =
                    "Connected Component確定後に2個以上の最終Pieceを生成できませんでした。";
                return false;
            }

            failureReason = null;
            return true;
        }

        firstPieceMesh =
            firstBuilder.BuildMesh(firstPieceMeshName);

        secondPieceMesh =
            secondBuilder.BuildMesh(secondPieceMeshName);

        if (firstPieceMesh == null ||
            secondPieceMesh == null)
        {
            failureReason =
                "有効な2つの分割Meshを生成できませんでした.";
            return false;
        }

        failureReason = null;
        return true;
    
        }
    }

    private static void DestroyMeshes(
        IReadOnlyList<Mesh> meshes)
    {
        if (meshes == null)
        {
            return;
        }

        for (int meshIndex = 0;
             meshIndex < meshes.Count;
             meshIndex++)
        {
            Mesh mesh = meshes[meshIndex];

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


    private static bool IsValidVertexIndex(
        int index,
        int vertexCount)
    {
        return index >= 0 &&
               index < vertexCount;
    }

    private static int GetNextConnectivityVertexId(
        IReadOnlyList<int> connectivityVertexIds)
    {
        int maximumConnectivityVertexId = -1;

        for (int i = 0;
             i < connectivityVertexIds.Count;
             i++)
        {
            maximumConnectivityVertexId =
                Mathf.Max(
                    maximumConnectivityVertexId,
                    connectivityVertexIds[i]);
        }

        return maximumConnectivityVertexId + 1;
    }


    private static VertexData CreateVertexData(
        int index,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uv,
        IReadOnlyList<Vector4> tangents,
        bool hasUv,
        bool hasTangents,
        IReadOnlyList<int> sourceConnectivityVertexIds)
    {
        int connectivityVertexId =
            sourceConnectivityVertexIds != null
                ? sourceConnectivityVertexIds[index]
                : -1;

        return new VertexData(
            vertices[index],
            normals[index],
            hasUv
                ? uv[index]
                : Vector2.zero,
            hasTangents
                ? tangents[index]
                : Vector4.zero,
            connectivityVertexId,
            index);
    }

    private static void ClipTriangle(
        VertexData vertexA,
        VertexData vertexB,
        VertexData vertexC,
        float distanceA,
        float distanceB,
        float distanceC,
        bool keepPositiveSide,
        float geometryEpsilon,
        List<VertexData> output,
        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge,
        ref int nextConnectivityVertexId)
    {
        // TriangleごとにListを生成せず、呼び出し側が分割処理中に再利用する作業領域へ書き込む。
        output.Clear();

        AddClippedEdge(
            vertexC,
            vertexA,
            distanceC,
            distanceA,
            keepPositiveSide,
            geometryEpsilon,
            output,
            cutConnectivityVertexIdsBySourceEdge,
            ref nextConnectivityVertexId);

        AddClippedEdge(
            vertexA,
            vertexB,
            distanceA,
            distanceB,
            keepPositiveSide,
            geometryEpsilon,
            output,
            cutConnectivityVertexIdsBySourceEdge,
            ref nextConnectivityVertexId);

        AddClippedEdge(
            vertexB,
            vertexC,
            distanceB,
            distanceC,
            keepPositiveSide,
            geometryEpsilon,
            output,
            cutConnectivityVertexIdsBySourceEdge,
            ref nextConnectivityVertexId);

        if (output.Count >= 2 &&
            ArePositionsClose(
                output[0].Position,
                output[output.Count - 1].Position,
                geometryEpsilon))
        {
            output.RemoveAt(
                output.Count - 1);
        }
    }

    private static void AddClippedEdge(
        VertexData previousVertex,
        VertexData currentVertex,
        float previousDistance,
        float currentDistance,
        bool keepPositiveSide,
        float geometryEpsilon,
        List<VertexData> output,
        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge,
        ref int nextConnectivityVertexId)
    {
        bool previousInside =
            IsInsideHalfSpace(
                previousDistance,
                keepPositiveSide,
                geometryEpsilon);

        bool currentInside =
            IsInsideHalfSpace(
                currentDistance,
                keepPositiveSide,
                geometryEpsilon);

        if (currentInside)
        {
            if (!previousInside)
            {
                VertexData intersection =
                    InterpolateToPlane(
                        previousVertex,
                        currentVertex,
                        previousDistance,
                        currentDistance,
                        geometryEpsilon,
                        cutConnectivityVertexIdsBySourceEdge,
                        ref nextConnectivityVertexId);

                AddDistinctPolygonVertex(
                    output,
                    intersection,
                    geometryEpsilon);
            }

            AddDistinctPolygonVertex(
                output,
                currentVertex,
                geometryEpsilon);

            return;
        }

        if (!previousInside)
        {
            return;
        }

        VertexData exitIntersection =
            InterpolateToPlane(
                previousVertex,
                currentVertex,
                previousDistance,
                currentDistance,
                geometryEpsilon,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId);

        AddDistinctPolygonVertex(
            output,
            exitIntersection,
            geometryEpsilon);
    }

    private static bool IsInsideHalfSpace(
        float signedDistance,
        bool keepPositiveSide,
        float geometryEpsilon)
    {
        return keepPositiveSide
            ? signedDistance >= -geometryEpsilon
            : signedDistance <= geometryEpsilon;
    }

    private static void AddDistinctPolygonVertex(
        List<VertexData> vertices,
        VertexData vertex,
        float geometryEpsilon)
    {
        if (vertices.Count == 0 ||
            !ArePositionsClose(
                vertices[vertices.Count - 1].Position,
                vertex.Position,
                geometryEpsilon))
        {
            vertices.Add(vertex);
        }
    }

    private static VertexData InterpolateToPlane(
        VertexData from,
        VertexData to,
        float fromDistance,
        float toDistance,
        float geometryEpsilon,
        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge,
        ref int nextConnectivityVertexId)
    {
        using (calculateIntersectionsProfilerMarker.Auto())
        {
            float denominator =
                fromDistance - toDistance;

            float interpolation =
                Mathf.Abs(denominator) <= Mathf.Epsilon
                    ? 0.5f
                    : fromDistance / denominator;

            interpolation =
                Mathf.Clamp01(interpolation);

            int connectivityVertexId =
                GetCutConnectivityVertexId(
                    from,
                    to,
                    fromDistance,
                    toDistance,
                    geometryEpsilon,
                    cutConnectivityVertexIdsBySourceEdge,
                    ref nextConnectivityVertexId);

            return VertexData.Lerp(
                from,
                to,
                interpolation,
                connectivityVertexId);
        }
    }

    private static int GetCutConnectivityVertexId(
        VertexData first,
        VertexData second,
        float firstDistance,
        float secondDistance,
        float geometryEpsilon,
        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge,
        ref int nextConnectivityVertexId)
    {
        if (Mathf.Abs(firstDistance) <= geometryEpsilon)
        {
            return first.ConnectivityVertexId;
        }

        if (Mathf.Abs(secondDistance) <= geometryEpsilon)
        {
            return second.ConnectivityVertexId;
        }

        if (cutConnectivityVertexIdsBySourceEdge == null)
        {
            return -1;
        }

        long sourceEdgeKey =
            CreateEdgeKey(
                first.ConnectivityVertexId,
                second.ConnectivityVertexId);

        if (cutConnectivityVertexIdsBySourceEdge.TryGetValue(
                sourceEdgeKey,
                out int connectivityVertexId))
        {
            return connectivityVertexId;
        }

        connectivityVertexId =
            nextConnectivityVertexId++;

        cutConnectivityVertexIdsBySourceEdge.Add(
            sourceEdgeKey,
            connectivityVertexId);

        return connectivityVertexId;
    }

    private static bool TryGetCutSegment(
        VertexData vertexA,
        VertexData vertexB,
        VertexData vertexC,
        int sourceVertexIndexA,
        int sourceVertexIndexB,
        int sourceVertexIndexC,
        float distanceA,
        float distanceB,
        float distanceC,
        float geometryEpsilon,
        List<Vector3> points,
        List<int> pointSourceEdgeVertexIndexA,
        List<int> pointSourceEdgeVertexIndexB,
        List<int> pointConnectivityVertexIds,
        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge,
        ref int nextConnectivityVertexId,
        out CutSegment cutSegment,
        out bool isDegenerateSegment)
    {
        cutSegment = default;
        isDegenerateSegment = false;

        if (!IsFinite(geometryEpsilon) ||
            geometryEpsilon <= 0.0f ||
            points == null ||
            pointSourceEdgeVertexIndexA == null ||
            pointSourceEdgeVertexIndexB == null ||
            pointConnectivityVertexIds == null)
        {
            return false;
        }

        // 交差TriangleごとにListを生成せず、TrySplitMeshで確保した作業領域を再利用する。
        points.Clear();
        pointSourceEdgeVertexIndexA.Clear();
        pointSourceEdgeVertexIndexB.Clear();
        pointConnectivityVertexIds.Clear();

        if (!TryAddCutPointsForEdge(
                vertexA,
                vertexB,
                sourceVertexIndexA,
                sourceVertexIndexB,
                distanceA,
                distanceB,
                geometryEpsilon,
                points,
                pointSourceEdgeVertexIndexA,
                pointSourceEdgeVertexIndexB,
                pointConnectivityVertexIds,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId) ||
            !TryAddCutPointsForEdge(
                vertexB,
                vertexC,
                sourceVertexIndexB,
                sourceVertexIndexC,
                distanceB,
                distanceC,
                geometryEpsilon,
                points,
                pointSourceEdgeVertexIndexA,
                pointSourceEdgeVertexIndexB,
                pointConnectivityVertexIds,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId) ||
            !TryAddCutPointsForEdge(
                vertexC,
                vertexA,
                sourceVertexIndexC,
                sourceVertexIndexA,
                distanceC,
                distanceA,
                geometryEpsilon,
                points,
                pointSourceEdgeVertexIndexA,
                pointSourceEdgeVertexIndexB,
                pointConnectivityVertexIds,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId))
        {
            return false;
        }

        if (points.Count < 2)
        {
            isDegenerateSegment = true;
            return false;
        }

        if (points.Count == 2)
        {
            float sqrDistance =
                (points[0] - points[1]).sqrMagnitude;

            if (!IsFinite(sqrDistance))
            {
                return false;
            }

            // 交点間の距離がgeometryEpsilon以下でも、異なる元Edge / Vertex由来なら
            // TriangleとPlaneの交差Topologyを構成する実在の境界線分として保持する。
            // ここで短さだけを理由に捨てると、隣接Triangle側の線分だけが残って
            // Boundary NodeがDegree=1になり、閉じた切断境界が途切れる。
            cutSegment =
                new CutSegment(
                    points[0],
                    points[1],
                    pointSourceEdgeVertexIndexA[0],
                    pointSourceEdgeVertexIndexB[0],
                    pointSourceEdgeVertexIndexA[1],
                    pointSourceEdgeVertexIndexB[1],
                    pointConnectivityVertexIds[0],
                    pointConnectivityVertexIds[1]);

            return true;
        }

        float greatestSqrDistance =
            -1.0f;

        int firstPointIndex = -1;
        int secondPointIndex = -1;

        for (int candidateFirstIndex = 0;
             candidateFirstIndex < points.Count - 1;
             candidateFirstIndex++)
        {
            for (int candidateSecondIndex = candidateFirstIndex + 1;
                 candidateSecondIndex < points.Count;
                 candidateSecondIndex++)
            {
                float sqrDistance =
                    (points[candidateFirstIndex] -
                     points[candidateSecondIndex]).sqrMagnitude;

                if (!IsFinite(sqrDistance))
                {
                    return false;
                }

                if (sqrDistance <=
                    greatestSqrDistance)
                {
                    continue;
                }

                greatestSqrDistance =
                    sqrDistance;

                firstPointIndex =
                    candidateFirstIndex;

                secondPointIndex =
                    candidateSecondIndex;
            }
        }

        if (firstPointIndex < 0 ||
            secondPointIndex < 0)
        {
            isDegenerateSegment = true;
            return false;
        }

        cutSegment =
            new CutSegment(
                points[firstPointIndex],
                points[secondPointIndex],
                pointSourceEdgeVertexIndexA[firstPointIndex],
                pointSourceEdgeVertexIndexB[firstPointIndex],
                pointSourceEdgeVertexIndexA[secondPointIndex],
                pointSourceEdgeVertexIndexB[secondPointIndex],
                pointConnectivityVertexIds[firstPointIndex],
                pointConnectivityVertexIds[secondPointIndex]);

        return true;
    }

    private static bool TryAddCutPointsForEdge(
        VertexData currentVertex,
        VertexData nextVertex,
        int currentSourceVertexIndex,
        int nextSourceVertexIndex,
        float currentDistance,
        float nextDistance,
        float geometryEpsilon,
        List<Vector3> points,
        List<int> pointSourceEdgeVertexIndexA,
        List<int> pointSourceEdgeVertexIndexB,
        List<int> pointConnectivityVertexIds,
        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge,
        ref int nextConnectivityVertexId)
    {
        if (!IsFinite(currentVertex.Position) ||
            !IsFinite(nextVertex.Position) ||
            !IsFinite(currentDistance) ||
            !IsFinite(nextDistance))
        {
            return false;
        }

        if (Mathf.Abs(currentDistance) <=
            geometryEpsilon)
        {
            // Plane上の元頂点は (vertex, vertex) として保持する。
            // 交差Edgeとは区別し、隣接Triangle間では同じ元頂点として接続する。
            AddDistinctCutPoint(
                points,
                pointSourceEdgeVertexIndexA,
                pointSourceEdgeVertexIndexB,
                currentVertex.Position,
                currentSourceVertexIndex,
                currentSourceVertexIndex,
                currentVertex.ConnectivityVertexId,
                pointConnectivityVertexIds);
        }

        bool crossesPlane =
            (currentDistance > geometryEpsilon &&
             nextDistance < -geometryEpsilon) ||
            (currentDistance < -geometryEpsilon &&
             nextDistance > geometryEpsilon);

        if (!crossesPlane)
        {
            return true;
        }

        VertexData intersection =
            InterpolateToPlane(
                currentVertex,
                nextVertex,
                currentDistance,
                nextDistance,
                geometryEpsilon,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId);

        if (!IsFinite(intersection.Position))
        {
            return false;
        }

        GetCanonicalEdgeVertexIndices(
            currentSourceVertexIndex,
            nextSourceVertexIndex,
            out int sourceEdgeVertexIndexA,
            out int sourceEdgeVertexIndexB);

        AddDistinctCutPoint(
            points,
            pointSourceEdgeVertexIndexA,
            pointSourceEdgeVertexIndexB,
            intersection.Position,
            sourceEdgeVertexIndexA,
            sourceEdgeVertexIndexB,
            intersection.ConnectivityVertexId,
            pointConnectivityVertexIds);

        return true;
    }

    private static void AddDistinctCutPoint(
        List<Vector3> points,
        List<int> pointSourceEdgeVertexIndexA,
        List<int> pointSourceEdgeVertexIndexB,
        Vector3 point,
        int sourceEdgeVertexIndexA,
        int sourceEdgeVertexIndexB,
        int connectivityVertexId,
        List<int> pointConnectivityVertexIds)
    {
        // 同一Triangle内では、位置の近さではなく生成元Topologyで交点の同一性を判定する。
        // Planeが元Vertexのすぐ近くを通った場合、異なる2本のEdge上の正しい交点が
        // geometryEpsilon以内へ接近することがあるため、位置だけで潰してはいけない。
        for (int i = 0;
             i < points.Count;
             i++)
        {
            if (pointSourceEdgeVertexIndexA[i] ==
                    sourceEdgeVertexIndexA &&
                pointSourceEdgeVertexIndexB[i] ==
                    sourceEdgeVertexIndexB)
            {
                return;
            }
        }

        points.Add(point);
        pointSourceEdgeVertexIndexA.Add(sourceEdgeVertexIndexA);
        pointSourceEdgeVertexIndexB.Add(sourceEdgeVertexIndexB);
        pointConnectivityVertexIds.Add(connectivityVertexId);
    }

    private static void GetCanonicalEdgeVertexIndices(
        int firstVertexIndex,
        int secondVertexIndex,
        out int edgeVertexIndexA,
        out int edgeVertexIndexB)
    {
        if (firstVertexIndex <= secondVertexIndex)
        {
            edgeVertexIndexA = firstVertexIndex;
            edgeVertexIndexB = secondVertexIndex;
            return;
        }

        edgeVertexIndexA = secondVertexIndex;
        edgeVertexIndexB = firstVertexIndex;
    }

    private static bool ArePositionsClose(
        Vector3 first,
        Vector3 second,
        float geometryEpsilon)
    {
        return (first - second).sqrMagnitude <=
               geometryEpsilon * geometryEpsilon;
    }

    private static bool TryBuildBoundaryLoops(
        IReadOnlyList<CutSegment> cutSegments,
        Mesh sourceMesh,
        IReadOnlyList<Vector3> sourceVertices,
        Plane cutPlane,
        float geometryEpsilon,
        bool enableDiagnostics,
        out List<List<Vector3>> boundaryLoops,
        out List<List<int>> boundaryConnectivityVertexIds,
        out string failureReason)
    {
        using (buildBoundaryLoopsProfilerMarker.Auto())
        {
        float sourceSplitVertexMatchEpsilon =
            geometryEpsilon *
            SOURCE_SPLIT_VERTEX_MATCH_EPSILON_SCALE;

        var nodes =
            new List<BoundaryNode>();

        var uniqueEdges =
            new HashSet<long>();

        // 各Boundary Edgeの生成元追跡は診断専用。OFF時はDictionary自体を生成しない。
        Dictionary<long, int> edgeSourceSegmentIndices =
            enableDiagnostics
                ? new Dictionary<long, int>()
                : null;

        int collapsedSegmentCount = 0;
        int duplicateEdgeCount = 0;

        for (int i = 0;
             i < cutSegments.Count;
             i++)
        {
            CutSegment cutSegment =
                cutSegments[i];

            int firstNodeIndex =
                FindOrCreateBoundaryNode(
                    nodes,
                    cutSegment.First,
                    cutSegment.FirstSourceEdgeVertexIndexA,
                    cutSegment.FirstSourceEdgeVertexIndexB,
                    cutSegment.FirstConnectivityVertexId,
                    sourceVertices,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon);

            int secondNodeIndex =
                FindOrCreateBoundaryNode(
                    nodes,
                    cutSegment.Second,
                    cutSegment.SecondSourceEdgeVertexIndexA,
                    cutSegment.SecondSourceEdgeVertexIndexB,
                    cutSegment.SecondConnectivityVertexId,
                    sourceVertices,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon);

            if (firstNodeIndex ==
                secondNodeIndex)
            {
                if (enableDiagnostics)
                {
                    collapsedSegmentCount++;
                }

                continue;
            }

            long edgeKey =
                CreateEdgeKey(
                    firstNodeIndex,
                    secondNodeIndex);

            if (!uniqueEdges.Add(edgeKey))
            {
                if (enableDiagnostics)
                {
                    duplicateEdgeCount++;

                    if (edgeSourceSegmentIndices.TryGetValue(
                            edgeKey,
                            out int existingSegmentIndex))
                    {
                        LogDuplicateBoundaryEdgeDiagnostic(
                            firstNodeIndex,
                            secondNodeIndex,
                            existingSegmentIndex,
                            i,
                            cutSegments);
                    }
                }

                continue;
            }

            if (enableDiagnostics)
            {
                edgeSourceSegmentIndices.Add(
                    edgeKey,
                    i);
            }

            nodes[firstNodeIndex]
                .Neighbors
                .Add(secondNodeIndex);

            nodes[secondNodeIndex]
                .Neighbors
                .Add(firstNodeIndex);
        }

        if (enableDiagnostics)
        {
            LogBoundaryGraphDiagnostic(
                cutSegments.Count,
                sourceSplitVertexMatchEpsilon,
                nodes,
                uniqueEdges.Count,
                collapsedSegmentCount,
                duplicateEdgeCount,
                cutSegments,
                edgeSourceSegmentIndices,
                sourceMesh,
                sourceVertices,
                cutPlane,
                geometryEpsilon);
        }

        if (uniqueEdges.Count < 3)
        {
            boundaryLoops = null;
            boundaryConnectivityVertexIds = null;
            failureReason =
                "切断境界を構成する線分が不足しています。";

            return false;
        }

        for (int i = 0;
             i < nodes.Count;
             i++)
        {
            if (nodes[i].Neighbors.Count != 2)
            {
                boundaryLoops = null;
                boundaryConnectivityVertexIds = null;
                failureReason =
                    "切断境界が閉じた単純ループになっていません。";

                return false;
            }
        }

        boundaryLoops =
            new List<List<Vector3>>();

        boundaryConnectivityVertexIds =
            new List<List<int>>();

        var visitedEdges =
            new HashSet<long>();

        for (int nodeIndex = 0;
             nodeIndex < nodes.Count;
             nodeIndex++)
        {
            for (int neighborIndex = 0;
                 neighborIndex <
                 nodes[nodeIndex].Neighbors.Count;
                 neighborIndex++)
            {
                int neighbor =
                    nodes[nodeIndex]
                        .Neighbors[neighborIndex];

                long edgeKey =
                    CreateEdgeKey(
                        nodeIndex,
                        neighbor);

                if (visitedEdges.Contains(edgeKey))
                {
                    continue;
                }

                if (!TryTraceBoundaryLoop(
                        nodes,
                        nodeIndex,
                        neighbor,
                        visitedEdges,
                        out List<Vector3> loop,
                        out List<int> loopConnectivityVertexIds))
                {
                    boundaryLoops = null;
                    boundaryConnectivityVertexIds = null;
                    failureReason =
                        "切断境界ループの追跡に失敗しました。";

                    return false;
                }

                if (loop.Count < 3)
                {
                    boundaryLoops = null;
                    boundaryConnectivityVertexIds = null;
                    failureReason =
                        "切断境界ループの頂点数が不足しています。";

                    return false;
                }

                boundaryLoops.Add(loop);
                boundaryConnectivityVertexIds.Add(
                    loopConnectivityVertexIds);
            }
        }

        if (boundaryLoops.Count == 0 ||
            visitedEdges.Count != uniqueEdges.Count)
        {
            boundaryLoops = null;
            boundaryConnectivityVertexIds = null;
            failureReason =
                "すべての切断境界を閉じたループへ構築できませんでした。";

            return false;
        }

        failureReason = null;
        return true;
    
        }
    }


    /// <summary>
    /// 交差Triangleから有効なCutSegmentを作れなかった場合に、
    /// Planeとの距離と元Edgeを残す。
    /// </summary>
    private static void LogDegenerateCutSegmentDiagnostic(
        int subMeshIndex,
        int triangleIndex,
        int triangleIndexBufferOffset,
        VertexData vertexA,
        VertexData vertexB,
        VertexData vertexC,
        int sourceVertexIndexA,
        int sourceVertexIndexB,
        int sourceVertexIndexC,
        float distanceA,
        float distanceB,
        float distanceC,
        float geometryEpsilon)
    {
        var edgeDetails =
            new List<string>(3);

        AddDegenerateCutSegmentEdgeDiagnostic(
            edgeDetails,
            0,
            vertexA,
            vertexB,
            sourceVertexIndexA,
            sourceVertexIndexB,
            distanceA,
            distanceB,
            geometryEpsilon);

        AddDegenerateCutSegmentEdgeDiagnostic(
            edgeDetails,
            1,
            vertexB,
            vertexC,
            sourceVertexIndexB,
            sourceVertexIndexC,
            distanceB,
            distanceC,
            geometryEpsilon);

        AddDegenerateCutSegmentEdgeDiagnostic(
            edgeDetails,
            2,
            vertexC,
            vertexA,
            sourceVertexIndexC,
            sourceVertexIndexA,
            distanceC,
            distanceA,
            geometryEpsilon);

        Debug.LogWarning(
            "[MeshCutBoundaryDiagnostic][DegenerateCutSegment]\n" +
            $"SubMesh={subMeshIndex}, " +
            $"Triangle={triangleIndex}, " +
            $"TriangleIndexBufferOffset={triangleIndexBufferOffset}, " +
            $"SourceVertexIndices=({sourceVertexIndexA}, {sourceVertexIndexB}, {sourceVertexIndexC}), " +
            $"GeometryEpsilon={geometryEpsilon:E6}\n" +
            $"Vertex[0]={FormatDiagnosticVector3(vertexA.Position)}, Distance={distanceA:E6}\n" +
            $"Vertex[1]={FormatDiagnosticVector3(vertexB.Position)}, Distance={distanceB:E6}\n" +
            $"Vertex[2]={FormatDiagnosticVector3(vertexC.Position)}, Distance={distanceC:E6}\n" +
            string.Join("\n", edgeDetails));
    }

    private static void AddDegenerateCutSegmentEdgeDiagnostic(
        List<string> edgeDetails,
        int edgeIndex,
        VertexData currentVertex,
        VertexData nextVertex,
        int currentSourceVertexIndex,
        int nextSourceVertexIndex,
        float currentDistance,
        float nextDistance,
        float geometryEpsilon)
    {
        GetCanonicalEdgeVertexIndices(
            currentSourceVertexIndex,
            nextSourceVertexIndex,
            out int edgeVertexIndexA,
            out int edgeVertexIndexB);

        bool crossesPlane =
            (currentDistance > geometryEpsilon &&
             nextDistance < -geometryEpsilon) ||
            (currentDistance < -geometryEpsilon &&
             nextDistance > geometryEpsilon);

        bool currentOnPlane =
            Mathf.Abs(currentDistance) <= geometryEpsilon;

        bool nextOnPlane =
            Mathf.Abs(nextDistance) <= geometryEpsilon;

        edgeDetails.Add(
            $"  Edge[{edgeIndex}]: SourceEdge=({edgeVertexIndexA}, {edgeVertexIndexB}), " +
            $"Distance=({currentDistance:E6}, {nextDistance:E6}), " +
            $"CurrentOnPlane={currentOnPlane}, " +
            $"NextOnPlane={nextOnPlane}, " +
            $"CrossesPlane={crossesPlane}, " +
            $"PositionA={FormatDiagnosticVector3(currentVertex.Position)}, " +
            $"PositionB={FormatDiagnosticVector3(nextVertex.Position)}");
    }

    /// <summary>
    /// Triangle分類と切断線分生成の結果を、境界ループ構築前にまとめて出力する。
    /// </summary>
    private static void LogSplitDiagnostic(
        float geometryEpsilon,
        int positiveOnlyTriangleCount,
        int negativeOnlyTriangleCount,
        int intersectingTriangleCount,
        int validCutSegmentCount,
        int degenerateCutSegmentCount,
        int firstPieceTriangleCount,
        int secondPieceTriangleCount)
    {
        Debug.Log(
            "[MeshCutBoundaryDiagnostic][SplitSummary] " +
            $"GeometryEpsilon={geometryEpsilon:E6}, " +
            $"PositiveOnlyTriangles={positiveOnlyTriangleCount}, " +
            $"NegativeOnlyTriangles={negativeOnlyTriangleCount}, " +
            $"IntersectingTriangles={intersectingTriangleCount}, " +
            $"ValidCutSegments={validCutSegmentCount}, " +
            $"DegenerateSegments={degenerateCutSegmentCount}, " +
            $"FirstPieceOriginalTriangles={firstPieceTriangleCount}, " +
            $"SecondPieceOriginalTriangles={secondPieceTriangleCount}");
    }

    /// <summary>
    /// 切断線分から構築した境界グラフの次数分布を出力する。
    /// </summary>
    /// <remarks>
    /// 正常な単純閉ループでは、境界NodeはすべてDegree=2になる。
    /// Degree=1は境界の途切れ、Degree>=3は分岐または近接点の誤結合を疑う材料になる。
    /// </remarks>
    private static void LogBoundaryGraphDiagnostic(
        int cutSegmentCount,
        float sourceSplitVertexMatchEpsilon,
        IReadOnlyList<BoundaryNode> nodes,
        int uniqueEdgeCount,
        int collapsedSegmentCount,
        int duplicateEdgeCount,
        IReadOnlyList<CutSegment> cutSegments,
        IReadOnlyDictionary<long, int> edgeSourceSegmentIndices,
        Mesh sourceMesh,
        IReadOnlyList<Vector3> sourceVertices,
        Plane cutPlane,
        float geometryEpsilon)
    {
        int degreeZeroCount = 0;
        int degreeOneCount = 0;
        int degreeTwoCount = 0;
        int degreeThreeOrMoreCount = 0;

        var abnormalNodeDetails =
            new List<string>();

        for (int nodeIndex = 0;
             nodeIndex < nodes.Count;
             nodeIndex++)
        {
            BoundaryNode node =
                nodes[nodeIndex];

            int degree =
                node.Neighbors.Count;

            switch (degree)
            {
                case 0:
                    degreeZeroCount++;
                    break;

                case 1:
                    degreeOneCount++;
                    break;

                case 2:
                    degreeTwoCount++;
                    break;

                default:
                    degreeThreeOrMoreCount++;
                    break;
            }

            if (degree == 2 ||
                abnormalNodeDetails.Count >=
                MAX_BOUNDARY_DIAGNOSTIC_NODE_DETAILS)
            {
                continue;
            }

            abnormalNodeDetails.Add(
                BuildBoundaryNodeDiagnostic(
                    nodeIndex,
                    nodes,
                    cutSegments,
                    edgeSourceSegmentIndices,
                    sourceMesh,
                    sourceVertices,
                    cutPlane,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon));
        }

        Debug.Log(
            "[MeshCutBoundaryDiagnostic][BoundaryGraph] " +
            $"SourceSplitVertexMatchEpsilon={sourceSplitVertexMatchEpsilon:E6}, " +
            $"CutSegments={cutSegmentCount}, " +
            $"UniqueEdges={uniqueEdgeCount}, " +
            $"Nodes={nodes.Count}, " +
            $"CollapsedSegments={collapsedSegmentCount}, " +
            $"DuplicateEdges={duplicateEdgeCount}, " +
            $"Degree0={degreeZeroCount}, " +
            $"Degree1={degreeOneCount}, " +
            $"Degree2={degreeTwoCount}, " +
            $"Degree3Plus={degreeThreeOrMoreCount}");

        if (abnormalNodeDetails.Count == 0)
        {
            return;
        }

        Debug.LogWarning(
            "[MeshCutBoundaryDiagnostic][AbnormalNodes]\n" +
            string.Join(
                "\n",
                abnormalNodeDetails));
    }

    private static string BuildBoundaryNodeDiagnostic(
        int nodeIndex,
        IReadOnlyList<BoundaryNode> nodes,
        IReadOnlyList<CutSegment> cutSegments,
        IReadOnlyDictionary<long, int> edgeSourceSegmentIndices,
        Mesh sourceMesh,
        IReadOnlyList<Vector3> sourceVertices,
        Plane cutPlane,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        BoundaryNode node =
            nodes[nodeIndex];

        var incidentSegmentDetails =
            new List<string>(
                node.Neighbors.Count);

        for (int neighborIndex = 0;
             neighborIndex < node.Neighbors.Count;
             neighborIndex++)
        {
            int neighborNodeIndex =
                node.Neighbors[neighborIndex];

            Vector3 neighborPosition =
                nodes[neighborNodeIndex].Position;

            float distance =
                Vector3.Distance(
                    node.Position,
                    neighborPosition);

            long edgeKey =
                CreateEdgeKey(
                    nodeIndex,
                    neighborNodeIndex);

            if (!edgeSourceSegmentIndices.TryGetValue(
                    edgeKey,
                    out int sourceSegmentIndex) ||
                sourceSegmentIndex < 0 ||
                sourceSegmentIndex >= cutSegments.Count)
            {
                incidentSegmentDetails.Add(
                    $"  IncidentEdge[{neighborIndex}]: " +
                    $"NeighborNode={neighborNodeIndex}, " +
                    $"Distance={distance:F6}, " +
                    "SourceSegment=Unknown");

                continue;
            }

            CutSegment sourceSegment =
                cutSegments[sourceSegmentIndex];

            incidentSegmentDetails.Add(
                $"  IncidentEdge[{neighborIndex}]: " +
                $"NeighborNode={neighborNodeIndex}, " +
                $"Distance={distance:F6}, " +
                $"CutSegmentIndex={sourceSegmentIndex}, " +
                $"SubMesh={sourceSegment.SourceSubMeshIndex}, " +
                $"Triangle={sourceSegment.SourceTriangleIndex}, " +
                $"TriangleIndexBufferOffset={sourceSegment.SourceTriangleIndexBufferOffset}, " +
                $"SourceVertexIndices=({sourceSegment.SourceVertexIndexA}, " +
                $"{sourceSegment.SourceVertexIndexB}, " +
                $"{sourceSegment.SourceVertexIndexC}), " +
                $"FirstSourceOrigin=({sourceSegment.FirstSourceEdgeVertexIndexA}, " +
                $"{sourceSegment.FirstSourceEdgeVertexIndexB}), " +
                $"SecondSourceOrigin=({sourceSegment.SecondSourceEdgeVertexIndexA}, " +
                $"{sourceSegment.SecondSourceEdgeVertexIndexB}), " +
                $"SegmentFirst={FormatDiagnosticVector3(sourceSegment.First)}, " +
                $"SegmentSecond={FormatDiagnosticVector3(sourceSegment.Second)}");
        }

        string incidentSegments =
            incidentSegmentDetails.Count > 0
                ? string.Join(
                    "\n",
                    incidentSegmentDetails)
                : "  IncidentEdge=None";

        string sourceOrigins =
            BuildBoundaryNodeSourceOriginDiagnostic(
                node);

        string nearbyEndpoints =
            BuildNearbyBoundaryEndpointDiagnostic(
                node,
                cutSegments,
                sourceVertices,
                geometryEpsilon,
                sourceSplitVertexMatchEpsilon);

        string sourceTopology =
            BuildBoundaryNodeSourceTopologyDiagnostic(
                node,
                sourceMesh,
                sourceVertices,
                cutPlane,
                geometryEpsilon,
                sourceSplitVertexMatchEpsilon);

        return
            $"Node={nodeIndex}, " +
            $"Position={FormatDiagnosticVector3(node.Position)}, " +
            $"Degree={node.Neighbors.Count}\n" +
            sourceOrigins + "\n" +
            incidentSegments + "\n" +
            nearbyEndpoints + "\n" +
            sourceTopology;
    }

    private static string BuildBoundaryNodeSourceOriginDiagnostic(
        BoundaryNode node)
    {
        var details =
            new List<string>(node.SourceOriginCount);

        for (int i = 0;
             i < node.SourceOriginCount;
             i++)
        {
            node.GetSourceOrigin(
                i,
                out int sourceVertexIndexA,
                out int sourceVertexIndexB);

            string originType =
                sourceVertexIndexA == sourceVertexIndexB
                    ? "Vertex"
                    : "Edge";

            details.Add(
                $"  SourceOrigin[{i}]: Type={originType}, " +
                $"Indices=({sourceVertexIndexA}, {sourceVertexIndexB})");
        }

        return
            "SourceOrigins:\n" +
            (details.Count > 0
                ? string.Join("\n", details)
                : "  None");
    }

    /// <summary>
    /// Degree=1の近傍に、位置は近いがTopology判定で別Nodeになった端点がないか確認する。
    /// </summary>
    private static string BuildNearbyBoundaryEndpointDiagnostic(
        BoundaryNode node,
        IReadOnlyList<CutSegment> cutSegments,
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        float searchDistance =
            geometryEpsilon *
            NEARBY_BOUNDARY_ENDPOINT_SEARCH_SCALE;

        float searchSqrDistance =
            searchDistance * searchDistance;

        var details =
            new List<string>();

        int totalNearbyEndpointCount = 0;

        for (int segmentIndex = 0;
             segmentIndex < cutSegments.Count;
             segmentIndex++)
        {
            CutSegment segment =
                cutSegments[segmentIndex];

            AppendNearbyBoundaryEndpointDiagnostic(
                details,
                ref totalNearbyEndpointCount,
                node,
                segmentIndex,
                "First",
                segment.First,
                segment.FirstSourceEdgeVertexIndexA,
                segment.FirstSourceEdgeVertexIndexB,
                sourceVertices,
                geometryEpsilon,
                sourceSplitVertexMatchEpsilon,
                searchSqrDistance);

            AppendNearbyBoundaryEndpointDiagnostic(
                details,
                ref totalNearbyEndpointCount,
                node,
                segmentIndex,
                "Second",
                segment.Second,
                segment.SecondSourceEdgeVertexIndexA,
                segment.SecondSourceEdgeVertexIndexB,
                sourceVertices,
                geometryEpsilon,
                sourceSplitVertexMatchEpsilon,
                searchSqrDistance);
        }

        string truncated =
            totalNearbyEndpointCount > details.Count
                ? $"\n  ... truncated. TotalNearbyEndpoints={totalNearbyEndpointCount}"
                : string.Empty;

        return
            $"NearbyCutSegmentEndpoints(SearchDistance={searchDistance:E6}):\n" +
            (details.Count > 0
                ? string.Join("\n", details)
                : "  None") +
            truncated;
    }

    private static void AppendNearbyBoundaryEndpointDiagnostic(
        List<string> details,
        ref int totalNearbyEndpointCount,
        BoundaryNode node,
        int segmentIndex,
        string endpointName,
        Vector3 endpointPosition,
        int sourceVertexIndexA,
        int sourceVertexIndexB,
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon,
        float searchSqrDistance)
    {
        float sqrDistance =
            (endpointPosition - node.Position).sqrMagnitude;

        if (sqrDistance > searchSqrDistance)
        {
            return;
        }

        totalNearbyEndpointCount++;

        if (details.Count >=
            MAX_NEARBY_ENDPOINT_DIAGNOSTIC_DETAILS)
        {
            return;
        }

        float distance =
            Mathf.Sqrt(sqrDistance);

        bool withinBoundaryMergeDistance =
            sqrDistance <=
            geometryEpsilon * geometryEpsilon;

        bool equivalentSourceOrigin =
            node.HasEquivalentSourceOrigin(
                sourceVertexIndexA,
                sourceVertexIndexB,
                sourceVertices,
                sourceSplitVertexMatchEpsilon);

        details.Add(
            $"  Segment={segmentIndex}, Endpoint={endpointName}, " +
            $"Distance={distance:E6}, " +
            $"WithinBoundaryMergeDistance={withinBoundaryMergeDistance}, " +
            $"EquivalentSourceOrigin={equivalentSourceOrigin}, " +
            $"SourceOrigin=({sourceVertexIndexA}, {sourceVertexIndexB}), " +
            $"Position={FormatDiagnosticVector3(endpointPosition)}");
    }

    /// <summary>
    /// Degree異常Nodeの生成元Vertex / Edgeを共有する元Triangleを列挙し、
    /// そのTriangleが現在のPlaneに対してどの分類になるかを出力する。
    /// </summary>
    private static string BuildBoundaryNodeSourceTopologyDiagnostic(
        BoundaryNode node,
        Mesh sourceMesh,
        IReadOnlyList<Vector3> sourceVertices,
        Plane cutPlane,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        if (sourceMesh == null)
        {
            return "SourceTopology: Mesh=null";
        }

        var details =
            new List<string>();

        int totalMatchedTriangleCount = 0;

        for (int subMeshIndex = 0;
             subMeshIndex < sourceMesh.subMeshCount;
             subMeshIndex++)
        {
            if (sourceMesh.GetTopology(subMeshIndex) !=
                MeshTopology.Triangles)
            {
                continue;
            }

            int[] triangles =
                sourceMesh.GetTriangles(subMeshIndex);

            for (int triangleIndexBufferOffset = 0;
                 triangleIndexBufferOffset < triangles.Length;
                 triangleIndexBufferOffset += 3)
            {
                int indexA =
                    triangles[triangleIndexBufferOffset];

                int indexB =
                    triangles[triangleIndexBufferOffset + 1];

                int indexC =
                    triangles[triangleIndexBufferOffset + 2];

                if (!DoesTriangleMatchAnyBoundaryNodeSourceOrigin(
                        node,
                        indexA,
                        indexB,
                        indexC,
                        sourceVertices,
                        sourceSplitVertexMatchEpsilon))
                {
                    continue;
                }

                totalMatchedTriangleCount++;

                if (details.Count >=
                    MAX_SOURCE_TOPOLOGY_DIAGNOSTIC_TRIANGLES)
                {
                    continue;
                }

                Vector3 positionA =
                    sourceVertices[indexA];

                Vector3 positionB =
                    sourceVertices[indexB];

                Vector3 positionC =
                    sourceVertices[indexC];

                float distanceA =
                    cutPlane.GetDistanceToPoint(positionA);

                float distanceB =
                    cutPlane.GetDistanceToPoint(positionB);

                float distanceC =
                    cutPlane.GetDistanceToPoint(positionC);

                string classification =
                    ClassifyTriangleForBoundaryDiagnostic(
                        distanceA,
                        distanceB,
                        distanceC,
                        geometryEpsilon);

                details.Add(
                    $"  SubMesh={subMeshIndex}, " +
                    $"Triangle={triangleIndexBufferOffset / 3}, " +
                    $"TriangleIndexBufferOffset={triangleIndexBufferOffset}, " +
                    $"Indices=({indexA}, {indexB}, {indexC}), " +
                    $"Classification={classification}, " +
                    $"Distances=({distanceA:E6}, {distanceB:E6}, {distanceC:E6})");
            }
        }

        string truncated =
            totalMatchedTriangleCount > details.Count
                ? $"\n  ... truncated. TotalMatchedTriangles={totalMatchedTriangleCount}"
                : string.Empty;

        return
            "SourceTopologyTriangles:\n" +
            (details.Count > 0
                ? string.Join("\n", details)
                : "  None") +
            truncated;
    }

    private static bool DoesTriangleMatchAnyBoundaryNodeSourceOrigin(
        BoundaryNode node,
        int triangleVertexIndexA,
        int triangleVertexIndexB,
        int triangleVertexIndexC,
        IReadOnlyList<Vector3> sourceVertices,
        float sourceSplitVertexMatchEpsilon)
    {
        int[] triangleVertexIndices =
        {
            triangleVertexIndexA,
            triangleVertexIndexB,
            triangleVertexIndexC
        };

        for (int originIndex = 0;
             originIndex < node.SourceOriginCount;
             originIndex++)
        {
            node.GetSourceOrigin(
                originIndex,
                out int originVertexIndexA,
                out int originVertexIndexB);

            bool originIsVertex =
                originVertexIndexA == originVertexIndexB;

            if (originIsVertex)
            {
                for (int vertexIndex = 0;
                     vertexIndex < triangleVertexIndices.Length;
                     vertexIndex++)
                {
                    if (AreSourceOriginsEquivalent(
                            originVertexIndexA,
                            originVertexIndexB,
                            triangleVertexIndices[vertexIndex],
                            triangleVertexIndices[vertexIndex],
                            sourceVertices,
                            sourceSplitVertexMatchEpsilon))
                    {
                        return true;
                    }
                }

                continue;
            }

            for (int edgeIndex = 0;
                 edgeIndex < triangleVertexIndices.Length;
                 edgeIndex++)
            {
                int nextEdgeIndex =
                    (edgeIndex + 1) %
                    triangleVertexIndices.Length;

                GetCanonicalEdgeVertexIndices(
                    triangleVertexIndices[edgeIndex],
                    triangleVertexIndices[nextEdgeIndex],
                    out int edgeVertexIndexA,
                    out int edgeVertexIndexB);

                if (AreSourceOriginsEquivalent(
                        originVertexIndexA,
                        originVertexIndexB,
                        edgeVertexIndexA,
                        edgeVertexIndexB,
                        sourceVertices,
                        sourceSplitVertexMatchEpsilon))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ClassifyTriangleForBoundaryDiagnostic(
        float distanceA,
        float distanceB,
        float distanceC,
        float geometryEpsilon)
    {
        bool hasPositiveVertex =
            distanceA > geometryEpsilon ||
            distanceB > geometryEpsilon ||
            distanceC > geometryEpsilon;

        bool hasNegativeVertex =
            distanceA < -geometryEpsilon ||
            distanceB < -geometryEpsilon ||
            distanceC < -geometryEpsilon;

        if (hasPositiveVertex && hasNegativeVertex)
        {
            return "Intersecting";
        }

        if (hasPositiveVertex)
        {
            return "PositiveOnlyOrTouching";
        }

        if (hasNegativeVertex)
        {
            return "NegativeOnlyOrTouching";
        }

        return "CoplanarWithinEpsilon";
    }

    /// <summary>
    /// 同じBoundary Edgeへ複数のCutSegmentが割り当てられた場合、
    /// それぞれの生成元Triangleを出力する。
    /// </summary>
    private static void LogDuplicateBoundaryEdgeDiagnostic(
        int firstNodeIndex,
        int secondNodeIndex,
        int existingSegmentIndex,
        int duplicateSegmentIndex,
        IReadOnlyList<CutSegment> cutSegments)
    {
        if (existingSegmentIndex < 0 ||
            existingSegmentIndex >= cutSegments.Count ||
            duplicateSegmentIndex < 0 ||
            duplicateSegmentIndex >= cutSegments.Count)
        {
            return;
        }

        CutSegment existingSegment =
            cutSegments[existingSegmentIndex];

        CutSegment duplicateSegment =
            cutSegments[duplicateSegmentIndex];

        Debug.LogWarning(
            "[MeshCutBoundaryDiagnostic][DuplicateEdge]\n" +
            $"BoundaryNodes=({firstNodeIndex}, {secondNodeIndex})\n" +
            "Existing: " +
            BuildCutSegmentSourceDiagnostic(
                existingSegmentIndex,
                existingSegment) +
            "\nDuplicate: " +
            BuildCutSegmentSourceDiagnostic(
                duplicateSegmentIndex,
                duplicateSegment));
    }

    private static string BuildCutSegmentSourceDiagnostic(
        int cutSegmentIndex,
        CutSegment cutSegment)
    {
        return
            $"CutSegmentIndex={cutSegmentIndex}, " +
            $"SubMesh={cutSegment.SourceSubMeshIndex}, " +
            $"Triangle={cutSegment.SourceTriangleIndex}, " +
            $"TriangleIndexBufferOffset={cutSegment.SourceTriangleIndexBufferOffset}, " +
            $"SourceVertexIndices=({cutSegment.SourceVertexIndexA}, " +
            $"{cutSegment.SourceVertexIndexB}, " +
            $"{cutSegment.SourceVertexIndexC}), " +
            $"FirstSourceOrigin=({cutSegment.FirstSourceEdgeVertexIndexA}, " +
            $"{cutSegment.FirstSourceEdgeVertexIndexB}), " +
            $"SecondSourceOrigin=({cutSegment.SecondSourceEdgeVertexIndexA}, " +
            $"{cutSegment.SecondSourceEdgeVertexIndexB}), " +
            $"First={FormatDiagnosticVector3(cutSegment.First)}, " +
            $"Second={FormatDiagnosticVector3(cutSegment.Second)}";
    }

    private static string FormatDiagnosticVector3(
        Vector3 value)
    {
        return
            $"({value.x:F6}, {value.y:F6}, {value.z:F6})";
    }

    private static int FindOrCreateBoundaryNode(
        List<BoundaryNode> nodes,
        Vector3 position,
        int sourceEdgeVertexIndexA,
        int sourceEdgeVertexIndexB,
        int connectivityVertexId,
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        for (int i = 0;
             i < nodes.Count;
             i++)
        {
            BoundaryNode node =
                nodes[i];

            if (connectivityVertexId >= 0 &&
                node.ConnectivityVertexId >= 0 &&
                node.ConnectivityVertexId != connectivityVertexId)
            {
                continue;
            }

            if (!ArePositionsClose(
                    node.Position,
                    position,
                    geometryEpsilon))
            {
                continue;
            }

            // Boundary Nodeは「近いから」ではなく、同じ元Vertex / Edge由来であることを
            // 主条件として統合する。異なるEdgeが同じ元Vertexを共有していても、
            // そのVertexがPlane上でない限り交点は別物なので、ここでは統合しない。
            if (!node.HasEquivalentSourceOrigin(
                    sourceEdgeVertexIndexA,
                    sourceEdgeVertexIndexB,
                    sourceVertices,
                    sourceSplitVertexMatchEpsilon))
            {
                continue;
            }

            node.AddSourceOrigin(
                sourceEdgeVertexIndexA,
                sourceEdgeVertexIndexB);

            return i;
        }

        nodes.Add(
            new BoundaryNode(
                position,
                sourceEdgeVertexIndexA,
                sourceEdgeVertexIndexB,
                connectivityVertexId));

        return nodes.Count - 1;
    }

    private static bool AreSourceOriginsEquivalent(
        int firstOriginVertexIndexA,
        int firstOriginVertexIndexB,
        int secondOriginVertexIndexA,
        int secondOriginVertexIndexB,
        IReadOnlyList<Vector3> sourceVertices,
        float sourceSplitVertexMatchEpsilon)
    {
        if (firstOriginVertexIndexA < 0 ||
            firstOriginVertexIndexB < 0 ||
            secondOriginVertexIndexA < 0 ||
            secondOriginVertexIndexB < 0)
        {
            return false;
        }

        if (firstOriginVertexIndexA == secondOriginVertexIndexA &&
            firstOriginVertexIndexB == secondOriginVertexIndexB)
        {
            return true;
        }

        if (!IsValidVertexIndex(
                firstOriginVertexIndexA,
                sourceVertices.Count) ||
            !IsValidVertexIndex(
                firstOriginVertexIndexB,
                sourceVertices.Count) ||
            !IsValidVertexIndex(
                secondOriginVertexIndexA,
                sourceVertices.Count) ||
            !IsValidVertexIndex(
                secondOriginVertexIndexB,
                sourceVertices.Count))
        {
            return false;
        }

        bool firstIsVertex =
            firstOriginVertexIndexA ==
            firstOriginVertexIndexB;

        bool secondIsVertex =
            secondOriginVertexIndexA ==
            secondOriginVertexIndexB;

        if (firstIsVertex != secondIsVertex)
        {
            return false;
        }

        if (firstIsVertex)
        {
            return ArePositionsClose(
                sourceVertices[firstOriginVertexIndexA],
                sourceVertices[secondOriginVertexIndexA],
                sourceSplitVertexMatchEpsilon);
        }

        Vector3 firstEdgeA =
            sourceVertices[firstOriginVertexIndexA];

        Vector3 firstEdgeB =
            sourceVertices[firstOriginVertexIndexB];

        Vector3 secondEdgeA =
            sourceVertices[secondOriginVertexIndexA];

        Vector3 secondEdgeB =
            sourceVertices[secondOriginVertexIndexB];

        bool sameDirection =
            ArePositionsClose(
                firstEdgeA,
                secondEdgeA,
                sourceSplitVertexMatchEpsilon) &&
            ArePositionsClose(
                firstEdgeB,
                secondEdgeB,
                sourceSplitVertexMatchEpsilon);

        if (sameDirection)
        {
            return true;
        }

        return
            ArePositionsClose(
                firstEdgeA,
                secondEdgeB,
                sourceSplitVertexMatchEpsilon) &&
            ArePositionsClose(
                firstEdgeB,
                secondEdgeA,
                sourceSplitVertexMatchEpsilon);
    }

    private static bool TryTraceBoundaryLoop(
        IReadOnlyList<BoundaryNode> nodes,
        int startNode,
        int firstNextNode,
        HashSet<long> visitedEdges,
        out List<Vector3> loop,
        out List<int> loopConnectivityVertexIds)
    {
        loop =
            new List<Vector3>();

        loopConnectivityVertexIds =
            new List<int>();

        int previousNode = -1;
        int currentNode = startNode;
        int nextNode = firstNextNode;
        int guard = 0;
        int maximumSteps = nodes.Count + 1;

        while (true)
        {
            loop.Add(
                nodes[currentNode].Position);

            loopConnectivityVertexIds.Add(
                nodes[currentNode].ConnectivityVertexId);

            visitedEdges.Add(
                CreateEdgeKey(
                    currentNode,
                    nextNode));

            previousNode =
                currentNode;

            currentNode =
                nextNode;

            if (currentNode == startNode)
            {
                return true;
            }

            if (nodes[currentNode]
                .Neighbors.Count != 2)
            {
                loop = null;
                loopConnectivityVertexIds = null;
                return false;
            }

            int firstNeighbor =
                nodes[currentNode]
                    .Neighbors[0];

            int secondNeighbor =
                nodes[currentNode]
                    .Neighbors[1];

            nextNode =
                firstNeighbor == previousNode
                    ? secondNeighbor
                    : firstNeighbor;

            guard++;

            if (guard > maximumSteps)
            {
                loop = null;
                loopConnectivityVertexIds = null;
                return false;
            }
        }
    }

    private static long CreateEdgeKey(
        int firstNode,
        int secondNode)
    {
        int minimum =
            Mathf.Min(
                firstNode,
                secondNode);

        int maximum =
            Mathf.Max(
                firstNode,
                secondNode);

        return ((long)minimum << 32) |
               (uint)maximum;
    }

    private static bool TryAddCutSurfaceCaps(
        IReadOnlyList<List<Vector3>> boundaryLoops,
        IReadOnlyList<List<int>> boundaryConnectivityVertexIds,
        Plane cutPlane,
        float cutSurfaceUvScale,
        float geometryEpsilon,
        int cutSurfaceSubMeshIndex,
        PieceMeshBuilder firstBuilder,
        PieceMeshBuilder secondBuilder,
        out string failureReason)
    {
        using (addCutSurfaceCapsProfilerMarker.Auto())
        {
        if (boundaryLoops == null ||
            boundaryConnectivityVertexIds == null ||
            boundaryLoops.Count != boundaryConnectivityVertexIds.Count)
        {
            failureReason =
                "切断境界とConnectivity IDの対応が不正です。";
            return false;
        }

        CreatePlaneBasis(
            cutPlane.normal,
            out Vector3 planeAxisU,
            out Vector3 planeAxisV);

        var preparedLoops =
            new List<PreparedBoundaryLoop>(
                boundaryLoops.Count);

        for (int i = 0;
             i < boundaryLoops.Count;
             i++)
        {
            if (!TryPrepareBoundaryLoop(
                    boundaryLoops[i],
                    boundaryConnectivityVertexIds[i],
                    planeAxisU,
                    planeAxisV,
                    geometryEpsilon,
                    out PreparedBoundaryLoop preparedLoop))
            {
                failureReason =
                    "切断境界を断面三角形分割用の単純Polygonへ変換できませんでした。";

                return false;
            }

            preparedLoops.Add(
                preparedLoop);
        }

        if (!TryAssignLoopHierarchy(
                preparedLoops,
                geometryEpsilon,
                out failureReason))
        {
            return false;
        }

        float earEpsilon =
            geometryEpsilon * geometryEpsilon;

        for (int outerLoopIndex = 0;
             outerLoopIndex < preparedLoops.Count;
             outerLoopIndex++)
        {
            PreparedBoundaryLoop outerLoop =
                preparedLoops[outerLoopIndex];

            // Even depth is material, odd depth is a hole.
            // depth 2, 4... are islands inside holes and are triangulated
            // as independent filled regions by their own iteration.
            if ((outerLoop.NestingDepth & 1) != 0)
            {
                continue;
            }

            var mergedPositions =
                new List<Vector3>(
                    outerLoop.Positions);

            var mergedProjectedPoints =
                new List<Vector2>(
                    outerLoop.ProjectedPoints);

            var mergedConnectivityVertexIds =
                new List<int>(
                    outerLoop.ConnectivityVertexIds);

            var directHoles =
                new List<PreparedBoundaryLoop>();

            for (int loopIndex = 0;
                 loopIndex < preparedLoops.Count;
                 loopIndex++)
            {
                PreparedBoundaryLoop candidate =
                    preparedLoops[loopIndex];

                if (candidate.ParentIndex == outerLoopIndex &&
                    (candidate.NestingDepth & 1) != 0)
                {
                    directHoles.Add(candidate);
                }
            }

            // 右端が外側に近いHoleから橋渡しする方が、
            // Bridge同士が交差するケースを減らせる。
            directHoles.Sort(
                (first, second) =>
                    GetRightmostX(second.ProjectedPoints)
                        .CompareTo(
                            GetRightmostX(first.ProjectedPoints)));

            for (int holeIndex = 0;
                 holeIndex < directHoles.Count;
                 holeIndex++)
            {
                if (!TryMergeHoleIntoPolygon(
                        mergedPositions,
                        mergedProjectedPoints,
                        mergedConnectivityVertexIds,
                        directHoles[holeIndex],
                        directHoles,
                        geometryEpsilon,
                        out failureReason))
                {
                    return false;
                }
            }

            if (!TryTriangulateEarClipping(
                    mergedProjectedPoints,
                    earEpsilon,
                    out List<int> triangleIndices))
            {
                failureReason =
                    "Ear Clippingによる切断面生成に失敗しました。";

                return false;
            }

            for (int triangleIndex = 0;
                 triangleIndex < triangleIndices.Count;
                 triangleIndex += 3)
            {
                Vector3 pointA =
                    mergedPositions[
                        triangleIndices[triangleIndex]];

                Vector3 pointB =
                    mergedPositions[
                        triangleIndices[triangleIndex + 1]];

                Vector3 pointC =
                    mergedPositions[
                        triangleIndices[triangleIndex + 2]];

                int connectivityVertexIdA =
                    mergedConnectivityVertexIds[
                        triangleIndices[triangleIndex]];

                int connectivityVertexIdB =
                    mergedConnectivityVertexIds[
                        triangleIndices[triangleIndex + 1]];

                int connectivityVertexIdC =
                    mergedConnectivityVertexIds[
                        triangleIndices[triangleIndex + 2]];

                AddCapTriangle(
                    firstBuilder,
                    cutSurfaceSubMeshIndex,
                    pointA,
                    pointB,
                    pointC,
                    connectivityVertexIdA,
                    connectivityVertexIdB,
                    connectivityVertexIdC,
                    -cutPlane.normal,
                    planeAxisU,
                    planeAxisV,
                    cutSurfaceUvScale);

                AddCapTriangle(
                    secondBuilder,
                    cutSurfaceSubMeshIndex,
                    pointA,
                    pointB,
                    pointC,
                    connectivityVertexIdA,
                    connectivityVertexIdB,
                    connectivityVertexIdC,
                    cutPlane.normal,
                    planeAxisU,
                    planeAxisV,
                    cutSurfaceUvScale);
            }
        }

        failureReason = null;
        return true;
    
        }
    }

    private static void CreatePlaneBasis(
        Vector3 planeNormal,
        out Vector3 planeAxisU,
        out Vector3 planeAxisV)
    {
        Vector3 referenceAxis =
            Mathf.Abs(
                Vector3.Dot(
                    planeNormal,
                    Vector3.up)) < 0.95f
                ? Vector3.up
                : Vector3.right;

        planeAxisU =
            Vector3.Cross(
                referenceAxis,
                planeNormal).normalized;

        if (planeAxisU.sqrMagnitude <=
            MIN_DIRECTION_SQR_MAGNITUDE)
        {
            referenceAxis =
                Vector3.forward;

            planeAxisU =
                Vector3.Cross(
                    referenceAxis,
                    planeNormal).normalized;
        }

        planeAxisV =
            Vector3.Cross(
                planeNormal,
                planeAxisU).normalized;
    }

    private static bool TryPrepareBoundaryLoop(
        IReadOnlyList<Vector3> sourcePositions,
        IReadOnlyList<int> sourceConnectivityVertexIds,
        Vector3 planeAxisU,
        Vector3 planeAxisV,
        float geometryEpsilon,
        out PreparedBoundaryLoop preparedLoop)
    {
        if (sourceConnectivityVertexIds == null ||
            sourceConnectivityVertexIds.Count != sourcePositions.Count)
        {
            preparedLoop = null;
            return false;
        }

        var positions =
            new List<Vector3>(
                sourcePositions.Count);

        var connectivityVertexIds =
            new List<int>(
                sourceConnectivityVertexIds.Count);

        var projectedPoints =
            new List<Vector2>(
                sourcePositions.Count);

        for (int i = 0;
             i < sourcePositions.Count;
             i++)
        {
            positions.Add(
                sourcePositions[i]);

            connectivityVertexIds.Add(
                sourceConnectivityVertexIds[i]);

            projectedPoints.Add(
                ProjectToPlane2D(
                    sourcePositions[i],
                    planeAxisU,
                    planeAxisV));
        }

        RemoveRedundantLoopPoints(
            positions,
            projectedPoints,
            connectivityVertexIds,
            geometryEpsilon);

        if (positions.Count < 3)
        {
            preparedLoop = null;
            return false;
        }

        float signedArea =
            CalculateSignedArea(
                projectedPoints);

        if (Mathf.Abs(signedArea) <=
            geometryEpsilon * geometryEpsilon)
        {
            preparedLoop = null;
            return false;
        }

        if (signedArea < 0.0f)
        {
            positions.Reverse();
            projectedPoints.Reverse();
            connectivityVertexIds.Reverse();
        }

        preparedLoop =
            new PreparedBoundaryLoop(
                positions,
                projectedPoints,
                connectivityVertexIds);

        return true;
    }

    private static Vector2 ProjectToPlane2D(
        Vector3 position,
        Vector3 planeAxisU,
        Vector3 planeAxisV)
    {
        return new Vector2(
            Vector3.Dot(
                position,
                planeAxisU),
            Vector3.Dot(
                position,
                planeAxisV));
    }

    private static void RemoveRedundantLoopPoints(
        List<Vector3> positions,
        List<Vector2> projectedPoints,
        List<int> connectivityVertexIds,
        float geometryEpsilon)
    {
        if (positions.Count < 3)
        {
            return;
        }

        float sqrEpsilon =
            geometryEpsilon * geometryEpsilon;

        bool removedPoint;
        int guard = 0;

        do
        {
            removedPoint = false;

            for (int i = 0;
                 i < projectedPoints.Count;
                 i++)
            {
                int previousIndex =
                    (i - 1 +
                     projectedPoints.Count) %
                    projectedPoints.Count;

                int nextIndex =
                    (i + 1) %
                    projectedPoints.Count;

                Vector2 previous =
                    projectedPoints[previousIndex];

                Vector2 current =
                    projectedPoints[i];

                Vector2 next =
                    projectedPoints[nextIndex];

                if ((current - previous).sqrMagnitude <=
                    sqrEpsilon ||
                    (next - current).sqrMagnitude <=
                    sqrEpsilon)
                {
                    positions.RemoveAt(i);
                    projectedPoints.RemoveAt(i);
                    connectivityVertexIds.RemoveAt(i);
                    removedPoint = true;
                    break;
                }

                Vector2 firstDirection =
                    current - previous;

                Vector2 secondDirection =
                    next - current;

                float cross =
                    Mathf.Abs(
                        Cross2D(
                            firstDirection,
                            secondDirection));

                float scale =
                    firstDirection.magnitude +
                    secondDirection.magnitude;

                if (cross <=
                    geometryEpsilon * scale)
                {
                    positions.RemoveAt(i);
                    projectedPoints.RemoveAt(i);
                    connectivityVertexIds.RemoveAt(i);
                    removedPoint = true;
                    break;
                }
            }

            guard++;
        }
        while (removedPoint &&
               projectedPoints.Count > 3 &&
               guard < 1024);
    }

    private static float CalculateSignedArea(
        IReadOnlyList<Vector2> polygon)
    {
        float area = 0.0f;

        for (int i = 0;
             i < polygon.Count;
             i++)
        {
            Vector2 current =
                polygon[i];

            Vector2 next =
                polygon[
                    (i + 1) %
                    polygon.Count];

            area +=
                current.x * next.y -
                next.x * current.y;
        }

        return area * 0.5f;
    }

    private static bool TryAssignLoopHierarchy(
        IReadOnlyList<PreparedBoundaryLoop> loops,
        float geometryEpsilon,
        out string failureReason)
    {
        float areaEpsilon =
            geometryEpsilon * geometryEpsilon;

        for (int innerIndex = 0;
             innerIndex < loops.Count;
             innerIndex++)
        {
            PreparedBoundaryLoop innerLoop =
                loops[innerIndex];

            innerLoop.ParentIndex = -1;
            innerLoop.NestingDepth = 0;

            float innerArea =
                Mathf.Abs(
                    CalculateSignedArea(
                        innerLoop.ProjectedPoints));

            float bestParentArea =
                float.PositiveInfinity;

            Vector2 testPoint =
                innerLoop.ProjectedPoints[0];

            for (int outerIndex = 0;
                 outerIndex < loops.Count;
                 outerIndex++)
            {
                if (outerIndex == innerIndex)
                {
                    continue;
                }

                PreparedBoundaryLoop outerLoop =
                    loops[outerIndex];

                float outerArea =
                    Mathf.Abs(
                        CalculateSignedArea(
                            outerLoop.ProjectedPoints));

                if (outerArea <=
                    innerArea + areaEpsilon)
                {
                    continue;
                }

                if (!IsPointInsidePolygon(
                        testPoint,
                        outerLoop.ProjectedPoints))
                {
                    continue;
                }

                if (outerArea < bestParentArea)
                {
                    bestParentArea =
                        outerArea;

                    innerLoop.ParentIndex =
                        outerIndex;
                }
            }
        }

        for (int loopIndex = 0;
             loopIndex < loops.Count;
             loopIndex++)
        {
            int depth = 0;
            int parentIndex =
                loops[loopIndex].ParentIndex;

            int guard = 0;

            while (parentIndex >= 0)
            {
                depth++;
                parentIndex =
                    loops[parentIndex].ParentIndex;

                guard++;

                if (guard > loops.Count)
                {
                    failureReason =
                        "切断境界の入れ子関係に循環が検出されました。";

                    return false;
                }
            }

            loops[loopIndex].NestingDepth =
                depth;
        }

        failureReason = null;
        return true;
    }

    private static float GetRightmostX(
        IReadOnlyList<Vector2> polygon)
    {
        float rightmostX =
            float.NegativeInfinity;

        for (int i = 0;
             i < polygon.Count;
             i++)
        {
            rightmostX =
                Mathf.Max(
                    rightmostX,
                    polygon[i].x);
        }

        return rightmostX;
    }

    /// <summary>
    /// Holeを外周へ可視なBridgeで接続し、Ear Clipping可能な弱単純Polygonへ変換する。
    /// </summary>
    /// <remarks>
    /// Bridgeの両端は意図的に2回現れる。これはHoleを切り開いて外周へ接続するために必要で、
    /// 通常の重複頂点除去を行うとHoleが再び閉じてしまう。
    /// </remarks>
    private static bool TryMergeHoleIntoPolygon(
        List<Vector3> polygonPositions,
        List<Vector2> polygonProjectedPoints,
        List<int> polygonConnectivityVertexIds,
        PreparedBoundaryLoop hole,
        IReadOnlyList<PreparedBoundaryLoop> siblingHoles,
        float geometryEpsilon,
        out string failureReason)
    {
        int holeBridgeIndex =
            FindRightmostVertexIndex(
                hole.ProjectedPoints);

        Vector2 holeBridgePoint =
            hole.ProjectedPoints[
                holeBridgeIndex];

        int polygonBridgeIndex = -1;
        float bestSqrDistance =
            float.PositiveInfinity;

        for (int candidateIndex = 0;
             candidateIndex < polygonProjectedPoints.Count;
             candidateIndex++)
        {
            Vector2 candidatePoint =
                polygonProjectedPoints[
                    candidateIndex];

            float sqrDistance =
                (candidatePoint -
                 holeBridgePoint).sqrMagnitude;

            if (sqrDistance <=
                geometryEpsilon * geometryEpsilon ||
                sqrDistance >= bestSqrDistance)
            {
                continue;
            }

            if (!IsBridgeVisible(
                    holeBridgePoint,
                    candidatePoint,
                    candidateIndex,
                    polygonProjectedPoints,
                    holeBridgeIndex,
                    hole.ProjectedPoints,
                    siblingHoles,
                    hole,
                    geometryEpsilon))
            {
                continue;
            }

            polygonBridgeIndex =
                candidateIndex;

            bestSqrDistance =
                sqrDistance;
        }

        if (polygonBridgeIndex < 0)
        {
            failureReason =
                "切断面Holeを外周へ接続できるBridgeが見つかりませんでした。";

            return false;
        }

        var mergedPositions =
            new List<Vector3>(
                polygonPositions.Count +
                hole.Positions.Count +
                2);

        var mergedProjectedPoints =
            new List<Vector2>(
                polygonProjectedPoints.Count +
                hole.ProjectedPoints.Count +
                2);

        var mergedConnectivityVertexIds =
            new List<int>(
                polygonConnectivityVertexIds.Count +
                hole.ConnectivityVertexIds.Count +
                2);

        for (int i = 0;
             i <= polygonBridgeIndex;
             i++)
        {
            mergedPositions.Add(
                polygonPositions[i]);

            mergedProjectedPoints.Add(
                polygonProjectedPoints[i]);

            mergedConnectivityVertexIds.Add(
                polygonConnectivityVertexIds[i]);
        }

        // PreparedBoundaryLoopは全てCCWへ正規化されている。
        // Holeは逆向き(CW)に辿ることでeven-oddの空洞を維持する。
        for (int offset = 0;
             offset < hole.Positions.Count;
             offset++)
        {
            int holeIndex =
                (holeBridgeIndex - offset +
                 hole.Positions.Count) %
                hole.Positions.Count;

            mergedPositions.Add(
                hole.Positions[holeIndex]);

            mergedProjectedPoints.Add(
                hole.ProjectedPoints[holeIndex]);

            mergedConnectivityVertexIds.Add(
                hole.ConnectivityVertexIds[holeIndex]);
        }

        mergedPositions.Add(
            hole.Positions[holeBridgeIndex]);

        mergedProjectedPoints.Add(
            hole.ProjectedPoints[holeBridgeIndex]);

        mergedConnectivityVertexIds.Add(
            hole.ConnectivityVertexIds[holeBridgeIndex]);

        mergedPositions.Add(
            polygonPositions[polygonBridgeIndex]);

        mergedProjectedPoints.Add(
            polygonProjectedPoints[polygonBridgeIndex]);

        mergedConnectivityVertexIds.Add(
            polygonConnectivityVertexIds[polygonBridgeIndex]);

        for (int i = polygonBridgeIndex + 1;
             i < polygonPositions.Count;
             i++)
        {
            mergedPositions.Add(
                polygonPositions[i]);

            mergedProjectedPoints.Add(
                polygonProjectedPoints[i]);

            mergedConnectivityVertexIds.Add(
                polygonConnectivityVertexIds[i]);
        }

        polygonPositions.Clear();
        polygonPositions.AddRange(
            mergedPositions);

        polygonProjectedPoints.Clear();
        polygonProjectedPoints.AddRange(
            mergedProjectedPoints);

        polygonConnectivityVertexIds.Clear();
        polygonConnectivityVertexIds.AddRange(
            mergedConnectivityVertexIds);

        failureReason = null;
        return true;
    }

    private static int FindRightmostVertexIndex(
        IReadOnlyList<Vector2> polygon)
    {
        int bestIndex = 0;

        for (int i = 1;
             i < polygon.Count;
             i++)
        {
            Vector2 candidate =
                polygon[i];

            Vector2 currentBest =
                polygon[bestIndex];

            if (candidate.x > currentBest.x ||
                (Mathf.Approximately(
                     candidate.x,
                     currentBest.x) &&
                 candidate.y < currentBest.y))
            {
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static bool IsBridgeVisible(
        Vector2 holePoint,
        Vector2 polygonPoint,
        int polygonPointIndex,
        IReadOnlyList<Vector2> polygon,
        int holePointIndex,
        IReadOnlyList<Vector2> hole,
        IReadOnlyList<PreparedBoundaryLoop> siblingHoles,
        PreparedBoundaryLoop currentHole,
        float geometryEpsilon)
    {
        if (SegmentIntersectsPolygon(
                holePoint,
                polygonPoint,
                polygon,
                polygonPointIndex,
                geometryEpsilon))
        {
            return false;
        }

        if (SegmentIntersectsPolygon(
                holePoint,
                polygonPoint,
                hole,
                holePointIndex,
                geometryEpsilon))
        {
            return false;
        }

        for (int holeIndex = 0;
             holeIndex < siblingHoles.Count;
             holeIndex++)
        {
            PreparedBoundaryLoop siblingHole =
                siblingHoles[holeIndex];

            if (object.ReferenceEquals(
                    siblingHole,
                    currentHole))
            {
                continue;
            }

            if (SegmentIntersectsPolygon(
                    holePoint,
                    polygonPoint,
                    siblingHole.ProjectedPoints,
                    -1,
                    geometryEpsilon))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SegmentIntersectsPolygon(
        Vector2 segmentStart,
        Vector2 segmentEnd,
        IReadOnlyList<Vector2> polygon,
        int allowedEndpointIndex,
        float geometryEpsilon)
    {
        float sqrEpsilon =
            geometryEpsilon * geometryEpsilon;

        for (int edgeStartIndex = 0;
             edgeStartIndex < polygon.Count;
             edgeStartIndex++)
        {
            int edgeEndIndex =
                (edgeStartIndex + 1) %
                polygon.Count;

            Vector2 edgeStart =
                polygon[edgeStartIndex];

            Vector2 edgeEnd =
                polygon[edgeEndIndex];

            if (allowedEndpointIndex >= 0 &&
                (edgeStartIndex == allowedEndpointIndex ||
                 edgeEndIndex == allowedEndpointIndex))
            {
                continue;
            }

            // Holeを複数Bridgeした後は同じ座標のBridge端点が
            // 非隣接Indexとして残るため、座標一致も共有端点として扱う。
            if ((edgeStart - segmentEnd).sqrMagnitude <= sqrEpsilon ||
                (edgeEnd - segmentEnd).sqrMagnitude <= sqrEpsilon ||
                (edgeStart - segmentStart).sqrMagnitude <= sqrEpsilon ||
                (edgeEnd - segmentStart).sqrMagnitude <= sqrEpsilon)
            {
                continue;
            }

            if (DoSegmentsIntersect(
                    segmentStart,
                    segmentEnd,
                    edgeStart,
                    edgeEnd,
                    geometryEpsilon))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoSegmentsIntersect(
        Vector2 firstStart,
        Vector2 firstEnd,
        Vector2 secondStart,
        Vector2 secondEnd,
        float epsilon)
    {
        float firstCross =
            Cross2D(
                firstEnd - firstStart,
                secondStart - firstStart);

        float secondCross =
            Cross2D(
                firstEnd - firstStart,
                secondEnd - firstStart);

        float thirdCross =
            Cross2D(
                secondEnd - secondStart,
                firstStart - secondStart);

        float fourthCross =
            Cross2D(
                secondEnd - secondStart,
                firstEnd - secondStart);

        if (((firstCross > epsilon && secondCross < -epsilon) ||
             (firstCross < -epsilon && secondCross > epsilon)) &&
            ((thirdCross > epsilon && fourthCross < -epsilon) ||
             (thirdCross < -epsilon && fourthCross > epsilon)))
        {
            return true;
        }

        if (Mathf.Abs(firstCross) <= epsilon &&
            IsPointOnSegment(
                secondStart,
                firstStart,
                firstEnd,
                epsilon))
        {
            return true;
        }

        if (Mathf.Abs(secondCross) <= epsilon &&
            IsPointOnSegment(
                secondEnd,
                firstStart,
                firstEnd,
                epsilon))
        {
            return true;
        }

        if (Mathf.Abs(thirdCross) <= epsilon &&
            IsPointOnSegment(
                firstStart,
                secondStart,
                secondEnd,
                epsilon))
        {
            return true;
        }

        return Mathf.Abs(fourthCross) <= epsilon &&
               IsPointOnSegment(
                   firstEnd,
                   secondStart,
                   secondEnd,
                   epsilon);
    }

    private static bool IsPointOnSegment(
        Vector2 point,
        Vector2 segmentStart,
        Vector2 segmentEnd,
        float epsilon)
    {
        return point.x >=
                   Mathf.Min(
                       segmentStart.x,
                       segmentEnd.x) - epsilon &&
               point.x <=
                   Mathf.Max(
                       segmentStart.x,
                       segmentEnd.x) + epsilon &&
               point.y >=
                   Mathf.Min(
                       segmentStart.y,
                       segmentEnd.y) - epsilon &&
               point.y <=
                   Mathf.Max(
                       segmentStart.y,
                       segmentEnd.y) + epsilon;
    }

    private static bool IsPointInsidePolygon(
        Vector2 point,
        IReadOnlyList<Vector2> polygon)
    {
        bool inside = false;

        for (int currentIndex = 0,
                 previousIndex = polygon.Count - 1;
             currentIndex < polygon.Count;
             previousIndex = currentIndex++)
        {
            Vector2 current =
                polygon[currentIndex];

            Vector2 previous =
                polygon[previousIndex];

            bool intersects =
                (current.y > point.y) !=
                (previous.y > point.y) &&
                point.x <
                (previous.x - current.x) *
                (point.y - current.y) /
                (previous.y - current.y) +
                current.x;

            if (intersects)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool TryTriangulateEarClipping(
        IReadOnlyList<Vector2> polygon,
        float areaEpsilon,
        out List<int> triangleIndices)
    {
        using (triangulateCapsProfilerMarker.Auto())
        {
        triangleIndices =
            new List<int>();

        if (polygon.Count < 3)
        {
            return false;
        }

        var remainingIndices =
            new List<int>(
                polygon.Count);

        for (int i = 0;
             i < polygon.Count;
             i++)
        {
            remainingIndices.Add(i);
        }

        int guard = 0;

        int maximumIterations =
            polygon.Count * polygon.Count;

        while (remainingIndices.Count > 3)
        {
            bool foundEar = false;

            for (int i = 0;
                 i < remainingIndices.Count;
                 i++)
            {
                int previousListIndex =
                    (i - 1 +
                     remainingIndices.Count) %
                    remainingIndices.Count;

                int nextListIndex =
                    (i + 1) %
                    remainingIndices.Count;

                int previousIndex =
                    remainingIndices[
                        previousListIndex];

                int currentIndex =
                    remainingIndices[i];

                int nextIndex =
                    remainingIndices[
                        nextListIndex];

                Vector2 previous =
                    polygon[previousIndex];

                Vector2 current =
                    polygon[currentIndex];

                Vector2 next =
                    polygon[nextIndex];

                float cornerCross =
                    Cross2D(
                        current - previous,
                        next - current);

                if (cornerCross <= areaEpsilon)
                {
                    continue;
                }

                bool containsOtherVertex = false;

                for (int testListIndex = 0;
                     testListIndex < remainingIndices.Count;
                     testListIndex++)
                {
                    int testIndex =
                        remainingIndices[
                            testListIndex];

                    if (testIndex == previousIndex ||
                        testIndex == currentIndex ||
                        testIndex == nextIndex)
                    {
                        continue;
                    }

                    Vector2 testPoint =
                        polygon[testIndex];

                    // Hole Bridgeでは同一座標が意図的に2回現れる。
                    // それを「Ear内部の別頂点」と判定すると、Bridge周辺の
                    // Earが永久に除外されて三角形分割できなくなる。
                    if ((testPoint - previous).sqrMagnitude <= areaEpsilon ||
                        (testPoint - current).sqrMagnitude <= areaEpsilon ||
                        (testPoint - next).sqrMagnitude <= areaEpsilon)
                    {
                        continue;
                    }

                    if (IsPointInsideTriangle(
                            testPoint,
                            previous,
                            current,
                            next,
                            areaEpsilon))
                    {
                        containsOtherVertex = true;
                        break;
                    }
                }

                if (containsOtherVertex)
                {
                    continue;
                }

                triangleIndices.Add(
                    previousIndex);

                triangleIndices.Add(
                    currentIndex);

                triangleIndices.Add(
                    nextIndex);

                remainingIndices.RemoveAt(i);

                foundEar = true;
                break;
            }

            if (!foundEar)
            {
                triangleIndices = null;
                return false;
            }

            guard++;

            if (guard > maximumIterations)
            {
                triangleIndices = null;
                return false;
            }
        }

        triangleIndices.Add(
            remainingIndices[0]);

        triangleIndices.Add(
            remainingIndices[1]);

        triangleIndices.Add(
            remainingIndices[2]);

        return true;
    
        }
    }

    private static bool IsPointInsideTriangle(
        Vector2 point,
        Vector2 first,
        Vector2 second,
        Vector2 third,
        float areaEpsilon)
    {
        float firstCross =
            Cross2D(
                second - first,
                point - first);

        float secondCross =
            Cross2D(
                third - second,
                point - second);

        float thirdCross =
            Cross2D(
                first - third,
                point - third);

        return firstCross >= -areaEpsilon &&
               secondCross >= -areaEpsilon &&
               thirdCross >= -areaEpsilon;
    }

    private static float Cross2D(
        Vector2 first,
        Vector2 second)
    {
        return first.x * second.y -
               first.y * second.x;
    }

    private static void AddCapTriangle(
        PieceMeshBuilder builder,
        int cutSurfaceSubMeshIndex,
        Vector3 pointA,
        Vector3 pointB,
        Vector3 pointC,
        int connectivityVertexIdA,
        int connectivityVertexIdB,
        int connectivityVertexIdC,
        Vector3 desiredNormal,
        Vector3 planeAxisU,
        Vector3 planeAxisV,
        float cutSurfaceUvScale)
    {
        Vector3 actualNormal =
            Vector3.Cross(
                pointB - pointA,
                pointC - pointA);

        if (Vector3.Dot(
                actualNormal,
                desiredNormal) < 0.0f)
        {
            Vector3 temporary =
                pointB;

            pointB =
                pointC;

            pointC =
                temporary;

            int temporaryConnectivityVertexId =
                connectivityVertexIdB;

            connectivityVertexIdB =
                connectivityVertexIdC;

            connectivityVertexIdC =
                temporaryConnectivityVertexId;
        }

        Vector4 tangent =
            new Vector4(
                planeAxisU.x,
                planeAxisU.y,
                planeAxisU.z,
                1.0f);

        VertexData vertexA =
            CreateCapVertex(
                pointA,
                connectivityVertexIdA,
                desiredNormal,
                tangent,
                planeAxisU,
                planeAxisV,
                cutSurfaceUvScale);

        VertexData vertexB =
            CreateCapVertex(
                pointB,
                connectivityVertexIdB,
                desiredNormal,
                tangent,
                planeAxisU,
                planeAxisV,
                cutSurfaceUvScale);

        VertexData vertexC =
            CreateCapVertex(
                pointC,
                connectivityVertexIdC,
                desiredNormal,
                tangent,
                planeAxisU,
                planeAxisV,
                cutSurfaceUvScale);

        builder.AddTriangle(
            vertexA,
            vertexB,
            vertexC,
            cutSurfaceSubMeshIndex,
            true);
    }

    private static VertexData CreateCapVertex(
        Vector3 position,
        int connectivityVertexId,
        Vector3 normal,
        Vector4 tangent,
        Vector3 planeAxisU,
        Vector3 planeAxisV,
        float cutSurfaceUvScale)
    {
        Vector2 uv =
            new Vector2(
                Vector3.Dot(
                    position,
                    planeAxisU) *
                cutSurfaceUvScale,
                Vector3.Dot(
                    position,
                    planeAxisV) *
                cutSurfaceUvScale);

        return new VertexData(
            position,
            normal,
            uv,
            tangent,
            connectivityVertexId);
    }

    private readonly struct VertexData
    {
        private const int NO_SOURCE_VERTEX_INDEX = -1;

        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Vector2 Uv { get; }
        public Vector4 Tangent { get; }
        public int ConnectivityVertexId { get; }
        public int SourceVertexIndex { get; }
        public bool HasSourceVertexIndex =>
            SourceVertexIndex != NO_SOURCE_VERTEX_INDEX;

        public VertexData(
            Vector3 position,
            Vector3 normal,
            Vector2 uv,
            Vector4 tangent)
            : this(
                position,
                normal,
                uv,
                tangent,
                -1,
                NO_SOURCE_VERTEX_INDEX)
        {
        }

        public VertexData(
            Vector3 position,
            Vector3 normal,
            Vector2 uv,
            Vector4 tangent,
            int connectivityVertexId)
            : this(
                position,
                normal,
                uv,
                tangent,
                connectivityVertexId,
                NO_SOURCE_VERTEX_INDEX)
        {
        }

        public VertexData(
            Vector3 position,
            Vector3 normal,
            Vector2 uv,
            Vector4 tangent,
            int connectivityVertexId,
            int sourceVertexIndex)
        {
            Position = position;
            Normal = normal;
            Uv = uv;
            Tangent = tangent;
            ConnectivityVertexId = connectivityVertexId;
            SourceVertexIndex = sourceVertexIndex;
        }

        public static VertexData Lerp(
            VertexData first,
            VertexData second,
            float interpolation,
            int connectivityVertexId)
        {
            Vector3 normal =
                Vector3.Lerp(
                    first.Normal,
                    second.Normal,
                    interpolation);

            if (normal.sqrMagnitude >
                MIN_DIRECTION_SQR_MAGNITUDE)
            {
                normal.Normalize();
            }

            Vector4 tangent =
                Vector4.Lerp(
                    first.Tangent,
                    second.Tangent,
                    interpolation);

            Vector3 tangentDirection =
                new Vector3(
                    tangent.x,
                    tangent.y,
                    tangent.z);

            if (tangentDirection.sqrMagnitude >
                MIN_DIRECTION_SQR_MAGNITUDE)
            {
                tangentDirection.Normalize();

                tangent.x =
                    tangentDirection.x;

                tangent.y =
                    tangentDirection.y;

                tangent.z =
                    tangentDirection.z;
            }

            tangent.w =
                tangent.w < 0.0f
                    ? -1.0f
                    : 1.0f;

            return new VertexData(
                Vector3.Lerp(
                    first.Position,
                    second.Position,
                    interpolation),
                normal,
                Vector2.Lerp(
                    first.Uv,
                    second.Uv,
                    interpolation),
                tangent,
                connectivityVertexId);
        }
    }

    private readonly struct CutSegment
    {
        public Vector3 First { get; }
        public Vector3 Second { get; }

        // (A, B) は、この端点を生成した元Mesh Vertex / Edgeの識別情報。
        // A == B の場合はPlane上の元頂点、A != B の場合は元Mesh Edgeを表す。
        public int FirstSourceEdgeVertexIndexA { get; }
        public int FirstSourceEdgeVertexIndexB { get; }
        public int SecondSourceEdgeVertexIndexA { get; }
        public int SecondSourceEdgeVertexIndexB { get; }
        public int FirstConnectivityVertexId { get; }
        public int SecondConnectivityVertexId { get; }

        // 以下は境界異常時に生成元Triangleまで追跡するための診断情報。
        public int SourceSubMeshIndex { get; }
        public int SourceTriangleIndex { get; }
        public int SourceTriangleIndexBufferOffset { get; }
        public int SourceVertexIndexA { get; }
        public int SourceVertexIndexB { get; }
        public int SourceVertexIndexC { get; }

        public CutSegment(
            Vector3 first,
            Vector3 second)
            : this(
                first,
                second,
                -1,
                -1,
                -1,
                -1,
                -1,
                -1,
                -1,
                -1,
                -1,
                -1,
                -1,
                -1)
        {
        }

        public CutSegment(
            Vector3 first,
            Vector3 second,
            int firstSourceEdgeVertexIndexA,
            int firstSourceEdgeVertexIndexB,
            int secondSourceEdgeVertexIndexA,
            int secondSourceEdgeVertexIndexB,
            int firstConnectivityVertexId,
            int secondConnectivityVertexId)
            : this(
                first,
                second,
                firstSourceEdgeVertexIndexA,
                firstSourceEdgeVertexIndexB,
                secondSourceEdgeVertexIndexA,
                secondSourceEdgeVertexIndexB,
                firstConnectivityVertexId,
                secondConnectivityVertexId,
                -1,
                -1,
                -1,
                -1,
                -1,
                -1)
        {
        }

        public CutSegment(
            Vector3 first,
            Vector3 second,
            int firstSourceEdgeVertexIndexA,
            int firstSourceEdgeVertexIndexB,
            int secondSourceEdgeVertexIndexA,
            int secondSourceEdgeVertexIndexB,
            int firstConnectivityVertexId,
            int secondConnectivityVertexId,
            int sourceSubMeshIndex,
            int sourceTriangleIndex,
            int sourceTriangleIndexBufferOffset,
            int sourceVertexIndexA,
            int sourceVertexIndexB,
            int sourceVertexIndexC)
        {
            First = first;
            Second = second;
            FirstSourceEdgeVertexIndexA = firstSourceEdgeVertexIndexA;
            FirstSourceEdgeVertexIndexB = firstSourceEdgeVertexIndexB;
            SecondSourceEdgeVertexIndexA = secondSourceEdgeVertexIndexA;
            SecondSourceEdgeVertexIndexB = secondSourceEdgeVertexIndexB;
            FirstConnectivityVertexId = firstConnectivityVertexId;
            SecondConnectivityVertexId = secondConnectivityVertexId;
            SourceSubMeshIndex = sourceSubMeshIndex;
            SourceTriangleIndex = sourceTriangleIndex;
            SourceTriangleIndexBufferOffset = sourceTriangleIndexBufferOffset;
            SourceVertexIndexA = sourceVertexIndexA;
            SourceVertexIndexB = sourceVertexIndexB;
            SourceVertexIndexC = sourceVertexIndexC;
        }
    }

    private sealed class BoundaryNode
    {
        public Vector3 Position { get; }
        public int ConnectivityVertexId { get; }
        public List<int> Neighbors { get; }
        public int SourceOriginCount =>
            sourceOriginVertexIndexA.Count;

        private readonly List<int> sourceOriginVertexIndexA;
        private readonly List<int> sourceOriginVertexIndexB;

        public BoundaryNode(
            Vector3 position,
            int sourceEdgeVertexIndexA,
            int sourceEdgeVertexIndexB,
            int connectivityVertexId)
        {
            Position = position;
            ConnectivityVertexId = connectivityVertexId;

            Neighbors =
                new List<int>(2);

            sourceOriginVertexIndexA =
                new List<int>(2);

            sourceOriginVertexIndexB =
                new List<int>(2);

            AddSourceOrigin(
                sourceEdgeVertexIndexA,
                sourceEdgeVertexIndexB);
        }

        public bool HasEquivalentSourceOrigin(
            int sourceEdgeVertexIndexA,
            int sourceEdgeVertexIndexB,
            IReadOnlyList<Vector3> sourceVertices,
            float sourceSplitVertexMatchEpsilon)
        {
            for (int i = 0;
                 i < sourceOriginVertexIndexA.Count;
                 i++)
            {
                if (AreSourceOriginsEquivalent(
                        sourceOriginVertexIndexA[i],
                        sourceOriginVertexIndexB[i],
                        sourceEdgeVertexIndexA,
                        sourceEdgeVertexIndexB,
                        sourceVertices,
                        sourceSplitVertexMatchEpsilon))
                {
                    return true;
                }
            }

            return false;
        }

        public void GetSourceOrigin(
            int index,
            out int sourceEdgeVertexIndexA,
            out int sourceEdgeVertexIndexB)
        {
            sourceEdgeVertexIndexA =
                sourceOriginVertexIndexA[index];

            sourceEdgeVertexIndexB =
                sourceOriginVertexIndexB[index];
        }

        public void AddSourceOrigin(
            int sourceEdgeVertexIndexA,
            int sourceEdgeVertexIndexB)
        {
            for (int i = 0;
                 i < sourceOriginVertexIndexA.Count;
                 i++)
            {
                if (sourceOriginVertexIndexA[i] == sourceEdgeVertexIndexA &&
                    sourceOriginVertexIndexB[i] == sourceEdgeVertexIndexB)
                {
                    return;
                }
            }

            sourceOriginVertexIndexA.Add(
                sourceEdgeVertexIndexA);

            sourceOriginVertexIndexB.Add(
                sourceEdgeVertexIndexB);
        }
    }

    private sealed class PreparedBoundaryLoop
    {
        public List<Vector3> Positions { get; }
        public List<Vector2> ProjectedPoints { get; }
        public List<int> ConnectivityVertexIds { get; }
        public int ParentIndex { get; set; }
        public int NestingDepth { get; set; }

        public PreparedBoundaryLoop(
            List<Vector3> positions,
            List<Vector2> projectedPoints,
            List<int> connectivityVertexIds)
        {
            Positions =
                positions;

            ProjectedPoints =
                projectedPoints;

            ConnectivityVertexIds =
                connectivityVertexIds;

            ParentIndex = -1;
            NestingDepth = 0;
        }
    }

    private static int CalculateInitialPieceVertexCapacity(
        int sourceVertexCount)
    {
        // Source Vertexは基本的に切断Planeのどちらか一方へ分かれるため、
        // 両BuilderへSource Mesh全頂点数を予約せず、半数を初期推定値とする。
        // 偏った切断や交点Vertex追加で超過した場合はList / Dictionaryの通常拡張へ任せる。
        return sourceVertexCount / 2 +
               sourceVertexCount % 2;
    }

    private static int CalculateInitialPieceTriangleIndexCapacity(
        int sourceIndexCount)
    {
        int sourceTriangleCount =
            sourceIndexCount / 3;

        int estimatedTriangleCount =
            sourceTriangleCount / 2 +
            sourceTriangleCount % 2;

        return estimatedTriangleCount * 3;
    }

    private sealed class PieceMeshBuilder
    {
        private readonly List<Vector3> vertices;

        private readonly List<Vector3> normals;

        private readonly List<Vector2> uv;

        private readonly List<Vector4> tangents;

        private readonly List<int> connectivityVertexIds;

        private readonly List<List<int>>
            trianglesBySubMesh;

        // 元Mesh由来の頂点だけを再利用する。位置一致では統合せず、
        // UV seam / hard edgeとして分離された別Source Vertex Indexは維持する。
        private readonly Dictionary<int, int>
            outputVertexIndexBySourceVertexIndex;

        private readonly bool hasTangents;

        private bool hasBounds;
        private Vector3 minimumPosition;
        private Vector3 maximumPosition;

        public int OriginalTriangleCount
        {
            get;
            private set;
        }

        public IReadOnlyList<Vector3> Vertices => vertices;
        public IReadOnlyList<Vector3> Normals => normals;
        public IReadOnlyList<Vector2> Uv => uv;
        public IReadOnlyList<Vector4> Tangents => tangents;
        public IReadOnlyList<int> ConnectivityVertexIds => connectivityVertexIds;
        public IReadOnlyList<List<int>> TrianglesBySubMesh => trianglesBySubMesh;
        public bool HasTangents => hasTangents;
        public Bounds BuilderBounds =>
            hasBounds
                ? new Bounds(
                    (minimumPosition + maximumPosition) * 0.5f,
                    maximumPosition - minimumPosition)
                : default;

        public PieceMeshBuilder(
            int subMeshCount,
            bool hasTangents,
            int estimatedVertexCapacity)
        {
            this.hasTangents =
                hasTangents;

            vertices =
                new List<Vector3>(
                    estimatedVertexCapacity);

            normals =
                new List<Vector3>(
                    estimatedVertexCapacity);

            uv =
                new List<Vector2>(
                    estimatedVertexCapacity);

            tangents =
                new List<Vector4>(
                    hasTangents
                        ? estimatedVertexCapacity
                        : 0);

            connectivityVertexIds =
                new List<int>(
                    estimatedVertexCapacity);

            outputVertexIndexBySourceVertexIndex =
                new Dictionary<int, int>(
                    estimatedVertexCapacity);

            trianglesBySubMesh =
                new List<List<int>>(
                    subMeshCount);

            for (int i = 0;
                 i < subMeshCount;
                 i++)
            {
                trianglesBySubMesh.Add(
                    new List<int>());
            }
        }

        public void SetInitialSubMeshIndexCapacity(
            int subMeshIndex,
            int estimatedIndexCapacity)
        {
            List<int> triangles =
                trianglesBySubMesh[subMeshIndex];

            // このメソッドは対象SubMeshへTriangleを追加する前にだけ呼ぶ。
            // Capacityは上限ではなく初期予約値なので、予測超過時はListの通常拡張を許容する。
            if (triangles.Count == 0 &&
                triangles.Capacity < estimatedIndexCapacity)
            {
                triangles.Capacity =
                    estimatedIndexCapacity;
            }
        }

        public void AddConvexPolygon(
            IReadOnlyList<VertexData> polygon,
            int subMeshIndex,
            bool isCap)
        {
            if (polygon == null ||
                polygon.Count < 3)
            {
                return;
            }

            for (int i = 1;
                 i < polygon.Count - 1;
                 i++)
            {
                AddTriangle(
                    polygon[0],
                    polygon[i],
                    polygon[i + 1],
                    subMeshIndex,
                    isCap);
            }
        }

        public void AddTriangle(
            VertexData first,
            VertexData second,
            VertexData third,
            int subMeshIndex,
            bool isCap)
        {
            int firstIndex;
            int secondIndex;
            int thirdIndex;

            using (MeshPlaneSplitter.builderResolveVerticesProfilerMarker.Auto())
            {
                firstIndex =
                    AddVertex(first);

                secondIndex =
                    AddVertex(second);

                thirdIndex =
                    AddVertex(third);
            }

            using (MeshPlaneSplitter.builderAppendTriangleIndicesProfilerMarker.Auto())
            {
                trianglesBySubMesh[
                    subMeshIndex].Add(
                        firstIndex);

                trianglesBySubMesh[
                    subMeshIndex].Add(
                        secondIndex);

                trianglesBySubMesh[
                    subMeshIndex].Add(
                        thirdIndex);
            }

            if (!isCap)
            {
                OriginalTriangleCount++;
            }
        }

        public Mesh BuildMesh(
            string meshName)
        {
            using (MeshPlaneSplitter.buildFinalMeshProfilerMarker.Auto())
            {
            if (vertices.Count == 0)
            {
                return null;
            }

            var mesh =
                new Mesh
                {
                    name = meshName,
                    indexFormat =
                        vertices.Count >
                        ushort.MaxValue
                            ? IndexFormat.UInt32
                            : IndexFormat.UInt16
                };

            mesh.SetVertices(
                vertices);

            mesh.SetNormals(
                normals);

            mesh.SetUVs(
                0,
                uv);

            if (hasTangents)
            {
                mesh.SetTangents(
                    tangents);
            }

            mesh.subMeshCount =
                trianglesBySubMesh.Count;

            for (int subMeshIndex = 0;
                 subMeshIndex <
                 trianglesBySubMesh.Count;
                 subMeshIndex++)
            {
                mesh.SetTriangles(
                    trianglesBySubMesh[
                        subMeshIndex],
                    subMeshIndex,
                    false);
            }

            mesh.bounds = BuilderBounds;

            return mesh;
        
            }
        }

        private int AddVertex(
            VertexData vertex)
        {
            if (vertex.HasSourceVertexIndex &&
                outputVertexIndexBySourceVertexIndex.TryGetValue(
                    vertex.SourceVertexIndex,
                    out int existingIndex))
            {
                return existingIndex;
            }

            int index =
                vertices.Count;

            vertices.Add(
                vertex.Position);

            normals.Add(
                vertex.Normal);

            uv.Add(
                vertex.Uv);

            if (hasTangents)
            {
                tangents.Add(
                    vertex.Tangent);
            }

            connectivityVertexIds.Add(
                vertex.ConnectivityVertexId);

            UpdateBounds(vertex.Position);

            if (vertex.HasSourceVertexIndex)
            {
                outputVertexIndexBySourceVertexIndex.Add(
                    vertex.SourceVertexIndex,
                    index);
            }

            return index;
        }

        private void UpdateBounds(Vector3 position)
        {
            if (!hasBounds)
            {
                minimumPosition = position;
                maximumPosition = position;
                hasBounds = true;
                return;
            }

            minimumPosition =
                Vector3.Min(minimumPosition, position);

            maximumPosition =
                Vector3.Max(maximumPosition, position);
        }
    }
}
