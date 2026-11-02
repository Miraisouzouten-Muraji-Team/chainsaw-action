using UnityEngine;
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

        // Near / Middle / Farを統合したWorld Normalと描画層ID。
        public TextureHandle Normal = TextureHandle.nullHandle;

        // 各Capture Cameraの描画解像度。
        public Vector2Int NearResolution;
        public Vector2Int MiddleResolution;
        public Vector2Int FarResolution;

        /// <summary>
        /// フレーム終了時に参照を初期化する。
        /// </summary>
        public override void Reset()
        {
            Depth = TextureHandle.nullHandle;
            Normal = TextureHandle.nullHandle;

            NearResolution = Vector2Int.zero;
            MiddleResolution = Vector2Int.zero;
            FarResolution = Vector2Int.zero;
        }
    }
}
