using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Opaque合成で生成したRender Graphリソースを後続Passへ共有する。
    /// TextureHandleは現在のフレーム内でのみ有効。
    /// </summary>
    public sealed class PixelOpaqueFrameData : ContextItem
    {
        // Near / Middle / Farを統合したDepth。
        public TextureHandle Depth = TextureHandle.nullHandle;

        // Near / Middle / Farを統合したWorld Normal。
        public TextureHandle Normal = TextureHandle.nullHandle;

        /// <summary>
        /// フレーム終了時に参照を初期化する。
        /// </summary>
        public override void Reset()
        {
            Depth = TextureHandle.nullHandle;
            Normal = TextureHandle.nullHandle;
        }
    }
}
