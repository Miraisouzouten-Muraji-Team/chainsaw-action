using System.Collections.Generic;
using UnityEngine;

namespace Project.Rendering.Pixel
{
    /// <summary>
    /// Renderer単位でPixel描画先を管理する。
    /// GameObject.layerは変更せず、Rendering Layerを使用する。
    /// </summary>
    [ExecuteAlways]
    public sealed class PixelRenderLayerManager : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Camera _targetCamera;

        [Header("Distance")]
        [SerializeField, Min(0f)] private float _nearDistance = 10f;
        [SerializeField, Min(0f)] private float _farDistance = 30f;

        [Header("Dynamic Renderer Discovery")]
        [SerializeField, Min(0.01f)] private float _discoveryInterval = 0.25f;

        private readonly HashSet<Renderer> _renderers = new();
        private readonly List<Renderer> _removedRenderers = new();
        private float _nextDiscoveryTime;

        private void OnEnable()
        {
            DiscoverRenderers();
            _nextDiscoveryTime = 0f;
        }

        private void LateUpdate()
        {
            // Edit Modeの更新はEditorのCapture処理から行う。
            if (!Application.isPlaying || _targetCamera == null) return;

            // Overrideが付いていない動的生成物も定期的に収集する。
            if (Time.unscaledTime >= _nextDiscoveryTime)
            {
                DiscoverRenderers();
                _nextDiscoveryTime = Time.unscaledTime + _discoveryInterval;
            }

            RefreshForCamera(_targetCamera);
        }

        /// <summary>
        /// 自身と子の3D Rendererを登録する。
        /// </summary>
        public void RegisterHierarchy(Transform root)
        {
            if (root == null) return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                if (IsSupported(renderer)) _renderers.Add(renderer);
            }
        }

        /// <summary>
        /// シーン内のRendererを収集する。
        /// 非アクティブなオブジェクトも対象にする。
        /// </summary>
        [ContextMenu("Refresh Pixel Renderers")]
        public void DiscoverRenderers()
        {
            Renderer[] renderers = FindObjectsByType<Renderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (Renderer renderer in renderers)
            {
                if (IsSupported(renderer)) _renderers.Add(renderer);
            }
        }

        /// <summary>
        /// EditorプレビューなどでRendererの収集と更新をまとめて行う。
        /// </summary>
        public void DiscoverAndRefresh(Camera camera)
        {
            DiscoverRenderers();
            RefreshForCamera(camera);
        }

        /// <summary>
        /// 指定したCameraを基準に描画グループを更新する。
        /// </summary>
        public void RefreshForCamera(Camera camera)
        {
            if (camera == null || !isActiveAndEnabled) return;

            Vector3 cameraPosition = camera.transform.position;
            _removedRenderers.Clear();

            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null)
                {
                    _removedRenderers.Add(renderer);
                    continue;
                }

                // Canvas配下はPixel描画の振り分け対象から除外する。
                if (renderer.GetComponentInParent<Canvas>(true) != null)
                {
                    renderer.renderingLayerMask &= ~PixelRenderingLayerMask.ALL;
                    continue;
                }

                PixelRenderOverride renderOverride = FindOverride(renderer.transform);
                PixelRenderGroup group = ResolveGroup(renderer, renderOverride, cameraPosition);
                ApplyRenderingLayer(renderer, group);
            }

            // 削除されたRendererへの参照を取り除く。
            foreach (Renderer renderer in _removedRenderers)
            {
                _renderers.Remove(renderer);
            }
        }

        /// <summary>
        /// 子から親へ検索し、最も近い有効なOverrideを取得する。
        /// Autoも子側で指定された場合は親の設定を上書きする。
        /// </summary>
        private static PixelRenderOverride FindOverride(Transform target)
        {
            Transform current = target;

            while (current != null)
            {
                if (current.TryGetComponent(out PixelRenderOverride renderOverride) && renderOverride.isActiveAndEnabled)
                {
                    return renderOverride;
                }

                current = current.parent;
            }

            return null;
        }

        /// <summary>
        /// 手動指定を優先し、Autoの場合は距離によって判定する。
        /// </summary>
        private PixelRenderGroup ResolveGroup(Renderer renderer, PixelRenderOverride renderOverride, Vector3 cameraPosition)
        {
            if (renderOverride != null && renderOverride.Group != PixelRenderGroup.Auto)
            {
                return renderOverride.Group;
            }

            // RendererのBoundsからカメラに最も近い座標を求める。
            Vector3 closestPoint = renderer.bounds.ClosestPoint(cameraPosition);
            float distance = Vector3.Distance(cameraPosition, closestPoint);

            if (distance < _nearDistance) return PixelRenderGroup.Near;
            if (distance < _farDistance) return PixelRenderGroup.Middle;
            return PixelRenderGroup.Far;
        }

        /// <summary>
        /// Pixel描画用ビットだけを書き換える。
        /// 他のRendering Layerは維持する。
        /// </summary>
        private static void ApplyRenderingLayer(Renderer renderer, PixelRenderGroup group)
        {
            uint currentMask = renderer.renderingLayerMask;
            uint groupMask = PixelRenderingLayerMask.GetMask(group);
            uint updatedMask = (currentMask & ~PixelRenderingLayerMask.ALL) | groupMask;

            if (currentMask != updatedMask)
            {
                renderer.renderingLayerMask = updatedMask;
            }
        }

        private static bool IsSupported(Renderer renderer)
        {
            return renderer is MeshRenderer ||
                   renderer is SkinnedMeshRenderer ||
                   renderer is ParticleSystemRenderer ||
                   renderer is TrailRenderer ||
                   renderer is LineRenderer;
        }

        private void OnValidate()
        {
            _nearDistance = Mathf.Max(0f, _nearDistance);
            _farDistance = Mathf.Max(_nearDistance, _farDistance);
            _discoveryInterval = Mathf.Max(0.01f, _discoveryInterval);
        }

        private void OnDisable()
        {
            // 管理終了時にPixel専用ビットを解除する。
            foreach (Renderer renderer in _renderers)
            {
                if (renderer == null) continue;
                renderer.renderingLayerMask &= ~PixelRenderingLayerMask.ALL;
            }

            _renderers.Clear();
        }
    }
}
