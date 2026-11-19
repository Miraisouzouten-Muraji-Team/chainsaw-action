using UnityEngine;

namespace RiftPrototypeA
{
    /// <summary>
    /// 方式Aのプレビュー時のみゲーム側を停止する。
    /// 注意：他スクリプトのAwakeより先に停止できる保証はない。
    /// 初期化の副作用があるステージでは個別対策または描画専用のシーンを使う。
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    public sealed class RiftAStageEntry : MonoBehaviour
    {
        [Header("プレビューカメラの移動点")]
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [Header("ゲーム本編")]
        [SerializeField] private Camera gameplayCamera;
        [Tooltip("プレイヤー・敵・入力・ゲーム進行・EventSystem等。地形やライトは含めない。")]
        [SerializeField] private GameObject[] gameplayRoots = new GameObject[0];
        [SerializeField] private Camera[] gameplayCameras = new Camera[0];
        [SerializeField] private AudioListener[] gameplayListeners = new AudioListener[0];

        private bool[] rootStates;
        private bool[] cameraStates;
        private bool[] listenerStates;
        private bool isPreview;
        private bool gameplayCameraWasEnabled;

        public Transform StartPoint => startPoint;
        public Transform EndPoint => endPoint;
        public Camera GameplayCamera => gameplayCamera;

        private void Awake()
        {
            if (RiftAStageLoader.IsPreviewLoad(gameObject.scene.name)) EnterPreview();
        }

        public void EnterPreview()
        {
            if (isPreview) { return; }

            // 本編カメラは配列への登録状況に関係なく停止する。
            gameplayCameraWasEnabled = gameplayCamera != null && gameplayCamera.enabled;

            if (gameplayCamera != null)
            {
                gameplayCamera.enabled = false;
            }

            rootStates = new bool[gameplayRoots.Length];
            cameraStates = new bool[gameplayCameras.Length];
            listenerStates = new bool[gameplayListeners.Length];

            for (int i = 0; i < gameplayRoots.Length; i++)
            {
                if (gameplayRoots[i] == null) continue;
                rootStates[i] = gameplayRoots[i].activeSelf;
                gameplayRoots[i].SetActive(false);
            }
            for (int i = 0; i < gameplayCameras.Length; i++)
            {
                if (gameplayCameras[i] == null) continue;
                cameraStates[i] = gameplayCameras[i].enabled;
                gameplayCameras[i].enabled = false;
            }
            for (int i = 0; i < gameplayListeners.Length; i++)
            {
                if (gameplayListeners[i] == null) continue;
                listenerStates[i] = gameplayListeners[i].enabled;
                gameplayListeners[i].enabled = false;
            }
            isPreview = true;
        }

        public void ActivateGameplay()
        {
            if (!isPreview) return;
            for (int i = 0; i < gameplayRoots.Length; i++)
            {
                if (gameplayRoots[i] != null) gameplayRoots[i].SetActive(rootStates[i]);
            }

            for (int i = 0; i < gameplayCameras.Length; i++)
            {
                if (gameplayCameras[i] != null) gameplayCameras[i].enabled = cameraStates[i];
            }

            for (int i = 0; i < gameplayListeners.Length; i++)
            {
                if (gameplayListeners[i] != null) gameplayListeners[i].enabled = listenerStates[i];
            }
            // 遷移が完了したら本編カメラを元の状態に戻す。
            if (gameplayCamera != null)
            {
                gameplayCamera.enabled = gameplayCameraWasEnabled;
            }
            isPreview = false;
        }
    }
}
