using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// くい込み攻撃で死亡したEnemyのMeshを、指定した目標Piece数へ分割して物理破片を生成する。
/// </summary>
/// <remarks>
/// 分割回数と分割対象Pieceの選択はMeshTargetCountSplitterへ、
/// Triangle分割と切断面生成はMeshPlaneSplitterへ委譲する。
/// 死亡原因の判定、通常攻撃用Cut Range、チェンソー軌跡、飛散方向・強度の決定は担当しない。
/// </remarks>
public sealed class EnemyDiggingMeshDeathEffect : MonoBehaviour
{
    private static readonly ProfilerMarker createPiecesProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.Effect.Total");

        private static readonly ProfilerMarker getSourceProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.Effect.GetSourceAndBake");

        private static readonly ProfilerMarker createPieceObjectsProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.Effect.CreatePieceObjects");

        private static readonly ProfilerMarker createPieceShellProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.Effect.CreatePieceShell");

        private static readonly ProfilerMarker configurePieceProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.Effect.ConfigurePieceObject");

        private static readonly ProfilerMarker resolvePlayerCollidersProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.Effect.ResolvePlayerColliders");

        private static readonly ProfilerMarker ignorePlayerCollisionProfilerMarker =
            new ProfilerMarker("EnemyMeshCut.Digging.Effect.IgnorePlayerCollisions");

    private const int MIN_TARGET_PIECE_COUNT = 2;

    [Header("分割対象")]

    [Tooltip(
        "SkinnedMeshRendererを使用するEnemyの分割対象。" +
        "MeshFilterと同時には設定しない。")]
    [SerializeField]
    private SkinnedMeshRenderer targetSkinnedMeshRenderer;

    [Tooltip(
        "MeshFilter + MeshRendererを使用するEnemyの分割対象。" +
        "同じGameObjectにMeshRendererが必要。" +
        "SkinnedMeshRendererと同時には設定しない。")]
    [SerializeField]
    private MeshFilter targetMeshFilter;

    [Tooltip("切断面へ設定するMaterial。")]
    [SerializeField]
    private Material cutSurfaceMaterial;

    [Header("目標Piece数")]

    [Tooltip(
        "MeshTargetCountSplitterへ渡す目標Piece数。" +
        "連結成分分離によって、この値を超える場合がある。")]
    [SerializeField]
    [Min(MIN_TARGET_PIECE_COUNT)]
    private int targetPieceCount = 8;

    [Tooltip("各切断面へ生成するUVのスケール。")]
    [SerializeField]
    [Min(0.0001f)]
    private float cutSurfaceUvScale = 1f;

    [Header("分割片物理")]

    [Tooltip(
        "Mesh.boundsから求めた疑似体積へ掛ける密度係数。" +
        "通常攻撃側EnemyMeshCutterと同じ計算方式を使用する。")]
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

    private readonly MeshTargetCountSplitter targetCountSplitter =
        new MeshTargetCountSplitter();

    private readonly List<Mesh> runtimePieceMeshes =
        new List<Mesh>();

    private readonly List<GameObject> pieceObjects =
        new List<GameObject>();

    // targetPieceCountを基準に死亡前へ生成コストを移すための事前生成殻。
    // 連結成分分離で不足した分だけ死亡時に追加生成する。
    private readonly List<GameObject> preGeneratedPieceObjects =
        new List<GameObject>();

    private readonly EnemyPlayerReference playerReference =
        new EnemyPlayerReference();

    private readonly List<Collider> playerColliders =
        new List<Collider>();

    private void Awake()
    {
        PreGeneratePieceObjects();
    }

    /// <summary>
    /// このEnemyについて、分割Piece生成と元Rendererの非表示まで完了しているかを示す。
    /// </summary>
    public bool HasCompletedSplit { get; private set; }

