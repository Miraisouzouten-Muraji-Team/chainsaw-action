
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// SceneビューのCameraにだけFull Screen Passを適用する。
    /// </summary>
    public sealed class SceneOnlyFullScreenPassRendererFeature : FullScreenPassRendererFeature
    {
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.SceneView)
            {
                return;
            }

            base.AddRenderPasses(renderer, ref renderingData);
        }
    }
}
