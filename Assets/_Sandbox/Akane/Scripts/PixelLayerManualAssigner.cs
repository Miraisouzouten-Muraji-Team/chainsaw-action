
using UnityEngine;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Inspectorから指定されたPixel Layerを、
    /// 自身と子オブジェクトへ手動で適用する。
    /// 実行時に自動更新は行わない。
    /// </summary>
    public sealed class PixelLayerManualAssigner : MonoBehaviour
    {
        private const string NEAR_LAYER_NAME = "PixelNear";
        private const string MIDDLE_LAYER_NAME = "PixelMiddle";
        private const string FAR_LAYER_NAME = "PixelFar";

        [SerializeField] private PixelRenderGroup _group = PixelRenderGroup.Near;

        [ContextMenu("Apply Pixel Layer")]
        public void ApplyPixelLayer()
        {
            string layerName = _group switch
            {
                PixelRenderGroup.Near => NEAR_LAYER_NAME,
                PixelRenderGroup.Middle => MIDDLE_LAYER_NAME,
                PixelRenderGroup.Far => FAR_LAYER_NAME,
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(layerName))
            {
                Debug.LogWarning("Pixel Render GroupをNear / Middle / Farから選択してください。", this);
                return;
            }

            int layer = LayerMask.NameToLayer(layerName);

            if (layer < 0)
            {
                Debug.LogError($"Layerが存在しません: {layerName}", this);
                return;
            }

            // 親オブジェクト自身も変更する。
            gameObject.layer = layer;

            // 子の描画オブジェクトにLayerを適用する。
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                renderer.gameObject.layer = layer;
            }

            Debug.Log($"Pixel Layerを適用しました: {layerName}", this);
        }
    }
}