    /// <summary>
    /// 死亡時姿勢のMeshを取得し、指定した目標数へ分割して物理Pieceを生成する。
    /// </summary>
    /// <returns>
    /// 1つ以上のPiece生成と元Rendererの非表示まで完了した場合はtrue。
    /// </returns>
    public bool TryCreatePieces()
    {
        using (createPiecesProfilerMarker.Auto())
        {
        if (HasCompletedSplit)
        {
            Debug.LogWarning(
                $"{nameof(EnemyDiggingMeshDeathEffect)}: " +
                "このEnemyは既にくい込み死亡用Mesh分割を完了しています。",
                this);

            return false;
        }

        if (!ValidateRequest(
                out string failureReason))
        {
            LogFailure(failureReason);
            return false;
        }

        if (!TryGetSplitSource(
                out Mesh sourceMesh,
                out Renderer sourceRenderer,
                out bool destroySourceMeshAfterSplit,
                out failureReason))
        {
            LogFailure(failureReason);
            return false;
        }

        try
        {
            if (!ValidateSourceMeshMaterialLayout(
                    sourceMesh,
                    sourceRenderer,
                    out failureReason))
            {
                LogFailure(failureReason);
                return false;
            }

            bool splitSucceeded =
                targetCountSplitter.TrySplit(
                    sourceMesh,
                    targetPieceCount,
                    cutSurfaceUvScale,
                    out List<Mesh> splitPieceMeshes,
                    out failureReason);

            if (!splitSucceeded)
            {
                LogFailure(failureReason);
                return false;
            }

            ResolvePlayerCollidersForPieceCollision();

            if (!TryCreatePieceObjects(
                    splitPieceMeshes,
                    sourceRenderer,
                    out failureReason))
            {
                LogFailure(failureReason);
                return false;
            }

            // 破片の生成が完了する前に元Rendererを消すと、
            // 分割失敗時にEnemyの見た目だけが消えるため最後に切り替える。
            sourceRenderer.enabled = false;
            HasCompletedSplit = true;

            return true;
        }
        finally
        {
            if (destroySourceMeshAfterSplit)
            {
                DestroyRuntimeMesh(sourceMesh);
            }
        }
    
        }
    }

