using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// くい込み攻撃で死亡したEnemyを、保存済みのMeshを使った物理破片へ切り替える。
/// </summary>
public sealed class EnemyDiggingMeshDeathEffect : MonoBehaviour
{
    public const int BakedPieceCount = 8;

    private static readonly ProfilerMarker useBakedPiecesProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.Digging.Effect.UseBakedPieces");
    private static readonly ProfilerMarker createPieceObjectsProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.Digging.Effect.CreatePieceObjects");
    private static readonly ProfilerMarker configurePieceProfilerMarker =
        new ProfilerMarker("EnemyMeshCut.Digging.Effect.ConfigurePiece");

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

#if UNITY_EDITOR
    [Tooltip("Editorで各切断面へ生成するUVのスケール。")]
    [SerializeField]
    [Min(0.0001f)]
    private float cutSurfaceUvScale = 1f;
#endif

    [Header("事前生成Mesh")]
    [Tooltip("Editorで生成した食い込み死亡用MeshをPiece番号順に参照する。")]
    [SerializeField]
    private Mesh[] bakedPieceMeshes;

    [SerializeField, HideInInspector]
    private Vector3 bakedSkinnedSourceScale;

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

    [Header("分割片の疑似押し出し")]

    [Tooltip("プレイヤーColliderと分割片Colliderの表面間で、押し出しを開始できる距離（m）。接近検出Triggerにも反映する。")]
    [SerializeField, Min(0f)]
    private float playerPushReactionDistance = 0.08f;

    [Tooltip("プレイヤーの水平速度に掛ける押し出し速度の倍率。")]
    [SerializeField, Min(0f)]
    private float playerPushSpeedMultiplier = 0.85f;

    [Tooltip("押し出しによる分割片の目標水平速度の上限（m/s）。吹き飛ばし速度そのものは制限しない。")]
    [SerializeField, Min(0f)]
    private float playerPushMaximumSpeed = 4f;

    [Tooltip("1物理ステップで押し出しが加える速度変化の上限（m/s）。")]
    [SerializeField, Min(0f)]
    private float playerPushMaximumVelocityChangePerStep = 1.5f;

    [Tooltip("上下方向の重なりに必要な、両Colliderの低い方の高さに対する割合。大きいほどジャンプ中の反応を抑える。")]
    [SerializeField, Range(0f, 1f)]
    private float playerPushMinimumSideOverlapRatio = 0.25f;

    [Tooltip("疑似押し出し時に質量中心から上へずらして力を加える割合。0なら追加回転なし。")]
    [SerializeField, Range(0f, 0.5f)]
    private float playerPushRollingLeverArmRatio = 0.1f;

    [Tooltip("死亡直後の吹き飛ばしと干渉しないよう、押し出し開始を遅らせる秒数。")]
    [SerializeField, Min(0f)]
    private float playerPushInitialDelay = 0.15f;


    private readonly List<GameObject> pieceObjects =
        new List<GameObject>(BakedPieceCount);

    private readonly List<GameObject> preGeneratedPieceObjects =
        new List<GameObject>(BakedPieceCount);

    private readonly EnemyPlayerReference playerReference =
        new EnemyPlayerReference();

    // 同じSubMesh数のPiece間ではMaterial配列を共有し、Pieceごとの配列生成を避ける。
    // 対象Rendererは死亡時の1種類だけなので、分割開始時にキャッシュを破棄する。
    private readonly Dictionary<int, Material[]> pieceMaterialsBySubMeshCount =
        new Dictionary<int, Material[]>();

    private void Awake()
    {
        PreGeneratePieceObjects();
    }

#if UNITY_EDITOR
    public float EditorCutSurfaceUvScale => cutSurfaceUvScale;
    public Material EditorCutSurfaceMaterial => cutSurfaceMaterial;
    public Vector3 EditorGetBakedSkinnedPieceScale(Vector3 liveScale) => GetBakedSkinnedPieceScale(liveScale);

