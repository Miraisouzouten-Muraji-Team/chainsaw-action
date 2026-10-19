
using UnityEngine;

namespace Project.Rendering.Pixel
{
    public enum PixelCaptureView
    {
        Game,
        Scene
    }

    /// <summary>
    /// Capture Cameraの用途と対象グループを保持する。
    /// Depth CaptureやEditorプレビューから参照する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelCaptureViewMarker : MonoBehaviour
    {
        [SerializeField] private PixelCaptureView _view = PixelCaptureView.Game;
        [SerializeField] private int _captureIndex = -1;

        public PixelCaptureView View => _view;
        public int CaptureIndex => _captureIndex;

        public void SetCapture(PixelCaptureView view, int captureIndex)
        {
            if (captureIndex < 0 || captureIndex > 2)
            {
                Debug.LogError($"Capture Indexが不正です: {captureIndex}", this);
                return;
            }

            _view = view;
            _captureIndex = captureIndex;
        }
    }
}
