using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using WorkingPiece = MeshPlaneSplitter.PieceMeshBuilder;

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
    private static readonly ProfilerMarker buildFinalPiecesProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.TargetCount.BuildFinalMeshes");




    private static readonly ProfilerMarker splitPieceProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.Digging.TargetCount.TrySplitPiece");
    private static readonly ProfilerMarker chooseLargestPieceProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.Digging.TargetCount.ChooseLargestPiece");
    private static readonly ProfilerMarker balancedPlaneProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.Digging.TargetCount.CalculateBalancedPlane");

    private const int MIN_TARGET_PIECE_COUNT = 2;
    private const int SPLIT_POSITION_BIN_COUNT = 24;
    private const float MIN_SPLIT_POSITION_FRACTION = 0.15f;

    private readonly MeshPlaneSplitter meshPlaneSplitter =
        new MeshPlaneSplitter();

    // 面積分布は保持中のPieceデータを直接参照するため、入力配列をコピーしない。
    private readonly float[] splitPositionWeights =
        new float[SPLIT_POSITION_BIN_COUNT];
    private readonly int[] axisOrderBuffer = new int[3];

    // 反復分割の候補ごとに同じ三角形を毎回走査しないため、Piece順に表面積を保持する。
    // Split成功時のみ該当要素を差し替え、失敗時は次の候補を探す。
    private readonly List<float> pieceSurfaceAreas = new List<float>();
    private readonly HashSet<WorkingPiece> unsplittablePieces =
        new HashSet<WorkingPiece>();
    private readonly List<WorkingPiece> workingPieces = new List<WorkingPiece>(8);

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
                    sourceMesh, targetPieceCount, cutSurfaceUvScale,
                    out failureReason))
            {
                return false;
            }

            pieceMeshes.Capacity = Mathf.Min(targetPieceCount, 64);
            pieceSurfaceAreas.Clear();
            unsplittablePieces.Clear();

            // Builderの所有権はこのリストだけが保持する。Plane分割失敗時は元Pieceを残し、
            // 成功時は入力Pieceを返却して、2つの出力Pieceで置き換える。
            workingPieces.Clear();
            if (workingPieces.Capacity < targetPieceCount)
            {
                workingPieces.Capacity = targetPieceCount;
            }
            bool completed = false;

            try
            {
                if (!meshPlaneSplitter.TryAcquireTargetCountSource(
                        sourceMesh,
                        out WorkingPiece sourcePiece,
                        out failureReason))
                {
                    return false;
                }
                workingPieces.Add(sourcePiece);

                if (!TrySplitPiece(
                        sourcePiece,
                        cutSurfaceUvScale,
                        targetPieceCount > 2,
                        out WorkingPiece initialFirst,
                        out WorkingPiece initialSecond,
                        out float initialFirstArea,
                        out float initialSecondArea,
                        out failureReason))
                {
                    return false;
                }

                workingPieces.Clear();
                meshPlaneSplitter.ReleaseTargetCountPiece(sourcePiece);
                workingPieces.Add(initialFirst);
                workingPieces.Add(initialSecond);
                if (workingPieces.Count < targetPieceCount)
                {
                    pieceSurfaceAreas.Add(initialFirstArea);
                    pieceSurfaceAreas.Add(initialSecondArea);
                }

                string lastSplitFailureReason = null;
                while (workingPieces.Count < targetPieceCount)
                {
                    int splitTargetIndex = FindLargestSplittablePieceIndex(
                        workingPieces, pieceSurfaceAreas, unsplittablePieces);
                    if (splitTargetIndex < 0)
                    {
                        failureReason =
                            $"目標Piece数 {targetPieceCount} へ到達する前に、" +
                            "追加分割できるPieceがなくなりました。" +
                            $" 最終分割失敗理由={lastSplitFailureReason ?? "取得できませんでした。"}";
                        return false;
                    }

                    WorkingPiece splitTarget = workingPieces[splitTargetIndex];
                    if (!TrySplitPiece(
                            splitTarget,
                            cutSurfaceUvScale,
                            workingPieces.Count + 1 < targetPieceCount,
                            out WorkingPiece firstHalf,
                            out WorkingPiece secondHalf,
                            out float firstHalfArea,
                            out float secondHalfArea,
                            out string splitFailureReason))
                    {
                        unsplittablePieces.Add(splitTarget);
                        lastSplitFailureReason = splitFailureReason;
                        continue;
                    }

                    // RemoveAtで入力をリストから外してからPoolへ返し、解放後参照を防ぐ。
                    workingPieces.RemoveAt(splitTargetIndex);
                    pieceSurfaceAreas.RemoveAt(splitTargetIndex);
                    meshPlaneSplitter.ReleaseTargetCountPiece(splitTarget);
                    workingPieces.Add(firstHalf);
                    workingPieces.Add(secondHalf);
                    if (workingPieces.Count < targetPieceCount)
                    {
                        pieceSurfaceAreas.Add(firstHalfArea);
                        pieceSurfaceAreas.Add(secondHalfArea);
                    }
                }

                // ここまでUnity Meshを生成しない。最終結果のMesh化は総計測内でまとめる。
                using (buildFinalPiecesProfilerMarker.Auto())
                {
                    foreach (WorkingPiece piece in workingPieces)
                    {
                        Mesh mesh = piece.BuildMesh(piece.WorkingMeshName);
                        if (mesh == null)
                        {
                            failureReason = "最終PieceのMesh構築に失敗しました。";
                            return false;
                        }
                        pieceMeshes.Add(mesh);
                    }
                }

                completed = true;
                failureReason = null;
                return true;
            }
            finally
            {
                // 成功・失敗・例外を問わず、Builderはすべて返却する。
                // 出力Meshは成功時だけ呼び出し側へ所有権を渡す。
                foreach (WorkingPiece piece in workingPieces)
                {
                    meshPlaneSplitter.ReleaseTargetCountPiece(piece);
                }
                workingPieces.Clear();
                pieceSurfaceAreas.Clear();
                unsplittablePieces.Clear();
                if (!completed)
                {
                    DestroyMeshes(pieceMeshes);
                    pieceMeshes.Clear();
                }
            }
        }
    }

    private bool TrySplitPiece(
        WorkingPiece sourcePiece,
        float cutSurfaceUvScale,
        bool calculateSurfaceAreas,
        out WorkingPiece firstPiece,
        out WorkingPiece secondPiece,
        out float firstSurfaceArea,
        out float secondSurfaceArea,
        out string failureReason)
    {
        using var trySplitPieceProfileScope = splitPieceProfilerMarker.Auto();
        firstPiece = null;
        secondPiece = null;
        firstSurfaceArea = 0f;
        secondSurfaceArea = 0f;
        failureReason = null;

        int[] axisOrder = CreateAxisOrderByExtent(sourcePiece.BuilderBounds.size);
        string lastFailureReason = null;
        for (int axisOrderIndex = 0;
             axisOrderIndex < axisOrder.Length;
             axisOrderIndex++)
        {
            int axisIndex = axisOrder[axisOrderIndex];
            Plane balancedPlane = CreateBalancedSplitPlane(sourcePiece, axisIndex);
            Plane centerPlane = CreateCenterSplitPlane(
                sourcePiece.BuilderBounds, axisIndex);

            // 試行中も入力Builderを変更しないので、軸・位置の再試行では再取得不要。
            for (int attemptIndex = 0; attemptIndex < 2; attemptIndex++)
            {
                if (attemptIndex == 1 &&
                    Mathf.Approximately(balancedPlane.distance, centerPlane.distance))
                {
                    continue;
                }

                Plane splitPlane = attemptIndex == 0 ? balancedPlane : centerPlane;
                if (meshPlaneSplitter.TrySplitForTargetCount(
                        sourcePiece,
                        splitPlane,
                        cutSurfaceUvScale,
                        calculateSurfaceAreas,
                        out WorkingPiece firstHalf,
                        out WorkingPiece secondHalf,
                        out float firstHalfArea,
                        out float secondHalfArea,
                        out string planeSplitFailureReason))
                {
                    firstPiece = firstHalf;
                    secondPiece = secondHalf;
                    firstSurfaceArea = firstHalfArea;
                    secondSurfaceArea = secondHalfArea;
                    return true;
                }

                lastFailureReason =
                    $"{GetAxisName(axisIndex)}軸Planeでの分割に失敗しました。" +
                    $" {planeSplitFailureReason}";
            }
        }

        failureReason = lastFailureReason ?? "追加分割用Planeを決定できませんでした。";
        return false;
    }

    private int FindLargestSplittablePieceIndex(
        IReadOnlyList<WorkingPiece> pieces,
        IReadOnlyList<float> surfaceAreas,
        HashSet<WorkingPiece> unsplittablePieces)
    {
        using var findLargestSplittablePieceIndexProfileScope = chooseLargestPieceProfilerMarker.Auto();
        int largestPieceIndex = -1;
        float largestSurfaceArea = float.NegativeInfinity;
        float largestBoundsMagnitude = float.NegativeInfinity;

        for (int pieceIndex = 0;
             pieceIndex < pieces.Count;
             pieceIndex++)
        {
            WorkingPiece piece = pieces[pieceIndex];

            if (piece == null ||
                unsplittablePieces.Contains(piece))
            {
                continue;
            }

            Vector3 boundsSize =
                piece.BuilderBounds.size;

            float surfaceArea = surfaceAreas[pieceIndex];

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

    private Plane CreateBalancedSplitPlane(WorkingPiece piece, int axisIndex)
    {
        using var createBalancedSplitPlaneProfileScope = balancedPlaneProfilerMarker.Auto();
        Bounds bounds = piece.BuilderBounds;
        float minPosition = GetAxisValue(bounds.min, axisIndex);
        float extent = GetAxisValue(bounds.size, axisIndex);
        if (extent <= Mathf.Epsilon)
        {
            return CreateCenterSplitPlane(bounds, axisIndex);
        }

        System.Array.Clear(splitPositionWeights, 0, splitPositionWeights.Length);
        // 入力Meshから取得した頂点・Index、または直前の分割結果をそのまま参照。
        IReadOnlyList<Vector3> vertices = piece.Vertices;
        IReadOnlyList<List<int>> trianglesBySubMesh = piece.TrianglesBySubMesh;
        IReadOnlyList<List<float>> doubleAreasBySubMesh =
            piece.TriangleDoubleAreasBySubMesh;
        float totalWeight = 0f;

        for (int subMeshIndex = 0;
             subMeshIndex < trianglesBySubMesh.Count;
             subMeshIndex++)
        {
            IReadOnlyList<int> indices = trianglesBySubMesh[subMeshIndex];
            IReadOnlyList<float> doubleAreas = doubleAreasBySubMesh[subMeshIndex];
            for (int index = 0; index < indices.Count; index += 3)
            {
                Vector3 a = vertices[indices[index]];
                Vector3 b = vertices[indices[index + 1]];
                Vector3 c = vertices[indices[index + 2]];
                float vertexWeight = doubleAreas[index / 3] / 6f;
                if (vertexWeight <= 0f)
                {
                    continue;
                }

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

    private int[] CreateAxisOrderByExtent(
        Vector3 boundsSize)
    {
        // 各TrySplitPieceの同期処理内でのみ使用する3要素の作業配列。
        int[] axisOrder = axisOrderBuffer;
        axisOrder[0] = 0;
        axisOrder[1] = 1;
        axisOrder[2] = 2;

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
