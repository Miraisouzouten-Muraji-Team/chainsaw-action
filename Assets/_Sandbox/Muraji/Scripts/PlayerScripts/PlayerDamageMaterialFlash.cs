using System.Collections.Generic;
using UnityEngine;

// ダメージの発生元に依存せず、身体のマテリアルを一定時間だけ白黒に点滅させる。
[DisallowMultipleComponent]
public sealed class PlayerDamageMaterialFlash : MonoBehaviour
{
    private static readonly int BASE_COLOR = Shader.PropertyToID("_BaseColor");
    private static readonly int EMISSION_COLOR = Shader.PropertyToID("_EmissionColor");

    [Header("点滅の設定")]
    [Tooltip("引数なしのPlayDamageFlashを呼んだときに点滅する秒数です。無敵や硬直は付与しません。")]
    [SerializeField, Min(0.01f)] private float defaultFlashDuration = 0.5f;
    [Tooltip("点滅させる身体・武器の親です。残像やエフェクトの親は含めないでください。")]
    [SerializeField] private Transform flashModelRoot;
    [Tooltip("個別指定する場合の描画対象です。空欄なら見た目の親からMeshRendererとSkinnedMeshRendererを取得します。")]
    [SerializeField] private Renderer[] flashRenderers;
    [Tooltip("白黒点滅の元にするUniversal Render Pipeline/Litのマテリアルです。実行時に専用コピーを作り、元の設定は変更しません。")]
    [SerializeField] private Material flashMaterial;
    [Tooltip("白と黒を切り替える間隔です。点滅時間が終わると元のマテリアルへ戻ります。")]
    [SerializeField, Min(0.01f)] private float flashInterval = 0.08f;

    public bool IsFlashing => flashRemaining > 0f;

    private float flashRemaining;
    private float flashElapsed;
    private int lastFlashPhase = -1;
    private bool warnedFlashSettings;
    private Material whiteMaterial;
    private Material blackMaterial;

    private sealed class FlashPart
    {
        public Renderer Renderer;
        public Material[] OriginalMaterials;
        public Material[] WhiteMaterials;
        public Material[] BlackMaterials;
        public MaterialPropertyBlock OriginalBlock;
        public MaterialPropertyBlock[] OriginalSlotBlocks;
    }

    private readonly List<FlashPart> activeFlashParts = new List<FlashPart>();

    // 既存Receiverだけを差し替えた場合に、従来のInspector設定を引き継ぐ。
    internal void ConfigureLegacySettings(Transform modelRoot, Renderer[] renderers,
        Material material, float interval)
    {
        StopFlashing();
        flashModelRoot = modelRoot;
        flashRenderers = renderers;
        flashMaterial = material;
        flashInterval = interval;
    }

    // Inspectorのイベントなどから、設定済みの秒数で点滅を開始する。
    public void PlayDamageFlash()
    {
        PlayDamageFlash(defaultFlashDuration);
    }

    // 指定秒数だけ点滅する。連続呼び出しでは色の位相を保ち、終了までの時間を延長する。
    public void PlayDamageFlash(float duration)
    {
        if (!isActiveAndEnabled || duration <= 0f ||
            float.IsNaN(duration) || float.IsInfinity(duration)) return;

        if (IsFlashing)
        {
            flashRemaining = Mathf.Max(flashRemaining, duration);
            return;
        }

        BeginFlashing();
        if (activeFlashParts.Count > 0) flashRemaining = duration;
    }

    // 物理更新で点滅時間を進め、指定秒数の経過後に元の描画状態へ戻す。
    private void FixedUpdate()
    {
        if (!IsFlashing) return;
        flashRemaining = Mathf.Max(0f, flashRemaining - Time.fixedDeltaTime);
        if (!IsFlashing)
        {
            RestoreMaterials();
            return;
        }
        flashElapsed += Time.fixedDeltaTime;
        ApplyFlashPhase();
    }

    // 死亡・復活・演出中断などの際に、点滅を止めて元の描画状態へ戻す。
    public void StopFlashing()
    {
        flashRemaining = 0f;
        RestoreMaterials();
    }

    // 専用マテリアルを生成し、現在の材質とプロパティを保存して白から点滅を始める。
    private void BeginFlashing()
    {
        RestoreMaterials();
        if (!PrepareFlashMaterials()) return;

        Renderer[] sources = flashRenderers != null && flashRenderers.Length > 0
            ? flashRenderers
            : flashModelRoot != null
                ? flashModelRoot.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];

        HashSet<Renderer> registered = new HashSet<Renderer>();
        foreach (Renderer source in sources)
        {
            if (source == null || !registered.Add(source) ||
                (source is not MeshRenderer && source is not SkinnedMeshRenderer)) continue;

            Material[] originals = source.sharedMaterials;
            if (originals.Length == 0) continue;
            FlashPart part = new FlashPart
            {
                Renderer = source,
                OriginalMaterials = originals,
                WhiteMaterials = new Material[originals.Length],
                BlackMaterials = new Material[originals.Length],
                OriginalBlock = new MaterialPropertyBlock(),
                OriginalSlotBlocks = new MaterialPropertyBlock[originals.Length]
            };
            source.GetPropertyBlock(part.OriginalBlock);
            for (int i = 0; i < originals.Length; i++)
            {
                part.WhiteMaterials[i] = whiteMaterial;
                part.BlackMaterials[i] = blackMaterial;
                part.OriginalSlotBlocks[i] = new MaterialPropertyBlock();
                source.GetPropertyBlock(part.OriginalSlotBlocks[i], i);
                source.SetPropertyBlock(null, i);
            }
            source.SetPropertyBlock(null);
            activeFlashParts.Add(part);
        }

