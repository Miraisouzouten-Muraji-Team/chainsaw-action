using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// チェンソー軌跡から切断Planeを決定し、共通Mesh分割処理へ切断を依頼する。
/// </summary>
/// <remarks>
/// Triangle分割、切断面生成、Mesh構築はMeshPlaneSplitterへ委譲する。
/// GameObject生成、Renderer操作、HP、死亡判定、攻撃受信は担当しない。
/// 入力するTip / Root位置は、切断対象Meshと同じローカル空間へ変換済みであること。
/// </remarks>
public sealed class ChainsawMeshCutProcessor
{
    private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.00000001f;

    private readonly MeshPlaneSplitter meshPlaneSplitter =
        new MeshPlaneSplitter();

    /// <summary>
    /// Bake済みMeshをチェンソー軌跡由来のPlaneで分割し、Connected Piece単位の最終Meshを生成する。
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
        bool enableDiagnostics,
        out List<Mesh> pieceMeshes,
        out string failureReason)
    {
        pieceMeshes = new List<Mesh>();
        failureReason = null;

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        enableDiagnostics = false;
#endif

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

        Vector3[] sourceVertices =
            sourceMesh.vertices;

        if (!TryGetSourceTriangleIndices(
                sourceMesh,
                sourceVertices.Length,
                out List<int[]> sourceTrianglesBySubMesh,
                out failureReason))
        {
            return false;
        }

        List<byte[]> triangleClassificationsBySubMesh =
            CreateTriangleClassificationBuffers(
                sourceTrianglesBySubMesh);

        var sourceVertexPlaneDistances =
            new float[sourceVertices.Length];

        float geometryEpsilon =
            MeshPlaneSplitter.CalculateGeometryEpsilon(sourceMesh.bounds);

        if (!TryCalculateCutRangeParameters(
                sourceVertices,
                sourceToCutRangeSpace,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                out Bounds cutRangeSpaceBounds,
                out float cutRangeMinimum,
                out float cutRangeMaximum,
                out float cutRangeGeometryEpsilon,
                out failureReason))
        {
            return false;
        }

        if (!TryCreateCutPlane(
                sourceVertices,
                sourceTrianglesBySubMesh,
                sourceVertexPlaneDistances,
                triangleClassificationsBySubMesh,
                localTipPositions,
                localRootPositions,
                cutDirectionSampleCount,
                sourceToCutRangeSpace,
                cutRangeAxis,
                cutRangeSpaceBounds,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                cutRangeMinimum,
                cutRangeMaximum,
                geometryEpsilon,
                cutRangeGeometryEpsilon,
                enableDiagnostics,
                out Plane correctedCutPlane,
                out failureReason))
        {
            return false;
        }

        if (!meshPlaneSplitter.TrySplitConnectedPieces(
                sourceMesh,
                correctedCutPlane,
                cutSurfaceUvScale,
                sourceVertices,
                sourceTrianglesBySubMesh,
                sourceVertexPlaneDistances,
                triangleClassificationsBySubMesh,
                "EnemyCut_FirstPiece",
                "EnemyCut_SecondPiece",
                cutBoundaryPoints =>
                {
                    return TryValidateFinalCutBoundaryWithinRange(
                            cutBoundaryPoints,
                            sourceToCutRangeSpace,
                            cutRangeAxis,
                            cutRangeSpaceBounds,
                            cutRangeMinimum,
                            cutRangeMaximum,
                            cutRangeGeometryEpsilon,
                            enableDiagnostics,
                            out string boundaryFailureReason)
                        ? null
                        : boundaryFailureReason;
                },
                enableDiagnostics,
                out pieceMeshes,
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

    private static bool TryGetSourceTriangleIndices(
        Mesh sourceMesh,
        int sourceVertexCount,
        out List<int[]> sourceTrianglesBySubMesh,
        out string failureReason)
    {
        sourceTrianglesBySubMesh =
            new List<int[]>(sourceMesh.subMeshCount);

        for (int subMeshIndex = 0;
             subMeshIndex < sourceMesh.subMeshCount;
             subMeshIndex++)
        {
            if (sourceMesh.GetTopology(subMeshIndex) !=
                MeshTopology.Triangles)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} がTriangles topologyではありません。";
                return false;
            }

            int[] triangles =
                sourceMesh.GetTriangles(subMeshIndex);

            if (triangles.Length % 3 != 0)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} のTriangle index数が不正です。";
                return false;
            }

            for (int triangleIndex = 0;
                 triangleIndex < triangles.Length;
                 triangleIndex++)
            {
                if (!IsValidVertexIndex(
                        triangles[triangleIndex],
                        sourceVertexCount))
                {
                    failureReason =
                        $"SubMesh {subMeshIndex} に範囲外の頂点indexがあります。";
                    return false;
                }
            }

            sourceTrianglesBySubMesh.Add(triangles);
        }

        failureReason = null;
        return true;
    }


    private static List<byte[]> CreateTriangleClassificationBuffers(
        IReadOnlyList<int[]> sourceTrianglesBySubMesh)
    {
        var result =
            new List<byte[]>(sourceTrianglesBySubMesh.Count);

        for (int subMeshIndex = 0;
             subMeshIndex < sourceTrianglesBySubMesh.Count;
             subMeshIndex++)
        {
            result.Add(
                new byte[
                    sourceTrianglesBySubMesh[subMeshIndex].Length / 3]);
        }

        return result;
    }


    private bool TryCreateCutPlane(
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<int[]> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        int cutDirectionSampleCount,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        Bounds cutRangeSpaceBounds,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        float cutRangeMinimum,
        float cutRangeMaximum,
        float geometryEpsilon,
        float cutRangeGeometryEpsilon,
        bool enableDiagnostics,
        out Plane correctedCutPlane,
        out string failureReason)
    {
        correctedCutPlane = default;

        string lastTrajectoryCandidateFailure = null;
        int sampleCount = localTipPositions.Count;

        // Plane候補ごとにListを生成せず、軌跡候補とFallback候補で同じ領域を再利用する。
        var intersectionPointBuffer =
            new List<Vector3>(3);

        // Bake済み軌跡の後半だけに固定せず、軌跡全体から順に候補を探す。
        // Cut Rangeは最終的な切断境界のハード制約なので、範囲外までMeshを横切るPlaneは採用しない。
        for (int endIndex = sampleCount - 1;
             endIndex >= 1;
             endIndex--)
        {
            int startIndex = Mathf.Max(
                0,
                endIndex - cutDirectionSampleCount + 1);

            if (!TryCreateTrajectoryPlaneCandidate(
                    localTipPositions,
                    localRootPositions,
                    startIndex,
                    endIndex,
                    geometryEpsilon,
                    out Vector3 planeNormal,
                    out Vector3 originalPlanePoint,
                    out string candidateFailureReason))
            {
                lastTrajectoryCandidateFailure =
                    candidateFailureReason;
                continue;
            }

            if (!TryClampPlanePointToCutRange(
                    originalPlanePoint,
                    sourceToCutRangeSpace,
                    cutRangeAxis,
                    cutRangeMinimum,
                    cutRangeMaximum,
                    out Vector3 correctedPlanePoint,
                    out Vector3 originalPointInCutRangeSpace,
                    out Vector3 correctedPointInCutRangeSpace,
                    out candidateFailureReason))
            {
                lastTrajectoryCandidateFailure =
                    candidateFailureReason;
                continue;
            }

            Plane candidatePlane = new Plane(
                planeNormal,
                correctedPlanePoint);

            if (!TryValidateCutPlaneWithinRange(
                    sourceVertices,
                    sourceTrianglesBySubMesh,
                    sourceVertexPlaneDistances,
                    triangleClassificationsBySubMesh,
                    candidatePlane,
                    sourceToCutRangeSpace,
                    cutRangeAxis,
                    cutRangeMinimum,
                    cutRangeMaximum,
                    geometryEpsilon,
                    cutRangeGeometryEpsilon,
                    intersectionPointBuffer,
                    out float intersectionMinimum,
                    out float intersectionMaximum,
                    out int intersectionSegmentCount,
                    out candidateFailureReason))
            {
                lastTrajectoryCandidateFailure =
                    candidateFailureReason;
                continue;
            }

            correctedCutPlane =
                candidatePlane;

            if (enableDiagnostics)
            {
                LogCutRangePlaneSelectionDiagnostic(
                    "Trajectory",
                    cutRangeAxis,
                    cutRangeSpaceBounds,
                    cutRangeMinimumNormalized,
                    cutRangeMaximumNormalized,
                    cutRangeMinimum,
                    cutRangeMaximum,
                    startIndex,
                    endIndex,
                    originalPointInCutRangeSpace,
                    correctedPointInCutRangeSpace,
                    intersectionMinimum,
                    intersectionMaximum,
                    intersectionSegmentCount,
                    cutRangeGeometryEpsilon);
            }

            failureReason = null;
            return true;
        }

        // 軌跡由来の角度ではCut Rangeを守れない場合、
        // 「範囲外を切らない」を最優先してCut Range Axisに直交するPlaneへフォールバックする。
        // 位置は軌跡サンプルから選ぶため、切断高さ/左右位置は可能な限り攻撃軌跡へ追従する。
        if (TryCreateAxisAlignedFallbackCutPlane(
                sourceVertices,
                sourceTrianglesBySubMesh,
                sourceVertexPlaneDistances,
                triangleClassificationsBySubMesh,
                localTipPositions,
                localRootPositions,
                sourceToCutRangeSpace,
                cutRangeAxis,
                cutRangeSpaceBounds,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                cutRangeMinimum,
                cutRangeMaximum,
                geometryEpsilon,
                cutRangeGeometryEpsilon,
                intersectionPointBuffer,
                enableDiagnostics,
                out correctedCutPlane,
                out failureReason))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(
                lastTrajectoryCandidateFailure))
        {
            failureReason =
                "Bake済み軌跡由来のCut Planeでは切断境界をCut Range内に収められず、" +
                "Axis直交Planeへのフォールバックにも失敗しました。" +
                $" 最後の軌跡候補: {lastTrajectoryCandidateFailure} / " +
                $"Fallback: {failureReason}";
        }

        return false;
    }

    private static bool TryCreateTrajectoryPlaneCandidate(
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        int startIndex,
        int endIndex,
        float geometryEpsilon,
        out Vector3 planeNormal,
        out Vector3 planePoint,
        out string failureReason)
    {
        planeNormal = default;
        planePoint = default;

        if (startIndex < 0 ||
            endIndex >= localTipPositions.Count ||
            startIndex >= endIndex)
        {
            failureReason =
                "Cut Plane候補の軌跡サンプル範囲が不正です。";
            return false;
        }

        Vector3 bladeDirectionSum =
            Vector3.zero;

        int validBladeDirectionCount =
            0;

        for (int i = startIndex;
             i <= endIndex;
             i++)
        {
            Vector3 bladeVector =
                localTipPositions[i] -
                localRootPositions[i];

            if (bladeVector.sqrMagnitude <=
                MIN_DIRECTION_SQR_MAGNITUDE)
            {
                continue;
            }

            bladeDirectionSum +=
                bladeVector.normalized;

            validBladeDirectionCount++;
        }

        if (validBladeDirectionCount == 0 ||
            bladeDirectionSum.sqrMagnitude <=
                MIN_DIRECTION_SQR_MAGNITUDE)
        {
            failureReason =
                "有効な代表刃方向を算出できませんでした。";
            return false;
        }

        Vector3 representativeBladeDirection =
            bladeDirectionSum.normalized;

        Vector3 movementSum =
            Vector3.zero;

        Vector3 previousCenter =
            GetBladeCenter(
                localTipPositions[startIndex],
                localRootPositions[startIndex]);

        for (int i = startIndex + 1;
             i <= endIndex;
             i++)
        {
            Vector3 currentCenter =
                GetBladeCenter(
                    localTipPositions[i],
                    localRootPositions[i]);

            movementSum +=
                currentCenter - previousCenter;

            previousCenter =
                currentCenter;
        }

        if (movementSum.sqrMagnitude <=
            geometryEpsilon * geometryEpsilon)
        {
            failureReason =
                "軌跡候補から有効なチェンソー移動方向を算出できませんでした。";
            return false;
        }

        Vector3 representativeMovementDirection =
            movementSum.normalized;

        planeNormal =
            Vector3.Cross(
                representativeBladeDirection,
                representativeMovementDirection);

        if (planeNormal.sqrMagnitude <=
            MIN_DIRECTION_SQR_MAGNITUDE)
        {
            planeNormal = default;
            failureReason =
                "刃方向と移動方向がほぼ平行なため、有効な切断Plane候補を算出できませんでした。";
            return false;
        }

        planeNormal.Normalize();

        planePoint =
            GetBladeCenter(
                localTipPositions[endIndex],
                localRootPositions[endIndex]);

        failureReason = null;
        return true;
    }

    private static Vector3 GetBladeCenter(
        Vector3 tipPosition,
        Vector3 rootPosition)
    {
        return (tipPosition + rootPosition) * 0.5f;
    }

    private static bool TryCalculateCutRangeParameters(
        IReadOnlyList<Vector3> sourceVertices,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        out Bounds cutRangeSpaceBounds,
        out float cutRangeMinimum,
        out float cutRangeMaximum,
        out float cutRangeGeometryEpsilon,
        out string failureReason)
    {
        cutRangeSpaceBounds = default;
        cutRangeMinimum = 0.0f;
        cutRangeMaximum = 0.0f;
        cutRangeGeometryEpsilon = 0.0f;

        if (!TryCalculateMeshBoundsInSpace(
                sourceVertices,
                sourceToCutRangeSpace,
                out cutRangeSpaceBounds))
        {
            failureReason =
                "Cut Range判定用のMesh Boundsを算出できませんでした。";
            return false;
        }

        float boundsMinimum =
            GetAxisValue(
                cutRangeSpaceBounds.min,
                cutRangeAxis);

        float boundsMaximum =
            GetAxisValue(
                cutRangeSpaceBounds.max,
                cutRangeAxis);

        float axisSize =
            boundsMaximum - boundsMinimum;

        cutRangeGeometryEpsilon =
            MeshPlaneSplitter.CalculateGeometryEpsilon(
                cutRangeSpaceBounds);

        if (axisSize <=
            cutRangeGeometryEpsilon)
        {
            failureReason =
                "Cut Range Axis方向のMesh Boundsサイズが小さすぎます。";
            return false;
        }

        cutRangeMinimum =
            Mathf.Lerp(
                boundsMinimum,
                boundsMaximum,
                cutRangeMinimumNormalized);

        cutRangeMaximum =
            Mathf.Lerp(
                boundsMinimum,
                boundsMaximum,
                cutRangeMaximumNormalized);

        if (cutRangeMaximum - cutRangeMinimum <=
            cutRangeGeometryEpsilon)
        {
            failureReason =
                "Cut Rangeの実寸幅が小さすぎます。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryClampPlanePointToCutRange(
        Vector3 originalPlanePoint,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimum,
        float cutRangeMaximum,
        out Vector3 correctedPlanePoint,
        out Vector3 originalPlanePointInCutRangeSpace,
        out Vector3 correctedPlanePointInCutRangeSpace,
        out string failureReason)
    {
        originalPlanePointInCutRangeSpace =
            sourceToCutRangeSpace.MultiplyPoint3x4(
                originalPlanePoint);

        correctedPlanePointInCutRangeSpace =
            originalPlanePointInCutRangeSpace;

        if (!IsFinite(
                originalPlanePointInCutRangeSpace))
        {
            correctedPlanePoint = default;
            failureReason =
                "軌跡位置をCut Range空間へ変換できませんでした。";
            return false;
        }

        float originalAxisPosition =
            GetAxisValue(
                originalPlanePointInCutRangeSpace,
                cutRangeAxis);

        SetAxisValue(
            ref correctedPlanePointInCutRangeSpace,
            cutRangeAxis,
            Mathf.Clamp(
                originalAxisPosition,
                cutRangeMinimum,
                cutRangeMaximum));

        Matrix4x4 cutRangeSpaceToSource =
            sourceToCutRangeSpace.inverse;

        correctedPlanePoint =
            cutRangeSpaceToSource.MultiplyPoint3x4(
                correctedPlanePointInCutRangeSpace);

        if (!IsFinite(
                correctedPlanePoint))
        {
            correctedPlanePoint = default;
            failureReason =
                "切断対象TransformのScaleに0が含まれるため、Cut Range座標を変換できません。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryValidateCutPlaneWithinRange(
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<int[]> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        Plane cutPlane,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        float cutRangeMinimum,
        float cutRangeMaximum,
        float geometryEpsilon,
        float cutRangeGeometryEpsilon,
        List<Vector3> intersectionPointBuffer,
        out float intersectionMinimum,
        out float intersectionMaximum,
        out int intersectionSegmentCount,
        out string failureReason)
    {
        intersectionMinimum =
            float.PositiveInfinity;

        intersectionMaximum =
            float.NegativeInfinity;

        intersectionSegmentCount =
            0;

        if (sourceVertexPlaneDistances == null ||
            sourceVertexPlaneDistances.Length != sourceVertices.Count ||
            triangleClassificationsBySubMesh == null ||
            triangleClassificationsBySubMesh.Count !=
                sourceTrianglesBySubMesh.Count ||
            intersectionPointBuffer == null)
        {
            failureReason =
                "Cut Plane検証結果の作業バッファ構成がSource Meshと一致していません。";
            return false;
        }

        float allowedMinimum =
            cutRangeMinimum -
            cutRangeGeometryEpsilon;

        float allowedMaximum =
            cutRangeMaximum +
            cutRangeGeometryEpsilon;

        // Triangleごとの配列やListは作らず、候補間で同じ作業領域を使い回す。
        // 採用候補については、この距離と分類結果をMeshPlaneSplitterへそのまま渡す。

        for (int subMeshIndex = 0;
             subMeshIndex < sourceTrianglesBySubMesh.Count;
             subMeshIndex++)
        {
            int[] triangles =
                sourceTrianglesBySubMesh[subMeshIndex];

            byte[] triangleClassifications =
                triangleClassificationsBySubMesh[subMeshIndex];

            if (triangleClassifications == null ||
                triangleClassifications.Length !=
                    triangles.Length / 3)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} のTriangle分類バッファ数が一致していません。";
                return false;
            }

            for (int triangleIndex = 0;
                 triangleIndex + 2 < triangles.Length;
                 triangleIndex += 3)
            {
                int indexA =
                    triangles[triangleIndex];

                int indexB =
                    triangles[triangleIndex + 1];

                int indexC =
                    triangles[triangleIndex + 2];

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

                if (!IsFinite(distanceA) ||
                    !IsFinite(distanceB) ||
                    !IsFinite(distanceC))
                {
                    failureReason =
                        "Cut Range検証中のPlane距離計算でNaN / Infinityを検出しました。";
                    return false;
                }

                // 同一頂点が複数Triangleに属していても値は同じなので、
                // 最後に書かれた値を採用候補の分割処理で再利用できる。
                // このSigned Distanceがあれば、交差Edge判定と
                // t = dA / (dA - dB) による交点補間率も同じ値から再現できる。
                sourceVertexPlaneDistances[indexA] =
                    distanceA;

                sourceVertexPlaneDistances[indexB] =
                    distanceB;

                sourceVertexPlaneDistances[indexC] =
                    distanceC;

                byte triangleClassification =
                    MeshPlaneSplitter.ClassifyTriangle(
                        distanceA,
                        distanceB,
                        distanceC,
                        geometryEpsilon);

                triangleClassifications[triangleIndex / 3] =
                    triangleClassification;

                if (triangleClassification !=
                    MeshPlaneSplitter.TRIANGLE_CLASSIFICATION_INTERSECTING)
                {
                    continue;
                }

                if (!TryGetTrianglePlaneIntersectionPoints(
                        positionA,
                        positionB,
                        positionC,
                        distanceA,
                        distanceB,
                        distanceC,
                        geometryEpsilon,
                        intersectionPointBuffer,
                        out failureReason))
                {
                    return false;
                }

                if (intersectionPointBuffer.Count < 2)
                {
                    continue;
                }

                intersectionSegmentCount++;

                for (int pointIndex = 0;
                     pointIndex < intersectionPointBuffer.Count;
                     pointIndex++)
                {
                    Vector3 pointInCutRangeSpace =
                        sourceToCutRangeSpace.MultiplyPoint3x4(
                            intersectionPointBuffer[pointIndex]);

                    if (!IsFinite(
                            pointInCutRangeSpace))
                    {
                        failureReason =
                            "切断境界点をCut Range空間へ変換できませんでした。";
                        return false;
                    }

                    float axisPosition =
                        GetAxisValue(
                            pointInCutRangeSpace,
                            cutRangeAxis);

                    intersectionMinimum =
                        Mathf.Min(
                            intersectionMinimum,
                            axisPosition);

                    intersectionMaximum =
                        Mathf.Max(
                            intersectionMaximum,
                            axisPosition);

                    // 一度でも許容範囲外へ出た切断境界は、残りTriangleを調べても
                    // 有効候補へ戻らないため、この候補の走査をここで終了する。
                    if (intersectionMinimum < allowedMinimum ||
                        intersectionMaximum > allowedMaximum)
                    {
                        failureReason =
                            "Cut Planeの実切断境界がCut Range外まで到達します。" +
                            $" Boundary=[{intersectionMinimum:F6}, {intersectionMaximum:F6}]," +
                            $" Allowed=[{cutRangeMinimum:F6}, {cutRangeMaximum:F6}]," +
                            $" Axis={cutRangeAxis}";
                        return false;
                    }
                }
            }
        }

        if (intersectionSegmentCount == 0)
        {
            failureReason =
                "Cut PlaneとMeshから有効な切断境界を取得できませんでした。";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryGetTrianglePlaneIntersectionPoints(
        Vector3 positionA,
        Vector3 positionB,
        Vector3 positionC,
        float distanceA,
        float distanceB,
        float distanceC,
        float geometryEpsilon,
        List<Vector3> intersectionPoints,
        out string failureReason)
    {
        intersectionPoints.Clear();

        if (!TryAddTrianglePlaneIntersectionPoint(
                positionA,
                positionB,
                distanceA,
                distanceB,
                geometryEpsilon,
                intersectionPoints,
                out failureReason) ||
            !TryAddTrianglePlaneIntersectionPoint(
                positionB,
                positionC,
                distanceB,
                distanceC,
                geometryEpsilon,
                intersectionPoints,
                out failureReason) ||
            !TryAddTrianglePlaneIntersectionPoint(
                positionC,
                positionA,
                distanceC,
                distanceA,
                geometryEpsilon,
                intersectionPoints,
                out failureReason))
        {
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryAddTrianglePlaneIntersectionPoint(
        Vector3 currentPosition,
        Vector3 nextPosition,
        float currentDistance,
        float nextDistance,
        float geometryEpsilon,
        List<Vector3> intersectionPoints,
        out string failureReason)
    {
        if (Mathf.Abs(currentDistance) <=
            geometryEpsilon)
        {
            AddDistinctIntersectionPoint(
                intersectionPoints,
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
            failureReason = null;
            return true;
        }

        float denominator =
            currentDistance - nextDistance;

        if (Mathf.Abs(denominator) <=
            Mathf.Epsilon)
        {
            failureReason =
                "TriangleとPlaneの交点補間に必要な距離差が0になりました。";
            return false;
        }

        float interpolation =
            Mathf.Clamp01(
                currentDistance /
                denominator);

        Vector3 intersectionPoint =
            Vector3.Lerp(
                currentPosition,
                nextPosition,
                interpolation);

        if (!IsFinite(
                intersectionPoint))
        {
            failureReason =
                "TriangleとPlaneの交点計算でNaN / Infinityを検出しました。";
            return false;
        }

        AddDistinctIntersectionPoint(
            intersectionPoints,
            intersectionPoint,
            geometryEpsilon);

        failureReason = null;
        return true;
    }

    private static void AddDistinctIntersectionPoint(
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

        points.Add(
            point);
    }

    private static bool TryCreateAxisAlignedFallbackCutPlane(
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<int[]> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        Bounds cutRangeSpaceBounds,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        float cutRangeMinimum,
        float cutRangeMaximum,
        float geometryEpsilon,
        float cutRangeGeometryEpsilon,
        List<Vector3> intersectionPointBuffer,
        bool enableDiagnostics,
        out Plane fallbackCutPlane,
        out string failureReason)
    {
        fallbackCutPlane = default;

        Matrix4x4 cutRangeSpaceToSource =
            sourceToCutRangeSpace.inverse;

        Vector3 cutRangeAxisNormal =
            GetAxisDirection(
                cutRangeAxis);

        var candidateAxisPositions =
            new List<float>(
                localTipPositions.Count + 1);

        for (int sampleIndex = localTipPositions.Count - 1;
             sampleIndex >= 0;
             sampleIndex--)
        {
            Vector3 centerInCutRangeSpace =
                sourceToCutRangeSpace.MultiplyPoint3x4(
                    GetBladeCenter(
                        localTipPositions[sampleIndex],
                        localRootPositions[sampleIndex]));

            if (!IsFinite(
                    centerInCutRangeSpace))
            {
                continue;
            }

            AddDistinctAxisPosition(
                candidateAxisPositions,
                ClampAxisPositionInsideRange(
                    GetAxisValue(
                        centerInCutRangeSpace,
                        cutRangeAxis),
                    cutRangeMinimum,
                    cutRangeMaximum,
                    cutRangeGeometryEpsilon),
                cutRangeGeometryEpsilon);
        }

        AddDistinctAxisPosition(
            candidateAxisPositions,
            (cutRangeMinimum + cutRangeMaximum) * 0.5f,
            cutRangeGeometryEpsilon);

        string lastFailureReason = null;

        for (int candidateIndex = 0;
             candidateIndex < candidateAxisPositions.Count;
             candidateIndex++)
        {
            float axisPosition =
                candidateAxisPositions[candidateIndex];

            Vector3 planePointInCutRangeSpace =
                cutRangeSpaceBounds.center;

            SetAxisValue(
                ref planePointInCutRangeSpace,
                cutRangeAxis,
                axisPosition);

            Plane planeInCutRangeSpace =
                new Plane(
                    cutRangeAxisNormal,
                    planePointInCutRangeSpace);

            Plane planeInSourceSpace =
                cutRangeSpaceToSource.TransformPlane(
                    planeInCutRangeSpace);

            if (!IsFinite(planeInSourceSpace.normal) ||
                planeInSourceSpace.normal.sqrMagnitude <=
                    MIN_DIRECTION_SQR_MAGNITUDE ||
                !IsFinite(planeInSourceSpace.distance))
            {
                lastFailureReason =
                    "Axis直交PlaneをSource空間へ変換できませんでした。";
                continue;
            }

            if (!TryValidateCutPlaneWithinRange(
                    sourceVertices,
                    sourceTrianglesBySubMesh,
                    sourceVertexPlaneDistances,
                    triangleClassificationsBySubMesh,
                    planeInSourceSpace,
                    sourceToCutRangeSpace,
                    cutRangeAxis,
                    cutRangeMinimum,
                    cutRangeMaximum,
                    geometryEpsilon,
                    cutRangeGeometryEpsilon,
                    intersectionPointBuffer,
                    out float intersectionMinimum,
                    out float intersectionMaximum,
                    out int intersectionSegmentCount,
                    out string candidateFailureReason))
            {
                lastFailureReason =
                    candidateFailureReason;
                continue;
            }

            fallbackCutPlane =
                planeInSourceSpace;

            if (enableDiagnostics)
            {
                LogCutRangePlaneSelectionDiagnostic(
                    "AxisAlignedFallback",
                    cutRangeAxis,
                    cutRangeSpaceBounds,
                    cutRangeMinimumNormalized,
                    cutRangeMaximumNormalized,
                    cutRangeMinimum,
                    cutRangeMaximum,
                    -1,
                    -1,
                    planePointInCutRangeSpace,
                    planePointInCutRangeSpace,
                    intersectionMinimum,
                    intersectionMaximum,
                    intersectionSegmentCount,
                    cutRangeGeometryEpsilon);
            }

            failureReason = null;
            return true;
        }

        failureReason =
            string.IsNullOrEmpty(lastFailureReason)
                ? "Cut Range内でMeshを2分割できるAxis直交Plane候補がありませんでした。"
                : lastFailureReason;

        return false;
    }

    private static float ClampAxisPositionInsideRange(
        float axisPosition,
        float cutRangeMinimum,
        float cutRangeMaximum,
        float cutRangeGeometryEpsilon)
    {
        float rangeWidth =
            cutRangeMaximum - cutRangeMinimum;

        float inset =
            Mathf.Min(
                cutRangeGeometryEpsilon * 2.0f,
                rangeWidth * 0.25f);

        float safeMinimum =
            cutRangeMinimum + inset;

        float safeMaximum =
            cutRangeMaximum - inset;

        if (safeMinimum >= safeMaximum)
        {
            return (cutRangeMinimum + cutRangeMaximum) * 0.5f;
        }

        return Mathf.Clamp(
            axisPosition,
            safeMinimum,
            safeMaximum);
    }

    private static void AddDistinctAxisPosition(
        List<float> axisPositions,
        float axisPosition,
        float cutRangeGeometryEpsilon)
    {
        for (int i = 0;
             i < axisPositions.Count;
             i++)
        {
            if (Mathf.Abs(
                    axisPositions[i] - axisPosition) <=
                cutRangeGeometryEpsilon)
            {
                return;
            }
        }

        axisPositions.Add(
            axisPosition);
    }

    private static Vector3 GetAxisDirection(
        MeshCutRangeAxis axis)
    {
        switch (axis)
        {
            case MeshCutRangeAxis.X:
                return Vector3.right;

            case MeshCutRangeAxis.Y:
                return Vector3.up;

            case MeshCutRangeAxis.Z:
                return Vector3.forward;

            default:
                return Vector3.up;
        }
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

        Vector3[] sourceVertices =
            sourceMesh.vertices;

        return TryCalculateMeshBoundsInSpace(
            sourceVertices,
            sourceToTargetSpace,
            out bounds);
    }

    private static bool TryCalculateMeshBoundsInSpace(
        IReadOnlyList<Vector3> sourceVertices,
        Matrix4x4 sourceToTargetSpace,
        out Bounds bounds)
    {
        bounds = default;

        if (sourceVertices == null ||
            sourceVertices.Count == 0)
        {
            return false;
        }

        Vector3 firstVertex =
            sourceToTargetSpace.MultiplyPoint3x4(
                sourceVertices[0]);

        if (!IsFinite(firstVertex))
        {
            return false;
        }

        bounds =
            new Bounds(
                firstVertex,
                Vector3.zero);

        for (int i = 1; i < sourceVertices.Count; i++)
        {
            Vector3 vertex =
                sourceToTargetSpace.MultiplyPoint3x4(
                    sourceVertices[i]);

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

    private static bool TryValidateFinalCutBoundaryWithinRange(
        IReadOnlyList<Vector3> cutBoundaryPoints,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        Bounds cutRangeSpaceBounds,
        float cutRangeMinimum,
        float cutRangeMaximum,
        float cutRangeGeometryEpsilon,
        bool enableDiagnostics,
        out string failureReason)
    {
        float boundaryMinimum =
            float.PositiveInfinity;

        float boundaryMaximum =
            float.NegativeInfinity;

        if (cutBoundaryPoints == null ||
            cutBoundaryPoints.Count == 0 ||
            cutBoundaryPoints.Count % 2 != 0)
        {
            failureReason =
                "最終切断境界点が取得できませんでした。";
            return false;
        }

        for (int pointIndex = 0;
             pointIndex < cutBoundaryPoints.Count;
             pointIndex++)
        {
            if (!TryExpandCutBoundaryAxisRange(
                    cutBoundaryPoints[pointIndex],
                    sourceToCutRangeSpace,
                    cutRangeAxis,
                    ref boundaryMinimum,
                    ref boundaryMaximum))
            {
                failureReason =
                    "最終CutSegmentをCut Range空間へ変換できませんでした。";
                return false;
            }
        }

        float allowedMinimum =
            cutRangeMinimum -
            cutRangeGeometryEpsilon;

        float allowedMaximum =
            cutRangeMaximum +
            cutRangeGeometryEpsilon;

        if (boundaryMinimum < allowedMinimum ||
            boundaryMaximum > allowedMaximum)
        {
            failureReason =
                "最終切断境界がCut Range外まで到達したため、Mesh生成を中止しました。" +
                $" Boundary=[{boundaryMinimum:F6}, {boundaryMaximum:F6}]," +
                $" Allowed=[{cutRangeMinimum:F6}, {cutRangeMaximum:F6}]," +
                $" Axis={cutRangeAxis}";
            return false;
        }

        if (enableDiagnostics)
        {
            Debug.Log(
                "[MeshCutBoundaryDiagnostic][FinalCutRangeValidation] " +
                $"Axis={cutRangeAxis}, " +
                $"MeshBounds=[{GetAxisValue(cutRangeSpaceBounds.min, cutRangeAxis):F6}, " +
                $"{GetAxisValue(cutRangeSpaceBounds.max, cutRangeAxis):F6}], " +
                $"AllowedRange=[{cutRangeMinimum:F6}, {cutRangeMaximum:F6}], " +
                $"BoundaryRange=[{boundaryMinimum:F6}, {boundaryMaximum:F6}], " +
                $"SegmentCount={cutBoundaryPoints.Count / 2}");
        }

        failureReason = null;
        return true;
    }

    private static bool TryExpandCutBoundaryAxisRange(
        Vector3 sourceSpacePoint,
        Matrix4x4 sourceToCutRangeSpace,
        MeshCutRangeAxis cutRangeAxis,
        ref float boundaryMinimum,
        ref float boundaryMaximum)
    {
        Vector3 pointInCutRangeSpace =
            sourceToCutRangeSpace.MultiplyPoint3x4(
                sourceSpacePoint);

        if (!IsFinite(pointInCutRangeSpace))
        {
            return false;
        }

        float axisPosition =
            GetAxisValue(
                pointInCutRangeSpace,
                cutRangeAxis);

        boundaryMinimum =
            Mathf.Min(
                boundaryMinimum,
                axisPosition);

        boundaryMaximum =
            Mathf.Max(
                boundaryMaximum,
                axisPosition);

        return true;
    }

    private static bool IsValidVertexIndex(
        int index,
        int vertexCount)
    {
        return index >= 0 &&
               index < vertexCount;
    }

    private static bool ArePositionsClose(
        Vector3 first,
        Vector3 second,
        float geometryEpsilon)
    {
        return (first - second).sqrMagnitude <=
               geometryEpsilon * geometryEpsilon;
    }

    /// <summary>
    /// 採用したCut Planeが、実切断境界まで含めてCut Range内に収まっていることを記録する。
    /// </summary>
    private static void LogCutRangePlaneSelectionDiagnostic(
        string selectionMode,
        MeshCutRangeAxis cutRangeAxis,
        Bounds cutRangeSpaceBounds,
        float cutRangeMinimumNormalized,
        float cutRangeMaximumNormalized,
        float cutRangeMinimum,
        float cutRangeMaximum,
        int trajectoryStartIndex,
        int trajectoryEndIndex,
        Vector3 originalPlanePointInCutRangeSpace,
        Vector3 correctedPlanePointInCutRangeSpace,
        float intersectionMinimum,
        float intersectionMaximum,
        int intersectionSegmentCount,
        float cutRangeGeometryEpsilon)
    {
        Debug.Log(
            "[MeshCutBoundaryDiagnostic][CutRangePlaneSelection] " +
            $"Mode={selectionMode}, " +
            $"Axis={cutRangeAxis}, " +
            $"MeshBounds=[{GetAxisValue(cutRangeSpaceBounds.min, cutRangeAxis):F6}, " +
            $"{GetAxisValue(cutRangeSpaceBounds.max, cutRangeAxis):F6}], " +
            $"NormalizedRange=[{cutRangeMinimumNormalized:F6}, {cutRangeMaximumNormalized:F6}], " +
            $"AllowedRange=[{cutRangeMinimum:F6}, {cutRangeMaximum:F6}], " +
            $"BoundaryRange=[{intersectionMinimum:F6}, {intersectionMaximum:F6}], " +
            $"BoundarySegmentCount={intersectionSegmentCount}, " +
            $"TrajectorySamples={trajectoryStartIndex}-{trajectoryEndIndex}, " +
            $"CutRangeGeometryEpsilon={cutRangeGeometryEpsilon:E6}\n" +
            $"OriginalPointCutRangeSpace={FormatDiagnosticVector3(originalPlanePointInCutRangeSpace)}\n" +
            $"CorrectedPointCutRangeSpace={FormatDiagnosticVector3(correctedPlanePointInCutRangeSpace)}");
    }

    private static string FormatDiagnosticVector3(
        Vector3 value)
    {
        return
            $"({value.x:F6}, {value.y:F6}, {value.z:F6})";
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
