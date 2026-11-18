using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RiftPrototypeA
{
    /// <summary>方式A専用の透視投影カメラ。移動ごとに3Dシーンを描画する。</summary>
    public sealed class RiftAPreviewCamera : MonoBehaviour
    {
        [Header("専用RendererのIndex（URP AssetのRenderer一覧）")]
        [SerializeField, Min(0)] private int rendererIndex;
        [SerializeField, Range(25f, 100f)] private float fieldOfView = 60f;
        [SerializeField, Range(0.25f, 1f)] private float resolutionScale = 1f;
        [SerializeField, Min(0.01f)] private float nearClip = 0.1f;
        [SerializeField, Min(10f)] private float farClip = 200f;
        [Tooltip("UIレイヤーを除外。必要に応じてステージ専用Layerだけにしてください。")]
        [SerializeField] private LayerMask cullingMask = ~(1 << 5);

        private Camera captureCamera;
        private RenderTexture renderTexture;
        private Transform startPoint;
        private Transform endPoint;

        public RenderTexture RenderTexture => renderTexture;

        public void Prepare(RiftAStageEntry entry)
        {
            if (entry == null || entry.StartPoint == null || entry.EndPoint == null)
                throw new InvalidOperationException("RiftAStageEntryのStartPoint/EndPointを設定してください。");

            Release();
            startPoint = entry.StartPoint;
            endPoint = entry.EndPoint;

            int width = Mathf.Max(32, Mathf.RoundToInt(Screen.width * resolutionScale));
            int height = Mathf.Max(32, Mathf.RoundToInt(Screen.height * resolutionScale));
            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "RiftA_Preview_Color",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            renderTexture.Create();

            GameObject cameraObject = new GameObject("RiftA_CaptureCamera_Runtime");
            SceneManager.MoveGameObjectToScene(cameraObject, gameObject.scene);
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = false;
            captureCamera.fieldOfView = fieldOfView;
            captureCamera.aspect = (float)width / height;
            captureCamera.nearClipPlane = nearClip;
            captureCamera.farClipPlane = farClip;
            captureCamera.cullingMask = cullingMask;
            captureCamera.clearFlags = CameraClearFlags.Skybox;
            captureCamera.targetTexture = renderTexture;
            captureCamera.allowHDR = true;

            UniversalAdditionalCameraData cameraData = captureCamera.GetUniversalAdditionalCameraData();
            cameraData.SetRenderer(rendererIndex);
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;
            MoveTo(0f);
            captureCamera.enabled = true;
        }

        public void MoveTo(float progress)
        {
            if (captureCamera == null || startPoint == null || endPoint == null) return;
            float t = Mathf.Clamp01(progress);
            captureCamera.transform.SetPositionAndRotation(
                Vector3.Lerp(startPoint.position, endPoint.position, t),
                Quaternion.Slerp(startPoint.rotation, endPoint.rotation, t));
        }

        public void StopRendering()
        {
            if (captureCamera != null) captureCamera.enabled = false;
        }

        public void Release()
        {
            if (captureCamera != null)
            {
                captureCamera.targetTexture = null;
                Destroy(captureCamera.gameObject);
                captureCamera = null;
            }
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
            startPoint = null;
            endPoint = null;
        }

        private void OnDestroy() => Release();
    }
}
