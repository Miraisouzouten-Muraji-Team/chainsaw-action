
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public sealed class CelShadingSettings : MonoBehaviour
{
    // セルシェーディングを有効にする。
    [SerializeField] private bool celEnabled = true;

    // 元のPBRとセルシェーディングの混合率。
    [SerializeField, Range(0.0f, 1.0f)]
    private float celStrength = 1.0f;

    // 3階調の境界。
    [SerializeField, Range(0.0f, 1.0f)]
    private float darkThreshold = 0.3f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float lightThreshold = 0.7f;

    // 各階調の直接光Diffuseの強さ。
    [SerializeField, Range(0.0f, 1.0f)]
    private float darkBrightness = 0.0f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float middleBrightness = 0.6f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float lightBrightness = 1.0f;

    // ShaderプロパティのID。
    private static readonly int CEL_ENABLED_ID = Shader.PropertyToID("_CelEnabled");
    private static readonly int CEL_STRENGTH_ID = Shader.PropertyToID("_CelStrength");
    private static readonly int CEL_DARK_THRESHOLD_ID = Shader.PropertyToID("_CelDarkThreshold");
    private static readonly int CEL_LIGHT_THRESHOLD_ID = Shader.PropertyToID("_CelLightThreshold");
    private static readonly int CEL_DARK_BRIGHTNESS_ID = Shader.PropertyToID("_CelDarkBrightness");
    private static readonly int CEL_MIDDLE_BRIGHTNESS_ID = Shader.PropertyToID("_CelMiddleBrightness");
    private static readonly int CEL_LIGHT_BRIGHTNESS_ID = Shader.PropertyToID("_CelLightBrightness");

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        ApplySettings();
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        Shader.SetGlobalFloat(CEL_ENABLED_ID, 0.0f);
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        // Game / Scene / Pixel Captureの各カメラで設定を反映する。
        ApplySettings();
    }

    private void ApplySettings()
    {
        Shader.SetGlobalFloat(CEL_ENABLED_ID, celEnabled ? 1.0f : 0.0f);
        Shader.SetGlobalFloat(CEL_STRENGTH_ID, celStrength);

        Shader.SetGlobalFloat(CEL_DARK_THRESHOLD_ID, darkThreshold);
        Shader.SetGlobalFloat(CEL_LIGHT_THRESHOLD_ID, lightThreshold);

        Shader.SetGlobalFloat(CEL_DARK_BRIGHTNESS_ID, darkBrightness);
        Shader.SetGlobalFloat(CEL_MIDDLE_BRIGHTNESS_ID, middleBrightness);
        Shader.SetGlobalFloat(CEL_LIGHT_BRIGHTNESS_ID, lightBrightness);
    }
}
