
using UnityEngine;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// ピクセル描画時の解像度グループ。
    /// Autoの場合はカメラとの距離で決定する。
    /// </summary>
    public enum PixelRenderGroup
    {
        Auto,
        Near,
        Middle,
        Far
    }

    /// <summary>
    /// 3Dオブジェクトの描画先を指定する。
    /// 親の設定は子に継承され、子の個別設定を優先する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PixelRenderOverride : MonoBehaviour
    {
        [SerializeField] private PixelRenderGroup _group = PixelRenderGroup.Auto;

        public PixelRenderGroup Group => _group;

        /// <summary>
        /// 実行中に描画グループを変更する。
        /// </summary>
        public void SetGroup(PixelRenderGroup group)
        {
            if (_group == group) return;
            _group = group;
            NotifyManager();
        }

        private void OnEnable()
        {
            NotifyManager();
        }

        private void OnTransformChildrenChanged()
        {
            NotifyManager();
        }

        private void OnTransformParentChanged()
        {
            NotifyManager();
        }

        private void OnValidate()
        {
            if (Application.isPlaying) NotifyManager();
        }

        /// <summary>
        /// 自身と子のRendererをManagerへ登録する。
        /// </summary>
        private void NotifyManager()
        {
            PixelRenderLayerManager manager = FindFirstObjectByType<PixelRenderLayerManager>();
            if (manager != null) manager.RegisterHierarchy(transform);
        }
    }
}
