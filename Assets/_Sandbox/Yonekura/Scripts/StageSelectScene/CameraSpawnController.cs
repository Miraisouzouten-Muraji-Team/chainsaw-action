using UnityEngine;

/// <summary>
/// カメラの初期位置を設定し、到着演出中の追従を制御する。
/// </summary>
[DefaultExecutionOrder(200)]
public class CameraSpawnController : MonoBehaviour
{
    [Header("カメラ設定")]
    [Tooltip("初期位置を設定するPlayerCamera。未設定の場合は自動取得する。")]
    [SerializeField]
    private PlayerCamera targetPlayerCamera;

    [Tooltip("カメラの回転も設定するかどうか。")]
    [SerializeField]
    private bool applyRotation = true;

    private bool isInitialized;

    /// <summary>
    /// カメラの初期位置を設定し、追従を一時停止する。
    /// </summary>
    private void Awake()
    {
        InitializeCamera();
    }

    /// <summary>
    /// カメラをCameraSpawnPointの位置に配置する。
    /// </summary>
    private void InitializeCamera()
    {
        if (isInitialized)
        {
            return;
        }

        // PlayerCameraを取得する。
        if (targetPlayerCamera == null)
        {
            targetPlayerCamera =
                FindFirstObjectByType<PlayerCamera>();
        }

        if (targetPlayerCamera == null)
        {
            Debug.LogError(
                "PlayerCameraが見つかりません。",
                this);

            enabled = false;
            return;
        }

        Transform cameraTransform = targetPlayerCamera.transform;

        // カメラを指定位置に配置する。
        cameraTransform.position = transform.position;

        if (applyRotation)
        {
            cameraTransform.rotation = transform.rotation;
        }

        // 到着演出が終わるまで追従処理を停止する。
        targetPlayerCamera.enabled = false;

        isInitialized = true;
    }

    /// <summary>
    /// 到着演出完了後にカメラの追従を再開する。
    /// </summary>
    public void CompleteArrival()
    {
        if (!isInitialized || targetPlayerCamera == null)
        {
            Debug.LogWarning(
                "カメラの初期化が完了していません。",
                this);

            return;
        }

        targetPlayerCamera.enabled = true;
    }
}
