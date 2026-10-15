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

    /// <summary>
    /// Bake済みMeshをチェンソー軌跡由来のPlaneで2つへ分割する。
    /// </summary>
    public bool TryCut(
        Mesh sourceMesh,
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        int cutDirectionSampleCount,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        float cutSurfaceUvScale,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out string failureReason)
    {
        firstPieceMesh = null;
        secondPieceMesh = null;
        failureReason = null;

        if (!ValidateInputs(
                sourceMesh,
                localTipPositions,
                localRootPositions,
                cutDirectionSampleCount,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                cutSurfaceUvScale,
                out failureReason))
        {
            return false;
        }

        float geometryEpsilon = CalculateGeometryEpsilon(sourceMesh.bounds);

        if (!TryCreateCutPlane(
                sourceMesh,
                localTipPositions,
                localRootPositions,
                cutDirectionSampleCount,
                sourceToCutRangeSpace,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
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
                out firstPieceMesh,
                out secondPieceMesh,
                out failureReason))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateInputs(
        Mesh sourceMesh,
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        int cutDirectionSampleCount,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
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

        if (cutRangeMinimumNormalized < 0.0f ||
            cutRangeMinimumNormalized > 1.0f ||
            cutRangeMaximumNormalized < 0.0f ||
            cutRangeMaximumNormalized > 1.0f)
        {
            failureReason =
                "切断位置有効範囲は0～1の範囲である必要があります。";
            return false;
        }

        if (cutRangeMinimumNormalized >=
            cutRangeMaximumNormalized)
        {
            failureReason =
                "cutRangeMinimumNormalizedはcutRangeMaximumNormalizedより小さい必要があります。";
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

    private bool TryCreateCutPlane(
        Mesh sourceMesh,
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        int cutDirectionSampleCount,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
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

        if (!TryCreateCutPlanePoint(
                sourceMesh,
                originalPlanePoint,
                sourceToCutRangeSpace,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                out Vector3 correctedPlanePoint,
                out failureReason))
        {
            correctedCutPlane = default;
            return false;
        }

        correctedCutPlane = new Plane(
            planeNormal,
            correctedPlanePoint);

        failureReason = null;
        return true;
    }

    private static Vector3 GetBladeCenter(
        Vector3 tipPosition,
        Vector3 rootPosition)
    {
        return (tipPosition + rootPosition) * 0.5f;
    }

    private static bool TryCreateCutPlanePoint(
        Mesh sourceMesh,
        Vector3 originalPlanePoint,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        out Vector3 correctedPlanePoint,
        out string failureReason)
    {
        if (!TryCalculateMeshBoundsInSpace(
                sourceMesh,
                sourceToCutRangeSpace,
                out Bounds cutRangeSpaceBounds))
        {
            correctedPlanePoint = default;
            failureReason =
                "Cut Range判定用のMesh Boundsを算出できませんでした。";
            return false;
        }

        float boundsMinimum = GetAxisValue(
            cutRangeSpaceBounds.min,
            cutRangeAxis);

        float boundsMaximum = GetAxisValue(
            cutRangeSpaceBounds.max,
            cutRangeAxis);

        float axisSize =
            boundsMaximum - boundsMinimum;

        float cutRangeGeometryEpsilon =
            CalculateGeometryEpsilon(
                cutRangeSpaceBounds);

        if (axisSize <= cutRangeGeometryEpsilon)
        {
            correctedPlanePoint = default;
            failureReason =
                "Cut Range Axis方向のMesh Boundsサイズが小さすぎます。";
            return false;
        }

        float cutRangeMinimum = Mathf.Lerp(
            boundsMinimum,
            boundsMaximum,
            cutRangeMinimumNormalized);

        float cutRangeMaximum = Mathf.Lerp(
            boundsMinimum,
            boundsMaximum,
            cutRangeMaximumNormalized);

        Vector3 originalPlanePointInCutRangeSpace =
            sourceToCutRangeSpace.MultiplyPoint3x4(
                originalPlanePoint);

        float originalAxisPosition = GetAxisValue(
            originalPlanePointInCutRangeSpace,
            cutRangeAxis);

        float correctedAxisPosition = Mathf.Clamp(
            originalAxisPosition,
            cutRangeMinimum,
            cutRangeMaximum);

        // Cut Range Axisだけを有効範囲内へ補正する。
        // それ以外の軸は実際の命中位置を維持し、
        // Cut PlaneがBounds中心へ不必要に移動することを防ぐ。
        Vector3 correctedPlanePointInCutRangeSpace =
            originalPlanePointInCutRangeSpace;

        SetAxisValue(
            ref correctedPlanePointInCutRangeSpace,
            cutRangeAxis,
            correctedAxisPosition);

        Matrix4x4 cutRangeSpaceToSource =
            sourceToCutRangeSpace.inverse;

        correctedPlanePoint =
            cutRangeSpaceToSource.MultiplyPoint3x4(
                correctedPlanePointInCutRangeSpace);

        // sourceToCutRangeSpaceが特異行列の場合はinverseから有効な点を得られない。
        if (!IsFinite(correctedPlanePoint))
        {
            correctedPlanePoint = default;
            failureReason =
                "切断対象TransformのScaleに0が含まれるため、Cut Range座標を変換できません。";
            return false;
        }

        failureReason = null;
        return true;
    }

    internal static bool TryCalculateMeshBoundsInSpace(
        Mesh sourceMesh,
        Matrix4x4 sourceToTargetSpace,
        out Bounds bounds)
    {
        bounds = default;

        if (sourceMesh == null ||
            sourceMesh.vertexCount == 0)
        {
            return false;
        }

        Vector3[] vertices =
            sourceMesh.vertices;

        Vector3 firstVertex =
            sourceToTargetSpace.MultiplyPoint3x4(
                vertices[0]);

        if (!IsFinite(firstVertex))
        {
            return false;
        }

        bounds =
            new Bounds(
                firstVertex,
                Vector3.zero);

        for (int i = 1; i < vertices.Length; i++)
        {
            Vector3 vertex =
                sourceToTargetSpace.MultiplyPoint3x4(
                    vertices[i]);

            if (!IsFinite(vertex))
            {
                return false;
            }

            bounds.Encapsulate(
                vertex);
        }

        return true;
    }

    private static bool IsFinite(
        Vector3 value)
    {
        return !float.IsNaN(value.x) &&
               !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) &&
               !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) &&
               !float.IsInfinity(value.z);
    }

    private static bool IsFinite(
        float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }

    /// <summary>
    /// Scene表示と実行時切断で同じ設定値を使えるよう、
    /// Mesh Bounds内の切断位置有効範囲をBoundsとして計算する。
    /// </summary>
    internal static bool TryCalculateCutRangeBounds(
        Bounds sourceBounds,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        out Bounds cutRangeBounds)
    {
        cutRangeBounds = default;

        if (cutRangeMinimumNormalized < 0.0f ||
            cutRangeMinimumNormalized > 1.0f ||
            cutRangeMaximumNormalized < 0.0f ||
            cutRangeMaximumNormalized > 1.0f ||
            cutRangeMinimumNormalized >= cutRangeMaximumNormalized)
        {
            return false;
        }

        Vector3 minimum =
            sourceBounds.min;

        Vector3 maximum =
            sourceBounds.max;

        float axisMinimum = GetAxisValue(
            minimum,
            cutRangeAxis);

        float axisMaximum = GetAxisValue(
            maximum,
            cutRangeAxis);

        if (axisMaximum <= axisMinimum)
        {
            return false;
        }

        SetAxisValue(
            ref minimum,
            cutRangeAxis,
            Mathf.Lerp(
                axisMinimum,
                axisMaximum,
                cutRangeMinimumNormalized));

        SetAxisValue(
            ref maximum,
            cutRangeAxis,
            Mathf.Lerp(
                axisMinimum,
                axisMaximum,
                cutRangeMaximumNormalized));

        cutRangeBounds.SetMinMax(
            minimum,
            maximum);

        return true;
    }

    private static float GetAxisValue(
        Vector3 value,
        MeshCutRangeAxis axis)
    {
        switch (axis)
        {
            case MeshCutRangeAxis.X:
                return value.x;

            case MeshCutRangeAxis.Y:
                return value.y;

            case MeshCutRangeAxis.Z:
                return value.z;

            default:
                return value.y;
        }
    }

    private static void SetAxisValue(
        ref Vector3 value,
        MeshCutRangeAxis axis,
        float axisValue)
    {
        switch (axis)
        {
            case MeshCutRangeAxis.X:
                value.x = axisValue;
                break;

            case MeshCutRangeAxis.Y:
                value.y = axisValue;
                break;

            case MeshCutRangeAxis.Z:
                value.z = axisValue;
                break;
        }
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

                if (!IsFinite(distanceA) ||
                    !IsFinite(distanceB) ||
                    !IsFinite(distanceC))
                {
                    failureReason =
                        "PlaneとTriangleの距離計算でNaN / Infinityを検出しました。";
                    return false;
                }

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
                        out CutSegment cutSegment,
                        out bool isDegenerateSegment))
                {
                    // PlaneがTriangleの頂点・辺付近を通ると、交点同士が
                    // geometryEpsilon以下へ潰れる場合がある。これは局所的な
                    // 縮退なのでMesh全体の切断失敗にはせず、境界線分だけ除外する。
                    if (isDegenerateSegment)
                    {
                        continue;
                    }

                    failureReason =
                        "交差Triangleの切断線分計算中にNaN / Infinity等の不正値を検出しました。";
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

        firstPieceMesh =
            firstBuilder.BuildMesh("EnemyCut_FirstPiece");

        secondPieceMesh =
            secondBuilder.BuildMesh("EnemyCut_SecondPiece");

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

    private static bool IsValidVertexIndex(
        int index,
        int vertexCount)
    {
        return index >= 0 &&
               index < vertexCount;
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
            hasUv
                ? uv[index]
                : Vector2.zero,
            hasTangents
                ? tangents[index]
                : Vector4.zero);
    }

    private static List<VertexData> ClipTriangle(
        IReadOnlyList<VertexData> triangle,
        IReadOnlyList<float> distances,
        bool keepPositiveSide,
        float geometryEpsilon)
    {
        var output =
            new List<VertexData>(4);

        VertexData previousVertex =
            triangle[triangle.Count - 1];

        float previousDistance =
            distances[distances.Count - 1];

        bool previousInside =
            IsInsideHalfSpace(
                previousDistance,
                keepPositiveSide,
                geometryEpsilon);

        for (int i = 0; i < triangle.Count; i++)
        {
            VertexData currentVertex =
                triangle[i];

            float currentDistance =
                distances[i];

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
                VertexData intersection =
                    InterpolateToPlane(
                        previousVertex,
                        currentVertex,
                        previousDistance,
                        currentDistance);

                AddDistinctPolygonVertex(
                    output,
                    intersection,
                    geometryEpsilon);
            }

            previousVertex =
                currentVertex;

            previousDistance =
                currentDistance;

            previousInside =
                currentInside;
        }

        if (output.Count >= 2 &&
            ArePositionsClose(
                output[0].Position,
                output[output.Count - 1].Position,
                geometryEpsilon))
        {
            output.RemoveAt(
                output.Count - 1);
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
        float denominator =
            fromDistance - toDistance;

        float interpolation =
            Mathf.Abs(denominator) <= Mathf.Epsilon
                ? 0.5f
                : fromDistance / denominator;

        interpolation =
            Mathf.Clamp01(interpolation);

        return VertexData.Lerp(
            from,
            to,
            interpolation);
    }

    private static bool TryGetCutSegment(
        IReadOnlyList<VertexData> triangle,
        IReadOnlyList<float> distances,
        float geometryEpsilon,
        out CutSegment cutSegment,
        out bool isDegenerateSegment)
    {
        cutSegment = default;
        isDegenerateSegment = false;

        if (!IsFinite(geometryEpsilon) ||
            geometryEpsilon <= 0.0f ||
            triangle == null ||
            distances == null ||
            triangle.Count != distances.Count)
        {
            return false;
        }

        var points =
            new List<Vector3>(3);

        for (int i = 0;
             i < triangle.Count;
             i++)
        {
            int nextIndex =
                (i + 1) % triangle.Count;

            Vector3 currentPosition =
                triangle[i].Position;

            Vector3 nextPosition =
                triangle[nextIndex].Position;

            float currentDistance =
                distances[i];

            float nextDistance =
                distances[nextIndex];

            if (!IsFinite(currentPosition) ||
                !IsFinite(nextPosition) ||
                !IsFinite(currentDistance) ||
                !IsFinite(nextDistance))
            {
                return false;
            }

            if (Mathf.Abs(currentDistance) <=
                geometryEpsilon)
            {
                AddDistinctPoint(
                    points,
                    currentPosition,
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

            VertexData intersection =
                InterpolateToPlane(
                    triangle[i],
                    triangle[nextIndex],
                    currentDistance,
                    nextDistance);

            if (!IsFinite(intersection.Position))
            {
                return false;
            }

            AddDistinctPoint(
                points,
                intersection.Position,
                geometryEpsilon);
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

            if (sqrDistance <=
                geometryEpsilon * geometryEpsilon)
            {
                isDegenerateSegment = true;
                return false;
            }

            cutSegment =
                new CutSegment(
                    points[0],
                    points[1]);

            return true;
        }

        float greatestSqrDistance =
            0.0f;

        Vector3 firstPoint =
            default;

        Vector3 secondPoint =
            default;

        for (int firstIndex = 0;
             firstIndex < points.Count - 1;
             firstIndex++)
        {
            for (int secondIndex = firstIndex + 1;
                 secondIndex < points.Count;
                 secondIndex++)
            {
                float sqrDistance =
                    (points[firstIndex] -
                     points[secondIndex]).sqrMagnitude;

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

                firstPoint =
                    points[firstIndex];

                secondPoint =
                    points[secondIndex];
            }
        }

        if (greatestSqrDistance <=
            geometryEpsilon * geometryEpsilon)
        {
            isDegenerateSegment = true;
            return false;
        }

        cutSegment =
            new CutSegment(
                firstPoint,
                secondPoint);

        return true;
    }

    private static void AddDistinctPoint(
        List<Vector3> points,
        Vector3 point,
        float geometryEpsilon)
    {
        for (int i = 0;
             i < points.Count;
             i++)
        {
            if (ArePositionsClose(
                    points[i],
                    point,
                    geometryEpsilon))
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
        var nodes =
            new List<BoundaryNode>();

        var uniqueEdges =
            new HashSet<long>();

        for (int i = 0;
             i < cutSegments.Count;
             i++)
        {
            int firstNodeIndex =
                FindOrCreateBoundaryNode(
                    nodes,
                    cutSegments[i].First,
                    geometryEpsilon);

            int secondNodeIndex =
                FindOrCreateBoundaryNode(
                    nodes,
                    cutSegments[i].Second,
                    geometryEpsilon);

            if (firstNodeIndex ==
                secondNodeIndex)
            {
                continue;
            }

            long edgeKey =
                CreateEdgeKey(
                    firstNodeIndex,
                    secondNodeIndex);

            if (!uniqueEdges.Add(edgeKey))
            {
                continue;
            }

            nodes[firstNodeIndex]
                .Neighbors
                .Add(secondNodeIndex);

            nodes[secondNodeIndex]
                .Neighbors
                .Add(firstNodeIndex);
        }

        if (uniqueEdges.Count < 3)
        {
            boundaryLoops = null;
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
                failureReason =
                    "切断境界が閉じた単純ループになっていません。";

                return false;
            }
        }

        boundaryLoops =
            new List<List<Vector3>>();

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
                        out List<Vector3> loop))
                {
                    boundaryLoops = null;
                    failureReason =
                        "切断境界ループの追跡に失敗しました。";

                    return false;
                }

                if (loop.Count < 3)
                {
                    boundaryLoops = null;
                    failureReason =
                        "切断境界ループの頂点数が不足しています。";

                    return false;
                }

                boundaryLoops.Add(loop);
            }
        }

        if (boundaryLoops.Count == 0 ||
            visitedEdges.Count != uniqueEdges.Count)
        {
            boundaryLoops = null;
            failureReason =
                "すべての切断境界を閉じたループへ構築できませんでした。";

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
        float sqrEpsilon =
            geometryEpsilon * geometryEpsilon;

        for (int i = 0;
             i < nodes.Count;
             i++)
        {
            if ((nodes[i].Position - position).sqrMagnitude <=
                sqrEpsilon)
            {
                return i;
            }
        }

        nodes.Add(
            new BoundaryNode(position));

        return nodes.Count - 1;
    }

    private static bool TryTraceBoundaryLoop(
        IReadOnlyList<BoundaryNode> nodes,
        int startNode,
        int firstNextNode,
        HashSet<long> visitedEdges,
        out List<Vector3> loop)
    {
        loop =
            new List<Vector3>();

        int previousNode = -1;
        int currentNode = startNode;
        int nextNode = firstNextNode;
        int guard = 0;
        int maximumSteps = nodes.Count + 1;

        while (true)
        {
            loop.Add(
                nodes[currentNode].Position);

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

        var preparedLoops =
            new List<PreparedBoundaryLoop>(
                boundaryLoops.Count);

        for (int i = 0;
             i < boundaryLoops.Count;
             i++)
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
        Vector3 planeAxisU,
        Vector3 planeAxisV,
        float geometryEpsilon,
        out PreparedBoundaryLoop preparedLoop)
    {
        var positions =
            new List<Vector3>(
                sourcePositions.Count);

        var projectedPoints =
            new List<Vector2>(
                sourcePositions.Count);

        for (int i = 0;
             i < sourcePositions.Count;
             i++)
        {
            positions.Add(
                sourcePositions[i]);

            projectedPoints.Add(
                ProjectToPlane2D(
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
        }

        preparedLoop =
            new PreparedBoundaryLoop(
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

        for (int i = 0;
             i <= polygonBridgeIndex;
             i++)
        {
            mergedPositions.Add(
                polygonPositions[i]);

            mergedProjectedPoints.Add(
                polygonProjectedPoints[i]);
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
        }

        mergedPositions.Add(
            hole.Positions[holeBridgeIndex]);

        mergedProjectedPoints.Add(
            hole.ProjectedPoints[holeBridgeIndex]);

        mergedPositions.Add(
            polygonPositions[polygonBridgeIndex]);

        mergedProjectedPoints.Add(
            polygonProjectedPoints[polygonBridgeIndex]);

        for (int i = polygonBridgeIndex + 1;
             i < polygonPositions.Count;
             i++)
        {
            mergedPositions.Add(
                polygonPositions[i]);

            mergedProjectedPoints.Add(
                polygonProjectedPoints[i]);
        }

        polygonPositions.Clear();
        polygonPositions.AddRange(
            mergedPositions);

        polygonProjectedPoints.Clear();
        polygonProjectedPoints.AddRange(
            mergedProjectedPoints);

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
                desiredNormal,
                tangent,
                planeAxisU,
                planeAxisV,
                cutSurfaceUvScale);

        VertexData vertexB =
            CreateCapVertex(
                pointB,
                desiredNormal,
                tangent,
                planeAxisU,
                planeAxisV,
                cutSurfaceUvScale);

        VertexData vertexC =
            CreateCapVertex(
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
                tangent);
        }
    }

    private readonly struct CutSegment
    {
        public Vector3 First { get; }
        public Vector3 Second { get; }

        public CutSegment(
            Vector3 first,
            Vector3 second)
        {
            First = first;
            Second = second;
        }
    }

    private sealed class BoundaryNode
    {
        public Vector3 Position { get; }
        public List<int> Neighbors { get; }

        public BoundaryNode(
            Vector3 position)
        {
            Position = position;

            Neighbors =
                new List<int>(2);
        }
    }

    private sealed class PreparedBoundaryLoop
    {
        public List<Vector3> Positions { get; }
        public List<Vector2> ProjectedPoints { get; }
        public int ParentIndex { get; set; }
        public int NestingDepth { get; set; }

        public PreparedBoundaryLoop(
            List<Vector3> positions,
            List<Vector2> projectedPoints)
        {
            Positions =
                positions;

            ProjectedPoints =
                projectedPoints;

            ParentIndex = -1;
            NestingDepth = 0;
        }
    }

    private sealed class PieceMeshBuilder
    {
        private readonly List<Vector3> vertices =
            new List<Vector3>();

        private readonly List<Vector3> normals =
            new List<Vector3>();

        private readonly List<Vector2> uv =
            new List<Vector2>();

        private readonly List<Vector4> tangents =
            new List<Vector4>();

        private readonly List<List<int>>
            trianglesBySubMesh;

        private readonly bool hasTangents;

        public int OriginalTriangleCount
        {
            get;
            private set;
        }

        public PieceMeshBuilder(
            int subMeshCount,
            bool hasTangents)
        {
            this.hasTangents =
                hasTangents;

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
            int firstIndex =
                AddVertex(first);

            int secondIndex =
                AddVertex(second);

            int thirdIndex =
                AddVertex(third);

            trianglesBySubMesh[
                subMeshIndex].Add(
                    firstIndex);

            trianglesBySubMesh[
                subMeshIndex].Add(
                    secondIndex);

            trianglesBySubMesh[
                subMeshIndex].Add(
                    thirdIndex);

            if (!isCap)
            {
                OriginalTriangleCount++;
            }
        }

        public Mesh BuildMesh(
            string meshName)
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

            mesh.RecalculateBounds();

            return mesh;
        }

        private int AddVertex(
            VertexData vertex)
        {
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

            return index;
        }
    }
}


/// <summary>
/// EnemyMeshCutterが付いているEnemy本体のローカル空間で、切断位置の有効範囲を定義する軸。
/// </summary>
public enum MeshCutRangeAxis
{
    X,
    Y,
    Z
}
