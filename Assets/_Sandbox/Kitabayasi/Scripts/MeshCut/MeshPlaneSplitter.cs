using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;
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


        private static readonly ProfilerMarker buildFinalMeshProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.BuildFinalMesh");

        private static readonly ProfilerMarker createPieceMeshBuildersProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.SplitPlane.CreatePieceMeshBuilders");

    // CreatePieceMeshBuildersの内訳。Acquisitionの子にCtor/Prepareの区間を配置する。
    private static readonly ProfilerMarker acquireNewBuilderProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Acquire.New");
    private static readonly ProfilerMarker acquireReuseBuilderProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Acquire.Reuse");
    private static readonly ProfilerMarker createPositionsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Create.Positions");
    private static readonly ProfilerMarker createNormalsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Create.Normals");
    private static readonly ProfilerMarker createUvProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Create.UV");
    private static readonly ProfilerMarker createTangentsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Create.Tangents");
    private static readonly ProfilerMarker createConnectivityProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Create.Connectivity");
    private static readonly ProfilerMarker createSourceIndexMapProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Create.SourceIndexMap");
    private static readonly ProfilerMarker createSubMeshBuffersProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Create.SubMeshBuffers");
    private static readonly ProfilerMarker growPositionsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Prepare.GrowPositions");
    private static readonly ProfilerMarker growNormalsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Prepare.GrowNormals");
    private static readonly ProfilerMarker growUvProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Prepare.GrowUV");
    private static readonly ProfilerMarker growTangentsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Prepare.GrowTangents");
    private static readonly ProfilerMarker growConnectivityProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Prepare.GrowConnectivity");
    private static readonly ProfilerMarker growSourceIndexMapProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Prepare.GrowSourceIndexMap");
    private static readonly ProfilerMarker resetSubMeshesProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.Prepare.ResetSubMeshes");
    private static readonly ProfilerMarker createTriangleAreaListsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.CacheTriangleAreas.CreateLists");













    // 各マーカーは1つの分割試行またはまとまった工程を計測する。
    // Triangle/Edgeごとにサンプルを作ると計測負荷が大きいため、ホットループ内部には置かない。
    private static readonly ProfilerMarker acquireTargetSourceProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.Digging.Source.AcquireMeshData");
    private static readonly ProfilerMarker prepareSourceVerticesProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.PrepareSourceVertices");
    private static readonly ProfilerMarker processTriangleLoopProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.TriangleLoop");
    private static readonly ProfilerMarker triangleIndexReservationProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.TriangleLoop.ReserveOutputIndices");
    private static readonly ProfilerMarker outputVertexGrowthProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.GrowVertexAttributes");
    private static readonly ProfilerMarker outputIndexGrowthProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.GrowTriangleIndices");
    private static readonly ProfilerMarker triangleAreaGrowthProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.GrowTriangleAreas");
    private static readonly ProfilerMarker cutSegmentGrowthProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.TriangleLoop.GrowCutSegments");
    private static readonly ProfilerMarker warmupTriangleBuffersProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.WarmupTriangleBuffers");
    private static readonly ProfilerMarker buildBoundaryGraphProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Boundary.BuildGraph");
    private static readonly ProfilerMarker traceBoundaryComponentsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Boundary.TraceComponents");
    private static readonly ProfilerMarker prepareCapLoopProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.PrepareLoop");
    private static readonly ProfilerMarker validateCapPolygonProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.ValidatePolygon");
    private static readonly ProfilerMarker assignCapHierarchyProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.AssignLoopHierarchy");
    private static readonly ProfilerMarker sourceComponentsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.BuildSourceComponents");
    private static readonly ProfilerMarker boundarySheetsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.BuildLocalSheets");
    private static readonly ProfilerMarker mergeCapHoleProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.MergeHole");
    private static readonly ProfilerMarker searchCapBridgeProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.MergeHole.SearchBridge");
    private static readonly ProfilerMarker rebuildCapPolygonProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.MergeHole.RebuildPolygon");
    private static readonly ProfilerMarker triangulateCapProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.EarClipping");
    private static readonly ProfilerMarker findCapEarProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.EarClipping.FindEar");
    private static readonly ProfilerMarker removeCapEarVertexProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.EarClipping.RemoveVertex");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Profiling Coreパッケージへの依存を避け、同一Cap生成試行内の回数を集計する。
    // ホットループでは整数加算だけを行い、完了後にConsoleへ1行だけ出力する。
    private static int mergeHoleVisibilityChecksCounter;
    private static int mergeHoleEdgeVisitsCounter;
    private static int mergeHoleIntersectionTestsCounter;
    private static int mergeHoleInputVerticesCounter;
    private static int mergeHoleSiblingHolesCounter;
    private static int earClippingCandidatesCounter;
    private static int earClippingTriangleTestsCounter;
    private static int earClippingInputVerticesCounter;
    private static int earClippingRemovedVerticesCounter;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 8分割1回のCreatePieceMeshBuildersだけを対象にする。途中で文字列/配列を生成しない。
    private static bool targetCountBuilderDiagnosticsEnabled;
    private static bool captureCreatePieceBuilders;
    private static int newBuilderCount;
    private static int reusedBuilderCount;
    private static int newSourceIndexMapCount;
    private static int grownSourceIndexMapCount;
    private static int grownPositionCount;
    private static int grownNormalCount;
    private static int grownUvCount;
    private static int grownTangentCount;
    private static int grownConnectivityCount;
    private static int newSubMeshIndexListCount;
    private static int newTriangleAreaListCount;
    private static int releasedTrackedBuilderCount;
    private static int maximumUnusedVertexCapacity;
    private static int maximumPositionCapacity;
    private static int maximumVertexCount;
    private static long requestedVertexCapacitySum;
    private static long releasedVertexCountSum;
    private static long releasedVertexCapacitySum;
    private static long releasedNormalCapacitySum;
    private static long releasedUvCapacitySum;
    private static long releasedTangentCapacitySum;
    private static long releasedConnectivityCapacitySum;
    private static long releasedSourceIndexMapLengthSum;

    // 1回のTargetCount分割内での「Builder取得」ごとに保存する固定長テーブル。
    // 分割の計測区間内ではこの配列への整数書き込みだけを行う。
    // リトライが多い場合は記録を打ち切り、欠落件数を明示する。
    private const int MAX_BUILDER_CAPACITY_RECORDS = 96;
    private const int BUILDER_CAPACITY_RECORD_FIELD_COUNT = 29;
    private const int DIAGNOSTIC_BUILDER_ID = 0;
    private const int DIAGNOSTIC_WAS_NEW = 1;
    private const int DIAGNOSTIC_SOURCE_VERTEX_COUNT = 2;
    private const int DIAGNOSTIC_REQUESTED_CAPACITY = 3;
    private const int DIAGNOSTIC_POSITION_BEFORE = 4;
    private const int DIAGNOSTIC_NORMAL_BEFORE = 5;
    private const int DIAGNOSTIC_UV_BEFORE = 6;
    private const int DIAGNOSTIC_TANGENT_BEFORE = 7;
    private const int DIAGNOSTIC_POSITION_PREPARED = 8;
    private const int DIAGNOSTIC_NORMAL_PREPARED = 9;
    private const int DIAGNOSTIC_UV_PREPARED = 10;
    private const int DIAGNOSTIC_TANGENT_PREPARED = 11;
    private const int DIAGNOSTIC_INDEX_MAP_BEFORE = 12;
    private const int DIAGNOSTIC_INDEX_MAP_PREPARED = 13;
    private const int DIAGNOSTIC_FINAL_VERTEX_COUNT = 14;
    private const int DIAGNOSTIC_FINAL_POSITION_CAPACITY = 15;
    private const int DIAGNOSTIC_FINAL_NORMAL_CAPACITY = 16;
    private const int DIAGNOSTIC_FINAL_UV_CAPACITY = 17;
    private const int DIAGNOSTIC_FINAL_TANGENT_CAPACITY = 18;
    private const int DIAGNOSTIC_FINAL_INDEX_MAP_LENGTH = 19;
    private const int DIAGNOSTIC_POSITION_GROWTHS_DURING_BUILD = 20;
    private const int DIAGNOSTIC_NORMAL_GROWTHS_DURING_BUILD = 21;
    private const int DIAGNOSTIC_UV_GROWTHS_DURING_BUILD = 22;
    private const int DIAGNOSTIC_TANGENT_GROWTHS_DURING_BUILD = 23;
    private const int DIAGNOSTIC_TRIANGLE_INDEX_GROWTHS = 24;
    private const int DIAGNOSTIC_TRIANGLE_AREA_GROWTHS = 25;
    private const int DIAGNOSTIC_FINAL_TRIANGLE_INDEX_COUNT = 26;
    private const int DIAGNOSTIC_FINAL_TRIANGLE_INDEX_CAPACITY = 27;
    private const int DIAGNOSTIC_RELEASED = 28;

    private static readonly int[,] builderCapacityRecords =
        new int[MAX_BUILDER_CAPACITY_RECORDS, BUILDER_CAPACITY_RECORD_FIELD_COUNT];
    private static int builderCapacityRecordCount;
    private static int omittedBuilderCapacityRecordCount;
    private static int nextDiagnosticBuilderId;

    private static void RecordBuilderAcquisitionDetails(
        PieceMeshBuilder builder,
        bool isNew,
        int sourceVertexCount,
        int requestedCapacity,
        int positionCapacityBefore,
        int normalCapacityBefore,
        int uvCapacityBefore,
        int tangentCapacityBefore,
        int indexMapLengthBefore)
    {
        // この関数はCreatePieceMeshBuilders内の取得だけに呼ばれる。
        // Source Builder取得、通常2分割、プールのWarmupは対象にしない。
        if (builderCapacityRecordCount == MAX_BUILDER_CAPACITY_RECORDS)
        {
            omittedBuilderCapacityRecordCount++;
            builder.DiagnosticRecordIndex = -1;
            return;
        }

        int index = builderCapacityRecordCount++;
        builder.DiagnosticRecordIndex = index;
        builder.DiagnosticPositionGrowthCount = 0;
        builder.DiagnosticNormalGrowthCount = 0;
        builder.DiagnosticUvGrowthCount = 0;
        builder.DiagnosticTangentGrowthCount = 0;
        builder.DiagnosticTriangleIndexGrowthCount = 0;
        builder.DiagnosticTriangleAreaGrowthCount = 0;

        builderCapacityRecords[index, DIAGNOSTIC_BUILDER_ID] = builder.DiagnosticBuilderId;
        builderCapacityRecords[index, DIAGNOSTIC_WAS_NEW] = isNew ? 1 : 0;
        builderCapacityRecords[index, DIAGNOSTIC_SOURCE_VERTEX_COUNT] = sourceVertexCount;
        builderCapacityRecords[index, DIAGNOSTIC_REQUESTED_CAPACITY] = requestedCapacity;
        builderCapacityRecords[index, DIAGNOSTIC_POSITION_BEFORE] = positionCapacityBefore;
        builderCapacityRecords[index, DIAGNOSTIC_NORMAL_BEFORE] = normalCapacityBefore;
        builderCapacityRecords[index, DIAGNOSTIC_UV_BEFORE] = uvCapacityBefore;
        builderCapacityRecords[index, DIAGNOSTIC_TANGENT_BEFORE] = tangentCapacityBefore;
        builderCapacityRecords[index, DIAGNOSTIC_POSITION_PREPARED] = builder.Vertices.Capacity;
        builderCapacityRecords[index, DIAGNOSTIC_NORMAL_PREPARED] = builder.Normals.Capacity;
        builderCapacityRecords[index, DIAGNOSTIC_UV_PREPARED] = builder.Uv.Capacity;
        builderCapacityRecords[index, DIAGNOSTIC_TANGENT_PREPARED] = builder.Tangents.Capacity;
        builderCapacityRecords[index, DIAGNOSTIC_INDEX_MAP_BEFORE] = indexMapLengthBefore;
        builderCapacityRecords[index, DIAGNOSTIC_INDEX_MAP_PREPARED] = builder.SourceIndexMapLength;
        builderCapacityRecords[index, DIAGNOSTIC_RELEASED] = 0;
    }

    private static void LogIndividualBuilderCapacities()
    {
        if (builderCapacityRecordCount == 0)
        {
            return;
        }

        // StringBuilder・補間文字列・Debug.LogはTargetCount.Totalのスコープ終了後にのみ実行する。
        var message = new StringBuilder(4096);
        long estimatedNewBufferPayloadBytes = 0;
        for (int index = 0; index < builderCapacityRecordCount; index++)
        {
            if (index % 6 == 0)
            {
                if (message.Length != 0)
                {
                    Debug.Log(message.ToString());
                    message.Clear();
                }
                message.Append("[EnemyMeshCutBuilderCapacity] 取得順 / Builder別の詳細（CountとCapacityは要素数）\n");
            }

            int sourceCount = builderCapacityRecords[index, DIAGNOSTIC_SOURCE_VERTEX_COUNT];
            int requested = builderCapacityRecords[index, DIAGNOSTIC_REQUESTED_CAPACITY];
            int finalCount = builderCapacityRecords[index, DIAGNOSTIC_FINAL_VERTEX_COUNT];
            int finalCapacity = builderCapacityRecords[index, DIAGNOSTIC_FINAL_POSITION_CAPACITY];
            int unused = finalCapacity - finalCount;
            bool released = builderCapacityRecords[index, DIAGNOSTIC_RELEASED] != 0;
            bool newBuilder = builderCapacityRecords[index, DIAGNOSTIC_WAS_NEW] != 0;

            // Prepare時の新規配列だけを推定する。拡張時は差分でなく配列全体を計上。
            long estimatedBufferPayloadBytes = 0;
            if (builderCapacityRecords[index, DIAGNOSTIC_POSITION_PREPARED] >
                builderCapacityRecords[index, DIAGNOSTIC_POSITION_BEFORE])
            {
                estimatedBufferPayloadBytes +=
                    (long)builderCapacityRecords[index, DIAGNOSTIC_POSITION_PREPARED] * 12;
            }
            if (builderCapacityRecords[index, DIAGNOSTIC_NORMAL_PREPARED] >
                builderCapacityRecords[index, DIAGNOSTIC_NORMAL_BEFORE])
            {
                estimatedBufferPayloadBytes +=
                    (long)builderCapacityRecords[index, DIAGNOSTIC_NORMAL_PREPARED] * 12;
            }
            if (builderCapacityRecords[index, DIAGNOSTIC_UV_PREPARED] >
                builderCapacityRecords[index, DIAGNOSTIC_UV_BEFORE])
            {
                estimatedBufferPayloadBytes +=
                    (long)builderCapacityRecords[index, DIAGNOSTIC_UV_PREPARED] * 8;
            }
            if (builderCapacityRecords[index, DIAGNOSTIC_TANGENT_PREPARED] >
                builderCapacityRecords[index, DIAGNOSTIC_TANGENT_BEFORE])
            {
                estimatedBufferPayloadBytes +=
                    (long)builderCapacityRecords[index, DIAGNOSTIC_TANGENT_PREPARED] * 16;
            }
            if (builderCapacityRecords[index, DIAGNOSTIC_INDEX_MAP_PREPARED] >
                builderCapacityRecords[index, DIAGNOSTIC_INDEX_MAP_BEFORE])
            {
                estimatedBufferPayloadBytes +=
                    (long)builderCapacityRecords[index, DIAGNOSTIC_INDEX_MAP_PREPARED] * 4;
            }
            estimatedNewBufferPayloadBytes += estimatedBufferPayloadBytes;

            message.Append($"#{index + 1} BuilderId={builderCapacityRecords[index, DIAGNOSTIC_BUILDER_ID]} " +
                $"Mode={(newBuilder ? "New" : "Reuse")} SourceVertices={sourceCount} " +
                $"Requested={requested} FinalCount={(released ? finalCount.ToString() : "Unreleased")} " +
                $"Unused={(released ? unused.ToString() : "N/A")} " +
                $"Usage={(released && finalCapacity > 0 ? (double)finalCount * 100.0 / finalCapacity : 0.0):F1}% " +
                $"EstimatedPreparedBufferPayload={estimatedBufferPayloadBytes / 1024.0:F1}KiB\n");
            message.Append($"  Position={builderCapacityRecords[index, DIAGNOSTIC_POSITION_BEFORE]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_POSITION_PREPARED]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_FINAL_POSITION_CAPACITY]}, " +
                $"Normal={builderCapacityRecords[index, DIAGNOSTIC_NORMAL_BEFORE]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_NORMAL_PREPARED]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_FINAL_NORMAL_CAPACITY]}, " +
                $"UV={builderCapacityRecords[index, DIAGNOSTIC_UV_BEFORE]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_UV_PREPARED]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_FINAL_UV_CAPACITY]}, " +
                $"Tangent={builderCapacityRecords[index, DIAGNOSTIC_TANGENT_BEFORE]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_TANGENT_PREPARED]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_FINAL_TANGENT_CAPACITY]}\n");
            message.Append($"  SourceIndexMap={builderCapacityRecords[index, DIAGNOSTIC_INDEX_MAP_BEFORE]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_INDEX_MAP_PREPARED]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_FINAL_INDEX_MAP_LENGTH]}, " +
                $"BuildGrowths[P/N/U/T]={builderCapacityRecords[index, DIAGNOSTIC_POSITION_GROWTHS_DURING_BUILD]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_NORMAL_GROWTHS_DURING_BUILD]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_UV_GROWTHS_DURING_BUILD]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_TANGENT_GROWTHS_DURING_BUILD]}, " +
                $"IndexGrowths={builderCapacityRecords[index, DIAGNOSTIC_TRIANGLE_INDEX_GROWTHS]}, " +
                $"AreaGrowths={builderCapacityRecords[index, DIAGNOSTIC_TRIANGLE_AREA_GROWTHS]}, " +
                $"IndexCount/Capacity={builderCapacityRecords[index, DIAGNOSTIC_FINAL_TRIANGLE_INDEX_COUNT]}/" +
                $"{builderCapacityRecords[index, DIAGNOSTIC_FINAL_TRIANGLE_INDEX_CAPACITY]}\n");
        }

        if (message.Length != 0)
        {
            Debug.Log(message.ToString());
        }

        Debug.Log("[EnemyMeshCutBuilderCapacity][Summary] " +
            $"Recorded={builderCapacityRecordCount}, Omitted={omittedBuilderCapacityRecordCount}, " +
            $"EstimatedPreparedBufferPayload={estimatedNewBufferPayloadBytes / 1048576.0:F3}MiB. " +
            "頂点属性4種類とSourceIndexMapの新規Backing配列の要素データのみの推定値。" +
            "オブジェクト管理領域、SubMesh/面積List、TriangleLoop以降の配列拡張は含まない。" +
            "同じBuilderの複数回取得は別レコードとして扱う。");
    }

    internal static void BeginTargetCountBuilderDiagnostics()
    {
        newBuilderCount = 0;
        reusedBuilderCount = 0;
        newSourceIndexMapCount = 0;
        grownSourceIndexMapCount = 0;
        grownPositionCount = 0;
        grownNormalCount = 0;
        grownUvCount = 0;
        grownTangentCount = 0;
        grownConnectivityCount = 0;
        newSubMeshIndexListCount = 0;
        newTriangleAreaListCount = 0;
        releasedTrackedBuilderCount = 0;
        maximumUnusedVertexCapacity = 0;
        maximumPositionCapacity = 0;
        maximumVertexCount = 0;
        requestedVertexCapacitySum = 0;
        releasedVertexCountSum = 0;
        releasedVertexCapacitySum = 0;
        releasedNormalCapacitySum = 0;
        releasedUvCapacitySum = 0;
        releasedTangentCapacitySum = 0;
        releasedConnectivityCapacitySum = 0;
        releasedSourceIndexMapLengthSum = 0;
        captureCreatePieceBuilders = false;
        builderCapacityRecordCount = 0;
        omittedBuilderCapacityRecordCount = 0;
        targetCountBuilderDiagnosticsEnabled = true;
    }

    internal static void FlushTargetCountBuilderDiagnostics()
    {
        // 8分割全体のProfilerMarkerを抜けてからログを構築する。
        // Count/Capacityの合計はBuilder取得1回ごとの値。同じBuilderの再取得は別件として数える。
        targetCountBuilderDiagnosticsEnabled = false;
        captureCreatePieceBuilders = false;
        if (newBuilderCount == 0 && reusedBuilderCount == 0)
        {
            return;
        }

        long unusedPositionCapacitySum =
            releasedVertexCapacitySum - releasedVertexCountSum;
        double positionUsageRate = releasedVertexCapacitySum > 0
            ? (double)releasedVertexCountSum * 100.0 / releasedVertexCapacitySum
            : 0.0;

        Debug.Log(
            "[EnemyMeshCutBuilderDiagnostic]\n" +
            $"Frame={Time.frameCount}, " +
            $"Acquire.New={newBuilderCount}, Acquire.Reused={reusedBuilderCount}, " +
            $"Released={releasedTrackedBuilderCount}\n" +
            "[Capacity / 取得1回ごとの累計] " +
            $"RequestedVertexCapacitySum={requestedVertexCapacitySum}, " +
            $"ReleasedVertexCountSum={releasedVertexCountSum}, " +
            $"ReleasedPositionCapacitySum={releasedVertexCapacitySum}\n" +
            "[未使用Capacity / 取得1回ごとの累計] " +
            $"UnusedPositionCapacitySum={unusedPositionCapacitySum}, " +
            $"PositionUsageRate={positionUsageRate:F1}%, " +
            $"MaximumPositionCapacity={maximumPositionCapacity}, " +
            $"MaximumVertexCount={maximumVertexCount}, " +
            $"MaximumUnusedPositionCapacity={maximumUnusedVertexCapacity}\n" +
            "[属性別Capacity / 取得1回ごとの累計] " +
            $"Normal={releasedNormalCapacitySum}, UV={releasedUvCapacitySum}, " +
            $"Tangent={releasedTangentCapacitySum}, " +
            $"Connectivity={releasedConnectivityCapacitySum}, " +
            $"SourceIndexMapLength={releasedSourceIndexMapLengthSum}\n" +
            "[配列生成・拡張回数] " +
            $"SourceIndexMap.New={newSourceIndexMapCount}, " +
            $"SourceIndexMap.Grown={grownSourceIndexMapCount}, " +
            $"Position.Grown={grownPositionCount}, " +
            $"Normal.Grown={grownNormalCount}, " +
            $"UV.Grown={grownUvCount}, " +
            $"Tangent.Grown={grownTangentCount}, " +
            $"Connectivity.Grown={grownConnectivityCount}, " +
            $"SubMesh.NewIndexLists={newSubMeshIndexListCount}, " +
            $"TriangleArea.NewLists={newTriangleAreaListCount}");
        LogIndividualBuilderCapacities();
    }

    private static void RecordBuilderReleaseDiagnostics(PieceMeshBuilder builder)
    {
        if (!targetCountBuilderDiagnosticsEnabled || !builder.TrackCreateBuilderDiagnostics)
        {
            return;
        }

        // 各Builder取得1回分の最終値。再取得された同一Builderも別の試行として集計する。
        builder.TrackCreateBuilderDiagnostics = false;
        int recordIndex = builder.DiagnosticRecordIndex;
        builder.DiagnosticRecordIndex = -1;
        if (recordIndex >= 0 && recordIndex < builderCapacityRecordCount)
        {
            builderCapacityRecords[recordIndex, DIAGNOSTIC_FINAL_VERTEX_COUNT] = builder.Vertices.Count;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_FINAL_POSITION_CAPACITY] = builder.Vertices.Capacity;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_FINAL_NORMAL_CAPACITY] = builder.Normals.Capacity;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_FINAL_UV_CAPACITY] = builder.Uv.Capacity;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_FINAL_TANGENT_CAPACITY] = builder.Tangents.Capacity;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_FINAL_INDEX_MAP_LENGTH] = builder.SourceIndexMapLength;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_POSITION_GROWTHS_DURING_BUILD] = builder.DiagnosticPositionGrowthCount;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_NORMAL_GROWTHS_DURING_BUILD] = builder.DiagnosticNormalGrowthCount;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_UV_GROWTHS_DURING_BUILD] = builder.DiagnosticUvGrowthCount;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_TANGENT_GROWTHS_DURING_BUILD] = builder.DiagnosticTangentGrowthCount;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_TRIANGLE_INDEX_GROWTHS] = builder.DiagnosticTriangleIndexGrowthCount;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_TRIANGLE_AREA_GROWTHS] = builder.DiagnosticTriangleAreaGrowthCount;
            int finalTriangleIndexCount = 0;
            int finalTriangleIndexCapacity = 0;
            foreach (List<int> indices in builder.TrianglesBySubMesh)
            {
                finalTriangleIndexCount += indices.Count;
                finalTriangleIndexCapacity += indices.Capacity;
            }
            builderCapacityRecords[recordIndex, DIAGNOSTIC_FINAL_TRIANGLE_INDEX_COUNT] = finalTriangleIndexCount;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_FINAL_TRIANGLE_INDEX_CAPACITY] = finalTriangleIndexCapacity;
            builderCapacityRecords[recordIndex, DIAGNOSTIC_RELEASED] = 1;
        }
        releasedTrackedBuilderCount++;
        releasedVertexCountSum += builder.Vertices.Count;
        releasedVertexCapacitySum += builder.Vertices.Capacity;
        releasedNormalCapacitySum += builder.Normals.Capacity;
        releasedUvCapacitySum += builder.Uv.Capacity;
        releasedTangentCapacitySum += builder.Tangents.Capacity;
        releasedConnectivityCapacitySum += builder.ConnectivityVertexIds.Capacity;
        releasedSourceIndexMapLengthSum += builder.SourceIndexMapLength;
        maximumPositionCapacity = Mathf.Max(maximumPositionCapacity, builder.Vertices.Capacity);
        maximumVertexCount = Mathf.Max(maximumVertexCount, builder.Vertices.Count);
        maximumUnusedVertexCapacity = Mathf.Max(
            maximumUnusedVertexCapacity,
            builder.Vertices.Capacity - builder.Vertices.Count);
    }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 診断専用。falseで新規カウンタとログを無効化できる。Releaseビルドには含めない。
    private const bool ENABLE_TRIANGLE_LOOP_DIAGNOSTICS = true;
    private const int MAX_TRIANGLE_LOOP_GROWTH_RECORDS = 96;
    private const int LOOP_POSITION = 0;
    private const int LOOP_NORMAL = 1;
    private const int LOOP_UV = 2;
    private const int LOOP_TANGENT = 3;
    private const int LOOP_CONNECTIVITY = 4;
    private const int LOOP_INDEX = 5;
    private const int LOOP_AREA = 6;
    private const int LOOP_SEGMENT = 7;
    private const int LOOP_CLIPPED_POLYGON = 8;
    private const int LOOP_CUT_POINT = 9;
    private const int LOOP_CUT_POINT_INDEX = 10;
    private const int LOOP_BUFFER_KIND_COUNT = 11;

    // MeshTargetCountSplitterごとの呼出し先インスタンスに保持する。
    // List/文字列の生成はTriangleLoop内で行わず、記録上限を超えた件数は明示する。
    private readonly int[,] triangleLoopGrowthRecords =
        new int[MAX_TRIANGLE_LOOP_GROWTH_RECORDS, 5];
    private readonly int[] triangleLoopGrowthCounts = new int[LOOP_BUFFER_KIND_COUNT];
    private readonly int[] triangleLoopPeakCounts = new int[LOOP_BUFFER_KIND_COUNT];
    private readonly int[] triangleLoopPeakCapacities = new int[LOOP_BUFFER_KIND_COUNT];
    private readonly int[] triangleLoopPeakUnusedCapacities = new int[LOOP_BUFFER_KIND_COUNT];
    private readonly long[] triangleLoopEstimatedGrowthBytes = new long[LOOP_BUFFER_KIND_COUNT];
    private bool triangleLoopDiagnosticsActive;
    private bool triangleLoopDiagnosticsInsideLoop;
    private int triangleLoopDiagnosticRunNumber;
    private int triangleLoopDiagnosticFrame;
    private int triangleLoopGrowthRecordCount;
    private int triangleLoopOmittedGrowthRecords;
    private int triangleLoopSplitAttempts;
    private int triangleLoopInputSourceVertices;
    private int triangleLoopInputSourceIndices;
    private long triangleLoopAttemptInputVertices;
    private long triangleLoopAttemptInputIndices;
    private long triangleLoopAttemptOutputVertices;
    private long triangleLoopAttemptOutputIndices;
    private int triangleLoopPeakBuilderVertices;
    private int triangleLoopPeakBuilderVertexCapacity;
    private int triangleLoopPeakBuilderIndices;
    private int triangleLoopPeakBuilderIndexCapacity;
    private int triangleLoopAcquiredNewBuilders;
    private int triangleLoopAcquiredReusedBuilders;
    private int triangleLoopReleasedBuilders;
    private int triangleLoopFinalOutputVertices;
    private int triangleLoopFinalOutputIndices;
    private int triangleLoopFinalOutputPieces;
    private int triangleLoopTemporaryNewLists;
    private int triangleLoopTemporarySourceArrayPoolRents;
    private int triangleLoopTemporaryDistanceArrayPoolRents;
    private int triangleLoopDedicatedSourceBufferUses;

    internal void BeginTriangleLoopDiagnostics()
    {
        if (!ENABLE_TRIANGLE_LOOP_DIAGNOSTICS)
        {
            triangleLoopDiagnosticsActive = false;
            return;
        }

        triangleLoopDiagnosticsActive = true;
        triangleLoopDiagnosticsInsideLoop = false;
        triangleLoopDiagnosticRunNumber++;
        triangleLoopDiagnosticFrame = Time.frameCount;
        triangleLoopGrowthRecordCount = 0;
        triangleLoopOmittedGrowthRecords = 0;
        triangleLoopSplitAttempts = 0;
        triangleLoopInputSourceVertices = 0;
        triangleLoopInputSourceIndices = 0;
        triangleLoopAttemptInputVertices = 0;
        triangleLoopAttemptInputIndices = 0;
        triangleLoopAttemptOutputVertices = 0;
        triangleLoopAttemptOutputIndices = 0;
        triangleLoopPeakBuilderVertices = 0;
        triangleLoopPeakBuilderVertexCapacity = 0;
        triangleLoopPeakBuilderIndices = 0;
        triangleLoopPeakBuilderIndexCapacity = 0;
        triangleLoopAcquiredNewBuilders = 0;
        triangleLoopAcquiredReusedBuilders = 0;
        triangleLoopReleasedBuilders = 0;
        triangleLoopFinalOutputVertices = 0;
        triangleLoopFinalOutputIndices = 0;
        triangleLoopFinalOutputPieces = 0;
        triangleLoopTemporaryNewLists = 0;
        triangleLoopTemporarySourceArrayPoolRents = 0;
        triangleLoopTemporaryDistanceArrayPoolRents = 0;
        triangleLoopDedicatedSourceBufferUses = 0;
        Array.Clear(triangleLoopGrowthCounts, 0, triangleLoopGrowthCounts.Length);
        Array.Clear(triangleLoopPeakCounts, 0, triangleLoopPeakCounts.Length);
        Array.Clear(triangleLoopPeakCapacities, 0, triangleLoopPeakCapacities.Length);
        Array.Clear(triangleLoopPeakUnusedCapacities, 0, triangleLoopPeakUnusedCapacities.Length);
        Array.Clear(triangleLoopEstimatedGrowthBytes, 0, triangleLoopEstimatedGrowthBytes.Length);
    }

    internal void RecordFinalTargetCountOutput(IReadOnlyList<PieceMeshBuilder> pieces)
    {
        if (!triangleLoopDiagnosticsActive)
        {
            return;
        }
        triangleLoopFinalOutputPieces = pieces.Count;
        for (int pieceIndex = 0; pieceIndex < pieces.Count; pieceIndex++)
        {
            PieceMeshBuilder piece = pieces[pieceIndex];
            triangleLoopFinalOutputVertices += piece.Vertices.Count;
            foreach (List<int> indices in piece.TrianglesBySubMesh)
            {
                triangleLoopFinalOutputIndices += indices.Count;
            }
        }
    }

    private void RecordTriangleLoopSource(PieceMeshBuilder source)
    {
        if (!triangleLoopDiagnosticsActive)
        {
            return;
        }
        triangleLoopInputSourceVertices = source.Vertices.Count;
        foreach (List<int> indices in source.TrianglesBySubMesh)
        {
            triangleLoopInputSourceIndices += indices.Count;
        }
    }

    private void RecordTriangleLoopAcquisition(bool isNew)
    {
        if (!triangleLoopDiagnosticsActive)
        {
            return;
        }
        if (isNew) triangleLoopAcquiredNewBuilders++;
        else triangleLoopAcquiredReusedBuilders++;
    }

    private void RecordTriangleLoopRelease(PieceMeshBuilder builder)
    {
        if (!triangleLoopDiagnosticsActive)
        {
            return;
        }
        triangleLoopReleasedBuilders++;
        // Release前の使用量。Source・中間Piece・最終Pieceを別途重複加算しない。
        triangleLoopPeakBuilderVertices = Mathf.Max(triangleLoopPeakBuilderVertices, builder.Vertices.Count);
        triangleLoopPeakBuilderVertexCapacity = Mathf.Max(triangleLoopPeakBuilderVertexCapacity, builder.Vertices.Capacity);
        int indexCount = 0;
        int indexCapacity = 0;
        foreach (List<int> indices in builder.TrianglesBySubMesh)
        {
            indexCount += indices.Count;
            indexCapacity += indices.Capacity;
        }
        triangleLoopPeakBuilderIndices = Mathf.Max(triangleLoopPeakBuilderIndices, indexCount);
        triangleLoopPeakBuilderIndexCapacity = Mathf.Max(triangleLoopPeakBuilderIndexCapacity, indexCapacity);
    }

    private void RecordTriangleLoopBegin(PieceMeshBuilder source)
    {
        if (!triangleLoopDiagnosticsActive)
        {
            return;
        }
        triangleLoopSplitAttempts++;
        triangleLoopAttemptInputVertices += source.Vertices.Count;
        foreach (List<int> indices in source.TrianglesBySubMesh)
        {
            triangleLoopAttemptInputIndices += indices.Count;
        }
        triangleLoopDiagnosticsInsideLoop = true;
    }

    private void RecordTriangleLoopEnd(
        PieceMeshBuilder first, PieceMeshBuilder second,
        List<CutSegment> segments, List<VertexData> polygon,
        List<Vector3> cutPoints, List<int> pointA, List<int> pointB, List<int> pointIds)
    {
        if (!triangleLoopDiagnosticsActive)
        {
            return;
        }
        triangleLoopDiagnosticsInsideLoop = false;
        RecordTriangleLoopBuilderUsage(first);
        RecordTriangleLoopBuilderUsage(second);
        RecordTriangleLoopBufferUsage(LOOP_SEGMENT, segments.Count, segments.Capacity);
        RecordTriangleLoopBufferUsage(LOOP_CLIPPED_POLYGON, polygon.Count, polygon.Capacity);
        RecordTriangleLoopBufferUsage(LOOP_CUT_POINT, cutPoints.Count, cutPoints.Capacity);
        RecordTriangleLoopBufferUsage(LOOP_CUT_POINT_INDEX, pointA.Count, pointA.Capacity);
        RecordTriangleLoopBufferUsage(LOOP_CUT_POINT_INDEX, pointB.Count, pointB.Capacity);
        RecordTriangleLoopBufferUsage(LOOP_CUT_POINT_INDEX, pointIds.Count, pointIds.Capacity);
    }

    private void RecordTriangleLoopBuilderUsage(PieceMeshBuilder builder)
    {
        triangleLoopAttemptOutputVertices += builder.Vertices.Count;
        RecordTriangleLoopBufferUsage(LOOP_POSITION, builder.Vertices.Count, builder.Vertices.Capacity);
        RecordTriangleLoopBufferUsage(LOOP_NORMAL, builder.Normals.Count, builder.Normals.Capacity);
        RecordTriangleLoopBufferUsage(LOOP_UV, builder.Uv.Count, builder.Uv.Capacity);
        if (builder.HasTangents)
        {
            RecordTriangleLoopBufferUsage(LOOP_TANGENT, builder.Tangents.Count, builder.Tangents.Capacity);
        }
        foreach (List<int> indices in builder.TrianglesBySubMesh)
        {
            triangleLoopAttemptOutputIndices += indices.Count;
            RecordTriangleLoopBufferUsage(LOOP_INDEX, indices.Count, indices.Capacity);
        }
        foreach (List<float> areas in builder.TriangleDoubleAreasBySubMesh)
        {
            RecordTriangleLoopBufferUsage(LOOP_AREA, areas.Count, areas.Capacity);
        }
    }

    private void RecordTriangleLoopBufferUsage(int kind, int count, int capacity)
    {
        triangleLoopPeakCounts[kind] = Mathf.Max(triangleLoopPeakCounts[kind], count);
        triangleLoopPeakCapacities[kind] = Mathf.Max(triangleLoopPeakCapacities[kind], capacity);
        // Count/Capacityを同じ観測点で比較。異なるBuilderの最大値同士を引かない。
        triangleLoopPeakUnusedCapacities[kind] = Mathf.Max(
            triangleLoopPeakUnusedCapacities[kind], capacity - count);
    }

    private void RecordTriangleLoopBufferGrowth(int kind, int before, int after, int builderId = 0)
    {
        if (!triangleLoopDiagnosticsActive || !triangleLoopDiagnosticsInsideLoop || after <= before)
        {
            return;
        }
        triangleLoopGrowthCounts[kind]++;
        int bytesPerElement = GetTriangleLoopElementSize(kind);
        if (bytesPerElement > 0)
        {
            // 新Backing Arrayのペイロード全体。差分ではなく新Capacity分を計上する。
            triangleLoopEstimatedGrowthBytes[kind] += (long)after * bytesPerElement;
        }
        triangleLoopPeakCapacities[kind] = Mathf.Max(triangleLoopPeakCapacities[kind], after);
        if (triangleLoopGrowthRecordCount < MAX_TRIANGLE_LOOP_GROWTH_RECORDS)
        {
            int index = triangleLoopGrowthRecordCount++;
            triangleLoopGrowthRecords[index, 0] = triangleLoopSplitAttempts;
            triangleLoopGrowthRecords[index, 1] = builderId;
            triangleLoopGrowthRecords[index, 2] = kind;
            triangleLoopGrowthRecords[index, 3] = before;
            triangleLoopGrowthRecords[index, 4] = after;
        }
        else
        {
            triangleLoopOmittedGrowthRecords++;
        }
    }

    private static int GetTriangleLoopElementSize(int kind)
    {
        switch (kind)
        {
            case LOOP_POSITION:
            case LOOP_NORMAL: return 12; // Vector3
            case LOOP_UV: return 8;       // Vector2
            case LOOP_TANGENT: return 16; // Vector4
            case LOOP_CONNECTIVITY:
            case LOOP_INDEX:
            case LOOP_CUT_POINT_INDEX:
            case LOOP_AREA: return 4;
            case LOOP_CUT_POINT: return 12;
            // CutSegment/VertexDataはレイアウトに依存するため概算に算入しない。
            default: return 0;
        }
    }

    private static string GetTriangleLoopBufferName(int kind)
    {
        switch (kind)
        {
            case LOOP_POSITION: return "Positions";
            case LOOP_NORMAL: return "Normals";
            case LOOP_UV: return "UV";
            case LOOP_TANGENT: return "Tangents";
            case LOOP_CONNECTIVITY: return "Connectivity";
            case LOOP_INDEX: return "TriangleIndices";
            case LOOP_AREA: return "TriangleAreas";
            case LOOP_SEGMENT: return "CutSegments";
            case LOOP_CLIPPED_POLYGON: return "ClippedPolygon";
            case LOOP_CUT_POINT: return "CutPoints";
            case LOOP_CUT_POINT_INDEX: return "CutPointIndices";
            default: return "Unknown";
        }
    }

    internal void FlushTriangleLoopDiagnostics()
    {
        if (!triangleLoopDiagnosticsActive)
        {
            return;
        }
        triangleLoopDiagnosticsInsideLoop = false;
        triangleLoopDiagnosticsActive = false;

        // このメソッドはTargetCount.Totalを抜けてからだけ呼ぶ。
        // ここでの文字列割り当てはTriangleLoopのGC Allocへ混入しない。
        var message = new StringBuilder(4096);
        message.Append("[EnemyMeshCutTriangleLoopDiagnostic] ")
            .Append("Frame=").Append(triangleLoopDiagnosticFrame)
            .Append(" Run=").Append(triangleLoopDiagnosticRunNumber)
            .Append(" Attempts=").Append(triangleLoopSplitAttempts)
            .Append(" (Run=1は同一Splitterの初回。事前Pool Warmupとは別)\n")
            .Append("Source V/I=").Append(triangleLoopInputSourceVertices).Append('/')
            .Append(triangleLoopInputSourceIndices)
            .Append(" | Attempts input V/I=").Append(triangleLoopAttemptInputVertices).Append('/')
            .Append(triangleLoopAttemptInputIndices)
            .Append(" | Loop output V/I(sum of attempts)=").Append(triangleLoopAttemptOutputVertices).Append('/')
            .Append(triangleLoopAttemptOutputIndices)
            .Append(" | Final output pieces/V/I=").Append(triangleLoopFinalOutputPieces).Append('/')
            .Append(triangleLoopFinalOutputVertices).Append('/').Append(triangleLoopFinalOutputIndices)
            .Append('\n')
            .Append("Builder acquired New/Reuse/Released=")
            .Append(triangleLoopAcquiredNewBuilders).Append('/')
            .Append(triangleLoopAcquiredReusedBuilders).Append('/')
            .Append(triangleLoopReleasedBuilders)
            .Append(" | Peak Builder V Count/Capacity(independent)=")
            .Append(triangleLoopPeakBuilderVertices).Append('/')
            .Append(triangleLoopPeakBuilderVertexCapacity)
            .Append(" | Peak Builder Index Count/Capacity(independent)=")
            .Append(triangleLoopPeakBuilderIndices).Append('/')
            .Append(triangleLoopPeakBuilderIndexCapacity).Append('\n');

        long estimatedBytes = 0;
        for (int kind = 0; kind < LOOP_BUFFER_KIND_COUNT; kind++)
        {
            estimatedBytes += triangleLoopEstimatedGrowthBytes[kind];
            message.Append(GetTriangleLoopBufferName(kind))
                .Append(" Growths=").Append(triangleLoopGrowthCounts[kind])
                .Append(" PeakCount/Capacity(independent)=").Append(triangleLoopPeakCounts[kind]).Append('/')
                .Append(triangleLoopPeakCapacities[kind])
                .Append(" MaxObservedUnusedCapacity=").Append(triangleLoopPeakUnusedCapacities[kind])
                .Append(" EstimatedNewArrayPayloadBytes=")
                .Append(triangleLoopEstimatedGrowthBytes[kind]).Append('\n');
        }
        message.Append("Temporary Lists new (pre-loop)=").Append(triangleLoopTemporaryNewLists)
            .Append(" | Source VertexData dedicated uses=").Append(triangleLoopDedicatedSourceBufferUses)
            .Append(" | Source VertexData ArrayPool rents=").Append(triangleLoopTemporarySourceArrayPoolRents)
            .Append(" | float distance ArrayPool rents=").Append(triangleLoopTemporaryDistanceArrayPoolRents)
            .Append(" (Rentが新規割当か既存再利用かはこのカウンタでは判定不可)\n")
            .Append("Loop array payload estimate bytes=").Append(estimatedBytes)
            .Append(" (Managed GC Alloc実測値ではない。配列ヘッダ/CutSegments等は除外)\n")
            .Append("Growth detail [Attempt BuilderId Buffer Before->After; BuilderId=0 is shared temporary buffer] Recorded=")
            .Append(triangleLoopGrowthRecordCount)
            .Append(" Omitted=").Append(triangleLoopOmittedGrowthRecords).Append('\n');
        for (int index = 0; index < triangleLoopGrowthRecordCount; index++)
        {
            message.Append('#').Append(index + 1).Append(" A")
                .Append(triangleLoopGrowthRecords[index, 0]).Append(" B")
                .Append(triangleLoopGrowthRecords[index, 1]).Append(':')
                .Append(GetTriangleLoopBufferName(triangleLoopGrowthRecords[index, 2]))
                .Append(' ').Append(triangleLoopGrowthRecords[index, 3])
                .Append("->").Append(triangleLoopGrowthRecords[index, 4]).Append('\n');
        }
        Debug.Log(message.ToString());
    }
