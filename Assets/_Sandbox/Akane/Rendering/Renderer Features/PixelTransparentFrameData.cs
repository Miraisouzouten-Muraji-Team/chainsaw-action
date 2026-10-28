using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Transparent描画前のColorを同一フレーム内で共有する。
    /// </summary>
    public sealed class PixelTransparentFrameData : ContextItem
    {
        public TextureHandle BeforeColor = TextureHandle.nullHandle;

        public override void Reset()
        {
            BeforeColor = TextureHandle.nullHandle;
        }
    }
}
