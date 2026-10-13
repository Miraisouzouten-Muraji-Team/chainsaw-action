using UnityEngine;

namespace Project.Rendering.Pixel
{
    public enum PixelCaptureView
    {
        Game,
        Scene
    }

    /// <summary>
    /// Capture CameraがGame用かScene用かを識別する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelCaptureViewMarker : MonoBehaviour
    {
        [SerializeField]
        private PixelCaptureView _view = PixelCaptureView.Game;

        public PixelCaptureView View => _view;

        public void SetView(PixelCaptureView view)
        {
            _view = view;
        }
    }
}
