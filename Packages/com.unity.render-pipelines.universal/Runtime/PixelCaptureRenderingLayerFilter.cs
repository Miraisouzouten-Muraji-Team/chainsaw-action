
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Capture Cameraが描画するRendering Layerを指定する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelCaptureRenderingLayerFilter : MonoBehaviour
    {
        [SerializeField] private uint _renderingLayerMask = uint.MaxValue;

        public uint RenderingLayerMask
        {
            get => _renderingLayerMask;
            set => _renderingLayerMask = value;
        }
    }

    /// <summary>
    /// Capture CameraにだけRendering Layerの描画制限を適用する。
    /// 通常CameraはURP標準の描画対象設定を維持する。
    /// </summary>
    internal static class PixelCaptureFiltering
    {
        public static FilteringSettings ForCamera(FilteringSettings settings, Camera camera)
        {
            if (camera != null &&
                camera.TryGetComponent(out PixelCaptureRenderingLayerFilter filter) &&
                filter.isActiveAndEnabled)
            {
                settings.renderingLayerMask = filter.RenderingLayerMask;
            }

            return settings;
        }
    }
}
