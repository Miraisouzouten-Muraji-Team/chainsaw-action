using System.Collections.Generic;
using UnityEngine;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// 3D RendererのPixel描画先を管理する。
    /// GameObject.layerは変更せず、Rendering Layerを使用する。
    /// </summary>
    public sealed class PixelRenderRoutingManager : MonoBehaviour
    {
        private const float RENDERER_REFRESH_INTERVAL = 0.5f;

        [Header("Camera")]
        [SerializeField] private Camera _targetCamera;

        [Header("Distance")]
        [SerializeField, Min(0f)] private float _nearDistance = 10f;
        [SerializeField, Min(0f)] private float _farDistance = 30f;

        private readonly List<RendererEntry> _entries = new();
        private float _nextRendererRefreshTime;

        /// <summary>
        /// Rendererと、そのRendererに適用されるOverrideを保持する。
        /// </summary>
        private sealed class RendererEntry
        {
            public Renderer Renderer;
            public PixelRenderOverride Override;
        }

        private void OnEnable()
        {
            RefreshRenderers();
            _nextRendererRefreshTime = Time.unscaledTime + RENDERER_REFRESH_INTERVAL;
        }

        private void LateUpdate()
        {
            if (_targetCamera == null)
            {
                return;
            }

            // 動的に生成されたRendererも一定間隔で登録する。
            if (Time.unscaledTime >= _nextRendererRefreshTime)
            {
                RefreshRenderers();
                _nextRendererRefreshTime = Time.unscaledTime + RENDERER_REFRESH_INTERVAL;
            }

            Vector3 cameraPosition = _targetCamera.transform.position;

            foreach (RendererEntry entry in _entries)
            {
                if (entry.Renderer == null || !entry.Renderer.enabled)
                {
                    continue;
                }

                // 手動設定または距離によって描画先を決定する。
                PixelRenderGroup group = ResolveGroup(entry.Renderer, entry.Override, cameraPosition);

                // GameObject.layerを変更せず、Rendering Layerだけ更新する。
                ApplyRenderingLayer(entry.Renderer, group);
            }
        }

        /// <summary>
        /// シーン内の3D Rendererを収集する。
        /// Canvas配下のUIは管理対象から除外する。
        /// </summary>
        [ContextMenu("Refresh Pixel Renderers")]
        public void RefreshRenderers()
        {
            _entries.Clear();

            Renderer[] renderers = FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            foreach (Renderer renderer in renderers)
            {
                // Canvas配下はPixel描画先の振り分けを行わない。
                if (renderer.GetComponentInParent<Canvas>() != null)
                {
                    continue;
                }

                // 最も近い親のOverrideを使用する。
                // 子にOverrideがあれば親より優先される。
                PixelRenderOverride renderOverride = renderer.GetComponentInParent<PixelRenderOverride>();

                _entries.Add(new RendererEntry
                {
                    Renderer = renderer,
                    Override = renderOverride
                });
            }
        }

        /// <summary>
        /// 手動設定を優先し、Autoの場合はカメラとの距離で描画先を決める。
        /// </summary>
        private PixelRenderGroup ResolveGroup(Renderer renderer, PixelRenderOverride renderOverride, Vector3 cameraPosition)
        {
            if (renderOverride != null && renderOverride.isActiveAndEnabled && renderOverride.Group != PixelRenderGroup.Auto)
            {
                return renderOverride.Group;
            }

            // RendererのBoundsを使用し、オブジェクトの大きさも考慮して距離を計算する。
            Vector3 closestPoint = renderer.bounds.ClosestPoint(cameraPosition);
            float distance = Vector3.Distance(cameraPosition, closestPoint);

            if (distance < _nearDistance)
            {
                return PixelRenderGroup.Near;
            }

            if (distance < _farDistance)
            {
                return PixelRenderGroup.Middle;
            }

            return PixelRenderGroup.Far;
        }

        /// <summary>
        /// 描画グループに対応するRendering Layerを設定する。
        /// Pixel描画用以外のRendering Layerのビットは維持する。
        /// </summary>
        private static void ApplyRenderingLayer(Renderer renderer, PixelRenderGroup group)
        {
            uint currentMask = renderer.renderingLayerMask;
            uint groupMask = PixelRenderingLayerMask.GetMask(group);

            // 既存のPixel描画用ビットだけ消去し、新しいグループを設定する。
            uint updatedMask = (currentMask & ~PixelRenderingLayerMask.ALL) | groupMask;

            if (currentMask != updatedMask)
            {
                renderer.renderingLayerMask = updatedMask;
            }
        }

        private void OnDisable()
        {
            // 管理を終了するときはPixel描画用ビットを解除する。
            foreach (RendererEntry entry in _entries)
            {
                if (entry.Renderer != null)
                {
                    entry.Renderer.renderingLayerMask &= ~PixelRenderingLayerMask.ALL;
                }
            }

            _entries.Clear();
        }
    }
}
