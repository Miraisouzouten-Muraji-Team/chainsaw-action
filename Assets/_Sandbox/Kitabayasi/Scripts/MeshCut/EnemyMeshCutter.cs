using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemy死亡時に切断対象Meshを取得し、
/// チェンソー軌跡を使って2つの静的Meshへ分割して表示する。
/// </summary>
/// <remarks>
/// SkinnedMeshRendererの場合は死亡時の現在姿勢をBakeしてから切断する。
/// MeshFilter + MeshRendererの場合はMeshFilterのMeshを切断する。
///
/// HP計算、死亡判定、攻撃受信、チェンソー軌跡記録、
/// Triangle分割アルゴリズム、切断後の物理演出は担当しない。
/// </remarks>
public sealed class EnemyMeshCutter : MonoBehaviour
{
    [Header("切断対象")]

    [Tooltip(
        "SkinnedMeshRendererを使用するEnemyの切断対象。"
        + "MeshFilterと同時には設定しない。")]
    [SerializeField]
    private SkinnedMeshRenderer targetSkinnedMeshRenderer;

    [Tooltip(
        "MeshFilter + MeshRendererを使用するEnemyの切断対象。"
        + "同じGameObjectにMeshRendererが必要。"
        + "SkinnedMeshRendererと同時には設定しない。")]
    [SerializeField]
    private MeshFilter targetMeshFilter;

    [Tooltip("切断面専用SubMeshへ設定するMaterial。")]
    [SerializeField]
    private Material cutSurfaceMaterial;

    [Header("切断Plane設定")]

    [Tooltip("Bake済み軌跡からCut Plane候補を作る際、各候補位置の代表刃方向・代表移動方向の算出に使用するサンプル数。")]
    [Min(2)]
    [SerializeField]
    private int cutDirectionSampleCount = 4;

    [Tooltip("EnemyMeshCutterが付いているEnemy本体のローカル空間で、最終的な切断境界を許可する範囲の軸。Yなら上下分割位置、Xなら左右分割位置の制御に使う。")]
    [SerializeField]
    private MeshCutRangeAxis cutRangeAxis = MeshCutRangeAxis.Y;

    [Tooltip("Cut Range Axis方向のMesh Boundsに対して、最終的な切断境界を許可する範囲の下限。0がBounds最小、1がBounds最大。")]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float cutRangeMinimumNormalized = 0.2f;

    [Tooltip("Cut Range Axis方向のMesh Boundsに対して、最終的な切断境界を許可する範囲の上限。0がBounds最小、1がBounds最大。")]
    [Range(0.0f, 1.0f)]
    [SerializeField]
    private float cutRangeMaximumNormalized = 0.8f;

    [Tooltip("切断Plane上へ生成する断面UVのスケール。")]
    [Min(0.0001f)]
    [SerializeField]
    private float cutSurfaceUvScale = 1.0f;

    [Header("Scene表示")]

    [Tooltip("選択中のEnemyに、最終的な切断境界を許可するCut Rangeを赤く表示する。")]
    [SerializeField]
    private bool showCutRangeGizmo = true;

    [Header("診断ログ")]

    [Tooltip(
        "2分割死亡演出の入力軌跡、切断対象Mesh、Processor結果、生成片までを段階的にConsoleへ出力する。" +
        "原因特定後は無効化してよい。")]
    [SerializeField]
    private bool enableDiagnosticLogs = true;

    private readonly ChainsawMeshCutProcessor cutProcessor =
        new ChainsawMeshCutProcessor();

    private Mesh firstPieceRuntimeMesh;
    private Mesh secondPieceRuntimeMesh;

    private GameObject firstPieceObject;
    private GameObject secondPieceObject;

    private bool hasCompletedCut;

