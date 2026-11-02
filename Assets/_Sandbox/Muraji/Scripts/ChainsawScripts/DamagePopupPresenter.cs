using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 敵への実ダメージを、同じフォントの文字と光の2層で表示する。
[DisallowMultipleComponent]
public sealed class DamagePopupPresenter : MonoBehaviour
{
    private static readonly int FACE_COLOR = Shader.PropertyToID("_FaceColor");
    private static readonly int OUTLINE_COLOR = Shader.PropertyToID("_OutlineColor");
    private static readonly int OUTLINE_WIDTH = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OUTLINE_SOFTNESS = Shader.PropertyToID("_OutlineSoftness");
    private static readonly int FACE_DILATE = Shader.PropertyToID("_FaceDilate");
    private const float MIN_LIFETIME = 0.05f;
    private const float FADE_START = 0.55f;
    private const float POP_END = 0.18f;
    private const float POP_SCALE = 1.25f;

    [Header("フォントと表示位置")]
    [Tooltip("用意したフォントから作成したTextMeshProのSDF Font Assetを指定します。字形は変更しません。数字0〜9を含めてください。")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [Tooltip("敵を描画している3Dカメラです。RenderTexture合成用カメラではなく、敵の位置を正しく映すカメラを指定してください。未指定ならMainCameraを使います。")]
    [SerializeField] private Camera worldCamera;
    [Tooltip("命中位置から数字を出す位置までの、ワールド座標でのずらし量です。")]
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.45f, 0f);
    [Tooltip("専用Canvasの表示順です。大きいほど他のUIより手前に表示します。")]
    [SerializeField] private int sortingOrder = 2000;
    [Tooltip("ゲーム画面を画面の一部へ合成している場合にオンにし、下の表示範囲を指定します。")]
    [SerializeField] private bool useCustomOutputRect;
    [Tooltip("最終画面内のゲーム表示範囲です。左下を原点とする0〜1の値で指定します。全画面ならX=0、Y=0、W=1、H=1です。")]
    [SerializeField] private Rect outputRect = new Rect(0f, 0f, 1f, 1f);

    [Header("表示時間と移動")]
    [Tooltip("数字が消えるまでの秒数です。後半は透明になりながら消えます。")]
    [SerializeField, Min(MIN_LIFETIME)] private float lifetime = 0.7f;
    [Tooltip("表示中に上へ移動する距離です。1920×1080を基準とするUIの距離です。")]
    [SerializeField, Min(0f)] private float riseDistance = 90f;
    [Tooltip("連続命中の数字を左右へ散らす幅です。0なら左右に散らしません。")]
    [SerializeField, Min(0f)] private float horizontalSpread = 24f;
    [Tooltip("オンならヒットストップ中も数字の移動と消滅を進めます。")]
    [SerializeField] private bool useUnscaledTime = true;
    [Tooltip("同時に出せる数字の上限です。上限時は古い表示を再利用します。起動時に必要な分を用意します。")]
    [SerializeField, Range(2, 64)] private int maximumPopups = 24;

    [Header("次の命中で前の数字を弾く")]
    [Tooltip("同じ敵に次の数字が表示された瞬間、直前の数字を弾けさせて消します。")]
    [SerializeField] private bool burstPreviousOnHit = true;
    [Tooltip("前の数字が弾け始めてから消えるまでの秒数です。")]
    [SerializeField, Min(MIN_LIFETIME)] private float burstDuration = 0.16f;
    [Tooltip("弾けるときの拡大率です。弾け始めの大きさに対する倍率です。")]
    [SerializeField, Min(1f)] private float burstScale = 1.6f;
    [Tooltip("数字の文字同士が左右へ離れる距離です。1920×1080を基準に指定します。")]
    [SerializeField, Min(0f)] private float burstSpread = 35f;
    [Tooltip("弾けるときに数字が上へ飛ぶ距離です。1920×1080を基準に指定します。")]
    [SerializeField, Min(0f)] private float burstRise = 25f;

    [Header("動作確認")]
    [Tooltip("再生中にコンポーネント右上のメニューからテスト表示するときに使う攻撃データです。実際の攻撃では、命中した攻撃のデータを使用します。")]
    [SerializeField] private AttackData previewAttackData;

