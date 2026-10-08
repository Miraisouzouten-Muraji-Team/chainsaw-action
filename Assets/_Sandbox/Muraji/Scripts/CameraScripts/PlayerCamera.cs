using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    private const float DEFAULT_FOLLOW_PERCENT = 0.1f;
    private const float MIN_FOLLOW_PERCENT = 0.01f;
    private const float MAX_FOLLOW_PERCENT = 1f;

    [Header("カメラデータ")]
    [SerializeField] private CameraData cameraData;

    [Header("プレイヤー")]
    [SerializeField] private SensorPlayerController playerController;

    [Header("追従設定")]
    [Tooltip("プレイヤーに対するカメラのオフセット。")]
    private Vector2 followOffset = new Vector2(-3f, -3f);

    [Tooltip("プレイヤーの進行方向へどれだけ視野を広げるか。")]
    private float directionLookAhead = 2f;

    [Tooltip("1フレームあたりの追従率。0.1 = 10%。")]
    private float followPercent = DEFAULT_FOLLOW_PERCENT;

    [Header("エリア制限")]
    [Tooltip("各エリアのカメラ範囲に使用するColliderを登録する。")]
    [SerializeField] private Collider[] cameraAreas;

    private Camera targetCamera;
    private Collider currentArea;
    private Vector3 targetPosition;

    private Vector3 initialPlayerPosition;

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();

        if (targetCamera == null)
        {
            Debug.LogError(
                "PlayerCameraにはCameraコンポーネントが必要です。",
                this);

            enabled = false;
            return;
        }

        if (!targetCamera.orthographic)
        {
            Debug.LogError(
                "PlayerCameraはOrthographic Cameraを前提としています。",
                this);

            enabled = false;
            return;
        }

        if (playerController == null)
        {
            Debug.LogError(
                "PlayerCameraのPlayer Controllerを設定してください。",
                this);

            enabled = false;
            return;
        }

        followOffset = cameraData.followOffset;
        directionLookAhead = cameraData.directionLookAhead;
        followPercent = cameraData.followPercent;

        // ゲーム開始時のプレイヤー位置を保存する。
        initialPlayerPosition =
            playerController.transform.position;

        // プレイヤーが最初にいるエリアを取得する。
        currentArea = FindPlayerArea(
            initialPlayerPosition);

        // 初期位置はプレイヤー位置 + オフセット。
        targetPosition = CalculateTargetPosition();

        if (currentArea != null)
        {
            targetPosition =
                ClampToCurrentArea(targetPosition);
        }

        // 初期位置だけは即座に設定する。
        transform.position = targetPosition;
    }

    private void LateUpdate()
    {
        if (playerController == null)
        {
            return;
        }

        UpdateCurrentArea();

        targetPosition =
            CalculateTargetPosition();

        if (currentArea != null)
        {
            targetPosition =
                ClampToCurrentArea(targetPosition);
        }

        float interpolation =
            CalculateInterpolation();

        transform.position =
            Vector3.Lerp(
                transform.position,
                targetPosition,
                interpolation);
    }
    
    private void UpdateCurrentArea()
    {
        Vector3 playerPosition =
            playerController.transform.position;

        // 現在のエリア内にいるなら、そのまま維持する。
        if (currentArea != null &&
            currentArea.enabled &&
            currentArea.gameObject.activeInHierarchy &&
            currentArea.bounds.Contains(playerPosition))
        {
            return;
        }

        Collider nextArea =
            FindPlayerArea(playerPosition);

        if (nextArea != null)
        {
            currentArea = nextArea;
        }
    }

    private Collider FindPlayerArea(Vector3 playerPosition)
    {
        if (cameraAreas == null)
        {
            return null;
        }

        for (int areaIndex = 0;
             areaIndex < cameraAreas.Length;
             areaIndex++)
        {
            Collider area =
                cameraAreas[areaIndex];

            if (area == null ||
                !area.enabled ||
                !area.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (area.bounds.Contains(playerPosition))
            {
                return area;
            }
        }

        return null;
    }

    private Vector3 CalculateTargetPosition()
    {
        float facingDirection =
            playerController.FacingDirection;

        Vector3 playerPosition =
            playerController.transform.position;

        // プレイヤー位置を基準にする。
        Vector3 position =
            playerPosition;

        // [-3,-3]オフセット。
        position.x += followOffset.x;
        position.y += followOffset.y;

        // プレイヤーの進行方向側へ視野を広げる。
        position.x +=
            facingDirection * directionLookAhead;

        // プレイヤーの進行方向側へ視野を広げる。
        position.y +=
            facingDirection;

        // カメラのZ座標は現在位置を維持する。
        position.z =
            transform.position.z;

        return position;
    }

    private Vector3 ClampToCurrentArea(Vector3 position)
    {
        Bounds areaBounds =
            currentArea.bounds;

        float cameraHalfHeight =
            targetCamera.orthographicSize;

        float cameraHalfWidth =
            cameraHalfHeight *
            targetCamera.aspect;

        float minX =
            areaBounds.min.x +
            cameraHalfWidth;

        float maxX =
            areaBounds.max.x -
            cameraHalfWidth;

        float minY =
            areaBounds.min.y +
            cameraHalfHeight;

        float maxY =
            areaBounds.max.y -
            cameraHalfHeight;

        // エリアよりカメラの方が大きい場合は、
        // そのエリアの中央に固定する。
        if (minX > maxX)
        {
            float centerX =
                areaBounds.center.x;

            minX = centerX;
            maxX = centerX;
        }

        if (minY > maxY)
        {
            float centerY =
                areaBounds.center.y;

            minY = centerY;
            maxY = centerY;
        }

        position.x =
            Mathf.Clamp(
                position.x,
                minX,
                maxX);

        position.y =
            Mathf.Clamp(
                position.y,
                minY,
                maxY);

        return position;
    }

    private float CalculateInterpolation()
    {
        float percent =
            Mathf.Clamp(
                followPercent,
                MIN_FOLLOW_PERCENT,
                MAX_FOLLOW_PERCENT);

        return 1f -
               Mathf.Pow(
                   1f - percent,
                   Time.deltaTime * 60f);
    }
}
