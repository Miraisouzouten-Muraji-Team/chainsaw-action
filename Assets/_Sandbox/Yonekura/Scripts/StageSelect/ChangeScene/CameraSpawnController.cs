//using UnityEngine;
//using DG.Tweening;

///// <summary>
/////  カメラの初期位置設定と到着演出を管理する。
///// </summary>
//[DefaultExecutionOrder(200)]
//public class CameraSpawnController : MonoBehaviour
//{
//    [Header("カメラ設定")]
//    [Tooltip("初期位置を設定するPlayerCamera。未設定の場合は自動取得する。")]
//    [SerializeField]
//    private PlayerCamera targetPlayerCamera;

//    [Header("移動先設定")]
//    [Tooltip("カメラの移動演出が終了する位置。")]
//    [SerializeField]
//    private Transform arrivalPoint;

//    [Header("移動演出設定")]
//    [Tooltip("カメラの移動にかかる時間。")]
//    [SerializeField, Min(0.1f)]
//    private float moveDuration = 2f;

//    [Tooltip("カメラの移動速度の変化。")]
//    [SerializeField]
//    private Ease moveEase = Ease.InOutCubic;

//    private Tween activeTween;
//    private bool isInitialized;

//    /// <summary>
//    /// カメラの初期位置を設定する。
//    /// </summary>
//    private void Awake()
//    {
//        InitializeCamera();
//    }

//    /// <summary>
//    /// カメラをCameraSpawnPointの位置に配置し、追従を停止する。
//    /// </summary>
//    private void InitializeCamera()
//    {
//        if (targetPlayerCamera == null)
//        {
//            targetPlayerCamera =
//                FindFirstObjectByType<PlayerCamera>();
//        }

//        if (targetPlayerCamera == null)
//        {
//            Debug.LogError("PlayerCameraが見つかりません。", this);
//            enabled = false;
//            return;
//        }

//        Transform cameraTransform = targetPlayerCamera.transform;

//        cameraTransform.position = transform.position;
//        cameraTransform.rotation = transform.rotation;

//        // 到着演出が終わるまで追従を停止する。
//        targetPlayerCamera.enabled = false;

//        isInitialized = true;
//    }

//    /// <summary>
//    /// シーン読み込み後にカメラの到着演出を開始する。
//    /// </summary>
//    private void Start()
//    {
//        StartArrival();
//    }

//    /// <summary>
//    /// カメラを到着地点まで移動させる。
//    /// </summary>
//    private void StartArrival()
//    {
//        if (!isInitialized)
//        {
//            return;
//        }

//        if (arrivalPoint == null)
//        {
//            Debug.LogError("CameraArrivalPointが設定されていません。", this);
//            return;
//        }

//        Transform cameraTransform = targetPlayerCamera.transform;

//        Sequence sequence = DOTween.Sequence();

//        // カメラの位置と回転を同時に移動させる。
//        sequence.Join(
//            cameraTransform.DOMove(
//                arrivalPoint.position,
//                moveDuration
//            )
//        );

//        sequence.Join(
//            cameraTransform.DORotateQuaternion(
//                arrivalPoint.rotation,
//                moveDuration
//            )
//        );

//        sequence.SetEase(moveEase);

//        activeTween = sequence;

//        sequence.OnComplete(CompleteArrival);
//    }

//    /// <summary>
//    /// 到着演出完了後にカメラの追従を再開する。
//    /// </summary>
//    private void CompleteArrival()
//    {
//        activeTween = null;

//        if (targetPlayerCamera != null)
//        {
//            targetPlayerCamera.enabled = true;
//        }
//    }

//    /// <summary>
//    /// オブジェクト破棄時にTweenを停止する。
//    /// </summary>
//    private void OnDestroy()
//    {
//        activeTween?.Kill();
//    }
//}




using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// カメラの初期位置設定とワープトランジションを管理する。
/// </summary>
[DefaultExecutionOrder(200)]
public class CameraSpawnController : MonoBehaviour
{
    [Header("カメラ設定")]
    [Tooltip("初期位置を設定するPlayerCamera。未設定の場合は自動取得する。")]
    [SerializeField]
    private PlayerCamera targetPlayerCamera;

    [Header("移動先設定")]
    [Tooltip("カメラの移動演出が終了する位置。")]
    [SerializeField]
    private Transform arrivalPoint;

    [Header("カメラ移動設定")]
    [Tooltip("カメラの移動にかかる時間。")]
    [SerializeField, Min(0.1f)]
    private float moveDuration = 2f;

    [SerializeField]
    private Ease moveEase = Ease.InOutCubic;

    [Header("ワープエフェクト設定")]
    [Tooltip("カメラ映像を出力するRenderTexture。")]
    [SerializeField]
    private RenderTexture cameraRenderTexture;

    [Tooltip("映像を表示するRaw Image。")]
    [SerializeField]
    private RawImage warpDisplay;

    [Tooltip("放射状ズームブラー用Material。")]
    [SerializeField]
    private Material radialBlurMaterial;

