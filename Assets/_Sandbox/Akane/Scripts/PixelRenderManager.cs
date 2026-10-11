using System.Collections.Generic;
using UnityEngine;

namespace Project.Rendering.Pixel
{
    public sealed class PixelRenderLayerManager : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField]
        private Camera _targetCamera;

        [Header("Distance")]
        [SerializeField]
        private float _nearDistance = 10.0f;

        [SerializeField]
        private float _farDistance = 30.0f;

        [Header("Layer Names")]
        [SerializeField]
        private string _nearLayerName = "PixelNear";

        [SerializeField]
        private string _middleLayerName = "PixelMiddle";

        [SerializeField]
        private string _farLayerName = "PixelFar";

        private readonly List<RendererEntry> _entries = new();

        private int _nearLayer;
        private int _middleLayer;
        private int _farLayer;

        private sealed class RendererEntry
        {
            public Renderer Renderer;
            public PixelRenderOverride Override;
        }

        private void Awake()
        {
            InitializeLayers();
            CollectRenderers();
        }

        private void LateUpdate()
        {
            UpdateRenderLayers();
        }

        private void InitializeLayers()
        {
            _nearLayer = LayerMask.NameToLayer(_nearLayerName);
            _middleLayer = LayerMask.NameToLayer(_middleLayerName);
            _farLayer = LayerMask.NameToLayer(_farLayerName);

            if (_nearLayer < 0 ||
                _middleLayer < 0 ||
                _farLayer < 0)
            {
                Debug.LogError(
                    "Pixel描画用Layerが見つかりません。" +
                    "PixelNear / PixelMiddle / PixelFar を確認してください。",
                    this);
            }
        }

        private void CollectRenderers()
        {
            _entries.Clear();

            Renderer[] renderers =
                FindObjectsByType<Renderer>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (Renderer renderer in renderers)
            {
                PixelRenderOverride renderOverride =
                    renderer.GetComponentInParent<PixelRenderOverride>();

                _entries.Add(new RendererEntry
                {
                    Renderer = renderer,
                    Override = renderOverride
                });
            }
        }

        private void UpdateRenderLayers()
        {
            if (_targetCamera == null)
            {
                return;
            }

            Vector3 cameraPosition =
                _targetCamera.transform.position;

            foreach (RendererEntry entry in _entries)
            {
                Renderer renderer = entry.Renderer;

                if (renderer == null)
                {
                    continue;
                }

                PixelRenderGroup group =
                    ResolveGroup(
                        renderer,
                        entry.Override,
                        cameraPosition);

                ApplyLayer(renderer.gameObject, group);
            }
        }

        private PixelRenderGroup ResolveGroup(
            Renderer renderer,
            PixelRenderOverride renderOverride,
            Vector3 cameraPosition)
        {
            // 手動設定されていればそちらを優先。
            if (renderOverride != null &&
                renderOverride.Group != PixelRenderGroup.Auto)
            {
                return renderOverride.Group;
            }

            // RendererのBounds上でカメラに最も近い位置を使う。
            Vector3 closestPoint =
                renderer.bounds.ClosestPoint(cameraPosition);

            float distance =
                Vector3.Distance(
                    cameraPosition,
                    closestPoint);

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

        private void ApplyLayer(
            GameObject target,
            PixelRenderGroup group)
        {
            int layer = group switch
            {
                PixelRenderGroup.Near => _nearLayer,
                PixelRenderGroup.Middle => _middleLayer,
                PixelRenderGroup.Far => _farLayer,

                _ => _middleLayer
            };

            if (layer >= 0 && target.layer != layer)
            {
                target.layer = layer;
            }
        }
    }
}