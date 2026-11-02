using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Project.Tutorial
{
    // 最終画面の色は再描画せず、看板だけをプレイヤー・指定ステージ・登録された敵のシルエットで抜く。
    [DefaultExecutionOrder(20000)]
    public sealed class TutorialSignOcclusion : MonoBehaviour
    {
        private TutorialPlayer player;
        private TutorialZone zone;
        private const float DISCOVERY_INTERVAL = 0.5f;
        private readonly HashSet<Renderer> renderers = new HashSet<Renderer>();
        private readonly HashSet<Renderer> playerRenderers = new HashSet<Renderer>();
        private readonly List<Renderer> playerChildren = new List<Renderer>();
        private readonly List<Renderer> staleRenderers = new List<Renderer>();
        private float nextDiscovery;
        private Material maskMaterial, uiMaterial;
        private RenderTexture mask;
        private CommandBuffer commands;
        private readonly Dictionary<Graphic, Material> originals = new Dictionary<Graphic, Material>();
        private bool initialized;

        // 表示対象とZoneを設定し、変更時に対象一覧の再取得を予約する。
        internal void Configure(TutorialPlayer target, TutorialZone source)
        {
            if (player != target || zone != source) nextDiscovery = 0f;
            player = target;
            zone = source;
        }
        // 切り抜き用のシェーダーと描画リソースを初回だけ準備する。
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
        // アニメーションとカメラ移動後の形状で、看板を切り抜くマスクを更新する。
        private void LateUpdate()
        {
            Initialize();
            if (uiMaterial == null) return;
            if (zone == null)
            { uiMaterial.SetTexture("_PlayerMask", Texture2D.blackTexture); return; }
            Camera camera = zone.projectionCamera != null ? zone.projectionCamera : Camera.main;
            if (camera == null) { uiMaterial.SetTexture("_PlayerMask", Texture2D.blackTexture); return; }
            if (Time.unscaledTime >= nextDiscovery)
            {
                CollectOccluders();
                nextDiscovery = Time.unscaledTime + DISCOVERY_INTERVAL;
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
            // プレイヤーは毎フレーム確認し、敵の参照切れや再取得間隔に依存させない。
            CollectPlayerOccluders();
            foreach (Renderer renderer in playerRenderers) RecordRenderer(renderer);
            staleRenderers.Clear();
            foreach (Renderer renderer in renderers)
            {
                if (playerRenderers.Contains(renderer)) continue;
                if (!RecordRenderer(renderer)) staleRenderers.Add(renderer);
            }
            foreach (Renderer renderer in staleRenderers) renderers.Remove(renderer);
            var previous = RenderTexture.active;
            try { Graphics.ExecuteCommandBuffer(commands); }
            finally { RenderTexture.active = previous; }
            uiMaterial.SetTexture("_PlayerMask", mask);
            uiMaterial.SetFloat("_FlipMaskY", player != null && player.flipTutorialMaskY ? 1f : 0f);
        }
        // 生存しているRendererだけを記録し、参照切れが他の対象の描画を止めないようにする。
        private bool RecordRenderer(Renderer renderer)
        {
            if (renderer == null) return false;
            try
            {
                if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) return true;
                Mesh mesh = null;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned == null) return false;
                    mesh = skinned.sharedMesh;
                }
                else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter))
                {
                    if (filter == null) return false;
                    mesh = filter.sharedMesh;
                }
                if (mesh == null) return true;
                int subMeshCount = mesh.subMeshCount;
                for (int i = 0; i < subMeshCount; i++)
                {
                    if (renderer == null) return false;
                    commands.DrawRenderer(renderer, maskMaterial, i, 0);
                }
                return true;
            }
            catch (MissingReferenceException)
            {
                // モデルの破棄・差し替え中はこの対象だけを除外し、次回の収集を予約する。
                nextDiscovery = 0f;
                return false;
            }
        }

        // プレイヤーの明示指定を優先し、全参照が失われた場合は現在の子モデルを再取得する。
        private void CollectPlayerOccluders()
        {
            playerRenderers.Clear();
            if (player == null || !player.hideTutorialBehindPlayer) return;
            if (player.tutorialOccluders != null)
                foreach (Renderer renderer in player.tutorialOccluders)
                    if (renderer != null) playerRenderers.Add(renderer);
            if (playerRenderers.Count > 0) return;
            playerChildren.Clear();
            player.GetComponentsInChildren<Renderer>(true, playerChildren);
            foreach (Renderer renderer in playerChildren)
                if (renderer != null && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                    playerRenderers.Add(renderer);
        }

        // 登録された敵・明示指定されたステージを集め、重複描画を防ぐ。
        private void CollectOccluders()
        {
            renderers.Clear();
            foreach (TutorialEnemyOccluder enemy in TutorialEnemyOccluder.Active)
            {
                if (enemy == null || !enemy.isActiveAndEnabled) continue;
                enemy.CollectRenderers(renderers);
            }
            if (zone == null) return;
            if (zone.stageOccluders != null)
                foreach (Renderer renderer in zone.stageOccluders)
                    if (renderer != null) renderers.Add(renderer);
            if (zone.stageOccluderRoots != null)
                foreach (Transform root in zone.stageOccluderRoots)
                {
                    if (root == null) continue;
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                        if (renderer != null) renderers.Add(renderer);
                }
        }

        // 看板の非表示時にマスクを解放する。
        private void OnDisable()
        {
            if (uiMaterial != null) uiMaterial.SetTexture("_PlayerMask", Texture2D.blackTexture);
            ReleaseMask();
        }
        // 生成したRenderTextureを解放する。
        private void ReleaseMask()
        {
            if (mask == null) return;
            mask.Release(); Destroy(mask); mask = null;
        }
        // UIのマテリアルを戻し、生成した描画リソースを破棄する。
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
