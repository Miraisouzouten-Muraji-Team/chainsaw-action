using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

/// <summary>
/// Third weak attack: threshold, inverse, threshold, inverse, slash, black.
/// One step per rendered game frame (not a fixed 60 Hz timeline).
/// Add to the player and assign the same AttackData asset as Slash3.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class PlayerThirdAttackImpact : MonoBehaviour
{
    [Header("3段目の判定")]
    [SerializeField] private SensorPlayerController controller;
    [Tooltip("SensorPlayerControllerのSlash3と同じアセット。1・2段目とは別のアセットにしてください。")]
    [SerializeField] private AttackData thirdAttackData;
    [Tooltip("3段目開始からの待ち時間。ゲーム内時間で計測。0なら開始直後。")]
    [SerializeField, Min(0f)] private float startDelaySeconds;

    [Header("白黒2値化 / Third Attack Impact Renderer Feature")]
    [Tooltip("通常はON。画面へ最終出力するカメラに適用。RenderTextureへ出すカメラを指定する場合のみOFF。原則カメラスタックの最後で処理します。 ")]
    [SerializeField] private bool autoSelectFinalCamera = true;
    [Tooltip("Auto Select Final CameraがOFFの時だけ使用。ゲーム画面を描画するカメラを指定。")]
    [SerializeField] private Camera impactCamera;
    [SerializeField, Range(0f, 1f)] private float threshold = 0.5f;
    [Tooltip("ONなら1・3コマ目を反転、2・4コマ目を通常の白黒にする。")]
    [SerializeField] private bool invertFirstStep;

    [Header("動作確認 / Play中のみ")]
    [Tooltip("ONの間は攻撃不要で白黒表示を維持。確認が済んだらOFFにする。")]
    [SerializeField] private bool previewThreshold;
    [SerializeField] private bool previewInverted;
    [SerializeField, TextArea] private string renderStatus = "待機中";

    [Header("各コマの描画フレーム数 / 合計は6項目の和")]
    [SerializeField, Range(1, 600)] private int firstStepFrames = 1;
    [SerializeField, Range(1, 600)] private int secondStepFrames = 1;
    [SerializeField, Range(1, 600)] private int thirdStepFrames = 1;
    [SerializeField, Range(1, 600)] private int fourthStepFrames = 1;
    [SerializeField, Range(1, 600)] private int slashFrames = 1;
    [SerializeField, Range(1, 600)] private int finalBlackFrames = 1;

    [Header("白いスラッシュ")]
    [Tooltip("任意。未設定なら先端が細い白い斜線を生成。設定する画像は白色・背景透過を推奨。")]
    [SerializeField] private Sprite slashSprite;
    [SerializeField] private float slashAngle = 25f;
    [SerializeField, Range(0.1f, 1.5f)] private float slashWidthRatio = 0.95f;
    [SerializeField, Range(0.005f, 0.5f)] private float slashHeightRatio = 0.08f;
    [SerializeField] private int sortingOrder = 32760;

    private GameObject overlay;
    private Image background;
    private Graphic slash;
    private bool wasThirdAttack;
    private bool pending;
    private float delayRemaining;
    [SerializeField] private bool triggerOnHit = true;
    [SerializeField, Min(0f)] private float outlineBlurPixels = 14f;
    [SerializeField, Range(0f, 1f)] private float outlineStrength = 0.7f;
    private Vector3 hitPoint;

    private enum RawImageEffect { Threshold, Outline }

    [Header("RawImage方式")]
    [SerializeField] private RawImageEffect rawImageEffect = RawImageEffect.Threshold;
    [Tooltip("閾値変換用のカメラ背景色。")]
    [SerializeField] private Color thresholdBackground = Color.white;

    [SerializeField] private bool useRawImage = true;
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Camera actorCaptureCamera;
    [Tooltip("ゲームカメラに対する演出カメラの倍率。1.5ならキャラが約1.5倍に映ります。")]
    [SerializeField, Min(1f)] private float actorCaptureZoom = 1.5f;
    [SerializeField] private RawImage actorRawImage;
    [SerializeField] private Shader actorCanvasShader;
    private Material canvasMaterial;
    [SerializeField] private int actorCanvasSortingOrder = 1000;
    private Canvas actorCanvas;
    private RawImage actorOutput;
    [Header("メッシュ塗りつぶし")]
    [SerializeField] private Shader silhouetteCaptureShader;
    [Tooltip("プラットフォームの自動補正後、さらに上下反転させる場合のみON。")]
    [SerializeField] private bool invertCaptureY;
    private Material silhouetteMaterial;
    private RenderTexture silhouetteTexture;

    private bool RenderSolidActors()
    {
        if (silhouetteMaterial == null)
        {
            Shader shader = silhouetteCaptureShader != null ? silhouetteCaptureShader
                : Shader.Find("Project/Impact/SolidCapture");
            if (shader == null || !shader.isSupported) return false;
            silhouetteMaterial = new Material(shader);
        }
        RenderTexture source = actorCaptureCamera.targetTexture;
        if (silhouetteTexture == null || silhouetteTexture.width != source.width ||
            silhouetteTexture.height != source.height)
        {
            if (silhouetteTexture != null)
            {
                silhouetteTexture.Release();
                Destroy(silhouetteTexture);
            }
            silhouetteTexture = new RenderTexture(source.width, source.height, 0,
                RenderTextureFormat.ARGB32)
            { name = "Impact Solid Actors" };
            silhouetteTexture.Create();
        }
        CommandBuffer cmd = CommandBufferPool.Get("Impact Solid Actors");
        try
        {
            cmd.SetRenderTarget(silhouetteTexture);
            cmd.ClearRenderTarget(false, true, Color.green);
            // 専用行列を渡し、URPが設定するカメラのグローバル行列に依存しない。
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(
                actorCaptureCamera.projectionMatrix, false);
            cmd.SetGlobalMatrix("_ImpactCaptureVP",
                projection * actorCaptureCamera.worldToCameraMatrix);
            cmd.SetGlobalFloat("_ImpactCaptureY", SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(actorCaptureCamera);
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                    (actorCaptureCamera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    !GeometryUtility.TestPlanesAABB(planes, renderer.bounds))
                    continue;
                Mesh sourceMesh = null;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    sourceMesh = skinned.sharedMesh;
                }
                else if (renderer is MeshRenderer)
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter != null) sourceMesh = filter.sharedMesh;
                }
                if (sourceMesh == null) continue;
                // Renderer自身に描画させ、スキニングとスケールをUnityに適用させる。
                for (int sub = 0; sub < sourceMesh.subMeshCount; sub++)
                    cmd.DrawRenderer(renderer, silhouetteMaterial, sub, 0);
            }
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            Graphics.ExecuteCommandBuffer(cmd);
        }
        finally { CommandBufferPool.Release(cmd); }
        actorOutput.texture = silhouetteTexture;
        return true;
    }

    private void UpdatePresentation()
    {
        if (!useRawImage)
        {
            UpdateDiagnostics();
            return;
        }

        bool show = previewThreshold || (activeStep >= 0 && activeStep < 4);
        if (actorCanvasShader == null ||
            actorCanvasShader.name != "Project/Impact/ThirdAttackImpactCanvas")
            actorCanvasShader = Shader.Find("Project/Impact/ThirdAttackImpactCanvas");

        if (actorCaptureCamera == null || gameplayCamera == null ||
            actorCaptureCamera == gameplayCamera ||
            actorCaptureCamera.targetTexture == null || actorCanvasShader == null ||
            !actorCanvasShader.isSupported)
        {
            SetCaptureVisible(false);
            renderStatus = "RawImage方式のカメラ・RenderTexture・RawImage・Shaderを確認してください。";
            return;
        }
        if (!show)
        {
            SetCaptureVisible(false);
            renderStatus = "待機中";
            return;
        }

        if (canvasMaterial == null)
        {
            var canvasObject = new GameObject("Impact Actor Canvas", typeof(Canvas));
            actorCanvas = canvasObject.GetComponent<Canvas>();
            actorCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            actorCanvas.sortingOrder = actorCanvasSortingOrder;
            var imageObject = new GameObject("Impact Actors", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(canvasObject.transform, false);
            actorOutput = imageObject.GetComponent<RawImage>();
            Stretch(actorOutput.rectTransform);
            if (actorRawImage != null)
                actorRawImage.enabled = false;
            canvasMaterial = new Material(actorCanvasShader);
            actorOutput.material = canvasMaterial;
            actorOutput.raycastTarget = false;
        }

        if (!canvasMaterial.HasProperty("_ImpactCanvasVersion") ||
            !canvasMaterial.HasProperty("_UseThreshold"))
        {
            SetCaptureVisible(false);
            renderStatus = "旧Canvasシェーダーです。ThirdAttackImpactCanvas.shaderを同梱版に上書きしてください。";
            return;
        }

        actorCaptureCamera.transform.SetPositionAndRotation(gameplayCamera.transform.position,
            gameplayCamera.transform.rotation);
        actorCaptureCamera.orthographic = gameplayCamera.orthographic;
        actorCaptureCamera.nearClipPlane = gameplayCamera.nearClipPlane;
        actorCaptureCamera.farClipPlane = gameplayCamera.farClipPlane;
        actorCaptureCamera.projectionMatrix = gameplayCamera.projectionMatrix;
        // 透明度を書き込まないキャラのマテリアルでも輪郭を取れるよう、
        // 専用カメラの背景を緑で塗り、合成時に色で抜く。
        actorCaptureCamera.clearFlags = CameraClearFlags.SolidColor;
        bool simpleThreshold = rawImageEffect == RawImageEffect.Threshold;
        actorCaptureCamera.backgroundColor = Color.green;
        actorOutput.texture = actorCaptureCamera.targetTexture;
        Vector4 settings = GetOutlineSettings(actorCaptureCamera);
        bool inverse = previewThreshold ? previewInverted : ((activeStep & 1) == 1) ^ invertFirstStep;
        canvasMaterial.SetFloat("_ActorWhite", inverse ? 1f : 0f);
        canvasMaterial.SetFloat("_UseThreshold", simpleThreshold ? 1f : 0f);
        canvasMaterial.SetFloat("_Threshold", Mathf.Clamp01(threshold));
        canvasMaterial.SetFloat("_ThresholdSilhouette", 1f);
        canvasMaterial.SetColor("_ThresholdBackground", thresholdBackground);
        // 拡大の中心を命中点に合わせる。プレビューではプレイヤーを中心にする。
        Vector3 focus = previewThreshold || !triggerOnHit
            ? actorCaptureCamera.WorldToViewportPoint(transform.position)
            : actorCaptureCamera.WorldToViewportPoint(hitPoint);
        Vector2 center = focus.z > 0f
            ? new Vector2(Mathf.Clamp01(focus.x), Mathf.Clamp01(focus.y))
            : new Vector2(0.5f, 0.5f);
        float zoom = Mathf.Max(1f, actorCaptureZoom);
        float span = 1f / zoom;
        if (zoom <= 1f) center = new Vector2(0.5f, 0.5f);
        center.x = Mathf.Clamp(center.x, span * 0.5f, 1f - span * 0.5f);
        center.y = Mathf.Clamp(center.y, span * 0.5f, 1f - span * 0.5f);
        actorOutput.uvRect = new Rect(center.x - span * 0.5f,
            center.y - span * 0.5f, span, span);
        // シェーダー側の拡大は等倍にし、RawImageのUVだけで拡大する。
        canvasMaterial.SetVector("_ImpactZoom", new Vector4(0.5f, 0.5f, 1f, 0f));
        canvasMaterial.SetVector("_ImpactOutline",
            new Vector4(center.x, center.y, settings.z * span, settings.w));
        bool solid = true;
        bool flipY = invertCaptureY;
        canvasMaterial.SetFloat("_CaptureFlipY", flipY ? 1f : 0f);
        if (solid && !RenderSolidActors())
        {
            SetCaptureVisible(false);
            renderStatus = "Silhouette Capture ShaderにImpactSolidCapture.shaderを指定してください。";
            return;
        }
        SetCaptureVisible(true);
        if (solid) actorCaptureCamera.enabled = false;
        renderStatus = $"Canvas v2 | Mode {rawImageEffect} | Applied {canvasMaterial.GetFloat("_UseThreshold"):F0} | Zoom {zoom:F2} | Shader {canvasMaterial.shader.name}";
    }

    private void SetCaptureVisible(bool visible)
    {
        if (actorCaptureCamera != null && actorCaptureCamera != gameplayCamera)
            actorCaptureCamera.enabled = visible;
        if (actorCanvas != null)
            actorCanvas.enabled = visible;
    }

    public static void PlayOnHit(Vector3 point)
    {
        if (instance == null || !instance.isActiveAndEnabled || !instance.triggerOnHit)
            return;
        instance.hitPoint = point;
        instance.firstFrame = Time.frameCount;
    }

    public static Vector4 GetOutlineSettings(Camera camera)
    {
        if (instance == null || (!instance.previewThreshold &&
            (instance.activeStep < 0 || instance.activeStep >= 4)))
            return Vector4.zero;

        Vector3 center = (instance.previewThreshold || !instance.triggerOnHit) ? new Vector3(0.5f, 0.5f, 1f)
            : camera.WorldToViewportPoint(instance.hitPoint);
        return new Vector4(center.x, center.y, instance.outlineBlurPixels, instance.outlineStrength);
    }

    private int firstFrame = -1;
    private int activeStep = -1;
    private static PlayerThirdAttackImpact instance;
    private int lastRenderedFrame = -100;
    private int effectRequestedFrame = -1;
    private bool missingPassWarning;

    public static bool TryGetRenderSettings(Camera camera, bool finalCamera, out Vector4 settings)
    {
        settings = Vector4.zero;
        if (instance == null || !instance.isActiveAndEnabled || instance.useRawImage || camera == null ||
            camera.cameraType != CameraType.Game)
            return false;
        bool selected = instance.autoSelectFinalCamera
            ? finalCamera && camera.targetTexture == null && camera.targetDisplay == 0
            : camera == instance.impactCamera;
        if (!selected) return false;
        bool active = instance.previewThreshold || (instance.activeStep >= 0 && instance.activeStep < 4);
        if (!active) return false;
        bool inverse = instance.previewThreshold ? instance.previewInverted :
            (((instance.activeStep & 1) == 1) ^ instance.invertFirstStep);
        settings = new Vector4(1f, Mathf.Clamp01(instance.threshold), inverse ? 1f : 0f, 0f);
        return true;
    }

    public static void NotifyPassExecuted(Camera camera)
    {
        if (instance == null) return;
        instance.lastRenderedFrame = Time.frameCount;
        instance.renderStatus = "白黒描画パス実行済み: " + camera.name;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => instance = null;

    private void OnEnable()
    {
        if (instance != null && instance != this)
        {
            Debug.LogError("[ThirdAttackImpact] 有効なPlayerThirdAttackImpactは1つにしてください。", this);
            enabled = false;
            return;
        }
        instance = this;
    }

    private void UpdateDiagnostics()
    {
        bool requested = previewThreshold || (activeStep >= 0 && activeStep < 4);
        if (!requested)
        {
            effectRequestedFrame = -1;
            return;
        }
        if (effectRequestedFrame < 0)
            effectRequestedFrame = Time.frameCount;
        if (Time.frameCount - effectRequestedFrame >= 2 && lastRenderedFrame < Time.frameCount - 1)
        {
            renderStatus = "白黒パス未実行: Renderer Feature / Shader / カメラ指定を確認";
            if (!missingPassWarning)
            {
                missingPassWarning = true;
                Debug.LogWarning("[ThirdAttackImpact] 白黒描画パスが実行されていません。使用中のRendererへThirdAttackImpactRendererFeatureを追加し、Threshold Shaderを設定してください。Auto Select Final CameraをONにするか、対象カメラを指定してください。", this);
            }
        }
    }

    private int StepAtFrame(int frame)
    {
        if (frame < 0) return -1;
        frame -= Mathf.Clamp(firstStepFrames, 1, 600);
        if (frame < 0) return 0;
        frame -= Mathf.Clamp(secondStepFrames, 1, 600);
        if (frame < 0) return 1;
        frame -= Mathf.Clamp(thirdStepFrames, 1, 600);
        if (frame < 0) return 2;
        frame -= Mathf.Clamp(fourthStepFrames, 1, 600);
        if (frame < 0) return 3;
        frame -= Mathf.Clamp(slashFrames, 1, 600);
        if (frame < 0) return 4;
        frame -= Mathf.Clamp(finalBlackFrames, 1, 600);
        return frame < 0 ? 5 : -1;
    }

    private void Reset() => controller = GetComponentInParent<SensorPlayerController>();

    private void Awake()
    {
        if (controller == null)
            controller = GetComponentInParent<SensorPlayerController>();
        BuildOverlay();
        if (useRawImage) SetCaptureVisible(false);
    }

    private void LateUpdate()
    {
        bool isThird = !triggerOnHit && controller != null && controller.isActiveAndEnabled &&
            thirdAttackData != null && controller.IsAttacking &&
            controller.CurrentAttackData == thirdAttackData;

        if (isThird && !wasThirdAttack)
        {
            pending = true;
            delayRemaining = startDelaySeconds;
        }
        else if (pending && isThird)
        {
            delayRemaining -= Time.deltaTime;
        }
        wasThirdAttack = isThird;

        // Cancel an impact that has not started if the attack is interrupted.
        if (!isThird)
            pending = false;

        if (pending && delayRemaining <= 0f)
        {
            pending = false;
            firstFrame = Time.frameCount;
        }

        if (firstFrame < 0)
        {
            activeStep = -1;
            overlay.SetActive(false);
            UpdatePresentation();
            return;
        }

        int frame = Time.frameCount - firstFrame;
        activeStep = StepAtFrame(frame);
        if (activeStep < 0)
        {
            firstFrame = -1;
            overlay.SetActive(false);
            UpdatePresentation();
            return;
        }

        // Steps 0..3 expose the actual camera image to the threshold shader.
        // Steps 4..5 use the existing black screen overlay.
        overlay.SetActive(!previewThreshold && activeStep >= 4);
        background.color = Color.black;
        slash.gameObject.SetActive(activeStep == 4);
        UpdatePresentation();
        // Render frames continue during hit stop; start delay uses game time.
    }

    private void BuildOverlay()
    {
        overlay = new GameObject("ThirdAttackImpactOverlay", typeof(RectTransform), typeof(Canvas));
        overlay.transform.SetParent(transform, false);
        Canvas canvas = overlay.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        bg.transform.SetParent(overlay.transform, false);
        background = bg.GetComponent<Image>();
        background.raycastTarget = false;
        Stretch(background.rectTransform);

        GameObject slashObject = new GameObject("WhiteSlash", typeof(RectTransform), typeof(CanvasRenderer));
        slashObject.transform.SetParent(overlay.transform, false);
        if (slashSprite != null)
        {
            Image image = slashObject.AddComponent<Image>();
            image.sprite = slashSprite;
            image.preserveAspect = true;
            slash = image;
        }
        else
        {
            slash = slashObject.AddComponent<ThirdAttackSlashGraphic>();
        }
        slash.color = Color.white;
        slash.raycastTarget = false;
        RectTransform rect = slash.rectTransform;
        rect.anchorMin = new Vector2(0.5f - slashWidthRatio * 0.5f, 0.5f - slashHeightRatio * 0.5f);
        rect.anchorMax = new Vector2(0.5f + slashWidthRatio * 0.5f, 0.5f + slashHeightRatio * 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.Euler(0f, 0f, slashAngle);
        overlay.SetActive(false);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void OnDisable()
    {
        if (instance == this) instance = null;
        if (useRawImage) SetCaptureVisible(false);
        activeStep = -1;
        effectRequestedFrame = -1;
        missingPassWarning = false;
        renderStatus = "待機中";
        pending = false;
        wasThirdAttack = false;
        firstFrame = -1;
        if (overlay != null)
            overlay.SetActive(false);
    }

    private void OnDestroy()
    {
        if (silhouetteMaterial != null) Destroy(silhouetteMaterial);
        if (silhouetteTexture != null)
        {
            silhouetteTexture.Release();
            Destroy(silhouetteTexture);
        }
        if (canvasMaterial != null)
        {
            Destroy(canvasMaterial);
        }
        if (actorCanvas != null)
            Destroy(actorCanvas.gameObject);
        if (overlay != null)
            Destroy(overlay);
    }
}

// Runtime-generated, tapered vector slash. No texture or custom shader required.
public sealed class ThirdAttackSlashGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = rectTransform.rect;
        Color32 c = color;
        vh.AddVert(new Vector3(r.xMin, r.center.y), c, Vector2.zero);
        vh.AddVert(new Vector3(r.center.x + r.width * 0.1f, r.yMax), c, Vector2.zero);
        vh.AddVert(new Vector3(r.xMax, r.center.y), c, Vector2.zero);
        vh.AddVert(new Vector3(r.center.x - r.width * 0.1f, r.yMin), c, Vector2.zero);
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);
    }
}