    public bool TryValidateEditorBakedPieces(out string reason) =>
        TryValidateBakedPieces(out _, out _, out reason);

    // Prefabに保存された姿勢を取得する。ownsMeshがtrueなら呼び出し側が破棄する。
    public bool TryGetEditorBakeSource(
        out Mesh mesh,
        out Renderer renderer,
        out Material[] materials,
        out bool ownsMesh,
        out string failureReason)
    {
        ownsMesh = false;
        if (!TryGetSourceLayout(out mesh, out renderer, out materials, out failureReason))
        {
            return false;
        }

        if (cutSurfaceUvScale <= 0f || !float.IsFinite(cutSurfaceUvScale))
        {
            failureReason = "Cut Surface UV Scaleには0より大きい有限値が必要です。";
            return false;
        }

        if (renderer is SkinnedMeshRenderer skinnedRenderer)
        {
            if (!IsValidBakeScale(renderer.transform.lossyScale))
            {
                failureReason = "Skinned Mesh Rendererのスケールを確認してください。";
                return false;
            }

            mesh = new Mesh { name = $"{renderer.name}_DiggingDeathBake" };
            ownsMesh = true;
            // BakeMesh(false)でPrefabのスケールを頂点へ反映する。
            skinnedRenderer.BakeMesh(mesh, false);
            mesh.RecalculateBounds();
        }
        else if (!mesh.isReadable)
        {
            failureReason = "分割対象MeshのModel Import SettingsでRead/Writeを有効にしてください。";
            return false;
        }

        return true;
    }

    // Playモード中だけの物理検証用。通常の死亡フローやプレイヤー攻撃には介入しない。
    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool isDebugSplitPressed =
            Keyboard.current != null &&
            Keyboard.current.yKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
        bool isDebugSplitPressed =
            Input.GetKeyDown(KeyCode.Y);
#else
        bool isDebugSplitPressed = false;
#endif

        if (!isDebugSplitPressed || HasCompletedSplit)
        {
            return;
        }

        RunDebugDiggingSplit();
    }

    /// <summary>
    /// くい込み死亡のMesh分割と専用飛散のみを手動実行する。
    /// 生成Pieceのステージ衝突を観察できるよう縮小・Enemy破棄は実行しない。
    /// </summary>
    private void RunDebugDiggingSplit()
    {
        if (!TryCreatePieces())
        {
            return;
        }

        var pieceTransforms = new List<Transform>();
        if (!TryGetPieceTransforms(pieceTransforms))
        {
            Debug.LogWarning(
                $"{nameof(EnemyDiggingMeshDeathEffect)}: " +
                "デバッグ分割後のPieceを取得できませんでした。",
                this);
            return;
        }

        // 生存中のAIや本体ColliderがPieceの物理挙動に介入しないよう、
        // 分割したPieceのColliderだけを残して本体を停止する。
        EnemyStateMachine stateMachine = GetComponent<EnemyStateMachine>();
        if (stateMachine != null)
        {
            stateMachine.enabled = false;
        }

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        foreach (Animator animator in animators)
        {
            if (animator != null)
            {
                animator.enabled = false;
            }
        }

        Collider[] allColliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider enemyCollider in allColliders)
        {
            if (enemyCollider == null)
            {
                continue;
            }

            bool isPieceCollider = false;
            for (int pieceIndex = 0; pieceIndex < pieceTransforms.Count; pieceIndex++)
            {
                Transform pieceTransform = pieceTransforms[pieceIndex];
                if (pieceTransform != null &&
                    enemyCollider.transform.IsChildOf(pieceTransform))
                {
                    isPieceCollider = true;
                    break;
                }
            }

            if (!isPieceCollider)
            {
                enemyCollider.enabled = false;
            }
        }

        Rigidbody enemyRigidbody = GetComponent<Rigidbody>();
        if (enemyRigidbody != null)
        {
            enemyRigidbody.isKinematic = true;
        }

        EnemyDiggingDeathBurst deathBurst = GetComponent<EnemyDiggingDeathBurst>();
        if (deathBurst != null)
        {
            deathBurst.BeginBurst(pieceTransforms);
        }
        else
        {
            Debug.LogWarning(
                $"{nameof(EnemyDiggingMeshDeathEffect)}: " +
                $"{nameof(EnemyDiggingDeathBurst)}が見つからないため、" +
                "分割のみ実行しました。",
                this);
        }
    }
