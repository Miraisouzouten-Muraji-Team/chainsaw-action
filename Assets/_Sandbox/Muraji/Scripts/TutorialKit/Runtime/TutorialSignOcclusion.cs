using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Project.Tutorial
{
    // 最終画面の色は再描画せず、看板だけをプレイヤーのシルエットで抜く。
    [DefaultExecutionOrder(20000)]
    public sealed class TutorialSignOcclusion : MonoBehaviour
    {
        private TutorialPlayer player;
        private TutorialZone zone;
        private Renderer[] renderers;
        private float nextDiscovery;
        private Material maskMaterial, uiMaterial;
        private RenderTexture mask;
        private CommandBuffer commands;
        private readonly Dictionary<Graphic, Material> originals = new Dictionary<Graphic, Material>();
        private bool initialized;

        internal void Configure(TutorialPlayer target, TutorialZone source)
        {
            if (player != target) { player = target; renderers = null; nextDiscovery = 0f; }
            zone = source;
        }
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Shader maskShader = Resources.Load<Shader>("Tutorial/PlayerSilhouette");
            Shader uiShader = Resources.Load<Shader>("Tutorial/OccludedUI");
            if (maskShader == null || uiShader == null || !maskShader.isSupported || !uiShader.isSupported)
            {
                Debug.LogError("Tutorial: Resources/Tutorial内の2つのShaderが必要です。", this);
                return;
            }
            maskMaterial = new Material(maskShader) { hideFlags = HideFlags.HideAndDontSave };
            uiMaterial = new Material(uiShader) { hideFlags = HideFlags.HideAndDontSave };
            uiMaterial.SetTexture("_PlayerMask", Texture2D.blackTexture);
            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            {
                originals.Add(graphic, graphic.material);
                graphic.material = uiMaterial;
            }
            commands = new CommandBuffer { name = "Tutorial Player Silhouette" };
        }
        private void LateUpdate()
        {
            Initialize();
            if (uiMaterial == null) return;
            if (player == null || zone == null || !player.hideTutorialBehindPlayer)
            { uiMaterial.SetTexture("_PlayerMask", Texture2D.blackTexture); return; }
            Camera camera = zone.projectionCamera != null ? zone.projectionCamera : Camera.main;
            if (camera == null) { uiMaterial.SetTexture("_PlayerMask", Texture2D.blackTexture); return; }
            if (renderers == null || Time.unscaledTime >= nextDiscovery)
            {
                renderers = player.tutorialOccluders != null && player.tutorialOccluders.Length > 0
                    ? player.tutorialOccluders : player.GetComponentsInChildren<Renderer>(true);
                nextDiscovery = Time.unscaledTime + .5f;
            }
            int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
            if (mask == null || mask.width != width || mask.height != height)
            {
                ReleaseMask();
                mask = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                {
                    name = "Tutorial Player Mask",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                    antiAliasing = 1
                };
                mask.Create();
            }
            Rect viewport = zone.outputViewport;
            if (camera.targetTexture == null && viewport == new Rect(0, 0, 1, 1)) viewport = camera.rect;
            commands.Clear();
            commands.SetRenderTarget(mask);
            commands.ClearRenderTarget(false, true, Color.clear);
            commands.SetViewport(new Rect(viewport.x * width, viewport.y * height, viewport.width * width, viewport.height * height));
            // 独自CameraのCullingMaskやRendering Layerによる振り分けを経由しない。
            commands.SetGlobalMatrix("_TutorialMaskVP", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                if (mesh == null) continue;
                for (int i = 0; i < mesh.subMeshCount; i++) commands.DrawRenderer(renderer, maskMaterial, i, 0);
            }
            var previous = RenderTexture.active;
            Graphics.ExecuteCommandBuffer(commands);
            RenderTexture.active = previous;
            uiMaterial.SetTexture("_PlayerMask", mask);
            uiMaterial.SetFloat("_FlipMaskY", player.flipTutorialMaskY ? 1f : 0f);
        }
        private void OnDisable()
        {
            if (uiMaterial != null) uiMaterial.SetTexture("_PlayerMask", Texture2D.blackTexture);
            ReleaseMask();
        }
        private void ReleaseMask()
        {
            if (mask == null) return;
            mask.Release(); Destroy(mask); mask = null;
        }
        private void OnDestroy()
        {
            foreach (var entry in originals)
                if (entry.Key != null) entry.Key.material = entry.Value;
            commands?.Release();
            ReleaseMask();
            if (maskMaterial != null) Destroy(maskMaterial);
            if (uiMaterial != null) Destroy(uiMaterial);
        }
    }
}
