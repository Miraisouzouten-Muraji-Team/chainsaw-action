using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 移動と食い込みの物理更新後に、既存の回避状態だけを読み取ります。
[DefaultExecutionOrder(2000)]
[DisallowMultipleComponent]
public sealed class PlayerEvadeAfterimage : MonoBehaviour
{
    private static readonly int BASE_COLOR = Shader.PropertyToID("_BaseColor");
    private static readonly int EMISSION_COLOR = Shader.PropertyToID("_EmissionColor");

    [Header("参照")]
    [Tooltip("既存の食い込み処理です。未設定なら自分または親から取得します。回避判定そのものは変更しません。")]
    [SerializeField] private SensorChainsawDigging chainsawDigging;
    [Tooltip("プレイヤーの見た目の親を指定します。例：main character。空欄の場合は描画対象の明示指定が必要です。")]
    [SerializeField] private Transform modelRoot;
    [Tooltip("通常は空欄にします。身体や武器など描画対象を個別指定したい場合だけ登録します。指定があると見た目の親からの自動収集を使いません。")]
    [SerializeField] private Renderer[] sourceRenderers;
    [Tooltip("Universal Render Pipeline/Litのマテリアルを指定します。実行時は専用コピーを使い、元のマテリアルは変更しません。")]
    [SerializeField] private Material afterimageMaterial;

    [Header("発生と消え方")]
    [Tooltip("回避中に残像を作る間隔（秒）です。固定更新より短い場合も、1回の固定更新で作るのは最大1体です。")]
    [SerializeField, Min(0.01f)] private float spawnInterval = 0.04f;
    [Tooltip("各残像が消えるまでの秒数です。回避が終わっても、既にある残像はこの時間で消えます。")]
    [SerializeField, Min(0.01f)] private float lifetime = 0.3f;
    [Tooltip("同時表示する残像の上限です。上限に達したら最も古い残像を再利用します。実行中に変更しても作成済みのプール数は変えません。")]
    [SerializeField, Range(1, 32)] private int maximumAfterimages = 10;
    [Tooltip("前回の生成位置から必要な最小移動距離です。停止中の残像の重なりを抑えます。0なら移動距離による制限をしません。")]
    [SerializeField, Min(0f)] private float minimumDistance = 0.05f;
    [Tooltip("最小移動距離に届かなくても、この角度以上向きが変われば残像を作ります。単位は度です。")]
    [SerializeField, Range(0.1f, 180f)] private float minimumAngle = 10f;
    [Tooltip("生成直後の残像の色です。Aが濃さです。初期値はオレンジです。")]
    [SerializeField] private Color freshColor = new Color(1f, 0.45f, 0f, 0.65f);
    [Tooltip("消える直前の色です。Aを0にすると透明になります。初期値は透明な黄色です。")]
    [SerializeField] private Color fadedColor = new Color(1f, 1f, 0f, 0f);
    [Tooltip("各残像を置くレイヤー番号です。カメラに映る、当たり判定に使わないレイヤーを指定してください。")]
    [SerializeField, Range(0, 31)] private int afterimageLayer;

    [Header("実行中の確認用")]
    [Tooltip("既存の回避状態を読み取った結果です。残像側で無敵時間は作りません。")]
    [SerializeField] private bool isEvading;
    [Tooltip("現在表示している残像の数です。")]
    [SerializeField] private int visibleAfterimages;

    private sealed class SourcePart
    {
        public Renderer Renderer;
        public SkinnedMeshRenderer Skinned;
        public MeshFilter Filter;
    }

    private sealed class GhostPart
    {
        public SourcePart Source;
        public Transform Transform;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public Mesh BakedMesh;
        public int MaterialCount;
    }

    private sealed class GhostFrame
    {
        public GameObject Root;
        public GhostPart[] Parts;
        public float Age;
        public float Lifetime;
        public Color FreshColor;
        public Color FadedColor;
        public bool Active;
    }

    private GhostFrame[] frames;
    private GameObject ghostRoot;
    private MaterialPropertyBlock propertyBlock;
    private Transform motionRoot;
    private Transform orientationRoot;
    private float spawnElapsed;
    private Vector3 lastSpawnPosition;
    private Quaternion lastSpawnRotation;
    private int nextFrame;
    private bool wasEvading;
    private bool initialized;
    private Material runtimeMaterial;