#endif

    /// <summary>
    /// このEnemyについて、分割Piece生成と元Rendererの非表示まで完了しているかを示す。
    /// </summary>
    public bool HasCompletedSplit { get; private set; }

    /// <summary>
    /// 保存済みMeshを破片に設定し、元Rendererから表示を切り替える。
    /// </summary>
    public bool TryCreatePieces()
    {
        using var sample = useBakedPiecesProfilerMarker.Auto();
        if (HasCompletedSplit)
        {
            return false;
        }

        if (!TryValidateBakedPieces(out Renderer sourceRenderer, out Material[] sourceMaterials, out string reason))
        {
            LogFailure(reason);
            return false;
        }

        if (!TryCreatePieceObjects(sourceRenderer, sourceMaterials, out reason))
        {
            LogFailure(reason);
            return false;
        }

        sourceRenderer.enabled = false;
        HasCompletedSplit = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[EnemyDiggingBakedPieces] '{name}' (InstanceID {GetInstanceID()}) が保存済みMeshを{bakedPieceMeshes.Length}個使用しました。", this);
#endif
        return true;
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

    private bool TryGetSourceLayout(
        out Mesh sourceMesh, out Renderer sourceRenderer, out Material[] sourceMaterials, out string reason)
    {
        sourceMesh = null;
        sourceRenderer = null;
        sourceMaterials = null;
        bool skinned = targetSkinnedMeshRenderer != null;
        if (skinned == (targetMeshFilter != null))
        {
            reason = "Target Skinned Mesh RendererまたはTarget Mesh Filterのどちらか一方を設定してください。";
            return false;
        }

        if (skinned)
        {
            sourceRenderer = targetSkinnedMeshRenderer;
            sourceMesh = targetSkinnedMeshRenderer.sharedMesh;
        }
        else
        {
            sourceMesh = targetMeshFilter.sharedMesh;
            sourceRenderer = targetMeshFilter.GetComponent<MeshRenderer>();
        }

        if (sourceMesh == null || sourceRenderer == null)
        {
            reason = "分割対象に元MeshとRendererの両方が必要です。";
            return false;
        }

        sourceMaterials = sourceRenderer.sharedMaterials;
        if (sourceMaterials.Length == 0 || sourceMaterials.Length != sourceMesh.subMeshCount)
        {
            reason = "元RendererのMaterial数を元MeshのSubMesh数と一致させてください。";
            return false;
        }

        if (cutSurfaceMaterial == null)
        {
            reason = "Cut Surface Materialを設定してください。";
            return false;
        }

        reason = null;
        return true;
    }

    private bool TryValidateBakedPieces(out Renderer sourceRenderer, out Material[] sourceMaterials, out string reason)
    {
        if (!TryGetSourceLayout(out _, out sourceRenderer, out sourceMaterials, out reason))
        {
            return false;
        }

        int pieceCount = bakedPieceMeshes?.Length ?? 0;
        if (pieceCount != BakedPieceCount)
        {
            reason = $"Baked Piece Meshesには{BakedPieceCount}個の参照が必要です。現在は{pieceCount}個です。";
            return false;
        }

        if (sourceRenderer is SkinnedMeshRenderer && !IsValidBakeScale(bakedSkinnedSourceScale))
        {
            reason = "ベイク時のスケールが設定されていません。Digging Piece Mesh Bakerで再生成してください。";
            return false;
        }

        if (sourceRenderer is SkinnedMeshRenderer && !IsValidBakeScale(sourceRenderer.transform.lossyScale))
        {
            reason = "Skinned Mesh Rendererのスケールを確認してください。";
            return false;
        }

        for (int index = 0; index < pieceCount; index++)
        {
            Mesh mesh = bakedPieceMeshes[index];
            if (mesh == null || mesh.vertexCount < 3 || mesh.subMeshCount <= sourceMaterials.Length ||
                System.Array.IndexOf(bakedPieceMeshes, mesh, 0, index) >= 0)
            {
                reason = $"Baked Piece MeshesのPiece {index + 1:D2}に有効で重複のないMeshを設定してください。";
                return false;
            }

            bool hasCutSurface = false;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                uint indexCount = mesh.GetIndexCount(subMesh);
                if (indexCount == 0)
                {
                    continue;
                }
                if (indexCount % 3 != 0 || mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    reason = $"Piece {index + 1:D2}のSubMesh {subMesh}に三角形以外が含まれています。";
                    return false;
                }

                hasCutSurface |= subMesh >= sourceMaterials.Length;
            }

            if (!hasCutSurface)
            {
                reason = $"Piece {index + 1:D2}に切断面がありません。";
                return false;
            }

            Bounds bounds = mesh.bounds;
            if (!IsFiniteVector(bounds.center) || !IsFiniteVector(bounds.size) || bounds.size == Vector3.zero)
            {
                reason = $"Piece {index + 1:D2}のMesh Boundsが無効です。";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private Vector3 GetBakedSkinnedPieceScale(Vector3 liveScale) => new Vector3(
        liveScale.x / bakedSkinnedSourceScale.x,
        liveScale.y / bakedSkinnedSourceScale.y,
        liveScale.z / bakedSkinnedSourceScale.z);

    private static bool IsValidBakeScale(Vector3 scale) =>
        IsFiniteVector(scale) && scale.x != 0f && scale.y != 0f && scale.z != 0f;

    private static bool IsFiniteVector(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private bool TryCreatePieceObjects(Renderer sourceRenderer, Material[] sourceMaterials, out string reason)
    {
        using var sample = createPieceObjectsProfilerMarker.Auto();
        pieceMaterialsBySubMeshCount.Clear();
        playerReference.TryGetPlayerTransform(out Transform playerRoot);

        try
        {
            for (int index = 0; index < BakedPieceCount; index++)
            {
                Mesh mesh = bakedPieceMeshes[index];
                Material[] materials = BuildPieceMaterials(sourceMaterials, mesh.subMeshCount);
                GameObject piece = GetOrCreatePieceObject(index);
                pieceObjects.Add(piece);
                ConfigurePieceObject(piece, index, mesh, materials, sourceRenderer, playerRoot);
            }
        }
        catch (System.Exception exception)
        {
            CleanupFailedPieceCreation();
            reason = $"破片の設定中に例外が発生しました。{exception.GetType().Name}: {exception.Message}";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// 反復分割では切断のたびに切断面SubMeshが追加されるため、
    /// 元SubMesh以降の全Material Slotへ切断面Materialを割り当てる。
    /// </summary>
    private Material[] BuildPieceMaterials(
        IReadOnlyList<Material> sourceMaterials,
        int pieceSubMeshCount)
    {
        if (pieceMaterialsBySubMeshCount.TryGetValue(
                pieceSubMeshCount, out Material[] cachedMaterials))
        {
            return cachedMaterials;
        }

        var pieceMaterials = new Material[pieceSubMeshCount];

        for (int materialIndex = 0;
             materialIndex < pieceMaterials.Length;
             materialIndex++)
        {
            pieceMaterials[materialIndex] =
                materialIndex < sourceMaterials.Count
                    ? sourceMaterials[materialIndex]
                    : cutSurfaceMaterial;
        }

        pieceMaterialsBySubMeshCount.Add(pieceSubMeshCount, pieceMaterials);
        return pieceMaterials;
    }

    /// <summary>
    /// くい込み死亡Piece用GameObjectと必要Componentを死亡前に生成する。
    /// </summary>
    private void PreGeneratePieceObjects()
    {
        for (int pieceIndex = 0;
             pieceIndex < BakedPieceCount;
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
    /// 事前生成範囲内なら既存の殻を返し、殻が失われていた場合のみ死亡時に追加生成する。
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
        // 事前生成殻の段階でPlayer接触Triggerも準備する。
        EnemyCutPiecePlayerPushReaction.CreateForPiece(pieceObject);

        return pieceObject;

    }

    /// <summary>
    /// 事前生成済み、または不足時に追加生成した殻へ死亡時固有のPiece設定を適用する。
    /// </summary>
    private void ConfigurePieceObject(
        GameObject pieceObject,
        int pieceIndex,
        Mesh mesh,
        Material[] materials,
        Renderer sourceRenderer,
        Transform playerRoot)
    {
        using var configurePieceObjectProfileScope = configurePieceProfilerMarker.Auto();
        pieceObject.name =
            $"{sourceRenderer.name}_DiggingPiece_{pieceIndex:D2}";

        Transform pieceTransform =
            pieceObject.transform;

        if (sourceRenderer is SkinnedMeshRenderer)
        {
            Transform sourceTransform =
                sourceRenderer.transform;

            Transform sourceParent =
                sourceTransform.parent;

            // 頂点にはベイク時のスケールが含まれる。配置時はインスタンスとの差分だけを反映する。
            pieceTransform.SetParent(
                null,
                false);

            pieceTransform.SetPositionAndRotation(
                sourceTransform.position,
                sourceTransform.rotation);

            pieceTransform.localScale = GetBakedSkinnedPieceScale(sourceTransform.lossyScale);

            pieceTransform.SetParent(
                sourceParent,
                true);
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

        // 死亡処理で事前生成PieceのColliderが無効化されるため、分割後に衝突判定を復帰させる。
        pieceCollider.enabled =
            true;

        Rigidbody pieceRigidbody =
            pieceObject.GetComponent<Rigidbody>();

        pieceRigidbody.useGravity =
            true;

        pieceRigidbody.isKinematic =
            false;

        // 再利用したPieceに以前の押し出し・回転速度を残さない。
        // この後の死亡演出が今回の吹き飛ばし初速度を付与する。
        pieceRigidbody.linearVelocity = Vector3.zero;
        pieceRigidbody.angularVelocity = Vector3.zero;

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

        pieceObject.GetComponentInChildren<EnemyCutPiecePlayerPushReaction>(true)
            .Configure(
                pieceRigidbody,
                pieceCollider,
                playerRoot,
                playerPushReactionDistance,
                playerPushSpeedMultiplier,
                playerPushMaximumSpeed,
                playerPushMaximumVelocityChangePerStep,
                playerPushMinimumSideOverlapRatio,
                playerPushRollingLeverArmRatio,
                playerPushInitialDelay);

        // 本体とSensorのレイヤーを確定してから物理シミュレーションへ参加させる。
        pieceObject.SetActive(
            true);

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

    private void CleanupFailedPieceCreation()
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
                // Mesh参照を外し、再試行できるInactive状態へ戻す。
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
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
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

        playerPushReactionDistance = Mathf.Max(0f, playerPushReactionDistance);
        playerPushSpeedMultiplier = Mathf.Max(0f, playerPushSpeedMultiplier);
        playerPushMaximumSpeed = Mathf.Max(0f, playerPushMaximumSpeed);
        playerPushMaximumVelocityChangePerStep = Mathf.Max(0f, playerPushMaximumVelocityChangePerStep);
        playerPushMinimumSideOverlapRatio = Mathf.Clamp01(playerPushMinimumSideOverlapRatio);
        playerPushRollingLeverArmRatio = Mathf.Clamp(playerPushRollingLeverArmRatio, 0f, 0.5f);
        playerPushInitialDelay = Mathf.Max(0f, playerPushInitialDelay);
    }
#endif

    private void LogFailure(string reason)
    {
        Debug.LogError($"[EnemyDiggingBakedPieces] '{name}' の死亡破片を生成できませんでした。{reason}", this);
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
