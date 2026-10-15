using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 命中時に確定済みの刃中心・刃方向・移動方向から切断Planeを求め、
/// Bake済みMeshを2つへ分割する。
/// </summary>
/// <remarks>
/// GameObject生成、Renderer操作、HP、死亡判定、攻撃受信は担当しない。
/// 入力する刃中心・刃方向・移動方向は、切断対象Meshと同じCut Spaceへ
/// 変換済みであること。
/// </remarks>
public sealed class ChainsawMeshCutProcessor
{
    private const float GEOMETRY_EPSILON_RATIO = 0.00001f;
    private const float MIN_GEOMETRY_EPSILON = 0.000001f;
    private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.00000001f;

    /// <summary>
    /// Bake済みMeshを、命中時に確定済みの刃情報から生成したPlaneで2つへ分割する。
    /// </summary>
    public bool TryCut(
        Mesh sourceMesh,
        Vector3 bladeCenterLocal,
        Vector3 bladeDirectionLocal,
        Vector3 bladeMovementDirectionLocal,
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
                bladeCenterLocal,
                bladeDirectionLocal,
                bladeMovementDirectionLocal,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                cutSurfaceUvScale,
                out failureReason))
        {
            return false;
        }

        float geometryEpsilon =
            CalculateGeometryEpsilon(
                sourceMesh.bounds);

        if (!TryCreateCutPlane(
                sourceMesh.bounds,
                bladeCenterLocal,
                bladeDirectionLocal,
                bladeMovementDirectionLocal,
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
        Vector3 bladeCenterLocal,
        Vector3 bladeDirectionLocal,
        Vector3 bladeMovementDirectionLocal,
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

        if (!IsFinite(bladeCenterLocal) ||
            !IsFinite(bladeDirectionLocal) ||
            !IsFinite(bladeMovementDirectionLocal))
        {
            failureReason =
                "切断情報にNaNまたはInfinityが含まれています。";
            return false;
        }

        if (bladeDirectionLocal.sqrMagnitude <=
            MIN_DIRECTION_SQR_MAGNITUDE)
        {
            failureReason =
                "有効な刃方向を取得できませんでした。";
            return false;
        }

        if (bladeMovementDirectionLocal.sqrMagnitude <=
            MIN_DIRECTION_SQR_MAGNITUDE)
        {
            failureReason =
                "有効なチェンソー移動方向を取得できませんでした。";
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
            failureReason =
                "cutSurfaceUvScaleは0より大きい値である必要があります。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool IsFinite(
        Vector3 value)
    {
        return IsFinite(value.x) &&
               IsFinite(value.y) &&
               IsFinite(value.z);
    }

    private static bool IsFinite(
        float value)
    {
        return !float.IsNaN(value) &&
               !float.IsInfinity(value);
    }

    private static float CalculateGeometryEpsilon(
        Bounds bounds)
    {
        return Mathf.Max(
            bounds.size.magnitude *
            GEOMETRY_EPSILON_RATIO,
            MIN_GEOMETRY_EPSILON);
    }

    private bool TryCreateCutPlane(
        Bounds sourceBounds,
        Vector3 bladeCenterLocal,
        Vector3 bladeDirectionLocal,
        Vector3 bladeMovementDirectionLocal,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        float geometryEpsilon,
        out Plane correctedCutPlane,
        out string failureReason)
    {
        Vector3 planeNormal =
            Vector3.Cross(
                bladeDirectionLocal.normalized,
                bladeMovementDirectionLocal.normalized);

        if (planeNormal.sqrMagnitude <=
            MIN_DIRECTION_SQR_MAGNITUDE)
        {
            correctedCutPlane = default;
            failureReason =
                "刃方向と移動方向がほぼ平行なため、"
                + "有効な切断Planeを算出できませんでした。";
            return false;
        }

        planeNormal.Normalize();

        if (!TryCreateCutPlanePoint(
                sourceBounds,
                bladeCenterLocal,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                geometryEpsilon,
                out Vector3 correctedPlanePoint,
                out failureReason))
        {
            correctedCutPlane = default;
            return false;
        }

        correctedCutPlane =
            new Plane(
                planeNormal,
                correctedPlanePoint);

        failureReason = null;
        return true;
    }

    private static bool TryCreateCutPlanePoint(
        Bounds sourceBounds,
        Vector3 originalPlanePoint,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        float geometryEpsilon,
        out Vector3 correctedPlanePoint,
        out string failureReason)
    {
        float boundsMinimum = GetAxisValue(
            sourceBounds.min,
            cutRangeAxis);

        float boundsMaximum = GetAxisValue(
            sourceBounds.max,
            cutRangeAxis);

        float axisSize =
            boundsMaximum - boundsMinimum;

        if (axisSize <= geometryEpsilon)
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

        float originalAxisPosition = GetAxisValue(
            originalPlanePoint,
            cutRangeAxis);

        float correctedAxisPosition = Mathf.Clamp(
            originalAxisPosition,
            cutRangeMinimum,
            cutRangeMaximum);

        // 切断位置として使う軸だけ実際の命中位置を反映し、
        // それ以外の軸はMesh Bounds中央へ寄せる。
        // これにより奥行き方向などの端をかすった命中位置が
        // そのまま切断Plane基準点になることを防ぐ。
        correctedPlanePoint =
            sourceBounds.center;

        SetAxisValue(
            ref correctedPlanePoint,
            cutRangeAxis,
            correctedAxisPosition);

        failureReason = null;
        return true;
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
                        cutPlane.normal,
                        geometryEpsilon,
                        out CutSegment cutSegment))
                {
                    failureReason =
                        "交差Triangleから有効な切断線分を生成できませんでした.";
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
        Vector3 cutPlaneNormal,
        float geometryEpsilon,
        out CutSegment cutSegment)
    {
        var points =
            new List<Vector3>(3);

        for (int i = 0;
             i < triangle.Count;
             i++)
        {
            int nextIndex =
                (i + 1) % triangle.Count;

            float currentDistance =
                distances[i];

            float nextDistance =
                distances[nextIndex];

            if (Mathf.Abs(currentDistance) <=
                geometryEpsilon)
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

            VertexData intersection =
                InterpolateToPlane(
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

            cutSegment =
                CreateOrientedCutSegment(
                    points[0],
                    points[1],
                    triangle,
                    cutPlaneNormal);

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
            cutSegment = default;
            return false;
        }

        cutSegment =
            CreateOrientedCutSegment(
                firstPoint,
                secondPoint,
                triangle,
                cutPlaneNormal);

        return true;
    }

    /// <summary>
    /// 元Triangleのwindingを利用して切断線分の向きを揃える。
    /// </summary>
    /// <remarks>
    /// 切断境界を無向グラフとして追跡するとループ方向が失われ、
    /// 入れ子ループが「別shell」なのか「穴」なのかを判定できない。
    /// Plane法線とTriangle法線から得られる交線方向へ線分を揃えることで、
    /// 外周と穴のwinding差を後段まで保持する。
    /// </remarks>
    private static CutSegment CreateOrientedCutSegment(
        Vector3 firstPoint,
        Vector3 secondPoint,
        IReadOnlyList<VertexData> triangle,
        Vector3 cutPlaneNormal)
    {
        Vector3 triangleNormal =
            Vector3.Cross(
                triangle[1].Position -
                triangle[0].Position,
                triangle[2].Position -
                triangle[0].Position);

        Vector3 expectedDirection =
            Vector3.Cross(
                cutPlaneNormal,
                triangleNormal);

        if (expectedDirection.sqrMagnitude >
                MIN_DIRECTION_SQR_MAGNITUDE &&
            Vector3.Dot(
                secondPoint - firstPoint,
                expectedDirection) < 0.0f)
        {
            Vector3 temporary =
                firstPoint;

            firstPoint =
                secondPoint;

            secondPoint =
                temporary;
        }

        return new CutSegment(
            firstPoint,
            secondPoint);
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

        // 無向Edgeでループを構築しつつ、
        // 元Triangle winding由来の線分方向を別途保持する。
        // 追跡開始方向が逆でも、完了後に本来のwindingへ戻せる。
        var directedEdgeByUndirectedEdge =
            new Dictionary<long, long>();

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

            directedEdgeByUndirectedEdge[edgeKey] =
                CreateDirectedEdgeKey(
                    firstNodeIndex,
                    secondNodeIndex);

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
                        directedEdgeByUndirectedEdge,
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
        IReadOnlyDictionary<long, long>
            directedEdgeByUndirectedEdge,
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
        int orientationBalance = 0;
        int guard = 0;
        int maximumSteps = nodes.Count + 1;

        while (true)
        {
            loop.Add(
                nodes[currentNode].Position);

            long edgeKey =
                CreateEdgeKey(
                    currentNode,
                    nextNode);

            visitedEdges.Add(
                edgeKey);

            if (directedEdgeByUndirectedEdge.TryGetValue(
                    edgeKey,
                    out long directedEdge))
            {
                orientationBalance +=
                    directedEdge ==
                    CreateDirectedEdgeKey(
                        currentNode,
                        nextNode)
                        ? 1
                        : -1;
            }

            previousNode =
                currentNode;

            currentNode =
                nextNode;

            if (currentNode == startNode)
            {
                // 無向グラフのどちら向きから追跡を開始しても、
                // 元Triangle windingから得た向きへ最終ループを揃える。
                if (orientationBalance < 0)
                {
                    loop.Reverse();
                }

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

    private static long CreateDirectedEdgeKey(
        int firstNode,
        int secondNode)
    {
        return ((long)(uint)firstNode << 32) |
               (uint)secondNode;
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
                    "切断境界を断面三角形分割用のPolygonへ変換できませんでした。";

                return false;
            }

            preparedLoops.Add(
                preparedLoop);
        }

        if (!TryAddPreparedLoopCaps(
                preparedLoops,
                cutPlane,
                cutSurfaceUvScale,
                geometryEpsilon,
                cutSurfaceSubMeshIndex,
                planeAxisU,
                planeAxisV,
                firstBuilder,
                secondBuilder,
                out failureReason))
        {
            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// windingを保持した切断境界から、別shellと穴を区別して断面を生成する。
    /// </summary>
    /// <remarks>
    /// 同じwindingを持つ入れ子ループはBodyと衣服などの別shellとして
    /// それぞれ独立した断面にする。
    /// 反対windingのループが外周内に存在する場合だけ穴として扱い、
    /// 外周へブリッジした弱単純Polygonへ変換してEar Clippingする。
    /// </remarks>
    private static bool TryAddPreparedLoopCaps(
        IReadOnlyList<PreparedBoundaryLoop> loops,
        Plane cutPlane,
        float cutSurfaceUvScale,
        float geometryEpsilon,
        int cutSurfaceSubMeshIndex,
        Vector3 planeAxisU,
        Vector3 planeAxisV,
        PieceMeshBuilder firstBuilder,
        PieceMeshBuilder secondBuilder,
        out string failureReason)
    {
        if (loops == null ||
            loops.Count == 0)
        {
            failureReason =
                "切断断面として処理できる境界ループがありません。";

            return false;
        }

        int referenceLoopIndex =
            FindLargestAbsoluteAreaLoopIndex(
                loops);

        if (referenceLoopIndex < 0)
        {
            failureReason =
                "切断境界ループのwinding基準を決定できませんでした。";

            return false;
        }

        float solidWindingSign =
            Mathf.Sign(
                loops[referenceLoopIndex]
                    .SignedArea);

        if (Mathf.Approximately(
                solidWindingSign,
                0.0f))
        {
            failureReason =
                "切断境界ループの面積が小さすぎます。";

            return false;
        }

        var solidLoopIndices =
            new List<int>();

        var oppositeLoopIndices =
            new List<int>();

        for (int loopIndex = 0;
             loopIndex < loops.Count;
             loopIndex++)
        {
            if (Mathf.Sign(
                    loops[loopIndex]
                        .SignedArea) ==
                solidWindingSign)
            {
                solidLoopIndices.Add(
                    loopIndex);
            }
            else
            {
                oppositeLoopIndices.Add(
                    loopIndex);
            }
        }

        var holeIndicesBySolidLoop =
            new Dictionary<int, List<int>>();

        var standaloneLoopIndices =
            new List<int>(
                solidLoopIndices);

        for (int oppositeListIndex = 0;
             oppositeListIndex <
             oppositeLoopIndices.Count;
             oppositeListIndex++)
        {
            int holeCandidateIndex =
                oppositeLoopIndices[
                    oppositeListIndex];

            int ownerLoopIndex =
                FindSmallestContainingSolidLoop(
                    loops,
                    solidLoopIndices,
                    holeCandidateIndex);

            if (ownerLoopIndex < 0)
            {
                // 反対windingでも他ループに包含されていない場合は、
                // windingが反転した独立shellである可能性が高い。
                // 穴として捨てず、独立断面として三角形分割する。
                standaloneLoopIndices.Add(
                    holeCandidateIndex);

                continue;
            }

            if (!holeIndicesBySolidLoop.TryGetValue(
                    ownerLoopIndex,
                    out List<int> holeIndices))
            {
                holeIndices =
                    new List<int>();

                holeIndicesBySolidLoop.Add(
                    ownerLoopIndex,
                    holeIndices);
            }

            holeIndices.Add(
                holeCandidateIndex);
        }

        float earEpsilon =
            geometryEpsilon *
            geometryEpsilon;

        for (int standaloneIndex = 0;
             standaloneIndex <
             standaloneLoopIndices.Count;
             standaloneIndex++)
        {
            int outerLoopIndex =
                standaloneLoopIndices[
                    standaloneIndex];

            IReadOnlyList<int> holeIndices =
                holeIndicesBySolidLoop.TryGetValue(
                    outerLoopIndex,
                    out List<int> assignedHoles)
                    ? assignedHoles
                    : null;

            if (!TryCreateTriangulationPolygon(
                    loops,
                    outerLoopIndex,
                    holeIndices,
                    geometryEpsilon,
                    out List<Vector3> polygonPositions,
                    out List<Vector2> polygonPoints))
            {
                failureReason =
                    "穴を含む切断境界を三角形分割用Polygonへ変換できませんでした。";

                return false;
            }

            if (!TryTriangulateEarClipping(
                    polygonPoints,
                    earEpsilon,
                    out List<int> triangleIndices))
            {
                failureReason =
                    "Ear Clippingによる切断面生成に失敗しました。";

                return false;
            }

            for (int triangleIndex = 0;
                 triangleIndex <
                 triangleIndices.Count;
                 triangleIndex += 3)
            {
                Vector3 pointA =
                    polygonPositions[
                        triangleIndices[
                            triangleIndex]];

                Vector3 pointB =
                    polygonPositions[
                        triangleIndices[
                            triangleIndex + 1]];

                Vector3 pointC =
                    polygonPositions[
                        triangleIndices[
                            triangleIndex + 2]];

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

    private static int FindLargestAbsoluteAreaLoopIndex(
        IReadOnlyList<PreparedBoundaryLoop> loops)
    {
        int largestLoopIndex = -1;
        float largestArea = 0.0f;

        for (int i = 0;
             i < loops.Count;
             i++)
        {
            float area =
                Mathf.Abs(
                    loops[i].SignedArea);

            if (area <= largestArea)
            {
                continue;
            }

            largestArea =
                area;

            largestLoopIndex =
                i;
        }

        return largestLoopIndex;
    }

    private static int FindSmallestContainingSolidLoop(
        IReadOnlyList<PreparedBoundaryLoop> loops,
        IReadOnlyList<int> solidLoopIndices,
        int holeCandidateIndex)
    {
        Vector2 testPoint =
            loops[holeCandidateIndex]
                .ProjectedPoints[0];

        int ownerLoopIndex = -1;
        float ownerArea =
            float.PositiveInfinity;

        for (int i = 0;
             i < solidLoopIndices.Count;
             i++)
        {
            int solidLoopIndex =
                solidLoopIndices[i];

            PreparedBoundaryLoop solidLoop =
                loops[solidLoopIndex];

            if (!IsPointInsidePolygon(
                    testPoint,
                    solidLoop.ProjectedPoints))
            {
                continue;
            }

            float solidArea =
                Mathf.Abs(
                    solidLoop.SignedArea);

            if (solidArea >= ownerArea)
            {
                continue;
            }

            ownerArea =
                solidArea;

            ownerLoopIndex =
                solidLoopIndex;
        }

        return ownerLoopIndex;
    }

    private static bool TryCreateTriangulationPolygon(
        IReadOnlyList<PreparedBoundaryLoop> loops,
        int outerLoopIndex,
        IReadOnlyList<int> holeIndices,
        float geometryEpsilon,
        out List<Vector3> polygonPositions,
        out List<Vector2> polygonPoints)
    {
        PreparedBoundaryLoop outerLoop =
            loops[outerLoopIndex];

        polygonPositions =
            new List<Vector3>(
                outerLoop.Positions);

        polygonPoints =
            new List<Vector2>(
                outerLoop.ProjectedPoints);

        // Ear Clipping側は反時計回り外周を前提にする。
        if (CalculateSignedArea(
                polygonPoints) < 0.0f)
        {
            polygonPositions.Reverse();
            polygonPoints.Reverse();
        }

        if (holeIndices == null ||
            holeIndices.Count == 0)
        {
            return true;
        }

        var sortedHoleIndices =
            new List<int>(
                holeIndices);

        SortHoleIndicesByRightmostPointDescending(
            loops,
            sortedHoleIndices);

        for (int holeListIndex = 0;
             holeListIndex <
             sortedHoleIndices.Count;
             holeListIndex++)
        {
            PreparedBoundaryLoop holeLoop =
                loops[
                    sortedHoleIndices[
                        holeListIndex]];

            var holePositions =
                new List<Vector3>(
                    holeLoop.Positions);

            var holePoints =
                new List<Vector2>(
                    holeLoop.ProjectedPoints);

            // 穴は外周と逆向きに辿る必要がある。
            if (CalculateSignedArea(
                    holePoints) > 0.0f)
            {
                holePositions.Reverse();
                holePoints.Reverse();
            }

            if (!TryBridgeHoleIntoPolygon(
                    polygonPositions,
                    polygonPoints,
                    holePositions,
                    holePoints,
                    geometryEpsilon))
            {
                polygonPositions = null;
                polygonPoints = null;

                return false;
            }
        }

        return polygonPositions.Count >= 3 &&
               polygonPoints.Count ==
               polygonPositions.Count;
    }

    private static void SortHoleIndicesByRightmostPointDescending(
        IReadOnlyList<PreparedBoundaryLoop> loops,
        List<int> holeIndices)
    {
        for (int i = 1;
             i < holeIndices.Count;
             i++)
        {
            int value =
                holeIndices[i];

            float valueRightmostX =
                GetRightmostPointX(
                    loops[value]
                        .ProjectedPoints);

            int insertionIndex =
                i - 1;

            while (insertionIndex >= 0)
            {
                float comparedRightmostX =
                    GetRightmostPointX(
                        loops[
                            holeIndices[
                                insertionIndex]]
                            .ProjectedPoints);

                if (comparedRightmostX >=
                    valueRightmostX)
                {
                    break;
                }

                holeIndices[
                    insertionIndex + 1] =
                    holeIndices[
                        insertionIndex];

                insertionIndex--;
            }

            holeIndices[
                insertionIndex + 1] =
                value;
        }
    }

    private static float GetRightmostPointX(
        IReadOnlyList<Vector2> points)
    {
        float rightmostX =
            float.NegativeInfinity;

        for (int i = 0;
             i < points.Count;
             i++)
        {
            rightmostX =
                Mathf.Max(
                    rightmostX,
                    points[i].x);
        }

        return rightmostX;
    }

    /// <summary>
    /// 穴の右端から外周方向へブリッジを作り、
    /// 穴付きPolygonをEar Clipping可能な弱単純Polygonへ変換する。
    /// </summary>
    private static bool TryBridgeHoleIntoPolygon(
        List<Vector3> polygonPositions,
        List<Vector2> polygonPoints,
        IReadOnlyList<Vector3> holePositions,
        IReadOnlyList<Vector2> holePoints,
        float geometryEpsilon)
    {
        if (polygonPositions == null ||
            polygonPoints == null ||
            holePositions == null ||
            holePoints == null ||
            polygonPositions.Count < 3 ||
            holePositions.Count < 3 ||
            polygonPositions.Count !=
                polygonPoints.Count ||
            holePositions.Count !=
                holePoints.Count)
        {
            return false;
        }

        int holeBridgeIndex =
            FindRightmostPointIndex(
                holePoints);

        Vector2 holeBridgePoint =
            holePoints[
                holeBridgeIndex];

        float closestIntersectionX =
            float.PositiveInfinity;

        int bridgeEdgeStartIndex = -1;
        int bridgeEdgeEndIndex = -1;
        float bridgeEdgeInterpolation =
            0.0f;

        for (int edgeStartIndex = 0;
             edgeStartIndex <
             polygonPoints.Count;
             edgeStartIndex++)
        {
            int edgeEndIndex =
                (edgeStartIndex + 1) %
                polygonPoints.Count;

            Vector2 edgeStart =
                polygonPoints[
                    edgeStartIndex];

            Vector2 edgeEnd =
                polygonPoints[
                    edgeEndIndex];

            if ((edgeStart.y >
                 holeBridgePoint.y) ==
                (edgeEnd.y >
                 holeBridgePoint.y))
            {
                continue;
            }

            float edgeDeltaY =
                edgeEnd.y -
                edgeStart.y;

            if (Mathf.Abs(
                    edgeDeltaY) <=
                geometryEpsilon)
            {
                continue;
            }

            float interpolation =
                (holeBridgePoint.y -
                 edgeStart.y) /
                edgeDeltaY;

            float intersectionX =
                Mathf.Lerp(
                    edgeStart.x,
                    edgeEnd.x,
                    interpolation);

            if (intersectionX <=
                    holeBridgePoint.x +
                    geometryEpsilon ||
                intersectionX >=
                    closestIntersectionX)
            {
                continue;
            }

            closestIntersectionX =
                intersectionX;

            bridgeEdgeStartIndex =
                edgeStartIndex;

            bridgeEdgeEndIndex =
                edgeEndIndex;

            bridgeEdgeInterpolation =
                interpolation;
        }

        if (bridgeEdgeStartIndex < 0 ||
            bridgeEdgeEndIndex < 0)
        {
            return false;
        }

        Vector2 bridgePoint2D =
            new Vector2(
                closestIntersectionX,
                holeBridgePoint.y);

        Vector3 bridgePoint3D =
            Vector3.Lerp(
                polygonPositions[
                    bridgeEdgeStartIndex],
                polygonPositions[
                    bridgeEdgeEndIndex],
                bridgeEdgeInterpolation);

        int polygonBridgeIndex;

        float sqrEpsilon =
            geometryEpsilon *
            geometryEpsilon;

        if ((polygonPoints[
                bridgeEdgeStartIndex] -
             bridgePoint2D).sqrMagnitude <=
            sqrEpsilon)
        {
            polygonBridgeIndex =
                bridgeEdgeStartIndex;
        }
        else if ((polygonPoints[
                     bridgeEdgeEndIndex] -
                  bridgePoint2D).sqrMagnitude <=
                 sqrEpsilon)
        {
            polygonBridgeIndex =
                bridgeEdgeEndIndex;
        }
        else
        {
            int insertionIndex =
                bridgeEdgeEndIndex == 0
                    ? polygonPoints.Count
                    : bridgeEdgeEndIndex;

            polygonPositions.Insert(
                insertionIndex,
                bridgePoint3D);

            polygonPoints.Insert(
                insertionIndex,
                bridgePoint2D);

            polygonBridgeIndex =
                insertionIndex;
        }

        var mergedPositions =
            new List<Vector3>(
                polygonPositions.Count +
                holePositions.Count +
                2);

        var mergedPoints =
            new List<Vector2>(
                polygonPoints.Count +
                holePoints.Count +
                2);

        for (int i = 0;
             i <= polygonBridgeIndex;
             i++)
        {
            mergedPositions.Add(
                polygonPositions[i]);

            mergedPoints.Add(
                polygonPoints[i]);
        }

        for (int holeOffset = 0;
             holeOffset <
             holePositions.Count;
             holeOffset++)
        {
            int sourceHoleIndex =
                (holeBridgeIndex +
                 holeOffset) %
                holePositions.Count;

            mergedPositions.Add(
                holePositions[
                    sourceHoleIndex]);

            mergedPoints.Add(
                holePoints[
                    sourceHoleIndex]);
        }

        // ブリッジは穴側と外周側の頂点をそれぞれ2回通る。
        // Ear Clipping時に非連続の同一点を許容することで、
        // 穴を塗り潰さず1本の弱単純Polygonとして扱える。
        mergedPositions.Add(
            holePositions[
                holeBridgeIndex]);

        mergedPoints.Add(
            holePoints[
                holeBridgeIndex]);

        mergedPositions.Add(
            polygonPositions[
                polygonBridgeIndex]);

        mergedPoints.Add(
            polygonPoints[
                polygonBridgeIndex]);

        for (int i = polygonBridgeIndex + 1;
             i < polygonPositions.Count;
             i++)
        {
            mergedPositions.Add(
                polygonPositions[i]);

            mergedPoints.Add(
                polygonPoints[i]);
        }

        polygonPositions.Clear();
        polygonPositions.AddRange(
            mergedPositions);

        polygonPoints.Clear();
        polygonPoints.AddRange(
            mergedPoints);

        return true;
    }

    private static int FindRightmostPointIndex(
        IReadOnlyList<Vector2> points)
    {
        int rightmostIndex = 0;

        for (int i = 1;
             i < points.Count;
             i++)
        {
            if (points[i].x >
                points[rightmostIndex].x)
            {
                rightmostIndex = i;
                continue;
            }

            if (Mathf.Approximately(
                    points[i].x,
                    points[rightmostIndex].x) &&
                points[i].y <
                points[rightmostIndex].y)
            {
                rightmostIndex = i;
            }
        }

        return rightmostIndex;
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

        preparedLoop =
            new PreparedBoundaryLoop(
                positions,
                projectedPoints,
                signedArea);

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

                    // 穴を外周へブリッジしたPolygonでは、
                    // ブリッジ両端が非連続位置に重複して存在する。
                    // 同一点を「Ear内部の別頂点」と判定すると
                    // 正しいEarまで拒否して三角形分割できなくなる。
                    if (AreProjectedPointsClose(
                            testPoint,
                            previous,
                            areaEpsilon) ||
                        AreProjectedPointsClose(
                            testPoint,
                            current,
                            areaEpsilon) ||
                        AreProjectedPointsClose(
                            testPoint,
                            next,
                            areaEpsilon))
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

        Vector2 finalFirst =
            polygon[
                remainingIndices[0]];

        Vector2 finalSecond =
            polygon[
                remainingIndices[1]];

        Vector2 finalThird =
            polygon[
                remainingIndices[2]];

        if (Cross2D(
                finalSecond - finalFirst,
                finalThird - finalSecond) <=
            areaEpsilon)
        {
            triangleIndices = null;
            return false;
        }

        triangleIndices.Add(
            remainingIndices[0]);

        triangleIndices.Add(
            remainingIndices[1]);

        triangleIndices.Add(
            remainingIndices[2]);

        return true;
    }

    private static bool AreProjectedPointsClose(
        Vector2 first,
        Vector2 second,
        float sqrEpsilon)
    {
        return (first - second).sqrMagnitude <=
               sqrEpsilon;
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
        public float SignedArea { get; }

        public PreparedBoundaryLoop(
            List<Vector3> positions,
            List<Vector2> projectedPoints,
            float signedArea)
        {
            Positions =
                positions;

            ProjectedPoints =
                projectedPoints;

            SignedArea =
                signedArea;
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
/// Meshローカル空間で切断位置の有効範囲を定義する軸。
/// </summary>
public enum MeshCutRangeAxis
{
    X,
    Y,
    Z
}
