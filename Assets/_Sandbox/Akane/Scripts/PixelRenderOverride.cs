using UnityEngine;

namespace Project.Rendering.Pixel
{
    public enum PixelRenderGroup
    {
        Auto,
        Near,
        Middle,
        Far
    }

    public sealed class PixelRenderOverride : MonoBehaviour
    {
        [SerializeField]
        private PixelRenderGroup _group = PixelRenderGroup.Auto;

        public PixelRenderGroup Group => _group;
    }
}