    [Tooltip("ブラーの最大強度。")]
    [SerializeField, Range(0f, 0.35f)]
    private float maxBlurStrength = 0.3f;

    private Camera targetCamera;
    private Material runtimeMaterial;

    private Tween activeTween;

    private bool isInitialized;
    private bool hasWarpEffect;

    /// <summary>
    /// カメラの初期位置を設定する。
    /// </summary>
    private void Awake()
    {
        InitializeCamera();
        InitializeWarpEffect();
    }

    /// <summary>
    /// カメラを初期位置に配置し、追従を停止する。
    /// </summary>
    private void InitializeCamera()
    {
        if (targetPlayerCamera == null)
        {
            targetPlayerCamera =
                FindFirstObjectByType<PlayerCamera>();
        }

        if (targetPlayerCamera == null)
        {
            Debug.LogError("PlayerCameraが見つかりません。", this);
            enabled = false;
            return;
        }

        targetCamera = targetPlayerCamera.GetComponent<Camera>();

        if (targetCamera == null)
        {
            Debug.LogError("カメラコンポーネントが見つかりません。", this);
            enabled = false;
            return;
        }

        Transform cameraTransform = targetPlayerCamera.transform;

        cameraTransform.position = transform.position;
        cameraTransform.rotation = transform.rotation;

        // 到着演出が終わるまで追従を停止する。
        targetPlayerCamera.enabled = false;

        isInitialized = true;
    }

    /// <summary>
    /// RenderTextureとブラーMaterialを設定する。
    /// </summary>
    private void InitializeWarpEffect()
    {
        if (cameraRenderTexture == null ||
            warpDisplay == null ||
            radialBlurMaterial == null)
        {
            Debug.LogWarning(
                "ワープエフェクトの設定が不足しています。",
                this);

            return;
        }

        // Materialを複製して、この演出専用にする。
        runtimeMaterial = new Material(radialBlurMaterial);

        warpDisplay.material = runtimeMaterial;
        warpDisplay.texture = cameraRenderTexture;
        warpDisplay.raycastTarget = false;
        warpDisplay.enabled = true;

        // カメラ映像をRenderTextureに出力する。
        targetCamera.targetTexture = cameraRenderTexture;

        SetBlurStrength(0f);

        hasWarpEffect = true;
    }

    /// <summary>
    /// シーン読み込み後に到着演出を開始する。
    /// </summary>
    private void Start()
    {
        StartArrival();
    }

    /// <summary>
    /// カメラ移動とワープエフェクトを同時に再生する。
    /// </summary>
    private void StartArrival()
    {
        if (!isInitialized)
        {
            return;
        }

        if (arrivalPoint == null)
        {
            Debug.LogError("CameraArrivalPointが設定されていません。", this);
            return;
        }

        Transform cameraTransform = targetCamera.transform;

        Sequence sequence = DOTween.Sequence();

        // カメラを到着地点へ移動する。
        sequence.Join(
            cameraTransform.DOMove(
                arrivalPoint.position,
                moveDuration
            ).SetEase(moveEase)
        );

        // カメラの向きも同時に変更する。
        sequence.Join(
            cameraTransform.DORotateQuaternion(
                arrivalPoint.rotation,
                moveDuration
            ).SetEase(moveEase)
        );

        if (hasWarpEffect)
        {
            // 前半でブラーを強くする。
            sequence.Join(
                DOVirtual.Float(
                    0f,
                    maxBlurStrength,
                    moveDuration * 0.6f,
                    SetBlurStrength
                ).SetEase(Ease.InCubic)
            );

            // 後半でブラーを解除する。
            sequence.Insert(
                moveDuration * 0.6f,
                DOVirtual.Float(
                    maxBlurStrength,
                    0f,
                    moveDuration * 0.4f,
                    SetBlurStrength
                ).SetEase(Ease.OutCubic)
            );
        }

        activeTween = sequence;

        sequence.OnComplete(CompleteArrival);
    }

    /// <summary>
    /// ブラーの強度をMaterialに反映する。
    /// </summary>
    private void SetBlurStrength(float value)
    {
        if (runtimeMaterial != null)
        {
            runtimeMaterial.SetFloat("_BlurStrength", value);
        }
    }

    /// <summary>
    /// 到着演出完了後にカメラの追従を再開する。
    /// </summary>
    private void CompleteArrival()
    {
        activeTween = null;

        if (hasWarpEffect)
        {
            // Raw Imageの表示を終了する。
            warpDisplay.enabled = false;

            // カメラの出力先を通常の画面に戻す。
            targetCamera.targetTexture = null;
        }

        // 通常のプレイヤー追従を再開する。
        targetPlayerCamera.enabled = true;
    }

    /// <summary>
    /// オブジェクト破棄時にTweenとMaterialを解放する。
    /// </summary>
    private void OnDestroy()
    {
        activeTween?.Kill();

        if (runtimeMaterial != null)
        {
            Destroy(runtimeMaterial);
        }
    }
}

