using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// 食い込み死亡用Meshを、非連結部分を追加分離せず指定されたPiece数へ分割する。
/// </summary>
/// <remarks>
/// Triangle分割と切断面生成はMeshPlaneSplitterへ委譲する。
/// Planeの両側をそれぞれ1Pieceとし、非連結形状も同じMeshに保持する。
/// GameObject生成、物理設定、飛散、攻撃情報、通常攻撃用Cut Rangeは扱わない。
/// </remarks>
public sealed class MeshTargetCountSplitter
{
    private static readonly ProfilerMarker targetCountTotalProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.TargetCount.Total");




    private const int MIN_TARGET_PIECE_COUNT = 2;
    private const int SPLIT_POSITION_BIN_COUNT = 24;
    private const float MIN_SPLIT_POSITION_FRACTION = 0.15f;

    private readonly MeshPlaneSplitter meshPlaneSplitter =
        new MeshPlaneSplitter();

    // 面積分布の集計時、毎回Meshの頂点・Index配列を確保しないための作業領域。
    private readonly List<Vector3> vertexBuffer = new List<Vector3>();
    private readonly List<int> triangleIndexBuffer = new List<int>();
    private readonly float[] splitPositionWeights =
        new float[SPLIT_POSITION_BIN_COUNT];

    /// <summary>
    /// 入力Meshを、指定Piece数ちょうどになるまで繰り返し分割する。
    /// </summary>
    /// <remarks>
    /// 1回のPlane分割で2Pieceに増やし、非連結部分を別Meshにしない。
    /// 面積分布による切断位置はサイズ差を軽減する近似で、体積の等分は保証しない。
    /// 成功時に返すMeshはすべて新規生成Meshであり、呼び出し側が寿命を管理する。
    /// 途中で目標数まで分割できなくなった場合は、生成済みMeshを破棄してfalseを返す。
    /// </remarks>
    /// <param name="sourceMesh">分割対象。MeshPlaneSplitterが読み取れるRuntime Mesh。</param>
    /// <param name="targetPieceCount">到達を目指すPiece数。2以上を指定する。</param>
    /// <param name="cutSurfaceUvScale">各切断面へ生成するUVのスケール。0より大きい値を指定する。</param>
    public bool TrySplit(
        Mesh sourceMesh,
        int targetPieceCount,
        float cutSurfaceUvScale,
        out List<Mesh> pieceMeshes,
        out string failureReason)
    {
        using (targetCountTotalProfilerMarker.Auto())
        {
        pieceMeshes = new List<Mesh>();
        failureReason = null;

        if (!ValidateInputs(
                sourceMesh,
                targetPieceCount,
                cutSurfaceUvScale,
                out failureReason))
        {
            return false;
        }

        if (!TrySplitPiece(
                sourceMesh,
                cutSurfaceUvScale,
                out List<Mesh> initialPieces,
                out failureReason))
        {
            return false;
        }

        pieceMeshes.AddRange(initialPieces);

        var unsplittablePieces = new HashSet<Mesh>();
        string lastSplitFailureReason = null;

        while (pieceMeshes.Count < targetPieceCount)
        {
            int splitTargetIndex =
                FindLargestSplittablePieceIndex(
                    pieceMeshes,
                    unsplittablePieces);

            if (splitTargetIndex < 0)
            {
                DestroyMeshes(pieceMeshes);
                pieceMeshes.Clear();

                failureReason =
                    $"目標Piece数 {targetPieceCount} へ到達する前に、" +
                    $"追加分割できるPieceがなくなりました。" +
                    $" 最終分割失敗理由={lastSplitFailureReason ?? "取得できませんでした。"}";

                return false;
            }

            Mesh splitTarget =
                pieceMeshes[splitTargetIndex];

            if (!TrySplitPiece(
                    splitTarget,
                    cutSurfaceUvScale,
                    out List<Mesh> splitResults,
                    out string splitFailureReason))
            {
                unsplittablePieces.Add(splitTarget);
                lastSplitFailureReason = splitFailureReason;
                continue;
            }

            pieceMeshes.RemoveAt(splitTargetIndex);

            // current Piecesはこのクラスが生成したRuntime Meshのみで構成される。
            // 再分割後は置き換え前Meshを残す必要がないため、その場で破棄する。
            DestroyMesh(splitTarget);

            pieceMeshes.AddRange(splitResults);
        }

        failureReason = null;
        return true;

        }
    }

