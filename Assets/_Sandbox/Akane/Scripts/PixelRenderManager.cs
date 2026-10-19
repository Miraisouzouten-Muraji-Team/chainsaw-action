
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

        // Play中に変更したRendererの元のRendering Layerを保存する。
        private readonly Dictionary<Renderer, uint> _originalMasks = new();

        private float _nextDiscoveryTime;

        private void OnEnable()
        {
            DiscoverRenderers();
            _nextDiscoveryTime = 0f;
        }

        private void LateUpdate()
        {
            // Edit Modeの更新はEditor Capture側で行う。
            if (!Application.isPlaying || _targetCamera == null) return;

            // Overrideがない動的生成物も一定間隔で収集する。
            if (Time.unscaledTime >= _nextDiscoveryTime)
            {
                DiscoverRenderers();
                _nextDiscoveryTime = Time.unscaledTime + _discoveryInterval;
            }

            RefreshForCamera(_targetCamera);
        }

        /// <summary>
        /// 自身と子のRendererを登録する。
        /// Play中は登録直後に描画先を反映する。
        /// </summary>
        public void RegisterHierarchy(Transform root)
        {
            if (root == null) return;

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

            foreach (Renderer renderer in renderers)
            {
                if (!IsSupported(renderer)) continue;

                _renderers.Add(renderer);

                // EditorではCapture時以外にRendering Layerを変更しない。
                if (Application.isPlaying &&
                    isActiveAndEnabled &&
                    _targetCamera != null)
                {
                    RefreshRenderer(renderer, _targetCamera.transform.position);
                }
            }
        }

        /// <summary>
        /// シーン内の対応Rendererを収集する。
        /// </summary>
        [ContextMenu("Refresh Pixel Renderers")]
        public void DiscoverRenderers()
        {
            Renderer[] renderers = FindObjectsByType<Renderer>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (Renderer renderer in renderers)
            {
                if (IsSupported(renderer))
                {
                    _renderers.Add(renderer);
                }
            }
        }

        /// <summary>
        /// Editor Capture用にRendererの収集と振り分けを行う。
        /// </summary>
        public void DiscoverAndRefresh(Camera camera)
        {
            DiscoverRenderers();
            RefreshForCamera(camera);
        }

        /// <summary>
        /// 指定Cameraの位置を基準に全登録Rendererを更新する。
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

                RefreshRenderer(renderer, cameraPosition);
            }

            // 破棄されたRendererの参照を解放する。
            foreach (Renderer renderer in _removedRenderers)
            {
                _renderers.Remove(renderer);
                _originalMasks.Remove(renderer);
            }
        }

        /// <summary>
        /// 単一Rendererの描画先を決定して適用する。
        /// </summary>
        private void RefreshRenderer(Renderer renderer, Vector3 cameraPosition)
        {
            if (renderer == null) return;

            // Canvas配下のRendererはPixel振り分け対象外にする。
            if (renderer.GetComponentInParent<Canvas>(true) != null)
            {
                if (Application.isPlaying)
                {
                    RestoreOriginalPixelBits(renderer);
                }

                return;
            }

            PixelRenderOverride renderOverride = FindOverride(renderer.transform);
            PixelRenderGroup group = ResolveGroup(renderer, renderOverride, cameraPosition);
            ApplyRenderingLayer(renderer, group);
        }

        /// <summary>
        /// 最も近い有効なOverrideを取得する。
        /// 子のAuto指定も親の指定より優先する。
        /// </summary>
        private static PixelRenderOverride FindOverride(Transform target)
        {
            Transform current = target;

            while (current != null)
            {
                if (current.TryGetComponent(out PixelRenderOverride renderOverride) &&
                    renderOverride.isActiveAndEnabled)
                {
                    return renderOverride;
                }

                current = current.parent;
            }

            return null;
        }

        /// <summary>
        /// 手動指定を優先し、Autoの場合は距離で判定する。
        /// </summary>
        private PixelRenderGroup ResolveGroup(
            Renderer renderer,
            PixelRenderOverride renderOverride,
            Vector3 cameraPosition)
        {
            if (renderOverride != null && renderOverride.Group != PixelRenderGroup.Auto)
            {
                return renderOverride.Group;
            }

            Vector3 closestPoint = renderer.bounds.ClosestPoint(cameraPosition);
            float distance = Vector3.Distance(cameraPosition, closestPoint);

            if (distance < _nearDistance) return PixelRenderGroup.Near;
            if (distance < _farDistance) return PixelRenderGroup.Middle;
            return PixelRenderGroup.Far;
        }

        /// <summary>
        /// Pixel専用ビットのみを更新する。
        /// Play中は変更前のビットを保存する。
        /// </summary>
        private void ApplyRenderingLayer(Renderer renderer, PixelRenderGroup group)
        {
            uint currentMask = renderer.renderingLayerMask;
            uint groupMask = PixelRenderingLayerMask.GetMask(group);
            uint updatedMask = (currentMask & ~PixelRenderingLayerMask.ALL) | groupMask;

            if (currentMask == updatedMask) return;

            if (Application.isPlaying && !_originalMasks.ContainsKey(renderer))
            {
                _originalMasks.Add(renderer, currentMask);
            }

            renderer.renderingLayerMask = updatedMask;
        }

        /// <summary>
        /// Managerが変更したPixel専用ビットを元に戻す。
        /// 他のRendering Layerの変更は維持する。
        /// </summary>
        private void RestoreOriginalPixelBits(Renderer renderer)
        {
            if (renderer == null ||
                !_originalMasks.TryGetValue(renderer, out uint originalMask))
            {
                return;
            }

            uint currentMask = renderer.renderingLayerMask;
            uint restoredMask =
                (currentMask & ~PixelRenderingLayerMask.ALL) |
                (originalMask & PixelRenderingLayerMask.ALL);

            if (currentMask != restoredMask)
            {
                renderer.renderingLayerMask = restoredMask;
            }

            _originalMasks.Remove(renderer);
        }

        /// <summary>
        /// Pixel描画対象として扱うRendererを判定する。
        /// </summary>
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
            // Edit ModeではPrefabやシーンの設定を書き換えない。
            if (Application.isPlaying)
            {
                foreach (KeyValuePair<Renderer, uint> entry in _originalMasks)
                {
                    Renderer renderer = entry.Key;
                    if (renderer == null) continue;

                    uint currentMask = renderer.renderingLayerMask;
                    uint restoredMask =
                        (currentMask & ~PixelRenderingLayerMask.ALL) |
                        (entry.Value & PixelRenderingLayerMask.ALL);

                    if (currentMask != restoredMask)
                    {
                        renderer.renderingLayerMask = restoredMask;
                    }
                }
            }

            _originalMasks.Clear();
            _removedRenderers.Clear();
            _renderers.Clear();
        }
    }
}
