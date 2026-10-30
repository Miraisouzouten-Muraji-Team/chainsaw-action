using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Enemy死亡時に切断対象Meshを取得し、
/// チェンソー軌跡で分割したConnected Pieceを物理切断片として表示する。
/// </summary>
/// <remarks>
/// SkinnedMeshRendererの場合は死亡時の現在姿勢をBakeしてから切断する。
/// MeshFilter + MeshRendererの場合はMeshFilterのMeshを切断する。
///
/// HP計算、死亡判定、攻撃受信、チェンソー軌跡記録、
/// Triangle分割アルゴリズム、Connected Componentによる追加分離、
/// 切断片への飛散速度・方向付与は担当しない。
/// </remarks>
public sealed class EnemyMeshCutter : MonoBehaviour
{
    private static readonly ProfilerMarker tryCutProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Normal.Cutter.Total");







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

    [Header("分割片物理")]

    [Tooltip(
        "Mesh.boundsから求めた疑似体積へ掛ける密度係数。" +
        "疑似体積 × Density ScaleをMassの基準値として使用する。")]
    [SerializeField]
    [Min(0f)]
    private float densityScale = 1f;

    [Tooltip("疑似体積から求めたMassの下限。")]
    [SerializeField]
    [Min(0.0001f)]
    private float minimumPieceMass = 0.1f;

    [Tooltip("疑似体積から求めたMassの上限。")]
    [SerializeField]
    [Min(0.0001f)]
    private float maximumPieceMass = 10f;

    [Header("分割片事前生成")]

    [Tooltip(
        "通常攻撃死亡用Piece GameObjectを死亡前に準備する個数。" +
        "Connected Componentにより最終Piece数はモデルと切断位置で変化する。" +
        "不足分だけ死亡時に従来方式で追加生成する。")]
    [SerializeField]
    [Min(0)]
    private int preGeneratedPieceCount;

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
    private bool showCutRangeGizmo = false;

    [Header("診断ログ")]

    [Tooltip(
        "Mesh分割死亡演出の入力軌跡、切断対象Mesh、Processor結果、最終Connected Piece生成までを段階的にConsoleへ出力する。" +
        "原因特定後は無効化してよい。")]
    [SerializeField]
    private bool enableDiagnosticLogs = false;

    private readonly ChainsawMeshCutProcessor cutProcessor =
        new ChainsawMeshCutProcessor();

    private readonly List<Mesh> runtimePieceMeshes =
        new List<Mesh>();

    private readonly List<GameObject> pieceObjects =
        new List<GameObject>();

    // 死亡フレームでGameObject生成とAddComponentを行わないための事前生成殻。
    // Mesh依存のTransform・Renderer・Collider・Rigidbody設定は死亡時まで確定しない。
    private readonly List<GameObject> preGeneratedPieceObjects =
        new List<GameObject>();

    private readonly EnemyPlayerReference playerReference =
        new EnemyPlayerReference();

    private readonly List<Collider> playerColliders =
        new List<Collider>();

    private bool hasCompletedCut;

    private bool ShouldRunDiagnostics
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return enableDiagnosticLogs;
#else
            return false;
#endif
        }
    }

#if UNITY_EDITOR
    // Scene Viewは再描画のたびにOnDrawGizmosSelectedを呼ぶため、
    // 同じ診断内容を毎回Consoleへ流さないよう段階ごとの最終メッセージを保持する。
    private Dictionary<string, string> lastGizmoDiagnosticMessages;