        if (activeFlashParts.Count == 0 && !warnedFlashSettings)
        {
            warnedFlashSettings = true;
            Debug.LogWarning("被弾点滅：Flash Model RootまたはFlash Renderersに身体の描画対象を設定してください。", this);
        }
        flashElapsed = 0f;
        lastFlashPhase = -1;
        ApplyFlashPhase();
    }

    // 割り当てたURP Litの専用コピーを、白黒点滅用に作成する。
    private bool PrepareFlashMaterials()
    {
        if (whiteMaterial != null && blackMaterial != null) return true;
        if (flashMaterial == null || flashMaterial.shader == null ||
            flashMaterial.shader.name != "Universal Render Pipeline/Lit" ||
            !flashMaterial.shader.isSupported)
        {
            if (!warnedFlashSettings)
            {
                warnedFlashSettings = true;
                Debug.LogWarning("被弾点滅：Universal Render Pipeline/LitのマテリアルをFlash Materialへ設定してください。硬直と無敵は動作します。", this);
            }
            return false;
        }

        whiteMaterial = new Material(flashMaterial) { name = "PlayerHurt_White", hideFlags = HideFlags.DontSave };
        blackMaterial = new Material(flashMaterial) { name = "PlayerHurt_Black", hideFlags = HideFlags.DontSave };
        ConfigureLitFlashMaterial(whiteMaterial, Color.white);
        ConfigureLitFlashMaterial(blackMaterial, Color.black);
        return true;
    }

    // Litの反射・透過・テクスチャの影響を抑え、Emissionで白と黒を指定する。
    // 実行時のコピーだけに適用し、元のマテリアルは変更しない。
    private static void ConfigureLitFlashMaterial(Material material, Color color)
    {
        material.SetColor(BASE_COLOR, Color.black);
        material.SetColor(EMISSION_COLOR, color);
        material.SetTexture("_BaseMap", Texture2D.whiteTexture);
        material.SetTexture("_EmissionMap", Texture2D.whiteTexture);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero);
        material.SetFloat("_ZWrite", 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_EnvironmentReflections", 0f);
        material.SetFloat("_ReceiveShadows", 0f);
        if (material.HasProperty("_SpecColor")) material.SetColor("_SpecColor", Color.black);
        if (material.HasProperty("_ZWriteControl")) material.SetFloat("_ZWriteControl", 1f);
        if (material.HasProperty("_SrcBlendAlpha"))
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_DstBlendAlpha"))
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.Zero);

        material.EnableKeyword("_EMISSION");
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.DisableKeyword("_NORMALMAP");
        material.DisableKeyword("_PARALLAXMAP");
        material.DisableKeyword("_METALLICSPECGLOSSMAP");
        material.DisableKeyword("_DETAIL_MULX2");
        material.DisableKeyword("_DETAIL_SCALED");
        material.DisableKeyword("_CLEARCOAT");
        material.DisableKeyword("_CLEARCOATMAP");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    }

    // 指定間隔で白黒を切り替える。同じ色の間は再割り当てしない。
    private void ApplyFlashPhase()
    {
        int phase = Mathf.FloorToInt(flashElapsed / Mathf.Max(0.01f, flashInterval)) % 2;
        if (phase == lastFlashPhase) return;
        lastFlashPhase = phase;
        foreach (FlashPart part in activeFlashParts)
        {
            if (part.Renderer != null)
                part.Renderer.sharedMaterials = phase == 0 ? part.WhiteMaterials : part.BlackMaterials;
        }
    }

    // 保存したマテリアルとプロパティを戻し、点滅用の対象一覧を空にする。
    private void RestoreMaterials()
    {
        foreach (FlashPart part in activeFlashParts)
        {
            if (part.Renderer == null) continue;
            part.Renderer.sharedMaterials = part.OriginalMaterials;
            part.Renderer.SetPropertyBlock(part.OriginalBlock.isEmpty ? null : part.OriginalBlock);
            for (int i = 0; i < part.OriginalSlotBlocks.Length; i++)
            {
                MaterialPropertyBlock block = part.OriginalSlotBlocks[i];
                part.Renderer.SetPropertyBlock(block.isEmpty ? null : block, i);
            }
        }
        activeFlashParts.Clear();
        flashElapsed = 0f;
        lastFlashPhase = -1;
    }

    // 無効化・プール返却の際に、点滅用マテリアルを残さない。
    private void OnDisable()
    {
        StopFlashing();
    }

    // 元の描画状態へ戻してから、実行中に生成した専用マテリアルを破棄する。
    private void OnDestroy()
    {
        StopFlashing();
        if (whiteMaterial != null) Destroy(whiteMaterial);
        if (blackMaterial != null) Destroy(blackMaterial);
    }
}