    /// <summary>
    /// 生成済みのくい込み死亡Pieceを取得する。
    /// </summary>
    /// <param name="destination">
    /// 呼び出し側が所有する格納先。呼び出し時に内容をクリアしてから有効なPieceを追加する。
    /// </param>
    public bool TryGetPieceTransforms(
        List<Transform> destination)
    {
        if (destination == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyDiggingMeshDeathEffect)}: " +
                "Pieceの格納先Listがnullです。",
                this);

            return false;
        }

        destination.Clear();

        if (!HasCompletedSplit)
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

    private bool ValidateRequest(
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
                "分割対象が設定されていません。" +
                "Target Skinned Mesh Rendererまたは" +
                "Target Mesh Filterのどちらかを設定してください。";

            return false;
        }

        if (hasSkinnedMeshTarget &&
            hasMeshFilterTarget)
        {
            failureReason =
                "Target Skinned Mesh Rendererと" +
                "Target Mesh Filterの両方が設定されています。" +
                "分割対象はどちらか一方だけ設定してください。";

            return false;
        }

        if (hasSkinnedMeshTarget)
        {
            if (targetSkinnedMeshRenderer.sharedMesh == null)
            {
                failureReason =
                    "Target Skinned Mesh RendererにsharedMeshがありません。";

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

            if (targetMeshFilter.GetComponent<MeshRenderer>() == null)
            {
                failureReason =
                    "Target Mesh Filterと同じGameObjectにMeshRendererがありません。";

                return false;
            }

            if (!targetMeshFilter.sharedMesh.isReadable)
            {
                failureReason =
                    "Target Mesh FilterのMeshが読み取り不可です。" +
                    "Mesh分割では頂点・Triangle情報を読み取るため、" +
                    "Model Import SettingsのRead/Write Enabledを有効にしてください。";

                return false;
            }
        }

        if (targetPieceCount < MIN_TARGET_PIECE_COUNT)
        {
            failureReason =
                $"Target Piece Countは{MIN_TARGET_PIECE_COUNT}以上にしてください。";

            return false;
        }

        if (cutSurfaceUvScale <= 0f)
        {
            failureReason =
                "Cut Surface Uv Scaleは0より大きい値にしてください。";

            return false;
        }

        if (cutSurfaceMaterial == null)
        {
            failureReason =
                "Cut Surface Materialが設定されていません。";

            return false;
        }

        failureReason = null;
        return true;
    }

    /// <summary>
    /// 現在の分割対象からMeshと表示元Rendererを取得する。
    /// SkinnedMeshRendererは死亡時姿勢をRuntime MeshへBakeする。
    /// </summary>
    private bool TryGetSplitSource(
        out Mesh sourceMesh,
        out Renderer sourceRenderer,
        out bool destroySourceMeshAfterSplit,
        out string failureReason)
    {
        using (getSourceProfilerMarker.Auto())
        {
        sourceMesh = null;
        sourceRenderer = null;
        destroySourceMeshAfterSplit = false;

        if (targetSkinnedMeshRenderer != null)
        {
            var bakedMesh = new Mesh
            {
                name =
                    $"{targetSkinnedMeshRenderer.name}_DiggingDeathBake"
            };

            // 既存EnemyMeshCutterと同じ座標系に揃える。
            // BakeMesh(false)でRenderer TransformのScaleを頂点側へ反映し、
            // Piece生成時はRenderer Scaleを再適用しないことで二重Scaleを避ける。
            targetSkinnedMeshRenderer.BakeMesh(
                bakedMesh,
                false);

            bakedMesh.RecalculateBounds();

            sourceMesh = bakedMesh;
            sourceRenderer = targetSkinnedMeshRenderer;
            destroySourceMeshAfterSplit = true;
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
                    "Target Mesh Filterと同じGameObjectにMeshRendererがありません。";

                return false;
            }

            sourceMesh =
                targetMeshFilter.sharedMesh;

            sourceRenderer =
                meshRenderer;

            // sharedMeshはProject AssetまたはUnity管理Meshなので、
            // このComponentから破棄しない。
            destroySourceMeshAfterSplit = false;
            failureReason = null;

            return true;
        }

        failureReason =
            "分割対象からMeshを取得できませんでした。";

        return false;
    
        }
    }

    private bool ValidateSourceMeshMaterialLayout(
        Mesh sourceMesh,
        Renderer sourceRenderer,
        out string failureReason)
    {
        if (sourceMesh == null)
        {
            failureReason =
                "分割対象Meshがnullです。";

            return false;
        }

        if (sourceRenderer == null)
        {
            failureReason =
                "分割対象Rendererがnullです。";

            return false;
        }

        Material[] sourceMaterials =
            sourceRenderer.sharedMaterials;

        if (sourceMaterials == null ||
            sourceMaterials.Length == 0)
        {
            failureReason =
                "分割対象RendererにMaterialがありません。";

            return false;
        }

        if (sourceMaterials.Length !=
            sourceMesh.subMeshCount)
        {
            failureReason =
                "元Material数とMeshのSubMesh数が一致していません。" +
                "分割後のMaterial対応を安全に維持できないため処理を中止します。";

            return false;
        }

        failureReason = null;
        return true;
    }

    private bool TryCreatePieceObjects(
        IReadOnlyList<Mesh> splitPieceMeshes,
        Renderer sourceRenderer,
        out string failureReason)
    {
        using (createPieceObjectsProfilerMarker.Auto())
        {
        if (splitPieceMeshes == null ||
            splitPieceMeshes.Count == 0)
        {
            failureReason =
                "MeshTargetCountSplitterから有効なPieceが返されませんでした。";

            return false;
        }

        Material[] sourceMaterials =
            sourceRenderer.sharedMaterials;

        try
        {
            for (int pieceIndex = 0;
                 pieceIndex < splitPieceMeshes.Count;
                 pieceIndex++)
            {
                Mesh pieceMesh =
                    splitPieceMeshes[pieceIndex];

                if (pieceMesh == null)
                {
                    failureReason =
                        $"分割結果のPiece {pieceIndex} がnullです。";

                    CleanupFailedPieceCreation(
                        splitPieceMeshes);

                    return false;
                }

                // MeshPlaneSplitterでもBoundsは構築されるが、
                // 物理設定がBoundsを直接使用するためPiece生成直前に明示的に更新する。
                pieceMesh.RecalculateBounds();

                Material[] pieceMaterials =
                    BuildPieceMaterials(
                        sourceMaterials,
                        pieceMesh.subMeshCount);

                GameObject pieceObject =
                    GetOrCreatePieceObject(
                        pieceIndex);

                ConfigurePieceObject(
                    pieceObject,
                    pieceIndex,
                    pieceMesh,
                    pieceMaterials,
                    sourceRenderer);

                runtimePieceMeshes.Add(
                    pieceMesh);

                pieceObjects.Add(
                    pieceObject);
            }
        }
        catch (System.Exception exception)
        {
            CleanupFailedPieceCreation(
                splitPieceMeshes);

            failureReason =
                "分割PieceのGameObject生成中に例外が発生しました。" +
                $" {exception.GetType().Name}: {exception.Message}";

            return false;
        }

        failureReason = null;
        return pieceObjects.Count > 0;
    
        }
    }

    /// <summary>
    /// 反復分割では切断のたびに切断面SubMeshが追加されるため、
    /// 元SubMesh以降の全Material Slotへ切断面Materialを割り当てる。
    /// </summary>
    private Material[] BuildPieceMaterials(
        IReadOnlyList<Material> sourceMaterials,
        int pieceSubMeshCount)
    {
        var pieceMaterials =
            new Material[pieceSubMeshCount];

        for (int materialIndex = 0;
             materialIndex < pieceMaterials.Length;
             materialIndex++)
        {
            pieceMaterials[materialIndex] =
                materialIndex < sourceMaterials.Count
                    ? sourceMaterials[materialIndex]
                    : cutSurfaceMaterial;
        }

        return pieceMaterials;
    }

    /// <summary>
    /// targetPieceCountを基準に、くい込み死亡Piece用GameObjectと必要Componentだけを死亡前に生成する。
    /// </summary>
    private void PreGeneratePieceObjects()
    {
        int resolvedCount =
            Mathf.Max(
                0,
                targetPieceCount);

        for (int pieceIndex = 0;
             pieceIndex < resolvedCount;
             pieceIndex++)
        {
            GameObject pieceObject =
                CreatePieceShell(
                    $"{name}_PreGeneratedDiggingPiece_{pieceIndex:D2}");

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
            $"{name}_FallbackDiggingPiece_{pieceIndex:D2}");
    }

    /// <summary>
    /// Meshを持たないくい込み死亡Piece用の殻を作る。
    /// </summary>
    /// <remarks>
    /// GameObjectを先に非アクティブ化してからCollider/Rigidbodyを追加し、
    /// 使用前の殻がPhysicsシミュレーションへ参加しないようにする。
    /// </remarks>
    private GameObject CreatePieceShell(
        string objectName)
    {
        using (createPieceShellProfilerMarker.Auto())
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
        pieceObject.AddComponent<BoxCollider>();
        pieceObject.AddComponent<Rigidbody>();

        return pieceObject;
    
        }
    }

    /// <summary>
    /// 事前生成済み、または不足時に追加生成した殻へ死亡時固有のPiece設定を適用する。
    /// </summary>
    private void ConfigurePieceObject(
        GameObject pieceObject,
        int pieceIndex,
        Mesh mesh,
        Material[] materials,
        Renderer sourceRenderer)
    {
        using (configurePieceProfilerMarker.Auto())
        {
        pieceObject.name =
            $"{sourceRenderer.name}_DiggingPiece_{pieceIndex:D2}";

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

        BoxCollider pieceCollider =
            pieceObject.GetComponent<BoxCollider>();

        // Mesh.boundsとBoxColliderは同じローカル空間を使う。
        // くい込み死亡Pieceは丸い端面で転がり続ける挙動を避けるため、
        // 分割MeshのBoundsへそのまま合わせた箱形状で衝突させる。
        pieceCollider.center =
            meshBounds.center;

        pieceCollider.size =
            meshBounds.size;

        pieceCollider.isTrigger =
            false;

        Rigidbody pieceRigidbody =
            pieceObject.GetComponent<Rigidbody>();

        pieceRigidbody.useGravity =
            true;

        pieceRigidbody.isKinematic =
            false;

        pieceRigidbody.mass =
            CalculatePieceMass(
                meshBounds);

        // 現在の通常攻撃Pieceと同じく、ゲームプレイ上の奥行き移動を固定し、
        // 画面に垂直な軸まわりの回転だけを許可する。
        pieceRigidbody.ResetInertiaTensor();

        Vector3 calculatedInertiaTensor =
            pieceRigidbody.inertiaTensor;

        pieceRigidbody.automaticInertiaTensor =
            false;

        pieceRigidbody.inertiaTensor =
            calculatedInertiaTensor;

        pieceRigidbody.inertiaTensorRotation =
            Quaternion.Inverse(
                pieceRigidbody.rotation);

        pieceRigidbody.constraints =
            RigidbodyConstraints.FreezePositionZ |
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationY;

        // 事前生成時にはPlayer Colliderが変化する可能性があるためIgnoreCollisionを固定せず、
        // Mesh・Collider・Rigidbody・Transform確定後の有効化時に毎回適用する。
        pieceObject.SetActive(
            true);

        IgnorePlayerCollisions(
            pieceCollider);
    
        }
    }

    /// <summary>
    /// Player Tagから解決したPlayer階層のColliderを、今回生成する全Piece用に保持する。
    /// </summary>
    private void ResolvePlayerCollidersForPieceCollision()
    {
        using (resolvePlayerCollidersProfilerMarker.Auto())
        {
        playerColliders.Clear();

        if (playerReference.TryGetPlayerColliders(
                playerColliders))
        {
            return;
        }

        Debug.LogWarning(
            $"{nameof(EnemyDiggingMeshDeathEffect)}: " +
            "Player TagからPlayer Colliderを取得できませんでした。" +
            "Piece生成は継続しますが、Playerとの衝突無効化は適用されません。",
            this);
    
        }
    }

    /// <summary>
    /// Piece ColliderとPlayer階層の各Colliderの組み合わせだけ物理衝突を無効化する。
    /// </summary>
    private void IgnorePlayerCollisions(
        Collider pieceCollider)
    {
        using (ignorePlayerCollisionProfilerMarker.Auto())
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
    }

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

    private void CleanupFailedPieceCreation(
        IReadOnlyList<Mesh> splitPieceMeshes)
    {
        for (int i = 0;
             i < pieceObjects.Count;
             i++)
        {
            GameObject pieceObject =
                pieceObjects[i];

            if (pieceObject == null)
            {
                continue;
            }

            if (preGeneratedPieceObjects.Contains(
                    pieceObject))
            {
                // 事前生成殻はEnemy初期化時の生成コストを再利用するため破棄せず戻す。
                // Runtime Mesh参照だけ外し、再試行時に上書き可能なInactive状態へ戻す。
                pieceObject.SetActive(
                    false);

                MeshFilter meshFilter =
                    pieceObject.GetComponent<MeshFilter>();

                if (meshFilter != null)
                {
                    meshFilter.sharedMesh =
                        null;
                }

                pieceObject.transform.SetParent(
                    transform,
                    false);
            }
            else
            {
                pieceObject.SetActive(
                    false);

                DestroyRuntimeObject(
                    pieceObject);
            }
        }

        pieceObjects.Clear();
        runtimePieceMeshes.Clear();

        DestroyRuntimeMeshes(
            splitPieceMeshes);
    }

    private void OnDestroy()
    {
        DestroyRuntimeMeshes(
            runtimePieceMeshes);

        runtimePieceMeshes.Clear();
        pieceObjects.Clear();
        preGeneratedPieceObjects.Clear();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        targetPieceCount =
            Mathf.Max(
                MIN_TARGET_PIECE_COUNT,
                targetPieceCount);

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

    private void LogFailure(
        string failureReason)
    {
        Debug.LogWarning(
            $"{nameof(EnemyDiggingMeshDeathEffect)}: " +
            "くい込み死亡Mesh分割に失敗しました。" +
            $" 理由: {failureReason}",
            this);
    }

    private static void DestroyRuntimeMeshes(
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
            DestroyRuntimeMesh(
                meshes[meshIndex]);
        }
    }

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

    private static void DestroyRuntimeObject(
        GameObject targetObject)
    {
        if (targetObject == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(targetObject);
        }
        else
        {
            DestroyImmediate(targetObject);
        }
    }
}
