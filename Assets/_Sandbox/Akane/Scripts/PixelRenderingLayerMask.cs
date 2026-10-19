namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Pixel描画先の振り分けに使用するRendering Layer。
    /// </summary>
    public static class PixelRenderingLayerMask
    {
        public const uint NEAR = 1u << 24;
        public const uint MIDDLE = 1u << 25;
        public const uint FAR = 1u << 26;
        public const uint ALL = NEAR | MIDDLE | FAR;

        public static uint GetMask(PixelRenderGroup group)
        {
            return group switch
            {
                PixelRenderGroup.Near => NEAR,
                PixelRenderGroup.Middle => MIDDLE,
                PixelRenderGroup.Far => FAR,
                _ => 0u
            };
        }
    }
}