    private sealed class PopupFrame
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public TextMeshProUGUI Core;
        public TextMeshProUGUI Halo;
        public Material CoreMaterial;
        public Material HaloMaterial;
        public Vector3 WorldPosition;
        public float HorizontalOffset;
        public float Age;
        public float Lifetime;
        public bool Active;
        public int TargetKey;
        public bool Bursting;
        public float BurstAge;
        public float BurstStartAlpha;
        public float BurstStartScale;
    }

    private PopupFrame[] frames;
    private GameObject canvasObject;
    private RectTransform canvasRect;
    private int nextFrame;

    // フォントを確認し、専用Canvas・数字・専用マテリアルを最初に確保する。
    private void Awake()
    {
        if (fontAsset == null || fontAsset.material == null ||
            !fontAsset.material.HasProperty(OUTLINE_WIDTH) ||
            !fontAsset.material.HasProperty(OUTLINE_SOFTNESS))
        {
            Debug.LogError("ダメージ数字：縁取りとぼかしに対応するTextMeshProのSDF Font Assetを指定してください。", this);
            enabled = false;
            return;
        }
        if (worldCamera == null) worldCamera = Camera.main;

        canvasObject = new GameObject("Damage Popup Canvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler));
        SceneManager.MoveGameObjectToScene(canvasObject, gameObject.scene);
        canvasRect = canvasObject.GetComponent<RectTransform>();
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        frames = new PopupFrame[Mathf.Clamp(maximumPopups, 2, 64)];
        for (int i = 0; i < frames.Length; i++) frames[i] = CreateFrame(i);
    }

    // 同じ字形の光と本体を重ねた、再利用可能な数字を作る。
    private PopupFrame CreateFrame(int index)
    {
        GameObject root = new GameObject("Damage " + index, typeof(RectTransform), typeof(CanvasGroup));
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.SetParent(canvasRect, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1200f, 400f);
        CanvasGroup group = root.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;
        Material haloMaterial = new Material(fontAsset.material) { hideFlags = HideFlags.DontSave };
        Material coreMaterial = new Material(fontAsset.material) { hideFlags = HideFlags.DontSave };
        TextMeshProUGUI halo = CreateText("Neon Halo", rect, haloMaterial);
        TextMeshProUGUI core = CreateText("Font", rect, coreMaterial);
        root.SetActive(false);
        return new PopupFrame
        {
            Root = rect,
            Group = group,
            Halo = halo,
            Core = core,
            HaloMaterial = haloMaterial,
            CoreMaterial = coreMaterial
        };
    }

    // フォント・文字配置を共通化し、クリックを妨げないTMP文字を作る。
    private TextMeshProUGUI CreateText(string objectName, RectTransform parent, Material material)
    {
        GameObject instance = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = instance.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = instance.GetComponent<TextMeshProUGUI>();
        text.font = fontAsset;
        text.fontSharedMaterial = material;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.richText = false;
        text.raycastTarget = false;
        text.extraPadding = true;
        text.color = Color.white;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    // 実ダメージを表示し、同じ敵の直前の数字を弾く。敵の識別値を省略した呼び出しは共通グループとして扱う。
    public void Show(Vector3 worldPosition, int damage, AttackData attackData, int targetKey = 0)
    {
        if (!isActiveAndEnabled || frames == null || canvasRect == null ||
            damage <= 0 || attackData == null ||
            !attackData.showDamagePopup) return;
        if (burstPreviousOnHit)
        {
            foreach (PopupFrame previous in frames)
            {
                if (previous.Active && !previous.Bursting && previous.TargetKey == targetKey)
                    BeginBurst(previous);
            }
        }
        // 空いている枠を優先し、弾けている途中の数字を可能な限り残す。
        for (int i = 0; i < frames.Length; i++)
        {
            int index = (nextFrame + i) % frames.Length;
            if (frames[index].Active) continue;
            nextFrame = index;
            break;
        }
        PopupFrame frame = frames[nextFrame];
        if (frame.Root == null) return;
        nextFrame = (nextFrame + 1) % frames.Length;
        frame.WorldPosition = worldPosition + worldOffset;
        frame.HorizontalOffset = Random.Range(-horizontalSpread, horizontalSpread);
        frame.Age = 0f;
        frame.Lifetime = Mathf.Max(MIN_LIFETIME, lifetime);
        frame.Active = true;
        frame.TargetKey = targetKey;
        frame.Bursting = false;
        frame.BurstAge = 0f;
        frame.Root.gameObject.SetActive(true);
        frame.Root.SetAsLastSibling();
        frame.Root.localScale = Vector3.one * POP_SCALE;
        frame.Group.alpha = 0f;

        string value = damage.ToString(CultureInfo.InvariantCulture);
        float fontSize = Mathf.Max(1f, attackData.damagePopupFontSize);
        frame.Core.fontSize = frame.Halo.fontSize = fontSize;
        frame.Core.SetText(value);
        frame.Halo.SetText(value);
        Color neon = attackData.damagePopupNeonColor;
        float strength = Mathf.Clamp01(attackData.damagePopupNeonStrength);
        // 同じ文字の上と下で色を変え、必要なら表示ごとに少しだけ色合いを散らす。
        float variation = Mathf.Clamp(attackData.damagePopupHueVariation, 0f, 0.1f);
        float hueOffset = variation > 0f ? Random.Range(-variation, variation) : 0f;
        Color topColor = ShiftHue(attackData.damagePopupColor, hueOffset);
        Color bottomColor = ShiftHue(attackData.damagePopupBottomColor, hueOffset);
        frame.Core.enableVertexGradient = attackData.damagePopupUseGradient;
        frame.Core.colorGradient = new VertexGradient(topColor, topColor, bottomColor, bottomColor);
        Color faceColor = attackData.damagePopupUseGradient ? Color.white : topColor;
        Color outlineColor = attackData.damagePopupUseOutline
            ? attackData.damagePopupOutlineColor
            : new Color(neon.r, neon.g, neon.b, neon.a * strength);
        float outlineWidth = attackData.damagePopupUseOutline
            ? Mathf.Clamp(attackData.damagePopupOutlineWidth, 0f, 0.5f)
            : 0.08f * strength;
        ConfigureMaterial(frame.CoreMaterial, faceColor, outlineColor,
            outlineWidth, attackData.damagePopupUseOutline ? 0f : 0.02f, 0f);
        ConfigureMaterial(frame.HaloMaterial,
            new Color(neon.r, neon.g, neon.b, neon.a * 0.35f * strength),
            new Color(neon.r, neon.g, neon.b, neon.a * 0.65f * strength),
            0.4f, 0.65f, 0.08f);
        frame.Halo.gameObject.SetActive(strength > 0f);
        frame.Core.UpdateMeshPadding();
        frame.Halo.UpdateMeshPadding();
        // 同じ数字の枠を再利用しても、前回の散らした頂点を残さない。
        frame.Core.ForceMeshUpdate();
        frame.Halo.ForceMeshUpdate(true);
        PositionFrame(frame);
    }

    // 通常表示の現在の大きさと透明度を引き継ぎ、弾けて消える段階へ切り替える。
    private void BeginBurst(PopupFrame frame)
    {
        if (frame.Root == null) return;
        PositionFrame(frame);
        frame.BurstStartScale = frame.Root.localScale.x;
        frame.BurstStartAlpha = frame.Group.alpha;
        frame.BurstAge = 0f;
        frame.Bursting = true;
    }

    // 明るさ・鮮やかさ・透明度を維持し、文字の色合いだけを少し変える。
    private static Color ShiftHue(Color color, float offset)
    {
        if (offset == 0f) return color;
        Color.RGBToHSV(color, out float hue, out float saturation, out float value);
        Color shifted = Color.HSVToRGB(Mathf.Repeat(hue + offset, 1f), saturation, value);
        shifted.a = color.a;
        return shifted;
    }

    // 文字本体の色と、SDFによる外側の光を専用コピーへ設定する。
    private static void ConfigureMaterial(Material material, Color face, Color outline,
        float width, float softness, float dilate)
    {
        material.SetColor(FACE_COLOR, face);
        material.SetColor(OUTLINE_COLOR, outline);
        material.SetFloat(OUTLINE_WIDTH, width);
        material.SetFloat(OUTLINE_SOFTNESS, softness);
        if (material.HasProperty(FACE_DILATE)) material.SetFloat(FACE_DILATE, dilate);
        material.EnableKeyword("OUTLINE_ON");
    }

    // 描画フレームで数字を滑らかに動かす。必要に応じてヒットストップ中も時間を進める。
    private void Update()
    {
        if (frames == null || canvasRect == null) return;
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        foreach (PopupFrame frame in frames)
        {
            if (!frame.Active || frame.Root == null) continue;
            if (frame.Bursting) frame.BurstAge += deltaTime;
            else frame.Age += deltaTime;
            if (frame.Bursting
                ? frame.BurstAge >= Mathf.Max(MIN_LIFETIME, burstDuration)
                : frame.Age >= frame.Lifetime)
            {
                frame.Active = false;
                frame.Root.gameObject.SetActive(false);
                continue;
            }
            PositionFrame(frame);
        }
    }

    // 命中位置をゲーム画面へ投影し、浮上・出現時の拡大・フェードを適用する。
    private void PositionFrame(PopupFrame frame)
    {
        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null)
        {
            frame.Group.alpha = 0f;
            return;
        }
        Vector3 viewport = worldCamera.WorldToViewportPoint(frame.WorldPosition);
        if (viewport.z <= 0f)
        {
            frame.Group.alpha = 0f;
            return;
        }
        Rect area = useCustomOutputRect ? outputRect
            : worldCamera.targetTexture != null ? new Rect(0f, 0f, 1f, 1f) : worldCamera.rect;
        Vector2 screen = new Vector2((area.x + viewport.x * area.width) * Screen.width,
            (area.y + viewport.y * area.height) * Screen.height);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null,
            out Vector2 local)) return;
        float progress = Mathf.Clamp01(frame.Age / frame.Lifetime);
        frame.Root.anchoredPosition = local + new Vector2(frame.HorizontalOffset, riseDistance * progress);
        float pop = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / POP_END));
        frame.Root.localScale = Vector3.one * Mathf.Lerp(POP_SCALE, 1f, pop);
        frame.Group.alpha = 1f - Mathf.Clamp01((progress - FADE_START) / (1f - FADE_START));
        if (frame.Bursting)
        {
            float burstProgress = Mathf.Clamp01(frame.BurstAge / Mathf.Max(MIN_LIFETIME, burstDuration));
            float burstEase = 1f - (1f - burstProgress) * (1f - burstProgress);
            frame.Root.anchoredPosition += Vector2.up * (burstRise * burstEase);
            frame.Root.localScale = Vector3.one * frame.BurstStartScale *
                Mathf.Lerp(1f, Mathf.Max(1f, burstScale), burstEase);
            frame.Group.alpha = frame.BurstStartAlpha * (1f - burstProgress);
            ScatterCharacters(frame.Core, burstEase);
            if (frame.Halo.gameObject.activeSelf) ScatterCharacters(frame.Halo, burstEase);
        }
    }

    // 元の文字メッシュを毎回作り直し、各桁を左右へ散らす。光の層にも同じ変形を適用する。
    private void ScatterCharacters(TextMeshProUGUI text, float progress)
    {
        text.ForceMeshUpdate();
        TMP_TextInfo textInfo = text.textInfo;
        float center = (textInfo.characterCount - 1) * 0.5f;
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            Vector3[] vertices = textInfo.meshInfo[character.materialReferenceIndex].vertices;
            // 拡大後も設定したUI距離になるように、親の拡大率で割って補正する。
            float scale = Mathf.Max(0.01f, text.rectTransform.parent.localScale.x);
            Vector3 offset = new Vector3((i - center) * burstSpread,
                (i % 2 == 0 ? 1f : -1f) * burstSpread * 0.2f, 0f) * (progress / scale);
            for (int vertex = 0; vertex < 4; vertex++)
                vertices[character.vertexIndex + vertex] += offset;
        }
        text.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
    }

    // 無効化時に表示をすべて返却し、再有効化後へ数字を持ち越さない。
    private void OnDisable()
    {
        if (frames == null) return;
        foreach (PopupFrame frame in frames)
        {
            frame.Active = false;
            if (frame.Root != null) frame.Root.gameObject.SetActive(false);
        }
    }

    // 再生中だけ、指定した攻撃データの数字を画面中央付近へテスト表示する。
    [ContextMenu("ダメージ数字をテスト表示（再生中）")]
    private void ShowPreview()
    {
        if (!Application.isPlaying || previewAttackData == null)
        {
            Debug.LogWarning("ダメージ数字：再生中にPreview Attack Dataを指定してテストしてください。", this);
            return;
        }
        Camera camera = worldCamera != null ? worldCamera : Camera.main;
        if (camera == null) return;
        Vector3 point = camera.transform.position + camera.transform.forward *
            Mathf.Max(5f, camera.nearClipPlane + 1f);
        Show(point, Mathf.Max(1, previewAttackData.damage), previewAttackData);
    }

    // 確保した専用マテリアルとCanvasだけを破棄する。
    private void OnDestroy()
    {
        if (frames != null)
        {
            foreach (PopupFrame frame in frames)
            {
                if (frame.CoreMaterial != null) Destroy(frame.CoreMaterial);
                if (frame.HaloMaterial != null) Destroy(frame.HaloMaterial);
            }
        }
        if (canvasObject != null) Destroy(canvasObject);
    }
}
