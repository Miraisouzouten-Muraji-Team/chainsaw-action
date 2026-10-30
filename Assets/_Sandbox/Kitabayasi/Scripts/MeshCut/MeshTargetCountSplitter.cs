using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Meshを目標Piece数へ近づけるため、共通のPlane分割処理を繰り返し実行する。
/// </summary>
/// <remarks>
/// Triangle分割と切断面生成はMeshPlaneSplitter、非連結部分の分離は
/// MeshConnectedComponentSplitterへ委譲する。
/// GameObject生成、物理設定、飛散、攻撃情報、通常攻撃用Cut Rangeは扱わない。
/// </remarks>
public sealed class MeshTargetCountSplitter
{
    private static readonly ProfilerMarker targetCountTotalProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.TargetCount.Total");




    private const int MIN_TARGET_PIECE_COUNT = 2;

    private readonly MeshPlaneSplitter meshPlaneSplitter =
        new MeshPlaneSplitter();

    private readonly MeshConnectedComponentSplitter connectedComponentSplitter =
        new MeshConnectedComponentSplitter();

    /// <summary>
    /// 入力Meshを、指定した目標Piece数以上になるまで繰り返し分割する。
    /// </summary>
    /// <remarks>
    /// 1回のPlane分割後に非連結部分も個別Meshへ分離するため、結果数は目標数を超える場合がある。
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
        splitPieceMeshes = new List<Mesh>();
        failureReason = null;

        int[] axisOrder =
            CreateAxisOrderByExtent(sourceMesh.bounds.size);

        string lastFailureReason = null;

        for (int axisOrderIndex = 0;
             axisOrderIndex < axisOrder.Length;
             axisOrderIndex++)
        {
            int axisIndex =
                axisOrder[axisOrderIndex];

            Plane splitPlane =
                CreateCenterSplitPlane(
                    sourceMesh.bounds,
                    axisIndex);

            if (!meshPlaneSplitter.TrySplit(
                    sourceMesh,
                    splitPlane,
                    cutSurfaceUvScale,
                    out Mesh firstHalfMesh,
                    out Mesh secondHalfMesh,
                    out string planeSplitFailureReason))
            {
                DestroyMesh(firstHalfMesh);
                DestroyMesh(secondHalfMesh);

                lastFailureReason =
                    $"{GetAxisName(axisIndex)}軸中心Planeでの分割に失敗しました。" +
                    $" {planeSplitFailureReason}";

                continue;
            }

            var candidatePieces = new List<Mesh>();
            bool completedCandidate = false;

            try
            {
                if (!TryAppendConnectedComponents(
                        firstHalfMesh,
                        candidatePieces,
                        out string firstHalfFailureReason))
                {
                    lastFailureReason =
                        $"{GetAxisName(axisIndex)}軸中心Planeの第1Pieceで" +
                        $"連結成分分離に失敗しました。 {firstHalfFailureReason}";

                    continue;
                }

                if (!TryAppendConnectedComponents(
                        secondHalfMesh,
                        candidatePieces,
                        out string secondHalfFailureReason))
                {
                    lastFailureReason =
                        $"{GetAxisName(axisIndex)}軸中心Planeの第2Pieceで" +
                        $"連結成分分離に失敗しました。 {secondHalfFailureReason}";

                    continue;
                }

                if (candidatePieces.Count < 2)
                {
                    lastFailureReason =
                        $"{GetAxisName(axisIndex)}軸中心Planeでは、" +
                        "2個以上の有効Pieceを生成できませんでした。";

                    continue;
                }

                splitPieceMeshes = candidatePieces;
                completedCandidate = true;
                failureReason = null;
                return true;
            }
            finally
            {
                DestroyMesh(firstHalfMesh);
                DestroyMesh(secondHalfMesh);

                if (!completedCandidate)
                {
                    DestroyMeshes(candidatePieces);
                }
            }
        }

        failureReason =
            lastFailureReason ??
            "追加分割用Planeを決定できませんでした。";

        return false;
    
    }

    private bool TryAppendConnectedComponents(
        Mesh sourceMesh,
        List<Mesh> destination,
        out string failureReason)
    {
        if (!connectedComponentSplitter.TrySplit(
                sourceMesh,
                out List<Mesh> componentMeshes,
                out failureReason))
        {
            return false;
        }

        destination.AddRange(componentMeshes);
        return true;
    
    }

    private static int FindLargestSplittablePieceIndex(
        IReadOnlyList<Mesh> pieces,
        HashSet<Mesh> unsplittablePieces)
    {
        int largestPieceIndex = -1;
        float largestApproximateVolume = float.NegativeInfinity;
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

            float approximateVolume =
                boundsSize.x *
                boundsSize.y *
                boundsSize.z;

            float boundsMagnitude =
                boundsSize.sqrMagnitude;

            if (approximateVolume < largestApproximateVolume)
            {
                continue;
            }

            if (Mathf.Approximately(
                    approximateVolume,
                    largestApproximateVolume) &&
                boundsMagnitude <= largestBoundsMagnitude)
            {
                continue;
            }

            largestPieceIndex = pieceIndex;
            largestApproximateVolume = approximateVolume;
            largestBoundsMagnitude = boundsMagnitude;
        }

        return largestPieceIndex;
    
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
