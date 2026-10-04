using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// チェンソー軌跡から切断Planeを求め、Bake済みMeshを2つへ分割する。
/// </summary>
/// <remarks>
/// GameObject生成、Renderer操作、HP、死亡判定、攻撃受信は担当しない。
/// 入力するTip / Root位置は、切断対象Meshと同じローカル空間へ変換済みであること。
/// </remarks>
public sealed class ChainsawMeshCutProcessor
{
    private const float GEOMETRY_EPSILON_RATIO = 0.00001f;
    private const float MIN_GEOMETRY_EPSILON = 0.000001f;
    private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.00000001f;

    public bool HasDebugData { get; private set; }
    public Plane LastOriginalCutPlane { get; private set; }
    public Plane LastCorrectedCutPlane { get; private set; }
    public float LastSafeMinimumProjection { get; private set; }
    public float LastSafeMaximumProjection { get; private set; }
    public Bounds LastSourceBounds { get; private set; }

    /// <summary>
    /// Bake済みMeshをチェンソー軌跡由来のPlaneで2つへ分割する。
    /// </summary>
    public bool TryCut(
        Mesh sourceMesh,
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        int cutDirectionSampleCount,
        float safeCutEdgeMarginNormalized,
        float cutSurfaceUvScale,
        out MeshCutResult result,
        out string failureReason)
    {
        result = null;
        failureReason = null;
        ResetDebugData();

        if (!ValidateInputs(
                sourceMesh,
                localTipPositions,
                localRootPositions,
                cutDirectionSampleCount,
                safeCutEdgeMarginNormalized,
                cutSurfaceUvScale,
                out failureReason))
        {
            return false;
        }

        float geometryEpsilon = CalculateGeometryEpsilon(sourceMesh.bounds);

        if (!TryCreateCutPlanes(
                sourceMesh.bounds,
                localTipPositions,
                localRootPositions,
                cutDirectionSampleCount,
                safeCutEdgeMarginNormalized,
                geometryEpsilon,
                out Plane correctedCutPlane,
                out failureReason))
        {
            return false;
        }

        if (!TrySplitMesh(
                sourceMesh,
                correctedCutPlane,
                cutSurfaceUvScale,
                geometryEpsilon,
                out Mesh firstPieceMesh,
                out Mesh secondPieceMesh,
                out failureReason))
        {
            return false;
        }

        result = new MeshCutResult(firstPieceMesh, secondPieceMesh);
        return true;
    }

    private void ResetDebugData()
    {
        HasDebugData = false;
        LastOriginalCutPlane = default;
        LastCorrectedCutPlane = default;
        LastSafeMinimumProjection = 0.0f;
        LastSafeMaximumProjection = 0.0f;
        LastSourceBounds = default;
    }

