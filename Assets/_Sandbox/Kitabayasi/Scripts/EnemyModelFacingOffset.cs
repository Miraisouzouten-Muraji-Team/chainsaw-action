using UnityEngine;

/// <summary>
/// VisualRootが示すゲーム上の正面を変えず、モデルの見た目だけをカメラ側へ傾ける。
/// </summary>
/// <remarks>
/// このコンポーネントはVisualRoot直下の見た目用Transformに付ける。
/// 移動・索敵・攻撃方向、VisualRootの回転、Rigidbodyや当たり判定は変更しない。
/// ゲームプレイ平面はワールドXY、奥行き方向はワールドZを前提とする。
/// </remarks>
[DisallowMultipleComponent]
public sealed class EnemyModelFacingOffset : MonoBehaviour
{
    private const float MINIMUM_ROTATION_AXIS_LENGTH = 0.0001f;
    private const float MINIMUM_CAMERA_DEPTH_DISTANCE = 0.0001f;

    [Header("参照")]
    [Tooltip("ゲーム上の正面（ローカル+Z）を管理するVisualRoot。ModelViewOffsetの直接の親を指定する。")]
    [SerializeField]
    private Transform visualRoot;

    [Tooltip("画面側を判断するカメラ。未設定なら開始時にCamera.mainを使用する。")]
    [SerializeField]
    private Transform cameraTransform;

    [Header("見た目の補正")]
    [Tooltip("ゲーム上の正面から、カメラ側へモデルを傾ける角度。0なら補正しない。")]
    [Range(0f, 85f)]
    [SerializeField]
    private float cameraFacingOffsetAngle = 25f;

    private Quaternion originalLocalRotation;

    private void Awake()
    {
        originalLocalRotation = transform.localRotation;

        if (visualRoot == null || transform.parent != visualRoot)
        {
            Debug.LogError(
                $"{nameof(EnemyModelFacingOffset)}: " +
                "VisualRootを設定し、このコンポーネントをVisualRoot直下の" +
                "見た目用オブジェクトに配置してください。",
                this);

            enabled = false;
        }
    }

    private void Start()
    {
        if (cameraTransform != null)
        {
            return;
        }

        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            cameraTransform = mainCamera.transform;
            return;
        }

        Debug.LogWarning(
            $"{nameof(EnemyModelFacingOffset)}: " +
            "カメラが見つかりません。Camera Transformを設定するか、" +
            "対象カメラにMainCameraタグを設定してください。",
            this);

        enabled = false;
    }

    private void LateUpdate()
    {
        if (visualRoot == null || cameraTransform == null)
        {
            return;
        }

        if (cameraFacingOffsetAngle <= 0f)
        {
            transform.localRotation = originalLocalRotation;
            return;
        }

        // このゲームではXYが移動平面。カメラのXY位置を角度に混ぜず、
        // どちらの奥行き側から見ているかだけを使う。
        float cameraDepthDistance =
            cameraTransform.position.z - visualRoot.position.z;

        if (Mathf.Abs(cameraDepthDistance) < MINIMUM_CAMERA_DEPTH_DISTANCE)
        {
            transform.localRotation = originalLocalRotation;
            return;
        }

        Vector3 cameraSideWorldDirection =
            cameraDepthDistance > 0f
                ? Vector3.forward
                : Vector3.back;

        Vector3 cameraSideLocalDirection =
            visualRoot.InverseTransformDirection(cameraSideWorldDirection);

        // +Z（ゲーム上の正面）から画面側へ回す軸を求める。
        // ドローンの180度旋回中、+Zとカメラ方向が平行になる瞬間は
        // 補正を0へ滑らかに弱め、回転軸の急反転を避ける。
        Vector3 rotationAxis =
            Vector3.Cross(Vector3.forward, cameraSideLocalDirection);

        float rotationAxisLength = rotationAxis.magnitude;

        if (rotationAxisLength < MINIMUM_ROTATION_AXIS_LENGTH)
        {
            transform.localRotation = originalLocalRotation;
            return;
        }

        Quaternion facingOffset = Quaternion.AngleAxis(
            cameraFacingOffsetAngle * rotationAxisLength,
            rotationAxis / rotationAxisLength);

        // 補正を前回の結果へ積み上げず、必ず初期姿勢を基準に適用する。
        transform.localRotation = facingOffset * originalLocalRotation;
    }
}