#endif

    private void Awake()
    {
        PreGeneratePieceObjects();
    }

    /// <summary>
    /// 確定済みチェンソー軌跡を使ってEnemy Meshの切断を試みる。
    /// </summary>
    /// <param name="trajectory">
    /// 死亡させたチェンソー攻撃が保持している確定済み軌跡。
    /// </param>
    /// <returns>
    /// 2個以上の物理切断片生成と表示切り替えまで成功した場合はtrue。
    /// </returns>
    public bool TryCut(ChainsawAttackTrajectory trajectory)
    {
        using (tryCutProfilerMarker.Auto())
        {
        if (ShouldRunDiagnostics)
        {
            LogDiagnostic(
                "Cutter-01 Request",
                trajectory == null
                    ? "TryCutを受信しました。trajectory=null"
                    : $"TryCutを受信しました。{BuildTrajectorySummary(trajectory)}");
        }

        if (hasCompletedCut)
        {
            Debug.LogWarning(
                $"{nameof(EnemyMeshCutter)}: "
                + "このEnemyは既にMesh分割済みです。",
                this);

            if (ShouldRunDiagnostics)
            {
                LogDiagnosticWarning(
                    "Cutter-01 Request",
                    "hasCompletedCut=trueのため再分割を中止しました。");
            }

            return false;
        }

        if (!ValidateCutRequest(
                trajectory,
                out string failureReason))
        {
            if (ShouldRunDiagnostics)
            {
                LogDiagnosticWarning(
                    "Cutter-01 Request",
                    $"入力・Inspector設定の検証で停止しました。{failureReason}");
            }

            LogCutFailure(failureReason);
            return false;
        }

        if (!TryGetCutSource(
                out Mesh sourceMesh,
                out Renderer sourceRenderer,
                out bool destroySourceMeshAfterCut,
                out failureReason))
        {
            if (ShouldRunDiagnostics)
            {
                LogDiagnosticWarning(
                    "Cutter-02 Source",
                    $"切断対象Meshの取得で停止しました。{failureReason}");
            }

            LogCutFailure(failureReason);
            return false;
        }

        try
        {
            if (ShouldRunDiagnostics)
            {
                LogDiagnostic(
                    "Cutter-02 Source",
                    $"切断対象を取得しました。{BuildMeshSummary(sourceMesh)} / " +
                    $"Renderer={sourceRenderer.name}, {BuildTransformSummary(sourceRenderer.transform)}, " +
                    $"CutRangeOwner={BuildTransformSummary(transform)}");
            }

            if (!ValidateSourceMeshMaterialLayout(
                    sourceMesh,
                    sourceRenderer,
                    out failureReason))
            {
                if (ShouldRunDiagnostics)
                {
                    LogDiagnosticWarning(
                        "Cutter-02 Source",
                        $"Mesh / Material構成の検証で停止しました。{failureReason}");
                }

                LogCutFailure(failureReason);
                return false;
            }

            ConvertTrajectoryToRendererLocalSpace(
                trajectory,
                sourceRenderer.transform,
                out List<Vector3> localTipPositions,
                out List<Vector3> localRootPositions);

            if (ShouldRunDiagnostics)
            {
                LogDiagnostic(
                    "Cutter-03 LocalTrajectory",
                    BuildLocalTrajectorySummary(
                        localTipPositions,
                        localRootPositions));
            }

            Matrix4x4 sourceToCutRangeSpace =
                transform.worldToLocalMatrix
                * sourceRenderer.transform.localToWorldMatrix;

            if (ShouldRunDiagnostics)
            {
                LogDiagnostic(
                    "Cutter-04 Processor",
                    "ChainsawMeshCutProcessor.TryCutを開始します。" +
                    $" CutDirectionSampleCount={cutDirectionSampleCount}," +
                    $" CutRangeAxis={cutRangeAxis}," +
                    $" CutRange={cutRangeMinimumNormalized:F3}-{cutRangeMaximumNormalized:F3}," +
                    $" CutSurfaceUvScale={cutSurfaceUvScale:F4}," +
                    $" SourceToCutRangeDeterminant={sourceToCutRangeSpace.determinant:F6}");
            }

            bool succeeded = cutProcessor.TryCut(
                sourceMesh,
                targetMeshFilter != null,
                localTipPositions,
                localRootPositions,
                cutDirectionSampleCount,
                sourceToCutRangeSpace,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                cutSurfaceUvScale,
                ShouldRunDiagnostics,
                out List<Mesh> finalPieceMeshes,
                out failureReason);

            if (!succeeded)
            {
                DestroyRuntimeMeshes(finalPieceMeshes);

                if (ShouldRunDiagnostics)
                {
                    LogDiagnosticWarning(
                        "Cutter-04 Processor",
                        "ChainsawMeshCutProcessor内で停止しました。" +
                        $" failureReason={failureReason}");
                }

                LogCutFailure(failureReason);
                return false;
            }

            if (finalPieceMeshes == null ||
                finalPieceMeshes.Count < 2)
            {
                DestroyRuntimeMeshes(finalPieceMeshes);

                failureReason =
                    "MeshPlaneSplitterの成功後に2個以上の最終Connected Pieceを取得できませんでした。";

                if (ShouldRunDiagnostics)
                {
                    LogDiagnosticWarning(
                        "Cutter-04 Processor",
                        failureReason);
                }

                LogCutFailure(failureReason);
                return false;
            }

            runtimePieceMeshes.AddRange(finalPieceMeshes);

            if (ShouldRunDiagnostics)
            {
                LogDiagnostic(
                    "Cutter-05 FinalPieces",
                    $"中間First / Second Meshを生成せず、Connected Component確定後の最終Pieceを{finalPieceMeshes.Count}個取得しました。");
            }

            Material[] pieceMaterials =
                BuildPieceMaterials(sourceRenderer);

            ResolvePlayerCollidersForPieceCollision();

            int pieceCount =
                finalPieceMeshes.Count;

            for (int pieceIndex = 0;
                 pieceIndex < pieceCount;
                 pieceIndex++)
            {
                Mesh pieceMesh =
                    finalPieceMeshes[pieceIndex];

                GameObject pieceObject =
                    GetOrCreatePieceObject(
                        pieceIndex);

                ConfigurePieceObject(
                    pieceObject,
                    $"Piece_{pieceIndex:D2}",
                    pieceMesh,
                    pieceMaterials,
                    sourceRenderer);

                pieceObjects.Add(
                    pieceObject);
            }

            if (ShouldRunDiagnostics)
            {
                LogDiagnostic(
                    "Cutter-06 PieceObjects",
                    $"切断片GameObjectを{pieceObjects.Count}個構成して有効化しました。" +
                    $" ParentRenderer={sourceRenderer.name}");
            }

            sourceRenderer.enabled = false;

            hasCompletedCut = true;

            if (ShouldRunDiagnostics)
            {
                LogDiagnostic(
                    "Cutter-07 Complete",
                    $"元Rendererを無効化し、{pieceObjects.Count}個の物理切断片生成を完了しました。" +
                    " 各切断片は個別のRigidbodyとCapsuleColliderを持ちます。");
            }

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
    }

    /// <summary>
    /// Mesh分割によって生成された全切断片を取得する。
    /// </summary>
    /// <param name="destination">
    /// 呼び出し側が所有する格納先。呼び出し時に内容をクリアしてから現在有効な切断片を追加する。
    /// </param>
    /// <returns>
    /// Mesh分割済みで、1つ以上の切断片が存在する場合はtrue。
    /// </returns>
    public bool TryGetCutPieceTransforms(
        List<Transform> destination)
    {
        if (destination == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyMeshCutter)}: "
                + "切断片の格納先Listがnullです。",
                this);

            return false;
        }

        destination.Clear();

        if (!hasCompletedCut)
        {
            return false;
        }

        for (int i = 0;
             i < pieceObjects.Count;
             i++)
        {
            GameObject pieceObject =
                pieceObjects[i];

            if (pieceObject != null)
            {
                destination.Add(
                    pieceObject.transform);
            }
        }

        return destination.Count > 0;
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

            if (ShouldRunDiagnostics)
            {
                LogDiagnostic(
                    "Scale-01 BeforeBake",
                    BuildSkinnedMeshScaleDiagnostic(
                        targetSkinnedMeshRenderer));
            }

            // Processorへ渡すチェンソー軌跡はRenderer Local空間なので、
            // Bake Meshも同じスケール感の座標へ揃える必要がある。
            // Unity 6ではuseScale=falseにするとRendererのTransform Scaleに合わせた
            // Meshが得られるため、軌跡と切断Meshの座標系を一致させる。
            // 切断片生成時はRenderer自身のScaleを再適用しない配置にして二重Scaleを防ぐ。
            targetSkinnedMeshRenderer.BakeMesh(
                bakedMesh,
                false);

            bakedMesh.RecalculateBounds();

            if (ShouldRunDiagnostics)
            {
                LogDiagnostic(
                    "Scale-02 AfterBake",
                    $"BakeMesh(false)直後。{BuildMeshSummary(bakedMesh)} / " +
                    $"RendererLocalScale={FormatVector3(targetSkinnedMeshRenderer.transform.localScale)}, " +
                    $"RendererLossyScale={FormatVector3(targetSkinnedMeshRenderer.transform.lossyScale)}, " +
                    $"RendererWorldBoundsCenter={FormatVector3(targetSkinnedMeshRenderer.bounds.center)}, " +
                    $"RendererWorldBoundsSize={FormatVector3(targetSkinnedMeshRenderer.bounds.size)}");
            }

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
    /// 死亡前に通常攻撃Piece用GameObjectと必要Componentだけを生成する。
    /// </summary>
    private void PreGeneratePieceObjects()
    {
        int resolvedCount =
            Mathf.Max(
                0,
                preGeneratedPieceCount);

        for (int pieceIndex = 0;
             pieceIndex < resolvedCount;
             pieceIndex++)
        {
            GameObject pieceObject =
                CreatePieceShell(
                    $"{name}_PreGeneratedCutPiece_{pieceIndex:D2}");

            preGeneratedPieceObjects.Add(
                pieceObject);
        }
    }

    /// <summary>
    /// 事前生成範囲内なら既存の殻を返し、不足時だけ死亡フレームで追加生成する。
    /// </summary>
    private GameObject GetOrCreatePieceObject(
        int pieceIndex)
    {
        if (pieceIndex < preGeneratedPieceObjects.Count)
        {
            GameObject preGeneratedPieceObject =
                preGeneratedPieceObjects[pieceIndex];

            if (preGeneratedPieceObject != null)
            {
                return preGeneratedPieceObject;
            }
        }

        return CreatePieceShell(
            $"{name}_FallbackCutPiece_{pieceIndex:D2}");
    }

    /// <summary>
    /// Meshを持たない通常攻撃Piece用の殻を作る。
    /// </summary>
    /// <remarks>
    /// GameObjectを先に非アクティブ化してからCollider/Rigidbodyを追加し、
    /// 使用前の殻がPhysicsシミュレーションへ参加しないようにする。
    /// </remarks>
    private GameObject CreatePieceShell(
        string objectName)
    {
        var pieceObject =
            new GameObject(
                objectName);

        pieceObject.SetActive(
            false);

        pieceObject.transform.SetParent(
            transform,
            false);

        MeshFilter meshFilter =
            pieceObject.AddComponent<MeshFilter>();

        meshFilter.sharedMesh =
            null;

        pieceObject.AddComponent<MeshRenderer>();
        pieceObject.AddComponent<CapsuleCollider>();
        pieceObject.AddComponent<Rigidbody>();

        return pieceObject;
    
    }

    /// <summary>
    /// 事前生成済み、または不足時に追加生成した殻へ死亡時固有のPiece設定を適用する。
    /// </summary>
    private void ConfigurePieceObject(
        GameObject pieceObject,
        string suffix,
        Mesh mesh,
        Material[] materials,
        Renderer sourceRenderer)
    {
        pieceObject.name =
            $"{sourceRenderer.name}_Cut_{suffix}";

        pieceObject.layer =
            sourceRenderer.gameObject.layer;

        Transform pieceTransform =
            pieceObject.transform;

        if (sourceRenderer is SkinnedMeshRenderer)
        {
            Transform sourceTransform =
                sourceRenderer.transform;

            Transform sourceParent =
                sourceTransform.parent;

            // BakeMesh(false)はRendererのScaleに合わせた頂点を生成するため、
            // 元Rendererの子にするとRenderer Scaleがもう一度適用されて巨大化する。
            // 同じ親へ配置し、元Rendererの位置・回転だけを引き継ぐ。
            pieceTransform.SetParent(
                sourceParent,
                false);

            if (sourceParent != null)
            {
                pieceTransform.localPosition =
                    sourceTransform.localPosition;

                pieceTransform.localRotation =
                    sourceTransform.localRotation;
            }
            else
            {
                pieceTransform.SetPositionAndRotation(
                    sourceTransform.position,
                    sourceTransform.rotation);
            }

            pieceTransform.localScale =
                Vector3.one;
        }
        else
        {
            // MeshFilter経路ではMeshへRenderer ScaleをBakeしていないため、
            // 従来どおり元Rendererの子として同じTransformを継承する。
            pieceTransform.SetParent(
                sourceRenderer.transform,
                false);

            pieceTransform.localPosition =
                Vector3.zero;

            pieceTransform.localRotation =
                Quaternion.identity;

            pieceTransform.localScale =
                Vector3.one;
        }

        MeshFilter meshFilter =
            pieceObject.GetComponent<MeshFilter>();

        meshFilter.sharedMesh =
            mesh;

        MeshRenderer meshRenderer =
            pieceObject.GetComponent<MeshRenderer>();

        CopyRendererSettings(
            sourceRenderer,
            meshRenderer);

        meshRenderer.sharedMaterials =
            materials;

        Bounds meshBounds =
            mesh.bounds;

        CapsuleCollider pieceCollider =
            pieceObject.GetComponent<CapsuleCollider>();

        // Mesh.boundsとColliderは同じローカル空間を使う。
        // 分割片ごとに形状が異なるため、最長軸をカプセルの長辺へ合わせて
        // BoxColliderの平らな底面で直立し続けやすい状態を避ける。
        ConfigureCapsuleColliderFromBounds(
            pieceCollider,
            meshBounds);

        pieceCollider.isTrigger =
            false;

        Rigidbody pieceRigidbody =
            pieceObject.GetComponent<Rigidbody>();

        // 分割片の移動はTransformで上書きせず、
        // 重力とCollider接触を含めてUnity Physicsへ任せる。
        pieceRigidbody.useGravity =
            true;

        pieceRigidbody.isKinematic =
            false;

        // 疑似体積は分割片生成時に1回だけ計算する。
        // Massを慣性テンソル再計算より先に設定することで、
        // サイズとMassの両方を反映した回転しにくさをUnity Physicsへ引き継ぐ。
        pieceRigidbody.mass =
            CalculatePieceMass(
                meshBounds);

        // 分割後Pieceはゲームプレイ上の奥行き移動だけを固定する。
        // 回転は制限せず、床・壁との衝突で倒れる、傾く、転がる挙動をUnity Physicsへ任せる。
        // EnemyCutPieceBurstが与えるZ軸Torqueも、この自然な物理回転へ追加される。
        pieceRigidbody.constraints =
            RigidbodyConstraints.FreezePositionZ;

        // 以前は回転Constraintを慣性空間へ合わせるため慣性テンソルを手動固定していた。
        // 回転をPhysicsへ解放したため、Collider形状に基づくUnityの自動計算へ戻す。
        pieceRigidbody.automaticInertiaTensor =
            true;

        pieceRigidbody.ResetInertiaTensor();

        // 事前生成時にはPlayer Colliderが変化する可能性があるためIgnoreCollisionを固定せず、
        // Mesh・Collider・Rigidbody・Transform確定後の有効化時に毎回適用する。
        pieceObject.SetActive(
            true);

        IgnorePlayerCollisions(
            pieceCollider);

        if (ShouldRunDiagnostics)
        {
            LogDiagnostic(
                "Scale-03 PieceCreated",
                $"Piece='{pieceObject.name}', " +
                $"MeshBoundsCenter={FormatVector3(mesh.bounds.center)}, " +
                $"MeshBoundsSize={FormatVector3(mesh.bounds.size)}, " +
                $"PseudoVolume={(meshBounds.size.x * meshBounds.size.y * meshBounds.size.z):F6}, " +
                $"Mass={pieceRigidbody.mass:F6}, " +
                $"LocalScale={FormatVector3(pieceTransform.localScale)}, " +
                $"LossyScale={FormatVector3(pieceTransform.lossyScale)}, " +
                $"Parent='{(pieceTransform.parent != null ? pieceTransform.parent.name : "null")}', " +
                $"ParentLossyScale={FormatVector3(pieceTransform.parent != null ? pieceTransform.parent.lossyScale : Vector3.one)}, " +
                $"SourceRendererLocalScale={FormatVector3(sourceRenderer.transform.localScale)}, " +
                $"SourceRendererLossyScale={FormatVector3(sourceRenderer.transform.lossyScale)}, " +
                $"PieceWorldBoundsCenter={FormatVector3(meshRenderer.bounds.center)}, " +
                $"PieceWorldBoundsSize={FormatVector3(meshRenderer.bounds.size)}, " +
                $"SourceWorldBoundsCenter={FormatVector3(sourceRenderer.bounds.center)}, " +
                $"SourceWorldBoundsSize={FormatVector3(sourceRenderer.bounds.size)}");
        }
    
    }

    /// <summary>
    /// Player Tagから解決したPlayer階層のColliderを、今回生成する全Piece用に保持する。
    /// </summary>
    private void ResolvePlayerCollidersForPieceCollision()
    {
        playerColliders.Clear();

        if (playerReference.TryGetPlayerColliders(
                playerColliders))
        {
            return;
        }

        Debug.LogWarning(
            $"{nameof(EnemyMeshCutter)}: " +
            "Player TagからPlayer Colliderを取得できませんでした。" +
            "Piece生成は継続しますが、Playerとの衝突無効化は適用されません。",
            this);
    
    }

    /// <summary>
    /// Piece ColliderとPlayer階層の各Colliderの組み合わせだけ物理衝突を無効化する。
    /// </summary>
    private void IgnorePlayerCollisions(
        Collider pieceCollider)
    {
        for (int colliderIndex = 0;
             colliderIndex < playerColliders.Count;
             colliderIndex++)
        {
            Collider playerCollider =
                playerColliders[colliderIndex];

            if (playerCollider == null)
            {
                continue;
            }

            Physics.IgnoreCollision(
                pieceCollider,
                playerCollider,
                true);
        }
    
    }

    /// <summary>
    /// 分割MeshのローカルBoundsを疑似体積として扱い、切断片のMassを決定する。
    /// </summary>
    /// <remarks>
    /// 正確なMesh体積計算は行わず、Boundsの各辺長の積だけを使用する。
    /// この値は切断片生成時に1回だけ計算し、毎フレーム更新しない。
    /// </remarks>
    private float CalculatePieceMass(
        Bounds meshBounds)
    {
        Vector3 boundsSize =
            meshBounds.size;

        float approximateVolume =
            boundsSize.x *
            boundsSize.y *
            boundsSize.z;

        float resolvedMinimumMass =
            Mathf.Max(
                0.0001f,
                minimumPieceMass);

        float resolvedMaximumMass =
            Mathf.Max(
                resolvedMinimumMass,
                maximumPieceMass);

        float calculatedMass =
            approximateVolume *
            Mathf.Max(
                0f,
                densityScale);

        return Mathf.Clamp(
            calculatedMass,
            resolvedMinimumMass,
            resolvedMaximumMass);
    }

    /// <summary>
    /// 分割メッシュのローカルBoundsを単一のCapsuleColliderで近似する。
    /// </summary>
    /// <remarks>
    /// 特定モデルのローカル軸を前提にせず、Boundsの最長軸をカプセルの長辺にする。
    /// これにより、元Rendererの回転を引き継ぐ分割片でもColliderの向きを合わせやすくする。
    /// </remarks>
    private static void ConfigureCapsuleColliderFromBounds(
        CapsuleCollider capsuleCollider,
        Bounds meshBounds)
    {
        Vector3 size =
            meshBounds.size;

        int direction =
            GetLongestBoundsAxis(size);

        float height;
        float transverseSizeA;
        float transverseSizeB;

        switch (direction)
        {
            case 0:
                height = size.x;
                transverseSizeA = size.y;
                transverseSizeB = size.z;
                break;

            case 1:
                height = size.y;
                transverseSizeA = size.x;
                transverseSizeB = size.z;
                break;

            default:
                height = size.z;
                transverseSizeA = size.x;
                transverseSizeB = size.y;
                break;
        }

        float radius =
            Mathf.Max(transverseSizeA, transverseSizeB) * 0.5f;

        capsuleCollider.center =
            meshBounds.center;

        capsuleCollider.direction =
            direction;

        capsuleCollider.radius =
            radius;

        // Capsuleの全高が直径未満にならないよう保証する。
        capsuleCollider.height =
            Mathf.Max(height, radius * 2f);
    }

    private static int GetLongestBoundsAxis(
        Vector3 size)
    {
        if (size.x >= size.y &&
            size.x >= size.z)
        {
            return 0;
        }

        if (size.y >= size.z)
        {
            return 1;
        }

        return 2;
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

    private static string BuildSkinnedMeshScaleDiagnostic(
        SkinnedMeshRenderer renderer)
    {
        if (renderer == null)
        {
            return "SkinnedMeshRenderer=null";
        }

        Transform rendererTransform =
            renderer.transform;

        Transform parentTransform =
            rendererTransform.parent;

        Mesh sharedMesh =
            renderer.sharedMesh;

        string sharedMeshSummary =
            sharedMesh != null
                ? BuildMeshSummary(sharedMesh)
                : "SharedMesh=null";

        string parentSummary =
            parentTransform != null
                ? $"Parent='{parentTransform.name}', " +
                  $"ParentLocalScale={FormatVector3(parentTransform.localScale)}, " +
                  $"ParentLossyScale={FormatVector3(parentTransform.lossyScale)}"
                : "Parent=null";

        return
            $"Renderer='{renderer.name}', " +
            $"RendererLocalScale={FormatVector3(rendererTransform.localScale)}, " +
            $"RendererLossyScale={FormatVector3(rendererTransform.lossyScale)}, " +
            $"RendererWorldBoundsCenter={FormatVector3(renderer.bounds.center)}, " +
            $"RendererWorldBoundsSize={FormatVector3(renderer.bounds.size)}, " +
            $"RendererLocalBoundsCenter={FormatVector3(renderer.localBounds.center)}, " +
            $"RendererLocalBoundsSize={FormatVector3(renderer.localBounds.size)}, " +
            $"{parentSummary}, {sharedMeshSummary}";
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
        if (!ShouldRunDiagnostics)
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
        if (!ShouldRunDiagnostics)
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
        DestroyRuntimeMeshes(
            runtimePieceMeshes);

        runtimePieceMeshes.Clear();
        pieceObjects.Clear();
        preGeneratedPieceObjects.Clear();
    }

    private static void DestroyRuntimeMeshes(
        IReadOnlyList<Mesh> meshes)
    {
        if (meshes == null)
        {
            return;
        }

        for (int i = 0;
             i < meshes.Count;
             i++)
        {
            DestroyRuntimeMesh(
                meshes[i]);
        }
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
        preGeneratedPieceCount =
            Mathf.Max(
                0,
                preGeneratedPieceCount);

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

        densityScale =
            Mathf.Max(
                0f,
                densityScale);

        minimumPieceMass =
            Mathf.Max(
                0.0001f,
                minimumPieceMass);

        maximumPieceMass =
            Mathf.Max(
                minimumPieceMass,
                maximumPieceMass);
    }
#endif

    private void OnDrawGizmosSelected()
    {
        // Scene View再描画ごとのBakeMeshを避けるため、表示OFFなら最初に終了する。
        if (!showCutRangeGizmo)
        {
            return;
        }

#if UNITY_EDITOR
        if (ShouldRunDiagnostics)
        {
            LogGizmoDiagnostic(
                "Gizmo-01 Enter",
                $"OnDrawGizmosSelectedへ到達しました。" +
                $" ShowCutRangeGizmo={showCutRangeGizmo}," +
                $" Enabled={enabled}, ActiveInHierarchy={gameObject.activeInHierarchy}," +
                $" Owner={BuildTransformSummary(transform)}");
        }
#endif

        if (!TryGetPreviewBounds(
                out Bounds sourceBounds,
                out string previewFailureReason))
        {
#if UNITY_EDITOR
            if (ShouldRunDiagnostics)
            {
                LogGizmoDiagnosticWarning(
                    "Gizmo-02 PreviewBounds",
                    "切断対象MeshのPreview Bounds取得で停止しました。 " +
                    previewFailureReason);
            }
#endif
            return;
        }

#if UNITY_EDITOR
        if (ShouldRunDiagnostics)
        {
            LogGizmoDiagnostic(
                "Gizmo-02 PreviewBounds",
                "Preview Bounds取得に成功しました。 " +
                $"Center={FormatVector3(sourceBounds.center)}, " +
                $"Size={FormatVector3(sourceBounds.size)}, " +
                $"Min={FormatVector3(sourceBounds.min)}, " +
                $"Max={FormatVector3(sourceBounds.max)}");
        }
#endif

        if (!ChainsawMeshCutProcessor.TryCalculateCutRangeBounds(
                sourceBounds,
                cutRangeAxis,
                cutRangeMinimumNormalized,
                cutRangeMaximumNormalized,
                out Bounds cutRangeBounds))
        {
#if UNITY_EDITOR
            if (ShouldRunDiagnostics)
            {
                LogGizmoDiagnosticWarning(
                    "Gizmo-03 CutRangeBounds",
                    BuildCutRangeBoundsFailureReason(
                        sourceBounds));
            }
#endif
            return;
        }

#if UNITY_EDITOR
        Vector3 cutRangeWorldCenter =
            transform.TransformPoint(
                cutRangeBounds.center);

        if (ShouldRunDiagnostics)
        {
            LogGizmoDiagnostic(
                "Gizmo-03 CutRangeBounds",
                "Cut Range Bounds計算に成功しました。 " +
                $"Axis={cutRangeAxis}, " +
                $"Normalized={cutRangeMinimumNormalized:F4}-{cutRangeMaximumNormalized:F4}, " +
                $"LocalCenter={FormatVector3(cutRangeBounds.center)}, " +
                $"LocalSize={FormatVector3(cutRangeBounds.size)}, " +
                $"WorldCenter={FormatVector3(cutRangeWorldCenter)}");
        }
#endif

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

#if UNITY_EDITOR
        if (ShouldRunDiagnostics)
        {
            LogGizmoDiagnostic(
                "Gizmo-04 Draw",
                "Gizmos.DrawCube / DrawWireCubeまで到達しました。 " +
                "このログが出ているのにScene Viewで見えない場合は、" +
                "コード内の早期returnではなくScene View側のGizmos表示、" +
                "描画位置・サイズ、または表示状態を確認してください。");
        }
#endif
    }

    /// <summary>
    /// Sceneビューで最終切断境界の許可範囲を表示するため、
    /// 切断対象Meshの現在形状をEnemyMeshCutterローカル空間のBoundsへ変換する。
    /// </summary>
    private bool TryGetPreviewBounds(
        out Bounds sourceBounds,
        out string failureReason)
    {
        if (targetMeshFilter != null)
        {
            if (targetMeshFilter.sharedMesh == null)
            {
                sourceBounds = default;
                failureReason =
                    "Target Mesh Filterは設定されていますがsharedMesh=nullです。";
                return false;
            }

#if UNITY_EDITOR
            if (ShouldRunDiagnostics)
            {
                LogGizmoDiagnostic(
                    "Gizmo-02A Source",
                    "MeshFilter経路を使用します。 " +
                    $"{BuildMeshSummary(targetMeshFilter.sharedMesh)} / " +
                    $"{BuildTransformSummary(targetMeshFilter.transform)}");
            }
#endif

            return TryCalculateBoundsInCutRangeSpace(
                targetMeshFilter.sharedMesh,
                targetMeshFilter.transform,
                out sourceBounds,
                out failureReason);
        }

        if (targetSkinnedMeshRenderer != null)
        {
            if (targetSkinnedMeshRenderer.sharedMesh == null)
            {
                sourceBounds = default;
                failureReason =
                    "Target Skinned Mesh Rendererは設定されていますがsharedMesh=nullです。";
                return false;
            }

#if UNITY_EDITOR
            if (ShouldRunDiagnostics)
            {
                LogGizmoDiagnostic(
                    "Gizmo-02A Source",
                    "SkinnedMeshRenderer経路を使用します。 " +
                    $"Renderer='{targetSkinnedMeshRenderer.name}', " +
                    $"SharedMesh={BuildMeshSummary(targetSkinnedMeshRenderer.sharedMesh)} / " +
                    $"{BuildTransformSummary(targetSkinnedMeshRenderer.transform)}");
            }
#endif

            var previewMesh = new Mesh
            {
                name =
                    $"{targetSkinnedMeshRenderer.name}_CutRangePreview",
                hideFlags = HideFlags.HideAndDontSave
            };

            try
            {
                // Runtime切断と同じScale補償条件でBakeする。
                // 切断片生成時とPreviewで座標系を一致させたうえで、
                // EnemyMeshCutterローカル空間へ変換してBoundsを求める。
                targetSkinnedMeshRenderer.BakeMesh(
                    previewMesh,
                    true);

                previewMesh.RecalculateBounds();

#if UNITY_EDITOR
                if (ShouldRunDiagnostics)
                {
                    LogGizmoDiagnostic(
                        "Gizmo-02B BakeMesh",
                        "BakeMesh(true)に成功しました。 " +
                        BuildMeshSummary(previewMesh));
                }
#endif

                bool succeeded =
                    TryCalculateBoundsInCutRangeSpace(
                        previewMesh,
                        targetSkinnedMeshRenderer.transform,
                        out sourceBounds,
                        out failureReason);

                return succeeded;
            }
            catch (System.Exception exception)
            {
                sourceBounds = default;
                failureReason =
                    "SkinnedMeshRenderer.BakeMeshまたはPreview Bounds計算中に例外が発生しました。 " +
                    $"{exception.GetType().Name}: {exception.Message}";

#if UNITY_EDITOR
                if (ShouldRunDiagnostics)
                {
                    LogGizmoDiagnosticWarning(
                        "Gizmo-02B BakeMesh",
                        failureReason + "\n" + exception.StackTrace);
                }
#endif
                return false;
            }
            finally
            {
                DestroyRuntimeMesh(
                    previewMesh);
            }
        }

        sourceBounds = default;
        failureReason =
            "Target Mesh Filter / Target Skinned Mesh Rendererのどちらも設定されていません。";
        return false;
    }

    /// <summary>
    /// sourceTransformローカルのMesh頂点をEnemyMeshCutterローカル空間へ変換し、
    /// Cut Range表示・判定に使用するBoundsを求める。
    /// </summary>
    private bool TryCalculateBoundsInCutRangeSpace(
        Mesh mesh,
        Transform sourceTransform,
        out Bounds bounds,
        out string failureReason)
    {
        bounds = default;

        if (mesh == null)
        {
            failureReason =
                "Bounds計算対象Meshがnullです。";
            return false;
        }

        if (sourceTransform == null)
        {
            failureReason =
                "Bounds計算対象のsourceTransformがnullです。";
            return false;
        }

        if (!mesh.isReadable)
        {
            failureReason =
                $"Mesh '{mesh.name}' が読み取り不可です。 " +
                "頂点配列を取得できないためPreview Boundsを計算できません。";
            return false;
        }

        if (mesh.vertexCount == 0)
        {
            failureReason =
                $"Mesh '{mesh.name}' のvertexCount=0です。";
            return false;
        }

        Matrix4x4 sourceToCutRangeSpace =
            transform.worldToLocalMatrix
            * sourceTransform.localToWorldMatrix;

#if UNITY_EDITOR
        if (ShouldRunDiagnostics)
        {
            LogGizmoDiagnostic(
                "Gizmo-02C CoordinateSpace",
                "Mesh頂点をEnemyMeshCutterローカル空間へ変換します。 " +
                $"Mesh='{mesh.name}', Vertices={mesh.vertexCount}, " +
                $"MatrixDeterminant={sourceToCutRangeSpace.determinant:F8}, " +
                $"Source={BuildTransformSummary(sourceTransform)}, " +
                $"Target={BuildTransformSummary(transform)}");
        }
#endif

        if (!ChainsawMeshCutProcessor.TryCalculateMeshBoundsInSpace(
                mesh,
                sourceToCutRangeSpace,
                out bounds))
        {
            failureReason =
                "ChainsawMeshCutProcessor.TryCalculateMeshBoundsInSpace=falseでした。 " +
                "Meshには頂点が存在するため、変換後頂点にNaN / Infinityが含まれる可能性があります。 " +
                $"Mesh={BuildMeshSummary(mesh)}, " +
                $"MatrixDeterminant={sourceToCutRangeSpace.determinant:F8}";
            return false;
        }

        if (!IsFinite(bounds.center) ||
            !IsFinite(bounds.size))
        {
            failureReason =
                "計算後BoundsにNaN / Infinityが含まれています。 " +
                $"Center={FormatVector3(bounds.center)}, Size={FormatVector3(bounds.size)}";
            return false;
        }

        failureReason = null;
        return true;
    }

    private string BuildCutRangeBoundsFailureReason(
        Bounds sourceBounds)
    {
        if (cutRangeMinimumNormalized < 0.0f ||
            cutRangeMinimumNormalized > 1.0f ||
            cutRangeMaximumNormalized < 0.0f ||
            cutRangeMaximumNormalized > 1.0f)
        {
            return
                "Cut Range正規化値が0～1の範囲外です。 " +
                $"Minimum={cutRangeMinimumNormalized:F4}, " +
                $"Maximum={cutRangeMaximumNormalized:F4}";
        }

        if (cutRangeMinimumNormalized >=
            cutRangeMaximumNormalized)
        {
            return
                "Cut Range Minimum >= Maximumのため計算できません。 " +
                $"Minimum={cutRangeMinimumNormalized:F4}, " +
                $"Maximum={cutRangeMaximumNormalized:F4}";
        }

        float axisMinimum =
            GetAxisValueForDiagnostic(
                sourceBounds.min,
                cutRangeAxis);

        float axisMaximum =
            GetAxisValueForDiagnostic(
                sourceBounds.max,
                cutRangeAxis);

        if (axisMaximum <= axisMinimum)
        {
            return
                "Source BoundsのCut Range軸方向に有効な幅がありません。 " +
                $"Axis={cutRangeAxis}, " +
                $"AxisMinimum={axisMinimum:F6}, " +
                $"AxisMaximum={axisMaximum:F6}, " +
                $"BoundsCenter={FormatVector3(sourceBounds.center)}, " +
                $"BoundsSize={FormatVector3(sourceBounds.size)}";
        }

        return
            "TryCalculateCutRangeBounds=falseでしたが、入力値の事前診断では原因を特定できませんでした。 " +
            $"Axis={cutRangeAxis}, " +
            $"Minimum={cutRangeMinimumNormalized:F4}, " +
            $"Maximum={cutRangeMaximumNormalized:F4}, " +
            $"BoundsCenter={FormatVector3(sourceBounds.center)}, " +
            $"BoundsSize={FormatVector3(sourceBounds.size)}";
    }

    private static float GetAxisValueForDiagnostic(
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

#if UNITY_EDITOR
    private void LogGizmoDiagnostic(
        string stage,
        string message)
    {
        LogGizmoDiagnosticInternal(
            stage,
            message,
            false);
    }

    private void LogGizmoDiagnosticWarning(
        string stage,
        string message)
    {
        LogGizmoDiagnosticInternal(
            stage,
            message,
            true);
    }

    private void LogGizmoDiagnosticInternal(
        string stage,
        string message,
        bool isWarning)
    {
        if (!ShouldRunDiagnostics)
        {
            return;
        }

        if (lastGizmoDiagnosticMessages == null)
        {
            lastGizmoDiagnosticMessages =
                new Dictionary<string, string>();
        }

        string signature =
            $"{isWarning}:{message}";

        if (lastGizmoDiagnosticMessages.TryGetValue(
                stage,
                out string previousSignature) &&
            previousSignature == signature)
        {
            return;
        }

        lastGizmoDiagnosticMessages[stage] =
            signature;

        string formattedMessage =
            $"[MeshCutDiagnostic][{stage}] {message}";

        if (isWarning)
        {
            Debug.LogWarning(
                formattedMessage,
                this);
            return;
        }

        Debug.Log(
            formattedMessage,
            this);
    }
#endif

}