    private static bool ValidateInputs(
        Mesh sourceMesh,
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        int cutDirectionSampleCount,
        float safeCutEdgeMarginNormalized,
        float cutSurfaceUvScale,
        out string failureReason)
    {
        if (sourceMesh == null)
        {
            failureReason = "切断対象Meshがnullです。";
            return false;
        }

        if (sourceMesh.vertexCount == 0)
        {
            failureReason = "切断対象Meshに頂点がありません。";
            return false;
        }

        if (sourceMesh.subMeshCount == 0)
        {
            failureReason = "切断対象MeshにSubMeshがありません。";
            return false;
        }

        if (localTipPositions == null || localRootPositions == null)
        {
            failureReason = "ローカル変換済みのTip / Root軌跡がnullです。";
            return false;
        }

        if (localTipPositions.Count != localRootPositions.Count)
        {
            failureReason = "TipとRootの軌跡サンプル数が一致していません。";
            return false;
        }

        if (localTipPositions.Count < 2)
        {
            failureReason = "切断Plane算出には2サンプル以上の軌跡が必要です。";
            return false;
        }

        if (cutDirectionSampleCount < 2)
        {
            failureReason = "cutDirectionSampleCountは2以上である必要があります。";
            return false;
        }

        if (safeCutEdgeMarginNormalized < 0.0f ||
            safeCutEdgeMarginNormalized > 0.49f)
        {
            failureReason =
                "safeCutEdgeMarginNormalizedは0～0.49の範囲である必要があります。";
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

    private static float CalculateGeometryEpsilon(Bounds bounds)
    {
        return Mathf.Max(
            bounds.size.magnitude * GEOMETRY_EPSILON_RATIO,
            MIN_GEOMETRY_EPSILON);
    }

    private bool TryCreateCutPlanes(
        Bounds sourceBounds,
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        int cutDirectionSampleCount,
        float safeCutEdgeMarginNormalized,
        float geometryEpsilon,
        out Plane correctedCutPlane,
        out string failureReason)
    {
        int sampleCount = localTipPositions.Count;
        int usedSampleCount = Mathf.Min(sampleCount, cutDirectionSampleCount);
        int startIndex = sampleCount - usedSampleCount;

        Vector3 bladeDirectionSum = Vector3.zero;
        int validBladeDirectionCount = 0;

        for (int i = startIndex; i < sampleCount; i++)
        {
            Vector3 bladeVector =
                localTipPositions[i] - localRootPositions[i];

            if (bladeVector.sqrMagnitude <= MIN_DIRECTION_SQR_MAGNITUDE)
            {
                continue;
            }

            bladeDirectionSum += bladeVector.normalized;
            validBladeDirectionCount++;
        }

        if (validBladeDirectionCount == 0 ||
            bladeDirectionSum.sqrMagnitude <= MIN_DIRECTION_SQR_MAGNITUDE)
        {
            correctedCutPlane = default;
            failureReason = "有効な代表刃方向を算出できませんでした。";
            return false;
        }

        Vector3 representativeBladeDirection =
            bladeDirectionSum.normalized;

        Vector3 movementSum = Vector3.zero;

        Vector3 previousCenter = GetBladeCenter(
            localTipPositions[startIndex],
            localRootPositions[startIndex]);

        for (int i = startIndex + 1; i < sampleCount; i++)
        {
            Vector3 currentCenter = GetBladeCenter(
                localTipPositions[i],
                localRootPositions[i]);

            movementSum += currentCenter - previousCenter;
            previousCenter = currentCenter;
        }

        if (movementSum.sqrMagnitude <= geometryEpsilon * geometryEpsilon)
        {
            correctedCutPlane = default;
            failureReason = "命中直前の軌跡から有効なチェンソー移動方向を算出できませんでした。";
            return false;
        }

        Vector3 representativeMovementDirection = movementSum.normalized;
        Vector3 planeNormal = Vector3.Cross(
            representativeBladeDirection,
            representativeMovementDirection);

        if (planeNormal.sqrMagnitude <= MIN_DIRECTION_SQR_MAGNITUDE)
        {
            correctedCutPlane = default;
            failureReason =
                "刃方向と移動方向がほぼ平行なため、有効な切断Planeを算出できませんでした。";
            return false;
        }

        planeNormal.Normalize();

        Vector3 originalPlanePoint = GetBladeCenter(
            localTipPositions[sampleCount - 1],
            localRootPositions[sampleCount - 1]);

        CalculateBoundsProjectionRange(
            sourceBounds,
            planeNormal,
            out float minimumProjection,
            out float maximumProjection);

        float thickness = maximumProjection - minimumProjection;

        if (thickness <= geometryEpsilon)
        {
            correctedCutPlane = default;
            failureReason = "切断Plane Normal方向のMesh厚みが小さすぎます。";
            return false;
        }

        float safeMinimumProjection =
            minimumProjection + thickness * safeCutEdgeMarginNormalized;
        float safeMaximumProjection =
            maximumProjection - thickness * safeCutEdgeMarginNormalized;

        if (safeMinimumProjection >= safeMaximumProjection)
        {
            correctedCutPlane = default;
            failureReason = "安全切断範囲を確保できませんでした。";
            return false;
        }

        float originalProjection = Vector3.Dot(
            originalPlanePoint,
            planeNormal);

        float correctedProjection = Mathf.Clamp(
            originalProjection,
            safeMinimumProjection,
            safeMaximumProjection);

        float correctionDistance = correctedProjection - originalProjection;
        Vector3 correctedPlanePoint =
            originalPlanePoint + planeNormal * correctionDistance;

        Plane originalCutPlane = new Plane(
            planeNormal,
            originalPlanePoint);
        correctedCutPlane = new Plane(
            planeNormal,
            correctedPlanePoint);

        HasDebugData = true;
        LastOriginalCutPlane = originalCutPlane;
        LastCorrectedCutPlane = correctedCutPlane;
        LastSafeMinimumProjection = safeMinimumProjection;
        LastSafeMaximumProjection = safeMaximumProjection;
        LastSourceBounds = sourceBounds;

        failureReason = null;
        return true;
    }

    private static Vector3 GetBladeCenter(
        Vector3 tipPosition,
        Vector3 rootPosition)
    {
        return (tipPosition + rootPosition) * 0.5f;
    }

    private static void CalculateBoundsProjectionRange(
        Bounds bounds,
        Vector3 normalizedDirection,
        out float minimumProjection,
        out float maximumProjection)
    {
        Vector3[] corners = GetBoundsCorners(bounds);

        minimumProjection = float.PositiveInfinity;
        maximumProjection = float.NegativeInfinity;

        for (int i = 0; i < corners.Length; i++)
        {
            float projection = Vector3.Dot(
                corners[i],
                normalizedDirection);

            minimumProjection = Mathf.Min(minimumProjection, projection);
            maximumProjection = Mathf.Max(maximumProjection, projection);
        }
    }

    private static Vector3[] GetBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, max.y, max.z)
        };
    }

    private static bool TrySplitMesh(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        float geometryEpsilon,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out string failureReason)
    {
        firstPieceMesh = null;
        secondPieceMesh = null;

        Vector3[] sourceVertices = sourceMesh.vertices;
        Vector3[] sourceNormals = sourceMesh.normals;
        Vector2[] sourceUv = sourceMesh.uv;
        Vector4[] sourceTangents = sourceMesh.tangents;

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

        var firstBuilder = new PieceMeshBuilder(
            originalSubMeshCount + 1,
            hasTangents);
        var secondBuilder = new PieceMeshBuilder(
            originalSubMeshCount + 1,
            hasTangents);
        var cutSegments = new List<CutSegment>();

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

            int[] triangles = sourceMesh.GetTriangles(subMeshIndex);
            if (triangles.Length % 3 != 0)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} のTriangle index数が不正です。";
                return false;
            }

            for (int triangleIndex = 0;
                 triangleIndex < triangles.Length;
                 triangleIndex += 3)
            {
                int indexA = triangles[triangleIndex];
                int indexB = triangles[triangleIndex + 1];
                int indexC = triangles[triangleIndex + 2];

                if (!IsValidVertexIndex(indexA, sourceVertices.Length) ||
                    !IsValidVertexIndex(indexB, sourceVertices.Length) ||
                    !IsValidVertexIndex(indexC, sourceVertices.Length))
                {
                    failureReason =
                        $"SubMesh {subMeshIndex} に範囲外の頂点indexがあります。";
                    return false;
                }

                VertexData[] triangle =
                {
                    CreateVertexData(
                        indexA,
                        sourceVertices,
                        sourceNormals,
                        sourceUv,
                        sourceTangents,
                        hasUv,
                        hasTangents),
                    CreateVertexData(
                        indexB,
                        sourceVertices,
                        sourceNormals,
                        sourceUv,
                        sourceTangents,
                        hasUv,
                        hasTangents),
                    CreateVertexData(
                        indexC,
                        sourceVertices,
                        sourceNormals,
                        sourceUv,
                        sourceTangents,
                        hasUv,
                        hasTangents)
                };

                float distanceA = cutPlane.GetDistanceToPoint(
                    triangle[0].Position);
                float distanceB = cutPlane.GetDistanceToPoint(
                    triangle[1].Position);
                float distanceC = cutPlane.GetDistanceToPoint(
                    triangle[2].Position);

                float[] distances =
                {
                    distanceA,
                    distanceB,
                    distanceC
                };

                bool hasPositiveVertex =
                    distanceA > geometryEpsilon ||
                    distanceB > geometryEpsilon ||
                    distanceC > geometryEpsilon;
                bool hasNegativeVertex =
                    distanceA < -geometryEpsilon ||
                    distanceB < -geometryEpsilon ||
                    distanceC < -geometryEpsilon;

                if (!hasNegativeVertex)
                {
                    firstBuilder.AddTriangle(
                        triangle[0],
                        triangle[1],
                        triangle[2],
                        subMeshIndex,
                        false);
                    continue;
                }

                if (!hasPositiveVertex)
                {
                    secondBuilder.AddTriangle(
                        triangle[0],
                        triangle[1],
                        triangle[2],
                        subMeshIndex,
                        false);
                    continue;
                }

                List<VertexData> positivePolygon = ClipTriangle(
                    triangle,
                    distances,
                    true,
                    geometryEpsilon);
                List<VertexData> negativePolygon = ClipTriangle(
                    triangle,
                    distances,
                    false,
                    geometryEpsilon);

                firstBuilder.AddConvexPolygon(
                    positivePolygon,
                    subMeshIndex,
                    false);
                secondBuilder.AddConvexPolygon(
                    negativePolygon,
                    subMeshIndex,
                    false);

                if (!TryGetCutSegment(
                        triangle,
                        distances,
                        geometryEpsilon,
                        out CutSegment cutSegment))
                {
                    failureReason =
                        "交差Triangleから有効な切断線分を生成できませんでした。";
                    return false;
                }

                cutSegments.Add(cutSegment);
            }
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

        if (!TryBuildBoundaryLoops(
                cutSegments,
                geometryEpsilon,
                out List<List<Vector3>> boundaryLoops,
                out failureReason))
        {
            return false;
        }

        if (!TryAddCutSurfaceCaps(
                boundaryLoops,
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

        firstPieceMesh = firstBuilder.BuildMesh("EnemyCut_FirstPiece");
        secondPieceMesh = secondBuilder.BuildMesh("EnemyCut_SecondPiece");

        if (firstPieceMesh == null || secondPieceMesh == null)
        {
            failureReason = "有効な2つの分割Meshを生成できませんでした。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool IsValidVertexIndex(int index, int vertexCount)
    {
        return index >= 0 && index < vertexCount;
    }

    private static VertexData CreateVertexData(
        int index,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<Vector3> normals,
        IReadOnlyList<Vector2> uv,
        IReadOnlyList<Vector4> tangents,
        bool hasUv,
        bool hasTangents)
    {
        return new VertexData(
            vertices[index],
            normals[index],
            hasUv ? uv[index] : Vector2.zero,
            hasTangents ? tangents[index] : Vector4.zero);
    }

    private static List<VertexData> ClipTriangle(
        IReadOnlyList<VertexData> triangle,
        IReadOnlyList<float> distances,
        bool keepPositiveSide,
        float geometryEpsilon)
    {
        var output = new List<VertexData>(4);

        VertexData previousVertex = triangle[triangle.Count - 1];
        float previousDistance = distances[distances.Count - 1];
        bool previousInside = IsInsideHalfSpace(
            previousDistance,
            keepPositiveSide,
            geometryEpsilon);

        for (int i = 0; i < triangle.Count; i++)
        {
            VertexData currentVertex = triangle[i];
            float currentDistance = distances[i];
            bool currentInside = IsInsideHalfSpace(
                currentDistance,
                keepPositiveSide,
                geometryEpsilon);

            if (currentInside)
            {
                if (!previousInside)
                {
                    VertexData intersection = InterpolateToPlane(
                        previousVertex,
                        currentVertex,
                        previousDistance,
                        currentDistance);
                    AddDistinctPolygonVertex(
                        output,
                        intersection,
                        geometryEpsilon);
                }

                AddDistinctPolygonVertex(
                    output,
                    currentVertex,
                    geometryEpsilon);
            }
            else if (previousInside)
            {
                VertexData intersection = InterpolateToPlane(
                    previousVertex,
                    currentVertex,
                    previousDistance,
                    currentDistance);
                AddDistinctPolygonVertex(
                    output,
                    intersection,
                    geometryEpsilon);
            }

            previousVertex = currentVertex;
            previousDistance = currentDistance;
            previousInside = currentInside;
        }

        if (output.Count >= 2 &&
            ArePositionsClose(
                output[0].Position,
                output[output.Count - 1].Position,
                geometryEpsilon))
        {
            output.RemoveAt(output.Count - 1);
        }

        return output;
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
        float toDistance)
    {
        float denominator = fromDistance - toDistance;
        float interpolation = Mathf.Abs(denominator) <= Mathf.Epsilon
            ? 0.5f
            : fromDistance / denominator;

        interpolation = Mathf.Clamp01(interpolation);
        return VertexData.Lerp(from, to, interpolation);
    }

    private static bool TryGetCutSegment(
        IReadOnlyList<VertexData> triangle,
        IReadOnlyList<float> distances,
        float geometryEpsilon,
        out CutSegment cutSegment)
    {
        var points = new List<Vector3>(3);

        for (int i = 0; i < triangle.Count; i++)
        {
            int nextIndex = (i + 1) % triangle.Count;
            float currentDistance = distances[i];
            float nextDistance = distances[nextIndex];

            if (Mathf.Abs(currentDistance) <= geometryEpsilon)
            {
                AddDistinctPoint(
                    points,
                    triangle[i].Position,
                    geometryEpsilon);
            }

            bool crossesPlane =
                (currentDistance > geometryEpsilon &&
                 nextDistance < -geometryEpsilon) ||
                (currentDistance < -geometryEpsilon &&
                 nextDistance > geometryEpsilon);

            if (!crossesPlane)
            {
                continue;
            }

            VertexData intersection = InterpolateToPlane(
                triangle[i],
                triangle[nextIndex],
                currentDistance,
                nextDistance);

            AddDistinctPoint(
                points,
                intersection.Position,
                geometryEpsilon);
        }

        if (points.Count < 2)
        {
            cutSegment = default;
            return false;
        }

        if (points.Count == 2)
        {
            if (ArePositionsClose(
                    points[0],
                    points[1],
                    geometryEpsilon))
            {
                cutSegment = default;
                return false;
            }

            cutSegment = new CutSegment(points[0], points[1]);
            return true;
        }

        float greatestSqrDistance = 0.0f;
        Vector3 firstPoint = default;
        Vector3 secondPoint = default;

        for (int firstIndex = 0;
             firstIndex < points.Count - 1;
             firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < points.Count;
                 secondIndex++)
            {
                float sqrDistance =
                    (points[firstIndex] - points[secondIndex]).sqrMagnitude;

                if (sqrDistance <= greatestSqrDistance)
                {
                    continue;
                }

                greatestSqrDistance = sqrDistance;
                firstPoint = points[firstIndex];
                secondPoint = points[secondIndex];
            }
        }

        if (greatestSqrDistance <= geometryEpsilon * geometryEpsilon)
        {
            cutSegment = default;
            return false;
        }

        cutSegment = new CutSegment(firstPoint, secondPoint);
        return true;
    }

    private static void AddDistinctPoint(
        List<Vector3> points,
        Vector3 point,
        float geometryEpsilon)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (ArePositionsClose(points[i], point, geometryEpsilon))
            {
                return;
            }
        }

        points.Add(point);
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
        float geometryEpsilon,
        out List<List<Vector3>> boundaryLoops,
        out string failureReason)
    {
        var nodes = new List<BoundaryNode>();
        var uniqueEdges = new HashSet<long>();

        for (int i = 0; i < cutSegments.Count; i++)
        {
            int firstNodeIndex = FindOrCreateBoundaryNode(
                nodes,
                cutSegments[i].First,
                geometryEpsilon);
            int secondNodeIndex = FindOrCreateBoundaryNode(
                nodes,
                cutSegments[i].Second,
                geometryEpsilon);

            if (firstNodeIndex == secondNodeIndex)
            {
                continue;
            }

            long edgeKey = CreateEdgeKey(
                firstNodeIndex,
                secondNodeIndex);

            if (!uniqueEdges.Add(edgeKey))
            {
                continue;
            }

            nodes[firstNodeIndex].Neighbors.Add(secondNodeIndex);
            nodes[secondNodeIndex].Neighbors.Add(firstNodeIndex);
        }

        if (uniqueEdges.Count < 3)
        {
            boundaryLoops = null;
            failureReason = "切断境界を構成する線分が不足しています。";
            return false;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Neighbors.Count != 2)
            {
                boundaryLoops = null;
                failureReason =
                    "切断境界が閉じた単純ループになっていません。";
                return false;
            }
        }

        boundaryLoops = new List<List<Vector3>>();
        var visitedEdges = new HashSet<long>();

        for (int nodeIndex = 0;
             nodeIndex < nodes.Count;
             nodeIndex++)
        {
            for (int neighborIndex = 0;
                 neighborIndex < nodes[nodeIndex].Neighbors.Count;
                 neighborIndex++)
            {
                int neighbor = nodes[nodeIndex].Neighbors[neighborIndex];
                long edgeKey = CreateEdgeKey(nodeIndex, neighbor);

                if (visitedEdges.Contains(edgeKey))
                {
                    continue;
                }

                if (!TryTraceBoundaryLoop(
                        nodes,
                        nodeIndex,
                        neighbor,
                        visitedEdges,
                        out List<Vector3> loop))
                {
                    boundaryLoops = null;
                    failureReason = "切断境界ループの追跡に失敗しました。";
                    return false;
                }

                if (loop.Count < 3)
                {
                    boundaryLoops = null;
                    failureReason = "切断境界ループの頂点数が不足しています。";
                    return false;
                }

                boundaryLoops.Add(loop);
            }
        }

        if (boundaryLoops.Count == 0 ||
            visitedEdges.Count != uniqueEdges.Count)
        {
            boundaryLoops = null;
            failureReason = "すべての切断境界を閉じたループへ構築できませんでした。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static int FindOrCreateBoundaryNode(
        List<BoundaryNode> nodes,
        Vector3 position,
        float geometryEpsilon)
    {
        float sqrEpsilon = geometryEpsilon * geometryEpsilon;

        for (int i = 0; i < nodes.Count; i++)
        {
            if ((nodes[i].Position - position).sqrMagnitude <= sqrEpsilon)
            {
                return i;
            }
        }

        nodes.Add(new BoundaryNode(position));
        return nodes.Count - 1;
    }

    private static bool TryTraceBoundaryLoop(
        IReadOnlyList<BoundaryNode> nodes,
        int startNode,
        int firstNextNode,
        HashSet<long> visitedEdges,
        out List<Vector3> loop)
    {
        loop = new List<Vector3>();

        int previousNode = -1;
        int currentNode = startNode;
        int nextNode = firstNextNode;
        int guard = 0;
        int maximumSteps = nodes.Count + 1;

        while (true)
        {
            loop.Add(nodes[currentNode].Position);
            visitedEdges.Add(CreateEdgeKey(currentNode, nextNode));

            previousNode = currentNode;
            currentNode = nextNode;

            if (currentNode == startNode)
            {
                return true;
            }

            if (nodes[currentNode].Neighbors.Count != 2)
            {
                loop = null;
                return false;
            }

            int firstNeighbor = nodes[currentNode].Neighbors[0];
            int secondNeighbor = nodes[currentNode].Neighbors[1];
            nextNode = firstNeighbor == previousNode
                ? secondNeighbor
                : firstNeighbor;

            guard++;
            if (guard > maximumSteps)
            {
                loop = null;
                return false;
            }
        }
    }

    private static long CreateEdgeKey(int firstNode, int secondNode)
    {
        int minimum = Mathf.Min(firstNode, secondNode);
        int maximum = Mathf.Max(firstNode, secondNode);
        return ((long)minimum << 32) | (uint)maximum;
    }

    private static bool TryAddCutSurfaceCaps(
        IReadOnlyList<List<Vector3>> boundaryLoops,
        Plane cutPlane,
        float cutSurfaceUvScale,
        float geometryEpsilon,
        int cutSurfaceSubMeshIndex,
        PieceMeshBuilder firstBuilder,
        PieceMeshBuilder secondBuilder,
        out string failureReason)
    {
        CreatePlaneBasis(
            cutPlane.normal,
            out Vector3 planeAxisU,
            out Vector3 planeAxisV);

        var preparedLoops = new List<PreparedBoundaryLoop>(
            boundaryLoops.Count);

        for (int i = 0; i < boundaryLoops.Count; i++)
        {
            if (!TryPrepareBoundaryLoop(
                    boundaryLoops[i],
                    planeAxisU,
                    planeAxisV,
                    geometryEpsilon,
                    out PreparedBoundaryLoop preparedLoop))
            {
                failureReason =
                    "切断境界を断面三角形分割用の単純Polygonへ変換できませんでした。";
                return false;
            }

            preparedLoops.Add(preparedLoop);
        }

        if (ContainsNestedLoops(preparedLoops))
        {
            failureReason =
                "polygon-with-holesとなる切断境界は初版では対応しません。";
            return false;
        }

        float earEpsilon = geometryEpsilon * geometryEpsilon;

        for (int loopIndex = 0;
             loopIndex < preparedLoops.Count;
             loopIndex++)
        {
            PreparedBoundaryLoop loop = preparedLoops[loopIndex];

            if (!TryTriangulateEarClipping(
                    loop.ProjectedPoints,
                    earEpsilon,
                    out List<int> triangleIndices))
            {
                failureReason = "Ear Clippingによる切断面生成に失敗しました。";
                return false;
            }

            for (int triangleIndex = 0;
                 triangleIndex < triangleIndices.Count;
                 triangleIndex += 3)
            {
                Vector3 pointA = loop.Positions[
                    triangleIndices[triangleIndex]];
                Vector3 pointB = loop.Positions[
                    triangleIndices[triangleIndex + 1]];
                Vector3 pointC = loop.Positions[
                    triangleIndices[triangleIndex + 2]];

                AddCapTriangle(
                    firstBuilder,
                    cutSurfaceSubMeshIndex,
                    pointA,
                    pointB,
                    pointC,
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
                    cutPlane.normal,
                    planeAxisU,
                    planeAxisV,
                    cutSurfaceUvScale);
            }
        }

        failureReason = null;
        return true;
    }

    private static void CreatePlaneBasis(
        Vector3 planeNormal,
        out Vector3 planeAxisU,
        out Vector3 planeAxisV)
    {
        Vector3 referenceAxis =
            Mathf.Abs(Vector3.Dot(planeNormal, Vector3.up)) < 0.95f
                ? Vector3.up
                : Vector3.right;

        planeAxisU = Vector3.Cross(referenceAxis, planeNormal).normalized;

        if (planeAxisU.sqrMagnitude <= MIN_DIRECTION_SQR_MAGNITUDE)
        {
            referenceAxis = Vector3.forward;
            planeAxisU =
                Vector3.Cross(referenceAxis, planeNormal).normalized;
        }

        planeAxisV = Vector3.Cross(planeNormal, planeAxisU).normalized;
    }

    private static bool TryPrepareBoundaryLoop(
        IReadOnlyList<Vector3> sourcePositions,
        Vector3 planeAxisU,
        Vector3 planeAxisV,
        float geometryEpsilon,
        out PreparedBoundaryLoop preparedLoop)
    {
        var positions = new List<Vector3>(sourcePositions.Count);
        var projectedPoints = new List<Vector2>(sourcePositions.Count);

        for (int i = 0; i < sourcePositions.Count; i++)
        {
            positions.Add(sourcePositions[i]);
            projectedPoints.Add(ProjectToPlane2D(
                sourcePositions[i],
                planeAxisU,
                planeAxisV));
        }

        RemoveRedundantLoopPoints(
            positions,
            projectedPoints,
            geometryEpsilon);

        if (positions.Count < 3)
        {
            preparedLoop = null;
            return false;
        }

        float signedArea = CalculateSignedArea(projectedPoints);
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
        }

        preparedLoop = new PreparedBoundaryLoop(
            positions,
            projectedPoints);
        return true;
    }

    private static Vector2 ProjectToPlane2D(
        Vector3 position,
        Vector3 planeAxisU,
        Vector3 planeAxisV)
    {
        return new Vector2(
            Vector3.Dot(position, planeAxisU),
            Vector3.Dot(position, planeAxisV));
    }

    private static void RemoveRedundantLoopPoints(
        List<Vector3> positions,
        List<Vector2> projectedPoints,
        float geometryEpsilon)
    {
        if (positions.Count < 3)
        {
            return;
        }

        float sqrEpsilon = geometryEpsilon * geometryEpsilon;
        bool removedPoint;
        int guard = 0;

        do
        {
            removedPoint = false;

            for (int i = 0; i < projectedPoints.Count; i++)
            {
                int previousIndex =
                    (i - 1 + projectedPoints.Count) % projectedPoints.Count;
                int nextIndex = (i + 1) % projectedPoints.Count;

                Vector2 previous = projectedPoints[previousIndex];
                Vector2 current = projectedPoints[i];
                Vector2 next = projectedPoints[nextIndex];

                if ((current - previous).sqrMagnitude <= sqrEpsilon ||
                    (next - current).sqrMagnitude <= sqrEpsilon)
                {
                    positions.RemoveAt(i);
                    projectedPoints.RemoveAt(i);
                    removedPoint = true;
                    break;
                }

                Vector2 firstDirection = current - previous;
                Vector2 secondDirection = next - current;
                float cross = Mathf.Abs(Cross2D(
                    firstDirection,
                    secondDirection));
                float scale =
                    firstDirection.magnitude + secondDirection.magnitude;

                if (cross <= geometryEpsilon * scale)
                {
                    positions.RemoveAt(i);
                    projectedPoints.RemoveAt(i);
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

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Count];
            area += current.x * next.y - next.x * current.y;
        }

        return area * 0.5f;
    }

    private static bool ContainsNestedLoops(
        IReadOnlyList<PreparedBoundaryLoop> loops)
    {
        for (int innerIndex = 0;
             innerIndex < loops.Count;
             innerIndex++)
        {
            Vector2 testPoint = loops[innerIndex].ProjectedPoints[0];

            for (int outerIndex = 0;
                 outerIndex < loops.Count;
                 outerIndex++)
            {
                if (innerIndex == outerIndex)
                {
                    continue;
                }

                if (IsPointInsidePolygon(
                        testPoint,
                        loops[outerIndex].ProjectedPoints))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPointInsidePolygon(
        Vector2 point,
        IReadOnlyList<Vector2> polygon)
    {
        bool inside = false;

        for (int currentIndex = 0, previousIndex = polygon.Count - 1;
             currentIndex < polygon.Count;
             previousIndex = currentIndex++)
        {
            Vector2 current = polygon[currentIndex];
            Vector2 previous = polygon[previousIndex];

            bool intersects =
                (current.y > point.y) != (previous.y > point.y) &&
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
        triangleIndices = new List<int>();

        if (polygon.Count < 3)
        {
            return false;
        }

        var remainingIndices = new List<int>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            remainingIndices.Add(i);
        }

        int guard = 0;
        int maximumIterations = polygon.Count * polygon.Count;

        while (remainingIndices.Count > 3)
        {
            bool foundEar = false;

            for (int i = 0; i < remainingIndices.Count; i++)
            {
                int previousListIndex =
                    (i - 1 + remainingIndices.Count) % remainingIndices.Count;
                int nextListIndex = (i + 1) % remainingIndices.Count;

                int previousIndex = remainingIndices[previousListIndex];
                int currentIndex = remainingIndices[i];
                int nextIndex = remainingIndices[nextListIndex];

                Vector2 previous = polygon[previousIndex];
                Vector2 current = polygon[currentIndex];
                Vector2 next = polygon[nextIndex];

                float cornerCross = Cross2D(
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
                    int testIndex = remainingIndices[testListIndex];

                    if (testIndex == previousIndex ||
                        testIndex == currentIndex ||
                        testIndex == nextIndex)
                    {
                        continue;
                    }

                    if (IsPointInsideTriangle(
                            polygon[testIndex],
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

                triangleIndices.Add(previousIndex);
                triangleIndices.Add(currentIndex);
                triangleIndices.Add(nextIndex);
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

        triangleIndices.Add(remainingIndices[0]);
        triangleIndices.Add(remainingIndices[1]);
        triangleIndices.Add(remainingIndices[2]);
        return true;
    }

    private static bool IsPointInsideTriangle(
        Vector2 point,
        Vector2 first,
        Vector2 second,
        Vector2 third,
        float areaEpsilon)
    {
        float firstCross = Cross2D(
            second - first,
            point - first);
        float secondCross = Cross2D(
            third - second,
            point - second);
        float thirdCross = Cross2D(
            first - third,
            point - third);

        return firstCross >= -areaEpsilon &&
               secondCross >= -areaEpsilon &&
               thirdCross >= -areaEpsilon;
    }

    private static float Cross2D(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    private static void AddCapTriangle(
        PieceMeshBuilder builder,
        int cutSurfaceSubMeshIndex,
        Vector3 pointA,
        Vector3 pointB,
        Vector3 pointC,
        Vector3 desiredNormal,
        Vector3 planeAxisU,
        Vector3 planeAxisV,
        float cutSurfaceUvScale)
    {
        Vector3 actualNormal = Vector3.Cross(
            pointB - pointA,
            pointC - pointA);

        if (Vector3.Dot(actualNormal, desiredNormal) < 0.0f)
        {
            Vector3 temporary = pointB;
            pointB = pointC;
            pointC = temporary;
        }

        Vector4 tangent = new Vector4(
            planeAxisU.x,
            planeAxisU.y,
            planeAxisU.z,
            1.0f);

        VertexData vertexA = CreateCapVertex(
            pointA,
            desiredNormal,
            tangent,
            planeAxisU,
            planeAxisV,
            cutSurfaceUvScale);
        VertexData vertexB = CreateCapVertex(
            pointB,
            desiredNormal,
            tangent,
            planeAxisU,
            planeAxisV,
            cutSurfaceUvScale);
        VertexData vertexC = CreateCapVertex(
            pointC,
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
        Vector3 normal,
        Vector4 tangent,
        Vector3 planeAxisU,
        Vector3 planeAxisV,
        float cutSurfaceUvScale)
    {
        Vector2 uv = new Vector2(
            Vector3.Dot(position, planeAxisU) * cutSurfaceUvScale,
            Vector3.Dot(position, planeAxisV) * cutSurfaceUvScale);

        return new VertexData(
            position,
            normal,
            uv,
            tangent);
    }

    private readonly struct VertexData
    {
        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Vector2 Uv { get; }
        public Vector4 Tangent { get; }

        public VertexData(
            Vector3 position,
            Vector3 normal,
            Vector2 uv,
            Vector4 tangent)
        {
            Position = position;
            Normal = normal;
            Uv = uv;
            Tangent = tangent;
        }

        public static VertexData Lerp(
            VertexData first,
            VertexData second,
            float interpolation)
        {
            Vector3 normal = Vector3.Lerp(
                first.Normal,
                second.Normal,
                interpolation);

            if (normal.sqrMagnitude > MIN_DIRECTION_SQR_MAGNITUDE)
            {
                normal.Normalize();
            }

            Vector4 tangent = Vector4.Lerp(
                first.Tangent,
                second.Tangent,
                interpolation);
            Vector3 tangentDirection = new Vector3(
                tangent.x,
                tangent.y,
                tangent.z);

            if (tangentDirection.sqrMagnitude >
                MIN_DIRECTION_SQR_MAGNITUDE)
            {
                tangentDirection.Normalize();
                tangent.x = tangentDirection.x;
                tangent.y = tangentDirection.y;
                tangent.z = tangentDirection.z;
            }

            tangent.w = tangent.w < 0.0f ? -1.0f : 1.0f;

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
                tangent);
        }
    }

    private readonly struct CutSegment
    {
        public Vector3 First { get; }
        public Vector3 Second { get; }

        public CutSegment(Vector3 first, Vector3 second)
        {
            First = first;
            Second = second;
        }
    }

    private sealed class BoundaryNode
    {
        public Vector3 Position { get; }
        public List<int> Neighbors { get; }

        public BoundaryNode(Vector3 position)
        {
            Position = position;
            Neighbors = new List<int>(2);
        }
    }

    private sealed class PreparedBoundaryLoop
    {
        public List<Vector3> Positions { get; }
        public List<Vector2> ProjectedPoints { get; }

        public PreparedBoundaryLoop(
            List<Vector3> positions,
            List<Vector2> projectedPoints)
        {
            Positions = positions;
            ProjectedPoints = projectedPoints;
        }
    }

    private sealed class PieceMeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly List<Vector4> tangents = new List<Vector4>();
        private readonly List<List<int>> trianglesBySubMesh;
        private readonly bool hasTangents;

        public int OriginalTriangleCount { get; private set; }

        public PieceMeshBuilder(
            int subMeshCount,
            bool hasTangents)
        {
            this.hasTangents = hasTangents;
            trianglesBySubMesh = new List<List<int>>(subMeshCount);

            for (int i = 0; i < subMeshCount; i++)
            {
                trianglesBySubMesh.Add(new List<int>());
            }
        }

        public void AddConvexPolygon(
            IReadOnlyList<VertexData> polygon,
            int subMeshIndex,
            bool isCap)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return;
            }

            for (int i = 1; i < polygon.Count - 1; i++)
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
            int firstIndex = AddVertex(first);
            int secondIndex = AddVertex(second);
            int thirdIndex = AddVertex(third);

            trianglesBySubMesh[subMeshIndex].Add(firstIndex);
            trianglesBySubMesh[subMeshIndex].Add(secondIndex);
            trianglesBySubMesh[subMeshIndex].Add(thirdIndex);

            if (!isCap)
            {
                OriginalTriangleCount++;
            }
        }

        public Mesh BuildMesh(string meshName)
        {
            if (vertices.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh
            {
                name = meshName,
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

            mesh.subMeshCount = trianglesBySubMesh.Count;

            for (int subMeshIndex = 0;
                 subMeshIndex < trianglesBySubMesh.Count;
                 subMeshIndex++)
            {
                mesh.SetTriangles(
                    trianglesBySubMesh[subMeshIndex],
                    subMeshIndex,
                    false);
            }

            mesh.RecalculateBounds();
            return mesh;
        }

        private int AddVertex(VertexData vertex)
        {
            int index = vertices.Count;
            vertices.Add(vertex.Position);
            normals.Add(vertex.Normal);
            uv.Add(vertex.Uv);

            if (hasTangents)
            {
                tangents.Add(vertex.Tangent);
            }

            return index;
        }
    }
}