    private bool TrySplitPiece(
        Mesh sourceMesh,
        float cutSurfaceUvScale,
        out List<Mesh> splitPieceMeshes,
        out string failureReason)
    {
        splitPieceMeshes = new List<Mesh>(2);
        failureReason = null;

        int[] axisOrder =
            CreateAxisOrderByExtent(sourceMesh.bounds.size);
        string lastFailureReason = null;

        for (int axisOrderIndex = 0;
             axisOrderIndex < axisOrder.Length;
             axisOrderIndex++)
        {
            int axisIndex = axisOrder[axisOrderIndex];
            Plane balancedPlane = CreateBalancedSplitPlane(sourceMesh, axisIndex);
            Plane centerPlane = CreateCenterSplitPlane(sourceMesh.bounds, axisIndex);

            // 面積分布から求めた位置で切れなければ、従来のBounds中心を試す。
            for (int attemptIndex = 0; attemptIndex < 2; attemptIndex++)
            {
                if (attemptIndex == 1 &&
                    Mathf.Approximately(balancedPlane.distance, centerPlane.distance))
                {
                    continue;
                }

                Plane splitPlane = attemptIndex == 0 ? balancedPlane : centerPlane;
                if (meshPlaneSplitter.TrySplit(
                        sourceMesh,
                        splitPlane,
                        cutSurfaceUvScale,
                        out Mesh firstHalfMesh,
                        out Mesh secondHalfMesh,
                        out string planeSplitFailureReason))
                {
                    // 左右1Meshずつを保持し、つながっていない領域は追加分離しない。
                    splitPieceMeshes.Add(firstHalfMesh);
                    splitPieceMeshes.Add(secondHalfMesh);
                    return true;
                }

                DestroyMesh(firstHalfMesh);
                DestroyMesh(secondHalfMesh);
                lastFailureReason =
                    $"{GetAxisName(axisIndex)}軸Planeでの分割に失敗しました。" +
                    $" {planeSplitFailureReason}";
            }
        }

        failureReason =
            lastFailureReason ??
            "追加分割用Planeを決定できませんでした。";
        return false;
    }

    private int FindLargestSplittablePieceIndex(
        IReadOnlyList<Mesh> pieces,
        HashSet<Mesh> unsplittablePieces)
    {
        int largestPieceIndex = -1;
        float largestSurfaceArea = float.NegativeInfinity;
        float largestBoundsMagnitude = float.NegativeInfinity;

        for (int pieceIndex = 0;
             pieceIndex < pieces.Count;
             pieceIndex++)
        {
            Mesh piece = pieces[pieceIndex];

            if (piece == null ||
                unsplittablePieces.Contains(piece))
            {
                continue;
            }

            Vector3 boundsSize =
                piece.bounds.size;

            float surfaceArea = CalculateSurfaceArea(piece);

            float boundsMagnitude =
                boundsSize.sqrMagnitude;

            if (surfaceArea < largestSurfaceArea)
            {
                continue;
            }

            if (Mathf.Approximately(
                    surfaceArea,
                    largestSurfaceArea) &&
                boundsMagnitude <= largestBoundsMagnitude)
            {
                continue;
            }

            largestPieceIndex = pieceIndex;
            largestSurfaceArea = surfaceArea;
            largestBoundsMagnitude = boundsMagnitude;
        }

        return largestPieceIndex;

    }

    // 入力Meshが非閉鎖形状でも比較できるように、Bounds体積ではなく三角形表面積を用いる。
    private float CalculateSurfaceArea(Mesh mesh)
    {
        mesh.GetVertices(vertexBuffer);
        float totalArea = 0f;

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                continue;
            }

            mesh.GetTriangles(triangleIndexBuffer, subMeshIndex);
            for (int index = 0; index < triangleIndexBuffer.Count; index += 3)
            {
                Vector3 a = vertexBuffer[triangleIndexBuffer[index]];
                Vector3 b = vertexBuffer[triangleIndexBuffer[index + 1]];
                Vector3 c = vertexBuffer[triangleIndexBuffer[index + 2]];
                totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
        }