    /// <summary>
    /// 確定済みチェンソー軌跡を使ってEnemy Meshの切断を試みる。
    /// </summary>
    /// <param name="trajectory">
    /// 死亡させたチェンソー攻撃が保持している確定済み軌跡。
    /// </param>
    /// <returns>
    /// 2つのMesh生成と表示切り替えまで成功した場合はtrue。
    /// </returns>
    public bool TryCut(ChainsawAttackTrajectory trajectory)
    {
        LogDiagnostic(
            "Cutter-01 Request",
            trajectory == null
                ? "TryCutを受信しました。trajectory=null"
                : $"TryCutを受信しました。{BuildTrajectorySummary(trajectory)}");

        if (hasCompletedCut)
        {
            Debug.LogWarning(
                $"{nameof(EnemyMeshCutter)}: "
                + "このEnemyは既にMesh分割済みです。",
                this);

            LogDiagnosticWarning(
                "Cutter-01 Request",
                "hasCompletedCut=trueのため再分割を中止しました。");

            return false;
        }

        if (!ValidateCutRequest(
                trajectory,
                out string failureReason))
        {
            LogDiagnosticWarning(
                "Cutter-01 Request",
                $"入力・Inspector設定の検証で停止しました。{failureReason}");

            LogCutFailure(failureReason);
            return false;
        }

        if (!TryGetCutSource(
                out Mesh sourceMesh,
                out Renderer sourceRenderer,
                out bool destroySourceMeshAfterCut,
                out failureReason))
        {
            LogDiagnosticWarning(
                "Cutter-02 Source",
                $"切断対象Meshの取得で停止しました。{failureReason}");

            LogCutFailure(failureReason);
            return false;
        }

        try
        {
            LogDiagnostic(
                "Cutter-02 Source",
                $"切断対象を取得しました。{BuildMeshSummary(sourceMesh)} / " +
                $"Renderer={sourceRenderer.name}, {BuildTransformSummary(sourceRenderer.transform)}, " +
                $"CutRangeOwner={BuildTransformSummary(transform)}");

            if (!ValidateSourceMeshMaterialLayout(
                    sourceMesh,
                    sourceRenderer,
                    out failureReason))
            {
                LogDiagnosticWarning(
                    "Cutter-02 Source",
                    $"Mesh / Material構成の検証で停止しました。{failureReason}");

                LogCutFailure(failureReason);
                return false;
            }

            ConvertTrajectoryToRendererLocalSpace(
                trajectory,
                sourceRenderer.transform,
                out List<Vector3> localTipPositions,
                out List<Vector3> localRootPositions);

            LogDiagnostic(
                "Cutter-03 LocalTrajectory",
                BuildLocalTrajectorySummary(
                    localTipPositions,
                    localRootPositions));

            Matrix4x4 sourceToCutRangeSpace =
                transform.worldToLocalMatrix
                * sourceRenderer.transform.localToWorldMatrix;

            LogDiagnostic(
                "Cutter-04 Processor",
                "ChainsawMeshCutProcessor.TryCutを開始します。" +
                $" CutDirectionSampleCount={cutDirectionSampleCount}," +
                $" CutRangeAxis={cutRangeAxis}," +
                $" CutRange={cutRangeMinimumNormalized:F3}-{cutRangeMaximumNormalized:F3}," +
                $" CutSurfaceUvScale={cutSurfaceUvScale:F4}," +
                $" SourceToCutRangeDeterminant={sourceToCutRangeSpace.determinant:F6}");

            bool succeeded = cutProcessor.TryCut(
                sourceMesh,
                localTipPositions,
                localRootPositions,
                cutDirectionSampleCount,
                sourceToCutRangeSpace,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                cutSurfaceUvScale,
                out Mesh firstPieceMesh,
                out Mesh secondPieceMesh,
                out failureReason);

            if (!succeeded)
            {
                LogDiagnosticWarning(
                    "Cutter-04 Processor",
                    "ChainsawMeshCutProcessor内で停止しました。" +
                    $" failureReason={failureReason}");

                LogCutFailure(failureReason);
                return false;
            }

            LogDiagnostic(
                "Cutter-04 Processor",
                "Processorによる2Mesh生成に成功しました。" +
                $" First={BuildMeshSummary(firstPieceMesh)} /" +
                $" Second={BuildMeshSummary(secondPieceMesh)}");

            Material[] pieceMaterials =
                BuildPieceMaterials(sourceRenderer);

            firstPieceRuntimeMesh =
                firstPieceMesh;

            secondPieceRuntimeMesh =
                secondPieceMesh;

            firstPieceObject = CreatePieceObject(
                "FirstPiece",
                firstPieceRuntimeMesh,
                pieceMaterials,
                sourceRenderer);

            secondPieceObject = CreatePieceObject(
                "SecondPiece",
                secondPieceRuntimeMesh,
                pieceMaterials,
                sourceRenderer);

            LogDiagnostic(
                "Cutter-05 PieceObjects",
                $"切断片GameObjectを生成しました。" +
                $" First={firstPieceObject.name}, Second={secondPieceObject.name}," +
                $" ParentRenderer={sourceRenderer.name}");

            sourceRenderer.enabled = false;

            hasCompletedCut = true;

            LogDiagnostic(
                "Cutter-06 Complete",
                "元Rendererを無効化し、2分割処理を完了しました。" +
                " このログまで出ている場合、以降の問題は切断片取得またはBurst/Shrink開始側です。");

            return true;
        }
        finally
        {
            if (destroySourceMeshAfterCut)
            {
                DestroyRuntimeMesh(sourceMesh);
            }
        }
    }

