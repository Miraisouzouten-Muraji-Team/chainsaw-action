
using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace RiftPrototypeA
{
    /// <summary>
    /// 方式A専用のプレビューカメラ。
    /// 透視投影でステージへ進入し、
    /// 最後に本編の平行投影画面へ構図を近づける。
    /// </summary>
    public sealed class RiftAPreviewCamera : MonoBehaviour
    {
        private const string RIFT_FRAGMENT_LAYER = "RiftAFragments";

        [Header("描画設定")]
        [SerializeField, Min(0)] private int rendererIndex = 0;
        [SerializeField, Range(1f, 100f)] private float fieldOfView = 60f;
        [SerializeField, Range(0.25f, 1f)] private float resolutionScale = 1f;
        [SerializeField, Min(0.01f)] private float nearClip = 0.1f;
        [SerializeField, Min(10f)] private float farClip = 200f;
        [SerializeField] private LayerMask cullingMask = ~(1 << 5);

        [Header("透視投影から平行投影への移行")]
        [SerializeField, Range(0f, 1f)]
        private float orthographicTransitionStart = 0.35f;

        [SerializeField, Min(0.1f)]
        private float gameplayFocusDistance = 10f;

        private Camera gameplayCamera;

        private Camera captureCamera;
        private RenderTexture renderTexture;
        private Transform startPoint;
        private Transform endPoint;

        private Vector3 alignmentStartPosition;
        private Vector3 alignmentEndPosition;
        private Quaternion alignmentStartRotation;
        private Quaternion alignmentEndRotation;
        private float alignmentStartFov;
        private float alignmentEndFov;
        private bool alignmentReady;

        public RenderTexture RenderTexture => renderTexture;

        /// <summary>
        /// ステージの描画用カメラを生成する。
        /// </summary>
        public void Prepare(RiftAStageEntry entry)
        {
            if (entry == null || entry.StartPoint == null || entry.GameplayCamera == null)
            {
                throw new InvalidOperationException("StartPointまたはGameplayCameraが未設定です。");
            }

            Release();

            startPoint = entry.StartPoint;
            gameplayCamera = entry.GameplayCamera;

            int width = Mathf.Max(
                32,
                Mathf.RoundToInt(Screen.width * resolutionScale));

            int height = Mathf.Max(
                32,
                Mathf.RoundToInt(Screen.height * resolutionScale));

            renderTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32)
            {
                name = "RiftA_Preview_Color",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            renderTexture.Create();

            GameObject cameraObject =
                new GameObject("RiftA_CaptureCamera_Runtime");

            SceneManager.MoveGameObjectToScene(
                cameraObject,
                gameObject.scene);

            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = false;
            captureCamera.fieldOfView = fieldOfView;
            captureCamera.aspect = (float)width / height;
            captureCamera.nearClipPlane = nearClip;
            captureCamera.farClipPlane = farClip;

            // 破片は破片専用カメラだけで描画する。
            int fractureLayer =
                LayerMask.NameToLayer(RIFT_FRAGMENT_LAYER);

            if (fractureLayer < 0)
            {
                throw new InvalidOperationException(
                    $"Layer '{RIFT_FRAGMENT_LAYER}' が見つかりません。");
            }

            captureCamera.cullingMask =
                cullingMask.value & ~(1 << fractureLayer);

            captureCamera.clearFlags = CameraClearFlags.Skybox;
            captureCamera.targetTexture = renderTexture;
            captureCamera.allowHDR = true;

            UniversalAdditionalCameraData cameraData =
                captureCamera.GetUniversalAdditionalCameraData();

            cameraData.SetRenderer(rendererIndex);
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = true;

            alignmentReady = false;

            MoveTo(0f);
            captureCamera.enabled = true;
        }


        /// <summary>
        /// 開始地点から本編カメラへ直接移動し、
        /// 透視投影を徐々に平行投影へ変化させる。
        /// </summary>
        public void MoveTo(float progress)
        {
            if (captureCamera == null ||
                startPoint == null ||
                gameplayCamera == null)
            {
                return;
            }

            float t = Mathf.Clamp01(progress);
            float movement = Mathf.SmoothStep(0f, 1f, t);

            // カメラの位置・回転を本編カメラへ直接近づける。
            // 遠くへ引く処理は行わない。
            Vector3 position = Vector3.Lerp(
                startPoint.position,
                gameplayCamera.transform.position,
                movement);

            Quaternion rotation = Quaternion.Slerp(
                startPoint.rotation,
                gameplayCamera.transform.rotation,
                movement);

            captureCamera.transform.SetPositionAndRotation(
                position,
                rotation);

            // 途中から平行投影への変化を開始する。
            float projectionProgress = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(
                    orthographicTransitionStart,
                    1f,
                    t));

            // 本編カメラが見ている基準平面の中心。
            Vector3 focusPoint =
                gameplayCamera.transform.position +
                gameplayCamera.transform.forward * gameplayFocusDistance;

            // 現在のカメラから基準平面までの前方距離。
            float distance = Mathf.Max(
                0.1f,
                Vector3.Dot(
                    focusPoint - position,
                    rotation * Vector3.forward));

            // 基準平面上で平行投影と大きさを合わせる画角。
            float matchedFov = 2f * Mathf.Atan(
                gameplayCamera.orthographicSize / distance
            ) * Mathf.Rad2Deg;

            float fov = Mathf.Lerp(
                fieldOfView,
                Mathf.Clamp(matchedFov, 1f, 170f),
                movement);

            // 最終的に本編カメラと同じ描画距離へ合わせる。
            float nearPlane = Mathf.Lerp(
                nearClip,
                gameplayCamera.nearClipPlane,
                projectionProgress);

            float farPlane = Mathf.Lerp(
                farClip,
                gameplayCamera.farClipPlane,
                projectionProgress);

            captureCamera.nearClipPlane = nearPlane;
            captureCamera.farClipPlane = Mathf.Max(
                nearPlane + 1f,
                farPlane);

            // 透視投影行列を生成する。
            Matrix4x4 perspectiveMatrix = Matrix4x4.Perspective(
                fov,
                captureCamera.aspect,
                captureCamera.nearClipPlane,
                captureCamera.farClipPlane);

            // 本編カメラの平行投影行列へ徐々に移行する。
            Matrix4x4 targetMatrix =
                gameplayCamera.projectionMatrix;

            captureCamera.orthographic = t >= 1f;

            captureCamera.projectionMatrix = LerpProjection(
                perspectiveMatrix,
                targetMatrix,
                projectionProgress);
        }

        /// <summary>
        /// 2つの投影行列を要素ごとに補間する。
        /// </summary>
        private static Matrix4x4 LerpProjection(
            Matrix4x4 from,
            Matrix4x4 to,
            float progress)
        {
            float t = Mathf.Clamp01(progress);
            Matrix4x4 result = new Matrix4x4();

            for (int i = 0; i < 16; i++)
            {
                result[i] = Mathf.Lerp(from[i], to[i], t);
            }

            return result;
        }

        /// <summary>
        /// 本編の平行投影画面に向け、位置・回転・画角を補間する。
        /// </summary>
        public void AlignToGameplay(float progress)
        {
            if (captureCamera == null || !alignmentReady)
            {
                return;
            }

            float t = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.Clamp01(progress));

            Vector3 position = Vector3.Lerp(
                alignmentStartPosition,
                alignmentEndPosition,
                t);

            Quaternion rotation = Quaternion.Slerp(
                alignmentStartRotation,
                alignmentEndRotation,
                t);

            float fov = Mathf.Lerp(
                alignmentStartFov,
                alignmentEndFov,
                t);

            captureCamera.transform.SetPositionAndRotation(
                position,
                rotation);

            captureCamera.fieldOfView = fov;
        }

        /// <summary>
        /// 最後に描画された映像をRenderTextureに残して停止する。
        /// </summary>
        public void StopRendering()
        {
            if (captureCamera != null)
            {
                captureCamera.enabled = false;
            }
        }

        /// <summary>
        /// 生成したカメラとRenderTextureを解放する。
        /// </summary>
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
            alignmentReady = false;
        }

        private void OnDestroy()
        {
            Release();
        }
    }
}