    private static bool IsUrpLit(Material material)
    {
        return material != null && material.shader != null &&
            material.shader.name == "Universal Render Pipeline/Lit";
    }

    // URPパッケージへのC#参照を追加せず、標準Litを透明な残像用に設定します。
    // 実行時の専用コピーだけを変更します。
    private static void ConfigureUrpLit(Material material)
    {
        if (!IsUrpLit(material)) return;
        SetFloatIfPresent(material, "_Surface", 1f);
        SetFloatIfPresent(material, "_Blend", 0f);
        SetFloatIfPresent(material, "_AlphaClip", 0f);
        SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_SrcBlendAlpha", (float)BlendMode.One);
        SetFloatIfPresent(material, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_ZWrite", 0f);
        SetFloatIfPresent(material, "_ZWriteControl", 2f); // ForceDisabled
        SetFloatIfPresent(material, "_ZTest", (float)CompareFunction.LessEqual);
        SetFloatIfPresent(material, "_Cull", (float)CullMode.Off);
        SetFloatIfPresent(material, "_BlendModePreserveSpecular", 0f);
        SetFloatIfPresent(material, "_Metallic", 0f);
        SetFloatIfPresent(material, "_Smoothness", 0f);
        SetFloatIfPresent(material, "_SpecularHighlights", 0f);
        SetFloatIfPresent(material, "_EnvironmentReflections", 0f);
        SetFloatIfPresent(material, "_ReceiveShadows", 0f);
        SetFloatIfPresent(material, "_QueueOffset", 0f);
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
        if (material.HasProperty("_EmissionMap")) material.SetTexture("_EmissionMap", null);
        // 拡散光を黒にしてEmissionのみで着色。暗い場所でも色付きシルエットを保つ。
        material.SetColor(BASE_COLOR, new Color(0f, 0f, 0f, 1f));
        material.SetColor(EMISSION_COLOR, Color.white);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_EMISSION");
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.DisableKeyword("_NORMALMAP");
        material.DisableKeyword("_METALLICSPECGLOSSMAP");
        material.DisableKeyword("_PARALLAXMAP");
        material.DisableKeyword("_DETAIL_MULX2");
        material.DisableKeyword("_DETAIL_SCALED");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("DepthNormals", false);
        material.SetShaderPassEnabled("DepthNormalsOnly", false);
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    }

    private static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    // 対象の描画部品を調べ、残像オブジェクトと焼き付け用Meshを先に確保します。
    private void Awake()
    {
        if (chainsawDigging == null) chainsawDigging = GetComponentInParent<SensorChainsawDigging>();
        if (chainsawDigging == null || !IsUrpLit(afterimageMaterial) ||
            !afterimageMaterial.shader.isSupported)
        {
            Debug.LogError("回避残像：食い込み処理と、Universal Render Pipeline/Litのマテリアルを設定してください。", this);
            enabled = false;
            return;
        }

        List<SourcePart> sources = CollectSources();
        if (sources.Count == 0)
        {
            Debug.LogError("回避残像：見た目の親、またはSkinnedMeshRenderer / MeshRendererを指定してください。", this);
            enabled = false;
            return;
        }

        motionRoot = chainsawDigging.transform;
        runtimeMaterial = new Material(afterimageMaterial)
        {
            name = afterimageMaterial.name + " (Afterimage Runtime)",
            hideFlags = HideFlags.DontSave
        };
        ConfigureUrpLit(runtimeMaterial);
        orientationRoot = modelRoot != null ? modelRoot : sources[0].Renderer.transform;
        propertyBlock = new MaterialPropertyBlock();
        // プレイヤーの子にしないことで、過去の位置に残した残像が追従しないようにする。
        ghostRoot = new GameObject(name + "_EvadeAfterimages");
        frames = new GhostFrame[Mathf.Clamp(maximumAfterimages, 1, 32)];
        for (int i = 0; i < frames.Length; i++) frames[i] = CreateFrame(sources, i);
        initialized = true;
    }

    // 明示指定があれば優先し、描画可能なMesh部品だけを重複なく収集します。
    private List<SourcePart> CollectSources()
    {
        Renderer[] renderers = sourceRenderers != null && sourceRenderers.Length > 0
            ? sourceRenderers
            : modelRoot != null ? modelRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
        List<SourcePart> sources = new List<SourcePart>();
        HashSet<Renderer> registered = new HashSet<Renderer>();
        foreach (Renderer source in renderers)
        {
            if (source == null || !registered.Add(source)) continue;
            SkinnedMeshRenderer skinned = source as SkinnedMeshRenderer;
            MeshFilter filter = source is MeshRenderer ? source.GetComponent<MeshFilter>() : null;
            if ((skinned == null || skinned.sharedMesh == null) && (filter == null || filter.sharedMesh == null)) continue;
            sources.Add(new SourcePart { Renderer = source, Skinned = skinned, Filter = filter });
        }
        return sources;
    }

    // 1体分の残像を作ります。アニメーション・Collider・攻撃スクリプトは追加しません。
    private GhostFrame CreateFrame(List<SourcePart> sources, int index)
    {
        GameObject root = new GameObject("Afterimage_" + index);
        root.transform.SetParent(ghostRoot.transform, false);
        root.layer = Mathf.Clamp(afterimageLayer, 0, 31);
        root.SetActive(false);
        GhostPart[] parts = new GhostPart[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            SourcePart source = sources[i];
            GameObject partObject = new GameObject(source.Renderer.name + "_Snapshot");
            partObject.layer = root.layer;
            partObject.transform.SetParent(root.transform, false);
            MeshFilter filter = partObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = partObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GhostPart part = new GhostPart
            {
                Source = source,
                Transform = partObject.transform,
                Filter = filter,
                Renderer = renderer
            };
            if (source.Skinned != null)
            {
                part.BakedMesh = new Mesh { name = "EvadeSnapshot_" + index + "_" + i };
                part.BakedMesh.MarkDynamic();
                filter.sharedMesh = part.BakedMesh;
            }
            Mesh mesh = source.Skinned != null ? source.Skinned.sharedMesh : source.Filter.sharedMesh;
            SetMaterials(part, mesh.subMeshCount);
            parts[i] = part;
        }
        return new GhostFrame { Root = root, Parts = parts };
    }

    // 元Meshのサブメッシュ数に合わせ、同じ残像マテリアルを全スロットへ割り当てます。
    private void SetMaterials(GhostPart part, int count)
    {
        count = Mathf.Max(1, count);
        if (part.MaterialCount == count) return;
        Material[] materials = new Material[count];
        for (int i = 0; i < count; i++) materials[i] = runtimeMaterial;
        part.Renderer.sharedMaterials = materials;
        part.MaterialCount = count;
    }

    // 残像を薄くし、回避中だけ一定間隔で新しいポーズを記録します。
    private void FixedUpdate()
    {
        if (!initialized) return;
        float deltaTime = Time.fixedDeltaTime;
        TickFrames(deltaTime);
        isEvading = chainsawDigging != null && chainsawDigging.IsEvading;
        if (!isEvading)
        {
            wasEvading = false;
            spawnElapsed = 0f;
            return;
        }

        spawnElapsed += deltaTime;
        bool started = !wasEvading;
        wasEvading = true;
        if (!started && spawnElapsed < Mathf.Max(0.01f, spawnInterval)) return;
        spawnElapsed = 0f;

        bool moved = minimumDistance <= 0f ||
            (motionRoot.position - lastSpawnPosition).sqrMagnitude >= minimumDistance * minimumDistance;
        Quaternion rotation = orientationRoot != null ? orientationRoot.rotation : motionRoot.rotation;
        bool rotated = Quaternion.Angle(rotation, lastSpawnRotation) >= Mathf.Max(0.1f, minimumAngle);
        if (started || moved || rotated) EmitFrame();
    }

    // 有効な残像を寿命に応じて変色・透過し、寿命切れをプールへ戻します。
    private void TickFrames(float deltaTime)
    {
        visibleAfterimages = 0;
        foreach (GhostFrame frame in frames)
        {
            if (!frame.Active) continue;
            frame.Age += deltaTime;
            if (frame.Age >= frame.Lifetime)
            {
                frame.Active = false;
                frame.Root.SetActive(false);
                continue;
            }
            ApplyColor(frame, Color.Lerp(frame.FreshColor, frame.FadedColor, frame.Age / frame.Lifetime));
            visibleAfterimages++;
        }
    }

    // 現在の変形済みポーズを保存し、各部品を生成時のワールド位置へ固定します。
    private void EmitFrame()
    {
        GhostFrame frame = frames[nextFrame];
        nextFrame = (nextFrame + 1) % frames.Length;
        bool wasActive = frame.Active;
        bool hasVisiblePart = false;
        foreach (GhostPart part in frame.Parts)
        {
            SourcePart source = part.Source;
            bool visible = source.Renderer != null && source.Renderer.enabled && source.Renderer.gameObject.activeInHierarchy;
            Mesh sourceMesh = source.Skinned != null ? source.Skinned.sharedMesh : source.Filter != null ? source.Filter.sharedMesh : null;
            if (!visible || sourceMesh == null)
            {
                part.Renderer.enabled = false;
                continue;
            }
            if (source.Skinned != null)
            {
                // BakeMeshはRendererのローカル座標で取得し、スケールはTransform側で1回だけ適用する。
                source.Skinned.BakeMesh(part.BakedMesh, false);
            }
            else part.Filter.sharedMesh = sourceMesh;

            Transform sourceTransform = source.Renderer.transform;
            part.Transform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
            part.Transform.localScale = sourceTransform.lossyScale;
            part.Renderer.sortingLayerID = source.Renderer.sortingLayerID;
            part.Renderer.sortingOrder = source.Renderer.sortingOrder;
            SetMaterials(part, sourceMesh.subMeshCount);
            part.Renderer.enabled = true;
            hasVisiblePart = true;
        }
        frame.Age = 0f;
        frame.Lifetime = Mathf.Max(0.01f, lifetime);
        frame.FreshColor = freshColor;
        frame.FadedColor = fadedColor;
        frame.Active = hasVisiblePart;
        ApplyColor(frame, frame.FreshColor);
        frame.Root.SetActive(hasVisiblePart);
        if (wasActive) visibleAfterimages--;
        if (hasVisiblePart) visibleAfterimages++;
        lastSpawnPosition = motionRoot.position;
        lastSpawnRotation = orientationRoot != null ? orientationRoot.rotation : motionRoot.rotation;
    }

    // 共有マテリアルを変更せず、各残像の色と透明度だけを個別に渡します。
    private void ApplyColor(GhostFrame frame, Color color)
    {
        propertyBlock.Clear();
        propertyBlock.SetColor(BASE_COLOR, new Color(0f, 0f, 0f, color.a));
        // AlphaはBaseColorから適用されるため、Emissionへ二重に掛けない。
        propertyBlock.SetColor(EMISSION_COLOR, new Color(color.r, color.g, color.b, 1f));
        foreach (GhostPart part in frame.Parts) part.Renderer.SetPropertyBlock(propertyBlock);
    }

    // リスポーンなどから呼ぶと、表示中の残像を即座に消して生成状態を初期化します。
    public void ClearAfterimages()
    {
        if (frames != null)
        {
            foreach (GhostFrame frame in frames)
            {
                frame.Active = false;
                if (frame.Root != null) frame.Root.SetActive(false);
            }
        }
        wasEvading = false;
        isEvading = false;
        visibleAfterimages = 0;
        spawnElapsed = 0f;
    }

    // コンポーネントやPlayerが無効化されたときは残像を残しません。
    private void OnDisable()
    {
        ClearAfterimages();
    }

    // 自分で確保したMeshと残像オブジェクトだけを破棄します。元モデルのMeshは破棄しません。
    private void OnDestroy()
    {
        if (frames != null)
        {
            foreach (GhostFrame frame in frames)
                foreach (GhostPart part in frame.Parts)
                    if (part.BakedMesh != null) Destroy(part.BakedMesh);
        }
        if (ghostRoot != null) Destroy(ghostRoot);
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }
}