    /// <summary>
    /// Mesh分割によって生成された2つの切断片を取得する。
    /// </summary>
    /// <returns>
    /// Mesh分割済みで、両方の切断片が存在する場合はtrue。
    /// </returns>
    public bool TryGetCutPieceTransforms(
        out Transform firstPieceTransform,
        out Transform secondPieceTransform)
    {
        firstPieceTransform =
            firstPieceObject != null
                ? firstPieceObject.transform
                : null;

        secondPieceTransform =
            secondPieceObject != null
                ? secondPieceObject.transform
                : null;

        return hasCompletedCut &&
               firstPieceTransform != null &&
               secondPieceTransform != null;
    }

    /// <summary>
    /// 切断要求に必要な設定と入力を検証する。
    /// </summary>
    private bool ValidateCutRequest(
        ChainsawAttackTrajectory trajectory,
        out string failureReason)
    {
        bool hasSkinnedMeshTarget =
            targetSkinnedMeshRenderer != null;

        bool hasMeshFilterTarget =
            targetMeshFilter != null;

        if (!hasSkinnedMeshTarget &&
            !hasMeshFilterTarget)
        {
            failureReason =
                "切断対象が設定されていません。"
                + "Target Skinned Mesh Rendererまたは"
                + "Target Mesh Filterのどちらかを設定してください。";

            return false;
        }

        if (hasSkinnedMeshTarget &&
            hasMeshFilterTarget)
        {
            failureReason =
                "Target Skinned Mesh Rendererと"
                + "Target Mesh Filterの両方が設定されています。"
                + "切断対象はどちらか一方だけ設定してください。";

            return false;
        }

        if (hasSkinnedMeshTarget)
        {
            if (targetSkinnedMeshRenderer.sharedMesh == null)
            {
                failureReason =
                    "Target Skinned Mesh Rendererに"
                    + "sharedMeshがありません。";

                return false;
            }
        }
        else
        {
            if (targetMeshFilter.sharedMesh == null)
            {
                failureReason =
                    "Target Mesh FilterにsharedMeshがありません。";

                return false;
            }

            MeshRenderer targetMeshRenderer =
                targetMeshFilter.GetComponent<MeshRenderer>();

            if (targetMeshRenderer == null)
            {
                failureReason =
                    "Target Mesh Filterと同じGameObjectに"
                    + "MeshRendererがありません。";

                return false;
            }

            if (!targetMeshFilter.sharedMesh.isReadable)
            {
                failureReason =
                    "Target Mesh FilterのMeshが読み取り不可です。"
                    + "Mesh切断では頂点・Triangle情報を読み取るため、"
                    + "Model Import Settingsの"
                    + "Read/Write Enabledを有効にしてください。";

                return false;
            }
        }

        if (cutRangeMinimumNormalized < 0.0f ||
            cutRangeMinimumNormalized > 1.0f ||
            cutRangeMaximumNormalized < 0.0f ||
            cutRangeMaximumNormalized > 1.0f)
        {
            failureReason =
                "切断位置有効範囲は0～1の範囲で設定してください。";

            return false;
        }

        if (cutRangeMinimumNormalized >=
            cutRangeMaximumNormalized)
        {
            failureReason =
                "Cut Range MinimumはCut Range Maximumより小さい値にしてください。";

            return false;
        }

        if (cutSurfaceMaterial == null)
        {
            failureReason =
                "Cut Surface Materialが設定されていません。";

            return false;
        }

        if (trajectory == null)
        {
            failureReason =
                "ChainsawAttackTrajectoryがnullです。";

            return false;
        }

        if (trajectory.SampleCount < 2)
        {
            failureReason =
                "Mesh切断には2サンプル以上の"
                + "チェンソー軌跡が必要です。";

            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// 現在設定されている切断対象から、
    /// ChainsawMeshCutProcessorへ渡すMeshとRendererを取得する。
    /// </summary>
    /// <remarks>
    /// SkinnedMeshRendererの場合は現在姿勢をRuntime MeshへBakeする。
    /// MeshFilterの場合はsharedMeshを読み取り専用の入力として使用する。
    /// </remarks>
    private bool TryGetCutSource(
        out Mesh sourceMesh,
        out Renderer sourceRenderer,
        out bool destroySourceMeshAfterCut,
        out string failureReason)
    {
        sourceMesh = null;
        sourceRenderer = null;
        destroySourceMeshAfterCut = false;

        if (targetSkinnedMeshRenderer != null)
        {
            var bakedMesh = new Mesh
            {
                name =
                    $"{targetSkinnedMeshRenderer.name}_DeathBake"
            };

            // BakeMeshの頂点はSkinnedMeshRenderer Transform基準。
            // 生成片も同Rendererの子へlocal identityで配置するため、
            // ScaleをMesh側へ焼き込まず、元Transform側で一度だけ適用する。
            targetSkinnedMeshRenderer.BakeMesh(
                bakedMesh,
                false);

            bakedMesh.RecalculateBounds();

            sourceMesh = bakedMesh;
            sourceRenderer = targetSkinnedMeshRenderer;
            destroySourceMeshAfterCut = true;

            failureReason = null;
            return true;
        }

        if (targetMeshFilter != null)
        {
            MeshRenderer meshRenderer =
                targetMeshFilter.GetComponent<MeshRenderer>();

            if (meshRenderer == null)
            {
                failureReason =
                    "Target Mesh Filterと同じGameObjectに"
                    + "MeshRendererがありません。";

                return false;
            }

            sourceMesh =
                targetMeshFilter.sharedMesh;

            sourceRenderer =
                meshRenderer;

            // sharedMeshはProject AssetまたはUnityが管理するMeshなので、
            // このEnemyMeshCutterからDestroyしてはいけない。
            destroySourceMeshAfterCut = false;

            failureReason = null;
            return true;
        }

        failureReason =
            "切断対象からMeshを取得できませんでした。";

        return false;
    }

    /// <summary>
    /// 元MeshのSubMesh数とRendererのMaterial数が一致しているか確認する。
    /// </summary>
    private bool ValidateSourceMeshMaterialLayout(
        Mesh sourceMesh,
        Renderer sourceRenderer,
        out string failureReason)
    {
        if (sourceMesh == null)
        {
            failureReason =
                "切断対象Meshがnullです。";

            return false;
        }

        if (sourceRenderer == null)
        {
            failureReason =
                "切断対象Rendererがnullです。";

            return false;
        }

        Material[] sourceMaterials =
            sourceRenderer.sharedMaterials;

        if (sourceMaterials == null ||
            sourceMaterials.Length == 0)
        {
            failureReason =
                "切断対象RendererにMaterialがありません。";

            return false;
        }

        if (sourceMaterials.Length !=
            sourceMesh.subMeshCount)
        {
            failureReason =
                "元Material数とMeshのSubMesh数が"
                + "一致していません。"
                + "切断後のMaterial対応を安全に"
                + "維持できないため処理を中止します。";

            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// World座標で記録されたチェンソー軌跡を、
    /// 切断対象RendererのLocal座標へ変換する。
    /// </summary>
    private void ConvertTrajectoryToRendererLocalSpace(
        ChainsawAttackTrajectory trajectory,
        Transform rendererTransform,
        out List<Vector3> localTipPositions,
        out List<Vector3> localRootPositions)
    {
        int sampleCount =
            trajectory.SampleCount;

        localTipPositions =
            new List<Vector3>(sampleCount);

        localRootPositions =
            new List<Vector3>(sampleCount);

        for (int i = 0; i < sampleCount; i++)
        {
            ChainsawBladeTrajectorySample sample =
                trajectory.Samples[i];

            localTipPositions.Add(
                rendererTransform.InverseTransformPoint(
                    sample.TipPosition));

            localRootPositions.Add(
                rendererTransform.InverseTransformPoint(
                    sample.RootPosition));
        }
    }

    /// <summary>
    /// 元RendererのMaterialに切断面Materialを追加する。
    /// </summary>
    private Material[] BuildPieceMaterials(
        Renderer sourceRenderer)
    {
        Material[] sourceMaterials =
            sourceRenderer.sharedMaterials;

        var pieceMaterials =
            new Material[sourceMaterials.Length + 1];

        for (int i = 0;
             i < sourceMaterials.Length;
             i++)
        {
            pieceMaterials[i] =
                sourceMaterials[i];
        }

        pieceMaterials[pieceMaterials.Length - 1] =
            cutSurfaceMaterial;

        return pieceMaterials;
    }

    /// <summary>
    /// 切断後Meshを表示するGameObjectを生成する。
    /// </summary>
    private GameObject CreatePieceObject(
        string suffix,
        Mesh mesh,
        Material[] materials,
        Renderer sourceRenderer)
    {
        var pieceObject =
            new GameObject(
                $"{sourceRenderer.name}_Cut_{suffix}");

        pieceObject.layer =
            sourceRenderer.gameObject.layer;

        Transform pieceTransform =
            pieceObject.transform;

        pieceTransform.SetParent(
            sourceRenderer.transform,
            false);

        pieceTransform.localPosition =
            Vector3.zero;

        pieceTransform.localRotation =
            Quaternion.identity;

        pieceTransform.localScale =
            Vector3.one;

        MeshFilter meshFilter =
            pieceObject.AddComponent<MeshFilter>();

        meshFilter.sharedMesh =
            mesh;

        MeshRenderer meshRenderer =
            pieceObject.AddComponent<MeshRenderer>();

        CopyRendererSettings(
            sourceRenderer,
            meshRenderer);

        meshRenderer.sharedMaterials =
            materials;

        return pieceObject;
    }

    /// <summary>
    /// 元Rendererから切断後Rendererへ、
    /// 表示に必要な共通設定をコピーする。
    /// </summary>
    private static void CopyRendererSettings(
        Renderer source,
        MeshRenderer destination)
    {
        destination.shadowCastingMode =
            source.shadowCastingMode;

        destination.receiveShadows =
            source.receiveShadows;

        destination.lightProbeUsage =
            source.lightProbeUsage;

        destination.reflectionProbeUsage =
            source.reflectionProbeUsage;

        destination.probeAnchor =
            source.probeAnchor;

        destination.sortingLayerID =
            source.sortingLayerID;

        destination.sortingOrder =
            source.sortingOrder;
    }

    private string BuildTrajectorySummary(
        ChainsawAttackTrajectory trajectory)
    {
        if (trajectory == null ||
            trajectory.SampleCount == 0)
        {
            return "Trajectoryにサンプルがありません。";
        }

        int lastIndex =
            trajectory.SampleCount - 1;

        int directionWindowStartIndex =
            Mathf.Max(
                0,
                trajectory.SampleCount -
                cutDirectionSampleCount);

        ChainsawBladeTrajectorySample firstSample =
            trajectory.Samples[0];

        ChainsawBladeTrajectorySample directionStartSample =
            trajectory.Samples[directionWindowStartIndex];

        ChainsawBladeTrajectorySample lastSample =
            trajectory.Samples[lastIndex];

        return
            $"Samples={trajectory.SampleCount}, " +
            $"DirectionWindow={directionWindowStartIndex}-{lastIndex}, " +
            $"FirstCenter={FormatVector3(GetSampleCenter(firstSample))}, " +
            $"DirectionWindowStartCenter={FormatVector3(GetSampleCenter(directionStartSample))}, " +
            $"LastCenter={FormatVector3(GetSampleCenter(lastSample))}, " +
            $"LastTip={FormatVector3(lastSample.TipPosition)}, " +
            $"LastRoot={FormatVector3(lastSample.RootPosition)}, " +
            $"LastSampleTime={lastSample.SampleTime:F4}";
    }

    private string BuildLocalTrajectorySummary(
        IReadOnlyList<Vector3> localTipPositions,
        IReadOnlyList<Vector3> localRootPositions)
    {
        if (localTipPositions == null ||
            localRootPositions == null ||
            localTipPositions.Count == 0 ||
            localRootPositions.Count == 0)
        {
            return "Renderer Local軌跡にサンプルがありません。";
        }

        int sampleCount =
            Mathf.Min(
                localTipPositions.Count,
                localRootPositions.Count);

        int lastIndex =
            sampleCount - 1;

        int directionWindowStartIndex =
            Mathf.Max(
                0,
                sampleCount -
                cutDirectionSampleCount);

        Vector3 firstCenter =
            GetBladeCenter(
                localTipPositions[0],
                localRootPositions[0]);

        Vector3 directionWindowStartCenter =
            GetBladeCenter(
                localTipPositions[directionWindowStartIndex],
                localRootPositions[directionWindowStartIndex]);

        Vector3 lastCenter =
            GetBladeCenter(
                localTipPositions[lastIndex],
                localRootPositions[lastIndex]);

        return
            $"Renderer Local軌跡: Samples={sampleCount}, " +
            $"DirectionWindow={directionWindowStartIndex}-{lastIndex}, " +
            $"FirstCenter={FormatVector3(firstCenter)}, " +
            $"DirectionWindowStartCenter={FormatVector3(directionWindowStartCenter)}, " +
            $"LastCenter={FormatVector3(lastCenter)}, " +
            $"LastTip={FormatVector3(localTipPositions[lastIndex])}, " +
            $"LastRoot={FormatVector3(localRootPositions[lastIndex])}";
    }

    private static Vector3 GetSampleCenter(
        ChainsawBladeTrajectorySample sample)
    {
        return GetBladeCenter(
            sample.TipPosition,
            sample.RootPosition);
    }

    private static Vector3 GetBladeCenter(
        Vector3 tipPosition,
        Vector3 rootPosition)
    {
        return (tipPosition + rootPosition) * 0.5f;
    }

    private static string BuildMeshSummary(
        Mesh mesh)
    {
        if (mesh == null)
        {
            return "Mesh=null";
        }

        long triangleCount = 0;

        for (int subMeshIndex = 0;
             subMeshIndex < mesh.subMeshCount;
             subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) !=
                MeshTopology.Triangles)
            {
                continue;
            }

            triangleCount +=
                mesh.GetIndexCount(subMeshIndex) / 3;
        }

        Bounds bounds =
            mesh.bounds;

        return
            $"Mesh='{mesh.name}', Vertices={mesh.vertexCount}, " +
            $"Triangles={triangleCount}, SubMeshes={mesh.subMeshCount}, " +
            $"Readable={mesh.isReadable}, " +
            $"BoundsCenter={FormatVector3(bounds.center)}, " +
            $"BoundsSize={FormatVector3(bounds.size)}";
    }

    private static string BuildTransformSummary(
        Transform targetTransform)
    {
        if (targetTransform == null)
        {
            return "Transform=null";
        }

        return
            $"Transform='{targetTransform.name}', " +
            $"WorldPosition={FormatVector3(targetTransform.position)}, " +
            $"WorldRotationEuler={FormatVector3(targetTransform.rotation.eulerAngles)}, " +
            $"LossyScale={FormatVector3(targetTransform.lossyScale)}";
    }

    private static string FormatVector3(
        Vector3 value)
    {
        return
            $"({value.x:F4}, {value.y:F4}, {value.z:F4})";
    }

    private void LogDiagnostic(
        string stage,
        string message)
    {
        if (!enableDiagnosticLogs)
        {
            return;
        }

        Debug.Log(
            $"[MeshCutDiagnostic][{stage}] {message}",
            this);
    }

    private void LogDiagnosticWarning(
        string stage,
        string message)
    {
        if (!enableDiagnosticLogs)
        {
            return;
        }

        Debug.LogWarning(
            $"[MeshCutDiagnostic][{stage}] {message}",
            this);
    }

    /// <summary>
    /// Mesh切断失敗理由をConsoleへ出力する。
    /// </summary>
    private void LogCutFailure(
        string failureReason)
    {
        Debug.LogWarning(
            $"{nameof(EnemyMeshCutter)}: "
            + "Mesh分割に失敗しました。"
            + $" 理由: {failureReason}",
            this);
    }

    private void OnDestroy()
    {
        DestroyRuntimeMesh(
            firstPieceRuntimeMesh);

        DestroyRuntimeMesh(
            secondPieceRuntimeMesh);

        firstPieceRuntimeMesh = null;
        secondPieceRuntimeMesh = null;
    }

    /// <summary>
    /// EnemyMeshCutterが所有するRuntime Meshを破棄する。
    /// </summary>
    private static void DestroyRuntimeMesh(
        Mesh mesh)
    {
        if (mesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(mesh);
        }
        else
        {
            DestroyImmediate(mesh);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        cutDirectionSampleCount =
            Mathf.Max(
                2,
                cutDirectionSampleCount);

        cutRangeMinimumNormalized =
            Mathf.Clamp01(
                cutRangeMinimumNormalized);

        cutRangeMaximumNormalized =
            Mathf.Clamp01(
                cutRangeMaximumNormalized);

        if (cutRangeMinimumNormalized >=
            cutRangeMaximumNormalized)
        {
            const float minimumNormalizedRangeWidth = 0.01f;

            if (cutRangeMinimumNormalized >= 1.0f)
            {
                cutRangeMinimumNormalized =
                    1.0f - minimumNormalizedRangeWidth;
            }

            cutRangeMaximumNormalized =
                Mathf.Min(
                    1.0f,
                    cutRangeMinimumNormalized
                    + minimumNormalizedRangeWidth);
        }

        cutSurfaceUvScale =
            Mathf.Max(
                0.0001f,
                cutSurfaceUvScale);
    }
#endif

    private void OnDrawGizmosSelected()
    {
        if (!showCutRangeGizmo)
        {
            return;
        }

        if (!TryGetPreviewBounds(
                out Bounds sourceBounds))
        {
            return;
        }

        if (!ChainsawMeshCutProcessor.TryCalculateCutRangeBounds(
                sourceBounds,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                out Bounds cutRangeBounds))
        {
            return;
        }

        Matrix4x4 previousMatrix =
            Gizmos.matrix;

        Color previousColor =
            Gizmos.color;

        // Cut Range AxisはEnemyMeshCutterが付いている本体のローカル軸。
        // SkinnedMeshRenderer子階層のFBX回転に引きずられないよう、
        // Gizmoも同じ基準空間で描画する。
        Gizmos.matrix =
            transform.localToWorldMatrix;

        Gizmos.color =
            new Color(
                1.0f,
                0.0f,
                0.0f,
                0.18f);

        Gizmos.DrawCube(
            cutRangeBounds.center,
            cutRangeBounds.size);

        Gizmos.color =
            new Color(
                1.0f,
                0.0f,
                0.0f,
                1.0f);

        Gizmos.DrawWireCube(
            cutRangeBounds.center,
            cutRangeBounds.size);

        Gizmos.matrix =
            previousMatrix;

        Gizmos.color =
            previousColor;
    }

    /// <summary>
    /// Sceneビューで最終切断境界の許可範囲を表示するため、
    /// 切断対象Meshの現在形状をEnemyMeshCutterローカル空間のBoundsへ変換する。
    /// </summary>
    private bool TryGetPreviewBounds(
        out Bounds sourceBounds)
    {
        if (targetMeshFilter != null &&
            targetMeshFilter.sharedMesh != null)
        {
            return TryCalculateBoundsInCutRangeSpace(
                targetMeshFilter.sharedMesh,
                targetMeshFilter.transform,
                out sourceBounds);
        }

        if (targetSkinnedMeshRenderer != null &&
            targetSkinnedMeshRenderer.sharedMesh != null)
        {
            var previewMesh = new Mesh
            {
                name =
                    $"{targetSkinnedMeshRenderer.name}_CutRangePreview",
                hideFlags = HideFlags.HideAndDontSave
            };

            // BakeMeshの頂点はSkinnedMeshRenderer Transform基準。
            // そのままBoundsを使うとFBX側のローカル軸がCut Range Axisへ混ざるため、
            // EnemyMeshCutterローカル空間へ変換してからBoundsを求める。
            targetSkinnedMeshRenderer.BakeMesh(
                previewMesh,
                false);

            bool succeeded =
                TryCalculateBoundsInCutRangeSpace(
                    previewMesh,
                    targetSkinnedMeshRenderer.transform,
                    out sourceBounds);

            DestroyRuntimeMesh(
                previewMesh);

            return succeeded;
        }

        sourceBounds = default;
        return false;
    }

    /// <summary>
    /// sourceTransformローカルのMesh頂点をEnemyMeshCutterローカル空間へ変換し、
    /// Cut Range表示・判定に使用するBoundsを求める。
    /// </summary>
    private bool TryCalculateBoundsInCutRangeSpace(
        Mesh mesh,
        Transform sourceTransform,
        out Bounds bounds)
    {
        bounds = default;

        if (mesh == null ||
            sourceTransform == null ||
            !mesh.isReadable ||
            mesh.vertexCount == 0)
        {
            return false;
        }

        Matrix4x4 sourceToCutRangeSpace =
            transform.worldToLocalMatrix
            * sourceTransform.localToWorldMatrix;

        return ChainsawMeshCutProcessor.TryCalculateMeshBoundsInSpace(
            mesh,
            sourceToCutRangeSpace,
            out bounds);
    }
}