        return totalArea;
    }

    private Plane CreateBalancedSplitPlane(Mesh mesh, int axisIndex)
    {
        Bounds bounds = mesh.bounds;
        float minPosition = GetAxisValue(bounds.min, axisIndex);
        float extent = GetAxisValue(bounds.size, axisIndex);
        if (extent <= Mathf.Epsilon)
        {
            return CreateCenterSplitPlane(bounds, axisIndex);
        }

        System.Array.Clear(splitPositionWeights, 0, splitPositionWeights.Length);
        mesh.GetVertices(vertexBuffer);
        float totalWeight = 0f;

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                continue;
            }

            mesh.GetTriangles(triangleIndexBuffer, subMeshIndex);
            for (int index = 0; index < triangleIndexBuffer.Count; index += 3)
            {
                Vector3 a = vertexBuffer[triangleIndexBuffer[index]];
                Vector3 b = vertexBuffer[triangleIndexBuffer[index + 1]];
                Vector3 c = vertexBuffer[triangleIndexBuffer[index + 2]];
                float vertexWeight = Vector3.Cross(b - a, c - a).magnitude / 6f;
                if (vertexWeight <= 0f)
                {
                    continue;
                }

                // 各Triangleの面積を3頂点へ均等に配分した近似中央値を使う。
                AddPositionWeight(a, axisIndex, minPosition, extent, vertexWeight);
                AddPositionWeight(b, axisIndex, minPosition, extent, vertexWeight);
                AddPositionWeight(c, axisIndex, minPosition, extent, vertexWeight);
                totalWeight += vertexWeight * 3f;
            }
        }

        if (totalWeight <= Mathf.Epsilon)
        {
            return CreateCenterSplitPlane(bounds, axisIndex);
        }

        float halfWeight = totalWeight * 0.5f;
        float accumulatedWeight = 0f;
        for (int binIndex = 0; binIndex < SPLIT_POSITION_BIN_COUNT; binIndex++)
        {
            float binWeight = splitPositionWeights[binIndex];
            if (accumulatedWeight + binWeight < halfWeight)
            {
                accumulatedWeight += binWeight;
                continue;
            }

            float fractionWithinBin = binWeight > 0f
                ? (halfWeight - accumulatedWeight) / binWeight
                : 0.5f;
            float splitFraction =
                (binIndex + fractionWithinBin) / SPLIT_POSITION_BIN_COUNT;
            splitFraction = Mathf.Clamp(
                splitFraction,
                MIN_SPLIT_POSITION_FRACTION,
                1f - MIN_SPLIT_POSITION_FRACTION);

            float splitPosition = minPosition + extent * splitFraction;
            Vector3 normal = GetAxisNormal(axisIndex);
            Vector3 splitPoint = bounds.center +
                normal * (splitPosition - GetAxisValue(bounds.center, axisIndex));
            return new Plane(normal, splitPoint);
        }

        return CreateCenterSplitPlane(bounds, axisIndex);
    }

    private void AddPositionWeight(
        Vector3 position,
        int axisIndex,
        float minPosition,
        float extent,
        float weight)
    {
        float fraction =
            (GetAxisValue(position, axisIndex) - minPosition) / extent;
        int binIndex = Mathf.Clamp(
            (int)(fraction * SPLIT_POSITION_BIN_COUNT),
            0,
            SPLIT_POSITION_BIN_COUNT - 1);
        splitPositionWeights[binIndex] += weight;
    }

    private static Plane CreateCenterSplitPlane(
        Bounds bounds,
        int axisIndex)
    {
        return new Plane(
            GetAxisNormal(axisIndex),
            bounds.center);
    }

    private static int[] CreateAxisOrderByExtent(
        Vector3 boundsSize)
    {
        int[] axisOrder =
        {
            0,
            1,
            2
        };

        for (int currentIndex = 1;
             currentIndex < axisOrder.Length;
             currentIndex++)
        {
            int currentAxis =
                axisOrder[currentIndex];

            float currentExtent =
                GetAxisValue(
                    boundsSize,
                    currentAxis);

            int insertionIndex =
                currentIndex - 1;

            while (insertionIndex >= 0 &&
                   GetAxisValue(
                       boundsSize,
                       axisOrder[insertionIndex]) < currentExtent)
            {
                axisOrder[insertionIndex + 1] =
                    axisOrder[insertionIndex];

                insertionIndex--;
            }

            axisOrder[insertionIndex + 1] =
                currentAxis;
        }

        return axisOrder;
    }

    private static float GetAxisValue(
        Vector3 value,
        int axisIndex)
    {
        switch (axisIndex)
        {
            case 0:
                return value.x;

            case 1:
                return value.y;

            case 2:
                return value.z;

            default:
                return 0.0f;
        }
    }

    private static Vector3 GetAxisNormal(
        int axisIndex)
    {
        switch (axisIndex)
        {
            case 0:
                return Vector3.right;

            case 1:
                return Vector3.up;

            case 2:
                return Vector3.forward;

            default:
                return Vector3.right;
        }
    }

    private static string GetAxisName(
        int axisIndex)
    {
        switch (axisIndex)
        {
            case 0:
                return "X";

            case 1:
                return "Y";

            case 2:
                return "Z";

            default:
                return "Unknown";
        }
    }

    private static bool ValidateInputs(
        Mesh sourceMesh,
        int targetPieceCount,
        float cutSurfaceUvScale,
        out string failureReason)
    {
        if (sourceMesh == null)
        {
            failureReason =
                "分割対象Meshがnullです。";
            return false;
        }

        if (targetPieceCount < MIN_TARGET_PIECE_COUNT)
        {
            failureReason =
                $"targetPieceCountは{MIN_TARGET_PIECE_COUNT}以上である必要があります。";
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
            DestroyMesh(meshes[meshIndex]);
        }
    }

    private static void DestroyMesh(
        Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(mesh);
        }
        else
        {
            Object.DestroyImmediate(mesh);
        }
    }
}
