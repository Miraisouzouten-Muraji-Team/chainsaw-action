using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shader Graphを使用して円弧ゲージを表示し、色設定と針の回転を管理する。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public class TachometerGraphic : MaskableGraphic
{
    /// <summary>
    /// 円弧の色とグラデーションの設定。
    /// </summary>
    [Serializable]
    public class ColorStop
    {
        [Tooltip("この色を適用する範囲。0が円弧の開始位置、1が終了位置です。")]
        [Range(0f, 1f)]
        public float position;

        [Tooltip("0から指定の位置までに設定する色。")]
        public Color color = Color.white;

        [Tooltip("ONにすると、境界付近でグラデーションを作成。OFFの場合は境界で色を切り替えます。")]
        public bool gradientFromPrevious;

        [Tooltip("グラデーションを適用する範囲の幅。値を大きくすると、色が変化する範囲が広がります。")]
        [Min(0.001f)]
        public float gradientWidth = 0.03f;
    }

    private static readonly int StartAngleId =
        Shader.PropertyToID("_ArcStartAngle");

    private static readonly int EndAngleId =
        Shader.PropertyToID("_ArcEndAngle");

    private static readonly int ThicknessId =
        Shader.PropertyToID("_ArcThickness");

    private static readonly int ColorRampId =
        Shader.PropertyToID("_ColorRamp");

    private const int RampResolution = 1024;

    [Header("Shader Graph Material")]
    [Tooltip("Tachometerの描画に使用するShader Graph対応のマテリアルを設定します。")]
    [SerializeField] private Material arcMaterial;

    [Header("Tachometer設定")]
    [Tooltip("Tachometerの開始角度。Shader Graph側では真下を0度として時計回りに角度を計算します。")]
    [SerializeField] private float startAngle = 0f;

    [Tooltip("Tachometerの終了角度。開始角度からの描画範囲を決定します。")]
    [SerializeField] private float endAngle = 250f;

    [Tooltip("Tachometerの太さ。値を大きくするとリングが太くなります。")]
    [SerializeField, Range(0.001f, 0.5f)]
    private float thickness = 0.12f;

    [Header("色設定")]
    [Tooltip("Tachometerの色を設定。追加要素で色を増やせ、後から追加したElementほど、指定Positionより左側の色を上書きします。")]
    [SerializeField]
    private List<ColorStop> colorStops = new List<ColorStop>
    {
        new ColorStop
        {
            position = 0f,
            color = Color.yellow,
            gradientFromPrevious = false
        },
        new ColorStop
        {
            position = 0.8f,
            color = Color.blue,
            gradientFromPrevious = false
        }
    };

    [Header("針の設定")]
    [Tooltip("針の回転に使用する速度情報を持つChainsawAcceleratorを設定します。")]
    [SerializeField] private ChainsawAccelerator accelerator;

    [Tooltip("回転させる針のRectTransformを設定。TachometerのStartAngleとEndAngleに合わせて回転します。")]
    [SerializeField] private RectTransform needle;

    private Material runtimeMaterial;
    private Texture2D colorRamp;

    private void Awake()
    {
        InitializeMaterial();
        RefreshShaderSettings();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        InitializeMaterial();
        RefreshShaderSettings();
    }

    private void Update()
    {
        UpdateNeedle();
    }

    /// <summary>
    /// Inspectorで設定が変更されたときに、円弧の表示設定を更新する。
    /// </summary>
    protected override void OnValidate()
    {
        base.OnValidate();

        if (!isActiveAndEnabled)
            return;

        InitializeMaterial();
        RefreshShaderSettings();
        SetVerticesDirty();
    }

    private void InitializeMaterial()
    {
        if (runtimeMaterial != null)
            return;

        if (arcMaterial == null)
            return;

        runtimeMaterial = new Material(arcMaterial)
        {
            name = arcMaterial.name + " (Runtime)"
        };

        // このUIオブジェクト専用のMaterialを使う
        base.material = runtimeMaterial;
    }

    private void RefreshShaderSettings()
    {
        if (runtimeMaterial == null)
            InitializeMaterial();

        if (runtimeMaterial == null)
            return;

        runtimeMaterial.SetFloat(StartAngleId, startAngle);
        runtimeMaterial.SetFloat(EndAngleId, endAngle);
        runtimeMaterial.SetFloat(ThicknessId, thickness);

        UpdateColorRamp();
    }

    private void UpdateColorRamp()
    {
        if (runtimeMaterial == null)
            return;

        if (colorRamp == null)
        {
            colorRamp = new Texture2D(
                RampResolution,
                1,
                TextureFormat.RGBA32,
                false,
                true
            )
            {
                name = "GlowArc Color Ramp",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };
        }

        Color[] pixels = new Color[RampResolution];

        for (int i = 0; i < RampResolution; i++)
        {
            float t = (float)i / (RampResolution - 1);
            pixels[i] = EvaluateColor(t);
        }

        colorRamp.SetPixels(pixels);
        colorRamp.Apply(false, false);

        runtimeMaterial.SetTexture(ColorRampId, colorRamp);
    }

    private Color EvaluateColor(float t)
    {
        if (colorStops == null || colorStops.Count == 0)
            return Color.white;

        // Element 0を全体の基本色にする
        Color currentColor = colorStops[0].color;

        // Element 1以降は、Positionより左側の色を上書きする
        for (int i = 1; i < colorStops.Count; i++)
        {
            ColorStop stop = colorStops[i];
            float boundary = Mathf.Clamp01(stop.position);

            // グラデーションがOFFなら、境界で色を切り替える
            if (!stop.gradientFromPrevious)
            {
                if (t < boundary)
                    currentColor = stop.color;

                continue;
            }

            // グラデーションの範囲を計算する
            float halfWidth = Mathf.Max(0.001f, stop.gradientWidth) * 0.5f;
            float gradientStart = boundary - halfWidth;
            float gradientEnd = boundary + halfWidth;

            if (t < gradientStart)
            {
                // 境界より左側では、追加した色で上書きする
                currentColor = stop.color;
            }
            else if (t <= gradientEnd)
            {
                // 境界付近では、追加した色から右側の色へ滑らかに変化させる
                float blend = Mathf.InverseLerp(
                    gradientStart,
                    gradientEnd,
                    t
                );

                blend = Mathf.SmoothStep(0f, 1f, blend);

                currentColor = Color.Lerp(
                    stop.color,
                    currentColor,
                    blend
                );
            }
        }

        return currentColor;
    }

    // 速度に応じて、針オブジェクトを円弧に沿って時計回りに回転させる。
    // 円弧の開始角度から終了角度までを針の可動範囲とする。
    private void UpdateNeedle()
    {
        if (needle == null || accelerator == null)
            return;

        // 速度を0～1に収める
        float speedRatio = Mathf.Clamp01(accelerator.SpeedRatio);

        // 円弧の開始角度から終了角度まで計算する
        float meterAngle = Mathf.Lerp(
            startAngle,
            endAngle,
            speedRatio
        );

        // タコメーター用の角度をUnityのZ回転に変換する
        float unityAngle = 180f - meterAngle;

        // 針を回転させる
        needle.localRotation = Quaternion.Euler(
            0f,
            0f,
            unityAngle
        );
    }
    protected override void OnDestroy()
    {
        // このコンポーネント専用に生成したリソースを破棄する
        if (runtimeMaterial != null)
            DestroyImmediate(runtimeMaterial);

        if (colorRamp != null)
            DestroyImmediate(colorRamp);

        base.OnDestroy();
    }
}