#endif

    private static readonly ProfilerMarker emitCapTrianglesProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.EmitTriangles");
    private static readonly ProfilerMarker overlapCapBoundaryProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.CheckOpenBoundaryOverlap");
    private static readonly ProfilerMarker simplifyCapLoopProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Cap.RemoveRedundantPoints");
    private static readonly ProfilerMarker cacheTriangleAreasProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.CacheTriangleAreas");
    private static readonly ProfilerMarker calculateSurfaceAreasProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.SplitPlane.Builder.CalculateSurfaceArea");


    private const float GEOMETRY_EPSILON_RATIO = 0.00001f;
    private const float MIN_GEOMETRY_EPSILON = 0.000001f;
    private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.00000001f;

    internal const byte TRIANGLE_CLASSIFICATION_POSITIVE = 1;
    internal const byte TRIANGLE_CLASSIFICATION_NEGATIVE = 2;
    internal const byte TRIANGLE_CLASSIFICATION_INTERSECTING = 3;

    private const byte PLANE_TOUCHING_EDGE_POSITIVE_SIDE_MASK = 1;
    private const byte PLANE_TOUCHING_EDGE_NEGATIVE_SIDE_MASK = 2;
    private const byte PLANE_TOUCHING_EDGE_BOTH_SIDES_MASK =
        PLANE_TOUCHING_EDGE_POSITIVE_SIDE_MASK |
        PLANE_TOUCHING_EDGE_NEGATIVE_SIDE_MASK;

    private const int MAX_BOUNDARY_DIAGNOSTIC_NODE_DETAILS = 20;
    private const int MAX_DEGENERATE_SEGMENT_DIAGNOSTIC_DETAILS = 30;
    private const int MAX_NEARBY_ENDPOINT_DIAGNOSTIC_DETAILS = 20;
    private const int MAX_SOURCE_TOPOLOGY_DIAGNOSTIC_TRIANGLES = 20;
    private const float NEARBY_BOUNDARY_ENDPOINT_SEARCH_SCALE = 4.0f;
    private const float SOURCE_SPLIT_VERTEX_MATCH_EPSILON_SCALE = 0.1f;
    // 小規模境界ではLookup構築より線形探索が安価なため、ノード数が増えてから索引を作る。
    private const int BOUNDARY_SPATIAL_LOOKUP_MIN_NODE_COUNT = 64;
    // 浮動小数点の差分・二乗距離の丸めによる境界候補の欠落を防ぐための保守的な余裕。
    private const double BOUNDARY_SPATIAL_SEARCH_EPSILON_SCALE = 1.000001d;

    // 現在のProfiler結果と既知のSource Vertex数（約30,002）を収容する。
    // 通常のEnemy死亡ではこの専用Bufferを直接再利用し、ArrayPoolへ所有権を渡さない。
    private const int SOURCE_VERTEX_DATA_BUFFER_LENGTH = 32768;

    // MeshのVertex属性を配列Propertyで取得せず、Capacityを保持したListへ直接読み込むための調整値。
    // 通常攻撃はVertex位置をChainsawMeshCutProcessorから受け取るため、主にNormal / UV / Tangentで使用する。
    private const int SOURCE_MESH_ATTRIBUTE_LIST_WARMUP_CAPACITY = 32768;

    // Connectivity IDは通常攻撃2分割でSource Vertex index範囲を基準に生成されるため、
    // Boundary Node lookupも同規模まで事前確保して死亡時の初回Allocationを避ける。
    private const int BOUNDARY_CONNECTIVITY_LOOKUP_BUFFER_LENGTH = 32768;

    private const int NORMAL_SPLIT_OUTPUT_COUNT = 2;
    private const int NORMAL_SPLIT_INPUT_INDEX_WARMUP_CAPACITY = 124482;
    private const int CUT_SEGMENT_WARMUP_CAPACITY = 2048;

    private static VertexData[] sourceVertexDataBuffer;
    private static int sourceVertexDataBufferLeaseState;

    private static int[] boundaryNodeIndexByConnectivityVertexId;
    private static int[] boundaryNodeGenerationByConnectivityVertexId;
    private static int boundaryConnectivityLookupGeneration;

    // 食い込み側もMesh属性をListへ直接取得して毎Planeの配列生成を避ける。
    private static readonly Stack<List<Vector3>> sourceVertexListPool =
        new Stack<List<Vector3>>();

    private static readonly Stack<List<int>> sourceTriangleIndexListPool =
        new Stack<List<int>>();

    // 分割試行ごとに使用する領域。Cap構築が終わるまで貸し出し、finallyで返却する。
    private static readonly Stack<List<CutSegment>> cutSegmentListPool =
        new Stack<List<CutSegment>>();
    private static readonly Stack<List<VertexData>> clippedPolygonListPool =
        new Stack<List<VertexData>>();
    private static readonly Stack<List<Vector3>> cutPointListPool =
        new Stack<List<Vector3>>();
    private static readonly Stack<List<int>> cutPointIndexListPool =
        new Stack<List<int>>();

    // 食い込み分割の境界構築中だけ借りるセル索引。ノードは既存ListのIndexで参照する。
    private static readonly Stack<Dictionary<Vector3Int, int>> boundarySpatialLookupPool =
        new Stack<Dictionary<Vector3Int, int>>();

    private static readonly Stack<List<Vector3>> sourceNormalListPool =
        new Stack<List<Vector3>>();

    private static readonly Stack<List<Vector2>> sourceUvListPool =
        new Stack<List<Vector2>>();

    private static readonly Stack<List<Vector4>> sourceTangentListPool =
        new Stack<List<Vector4>>();

    // 通常攻撃は各分割内で返却し、Editorでは最終Piece確定まで貸し出す。
    // 必要Capacityに合うBuilderを選ぶため、Pool内の空き要素を走査できるListで保持する。
    // 貸し出し中のBuilderはこのListへ含めず、同時分割でも共有しない。
    private static readonly List<PieceMeshBuilder> pieceMeshBuilderPool =
        new List<PieceMeshBuilder>(NORMAL_SPLIT_OUTPUT_COUNT);

    // Boundary診断がSource Triangleを再取得する場合だけ使用する。
    // 通常分割本体のTriangleはChainsawMeshCutProcessorから受け取るため、再取得しない。
    private static readonly Stack<List<int>> diagnosticTriangleIndexListPool =
        new Stack<List<int>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeSourceVertexDataBuffer()
    {
        if (sourceVertexDataBuffer == null ||
            sourceVertexDataBuffer.Length < SOURCE_VERTEX_DATA_BUFFER_LENGTH)
        {
            sourceVertexDataBuffer =
                new VertexData[SOURCE_VERTEX_DATA_BUFFER_LENGTH];
        }

        sourceVertexDataBufferLeaseState = 0;

        if (boundaryNodeIndexByConnectivityVertexId == null ||
            boundaryNodeGenerationByConnectivityVertexId == null ||
            boundaryNodeIndexByConnectivityVertexId.Length <
            BOUNDARY_CONNECTIVITY_LOOKUP_BUFFER_LENGTH ||
            boundaryNodeGenerationByConnectivityVertexId.Length <
            BOUNDARY_CONNECTIVITY_LOOKUP_BUFFER_LENGTH)
        {
            boundaryNodeIndexByConnectivityVertexId =
                new int[BOUNDARY_CONNECTIVITY_LOOKUP_BUFFER_LENGTH];

            boundaryNodeGenerationByConnectivityVertexId =
                new int[BOUNDARY_CONNECTIVITY_LOOKUP_BUFFER_LENGTH];
        }

        // Domain Reload無効でも前回PlayのGenerationが残らないよう、
        // ゲームプレイ開始前にだけGeneration配列を初期化する。
        Array.Clear(
            boundaryNodeGenerationByConnectivityVertexId,
            0,
            boundaryNodeGenerationByConnectivityVertexId.Length);

        boundaryConnectivityLookupGeneration = 0;

        EnsurePooledListAvailable(
            sourceVertexListPool,
            SOURCE_MESH_ATTRIBUTE_LIST_WARMUP_CAPACITY);

        EnsurePooledListAvailable(
            sourceTriangleIndexListPool,
            SOURCE_MESH_ATTRIBUTE_LIST_WARMUP_CAPACITY * 3);

        // 交差Triangleから生成するCutSegmentも1つの作業Listを繰り返し使用する。
        EnsurePooledListAvailable(cutSegmentListPool,
            CUT_SEGMENT_WARMUP_CAPACITY);
        EnsurePooledListAvailable(clippedPolygonListPool, 4);
        EnsurePooledListAvailable(cutPointListPool, 3);
        // 端点ごとの元Edge index 2種類＋接続IDの合計3本を同時に使用する。
        while (cutPointIndexListPool.Count < 3)
        {
            cutPointIndexListPool.Push(new List<int>(3));
        }

        EnsurePooledListAvailable(
            sourceNormalListPool,
            SOURCE_MESH_ATTRIBUTE_LIST_WARMUP_CAPACITY);

        EnsurePooledListAvailable(
            sourceUvListPool,
            SOURCE_MESH_ATTRIBUTE_LIST_WARMUP_CAPACITY);

        EnsurePooledListAvailable(
            sourceTangentListPool,
            SOURCE_MESH_ATTRIBUTE_LIST_WARMUP_CAPACITY);

        WarmupPieceMeshBuilderPool();
    }

    private readonly MeshConnectedComponentSplitter connectedComponentSplitter =
        new MeshConnectedComponentSplitter();

    // 目標数分割の同期呼び出し中だけ、Builderから出力Pieceの面積を取得する。
    private bool isTargetCountSplit;
    private bool collectTargetPieceSurfaceAreas;
    private float firstTargetPieceSurfaceArea;
    private float secondTargetPieceSurfaceArea;

    /// <summary>
    /// 食い込み分割用の入力を一度だけMeshから読み込み、以後はBuilderをPieceとして保持する。
    /// 呼び出し側は成功時に受け取ったBuilderを必ずReleaseTargetCountPieceで返却する。
    /// </summary>
    internal bool TryAcquireTargetCountSource(
        Mesh sourceMesh,
        out PieceMeshBuilder sourcePiece,
        out string failureReason)
    {
        using var tryAcquireTargetCountSourceProfileScope = acquireTargetSourceProfilerMarker.Auto();
        sourcePiece = null;
        if (sourceMesh == null || sourceMesh.vertexCount == 0 ||
            sourceMesh.subMeshCount == 0)
        {
            failureReason = "分割対象Meshに有効な頂点またはSubMeshがありません。";
            return false;
        }

        PieceMeshBuilder builder = AcquirePieceMeshBuilder(
            sourceMesh.subMeshCount,
            sourceMesh.HasVertexAttribute(VertexAttribute.Tangent),
            sourceMesh.vertexCount,
            sourceMesh.vertexCount,
            false,
            this,
            null,
            sourceMesh);

        try
        {
            // Get*のList版へ直接読み込む。後続の分割試行はこのBuilderを読み取るだけ。
            sourceMesh.GetVertices(builder.Vertices);
            sourceMesh.GetNormals(builder.Normals);
            sourceMesh.GetUVs(0, builder.Uv);
            sourceMesh.GetTangents(builder.Tangents);

            if (builder.Normals.Count != builder.Vertices.Count ||
                (builder.Uv.Count != 0 && builder.Uv.Count != builder.Vertices.Count) ||
                (builder.Tangents.Count != 0 &&
                 builder.Tangents.Count != builder.Vertices.Count))
            {
                failureReason = "切断対象Meshの頂点属性数がVertex数と一致しません。";
                return false;
            }

            for (int subMeshIndex = 0;
                 subMeshIndex < sourceMesh.subMeshCount;
                 subMeshIndex++)
            {
                if (sourceMesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
                {
                    failureReason =
                        $"SubMesh {subMeshIndex} がTriangles topologyではありません。";
                    return false;
                }

                List<int> indices = builder.TrianglesBySubMesh[subMeshIndex];
                sourceMesh.GetTriangles(indices, subMeshIndex);
                if (indices.Count % 3 != 0)
                {
                    failureReason = $"SubMesh {subMeshIndex} のTriangle index数が不正です。";
                    return false;
                }
            }

            // 初回の切断位置計算用に、SourceのTriangle面積を一度だけ求める。
            builder.EnableTriangleAreaCache();

            // 元MeshのBounds設定値を維持する。生成Pieceは従来どおり頂点から計算する。
            builder.SetSourceBounds(sourceMesh.bounds);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordTriangleLoopSource(builder);
#endif
            sourcePiece = builder;
            builder = null;
            failureReason = null;
            return true;
        }
        finally
        {
            ReleasePieceMeshBuilder(builder);
        }
    }

    /// <summary>
    /// Builderを破壊せずPlane分割する。失敗時は入力Pieceを保持し、出力のみ返却する。
    /// 成功時の出力Builderの所有権は呼び出し側へ移る。
    /// </summary>
    internal bool TrySplitForTargetCount(
        PieceMeshBuilder sourcePiece,
        Plane cutPlane,
        float cutSurfaceUvScale,
        bool calculateSurfaceAreas,
        out PieceMeshBuilder firstPiece,
        out PieceMeshBuilder secondPiece,
        out float firstSurfaceArea,
        out float secondSurfaceArea,
        out string failureReason)
    {
        firstPiece = null;
        secondPiece = null;
        firstSurfaceArea = 0f;
        secondSurfaceArea = 0f;

        if (sourcePiece == null || sourcePiece.Vertices.Count == 0 ||
            sourcePiece.TrianglesBySubMesh.Count == 0 ||
            !IsFinite(cutPlane.normal) || !IsFinite(cutPlane.distance) ||
            cutPlane.normal.sqrMagnitude <= MIN_DIRECTION_SQR_MAGNITUDE ||
            cutSurfaceUvScale <= 0f)
        {
            failureReason = "食い込み分割の入力PieceまたはPlaneが不正です。";
            return false;
        }

        isTargetCountSplit = true;
        collectTargetPieceSurfaceAreas = calculateSurfaceAreas;
        firstTargetPieceSurfaceArea = 0f;
        secondTargetPieceSurfaceArea = 0f;
        try
        {
            bool succeeded;
            using (splitTotalProfilerMarker.Auto())
            {
                succeeded = TrySplitMesh(
                    null,
                    sourcePiece,
                    cutPlane,
                    cutSurfaceUvScale,
                    CalculateGeometryEpsilon(sourcePiece.BuilderBounds),
                    null, null, null, null, null,
                    "MeshPlaneSplit_FirstPiece",
                    "MeshPlaneSplit_SecondPiece",
                    null, false, false, false, false,
                    out _, out _, out _,
                    out firstPiece, out secondPiece,
                    out failureReason);
            }

            firstSurfaceArea = firstTargetPieceSurfaceArea;
            secondSurfaceArea = secondTargetPieceSurfaceArea;
            return succeeded;
        }
        finally
        {
            collectTargetPieceSurfaceAreas = false;
            isTargetCountSplit = false;
        }
    }

    internal void ReleaseTargetCountPiece(PieceMeshBuilder piece)
    {
        ReleasePieceMeshBuilder(piece);
    }

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
    /// Plane上頂点fan対応はShieldのEditor事前生成だけで有効化し、既存Runtime経路では無効のまま使用する。
    /// </remarks>
    internal bool TrySplit(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        string firstPieceMeshName,
        string secondPieceMeshName,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        bool enablePlaneTouchingVertexFanHandling,
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
            null,
            firstPieceMeshName,
            secondPieceMeshName,
            cutBoundaryValidator,
            enableDiagnostics,
            enablePlaneTouchingVertexFanHandling,
            false,
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
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
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
            null,
            firstPieceMeshName,
            secondPieceMeshName,
            cutBoundaryValidator,
            enableDiagnostics,
            false,
            false,
            false,
            out firstPieceMesh,
            out secondPieceMesh,
            out _,
            out failureReason);
    }

    /// <summary>
    /// Plane分割結果から、連結成分単位またはPlaneの両側単位で最終Meshを直接生成する。
    /// </summary>
    /// <remarks>
    /// 通常攻撃死亡経路専用。Plane分割中に構築した頂点属性とTriangle indexを
    /// 通常はMeshConnectedComponentSplitterへ直接渡す。両側統合モードでは
    /// 同じ切断・境界構築結果から各側1つのMeshを生成し、Connected Component分離を省略する。
    /// </remarks>
    internal bool TrySplitConnectedPieces(
        Mesh sourceMesh,
        Plane cutPlane,
        float cutSurfaceUvScale,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        IReadOnlyList<int> sourceConnectivityVertexIds,
        string firstSideMeshNamePrefix,
        string secondSideMeshNamePrefix,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        bool keepCutSidesAsSinglePieces,
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
                sourceConnectivityVertexIds,
                firstSideMeshNamePrefix,
                secondSideMeshNamePrefix,
                cutBoundaryValidator,
                enableDiagnostics,
                false,
                true,
                keepCutSidesAsSinglePieces,
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
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        IReadOnlyList<int> cachedSourceConnectivityVertexIds,
        string firstPieceMeshName,
        string secondPieceMeshName,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        bool enablePlaneTouchingVertexFanHandling,
        bool splitConnectedComponents,
        bool keepCutSidesAsSinglePieces,
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
            null,
            cutPlane,
            cutSurfaceUvScale,
            geometryEpsilon,
            sourceVertices,
            sourceTrianglesBySubMesh,
            sourceVertexPlaneDistances,
            triangleClassificationsBySubMesh,
            cachedSourceConnectivityVertexIds,
            firstPieceMeshName,
            secondPieceMeshName,
            cutBoundaryValidator,
            enableDiagnostics,
            enablePlaneTouchingVertexFanHandling,
            splitConnectedComponents,
            keepCutSidesAsSinglePieces,
            out firstPieceMesh,
            out secondPieceMesh,
            out connectedPieceMeshes,
            out _,
            out _,
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
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
        float[] sourceVertexPlaneDistances,
        IReadOnlyList<byte[]> triangleClassificationsBySubMesh,
        out string failureReason)
    {
        if (sourceVertices == null ||
            sourceVertices.Count != sourceMesh.vertexCount ||
            sourceVertexPlaneDistances == null ||
            sourceVertexPlaneDistances.Length < sourceVertices.Count ||
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
            List<int> triangles =
                sourceTrianglesBySubMesh[subMeshIndex];

            byte[] triangleClassifications =
                triangleClassificationsBySubMesh[subMeshIndex];

            if (triangles == null ||
                triangles.Count % 3 != 0 ||
                triangleClassifications == null ||
                triangleClassifications.Length < triangles.Count / 3)
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
        PieceMeshBuilder sourceWorkingPiece,
        Plane cutPlane,
        float cutSurfaceUvScale,
        float geometryEpsilon,
        IReadOnlyList<Vector3> cachedSourceVertices,
        IReadOnlyList<List<int>> cachedSourceTrianglesBySubMesh,
        float[] cachedSourceVertexPlaneDistances,
        IReadOnlyList<byte[]> cachedTriangleClassificationsBySubMesh,
        IReadOnlyList<int> cachedSourceConnectivityVertexIds,
        string firstPieceMeshName,
        string secondPieceMeshName,
        Func<IReadOnlyList<Vector3>, string> cutBoundaryValidator,
        bool enableDiagnostics,
        bool enablePlaneTouchingVertexFanHandling,
        bool splitConnectedComponents,
        bool keepCutSidesAsSinglePieces,
        out Mesh firstPieceMesh,
        out Mesh secondPieceMesh,
        out List<Mesh> connectedPieceMeshes,
        out PieceMeshBuilder firstWorkingPiece,
        out PieceMeshBuilder secondWorkingPiece,
        out string failureReason)
    {
        using (splitGeometryProfilerMarker.Auto())
        {
        firstPieceMesh = null;
        secondPieceMesh = null;
        connectedPieceMeshes = null;
        firstWorkingPiece = null;
        secondWorkingPiece = null;

        bool useWorkingPiece = sourceWorkingPiece != null;
        Bounds sourceBounds = useWorkingPiece
            ? sourceWorkingPiece.BuilderBounds
            : sourceMesh.bounds;
        IReadOnlyList<List<int>> effectiveSourceTrianglesBySubMesh =
            useWorkingPiece
                ? sourceWorkingPiece.TrianglesBySubMesh
                : cachedSourceTrianglesBySubMesh;

        bool useCachedTriangleValidation =
            cachedSourceVertices != null;

        IReadOnlyList<Vector3> sourceVertices;
        IReadOnlyList<Vector3> sourceNormals;
        IReadOnlyList<Vector2> sourceUv;
        IReadOnlyList<Vector4> sourceTangents;

        List<Vector3> sourceVertexBuffer = null;
        List<Vector3> sourceNormalBuffer = null;
        List<Vector2> sourceUvBuffer = null;
        List<Vector4> sourceTangentBuffer = null;
        List<int> triangleIndices = null;
        List<CutSegment> cutSegments = null;
        List<VertexData> clippedPolygon = null;
        List<Vector3> cutPoints = null;
        List<int> cutPointSourceEdgeVertexIndexA = null;
        List<int> cutPointSourceEdgeVertexIndexB = null;
        List<int> cutPointConnectivityVertexIds = null;

        PieceMeshBuilder firstBuilder = null;
        PieceMeshBuilder secondBuilder = null;

        try
        {
            if (useWorkingPiece)
            {
                // 中間Pieceを読み取る。Unity Mesh化もList間コピーも不要。
                sourceVertices = sourceWorkingPiece.Vertices;
                sourceNormals = sourceWorkingPiece.Normals;
                sourceUv = sourceWorkingPiece.Uv;
                sourceTangents = sourceWorkingPiece.Tangents;
            }
            else
            {
                if (useCachedTriangleValidation)
                {
                    sourceVertices = cachedSourceVertices;
                }
                else
                {
                    sourceVertexBuffer = AcquireReusableList(
                        sourceVertexListPool, sourceMesh.vertexCount);
                    sourceMesh.GetVertices(sourceVertexBuffer);
                    sourceVertices = sourceVertexBuffer;
                }

                sourceNormalBuffer = AcquireReusableList(
                    sourceNormalListPool, sourceVertices.Count);
                sourceUvBuffer = AcquireReusableList(
                    sourceUvListPool, sourceVertices.Count);
                sourceTangentBuffer = AcquireReusableList(
                    sourceTangentListPool, sourceVertices.Count);

                // 通常攻撃の取得経路は変更しない。
                sourceMesh.GetNormals(sourceNormalBuffer);
                sourceMesh.GetUVs(0, sourceUvBuffer);
                sourceMesh.GetTangents(sourceTangentBuffer);
                sourceNormals = sourceNormalBuffer;
                sourceUv = sourceUvBuffer;
                sourceTangents = sourceTangentBuffer;
            }

            if (sourceNormals.Count != sourceVertices.Count)
            {
                failureReason =
                    "切断対象Meshに頂点数と一致するNormalがありません。";
                return false;
            }

            bool hasUv = sourceUv.Count > 0;
            if (hasUv && sourceUv.Count != sourceVertices.Count)
            {
                failureReason =
                    "切断対象MeshのUV0数が頂点数と一致していません。";
                return false;
            }

            bool hasTangents =
                sourceTangents.Count > 0;
            if (hasTangents && sourceTangents.Count != sourceVertices.Count)
            {
                failureReason =
                    "切断対象MeshのTangent数が頂点数と一致していません。";
                return false;
            }

        int originalSubMeshCount = useWorkingPiece
            ? sourceWorkingPiece.TrianglesBySubMesh.Count
            : sourceMesh.subMeshCount;
        int cutSurfaceSubMeshIndex = originalSubMeshCount;

        if (splitConnectedComponents &&
            cachedSourceConnectivityVertexIds != null &&
            cachedSourceConnectivityVertexIds.Count != sourceVertices.Count)
        {
            failureReason =
                "事前取得したSource Connectivity ID数がSource Vertex数と一致していません。";
            return false;
        }

        IReadOnlyList<int> sourceConnectivityVertexIds =
            splitConnectedComponents
                ? cachedSourceConnectivityVertexIds ??
                  MeshConnectedComponentSplitter.BuildConnectedVertexIds(
                      sourceVertices,
                      sourceBounds)
                : null;

        int nextConnectivityVertexId =
            sourceConnectivityVertexIds != null
                ? GetNextConnectivityVertexId(sourceConnectivityVertexIds)
                : 0;

        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge =
            splitConnectedComponents
                ? new Dictionary<long, int>()
                : null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (isTargetCountSplit && triangleLoopDiagnosticsActive)
        {
            triangleLoopTemporaryNewLists += cutSegmentListPool.Count == 0 ? 1 : 0;
            triangleLoopTemporaryNewLists += clippedPolygonListPool.Count == 0 ? 1 : 0;
            triangleLoopTemporaryNewLists += cutPointListPool.Count == 0 ? 1 : 0;
            triangleLoopTemporaryNewLists += Mathf.Max(0, 3 - cutPointIndexListPool.Count);
        }
#endif
        cutSegments = AcquireReusableList(cutSegmentListPool, 0);

        // Triangle単位で一時Collectionを生成しないため、分割処理中だけ使う作業領域を再利用する。
        // List.Clear()後もCapacityは維持されるため、交差Triangle数に比例したManaged Allocationを避けられる。
        triangleIndices = useCachedTriangleValidation || useWorkingPiece
            ? null
            : AcquireReusableList(sourceTriangleIndexListPool, 0);

        clippedPolygon = AcquireReusableList(clippedPolygonListPool, 4);
        cutPoints = AcquireReusableList(cutPointListPool, 3);
        cutPointSourceEdgeVertexIndexA = AcquireReusableList(cutPointIndexListPool, 3);
        cutPointSourceEdgeVertexIndexB = AcquireReusableList(cutPointIndexListPool, 3);
        cutPointConnectivityVertexIds = AcquireReusableList(cutPointIndexListPool, 3);

        int positiveOnlyTriangleCount = 0;
        int negativeOnlyTriangleCount = 0;
        int intersectingTriangleCount = 0;
        int degenerateCutSegmentCount = 0;
        int loggedDegenerateCutSegmentCount = 0;

        VertexData[] sourceVertexDataByIndex = null;
        bool returnSourceVertexDataBufferToPool = false;
        float[] sourcePlaneDistances = null;
        int positiveSourceVertexCount = 0;
        int negativeSourceVertexCount = 0;
        int onPlaneSourceVertexCount = 0;

        try
        {
                sourceVertexDataByIndex =
                    AcquireSourceVertexDataBuffer(
                        sourceVertices.Count,
                        out returnSourceVertexDataBufferToPool);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (isTargetCountSplit && triangleLoopDiagnosticsActive)
                {
                    if (returnSourceVertexDataBufferToPool)
                        triangleLoopTemporarySourceArrayPoolRents++;
                    else
                        triangleLoopDedicatedSourceBufferUses++;
                }
#endif

                if (isTargetCountSplit && !useCachedTriangleValidation)
                {
                    sourcePlaneDistances = ArrayPool<float>.Shared.Rent(sourceVertices.Count);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (triangleLoopDiagnosticsActive)
                    {
                        triangleLoopTemporaryDistanceArrayPoolRents++;
                    }
#endif
                }

                // Source Mesh由来の頂点属性はTriangle間で共有されるため、
                // Source Vertexごとに一度だけVertexData化し、TriangleループではIndex参照だけを行う。
                // Plane交差で生成されるCut VertexはこのBufferへ入れず、従来どおり補間処理で生成する。
                using (prepareSourceVerticesProfilerMarker.Auto())
                {
                for (int sourceVertexIndex = 0;
                     sourceVertexIndex < sourceVertices.Count;
                     sourceVertexIndex++)
                {
                    sourceVertexDataByIndex[sourceVertexIndex] =
                        CreateVertexData(
                            sourceVertexIndex,
                            sourceVertices,
                            sourceNormals,
                            sourceUv,
                            sourceTangents,
                            hasUv,
                            hasTangents,
                            sourceConnectivityVertexIds);
                    if (sourcePlaneDistances != null)
                    {
                        float signedDistance =
                            cutPlane.GetDistanceToPoint(sourceVertices[sourceVertexIndex]);
                        sourcePlaneDistances[sourceVertexIndex] = signedDistance;

                        // 食い込み分割で既に計算する距離を利用する。Triangleの再走査は不要。
                        // Plane上のSource頂点は両側で使われ得るため別に数える。
                        if (signedDistance > geometryEpsilon)
                        {
                            positiveSourceVertexCount++;
                        }
                        else if (signedDistance < -geometryEpsilon)
                        {
                            negativeSourceVertexCount++;
                        }
                        else
                        {
                            onPlaneSourceVertexCount++;
                        }
                    }
                }
                }

            int firstEstimatedVertexCapacity =
                CalculateInitialPieceVertexCapacity(sourceVertices.Count);
            int secondEstimatedVertexCapacity = firstEstimatedVertexCapacity;
            if (sourcePlaneDistances != null)
            {
                // 固定の半数予約では、偏ったPlaneの大きい側で拡張が発生し、
                // 小さい側では使用されない配列を確保してしまう。
                // 交差頂点・Cap頂点は事前に正確に数えられないため、
                // 小さい側のSource頂点数の1/4を控えめな予備とし、不足時は通常拡張する。
                int cutVertexAllowance =
                    Mathf.Min(positiveSourceVertexCount, negativeSourceVertexCount) / 4;
                firstEstimatedVertexCapacity =
                    CalculateEstimatedSideVertexCapacity(
                        positiveSourceVertexCount,
                        onPlaneSourceVertexCount,
                        cutVertexAllowance);
                secondEstimatedVertexCapacity =
                    CalculateEstimatedSideVertexCapacity(
                        negativeSourceVertexCount,
                        onPlaneSourceVertexCount,
                        cutVertexAllowance);
            }

            using (createPieceMeshBuildersProfilerMarker.Auto())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // 通常分割やTargetCountのSource取得を集計から除外する。
                bool recordBuilderAcquisitions =
                    targetCountBuilderDiagnosticsEnabled && isTargetCountSplit;
                if (recordBuilderAcquisitions)
                {
                    captureCreatePieceBuilders = true;
                }
                try
                {
#endif
                // 大きい出力側を先に取得し、容量の大きいPool Builderを小さい側へ
                // 誤って割り当てないようにする。返却後のCapacityは維持される。
                if (firstEstimatedVertexCapacity >= secondEstimatedVertexCapacity)
                {
                    firstBuilder = AcquirePieceMeshBuilder(
                        originalSubMeshCount + 1,
                        hasTangents,
                        sourceVertices.Count,
                        firstEstimatedVertexCapacity,
                        splitConnectedComponents,
                        isTargetCountSplit ? this : null,
                        useWorkingPiece ? sourceWorkingPiece.TrianglesBySubMesh : null);
                    secondBuilder = AcquirePieceMeshBuilder(
                        originalSubMeshCount + 1,
                        hasTangents,
                        sourceVertices.Count,
                        secondEstimatedVertexCapacity,
                        splitConnectedComponents,
                        isTargetCountSplit ? this : null,
                        useWorkingPiece ? sourceWorkingPiece.TrianglesBySubMesh : null);
                }
                else
                {
                    secondBuilder = AcquirePieceMeshBuilder(
                        originalSubMeshCount + 1,
                        hasTangents,
                        sourceVertices.Count,
                        secondEstimatedVertexCapacity,
                        splitConnectedComponents,
                        isTargetCountSplit ? this : null,
                        useWorkingPiece ? sourceWorkingPiece.TrianglesBySubMesh : null);
                    firstBuilder = AcquirePieceMeshBuilder(
                        originalSubMeshCount + 1,
                        hasTangents,
                        sourceVertices.Count,
                        firstEstimatedVertexCapacity,
                        splitConnectedComponents,
                        isTargetCountSplit ? this : null,
                        useWorkingPiece ? sourceWorkingPiece.TrianglesBySubMesh : null);
                }

                if (collectTargetPieceSurfaceAreas)
                {
                    firstBuilder.EnableTriangleAreaCache();
                    secondBuilder.EnableTriangleAreaCache();
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                }
                finally
                {
                    if (recordBuilderAcquisitions)
                    {
                        captureCreatePieceBuilders = false;
                    }
                }
#endif
            }

            using (processTriangleLoopProfilerMarker.Auto())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bool recordThisTriangleLoop = isTargetCountSplit && triangleLoopDiagnosticsActive;
            if (recordThisTriangleLoop)
            {
                RecordTriangleLoopBegin(sourceWorkingPiece);
            }
            try
            {
#endif
            for (int subMeshIndex = 0;
                 subMeshIndex < originalSubMeshCount;
                 subMeshIndex++)
            {
                if (!useWorkingPiece &&
                    sourceMesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
                {
                    failureReason =
                        $"SubMesh {subMeshIndex} がTriangles topologyではありません。";
                    return false;
                }

                IReadOnlyList<int> sourceTriangleIndices;

                if (useWorkingPiece)
                {
                    sourceTriangleIndices =
                        sourceWorkingPiece.TrianglesBySubMesh[subMeshIndex];
                }
                else if (useCachedTriangleValidation)
                {
                    // Cut Plane検証で取得済みのSubMesh index配列をそのまま使い、
                    // 採用Planeの分割開始時にGetTrianglesを再実行しない。
                    sourceTriangleIndices =
                        cachedSourceTrianglesBySubMesh[subMeshIndex];
                }
                else
                {
                    // 事前検証キャッシュを使わない既存経路では、従来どおり作業Listへ取得する。
                    sourceMesh.GetTriangles(
                        triangleIndices,
                        subMeshIndex);

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

                using (triangleIndexReservationProfilerMarker.Auto())
                {
                firstBuilder.SetInitialSubMeshIndexCapacity(
                    subMeshIndex,
                    estimatedSubMeshIndexCapacity);

                secondBuilder.SetInitialSubMeshIndexCapacity(
                    subMeshIndex,
                    estimatedSubMeshIndexCapacity);
                }

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

                        indexA = sourceTriangleIndices[triangleIndex];
                        indexB = sourceTriangleIndices[triangleIndex + 1];
                        indexC = sourceTriangleIndices[triangleIndex + 2];

                        if (!IsValidVertexIndex(indexA, sourceVertices.Count) ||
                            !IsValidVertexIndex(indexB, sourceVertices.Count) ||
                            !IsValidVertexIndex(indexC, sourceVertices.Count))
                        {
                            failureReason =
                                $"SubMesh {subMeshIndex} に範囲外の頂点indexがあります。";
                            return false;
                        }

                        vertexA = sourceVertexDataByIndex[indexA];
                        vertexB = sourceVertexDataByIndex[indexB];
                        vertexC = sourceVertexDataByIndex[indexC];

                    float distanceA;
                    float distanceB;
                    float distanceC;
                    byte triangleClassification;

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
                            distanceA = sourcePlaneDistances != null
                                ? sourcePlaneDistances[indexA]
                                : cutPlane.GetDistanceToPoint(vertexA.Position);
                            distanceB = sourcePlaneDistances != null
                                ? sourcePlaneDistances[indexB]
                                : cutPlane.GetDistanceToPoint(vertexB.Position);
                            distanceC = sourcePlaneDistances != null
                                ? sourcePlaneDistances[indexC]
                                : cutPlane.GetDistanceToPoint(vertexC.Position);

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

                    if (triangleClassification ==
                        TRIANGLE_CLASSIFICATION_POSITIVE)
                    {
                        if (enableDiagnostics)
                        {
                            positiveOnlyTriangleCount++;
                        }

                            firstBuilder.AddTriangle(
                                vertexA,
                                vertexB,
                                vertexC,
                                subMeshIndex,
                                false);

                        continue;
                    }

                    if (triangleClassification ==
                        TRIANGLE_CLASSIFICATION_NEGATIVE)
                    {
                        if (enableDiagnostics)
                        {
                            negativeOnlyTriangleCount++;
                        }

                            secondBuilder.AddTriangle(
                                vertexA,
                                vertexB,
                                vertexC,
                                subMeshIndex,
                                false);

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

                            TriangleIntersectionData triangleIntersections =
                                ResolveTriangleIntersections(
                                    vertexA,
                                    vertexB,
                                    vertexC,
                                    distanceA,
                                    distanceB,
                                    distanceC,
                                    geometryEpsilon,
                                    cutConnectivityVertexIdsBySourceEdge,
                                    ref nextConnectivityVertexId);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            int clippedPolygonCapacityBefore = recordThisTriangleLoop
                                ? clippedPolygon.Capacity : 0;
#endif
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
                                in triangleIntersections);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            if (recordThisTriangleLoop)
                            {
                                RecordTriangleLoopBufferGrowth(LOOP_CLIPPED_POLYGON,
                                    clippedPolygonCapacityBefore, clippedPolygon.Capacity);
                                RecordTriangleLoopBufferUsage(LOOP_CLIPPED_POLYGON,
                                    clippedPolygon.Count, clippedPolygon.Capacity);
                            }
#endif

                            firstBuilder.AddConvexPolygon(
                                clippedPolygon,
                                subMeshIndex,
                                false);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            int clippedPolygonCapacityBeforeSecond = recordThisTriangleLoop
                                ? clippedPolygon.Capacity : 0;
#endif
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
                                in triangleIntersections);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            if (recordThisTriangleLoop)
                            {
                                RecordTriangleLoopBufferGrowth(LOOP_CLIPPED_POLYGON,
                                    clippedPolygonCapacityBeforeSecond, clippedPolygon.Capacity);
                                RecordTriangleLoopBufferUsage(LOOP_CLIPPED_POLYGON,
                                    clippedPolygon.Count, clippedPolygon.Capacity);
                            }
#endif

                            secondBuilder.AddConvexPolygon(
                                clippedPolygon,
                                subMeshIndex,
                                false);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            int cutPointCapacityBefore = recordThisTriangleLoop ? cutPoints.Capacity : 0;
                            int cutPointIndexACapacityBefore = recordThisTriangleLoop
                                ? cutPointSourceEdgeVertexIndexA.Capacity : 0;
                            int cutPointIndexBCapacityBefore = recordThisTriangleLoop
                                ? cutPointSourceEdgeVertexIndexB.Capacity : 0;
                            int cutPointIdCapacityBefore = recordThisTriangleLoop
                                ? cutPointConnectivityVertexIds.Capacity : 0;
#endif
                            bool foundCutSegment = TryGetCutSegment(
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
                                    in triangleIntersections,
                                    cutPoints,
                                    cutPointSourceEdgeVertexIndexA,
                                    cutPointSourceEdgeVertexIndexB,
                                    cutPointConnectivityVertexIds,
                                    out CutSegment cutSegment,
                                    out bool isDegenerateSegment);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                            if (recordThisTriangleLoop)
                            {
                                RecordTriangleLoopBufferGrowth(LOOP_CUT_POINT,
                                    cutPointCapacityBefore, cutPoints.Capacity);
                                RecordTriangleLoopBufferGrowth(LOOP_CUT_POINT_INDEX,
                                    cutPointIndexACapacityBefore, cutPointSourceEdgeVertexIndexA.Capacity);
                                RecordTriangleLoopBufferGrowth(LOOP_CUT_POINT_INDEX,
                                    cutPointIndexBCapacityBefore, cutPointSourceEdgeVertexIndexB.Capacity);
                                RecordTriangleLoopBufferGrowth(LOOP_CUT_POINT_INDEX,
                                    cutPointIdCapacityBefore, cutPointConnectivityVertexIds.Capacity);
                                RecordTriangleLoopBufferUsage(LOOP_CUT_POINT,
                                    cutPoints.Count, cutPoints.Capacity);
                                RecordTriangleLoopBufferUsage(LOOP_CUT_POINT_INDEX,
                                    cutPointSourceEdgeVertexIndexA.Count, cutPointSourceEdgeVertexIndexA.Capacity);
                                RecordTriangleLoopBufferUsage(LOOP_CUT_POINT_INDEX,
                                    cutPointSourceEdgeVertexIndexB.Count, cutPointSourceEdgeVertexIndexB.Capacity);
                                RecordTriangleLoopBufferUsage(LOOP_CUT_POINT_INDEX,
                                    cutPointConnectivityVertexIds.Count, cutPointConnectivityVertexIds.Capacity);
                            }
#endif
                            if (!foundCutSegment)
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

                            AddWithGrowthMarker(cutSegments, cutSegment, cutSegmentGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                                , recordThisTriangleLoop ? this : null, LOOP_SEGMENT
#endif
                                );
                }
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            }
            finally
            {
                if (recordThisTriangleLoop)
                {
                    RecordTriangleLoopEnd(firstBuilder, secondBuilder, cutSegments,
                        clippedPolygon, cutPoints, cutPointSourceEdgeVertexIndexA,
                        cutPointSourceEdgeVertexIndexB, cutPointConnectivityVertexIds);
                }
            }
#endif
            }
        }
        finally
        {
            if (sourcePlaneDistances != null)
            {
                ArrayPool<float>.Shared.Return(sourcePlaneDistances);
            }
            ReleaseSourceVertexDataBuffer(
                sourceVertexDataByIndex,
                returnSourceVertexDataBufferToPool);
        }

        // Plane上頂点を含むfanでは、Plane上Edgeが正負両側のTriangleを隔てる場合がある。
        // 通常Runtime分割は従来挙動を維持するため、この追加Topology走査は明示的にONのときだけ行う。
        if (enablePlaneTouchingVertexFanHandling &&
            !TryAddPlaneTouchingVertexFanBoundarySegments(
                sourceMesh,
                sourceVertices,
                cutPlane,
                geometryEpsilon,
                cutSegments,
                out failureReason))
        {
            return false;
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
                splitConnectedComponents,
                nextConnectivityVertexId,
                enableDiagnostics,
                out List<List<Vector3>> boundaryLoops,
                out List<List<int>> boundaryConnectivityVertexIds,
                out List<List<int>> boundarySourceVertexIndices,
                out List<Vector3> uncappedBoundaryEdgePoints,
                out List<List<int>> uncappedBoundarySourceVertexIndices,
                out int openBoundaryPathCount,
                out int ambiguousBoundaryComponentCount,
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
                boundarySourceVertexIndices,
                cutSegments,
                sourceMesh,
                sourceBounds,
                originalSubMeshCount,
                sourceVertices,
                effectiveSourceTrianglesBySubMesh,
                sourceConnectivityVertexIds,
                cutPlane,
                cutSurfaceUvScale,
                geometryEpsilon,
                cutSurfaceSubMeshIndex,
                uncappedBoundaryEdgePoints,
                uncappedBoundarySourceVertexIndices,
                enableDiagnostics,
                firstBuilder,
                secondBuilder,
                out int generatedCapRegionCount,
                out int skippedCapRegionCount,
                out failureReason))
        {
            return false;
        }

        if (enableDiagnostics)
        {
            Debug.Log(
                "[MeshCutBoundaryDiagnostic][CapSummary] " +
                $"ClosedLoops={boundaryLoops.Count}, " +
                $"OpenPaths={openBoundaryPathCount}, " +
                $"AmbiguousComponents={ambiguousBoundaryComponentCount}, " +
                $"UncappedEdges={uncappedBoundaryEdgePoints.Count / 2}, " +
                $"GeneratedCapRegions={generatedCapRegionCount}, " +
                $"SkippedCapRegions={skippedCapRegionCount}");
        }

        if (splitConnectedComponents && keepCutSidesAsSinglePieces)
        {
            // 既存の切断境界・Cap生成・Builder Poolを維持したまま、
            // 非連結部分だけは独立Piece化せず同じ側のMeshへ含める。
            Mesh positiveSideMesh =
                firstBuilder.BuildMesh(firstPieceMeshName);

            Mesh negativeSideMesh =
                secondBuilder.BuildMesh(secondPieceMeshName);

            if (positiveSideMesh == null || negativeSideMesh == null)
            {
                DestroyMeshes(new[] { positiveSideMesh, negativeSideMesh });
                failureReason =
                    "切断Planeの両側から2個のMeshを生成できませんでした。";
                return false;
            }

            connectedPieceMeshes = new List<Mesh>(2)
            {
                positiveSideMesh,
                negativeSideMesh
            };

            failureReason = null;
            return true;
        }

        if (splitConnectedComponents)
        {
            if (!connectedComponentSplitter.TrySplitTrustedWorkingData(
                    firstBuilder.Vertices,
                    firstBuilder.Normals,
                    firstBuilder.Uv,
                    firstBuilder.Tangents,
                    firstBuilder.HasTangents,
                    firstBuilder.TrianglesBySubMesh,
                    firstBuilder.ConnectivityVertexIds,
                    firstBuilder.TotalTriangleCount,
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

            if (!connectedComponentSplitter.TrySplitTrustedWorkingData(
                    secondBuilder.Vertices,
                    secondBuilder.Normals,
                    secondBuilder.Uv,
                    secondBuilder.Tangents,
                    secondBuilder.HasTangents,
                    secondBuilder.TrianglesBySubMesh,
                    secondBuilder.ConnectivityVertexIds,
                    secondBuilder.TotalTriangleCount,
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

        if (collectTargetPieceSurfaceAreas)
        {
            // Meshへ転送後に頂点とIndexを再取得せず、同じSubMesh順で面積を集計する。
            firstTargetPieceSurfaceArea = firstBuilder.CalculateSurfaceArea();
            secondTargetPieceSurfaceArea = secondBuilder.CalculateSurfaceArea();
        }

        if (useWorkingPiece)
        {
            // 成功時だけBuilderの所有権を呼び出し側へ移す。finallyでPoolへ返さない。
            firstBuilder.WorkingMeshName = firstPieceMeshName;
            secondBuilder.WorkingMeshName = secondPieceMeshName;
            firstWorkingPiece = firstBuilder;
            secondWorkingPiece = secondBuilder;
            firstBuilder = null;
            secondBuilder = null;
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
            // 片側のMesh生成だけが成功した場合も呼出元に中間成果物を残さない。
            DestroyMeshes(new[] { firstPieceMesh, secondPieceMesh });
            firstPieceMesh = null;
            secondPieceMesh = null;
            failureReason =
                "有効な2つの分割Meshを生成できませんでした。";
            return false;
        }

        failureReason = null;
        return true;
        }
        finally
        {
            // 通常攻撃・食い込み攻撃の双方がここでBuilderの所有権を返す。
            // 途中returnや例外でも保持済みの作業領域を解放する。
            ReleasePieceMeshBuilder(secondBuilder);
            ReleasePieceMeshBuilder(firstBuilder);
            ReleaseReusableList(cutPointIndexListPool, cutPointConnectivityVertexIds);
            ReleaseReusableList(cutPointIndexListPool, cutPointSourceEdgeVertexIndexB);
            ReleaseReusableList(cutPointIndexListPool, cutPointSourceEdgeVertexIndexA);
            ReleaseReusableList(cutPointListPool, cutPoints);
            ReleaseReusableList(clippedPolygonListPool, clippedPolygon);
            ReleaseReusableList(cutSegmentListPool, cutSegments);
            ReleaseReusableList(sourceTriangleIndexListPool, triangleIndices);
            ReleaseReusableList(sourceVertexListPool, sourceVertexBuffer);

            ReleaseReusableList(
                sourceTangentListPool,
                sourceTangentBuffer);

            ReleaseReusableList(
                sourceUvListPool,
                sourceUvBuffer);

            ReleaseReusableList(
                sourceNormalListPool,
                sourceNormalBuffer);
        }

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


    private static int BeginBoundaryConnectivityLookup(
        int requiredLength)
    {
        EnsureBoundaryConnectivityLookupCapacity(requiredLength);

        if (boundaryConnectivityLookupGeneration == int.MaxValue)
        {
            Array.Clear(
                boundaryNodeGenerationByConnectivityVertexId,
                0,
                boundaryNodeGenerationByConnectivityVertexId.Length);

            boundaryConnectivityLookupGeneration = 1;
            return boundaryConnectivityLookupGeneration;
        }

        boundaryConnectivityLookupGeneration++;

        // Generation 0は「fast path無効」を表すため、Runtime初期化直後も1から開始する。
        if (boundaryConnectivityLookupGeneration == 0)
        {
            boundaryConnectivityLookupGeneration = 1;
        }

        return boundaryConnectivityLookupGeneration;
    }

    private static void EnsureBoundaryConnectivityLookupCapacity(
        int requiredLength)
    {
        if (requiredLength <= 0)
        {
            requiredLength = 1;
        }

        if (boundaryNodeIndexByConnectivityVertexId != null &&
            boundaryNodeGenerationByConnectivityVertexId != null &&
            boundaryNodeIndexByConnectivityVertexId.Length >= requiredLength &&
            boundaryNodeGenerationByConnectivityVertexId.Length >= requiredLength)
        {
            return;
        }

        int currentLength =
            boundaryNodeIndexByConnectivityVertexId != null
                ? boundaryNodeIndexByConnectivityVertexId.Length
                : 0;

        int newLength =
            Mathf.Max(
                BOUNDARY_CONNECTIVITY_LOOKUP_BUFFER_LENGTH,
                currentLength > 0
                    ? currentLength
                    : 1);

        while (newLength < requiredLength)
        {
            int doubledLength =
                newLength <= int.MaxValue / 2
                    ? newLength * 2
                    : int.MaxValue;

            if (doubledLength <= newLength)
            {
                newLength = requiredLength;
                break;
            }

            newLength = doubledLength;
        }

        Array.Resize(
            ref boundaryNodeIndexByConnectivityVertexId,
            newLength);

        Array.Resize(
            ref boundaryNodeGenerationByConnectivityVertexId,
            newLength);
    }

    private static void EnsurePooledListAvailable<T>(
        Stack<List<T>> pool,
        int warmupCapacity)
    {
        foreach (List<T> list in pool)
        {
            if (list.Capacity >= warmupCapacity)
            {
                return;
            }
        }

        pool.Push(
            new List<T>(warmupCapacity));
    }

    private static List<T> AcquireReusableList<T>(
        Stack<List<T>> pool,
        int minimumCapacity)
    {
        List<T> list =
            pool.Count > 0
                ? pool.Pop()
                : new List<T>(minimumCapacity);

        list.Clear();

        if (list.Capacity < minimumCapacity)
        {
            list.Capacity = minimumCapacity;
        }

        return list;
    }

    // サンプルは容量拡張時だけ作る。List.Add自体の挙動と容量拡張方式は変えない。
    private static bool AddWithGrowthMarker<T>(
        List<T> list, T value, ProfilerMarker marker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        , MeshPlaneSplitter diagnosticOwner = null, int bufferKind = 0,
        int diagnosticBuilderId = 0
#endif
        )
    {
        if (list.Count == list.Capacity)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int capacityBefore = list.Capacity;
#endif
            using (marker.Auto())
            {
                list.Add(value);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (diagnosticOwner != null)
            {
                diagnosticOwner.RecordTriangleLoopBufferGrowth(
                    bufferKind, capacityBefore, list.Capacity, diagnosticBuilderId);
            }
#endif
            return true;
        }

        list.Add(value);
        return false;
    }

    private static void ReleaseReusableList<T>(
        Stack<List<T>> pool,
        List<T> list)
    {
        if (list == null)
        {
            return;
        }

        list.Clear();
        pool.Push(list);
    }

    private static void WarmupPieceMeshBuilderPool()
    {
        using var warmupSample = warmupTriangleBuffersProfilerMarker.Auto();
        // 通常のPlane分割に必要な正側・負側のBuilderと三角形Index領域を用意する。
        // Editorの反復分割では同じPoolから借り、不足する分を必要時に追加する。
        const int warmupSubMeshCount = 2;
        int vertexCapacity = CalculateInitialPieceVertexCapacity(SOURCE_VERTEX_DATA_BUFFER_LENGTH);

        while (pieceMeshBuilderPool.Count < NORMAL_SPLIT_OUTPUT_COUNT)
        {
            var builder = new PieceMeshBuilder(warmupSubMeshCount, true,
                SOURCE_VERTEX_DATA_BUFFER_LENGTH, vertexCapacity, true, true);
            builder.WarmupNormalSplitIndexBuffers();
            pieceMeshBuilderPool.Add(builder);
        }
    }

    private static PieceMeshBuilder AcquirePieceMeshBuilder(
        int subMeshCount,
        bool hasTangents,
        int sourceVertexCount,
        int estimatedVertexCapacity,
        bool storeConnectivityVertexIds,
        MeshPlaneSplitter diagnosticOwner = null,
        IReadOnlyList<List<int>> sourceTrianglesBySubMesh = null,
        Mesh sourceMeshForIndexSelection = null)
    {
        if (pieceMeshBuilderPool.Count == 0)
        {
            // 同時使用中のBuilderは再利用しない。Poolを使い切った場合だけ追加生成する。
            using (acquireNewBuilderProfilerMarker.Auto())
            {
                PieceMeshBuilder newBuilder = new PieceMeshBuilder(
                    subMeshCount,
                    hasTangents,
                    sourceVertexCount,
                    estimatedVertexCapacity,
                    true,
                    storeConnectivityVertexIds);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (captureCreatePieceBuilders)
                {
                    newBuilderCount++;
                    requestedVertexCapacitySum += estimatedVertexCapacity;
                    newBuilder.TrackCreateBuilderDiagnostics = true;
                    RecordBuilderAcquisitionDetails(
                        newBuilder, true, sourceVertexCount, estimatedVertexCapacity,
                        0, 0, 0, 0, 0);
                }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (diagnosticOwner != null && diagnosticOwner.triangleLoopDiagnosticsActive)
                {
                    newBuilder.DiagnosticOwner = diagnosticOwner;
                    diagnosticOwner.RecordTriangleLoopAcquisition(true);
                }
#endif
                return newBuilder;
            }
        }

        // 通常2分割では従来の頂点属性Capacityによる選択を維持する。
        // 8分割では頂点数だけで選ぶとIndex用Backing Arrayが小さいBuilderを
        // 取得してしまうため、実際の入力SubMeshに対するIndex不足も考慮する。
        int selectedIndex = 0;
        if (sourceTrianglesBySubMesh == null && sourceMeshForIndexSelection == null)
        {
            int selectedCapacity = -1;
            for (int index = 0; index < pieceMeshBuilderPool.Count; index++)
            {
                PieceMeshBuilder candidate = pieceMeshBuilderPool[index];
                int candidateCapacity = candidate.GetReusableVertexCapacity(
                    hasTangents, storeConnectivityVertexIds);
                bool candidateFits = candidateCapacity >= estimatedVertexCapacity;
                bool selectedFits = selectedCapacity >= estimatedVertexCapacity;
                if ((candidateFits && !selectedFits) ||
                    (candidateFits && candidateCapacity < selectedCapacity) ||
                    (!candidateFits && !selectedFits && candidateCapacity > selectedCapacity))
                {
                    selectedIndex = index;
                    selectedCapacity = candidateCapacity;
                }
            }
        }
        else
        {
            long leastEstimatedGrowthBytes = long.MaxValue;
            long leastReservedCapacityBytes = long.MaxValue;
            for (int index = 0; index < pieceMeshBuilderPool.Count; index++)
            {
                PieceMeshBuilder candidate = pieceMeshBuilderPool[index];
                int availableVertexCapacity = candidate.GetReusableVertexCapacity(
                    hasTangents, storeConnectivityVertexIds);
                int missingVertices = Mathf.Max(0,
                    estimatedVertexCapacity - availableVertexCapacity);
                long availableIndexCapacity;
                long missingIndices = sourceMeshForIndexSelection != null
                    ? candidate.GetSourceMeshIndexCapacityShortfall(
                        sourceMeshForIndexSelection, out availableIndexCapacity)
                    : candidate.GetTriangleIndexCapacityShortfall(
                        sourceTrianglesBySubMesh, out availableIndexCapacity);
                // 頂点属性とIndex/Areaの概算ペイロード。成長量ゼロの候補を優先し、
                // 同点なら小さめのBuilderを先に使用して大容量を残す。
                long estimatedGrowthBytes = (long)missingVertices * 48L +
                    missingIndices * 6L;
                long reservedBytes = (long)availableVertexCapacity * 48L +
                    availableIndexCapacity * 4L;
                if (estimatedGrowthBytes < leastEstimatedGrowthBytes ||
                    (estimatedGrowthBytes == leastEstimatedGrowthBytes &&
                     reservedBytes < leastReservedCapacityBytes))
                {
                    selectedIndex = index;
                    leastEstimatedGrowthBytes = estimatedGrowthBytes;
                    leastReservedCapacityBytes = reservedBytes;
                }
            }
        }

        PieceMeshBuilder builder = pieceMeshBuilderPool[selectedIndex];
        pieceMeshBuilderPool.RemoveAt(selectedIndex);

        try
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int positionCapacityBefore = builder.Vertices.Capacity;
            int normalCapacityBefore = builder.Normals.Capacity;
            int uvCapacityBefore = builder.Uv.Capacity;
            int tangentCapacityBefore = builder.Tangents.Capacity;
            int indexMapLengthBefore = builder.SourceIndexMapLength;
#endif
            using (acquireReuseBuilderProfilerMarker.Auto())
            {
                builder.PrepareForUse(
                    subMeshCount,
                    hasTangents,
                    sourceVertexCount,
                    estimatedVertexCapacity,
                    storeConnectivityVertexIds);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (captureCreatePieceBuilders)
            {
                reusedBuilderCount++;
                requestedVertexCapacitySum += estimatedVertexCapacity;
                builder.TrackCreateBuilderDiagnostics = true;
                RecordBuilderAcquisitionDetails(
                    builder, false, sourceVertexCount, estimatedVertexCapacity,
                    positionCapacityBefore, normalCapacityBefore,
                    uvCapacityBefore, tangentCapacityBefore, indexMapLengthBefore);
            }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (diagnosticOwner != null && diagnosticOwner.triangleLoopDiagnosticsActive)
            {
                builder.DiagnosticOwner = diagnosticOwner;
                diagnosticOwner.RecordTriangleLoopAcquisition(false);
            }
#endif
            return builder;
        }
        catch
        {
            // Prepare中に例外が発生しても、取得済みBuilderをPoolから失わない。
            pieceMeshBuilderPool.Add(builder);
            throw;
        }
    }

    private static void ReleasePieceMeshBuilder(
        PieceMeshBuilder builder)
    {
        if (builder == null)
        {
            return;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        RecordBuilderReleaseDiagnostics(builder);
        if (builder.DiagnosticOwner != null)
        {
            builder.DiagnosticOwner.RecordTriangleLoopRelease(builder);
            builder.DiagnosticOwner = null;
        }
#endif
        builder.ClearForPool();
        pieceMeshBuilderPool.Add(builder);
    }

    private static VertexData[] AcquireSourceVertexDataBuffer(
        int requiredLength,
        out bool returnToPool)
    {
        VertexData[] retainedBuffer =
            sourceVertexDataBuffer;

        if (retainedBuffer != null &&
            requiredLength <= retainedBuffer.Length &&
            Interlocked.CompareExchange(
                ref sourceVertexDataBufferLeaseState,
                1,
                0) == 0)
        {
            returnToPool = false;
            return retainedBuffer;
        }

        // 32,768頂点超過または想定外の再入時だけ、従来のPool経路へ退避する。
        // 通常のEnemy死亡ではBeforeSceneLoadで保持した専用Bufferを使用する。
        returnToPool = true;
        return ArrayPool<VertexData>.Shared.Rent(requiredLength);
    }

    private static void ReleaseSourceVertexDataBuffer(
        VertexData[] buffer,
        bool returnToPool)
    {
        if (buffer == null)
        {
            return;
        }

        if (returnToPool)
        {
            ArrayPool<VertexData>.Shared.Return(
                buffer,
                false);
            return;
        }

        Interlocked.Exchange(
            ref sourceVertexDataBufferLeaseState,
            0);
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

    private static TriangleIntersectionData ResolveTriangleIntersections(
        VertexData vertexA,
        VertexData vertexB,
        VertexData vertexC,
        float distanceA,
        float distanceB,
        float distanceC,
        float geometryEpsilon,
        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge,
        ref int nextConnectivityVertexId)
    {
        // Positive側Clipが従来処理していたEdge順(C-A, A-B, B-C)で解決する。
        // 厳密交差Edgeで新しいConnectivity IDが必要な場合も、割り当て順を変えない。
        bool hasEdgeCAIntersection =
            TryResolveClippingIntersection(
                vertexC,
                vertexA,
                distanceC,
                distanceA,
                geometryEpsilon,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId,
                out VertexData edgeCAIntersection);

        bool hasEdgeABIntersection =
            TryResolveClippingIntersection(
                vertexA,
                vertexB,
                distanceA,
                distanceB,
                geometryEpsilon,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId,
                out VertexData edgeABIntersection);

        bool hasEdgeBCIntersection =
            TryResolveClippingIntersection(
                vertexB,
                vertexC,
                distanceB,
                distanceC,
                geometryEpsilon,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId,
                out VertexData edgeBCIntersection);

        return new TriangleIntersectionData(
            hasEdgeABIntersection,
            edgeABIntersection,
            hasEdgeBCIntersection,
            edgeBCIntersection,
            hasEdgeCAIntersection,
            edgeCAIntersection);
    }

    private static bool TryResolveClippingIntersection(
        VertexData first,
        VertexData second,
        float firstDistance,
        float secondDistance,
        float geometryEpsilon,
        Dictionary<long, int> cutConnectivityVertexIdsBySourceEdge,
        ref int nextConnectivityVertexId,
        out VertexData intersection)
    {
        bool positiveSideChanges =
            IsInsideHalfSpace(
                firstDistance,
                true,
                geometryEpsilon) !=
            IsInsideHalfSpace(
                secondDistance,
                true,
                geometryEpsilon);

        bool negativeSideChanges =
            IsInsideHalfSpace(
                firstDistance,
                false,
                geometryEpsilon) !=
            IsInsideHalfSpace(
                secondDistance,
                false,
                geometryEpsilon);

        if (!positiveSideChanges &&
            !negativeSideChanges)
        {
            intersection = default;
            return false;
        }

        intersection =
            InterpolateToPlane(
                first,
                second,
                firstDistance,
                secondDistance,
                geometryEpsilon,
                cutConnectivityVertexIdsBySourceEdge,
                ref nextConnectivityVertexId);

        return true;
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
        in TriangleIntersectionData intersections)
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
            intersections.HasEdgeCAIntersection,
            intersections.EdgeCAIntersection);

        AddClippedEdge(
            vertexA,
            vertexB,
            distanceA,
            distanceB,
            keepPositiveSide,
            geometryEpsilon,
            output,
            intersections.HasEdgeABIntersection,
            intersections.EdgeABIntersection);

        AddClippedEdge(
            vertexB,
            vertexC,
            distanceB,
            distanceC,
            keepPositiveSide,
            geometryEpsilon,
            output,
            intersections.HasEdgeBCIntersection,
            intersections.EdgeBCIntersection);

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
        bool hasIntersection,
        VertexData intersection)
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
                // ResolveTriangleIntersectionsは同じinside判定から必要Edgeだけを先に解決するため、
                // この分岐へ入るEdgeには必ず対応する補間結果が存在する。
                if (hasIntersection)
                {
                    AddDistinctPolygonVertex(
                        output,
                        intersection,
                        geometryEpsilon);
                }
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

        if (hasIntersection)
        {
            AddDistinctPolygonVertex(
                output,
                intersection,
                geometryEpsilon);
        }
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

    /// <summary>
    /// Plane上Edgeを含むTriangle fanを確認し、正側と負側のSurfaceを隔てるEdgeだけを
    /// Cut Boundaryへ補完する。
    /// </summary>
    /// <remarks>
    /// Planeへ接しているだけのTriangleを一律Boundary化せず、同一の幾何Edgeに
    /// strict positive側とstrict negative側の隣接Triangleが実在するときだけ追加する。
    /// UV seam等でSource Vertex indexが分離されている場合はConnectivity IDを候補検索に使うが、
    /// 最終的なEdge同一性は既存SourceOrigin判定と同等のより厳しい位置許容値で再確認する。
    /// </remarks>
    private static bool TryAddPlaneTouchingVertexFanBoundarySegments(
        Mesh sourceMesh,
        IReadOnlyList<Vector3> sourceVertices,
        Plane cutPlane,
        float geometryEpsilon,
        List<CutSegment> cutSegments,
        out string failureReason)
    {
        if (sourceMesh == null ||
            sourceVertices == null ||
            cutSegments == null)
        {
            failureReason =
                "Plane上頂点fan解析に必要なSource Mesh情報が不足しています。";
            return false;
        }

        if (sourceVertices.Count != sourceMesh.vertexCount)
        {
            failureReason =
                "Plane上頂点fan解析のSource Vertex数がMeshと一致していません。";
            return false;
        }

        float sourceSplitVertexMatchEpsilon =
            geometryEpsilon *
            SOURCE_SPLIT_VERTEX_MATCH_EPSILON_SCALE;

        // UV seam / hard edgeでindexが分かれていても同じ幾何Edgeを候補として探せるようにする。
        // このIDだけでEdgeを確定せず、下の位置比較で既存Boundaryと同等の厳しい条件へ絞り込む。
        int[] sourceConnectivityVertexIds =
            MeshConnectedComponentSplitter.BuildConnectedVertexIds(
                sourceVertices,
                sourceMesh.bounds);

        var sourceVertexPlaneDistances =
            new float[sourceVertices.Count];

        for (int vertexIndex = 0;
             vertexIndex < sourceVertices.Count;
             vertexIndex++)
        {
            float distance =
                cutPlane.GetDistanceToPoint(
                    sourceVertices[vertexIndex]);

            if (!IsFinite(distance))
            {
                failureReason =
                    "Plane上頂点fan解析中の距離計算でNaN / Infinityを検出しました。";
                return false;
            }

            sourceVertexPlaneDistances[vertexIndex] =
                distance;
        }

        var candidateSegments =
            new List<CutSegment>();

        var candidateSideMasks =
            new List<byte>();

        var candidateHasCoplanarNeighbor =
            new List<bool>();

        var candidateIndicesByConnectivityEdge =
            new Dictionary<long, List<int>>();

        var triangleIndices =
            new List<int>();

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

            triangleIndices.Clear();
            sourceMesh.GetTriangles(
                triangleIndices,
                subMeshIndex);

            if (triangleIndices.Count % 3 != 0)
            {
                failureReason =
                    $"SubMesh {subMeshIndex} のTriangle index数が不正です。";
                return false;
            }

            for (int triangleIndexBufferOffset = 0;
                 triangleIndexBufferOffset < triangleIndices.Count;
                 triangleIndexBufferOffset += 3)
            {
                int indexA =
                    triangleIndices[triangleIndexBufferOffset];

                int indexB =
                    triangleIndices[triangleIndexBufferOffset + 1];

                int indexC =
                    triangleIndices[triangleIndexBufferOffset + 2];

                if (!IsValidVertexIndex(
                        indexA,
                        sourceVertices.Count) ||
                    !IsValidVertexIndex(
                        indexB,
                        sourceVertices.Count) ||
                    !IsValidVertexIndex(
                        indexC,
                        sourceVertices.Count))
                {
                    failureReason =
                        $"SubMesh {subMeshIndex} に範囲外の頂点indexがあります。";
                    return false;
                }

                RecordPlaneTouchingEdgeCandidate(
                    indexA,
                    indexB,
                    indexC,
                    sourceVertexPlaneDistances[indexA],
                    sourceVertexPlaneDistances[indexB],
                    sourceVertexPlaneDistances[indexC],
                    subMeshIndex,
                    triangleIndexBufferOffset,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon,
                    sourceVertices,
                    sourceConnectivityVertexIds,
                    candidateSegments,
                    candidateSideMasks,
                    candidateHasCoplanarNeighbor,
                    candidateIndicesByConnectivityEdge);

                RecordPlaneTouchingEdgeCandidate(
                    indexB,
                    indexC,
                    indexA,
                    sourceVertexPlaneDistances[indexB],
                    sourceVertexPlaneDistances[indexC],
                    sourceVertexPlaneDistances[indexA],
                    subMeshIndex,
                    triangleIndexBufferOffset,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon,
                    sourceVertices,
                    sourceConnectivityVertexIds,
                    candidateSegments,
                    candidateSideMasks,
                    candidateHasCoplanarNeighbor,
                    candidateIndicesByConnectivityEdge);

                RecordPlaneTouchingEdgeCandidate(
                    indexC,
                    indexA,
                    indexB,
                    sourceVertexPlaneDistances[indexC],
                    sourceVertexPlaneDistances[indexA],
                    sourceVertexPlaneDistances[indexB],
                    subMeshIndex,
                    triangleIndexBufferOffset,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon,
                    sourceVertices,
                    sourceConnectivityVertexIds,
                    candidateSegments,
                    candidateSideMasks,
                    candidateHasCoplanarNeighbor,
                    candidateIndicesByConnectivityEdge);
            }
        }

        for (int candidateIndex = 0;
             candidateIndex < candidateSegments.Count;
             candidateIndex++)
        {
            if (candidateSideMasks[candidateIndex] !=
                PLANE_TOUCHING_EDGE_BOTH_SIDES_MASK)
            {
                continue;
            }

            // このPlane上Edgeは正側Surfaceと負側Surfaceの境界なので、
            // Intersecting Triangle由来のSegmentと同じCut Boundaryへ接続する。
            cutSegments.Add(
                candidateSegments[candidateIndex]);
        }

        AddCoplanarBridgeSegmentsBetweenDegreeOneBoundaryNodes(
            sourceVertices,
            geometryEpsilon,
            sourceSplitVertexMatchEpsilon,
            cutSegments,
            candidateSegments,
            candidateSideMasks,
            candidateHasCoplanarNeighbor);

        failureReason = null;
        return true;
    }

    private static void RecordPlaneTouchingEdgeCandidate(
        int firstVertexIndex,
        int secondVertexIndex,
        int oppositeVertexIndex,
        float firstDistance,
        float secondDistance,
        float oppositeDistance,
        int sourceSubMeshIndex,
        int sourceTriangleIndexBufferOffset,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<int> sourceConnectivityVertexIds,
        List<CutSegment> candidateSegments,
        List<byte> candidateSideMasks,
        List<bool> candidateHasCoplanarNeighbor,
        Dictionary<long, List<int>> candidateIndicesByConnectivityEdge)
    {
        if (Mathf.Abs(firstDistance) > geometryEpsilon ||
            Mathf.Abs(secondDistance) > geometryEpsilon)
        {
            return;
        }

        byte sideMask;
        bool hasCoplanarNeighbor;

        if (oppositeDistance > geometryEpsilon)
        {
            sideMask =
                PLANE_TOUCHING_EDGE_POSITIVE_SIDE_MASK;

            hasCoplanarNeighbor = false;
        }
        else if (oppositeDistance < -geometryEpsilon)
        {
            sideMask =
                PLANE_TOUCHING_EDGE_NEGATIVE_SIDE_MASK;

            hasCoplanarNeighbor = false;
        }
        else
        {
            // 全頂点がepsilon帯に入るTriangle自体では正負を確定できない。
            // ただし隣接するstrict側Triangleと組み合わさったとき、
            // Degree1同士をTopology上の実Edgeで閉じるための候補として保持する。
            sideMask = 0;
            hasCoplanarNeighbor = true;
        }

        int firstConnectivityVertexId =
            sourceConnectivityVertexIds[firstVertexIndex];

        int secondConnectivityVertexId =
            sourceConnectivityVertexIds[secondVertexIndex];

        if (firstConnectivityVertexId ==
            secondConnectivityVertexId)
        {
            return;
        }

        long connectivityEdgeKey =
            CreateEdgeKey(
                firstConnectivityVertexId,
                secondConnectivityVertexId);

        if (!candidateIndicesByConnectivityEdge.TryGetValue(
                connectivityEdgeKey,
                out List<int> candidateIndices))
        {
            candidateIndices =
                new List<int>(1);

            candidateIndicesByConnectivityEdge.Add(
                connectivityEdgeKey,
                candidateIndices);
        }

        Vector3 firstPosition =
            sourceVertices[firstVertexIndex];

        Vector3 secondPosition =
            sourceVertices[secondVertexIndex];

        for (int candidateListIndex = 0;
             candidateListIndex < candidateIndices.Count;
             candidateListIndex++)
        {
            int candidateIndex =
                candidateIndices[candidateListIndex];

            CutSegment candidateSegment =
                candidateSegments[candidateIndex];

            if (!AreEquivalentPlaneTouchingEdges(
                    candidateSegment,
                    firstPosition,
                    secondPosition,
                    sourceSplitVertexMatchEpsilon))
            {
                continue;
            }

            candidateSideMasks[candidateIndex] =
                (byte)(candidateSideMasks[candidateIndex] |
                       sideMask);

            if (hasCoplanarNeighbor)
            {
                candidateHasCoplanarNeighbor[candidateIndex] =
                    true;
            }

            return;
        }

        int newCandidateIndex =
            candidateSegments.Count;

        // Connectivity IDはUV seamをまたぐ候補検索だけに使う。
        // 生成Segmentへは持ち込まず、既存2Mesh分割のBoundary / Cap側のConnectivity意味を変えない。
        candidateSegments.Add(
            new CutSegment(
                firstPosition,
                secondPosition,
                firstVertexIndex,
                firstVertexIndex,
                secondVertexIndex,
                secondVertexIndex,
                -1,
                -1,
                sourceSubMeshIndex,
                sourceTriangleIndexBufferOffset / 3,
                sourceTriangleIndexBufferOffset,
                firstVertexIndex,
                secondVertexIndex,
                oppositeVertexIndex));

        candidateSideMasks.Add(
            sideMask);

        candidateHasCoplanarNeighbor.Add(
            hasCoplanarNeighbor);

        candidateIndices.Add(
            newCandidateIndex);
    }

    /// <summary>
    /// strict片側TriangleとCoplanarWithinEpsilon Triangleが共有するPlane上Edgeのうち、
    /// 既存BoundaryでDegree1になっている2頂点を直接結ぶ実Edgeだけを補完する。
    /// </summary>
    /// <remarks>
    /// Coplanar Triangleの全EdgeをBoundary化すると分岐を作るため、
    /// すでに形成済みのBoundary終端2点を同時に解消できるEdgeだけに限定する。
    /// この処理はPlane上頂点fan対応ON時にしか呼ばれない。
    /// </remarks>
    private static void AddCoplanarBridgeSegmentsBetweenDegreeOneBoundaryNodes(
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon,
        List<CutSegment> cutSegments,
        IReadOnlyList<CutSegment> candidateSegments,
        IReadOnlyList<byte> candidateSideMasks,
        IReadOnlyList<bool> candidateHasCoplanarNeighbor)
    {
        if (cutSegments.Count == 0 ||
            candidateSegments.Count == 0)
        {
            return;
        }

        var nodes =
            new List<BoundaryNode>();

        var uniqueEdges =
            new HashSet<long>();

        for (int segmentIndex = 0;
             segmentIndex < cutSegments.Count;
             segmentIndex++)
        {
            CutSegment cutSegment =
                cutSegments[segmentIndex];

            int firstNodeIndex =
                FindOrCreateBoundaryNode(
                    nodes,
                    0,
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
                    0,
                    cutSegment.Second,
                    cutSegment.SecondSourceEdgeVertexIndexA,
                    cutSegment.SecondSourceEdgeVertexIndexB,
                    cutSegment.SecondConnectivityVertexId,
                    sourceVertices,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon);

            if (firstNodeIndex == secondNodeIndex)
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

        var repairedNodeIndices =
            new HashSet<int>();

        for (int candidateIndex = 0;
             candidateIndex < candidateSegments.Count;
             candidateIndex++)
        {
            byte sideMask =
                candidateSideMasks[candidateIndex];

            if (!candidateHasCoplanarNeighbor[candidateIndex] ||
                sideMask == 0 ||
                sideMask == PLANE_TOUCHING_EDGE_BOTH_SIDES_MASK)
            {
                continue;
            }

            CutSegment candidateSegment =
                candidateSegments[candidateIndex];

            int firstNodeIndex =
                FindMatchingDegreeOneBoundaryNode(
                    nodes,
                    candidateSegment.First,
                    candidateSegment.FirstSourceEdgeVertexIndexA,
                    sourceVertices,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon);

            int secondNodeIndex =
                FindMatchingDegreeOneBoundaryNode(
                    nodes,
                    candidateSegment.Second,
                    candidateSegment.SecondSourceEdgeVertexIndexA,
                    sourceVertices,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon);

            if (firstNodeIndex < 0 ||
                secondNodeIndex < 0 ||
                firstNodeIndex == secondNodeIndex ||
                repairedNodeIndices.Contains(firstNodeIndex) ||
                repairedNodeIndices.Contains(secondNodeIndex))
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

            // strict片側だけではBoundaryと断定しない。
            // Coplanar隣接を持ち、かつ現在のDegree1終端2点を同時に閉じる実Edgeだけを採用する。
            cutSegments.Add(candidateSegment);

            nodes[firstNodeIndex]
                .Neighbors
                .Add(secondNodeIndex);

            nodes[secondNodeIndex]
                .Neighbors
                .Add(firstNodeIndex);

            repairedNodeIndices.Add(firstNodeIndex);
            repairedNodeIndices.Add(secondNodeIndex);
        }
    }

    private static int FindMatchingDegreeOneBoundaryNode(
        IReadOnlyList<BoundaryNode> nodes,
        Vector3 position,
        int sourceVertexIndex,
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        for (int nodeIndex = 0;
             nodeIndex < nodes.Count;
             nodeIndex++)
        {
            BoundaryNode node =
                nodes[nodeIndex];

            if (node.Neighbors.Count != 1)
            {
                continue;
            }

            if (IsMatchingBoundaryNode(
                    node,
                    position,
                    sourceVertexIndex,
                    sourceVertexIndex,
                    sourceVertices,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon))
            {
                return nodeIndex;
            }
        }

        return -1;
    }

    private static bool AreEquivalentPlaneTouchingEdges(
        CutSegment candidateSegment,
        Vector3 firstPosition,
        Vector3 secondPosition,
        float sourceSplitVertexMatchEpsilon)
    {
        bool sameDirection =
            ArePositionsClose(
                candidateSegment.First,
                firstPosition,
                sourceSplitVertexMatchEpsilon) &&
            ArePositionsClose(
                candidateSegment.Second,
                secondPosition,
                sourceSplitVertexMatchEpsilon);

        if (sameDirection)
        {
            return true;
        }

        return
            ArePositionsClose(
                candidateSegment.First,
                secondPosition,
                sourceSplitVertexMatchEpsilon) &&
            ArePositionsClose(
                candidateSegment.Second,
                firstPosition,
                sourceSplitVertexMatchEpsilon);
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
        in TriangleIntersectionData intersections,
        List<Vector3> points,
        List<int> pointSourceEdgeVertexIndexA,
        List<int> pointSourceEdgeVertexIndexB,
        List<int> pointConnectivityVertexIds,
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
                intersections.HasEdgeABIntersection,
                intersections.EdgeABIntersection,
                points,
                pointSourceEdgeVertexIndexA,
                pointSourceEdgeVertexIndexB,
                pointConnectivityVertexIds) ||
            !TryAddCutPointsForEdge(
                vertexB,
                vertexC,
                sourceVertexIndexB,
                sourceVertexIndexC,
                distanceB,
                distanceC,
                geometryEpsilon,
                intersections.HasEdgeBCIntersection,
                intersections.EdgeBCIntersection,
                points,
                pointSourceEdgeVertexIndexA,
                pointSourceEdgeVertexIndexB,
                pointConnectivityVertexIds) ||
            !TryAddCutPointsForEdge(
                vertexC,
                vertexA,
                sourceVertexIndexC,
                sourceVertexIndexA,
                distanceC,
                distanceA,
                geometryEpsilon,
                intersections.HasEdgeCAIntersection,
                intersections.EdgeCAIntersection,
                points,
                pointSourceEdgeVertexIndexA,
                pointSourceEdgeVertexIndexB,
                pointConnectivityVertexIds))
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
        bool hasIntersection,
        VertexData intersection,
        List<Vector3> points,
        List<int> pointSourceEdgeVertexIndexA,
        List<int> pointSourceEdgeVertexIndexB,
        List<int> pointConnectivityVertexIds)
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

        // 厳密交差EdgeはClip用に解決済みの同一VertexDataを使う。
        // Plane上扱いVertexはこの分岐へ入らず、従来どおり元VertexをCutPointとして保持する。
        if (!hasIntersection ||
            !IsFinite(intersection.Position))
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
        bool useConnectivityLookup,
        int connectivityVertexIdUpperBound,
        bool enableDiagnostics,
        out List<List<Vector3>> boundaryLoops,
        out List<List<int>> boundaryConnectivityVertexIds,
        out List<List<int>> boundarySourceVertexIndices,
        out List<Vector3> uncappedBoundaryEdgePoints,
        out List<List<int>> uncappedBoundarySourceVertexIndices,
        out int openBoundaryPathCount,
        out int ambiguousBoundaryComponentCount,
        out string failureReason)
    {
        using (buildBoundaryLoopsProfilerMarker.Auto())
        {
        float sourceSplitVertexMatchEpsilon =
            geometryEpsilon *
            SOURCE_SPLIT_VERTEX_MATCH_EPSILON_SCALE;

        int boundaryConnectivityLookupGeneration =
            useConnectivityLookup &&
            connectivityVertexIdUpperBound > 0
                ? BeginBoundaryConnectivityLookup(
                    connectivityVertexIdUpperBound)
                : 0;

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

        // 通常攻撃のConnectivity ID索引には触れない。食い込み分割だけ大規模化時に索引を作る。
        bool spatialLookupUsable =
            !useConnectivityLookup &&
            geometryEpsilon > 0f &&
            !float.IsNaN(geometryEpsilon) &&
            !float.IsInfinity(geometryEpsilon) &&
            !float.IsInfinity(geometryEpsilon * geometryEpsilon);

        Dictionary<Vector3Int, int> spatialNodeHeads = null;
        Vector3 spatialLookupOrigin = Vector3.zero;

        using (buildBoundaryGraphProfilerMarker.Auto())
        {
        try
        {
        for (int i = 0;
             i < cutSegments.Count;
             i++)
        {
            CutSegment cutSegment =
                cutSegments[i];

            int firstNodeIndex =
                !useConnectivityLookup
                    ? FindOrCreateBoundaryNodeBySpatialLookup(
                        nodes,
                        ref spatialNodeHeads,
                        ref spatialLookupOrigin,
                        ref spatialLookupUsable,
                        cutSegment.First,
                        cutSegment.FirstSourceEdgeVertexIndexA,
                        cutSegment.FirstSourceEdgeVertexIndexB,
                        cutSegment.FirstConnectivityVertexId,
                        sourceVertices,
                        geometryEpsilon,
                        sourceSplitVertexMatchEpsilon)
                    : FindOrCreateBoundaryNode(
                        nodes,
                        boundaryConnectivityLookupGeneration,
                        cutSegment.First,
                        cutSegment.FirstSourceEdgeVertexIndexA,
                        cutSegment.FirstSourceEdgeVertexIndexB,
                        cutSegment.FirstConnectivityVertexId,
                        sourceVertices,
                        geometryEpsilon,
                        sourceSplitVertexMatchEpsilon);

            int secondNodeIndex =
                !useConnectivityLookup
                    ? FindOrCreateBoundaryNodeBySpatialLookup(
                        nodes,
                        ref spatialNodeHeads,
                        ref spatialLookupOrigin,
                        ref spatialLookupUsable,
                        cutSegment.Second,
                        cutSegment.SecondSourceEdgeVertexIndexA,
                        cutSegment.SecondSourceEdgeVertexIndexB,
                        cutSegment.SecondConnectivityVertexId,
                        sourceVertices,
                        geometryEpsilon,
                        sourceSplitVertexMatchEpsilon)
                    : FindOrCreateBoundaryNode(
                        nodes,
                        boundaryConnectivityLookupGeneration,
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
        }
        finally
        {
            if (spatialNodeHeads != null)
            {
                spatialNodeHeads.Clear();
                boundarySpatialLookupPool.Push(spatialNodeHeads);
            }
        }
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

        boundaryLoops = new List<List<Vector3>>();
        boundaryConnectivityVertexIds = new List<List<int>>();
        boundarySourceVertexIndices = new List<List<int>>();
        uncappedBoundaryEdgePoints = new List<Vector3>();
        uncappedBoundarySourceVertexIndices = new List<List<int>>();
        openBoundaryPathCount = 0;
        ambiguousBoundaryComponentCount = 0;

        if (uniqueEdges.Count == 0)
        {
            failureReason = "有効な切断境界Edgeが存在しません。";
            return false;
        }

        // 連結成分ごとに判定する。Degree=1は元Meshの開放境界に達する
        // 切断線として保持し、別成分の閉ループから作れるCapまで破棄しない。
        // Degree>=3や閉じない巡回は接続を推測せず、当該成分をCap対象外とする。
        var visitedNodes = new bool[nodes.Count];
        var queuedNodes = new Queue<int>();
        var componentNodes = new List<int>();
        var visitedLoopEdges = new HashSet<long>();

        using (traceBoundaryComponentsProfilerMarker.Auto())
        {
        for (int startNodeIndex = 0;
             startNodeIndex < nodes.Count;
             startNodeIndex++)
        {
            if (visitedNodes[startNodeIndex])
            {
                continue;
            }

            queuedNodes.Enqueue(startNodeIndex);
            visitedNodes[startNodeIndex] = true;
            componentNodes.Clear();

            int degreeOneCount = 0;
            bool allDegreeTwo = true;
            bool hasBranch = false;

            while (queuedNodes.Count > 0)
            {
                int currentNodeIndex = queuedNodes.Dequeue();
                componentNodes.Add(currentNodeIndex);

                int degree = nodes[currentNodeIndex].Neighbors.Count;
                if (degree != 2)
                {
                    allDegreeTwo = false;
                }

                if (degree == 1)
                {
                    degreeOneCount++;
                }
                else if (degree > 2 || degree == 0)
                {
                    hasBranch = true;
                }

                foreach (int neighbor in nodes[currentNodeIndex].Neighbors)
                {
                    if (!visitedNodes[neighbor])
                    {
                        visitedNodes[neighbor] = true;
                        queuedNodes.Enqueue(neighbor);
                    }
                }
            }

            if (allDegreeTwo && componentNodes.Count >= 3)
            {
                visitedLoopEdges.Clear();
                int firstNodeIndex = componentNodes[0];

                if (TryTraceBoundaryLoop(
                        nodes,
                        firstNodeIndex,
                        nodes[firstNodeIndex].Neighbors[0],
                        visitedLoopEdges,
                        out List<Vector3> loop,
                        out List<int> loopConnectivityVertexIds,
                        out List<int> loopSourceVertexIndices) &&
                    loop.Count == componentNodes.Count &&
                    visitedLoopEdges.Count == componentNodes.Count)
                {
                    boundaryLoops.Add(loop);
                    boundaryConnectivityVertexIds.Add(loopConnectivityVertexIds);
                    boundarySourceVertexIndices.Add(loopSourceVertexIndices);
                    continue;
                }
            }

            if (!hasBranch && degreeOneCount == 2)
            {
                openBoundaryPathCount++;
            }
            else
            {
                ambiguousBoundaryComponentCount++;
            }

            // 非閉鎖成分の全Edgeを後段へ渡す。Cap候補の領域と投影が
            // 重なる場合、そのCapは元の穴を塞ぐ恐れがあるため作らない。
            foreach (int nodeIndex in componentNodes)
            {
                foreach (int neighbor in nodes[nodeIndex].Neighbors)
                {
                    if (nodeIndex >= neighbor)
                    {
                        continue;
                    }

                    uncappedBoundaryEdgePoints.Add(nodes[nodeIndex].Position);
                    uncappedBoundaryEdgePoints.Add(nodes[neighbor].Position);

                    // 元の穴を別の独立部品のCapと混同しないため、
                    // 各Edge両端の生成元Vertexを保持する。
                    var edgeSourceVertices = new List<int>();
                    AppendBoundaryNodeSourceVertices(nodes[nodeIndex], edgeSourceVertices);
                    AppendBoundaryNodeSourceVertices(nodes[neighbor], edgeSourceVertices);
                    uncappedBoundarySourceVertexIndices.Add(edgeSourceVertices);
                }
            }
        }
        }

        // 閉路も開放経路も特定できず、分岐等の不正接続だけが残る場合は
        // 幾何学的に成立する切断境界を保証できないので分割成功にしない。
        if (boundaryLoops.Count == 0 && openBoundaryPathCount == 0)
        {
            failureReason =
                "切断境界に有効な閉ループ・開放経路がなく、" +
                $"不確定な接続成分のみ存在します。Components={ambiguousBoundaryComponentCount}";
            return false;
        }

        if (enableDiagnostics)
        {
            Debug.Log(
                "[MeshCutBoundaryDiagnostic][BoundaryComponents] " +
                $"ClosedLoops={boundaryLoops.Count}, " +
                $"OpenPaths={openBoundaryPathCount}, " +
                $"AmbiguousComponents={ambiguousBoundaryComponentCount}, " +
                $"UncappedEdges={uncappedBoundaryEdgePoints.Count / 2}");
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

            List<int> triangles =
                AcquireReusableList(
                    diagnosticTriangleIndexListPool,
                    0);

            try
            {
                sourceMesh.GetTriangles(
                    triangles,
                    subMeshIndex);

                for (int triangleIndexBufferOffset = 0;
                     triangleIndexBufferOffset < triangles.Count;
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
            finally
            {
                ReleaseReusableList(
                    diagnosticTriangleIndexListPool,
                    triangles);
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
        int lookupGeneration,
        Vector3 position,
        int sourceEdgeVertexIndexA,
        int sourceEdgeVertexIndexB,
        int connectivityVertexId,
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        if (lookupGeneration > 0 &&
            connectivityVertexId >= 0 &&
            connectivityVertexId <
            boundaryNodeGenerationByConnectivityVertexId.Length)
        {
            return FindOrCreateBoundaryNodeByConnectivityId(
                nodes,
                lookupGeneration,
                position,
                sourceEdgeVertexIndexA,
                sourceEdgeVertexIndexB,
                connectivityVertexId,
                sourceVertices,
                geometryEpsilon,
                sourceSplitVertexMatchEpsilon);
        }

        return FindOrCreateBoundaryNodeByLinearSearch(
            nodes,
            position,
            sourceEdgeVertexIndexA,
            sourceEdgeVertexIndexB,
            connectivityVertexId,
            sourceVertices,
            geometryEpsilon,
            sourceSplitVertexMatchEpsilon);
    }

    private static int FindOrCreateBoundaryNodeByConnectivityId(
        List<BoundaryNode> nodes,
        int lookupGeneration,
        Vector3 position,
        int sourceEdgeVertexIndexA,
        int sourceEdgeVertexIndexB,
        int connectivityVertexId,
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        if (boundaryNodeGenerationByConnectivityVertexId[connectivityVertexId] ==
            lookupGeneration)
        {
            int currentNodeIndex =
                boundaryNodeIndexByConnectivityVertexId[
                    connectivityVertexId];

            int lastNodeIndex = -1;

            while (currentNodeIndex >= 0)
            {
                BoundaryNode node =
                    nodes[currentNodeIndex];

                if (IsMatchingBoundaryNode(
                        node,
                        position,
                        sourceEdgeVertexIndexA,
                        sourceEdgeVertexIndexB,
                        sourceVertices,
                        geometryEpsilon,
                        sourceSplitVertexMatchEpsilon))
                {
                    node.AddSourceOrigin(
                        sourceEdgeVertexIndexA,
                        sourceEdgeVertexIndexB);

                    return currentNodeIndex;
                }

                lastNodeIndex = currentNodeIndex;
                currentNodeIndex =
                    node.NextSameConnectivityNodeIndex;
            }

            int additionalNodeIndex =
                AddBoundaryNode(
                    nodes,
                    position,
                    sourceEdgeVertexIndexA,
                    sourceEdgeVertexIndexB,
                    connectivityVertexId);

            // Source Connectivity IDはPosition epsilon内で共有されるため、
            // ID一致だけでは従来のTopology同一性を保証できない。
            // 全Nodeではなく同じIDのNodeだけを生成順で辿り、従来条件を維持する。
            nodes[lastNodeIndex].NextSameConnectivityNodeIndex =
                additionalNodeIndex;

            return additionalNodeIndex;
        }

        int nodeIndex =
            AddBoundaryNode(
                nodes,
                position,
                sourceEdgeVertexIndexA,
                sourceEdgeVertexIndexB,
                connectivityVertexId);

        boundaryNodeIndexByConnectivityVertexId[connectivityVertexId] =
            nodeIndex;

        boundaryNodeGenerationByConnectivityVertexId[connectivityVertexId] =
            lookupGeneration;

        return nodeIndex;
    }

    /// <summary>
    /// 食い込み分割の境界ノードだけをセル索引で候補削減する。
    /// </summary>
    /// <remarks>
    /// Node Indexは生成順のため、一致候補の最小Indexを選べば従来の先頭からの線形探索と一致する。
    /// 位置は候補選別にのみ使い、Seam / 元Edge / Connectivityの最終判定は既存処理を維持する。
    /// </remarks>
    private static int FindOrCreateBoundaryNodeBySpatialLookup(
        List<BoundaryNode> nodes,
        ref Dictionary<Vector3Int, int> spatialNodeHeads,
        ref Vector3 spatialLookupOrigin,
        ref bool spatialLookupUsable,
        Vector3 position,
        int sourceEdgeVertexIndexA,
        int sourceEdgeVertexIndexB,
        int connectivityVertexId,
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        if (!spatialLookupUsable ||
            (spatialNodeHeads == null &&
             nodes.Count < BOUNDARY_SPATIAL_LOOKUP_MIN_NODE_COUNT))
        {
            return FindOrCreateBoundaryNodeByLinearSearch(
                nodes, position, sourceEdgeVertexIndexA, sourceEdgeVertexIndexB,
                connectivityVertexId, sourceVertices, geometryEpsilon,
                sourceSplitVertexMatchEpsilon);
        }

        double cellSize = (double)geometryEpsilon * 2d;

        if (spatialNodeHeads == null)
        {
            spatialLookupOrigin = nodes[0].Position;
            spatialNodeHeads = boundarySpatialLookupPool.Count > 0
                ? boundarySpatialLookupPool.Pop()
                : new Dictionary<Vector3Int, int>(
                    BOUNDARY_SPATIAL_LOOKUP_MIN_NODE_COUNT * 2);

            // 64ノードまでは追加順の線形探索。索引構築時も同じ生成Indexを維持する。
            for (int nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
            {
                if (!TryGetBoundarySpatialCell(
                        nodes[nodeIndex].Position,
                        spatialLookupOrigin,
                        cellSize,
                        out Vector3Int cell))
                {
                    spatialLookupUsable = false;
                    break;
                }

                AddNodeToBoundarySpatialCell(
                    nodes, spatialNodeHeads, cell, nodeIndex);
            }
        }

        // 無効座標や整数セルの範囲外ではセル近傍を保証できないため線形探索へ戻す。
        // 一度無効と判定した境界では、未索引ノードを取りこぼさないよう復帰しない。
        if (!spatialLookupUsable ||
            !TryGetBoundarySpatialCellRange(
                position,
                spatialLookupOrigin,
                cellSize,
                (double)geometryEpsilon * BOUNDARY_SPATIAL_SEARCH_EPSILON_SCALE,
                out Vector3Int minimumCell,
                out Vector3Int maximumCell))
        {
            spatialLookupUsable = false;
            return FindOrCreateBoundaryNodeByLinearSearch(
                nodes, position, sourceEdgeVertexIndexA, sourceEdgeVertexIndexB,
                connectivityVertexId, sourceVertices, geometryEpsilon,
                sourceSplitVertexMatchEpsilon);
        }

        int firstMatchingNodeIndex = int.MaxValue;

        for (int z = minimumCell.z; z <= maximumCell.z; z++)
        {
            for (int y = minimumCell.y; y <= maximumCell.y; y++)
            {
                for (int x = minimumCell.x; x <= maximumCell.x; x++)
                {
                    if (!spatialNodeHeads.TryGetValue(
                            new Vector3Int(x, y, z),
                            out int nodeIndex))
                    {
                        continue;
                    }

                    while (nodeIndex >= 0)
                    {
                        BoundaryNode node = nodes[nodeIndex];

                        if (nodeIndex < firstMatchingNodeIndex &&
                            (connectivityVertexId < 0 ||
                             node.ConnectivityVertexId < 0 ||
                             node.ConnectivityVertexId == connectivityVertexId) &&
                            IsMatchingBoundaryNode(
                                node, position,
                                sourceEdgeVertexIndexA,
                                sourceEdgeVertexIndexB,
                                sourceVertices, geometryEpsilon,
                                sourceSplitVertexMatchEpsilon))
                        {
                            firstMatchingNodeIndex = nodeIndex;
                        }

                        nodeIndex = node.NextSameSpatialCellNodeIndex;
                    }
                }
            }
        }

        if (firstMatchingNodeIndex != int.MaxValue)
        {
            nodes[firstMatchingNodeIndex].AddSourceOrigin(
                sourceEdgeVertexIndexA, sourceEdgeVertexIndexB);
            return firstMatchingNodeIndex;
        }

        int newNodeIndex = AddBoundaryNode(
            nodes, position, sourceEdgeVertexIndexA,
            sourceEdgeVertexIndexB, connectivityVertexId);

        if (TryGetBoundarySpatialCell(
                position,
                spatialLookupOrigin,
                cellSize,
                out Vector3Int newNodeCell))
        {
            AddNodeToBoundarySpatialCell(
                nodes, spatialNodeHeads, newNodeCell, newNodeIndex);
        }
        else
        {
            spatialLookupUsable = false;
        }

        return newNodeIndex;
    }

    private static void AddNodeToBoundarySpatialCell(
        List<BoundaryNode> nodes,
        Dictionary<Vector3Int, int> spatialNodeHeads,
        Vector3Int cell,
        int nodeIndex)
    {
        nodes[nodeIndex].NextSameSpatialCellNodeIndex =
            spatialNodeHeads.TryGetValue(cell, out int previousHeadIndex)
                ? previousHeadIndex
                : -1;

        spatialNodeHeads[cell] = nodeIndex;
    }

    private static bool TryGetBoundarySpatialCell(
        Vector3 position,
        Vector3 origin,
        double cellSize,
        out Vector3Int cell)
    {
        cell = default(Vector3Int);
        if (!TryGetBoundarySpatialCoordinate(
                (double)position.x - origin.x, cellSize, out int x) ||
            !TryGetBoundarySpatialCoordinate(
                (double)position.y - origin.y, cellSize, out int y) ||
            !TryGetBoundarySpatialCoordinate(
                (double)position.z - origin.z, cellSize, out int z))
        {
            return false;
        }

        cell = new Vector3Int(x, y, z);
        return true;
    }

    private static bool TryGetBoundarySpatialCellRange(
        Vector3 position,
        Vector3 origin,
        double cellSize,
        double radius,
        out Vector3Int minimumCell,
        out Vector3Int maximumCell)
    {
        minimumCell = default(Vector3Int);
        maximumCell = default(Vector3Int);

        double x = (double)position.x - origin.x;
        double y = (double)position.y - origin.y;
        double z = (double)position.z - origin.z;

        if (!TryGetBoundarySpatialCoordinate(x - radius, cellSize, out int minX) ||
            !TryGetBoundarySpatialCoordinate(y - radius, cellSize, out int minY) ||
            !TryGetBoundarySpatialCoordinate(z - radius, cellSize, out int minZ) ||
            !TryGetBoundarySpatialCoordinate(x + radius, cellSize, out int maxX) ||
            !TryGetBoundarySpatialCoordinate(y + radius, cellSize, out int maxY) ||
            !TryGetBoundarySpatialCoordinate(z + radius, cellSize, out int maxZ))
        {
            return false;
        }

        minimumCell = new Vector3Int(minX, minY, minZ);
        maximumCell = new Vector3Int(maxX, maxY, maxZ);
        return true;
    }

    private static bool TryGetBoundarySpatialCoordinate(
        double offset,
        double cellSize,
        out int coordinate)
    {
        coordinate = 0;
        double value = Math.Floor(offset / cellSize);

        // 近傍セルの列挙でintがオーバーフローしない範囲だけを索引化する。
        if (double.IsNaN(value) ||
            double.IsInfinity(value) ||
            value <= int.MinValue ||
            value >= int.MaxValue)
        {
            return false;
        }

        coordinate = (int)value;
        return true;
    }

    private static int FindOrCreateBoundaryNodeByLinearSearch(
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

            if (!IsMatchingBoundaryNode(
                    node,
                    position,
                    sourceEdgeVertexIndexA,
                    sourceEdgeVertexIndexB,
                    sourceVertices,
                    geometryEpsilon,
                    sourceSplitVertexMatchEpsilon))
            {
                continue;
            }

            node.AddSourceOrigin(
                sourceEdgeVertexIndexA,
                sourceEdgeVertexIndexB);

            return i;
        }

        return AddBoundaryNode(
            nodes,
            position,
            sourceEdgeVertexIndexA,
            sourceEdgeVertexIndexB,
            connectivityVertexId);
    }

    private static bool IsMatchingBoundaryNode(
        BoundaryNode node,
        Vector3 position,
        int sourceEdgeVertexIndexA,
        int sourceEdgeVertexIndexB,
        IReadOnlyList<Vector3> sourceVertices,
        float geometryEpsilon,
        float sourceSplitVertexMatchEpsilon)
    {
        if (!ArePositionsClose(
                node.Position,
                position,
                geometryEpsilon))
        {
            return false;
        }

        return node.HasEquivalentSourceOrigin(
            sourceEdgeVertexIndexA,
            sourceEdgeVertexIndexB,
            sourceVertices,
            sourceSplitVertexMatchEpsilon);
    }

    private static int AddBoundaryNode(
        List<BoundaryNode> nodes,
        Vector3 position,
        int sourceEdgeVertexIndexA,
        int sourceEdgeVertexIndexB,
        int connectivityVertexId)
    {
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

    private static void AppendBoundaryNodeSourceVertices(
        BoundaryNode node,
        List<int> sourceVertexIndices)
    {
        for (int originIndex = 0;
             originIndex < node.SourceOriginCount;
             originIndex++)
        {
            node.GetSourceOrigin(
                originIndex,
                out int sourceVertexIndexA,
                out int sourceVertexIndexB);

            sourceVertexIndices.Add(sourceVertexIndexA);
            sourceVertexIndices.Add(sourceVertexIndexB);
        }
    }

    private static bool TryTraceBoundaryLoop(
        IReadOnlyList<BoundaryNode> nodes,
        int startNode,
        int firstNextNode,
        HashSet<long> visitedEdges,
        out List<Vector3> loop,
        out List<int> loopConnectivityVertexIds,
        out List<int> loopSourceVertexIndices)
    {
        loopSourceVertexIndices = new List<int>();
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

            AppendBoundaryNodeSourceVertices(
                nodes[currentNode], loopSourceVertexIndices);

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
                loopSourceVertexIndices = null;
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
                loopSourceVertexIndices = null;
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
        IReadOnlyList<List<int>> boundarySourceVertexIndices,
        IReadOnlyList<CutSegment> cutSegments,
        Mesh sourceMesh,
        Bounds sourceBounds,
        int sourceSubMeshCount,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<List<int>> cachedSourceTrianglesBySubMesh,
        IReadOnlyList<int> sourceConnectivityVertexIds,
        Plane cutPlane,
        float cutSurfaceUvScale,
        float geometryEpsilon,
        int cutSurfaceSubMeshIndex,
        IReadOnlyList<Vector3> uncappedBoundaryEdgePoints,
        IReadOnlyList<List<int>> uncappedBoundarySourceVertexIndices,
        bool enableDiagnostics,
        PieceMeshBuilder firstBuilder,
        PieceMeshBuilder secondBuilder,
        out int generatedCapRegionCount,
        out int skippedCapRegionCount,
        out string failureReason)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        try
        {
#endif
        using (addCutSurfaceCapsProfilerMarker.Auto())
        {
        generatedCapRegionCount = 0;
        skippedCapRegionCount = 0;
        failureReason = null;

        if (uncappedBoundaryEdgePoints == null ||
            (uncappedBoundaryEdgePoints.Count & 1) != 0 ||
            boundaryLoops == null ||
            boundaryConnectivityVertexIds == null ||
            boundaryLoops.Count != boundaryConnectivityVertexIds.Count ||
            boundarySourceVertexIndices == null ||
            boundaryLoops.Count != boundarySourceVertexIndices.Count ||
            uncappedBoundarySourceVertexIndices == null ||
            uncappedBoundaryEdgePoints.Count / 2 != uncappedBoundarySourceVertexIndices.Count ||
            cutSegments == null)
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

        // Open Pathとの交差検証で、Cap三角形ごとに同じ頂点を再投影しない。
        // 閉ループのみではこの追加作業領域を確保しない。
        List<Vector2> projectedUncappedBoundaryEdges = null;
        if (uncappedBoundaryEdgePoints.Count > 0)
        {
            projectedUncappedBoundaryEdges =
                new List<Vector2>(uncappedBoundaryEdgePoints.Count);
            for (int i = 0; i < uncappedBoundaryEdgePoints.Count; i++)
            {
                projectedUncappedBoundaryEdges.Add(ProjectToPlane2D(
                    uncappedBoundaryEdgePoints[i], planeAxisU, planeAxisV));
            }
        }

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
                    uncappedBoundaryEdgePoints.Count > 0,
                    out PreparedBoundaryLoop preparedLoop))
            {
                failureReason =
                    "切断境界を断面三角形分割用の単純Polygonへ変換できませんでした。";

                // 欠落した内周を無視すると外周Capが元の穴を埋めてしまう。
                // 開放境界を含むMeshは安全側に倒し、今回は全Capを見送る。
                if (uncappedBoundaryEdgePoints.Count > 0)
                {
                    skippedCapRegionCount = boundaryLoops.Count;
                    if (enableDiagnostics)
                    {
                        Debug.LogWarning(
                            "[MeshCutBoundaryDiagnostic][CapSkipped] " + failureReason);
                    }
                    failureReason = null;
                    return true;
                }

                return false;
            }

            preparedLoops.Add(
                preparedLoop);
        }

        if (uncappedBoundaryEdgePoints.Count == 0 &&
            !TryAssignLoopHierarchy(
                preparedLoops,
                geometryEpsilon,
                null,
                out failureReason))
        {
            // 開放境界のない従来経路では幾何学的失敗を成功扱いしない。
            return false;
        }

        // 開放線と閉ループが異なる元Mesh連結部品に属するなら、
        // Plane投影が重なっていても開放線はそのCapを妨げない。
        // 閉ループのみの場合は追加のTopology走査を行わず、従来経路を維持する。
        int[] boundaryLoopSourceComponentIds = null;
        int[] uncappedBoundarySourceComponentIds = null;
        int[] boundaryLoopLocalSheetIds = null;
        int[] uncappedBoundaryLocalSheetIds = null;
        if (uncappedBoundaryEdgePoints.Count > 0 && preparedLoops.Count > 0)
        {
            // Seamの頂点indexは異なっても、元の幾何Edgeは同一視する。
            // ただし位置が同じだけの別部品をEdgeとして無条件に接続しない。
            IReadOnlyList<int> weldedSourceVertexIds =
                sourceConnectivityVertexIds ??
                MeshConnectedComponentSplitter.BuildConnectedVertexIds(
                    sourceVertices, sourceBounds);

            int[] sourceComponentByVertexIndex = BuildSourceVertexComponents(
                sourceMesh,
                sourceBounds,
                sourceSubMeshCount,
                sourceVertices,
                cachedSourceTrianglesBySubMesh,
                weldedSourceVertexIds,
                cutSegments,
                out Dictionary<long, int> originalEdgeIncidenceCounts);

            int[] localSheetBySourceVertexIndex = BuildCutBoundaryLocalSheetIds(
                cutSegments,
                sourceVertices,
                weldedSourceVertexIds,
                originalEdgeIncidenceCounts,
                geometryEpsilon * SOURCE_SPLIT_VERTEX_MATCH_EPSILON_SCALE,
                out int ambiguousCutEdgeCount);

            boundaryLoopSourceComponentIds = new int[boundarySourceVertexIndices.Count];
            boundaryLoopLocalSheetIds = new int[boundarySourceVertexIndices.Count];
            for (int i = 0; i < boundaryLoopSourceComponentIds.Length; i++)
            {
                boundaryLoopSourceComponentIds[i] = GetBoundarySourceComponent(
                    boundarySourceVertexIndices[i], sourceComponentByVertexIndex);
                boundaryLoopLocalSheetIds[i] = GetBoundarySourceComponent(
                    boundarySourceVertexIndices[i], localSheetBySourceVertexIndex);
            }

            uncappedBoundarySourceComponentIds = new int[uncappedBoundarySourceVertexIndices.Count];
            uncappedBoundaryLocalSheetIds = new int[uncappedBoundarySourceVertexIndices.Count];
            for (int i = 0; i < uncappedBoundarySourceComponentIds.Length; i++)
            {
                uncappedBoundarySourceComponentIds[i] = GetBoundarySourceComponent(
                    uncappedBoundarySourceVertexIndices[i], sourceComponentByVertexIndex);
                uncappedBoundaryLocalSheetIds[i] = GetBoundarySourceComponent(
                    uncappedBoundarySourceVertexIndices[i], localSheetBySourceVertexIndex);
            }

            if (enableDiagnostics)
            {
                Debug.Log(
                    "[MeshCutBoundaryDiagnostic][LocalSheets] " +
                    $"ClosedLoops={boundaryLoopLocalSheetIds.Length}, " +
                    $"OpenEdges={uncappedBoundaryLocalSheetIds.Length}, " +
                    $"AmbiguousCrossingEdges={ambiguousCutEdgeCount}");
            }

            // 独立部品同士の投影だけによる入れ子判定は不正な穴を作る。
            if (!TryAssignLoopHierarchy(
                    preparedLoops,
                    geometryEpsilon,
                    boundaryLoopSourceComponentIds,
                    out failureReason))
            {
                skippedCapRegionCount = boundaryLoops.Count;
                if (enableDiagnostics)
                {
                    Debug.LogWarning(
                        "[MeshCutBoundaryDiagnostic][CapSkipped] " + failureReason);
                }
                failureReason = null;
                return true;
            }
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

            int capSourceComponentId =
                boundaryLoopSourceComponentIds != null
                    ? boundaryLoopSourceComponentIds[outerLoopIndex]
                    : -1;

            // 外周と内周は一つのCap領域に属する。内周の開放線も保護する。
            // 閉ループだけの従来経路では不要な集合を確保しない。
            HashSet<int> regionLocalSheetIds = null;
            if (boundaryLoopLocalSheetIds != null)
            {
                regionLocalSheetIds = new HashSet<int>();
                if (boundaryLoopLocalSheetIds[outerLoopIndex] >= 0)
                {
                    regionLocalSheetIds.Add(boundaryLoopLocalSheetIds[outerLoopIndex]);
                }
                for (int loopIndex = 0; loopIndex < preparedLoops.Count; loopIndex++)
                {
                    if (preparedLoops[loopIndex].ParentIndex == outerLoopIndex &&
                        boundaryLoopLocalSheetIds[loopIndex] >= 0)
                    {
                        regionLocalSheetIds.Add(boundaryLoopLocalSheetIds[loopIndex]);
                    }
                }
            }

            if (uncappedBoundaryEdgePoints.Count > 0 &&
                DoesUncappedBoundaryOverlapCapRegion(
                    outerLoop.ProjectedPoints,
                    directHoles,
                    projectedUncappedBoundaryEdges,
                    capSourceComponentId,
                    uncappedBoundarySourceComponentIds,
                    regionLocalSheetIds,
                    uncappedBoundaryLocalSheetIds,
                    geometryEpsilon))
            {
                skippedCapRegionCount++;
                if (enableDiagnostics)
                {
                    Debug.LogWarning(
                        "[MeshCutBoundaryDiagnostic][CapSkipped] " +
                        $"OuterLoop={outerLoopIndex}: 局所接続が同一または不確定な開放線がCap領域と重なります。");
                }
                continue;
            }

            // 異なる局所シートでも同じ元Mesh部品内にある開放線は、
            // 交差する三角形だけ除外してフタの一部を残す。
            List<int> independentOpenEdgeIndices = null;
            if (uncappedBoundaryLocalSheetIds != null &&
                capSourceComponentId >= 0 &&
                regionLocalSheetIds != null &&
                regionLocalSheetIds.Count > 0)
            {
                for (int edgeIndex = 0;
                     edgeIndex < uncappedBoundaryLocalSheetIds.Length;
                     edgeIndex++)
                {
                    int sheetId = uncappedBoundaryLocalSheetIds[edgeIndex];
                    if (uncappedBoundarySourceComponentIds[edgeIndex] != capSourceComponentId ||
                        sheetId < 0 || regionLocalSheetIds.Contains(sheetId))
                    {
                        continue;
                    }
                    if (independentOpenEdgeIndices == null)
                    {
                        independentOpenEdgeIndices = new List<int>();
                    }
                    independentOpenEdgeIndices.Add(edgeIndex);
                }
            }

            if (enableDiagnostics && independentOpenEdgeIndices != null)
            {
                Debug.Log(
                    "[MeshCutBoundaryDiagnostic][CapLocalRelation] " +
                    $"OuterLoop={outerLoopIndex}, " +
                    $"IndependentOpenEdges={independentOpenEdgeIndices.Count}");
            }

            // 右端が外側に近いHoleから橋渡しする方が、
            // Bridge同士が交差するケースを減らせる。
            directHoles.Sort(
                (first, second) =>
                    GetRightmostX(second.ProjectedPoints)
                        .CompareTo(
                            GetRightmostX(first.ProjectedPoints)));

            bool failedToMergeHole = false;
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
                    failedToMergeHole = true;
                    break;
                }
            }

            if (failedToMergeHole)
            {
                if (uncappedBoundaryEdgePoints.Count == 0)
                {
                    return false;
                }

                skippedCapRegionCount++;
                if (enableDiagnostics)
                {
                    Debug.LogWarning(
                        "[MeshCutBoundaryDiagnostic][CapSkipped] " +
                        $"OuterLoop={outerLoopIndex}: {failureReason}");
                }
                continue;
            }

            if (!TryTriangulateEarClipping(
                    mergedProjectedPoints,
                    earEpsilon,
                    out List<int> triangleIndices))
            {
                failureReason = "Ear Clippingによる切断面生成に失敗しました。";

                if (uncappedBoundaryEdgePoints.Count == 0)
                {
                    return false;
                }

                skippedCapRegionCount++;
                if (enableDiagnostics)
                {
                    Debug.LogWarning(
                        "[MeshCutBoundaryDiagnostic][CapSkipped] " +
                        $"OuterLoop={outerLoopIndex}: {failureReason}");
                }
                continue;
            }

            // 部分成功が許される経路でも、異常な三角形をBuilderへは追加しない。
            if (uncappedBoundaryEdgePoints.Count > 0 &&
                !AreCapTriangleIndicesValid(
                    mergedProjectedPoints,
                    triangleIndices,
                    earEpsilon))
            {
                failureReason = "切断面三角形に範囲外Index・縮退面・面積不一致があります。";
                if (uncappedBoundaryEdgePoints.Count == 0)
                {
                    return false;
                }

                skippedCapRegionCount++;
                if (enableDiagnostics)
                {
                    Debug.LogWarning(
                        "[MeshCutBoundaryDiagnostic][CapSkipped] " +
                        $"OuterLoop={outerLoopIndex}: {failureReason}");
                }
                continue;
            }

            int generatedTriangleCount = 0;
            int omittedTriangleCount = 0;
            using (emitCapTrianglesProfilerMarker.Auto())
            {
            for (int triangleIndex = 0;
                 triangleIndex < triangleIndices.Count;
                 triangleIndex += 3)
            {
                // 同じ元部品だが別の局所切断シートに属する開放線は、
                // 断面全体を無効化しない。ただし開放線を横断する三角形は追加しない。
                if (DoesCapTriangleTouchIndependentOpenBoundary(
                        mergedProjectedPoints[triangleIndices[triangleIndex]],
                        mergedProjectedPoints[triangleIndices[triangleIndex + 1]],
                        mergedProjectedPoints[triangleIndices[triangleIndex + 2]],
                        projectedUncappedBoundaryEdges,
                        independentOpenEdgeIndices,
                        geometryEpsilon))
                {
                    omittedTriangleCount++;
                    continue;
                }

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
                generatedTriangleCount++;
            }
            }

            if (generatedTriangleCount > 0)
            {
                generatedCapRegionCount++;
            }
            else
            {
                skippedCapRegionCount++;
                if (enableDiagnostics)
                {
                    Debug.LogWarning(
                        "[MeshCutBoundaryDiagnostic][CapSkipped] " +
                        $"OuterLoop={outerLoopIndex}: " +
                        "全ての断面三角形が独立した開放線と重なるため除外しました。");
                }
            }

            if (enableDiagnostics &&
                generatedTriangleCount > 0 && omittedTriangleCount > 0)
            {
                Debug.LogWarning(
                    "[MeshCutBoundaryDiagnostic][CapPartial] " +
                    $"OuterLoop={outerLoopIndex}, " +
                    $"GeneratedTriangles={generatedTriangleCount}, " +
                    $"OmittedTriangles={omittedTriangleCount}: " +
                    "別の局所切断シートの開放線と重なる三角形を除外しました。");
            }
        }

        failureReason = null;
        return true;

        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        }
        finally
        {
            FlushCapDiagnostics();
        }
#endif
    }

    /// <summary>
    /// Source Meshの三角形接続から部品IDを生成する。UVシームの頂点は既存の
    /// 位置ベースConnectivity IDで同一視するが、接触した別部品まで
    /// 同一視する可能性があるため、その場合は安全側でCap生成を見送る。
    /// </summary>
    private static int[] BuildSourceVertexComponents(
        Mesh sourceMesh,
        Bounds sourceBounds,
        int sourceSubMeshCount,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<List<int>> cachedSourceTrianglesBySubMesh,
        IReadOnlyList<int> cachedSourceConnectivityVertexIds,
        IReadOnlyList<CutSegment> cutSegments,
        out Dictionary<long, int> originalEdgeIncidenceCounts)
    {
        using var buildSourceVertexComponentsProfileScope = sourceComponentsProfilerMarker.Auto();
        int vertexCount = sourceVertices.Count;
        var parents = new int[vertexCount];
        var componentSizes = new int[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            parents[i] = i;
            componentSizes[i] = 1;
        }

        IReadOnlyList<int> weldedVertexIds =
            cachedSourceConnectivityVertexIds ??
            MeshConnectedComponentSplitter.BuildConnectedVertexIds(
                sourceVertices, sourceBounds);

        // 切断点を生成したSource Edgeに限って、全Triangleでの共有回数を数える。
        // CutSegmentの数だけでは、Planeに接している3枚目の面を検出できない。
        originalEdgeIncidenceCounts = new Dictionary<long, int>();
        for (int i = 0; i < cutSegments.Count; i++)
        {
            CutSegment segment = cutSegments[i];
            AddSourceEdgeIncidenceCandidate(
                originalEdgeIncidenceCounts, weldedVertexIds,
                segment.FirstSourceEdgeVertexIndexA,
                segment.FirstSourceEdgeVertexIndexB);
            AddSourceEdgeIncidenceCandidate(
                originalEdgeIncidenceCounts, weldedVertexIds,
                segment.SecondSourceEdgeVertexIndexA,
                segment.SecondSourceEdgeVertexIndexB);
        }

        var representativeByWeldedId = new Dictionary<int, int>(vertexCount);
        for (int i = 0; i < vertexCount; i++)
        {
            int weldedId = weldedVertexIds[i];
            if (representativeByWeldedId.TryGetValue(weldedId, out int representative))
            {
                UnionSourceComponentVertices(parents, componentSizes, i, representative);
            }
            else
            {
                representativeByWeldedId.Add(weldedId, i);
            }
        }

        // 既存の切断前Triangleデータを利用する。通常分割経路では
        // 追加のGetTrianglesは行わない。
        var triangleIndexScratch =
            cachedSourceTrianglesBySubMesh == null ? new List<int>() : null;

        for (int subMeshIndex = 0; subMeshIndex < sourceSubMeshCount; subMeshIndex++)
        {
            IReadOnlyList<int> indices;
            if (cachedSourceTrianglesBySubMesh != null)
            {
                indices = cachedSourceTrianglesBySubMesh[subMeshIndex];
            }
            else
            {
                sourceMesh.GetTriangles(triangleIndexScratch, subMeshIndex);
                indices = triangleIndexScratch;
            }

            for (int triangleIndex = 0; triangleIndex < indices.Count; triangleIndex += 3)
            {
                int first = indices[triangleIndex];
                int second = indices[triangleIndex + 1];
                int third = indices[triangleIndex + 2];
                UnionSourceComponentVertices(parents, componentSizes, first, second);
                UnionSourceComponentVertices(parents, componentSizes, second, third);
                CountOriginalEdgeIncidence(
                    originalEdgeIncidenceCounts, weldedVertexIds, first, second);
                CountOriginalEdgeIncidence(
                    originalEdgeIncidenceCounts, weldedVertexIds, second, third);
                CountOriginalEdgeIncidence(
                    originalEdgeIncidenceCounts, weldedVertexIds, third, first);
            }
        }

        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = FindSourceComponentRoot(parents, i);
        }

        return parents;
    }

    private static void AddSourceEdgeIncidenceCandidate(
        Dictionary<long, int> originalEdgeIncidenceCounts,
        IReadOnlyList<int> weldedVertexIds,
        int firstVertexIndex,
        int secondVertexIndex)
    {
        if (firstVertexIndex < 0 || secondVertexIndex < 0 ||
            firstVertexIndex >= weldedVertexIds.Count ||
            secondVertexIndex >= weldedVertexIds.Count ||
            weldedVertexIds[firstVertexIndex] == weldedVertexIds[secondVertexIndex])
        {
            return; // Plane上の単一点はOriginal Edgeの面数で判定できない。
        }
        long key = CreateEdgeKey(
            weldedVertexIds[firstVertexIndex], weldedVertexIds[secondVertexIndex]);
        if (!originalEdgeIncidenceCounts.ContainsKey(key))
        {
            originalEdgeIncidenceCounts.Add(key, 0);
        }
    }

    private static void CountOriginalEdgeIncidence(
        Dictionary<long, int> originalEdgeIncidenceCounts,
        IReadOnlyList<int> weldedVertexIds,
        int firstVertexIndex,
        int secondVertexIndex)
    {
        int firstWeldedId = weldedVertexIds[firstVertexIndex];
        int secondWeldedId = weldedVertexIds[secondVertexIndex];
        if (firstWeldedId == secondWeldedId)
        {
            return;
        }
        long key = CreateEdgeKey(firstWeldedId, secondWeldedId);
        if (originalEdgeIncidenceCounts.TryGetValue(key, out int count))
        {
            originalEdgeIncidenceCounts[key] = count + 1;
        }
    }

    private static int FindSourceComponentRoot(int[] parents, int vertexIndex)
    {
        while (parents[vertexIndex] != vertexIndex)
        {
            parents[vertexIndex] = parents[parents[vertexIndex]];
            vertexIndex = parents[vertexIndex];
        }
        return vertexIndex;
    }

    private static void UnionSourceComponentVertices(
        int[] parents,
        int[] componentSizes,
        int first,
        int second)
    {
        int firstRoot = FindSourceComponentRoot(parents, first);
        int secondRoot = FindSourceComponentRoot(parents, second);
        if (firstRoot == secondRoot)
        {
            return;
        }

        // 大きい連結成分を代表にして長いParent連鎖を防ぐ。
        if (componentSizes[firstRoot] < componentSizes[secondRoot])
        {
            int previousFirstRoot = firstRoot;
            firstRoot = secondRoot;
            secondRoot = previousFirstRoot;
        }

        parents[secondRoot] = firstRoot;
        componentSizes[firstRoot] += componentSizes[secondRoot];
    }

    private static int GetBoundarySourceComponent(
        IReadOnlyList<int> sourceVertexIndices,
        IReadOnlyList<int> sourceComponentByVertexIndex)
    {
        if (sourceVertexIndices == null || sourceVertexIndices.Count == 0)
        {
            return -1;
        }

        int componentId = -1;
        for (int i = 0; i < sourceVertexIndices.Count; i++)
        {
            int sourceVertexIndex = sourceVertexIndices[i];
            if (sourceVertexIndex < 0 || sourceVertexIndex >= sourceComponentByVertexIndex.Count)
            {
                return -1;
            }

            int nextComponentId = sourceComponentByVertexIndex[sourceVertexIndex];
            if (componentId >= 0 && componentId != nextComponentId)
            {
                return -1;
            }
            componentId = nextComponentId;
        }
        return componentId;
    }

    private static bool AreCapTriangleIndicesValid(
        IReadOnlyList<Vector2> positions,
        IReadOnlyList<int> triangleIndices,
        float areaEpsilon)
    {
        if (triangleIndices == null || triangleIndices.Count == 0 ||
            (triangleIndices.Count % 3) != 0)
        {
            return false;
        }

        double triangleAreaSum = 0.0;
        for (int i = 0; i < triangleIndices.Count; i += 3)
        {
            int a = triangleIndices[i];
            int b = triangleIndices[i + 1];
            int c = triangleIndices[i + 2];
            if (a < 0 || b < 0 || c < 0 ||
                a >= positions.Count || b >= positions.Count || c >= positions.Count ||
                a == b || b == c || a == c)
            {
                return false;
            }

            float doubleArea = Cross2D(
                positions[b] - positions[a], positions[c] - positions[a]);
            if (!IsFinite(doubleArea) || doubleArea <= areaEpsilon)
            {
                return false;
            }
            triangleAreaSum += doubleArea * 0.5;
        }

        // Hole-bridge後のPolygonと三角形の総面積が一致しなければ、
        // 重複生成や未三角形化の可能性があるためBuilderへ追加しない。
        double expectedArea = Math.Abs(CalculateSignedArea(positions));
        double allowedError = Math.Max(
            areaEpsilon * triangleIndices.Count,
            expectedArea * 0.001);
        return expectedArea > areaEpsilon &&
               Math.Abs(triangleAreaSum - expectedArea) <= allowedError;
    }

    /// <summary>
    /// CutSegment生成元のOriginal Edgeを使って、Planeと交差するTriangleだけの
    /// 局所シート連結性を求める。元Mesh全体の連結成分とは異なる判定軸。
    /// </summary>
    /// <remarks>
    /// UV seamは溶接IDで照合し、元Vertexの位置をより厳しく再照合する。
    /// 同じ幾何Edgeに3つ以上のSegmentが接続する非多様体ケースや、
    /// 一つの元Vertexが複数シートに属するケースは不明(-1)として扱う。
    /// </remarks>
    private static int[] BuildCutBoundaryLocalSheetIds(
        IReadOnlyList<CutSegment> cutSegments,
        IReadOnlyList<Vector3> sourceVertices,
        IReadOnlyList<int> weldedSourceVertexIds,
        IReadOnlyDictionary<long, int> originalEdgeIncidenceCounts,
        float sourceVertexMatchEpsilon,
        out int ambiguousCrossingEdgeCount)
    {
        using var buildCutBoundaryLocalSheetIdsProfileScope = boundarySheetsProfilerMarker.Auto();
        int[] parents = new int[cutSegments.Count];
        int[] componentSizes = new int[cutSegments.Count];
        bool[] ambiguousSegments = new bool[cutSegments.Count];
        for (int i = 0; i < cutSegments.Count; i++)
        {
            parents[i] = i;
            componentSizes[i] = 1;
        }

        // 1つのCutSegmentの2端点をindex*2で符号化する。Original Edgeを
        // 共有する2つの交差Triangleだけを結ぶ。3つ以上なら結合しない。
        var endpointIndicesBySourceEdge = new Dictionary<long, List<int>>();
        for (int segmentIndex = 0; segmentIndex < cutSegments.Count; segmentIndex++)
        {
            CutSegment segment = cutSegments[segmentIndex];
            AddCutSegmentSourceEdge(
                endpointIndicesBySourceEdge, weldedSourceVertexIds,
                segment.FirstSourceEdgeVertexIndexA,
                segment.FirstSourceEdgeVertexIndexB,
                segmentIndex * 2);
            AddCutSegmentSourceEdge(
                endpointIndicesBySourceEdge, weldedSourceVertexIds,
                segment.SecondSourceEdgeVertexIndexA,
                segment.SecondSourceEdgeVertexIndexB,
                segmentIndex * 2 + 1);
        }

        ambiguousCrossingEdgeCount = 0;
        foreach (List<int> endpoints in endpointIndicesBySourceEdge.Values)
        {
            if (endpoints.Count != 2)
            {
                if (endpoints.Count > 2)
                {
                    ambiguousCrossingEdgeCount++;
                    foreach (int endpoint in endpoints)
                    {
                        ambiguousSegments[endpoint / 2] = true;
                    }
                }
                continue;
            }

            int firstSegmentIndex = endpoints[0] / 2;
            int secondSegmentIndex = endpoints[1] / 2;
            CutSegment first = cutSegments[firstSegmentIndex];
            CutSegment second = cutSegments[secondSegmentIndex];
            int firstA = (endpoints[0] & 1) == 0
                ? first.FirstSourceEdgeVertexIndexA
                : first.SecondSourceEdgeVertexIndexA;
            int firstB = (endpoints[0] & 1) == 0
                ? first.FirstSourceEdgeVertexIndexB
                : first.SecondSourceEdgeVertexIndexB;
            int secondA = (endpoints[1] & 1) == 0
                ? second.FirstSourceEdgeVertexIndexA
                : second.SecondSourceEdgeVertexIndexA;
            int secondB = (endpoints[1] & 1) == 0
                ? second.FirstSourceEdgeVertexIndexB
                : second.SecondSourceEdgeVertexIndexB;

            long sourceEdgeKey = CreateEdgeKey(
                weldedSourceVertexIds[firstA], weldedSourceVertexIds[firstB]);
            bool isOriginalEdge = weldedSourceVertexIds[firstA] !=
                                  weldedSourceVertexIds[firstB];
            bool isNonManifoldEdge = isOriginalEdge &&
                (!originalEdgeIncidenceCounts.TryGetValue(
                    sourceEdgeKey, out int incidentTriangleCount) ||
                 incidentTriangleCount != 2);

            if (firstSegmentIndex == secondSegmentIndex || isNonManifoldEdge ||
                !AreSourceOriginsEquivalent(
                    firstA, firstB, secondA, secondB,
                    sourceVertices, sourceVertexMatchEpsilon))
            {
                ambiguousCrossingEdgeCount++;
                ambiguousSegments[firstSegmentIndex] = true;
                ambiguousSegments[secondSegmentIndex] = true;
                continue;
            }

            UnionSourceComponentVertices(
                parents, componentSizes, firstSegmentIndex, secondSegmentIndex);
        }

        bool[] ambiguousRoots = new bool[cutSegments.Count];
        for (int i = 0; i < cutSegments.Count; i++)
        {
            if (ambiguousSegments[i])
            {
                ambiguousRoots[FindSourceComponentRoot(parents, i)] = true;
            }
        }

        int[] sheetBySourceVertexIndex = new int[sourceVertices.Count];
        for (int i = 0; i < sheetBySourceVertexIndex.Length; i++)
        {
            sheetBySourceVertexIndex[i] = -2; // 切断元Edgeに未登場
        }

        for (int i = 0; i < cutSegments.Count; i++)
        {
            int root = FindSourceComponentRoot(parents, i);
            int sheetId = ambiguousRoots[root] ? -1 : root;
            CutSegment segment = cutSegments[i];
            AssignCutSourceVertexSheet(
                sheetBySourceVertexIndex, segment.FirstSourceEdgeVertexIndexA, sheetId);
            AssignCutSourceVertexSheet(
                sheetBySourceVertexIndex, segment.FirstSourceEdgeVertexIndexB, sheetId);
            AssignCutSourceVertexSheet(
                sheetBySourceVertexIndex, segment.SecondSourceEdgeVertexIndexA, sheetId);
            AssignCutSourceVertexSheet(
                sheetBySourceVertexIndex, segment.SecondSourceEdgeVertexIndexB, sheetId);
        }

        return sheetBySourceVertexIndex;
    }

    private static void AddCutSegmentSourceEdge(
        Dictionary<long, List<int>> endpointsBySourceEdge,
        IReadOnlyList<int> weldedSourceVertexIds,
        int vertexA,
        int vertexB,
        int encodedEndpointIndex)
    {
        if (vertexA < 0 || vertexB < 0 ||
            vertexA >= weldedSourceVertexIds.Count ||
            vertexB >= weldedSourceVertexIds.Count)
        {
            return;
        }

        long edgeKey = CreateEdgeKey(
            weldedSourceVertexIds[vertexA], weldedSourceVertexIds[vertexB]);
        if (!endpointsBySourceEdge.TryGetValue(edgeKey, out List<int> endpoints))
        {
            endpoints = new List<int>(2);
            endpointsBySourceEdge.Add(edgeKey, endpoints);
        }
        endpoints.Add(encodedEndpointIndex);
    }

    private static void AssignCutSourceVertexSheet(
        int[] sheetsByVertexIndex,
        int sourceVertexIndex,
        int sheetId)
    {
        if (sourceVertexIndex < 0 || sourceVertexIndex >= sheetsByVertexIndex.Length)
        {
            return;
        }
        int previous = sheetsByVertexIndex[sourceVertexIndex];
        if (previous == -2)
        {
            sheetsByVertexIndex[sourceVertexIndex] = sheetId;
        }
        else if (previous != sheetId)
        {
            sheetsByVertexIndex[sourceVertexIndex] = -1;
        }
    }

    // 別シートと判明しても、開いた切断線を直接横切るCap三角形は作らない。
    // 射影された開放線は面積を持たないため、既存Triangle単位で安全側に除外する。
    private static bool DoesCapTriangleTouchIndependentOpenBoundary(
        Vector2 a,
        Vector2 b,
        Vector2 c,
        IReadOnlyList<Vector2> projectedUncappedBoundaryEdges,
        IReadOnlyList<int> independentOpenEdgeIndices,
        float geometryEpsilon)
    {
        if (independentOpenEdgeIndices == null)
        {
            return false;
        }

        float areaEpsilon = geometryEpsilon * geometryEpsilon;
        float minX = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - geometryEpsilon;
        float maxX = Mathf.Max(a.x, Mathf.Max(b.x, c.x)) + geometryEpsilon;
        float minY = Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - geometryEpsilon;
        float maxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) + geometryEpsilon;

        for (int i = 0; i < independentOpenEdgeIndices.Count; i++)
        {
            int projectedPointIndex = independentOpenEdgeIndices[i] * 2;
            Vector2 start = projectedUncappedBoundaryEdges[projectedPointIndex];
            Vector2 end = projectedUncappedBoundaryEdges[projectedPointIndex + 1];
            if (Mathf.Max(start.x, end.x) < minX ||
                Mathf.Min(start.x, end.x) > maxX ||
                Mathf.Max(start.y, end.y) < minY ||
                Mathf.Min(start.y, end.y) > maxY)
            {
                continue;
            }

            if (IsPointInsideTriangle(start, a, b, c, areaEpsilon) ||
                IsPointInsideTriangle(end, a, b, c, areaEpsilon) ||
                DoSegmentsIntersect(start, end, a, b, geometryEpsilon) ||
                DoSegmentsIntersect(start, end, b, c, geometryEpsilon) ||
                DoSegmentsIntersect(start, end, c, a, geometryEpsilon))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 局所切断Sheetが同一/不明な開放線だけをCap候補の障害として判定する。
    /// 別Sheetの線は断面全体を止めず、後段で重なる三角形のみ除外する。
    /// </summary>
    private static bool DoesUncappedBoundaryOverlapCapRegion(
        IReadOnlyList<Vector2> outerPolygon,
        IReadOnlyList<PreparedBoundaryLoop> holes,
        IReadOnlyList<Vector2> projectedUncappedBoundaryEdges,
        int capSourceComponentId,
        IReadOnlyList<int> uncappedBoundarySourceComponentIds,
        HashSet<int> regionLocalSheetIds,
        IReadOnlyList<int> uncappedBoundaryLocalSheetIds,
        float geometryEpsilon)
    {
        if (projectedUncappedBoundaryEdges.Count == 0)
        {
        using var boundaryOverlapSample = overlapCapBoundaryProfilerMarker.Auto();
            return false;
        }

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;

        foreach (Vector2 point in outerPolygon)
        {
            minX = Mathf.Min(minX, point.x);
            maxX = Mathf.Max(maxX, point.x);
            minY = Mathf.Min(minY, point.y);
            maxY = Mathf.Max(maxY, point.y);
        }

        for (int i = 0; i < projectedUncappedBoundaryEdges.Count; i += 2)
        {
            // 別部品の開いた切断線は、この閉ループで作る断面の穴ではない。
            // 分類不能(-1)は誤って元の穴を塞がないよう遮蔽物として扱う。
            if (uncappedBoundarySourceComponentIds != null &&
                capSourceComponentId >= 0 &&
                uncappedBoundarySourceComponentIds[i / 2] >= 0 &&
                capSourceComponentId != uncappedBoundarySourceComponentIds[i / 2])
            {
                continue;
            }

            // Cut元Triangleが、閉ループのある局所Sheetと独立している場合、
            // 領域一括スキップはしない。交差するCap三角形だけを後で除外する。
            // Sheet不明(-1)や非多様体接続は安全側で従来判定へ戻す。
            if (uncappedBoundaryLocalSheetIds != null &&
                uncappedBoundaryLocalSheetIds[i / 2] >= 0 &&
                regionLocalSheetIds != null &&
                regionLocalSheetIds.Count > 0 &&
                !regionLocalSheetIds.Contains(uncappedBoundaryLocalSheetIds[i / 2]))
            {
                continue;
            }

            Vector2 start = projectedUncappedBoundaryEdges[i];
            Vector2 end = projectedUncappedBoundaryEdges[i + 1];

            if (Mathf.Max(start.x, end.x) < minX - geometryEpsilon ||
                Mathf.Min(start.x, end.x) > maxX + geometryEpsilon ||
                Mathf.Max(start.y, end.y) < minY - geometryEpsilon ||
                Mathf.Min(start.y, end.y) > maxY + geometryEpsilon)
            {
                continue;
            }

            if (DoesBoundarySegmentIntersectPolygon(start, end, outerPolygon, geometryEpsilon))
            {
                return true;
            }

            bool startInOuter = IsPointInsidePolygon(start, outerPolygon);
            bool endInOuter = IsPointInsidePolygon(end, outerPolygon);
            if (!startInOuter && !endInOuter)
            {
                continue;
            }

            bool startInHole = false;
            bool endInHole = false;
            foreach (PreparedBoundaryLoop hole in holes)
            {
                if (DoesBoundarySegmentIntersectPolygon(
                        start, end, hole.ProjectedPoints, geometryEpsilon))
                {
                    return true;
                }

                startInHole |= IsPointInsidePolygon(start, hole.ProjectedPoints);
                endInHole |= IsPointInsidePolygon(end, hole.ProjectedPoints);
            }

            if ((startInOuter && !startInHole) ||
                (endInOuter && !endInHole))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DoesBoundarySegmentIntersectPolygon(
        Vector2 start,
        Vector2 end,
        IReadOnlyList<Vector2> polygon,
        float geometryEpsilon)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            if (DoSegmentsIntersect(
                    start,
                    end,
                    polygon[i],
                    polygon[(i + 1) % polygon.Count],
                    geometryEpsilon))
            {
                return true;
            }
        }

        return false;
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
        bool validateSelfIntersection,
        out PreparedBoundaryLoop preparedLoop)
    {
        using var tryPrepareBoundaryLoopProfileScope = prepareCapLoopProfilerMarker.Auto();
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

        if (positions.Count < 3 ||
            (validateSelfIntersection &&
             !IsSimpleClosedBoundaryPolygon(projectedPoints, geometryEpsilon)))
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

        float absoluteArea = Mathf.Abs(signedArea);

        if (signedArea < 0.0f)
        {
            positions.Reverse();
            projectedPoints.Reverse();
            connectivityVertexIds.Reverse();

            // 階層判定は反転後の頂点順で面積を求めていたため、
            // 浮動小数点の加算順まで変えないよう反転時のみ再計算する。
            absoluteArea = Mathf.Abs(CalculateSignedArea(projectedPoints));
        }

        preparedLoop =
            new PreparedBoundaryLoop(
                positions,
                projectedPoints,
                connectivityVertexIds,
                absoluteArea);

        return true;
    }

    // Degree=2だけでは、投影で自己交差する閉路を単純Polygonと断定できない。
    // そのままEar Clippingすると存在しない断面を生成し得るため事前に拒否する。
    private static bool IsSimpleClosedBoundaryPolygon(
        IReadOnlyList<Vector2> polygon,
        float geometryEpsilon)
    {
        using var isSimpleClosedBoundaryPolygonProfileScope = validateCapPolygonProfilerMarker.Auto();
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 start = polygon[i];
            Vector2 end = polygon[(i + 1) % polygon.Count];
            if (!IsFinite(start.x) || !IsFinite(start.y) ||
                !IsFinite(end.x) || !IsFinite(end.y) ||
                (end - start).sqrMagnitude <= geometryEpsilon * geometryEpsilon)
            {
                return false;
            }

            for (int j = i + 1; j < polygon.Count; j++)
            {
                // 隣接Edgeの共有端点は自己交差ではない。
                if (j == i + 1 || (i == 0 && j == polygon.Count - 1))
                {
                    continue;
                }

                if (DoSegmentsIntersect(
                        start, end,
                        polygon[j], polygon[(j + 1) % polygon.Count],
                        geometryEpsilon))
                {
                    return false;
                }
            }
        }

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
        using var removeRedundantLoopPointsSample = simplifyCapLoopProfilerMarker.Auto();
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
        IReadOnlyList<int> sourceComponentByLoop,
        out string failureReason)
    {
        using var tryAssignLoopHierarchyProfileScope = assignCapHierarchyProfilerMarker.Auto();
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

            float innerArea = innerLoop.AbsoluteArea;

            float bestParentArea =
                float.PositiveInfinity;

            Vector2 testPoint =
                innerLoop.ProjectedPoints[0];

            for (int outerIndex = 0;
                 outerIndex < loops.Count;
                 outerIndex++)
            {
                if (outerIndex == innerIndex ||
                    (sourceComponentByLoop != null &&
                     sourceComponentByLoop[innerIndex] >= 0 &&
                     sourceComponentByLoop[outerIndex] >= 0 &&
                     sourceComponentByLoop[innerIndex] != sourceComponentByLoop[outerIndex]))
                {
                    continue;
                }

                PreparedBoundaryLoop outerLoop =
                    loops[outerIndex];

                float outerArea = outerLoop.AbsoluteArea;

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
        using var tryMergeHoleIntoPolygonProfileScope = mergeCapHoleProfilerMarker.Auto();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int inputPolygonVertexCount = polygonProjectedPoints.Count;
        int visibilityCheckCount = 0;
        int visitedEdgeCount = 0;
        int segmentIntersectionTestCount = 0;
#endif
        int holeBridgeIndex =
            FindRightmostVertexIndex(
                hole.ProjectedPoints);

        Vector2 holeBridgePoint =
            hole.ProjectedPoints[
                holeBridgeIndex];

        int polygonBridgeIndex = -1;
        float bestSqrDistance =
            float.PositiveInfinity;

        using (searchCapBridgeProfilerMarker.Auto())
        {
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                visibilityCheckCount++;
#endif
                if (!IsBridgeVisible(
                        holeBridgePoint,
                        candidatePoint,
                        candidateIndex,
                        polygonProjectedPoints,
                        holeBridgeIndex,
                        hole.ProjectedPoints,
                        siblingHoles,
                        hole,
                        geometryEpsilon
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        , ref visitedEdgeCount,
                        ref segmentIntersectionTestCount
#endif
                        ))
                {
                    continue;
                }

                polygonBridgeIndex =
                    candidateIndex;

                bestSqrDistance =
                    sqrDistance;
            }
        }

        if (polygonBridgeIndex < 0)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordMergeHoleDiagnostics(inputPolygonVertexCount, hole.ProjectedPoints.Count,
                siblingHoles.Count, visibilityCheckCount, visitedEdgeCount,
                segmentIntersectionTestCount);
#endif
            failureReason =
                "切断面Holeを外周へ接続できるBridgeが見つかりませんでした。";

            return false;
        }

        using (rebuildCapPolygonProfilerMarker.Auto())
        {
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
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        RecordMergeHoleDiagnostics(inputPolygonVertexCount, hole.ProjectedPoints.Count,
            siblingHoles.Count, visibilityCheckCount, visitedEdgeCount,
            segmentIntersectionTestCount);
#endif
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
        float geometryEpsilon
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        , ref int visitedEdgeCount,
        ref int segmentIntersectionTestCount
#endif
        )
    {
        if (SegmentIntersectsPolygon(
                holePoint,
                polygonPoint,
                polygon,
                polygonPointIndex,
                geometryEpsilon
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                , ref visitedEdgeCount,
                ref segmentIntersectionTestCount
#endif
                ))
        {
            return false;
        }

        if (SegmentIntersectsPolygon(
                holePoint,
                polygonPoint,
                hole,
                holePointIndex,
                geometryEpsilon
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                , ref visitedEdgeCount,
                ref segmentIntersectionTestCount
#endif
                ))
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
                    geometryEpsilon
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , ref visitedEdgeCount,
                    ref segmentIntersectionTestCount
#endif
                    ))
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
        float geometryEpsilon
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        , ref int visitedEdgeCount,
        ref int segmentIntersectionTestCount
#endif
        )
    {
        float sqrEpsilon =
            geometryEpsilon * geometryEpsilon;

        // IsPointOnSegmentは座標ごとにgeometryEpsilonを許容するため、
        // BridgeのAABBも同じ幅だけ広げる。交差し得ない辺だけを除外する。
        float segmentMinX = Mathf.Min(segmentStart.x, segmentEnd.x) - geometryEpsilon;
        float segmentMaxX = Mathf.Max(segmentStart.x, segmentEnd.x) + geometryEpsilon;
        float segmentMinY = Mathf.Min(segmentStart.y, segmentEnd.y) - geometryEpsilon;
        float segmentMaxY = Mathf.Max(segmentStart.y, segmentEnd.y) + geometryEpsilon;

        for (int edgeStartIndex = 0;
             edgeStartIndex < polygon.Count;
             edgeStartIndex++)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            visitedEdgeCount++;
#endif
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

            // AABBが離れていれば厳密交差も許容誤差内の端点接触も起こらない。
            // 重複Bridge端点の判定と詳細な外積計算を行う前に除外する。
            if ((edgeStart.x < segmentMinX && edgeEnd.x < segmentMinX) ||
                (edgeStart.x > segmentMaxX && edgeEnd.x > segmentMaxX) ||
                (edgeStart.y < segmentMinY && edgeEnd.y < segmentMinY) ||
                (edgeStart.y > segmentMaxY && edgeEnd.y > segmentMaxY))
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            segmentIntersectionTestCount++;
#endif
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
        using var tryTriangulateEarClippingProfileScope = triangulateCapProfilerMarker.Auto();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int testedEarCandidateCount = 0;
        int pointInsideTriangleTestCount = 0;
        int removedVertexCount = 0;
#endif
        triangleIndices =
            new List<int>();

        if (polygon.Count < 3)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RecordEarClippingDiagnostics(polygon.Count, 0, 0, 0);
#endif
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

        using (findCapEarProfilerMarker.Auto())
        {
            while (remainingIndices.Count > 3)
            {
                bool foundEar = false;

                for (int i = 0;
                     i < remainingIndices.Count;
                     i++)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    testedEarCandidateCount++;
#endif
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

                    // IsPointInsideTriangleは各辺の外積に-areaEpsilonまでの
                    // はみ出しを許容する。通常の三角形AABBではその点を誤って
                    // 除外するため、面積比から求めた安全側の範囲を使用する。
                    // 正の面積をAとすると、各頂点の重みは-areaEpsilon/A以上。
                    // その制約下で到達し得る座標の上下限を三角形ごとに計算する。
                    float minX = Mathf.Min(previous.x, Mathf.Min(current.x, next.x));
                    float maxX = Mathf.Max(previous.x, Mathf.Max(current.x, next.x));
                    float minY = Mathf.Min(previous.y, Mathf.Min(current.y, next.y));
                    float maxY = Mathf.Max(previous.y, Mathf.Max(current.y, next.y));

                    // ほぼ一直線の3点ではfloatの外積の丸め誤差が支配的になり、
                    // 面積比から求めた範囲でも元の内外判定と食い違うため、
                    // その場合は従来どおり全頂点を詳細判定する。
                    double maximumSpan = Math.Max((double)maxX - minX, (double)maxY - minY);
                    bool useEarBounds = cornerCross > maximumSpan * maximumSpan * 0.0001;
                    double earMinX = 0.0;
                    double earMaxX = 0.0;
                    double earMinY = 0.0;
                    double earMaxY = 0.0;
                    if (useEarBounds)
                    {
                        double toleranceRatio = (double)areaEpsilon / cornerCross;
                        double sumX = (double)previous.x + current.x + next.x;
                        double sumY = (double)previous.y + current.y + next.y;

                        // float外積の丸めで境界付近の候補を落とさないため、
                        // AABBはさらに座標スケールに応じて保守的に広げる。
                        double coordinateScale = Math.Max(1.0,
                            Math.Max(Math.Max(Math.Abs(minX), Math.Abs(maxX)),
                                     Math.Max(Math.Abs(minY), Math.Abs(maxY))));
                        double roundingMargin = coordinateScale * 0.00001;

                        earMinX = minX - toleranceRatio * (sumX - 3.0 * minX) - roundingMargin;
                        earMaxX = maxX + toleranceRatio * (3.0 * maxX - sumX) + roundingMargin;
                        earMinY = minY - toleranceRatio * (sumY - 3.0 * minY) - roundingMargin;
                        earMaxY = maxY + toleranceRatio * (3.0 * maxY - sumY) + roundingMargin;
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

                        if (useEarBounds &&
                            (testPoint.x < earMinX || testPoint.x > earMaxX ||
                             testPoint.y < earMinY || testPoint.y > earMaxY))
                        {
                            continue;
                        }

                        // Hole Bridgeでは同一座標が意図的に2回現れる。
                        // それを「Ear内部の別頂点」と判定すると、Bridge周辺の
                        // Earが永久に除外されて三角形分割できなくなる。
                        if ((testPoint - previous).sqrMagnitude <= areaEpsilon ||
                            (testPoint - current).sqrMagnitude <= areaEpsilon ||
                            (testPoint - next).sqrMagnitude <= areaEpsilon)
                        {
                            continue;
                        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        pointInsideTriangleTestCount++;
#endif
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

                    using (removeCapEarVertexProfilerMarker.Auto())
                    {
                        remainingIndices.RemoveAt(i);
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    removedVertexCount++;
#endif

                    foundEar = true;
                    break;
                }

                if (!foundEar)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    RecordEarClippingDiagnostics(polygon.Count, testedEarCandidateCount,
                        pointInsideTriangleTestCount, removedVertexCount);
#endif
                    triangleIndices = null;
                    return false;
                }

                guard++;

                if (guard > maximumIterations)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    RecordEarClippingDiagnostics(polygon.Count, testedEarCandidateCount,
                        pointInsideTriangleTestCount, removedVertexCount);
#endif
                    triangleIndices = null;
                    return false;
                }
            }
        }

        triangleIndices.Add(
            remainingIndices[0]);

        triangleIndices.Add(
            remainingIndices[1]);

        triangleIndices.Add(
            remainingIndices[2]);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        RecordEarClippingDiagnostics(polygon.Count, testedEarCandidateCount,
            pointInsideTriangleTestCount, removedVertexCount);
#endif
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // 失敗した分割試行も含め、Cap生成試行ごとの実行回数を集計する。
    private static void RecordMergeHoleDiagnostics(
        int polygonVertexCount,
        int holeVertexCount,
        int siblingHoleCount,
        int visibilityChecks,
        int visitedEdges,
        int segmentIntersectionTests)
    {
        mergeHoleVisibilityChecksCounter += visibilityChecks;
        mergeHoleEdgeVisitsCounter += visitedEdges;
        mergeHoleIntersectionTestsCounter += segmentIntersectionTests;
        mergeHoleInputVerticesCounter += polygonVertexCount + holeVertexCount;
        mergeHoleSiblingHolesCounter += siblingHoleCount;
    }

    private static void RecordEarClippingDiagnostics(
        int polygonVertexCount,
        int candidatesChecked,
        int pointInsideTriangleTests,
        int removedVertices)
    {
        earClippingCandidatesCounter += candidatesChecked;
        earClippingTriangleTestsCounter += pointInsideTriangleTests;
        earClippingInputVerticesCounter += polygonVertexCount;
        earClippingRemovedVerticesCounter += removedVertices;
    }

    private static void FlushCapDiagnostics()
    {
        // ログ生成はBuildCutSurfaceCapsマーカー外で行い、CPU測定への影響を抑える。
        if (mergeHoleVisibilityChecksCounter != 0 ||
            earClippingCandidatesCounter != 0 ||
            mergeHoleInputVerticesCounter != 0 ||
            earClippingInputVerticesCounter != 0)
        {
            Debug.Log(
                "[EnemyMeshCutCapDiagnostic] " +
                $"Frame={Time.frameCount}, " +
                $"MergeHole.VisibilityChecks={mergeHoleVisibilityChecksCounter}, " +
                $"MergeHole.EdgeVisits={mergeHoleEdgeVisitsCounter}, " +
                $"MergeHole.SegmentTests={mergeHoleIntersectionTestsCounter}, " +
                $"MergeHole.InputVerticesTotal={mergeHoleInputVerticesCounter}, " +
                $"MergeHole.SiblingHolesTotal={mergeHoleSiblingHolesCounter}, " +
                $"EarClipping.Candidates={earClippingCandidatesCounter}, " +
                $"EarClipping.PointTests={earClippingTriangleTestsCounter}, " +
                $"EarClipping.InputVerticesTotal={earClippingInputVerticesCounter}, " +
                $"EarClipping.RemovedVertices={earClippingRemovedVerticesCounter}");
        }

        mergeHoleVisibilityChecksCounter = 0;
        mergeHoleEdgeVisitsCounter = 0;
        mergeHoleIntersectionTestsCounter = 0;
        mergeHoleInputVerticesCounter = 0;
        mergeHoleSiblingHolesCounter = 0;
        earClippingCandidatesCounter = 0;
        earClippingTriangleTestsCounter = 0;
        earClippingInputVerticesCounter = 0;
        earClippingRemovedVerticesCounter = 0;
    }
#endif

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

    private readonly struct TriangleIntersectionData
    {
        public bool HasEdgeABIntersection { get; }
        public VertexData EdgeABIntersection { get; }
        public bool HasEdgeBCIntersection { get; }
        public VertexData EdgeBCIntersection { get; }
        public bool HasEdgeCAIntersection { get; }
        public VertexData EdgeCAIntersection { get; }

        public TriangleIntersectionData(
            bool hasEdgeABIntersection,
            VertexData edgeABIntersection,
            bool hasEdgeBCIntersection,
            VertexData edgeBCIntersection,
            bool hasEdgeCAIntersection,
            VertexData edgeCAIntersection)
        {
            HasEdgeABIntersection = hasEdgeABIntersection;
            EdgeABIntersection = edgeABIntersection;
            HasEdgeBCIntersection = hasEdgeBCIntersection;
            EdgeBCIntersection = edgeBCIntersection;
            HasEdgeCAIntersection = hasEdgeCAIntersection;
            EdgeCAIntersection = edgeCAIntersection;
        }
    }

    internal readonly struct VertexData
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
        public int NextSameConnectivityNodeIndex { get; set; }
        public int NextSameSpatialCellNodeIndex { get; set; }
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
            NextSameConnectivityNodeIndex = -1;
            NextSameSpatialCellNodeIndex = -1;

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
        public float AbsoluteArea { get; }
        public int ParentIndex { get; set; }
        public int NestingDepth { get; set; }

        public PreparedBoundaryLoop(
            List<Vector3> positions,
            List<Vector2> projectedPoints,
            List<int> connectivityVertexIds,
            float absoluteArea)
        {
            Positions =
                positions;

            ProjectedPoints =
                projectedPoints;

            ConnectivityVertexIds =
                connectivityVertexIds;

            // 冗長点除去と向きの正規化が完了したループの面積を保持する。
            AbsoluteArea = absoluteArea;

            ParentIndex = -1;
            NestingDepth = 0;
        }
    }

    private static int CalculateInitialPieceVertexCapacity(
        int sourceVertexCount)
    {
        // Source Vertexは基本的に切断Planeのどちらか一方へ分かれるため、
        // 両BuilderへSource Mesh全頂点数を予約せず、半数を初期推定値とする。
        // 偏った切断や交点Vertex追加で超過した場合はListの通常拡張へ任せる。
        return sourceVertexCount / 2 +
               sourceVertexCount % 2;
    }

    private static int CalculateEstimatedSideVertexCapacity(
        int sideSourceVertexCount,
        int onPlaneSourceVertexCount,
        int cutVertexAllowance)
    {
        // 大きすぎる推定値でintを超えないよう上限を設ける。
        return (int)Math.Min(
            int.MaxValue,
            (long)sideSourceVertexCount + onPlaneSourceVertexCount + cutVertexAllowance);
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

    // 既存Builderを食い込み分割中のPiece保持にも使用する。新しいデータ型は作らない。
    internal sealed class PieceMeshBuilder
    {
        private readonly List<Vector3> vertices;

        private readonly List<Vector3> normals;

        private readonly List<Vector2> uv;

        private readonly List<Vector4> tangents;

        private readonly List<int> connectivityVertexIds;

        private readonly List<List<int>>
            trianglesBySubMesh;

        // 食い込み側だけ使用する。SubMeshごとにTriangle index順と1:1で対応させる。
        // Crossのmagnitudeを保持し、表面積集計と次の切断位置の重み付けで使い回す。
        private readonly List<List<float>> triangleDoubleAreasBySubMesh =
            new List<List<float>>();

        private bool cacheTriangleAreas;

        // SubMesh数が減った場合もList本体とBacking Arrayを捨てず、
        // 次にSubMesh数が増えたときへ再利用する。Pool Builderが保持する。
        private readonly Stack<List<int>>
            spareTrianglesBySubMesh;

        // Source頂点だけを再利用する。位置一致では統合せず、
        // UV seam / hard edgeとして分離された別Source Vertex Indexは維持する。
        // 0を未登録として使えるよう、実際のOutput Vertex Index + 1を格納する。
        private int[]
            outputVertexIndexBySourceVertexIndex;

        private bool hasTangents;
        private bool storeConnectivityVertexIds;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Builderごとの最終Count/CapacityはPoolへ返す直前に一度だけ集計する。
        internal MeshPlaneSplitter DiagnosticOwner { get; set; }
        internal bool TrackCreateBuilderDiagnostics { get; set; }
        internal int SourceIndexMapLength => outputVertexIndexBySourceVertexIndex.Length;
        internal int DiagnosticBuilderId { get; private set; }
        internal int DiagnosticRecordIndex { get; set; } = -1;
        internal int DiagnosticPositionGrowthCount { get; set; }
        internal int DiagnosticNormalGrowthCount { get; set; }
        internal int DiagnosticUvGrowthCount { get; set; }
        internal int DiagnosticTangentGrowthCount { get; set; }
        internal int DiagnosticTriangleIndexGrowthCount { get; set; }
        internal int DiagnosticTriangleAreaGrowthCount { get; set; }
#endif

        private bool hasBounds;
        private Vector3 minimumPosition;
        private Vector3 maximumPosition;

        public int OriginalTriangleCount
        {
            get;
            private set;
        }

        public int TotalTriangleCount
        {
            get;
            private set;
        }

        public List<Vector3> Vertices => vertices;
        public List<Vector3> Normals => normals;
        public List<Vector2> Uv => uv;
        public List<Vector4> Tangents => tangents;
        public List<int> ConnectivityVertexIds => connectivityVertexIds;
        public List<List<int>> TrianglesBySubMesh => trianglesBySubMesh;
        public IReadOnlyList<List<float>> TriangleDoubleAreasBySubMesh =>
            triangleDoubleAreasBySubMesh;
        public bool HasTangents => hasTangents;

        // 全ての使用属性Listが再確保なしで保持できる頂点数を返す。
        public int GetReusableVertexCapacity(
            bool useTangents,
            bool useConnectivityVertexIds)
        {
            int reusableCapacity = Mathf.Min(
                vertices.Capacity, normals.Capacity);
            reusableCapacity = Mathf.Min(reusableCapacity, uv.Capacity);
            if (useTangents)
            {
                reusableCapacity = Mathf.Min(reusableCapacity, tangents.Capacity);
            }
            if (useConnectivityVertexIds)
            {
                reusableCapacity = Mathf.Min(
                    reusableCapacity, connectivityVertexIds.Capacity);
            }
            return reusableCapacity;
        }

        // 8分割で現在の入力SubMeshから想定される両側の初期Index予約量を比較する。
        // SubMesh別のCapacityを評価し、合計Capacityが足りても一部が不足する場合を検知する。
        public long GetTriangleIndexCapacityShortfall(
            IReadOnlyList<List<int>> sourceTrianglesBySubMesh,
            out long availableCapacity)
        {
            long shortfall = 0;
            availableCapacity = 0;
            int subMeshIndex = 0;
            // PrepareForUseで一時的に外されたSubMesh用ListもStackに残っている。
            // 新しい分割に再割り当てされる順で確認し、実際に使用可能な容量を評価する。
            for (; subMeshIndex < trianglesBySubMesh.Count &&
                   subMeshIndex < sourceTrianglesBySubMesh.Count; subMeshIndex++)
            {
                int capacity = trianglesBySubMesh[subMeshIndex].Capacity;
                int required = CalculateInitialPieceTriangleIndexCapacity(
                    sourceTrianglesBySubMesh[subMeshIndex].Count);
                availableCapacity += capacity;
                if (capacity < required)
                {
                    shortfall += (long)required - capacity;
                }
            }
            if (spareTrianglesBySubMesh != null)
            {
                foreach (List<int> spareIndices in spareTrianglesBySubMesh)
                {
                    if (subMeshIndex >= sourceTrianglesBySubMesh.Count)
                    {
                        break;
                    }
                    int capacity = spareIndices.Capacity;
                    int required = CalculateInitialPieceTriangleIndexCapacity(
                        sourceTrianglesBySubMesh[subMeshIndex].Count);
                    availableCapacity += capacity;
                    if (capacity < required)
                    {
                        shortfall += (long)required - capacity;
                    }
                    subMeshIndex++;
                }
            }
            // 未生成のSubMesh Listは取得時に新規作成されるため容量ゼロとして扱う。
            for (; subMeshIndex < sourceTrianglesBySubMesh.Count; subMeshIndex++)
            {
                shortfall += CalculateInitialPieceTriangleIndexCapacity(
                    sourceTrianglesBySubMesh[subMeshIndex].Count);
            }
            return shortfall;
        }

        public void WarmupNormalSplitIndexBuffers()
        {
            trianglesBySubMesh[0].Capacity =
                CalculateInitialPieceTriangleIndexCapacity(NORMAL_SPLIT_INPUT_INDEX_WARMUP_CAPACITY);
            trianglesBySubMesh[1].Capacity = NORMAL_SPLIT_INPUT_INDEX_WARMUP_CAPACITY / 16;
        }

        public long GetSourceMeshIndexCapacityShortfall(
            Mesh sourceMesh, out long availableCapacity)
        {
            long shortfall = 0;
            availableCapacity = 0;
            for (int subMeshIndex = 0;
                 subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
            {
                int capacity = GetReusableSubMeshIndexCapacity(subMeshIndex);
                int required = (int)Math.Min(int.MaxValue,
                    (long)sourceMesh.GetIndexCount(subMeshIndex));
                availableCapacity += capacity;
                if (capacity < required)
                {
                    shortfall += (long)required - capacity;
                }
            }
            return shortfall;
        }

        // 縮小中にStackへ退避したSubMeshのBacking ArrayもCapacity評価に含める。
        private int GetReusableSubMeshIndexCapacity(int subMeshIndex)
        {
            if (subMeshIndex < trianglesBySubMesh.Count)
            {
                return trianglesBySubMesh[subMeshIndex].Capacity;
            }
            if (spareTrianglesBySubMesh == null)
            {
                return 0;
            }
            int spareIndex = trianglesBySubMesh.Count;
            foreach (List<int> indices in spareTrianglesBySubMesh)
            {
                if (spareIndex == subMeshIndex)
                {
                    return indices.Capacity;
                }
                spareIndex++;
            }
            return 0;
        }

        public string WorkingMeshName { get; set; }
        public Bounds BuilderBounds =>
            hasBounds
                ? new Bounds(
                    (minimumPosition + maximumPosition) * 0.5f,
                    maximumPosition - minimumPosition)
                : default;

        public PieceMeshBuilder(
            int subMeshCount,
            bool hasTangents,
            int sourceVertexCount,
            int estimatedVertexCapacity,
            bool supportsReuse,
            bool storeConnectivityVertexIds)
        {
            this.hasTangents = hasTangents;
            this.storeConnectivityVertexIds = storeConnectivityVertexIds;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DiagnosticBuilderId = ++nextDiagnosticBuilderId;
#endif

            using (createPositionsProfilerMarker.Auto())
            {
                vertices = new List<Vector3>(estimatedVertexCapacity);
            }
            using (createNormalsProfilerMarker.Auto())
            {
                normals = new List<Vector3>(estimatedVertexCapacity);
            }
            using (createUvProfilerMarker.Auto())
            {
                uv = new List<Vector2>(estimatedVertexCapacity);
            }
            using (createTangentsProfilerMarker.Auto())
            {
                tangents = new List<Vector4>(hasTangents ? estimatedVertexCapacity : 0);
            }
            using (createConnectivityProfilerMarker.Auto())
            {
                connectivityVertexIds = new List<int>(
                    storeConnectivityVertexIds ? estimatedVertexCapacity : 0);
            }
            using (createSourceIndexMapProfilerMarker.Auto())
            {
                outputVertexIndexBySourceVertexIndex = new int[sourceVertexCount];
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (captureCreatePieceBuilders)
            {
                newSourceIndexMapCount++;
            }
#endif
            using (createSubMeshBuffersProfilerMarker.Auto())
            {
                trianglesBySubMesh = new List<List<int>>(subMeshCount);
                spareTrianglesBySubMesh =
                    supportsReuse ? new Stack<List<int>>() : null;
                for (int i = 0; i < subMeshCount; i++)
                {
                    trianglesBySubMesh.Add(new List<int>());
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (captureCreatePieceBuilders)
                    {
                        newSubMeshIndexListCount++;
                    }
#endif
                }
            }
        }

        public void PrepareForUse(
            int subMeshCount,
            bool useTangents,
            int sourceVertexCount,
            int estimatedVertexCapacity,
            bool storeConnectivityVertexIds)
        {
            // Poolから取得するたびに保存設定とデータをリセットする。
            this.storeConnectivityVertexIds = storeConnectivityVertexIds;
            cacheTriangleAreas = false;
            foreach (List<float> triangleAreas in triangleDoubleAreasBySubMesh)
            {
                triangleAreas.Clear();
            }

            vertices.Clear();
            normals.Clear();
            uv.Clear();
            tangents.Clear();
            connectivityVertexIds.Clear();

            if (vertices.Capacity < estimatedVertexCapacity)
            {
                using (growPositionsProfilerMarker.Auto())
                {
                    vertices.Capacity = estimatedVertexCapacity;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (captureCreatePieceBuilders)
                {
                    grownPositionCount++;
                }
#endif
            }

            if (normals.Capacity < estimatedVertexCapacity)
            {
                using (growNormalsProfilerMarker.Auto())
                {
                    normals.Capacity = estimatedVertexCapacity;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (captureCreatePieceBuilders)
                {
                    grownNormalCount++;
                }
#endif
            }

            if (uv.Capacity < estimatedVertexCapacity)
            {
                using (growUvProfilerMarker.Auto())
                {
                    uv.Capacity = estimatedVertexCapacity;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (captureCreatePieceBuilders)
                {
                    grownUvCount++;
                }
#endif
            }

            if (useTangents &&
                tangents.Capacity < estimatedVertexCapacity)
            {
                using (growTangentsProfilerMarker.Auto())
                {
                    tangents.Capacity = estimatedVertexCapacity;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (captureCreatePieceBuilders)
                {
                    grownTangentCount++;
                }
#endif
            }

            if (storeConnectivityVertexIds &&
                connectivityVertexIds.Capacity < estimatedVertexCapacity)
            {
                using (growConnectivityProfilerMarker.Auto())
                {
                    connectivityVertexIds.Capacity = estimatedVertexCapacity;
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (captureCreatePieceBuilders)
                {
                    grownConnectivityCount++;
                }
#endif
            }

            if (outputVertexIndexBySourceVertexIndex.Length < sourceVertexCount)
            {
                // 旧内容は引き継ぐ必要がないため、Array.Resizeによるコピーは行わない。
                using (growSourceIndexMapProfilerMarker.Auto())
                {
                    outputVertexIndexBySourceVertexIndex =
                        new int[sourceVertexCount];
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (captureCreatePieceBuilders)
                {
                    grownSourceIndexMapCount++;
                }
#endif
            }
            else
            {
                // 0を未登録値として使うため、今回参照し得るSource Vertex範囲だけ初期化する。
                Array.Clear(
                    outputVertexIndexBySourceVertexIndex,
                    0,
                    sourceVertexCount);
            }

            using (resetSubMeshesProfilerMarker.Auto())
            {
                ResetSubMeshTriangleLists(subMeshCount);
            }

            hasTangents = useTangents;
            WorkingMeshName = null;
            OriginalTriangleCount = 0;
            TotalTriangleCount = 0;
            hasBounds = false;
            minimumPosition = default;
            maximumPosition = default;
        }

        /// <summary>
        /// 次の分割処理で使うTriangle面積を保持する。
        /// Source MeshのようにIndex設定済みならここで計算し、生成途中のPieceでは
        /// 以降のAddTriangle時に求める。通常分割には適用しない。
        /// </summary>
        public void EnableTriangleAreaCache()
        {
            for (int subMeshIndex = 0;
                 subMeshIndex < trianglesBySubMesh.Count;
                 subMeshIndex++)
            {
            using var cacheTriangleAreasSample = MeshPlaneSplitter.cacheTriangleAreasProfilerMarker.Auto();
                if (triangleDoubleAreasBySubMesh.Count <= subMeshIndex)
                {
                    using (createTriangleAreaListsProfilerMarker.Auto())
                    {
                        triangleDoubleAreasBySubMesh.Add(new List<float>());
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (captureCreatePieceBuilders)
                    {
                        newTriangleAreaListCount++;
                    }
#endif
                }

                List<float> triangleAreas = triangleDoubleAreasBySubMesh[subMeshIndex];
                triangleAreas.Clear();

                List<int> indices = trianglesBySubMesh[subMeshIndex];
                for (int index = 0; index < indices.Count; index += 3)
                {
                    Vector3 a = vertices[indices[index]];
                    Vector3 b = vertices[indices[index + 1]];
                    Vector3 c = vertices[indices[index + 2]];
                    triangleAreas.Add(Vector3.Cross(b - a, c - a).magnitude);
                }
            }

            cacheTriangleAreas = true;
        }

        public void SetSourceBounds(Bounds bounds)
        {
            minimumPosition = bounds.min;
            maximumPosition = bounds.max;
            hasBounds = true;
        }

        // Poolへ戻す際に件数をゼロにする。ListのCapacityは再利用する。
        public void ClearForPool()
        {
            cacheTriangleAreas = false;
            foreach (List<float> triangleAreas in triangleDoubleAreasBySubMesh)
            {
                triangleAreas.Clear();
            }

            vertices.Clear();
            normals.Clear();
            uv.Clear();
            tangents.Clear();
            connectivityVertexIds.Clear();
            foreach (List<int> triangles in trianglesBySubMesh)
            {
                triangles.Clear();
            }
            WorkingMeshName = null;
            hasBounds = false;
            OriginalTriangleCount = 0;
            TotalTriangleCount = 0;
        }

        private void ResetSubMeshTriangleLists(
            int requiredSubMeshCount)
        {
            for (int subMeshIndex = 0;
                 subMeshIndex < trianglesBySubMesh.Count;
                 subMeshIndex++)
            {
                trianglesBySubMesh[subMeshIndex].Clear();
            }

            if (trianglesBySubMesh.Count > requiredSubMeshCount)
            {
                // Pool Builderは減ったSubMeshのListも次の分割用に退避する。
                while (trianglesBySubMesh.Count > requiredSubMeshCount)
                {
                    int lastIndex =
                        trianglesBySubMesh.Count - 1;

                    List<int> spareTriangles =
                        trianglesBySubMesh[lastIndex];

                    trianglesBySubMesh.RemoveAt(lastIndex);
                    spareTrianglesBySubMesh.Push(spareTriangles);
                }
            }

            if (trianglesBySubMesh.Capacity < requiredSubMeshCount)
            {
                trianglesBySubMesh.Capacity = requiredSubMeshCount;
            }

            while (trianglesBySubMesh.Count < requiredSubMeshCount)
            {
                bool createNewList = spareTrianglesBySubMesh.Count == 0;
                List<int> triangles =
                    createNewList
                        ? new List<int>()
                        : spareTrianglesBySubMesh.Pop();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (createNewList && captureCreatePieceBuilders)
                {
                    newSubMeshIndexListCount++;
                }
#endif
                triangles.Clear();
                trianglesBySubMesh.Add(triangles);
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                int capacityBefore = triangles.Capacity;
#endif
                triangles.Capacity =
                    estimatedIndexCapacity;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (DiagnosticOwner != null)
                {
                    DiagnosticOwner.RecordTriangleLoopBufferGrowth(
                        LOOP_INDEX, capacityBefore, triangles.Capacity, DiagnosticBuilderId);
                }
#endif
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

                firstIndex =
                    AddVertex(first);

                secondIndex =
                    AddVertex(second);

                thirdIndex =
                    AddVertex(third);

                bool firstIndexGrew = AddWithGrowthMarker(trianglesBySubMesh[subMeshIndex], firstIndex, outputIndexGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , DiagnosticOwner, LOOP_INDEX, DiagnosticBuilderId
#endif
                    );
                bool secondIndexGrew = AddWithGrowthMarker(trianglesBySubMesh[subMeshIndex], secondIndex, outputIndexGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , DiagnosticOwner, LOOP_INDEX, DiagnosticBuilderId
#endif
                    );
                bool thirdIndexGrew = AddWithGrowthMarker(trianglesBySubMesh[subMeshIndex], thirdIndex, outputIndexGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , DiagnosticOwner, LOOP_INDEX, DiagnosticBuilderId
#endif
                    );
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (TrackCreateBuilderDiagnostics)
                {
                    DiagnosticTriangleIndexGrowthCount += (firstIndexGrew ? 1 : 0) +
                        (secondIndexGrew ? 1 : 0) + (thirdIndexGrew ? 1 : 0);
                }
#endif

            // Connected Component入口でSubMesh全Indexを再走査してTriangle数を数え直さないため、
            // BuilderがTriangle追加時点で総数を保持する。Capも連結判定対象なので総数へ含める。
            TotalTriangleCount++;

            if (cacheTriangleAreas)
            {
                bool areaGrew = AddWithGrowthMarker(triangleDoubleAreasBySubMesh[subMeshIndex],
                    Vector3.Cross(
                        second.Position - first.Position,
                        third.Position - first.Position).magnitude, triangleAreaGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , DiagnosticOwner, LOOP_AREA, DiagnosticBuilderId
#endif
                    );
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (TrackCreateBuilderDiagnostics && areaGrew)
                {
                    DiagnosticTriangleAreaGrowthCount++;
                }
#endif
            }

            if (!isCap)
            {
                OriginalTriangleCount++;
            }
        }

        public float CalculateSurfaceArea()
        {
            float totalArea = 0f;
            for (int subMeshIndex = 0; subMeshIndex < trianglesBySubMesh.Count; subMeshIndex++)
            {
            using var calculateSurfaceAreaSample = MeshPlaneSplitter.calculateSurfaceAreasProfilerMarker.Auto();
                if (cacheTriangleAreas)
                {
                    List<float> doubleAreas = triangleDoubleAreasBySubMesh[subMeshIndex];
                    for (int index = 0; index < doubleAreas.Count; index++)
                    {
                        totalArea += doubleAreas[index] * 0.5f;
                    }
                }
                else
                {
                    // 通常分割やキャッシュを使わない呼出元の結果は従来どおり計算する。
                    List<int> indices = trianglesBySubMesh[subMeshIndex];
                    for (int index = 0; index < indices.Count; index += 3)
                    {
                        Vector3 a = vertices[indices[index]];
                        Vector3 b = vertices[indices[index + 1]];
                        Vector3 c = vertices[indices[index + 2]];
                        totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                    }
                }
            }
            return totalArea;
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

            try
            {
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
            catch
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(mesh);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
                throw;
            }

            }
        }

        private int AddVertex(
            VertexData vertex)
        {
            if (vertex.HasSourceVertexIndex)
            {
                int storedOutputIndex =
                    outputVertexIndexBySourceVertexIndex[
                        vertex.SourceVertexIndex];

                if (storedOutputIndex != 0)
                {
                    return storedOutputIndex - 1;
                }
            }

            int index =
                vertices.Count;

            bool positionGrew = AddWithGrowthMarker(vertices, vertex.Position, outputVertexGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                , DiagnosticOwner, LOOP_POSITION, DiagnosticBuilderId
#endif
                );
            bool normalGrew = AddWithGrowthMarker(normals, vertex.Normal, outputVertexGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                , DiagnosticOwner, LOOP_NORMAL, DiagnosticBuilderId
#endif
                );
            bool uvGrew = AddWithGrowthMarker(uv, vertex.Uv, outputVertexGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                , DiagnosticOwner, LOOP_UV, DiagnosticBuilderId
#endif
                );
            bool tangentGrew = false;
            if (hasTangents)
            {
                tangentGrew = AddWithGrowthMarker(tangents, vertex.Tangent, outputVertexGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , DiagnosticOwner, LOOP_TANGENT, DiagnosticBuilderId
#endif
                    );
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TrackCreateBuilderDiagnostics)
            {
                DiagnosticPositionGrowthCount += positionGrew ? 1 : 0;
                DiagnosticNormalGrowthCount += normalGrew ? 1 : 0;
                DiagnosticUvGrowthCount += uvGrew ? 1 : 0;
                DiagnosticTangentGrowthCount += tangentGrew ? 1 : 0;
            }
#endif

            if (storeConnectivityVertexIds)
            {
                AddWithGrowthMarker(connectivityVertexIds, vertex.ConnectivityVertexId, outputVertexGrowthProfilerMarker
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    , DiagnosticOwner, LOOP_CONNECTIVITY, DiagnosticBuilderId
#endif
                    );
            }

            UpdateBounds(vertex.Position);

            if (vertex.HasSourceVertexIndex)
            {
                outputVertexIndexBySourceVertexIndex[
                    vertex.SourceVertexIndex] =
                    index + 1;
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
