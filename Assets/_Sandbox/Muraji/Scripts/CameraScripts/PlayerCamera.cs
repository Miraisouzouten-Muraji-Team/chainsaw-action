using TMPro;
using UnityEngine;

public class PlayerCamera : MonoBehaviour
{
    private const float MIN_FOLLOW_PERCENT = 0.01f;
    private const float MAX_FOLLOW_PERCENT = 1f;
    private const float REFERENCE_FRAME_RATE = 60f;

    [Header("カメラデータ")]
    [Tooltip("通常・食い込み・空中のオフセットと追従率を設定したCameraDataを指定してください。")]
    [SerializeField]
    private CameraData cameraData;

    [Header("プレイヤー")]
    [Tooltip("追従するプレイヤーのSensorPlayerControllerを指定してください。")]
    [SerializeField]
    private SensorPlayerController playerController;

    [Tooltip("同じプレイヤーの食い込み処理を指定してください。未設定なら自動取得します。")]
    [SerializeField]
    private SensorChainsawDigging chainsawDigging;

    [Header("エリア制限")]
    [Tooltip("カメラの表示範囲を収めるエリアのColliderを登録してください。")]
    [SerializeField]
    private Collider[] cameraAreas;

    private Camera targetCamera;
    private Collider currentArea;
    private float smoothedFacingDirection;
    // 登録されているエリア数を公開する。
    public int AreaCount => cameraAreas != null ? cameraAreas.Length : 0;

    // 必要な参照を確認し、初期位置と左右の視野方向を設定する。
    private void Awake()
    {
        if (cameraData == null)
        {
            Debug.LogError(
                "PlayerCameraのCamera DataにCameraDataアセットを設定してください。",
                this);

            enabled = false;
            return;
        }

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

        if (chainsawDigging == null)
        {
            chainsawDigging =
                playerController.GetComponent<SensorChainsawDigging>();
        }

        if (chainsawDigging == null)
        {
            Debug.LogError(
                "PlayerCameraのChainsaw Diggingに、プレイヤーの食い込み処理を設定してください。",
                this);

            enabled = false;
            return;
        }

        smoothedFacingDirection =
            playerController.FacingDirection;

        currentArea = FindPlayerArea(
            playerController.transform.position);

        // 接地判定の初回更新前なので、初期配置には通常設定を使用する。
        Vector3 initialPosition =
            CalculateTargetPosition(cameraData.followOffset);

        if (currentArea != null)
        {
            initialPosition =
                ClampToCurrentArea(initialPosition);
        }

        // 初期位置のみ即座に設定する。
        transform.position = initialPosition;
    }

    // 状態別設定をSOから読み取り、左右の視野とカメラ位置を物理更新する。
    private void FixedUpdate()
    {
        if (cameraData == null ||
            playerController == null ||
            chainsawDigging == null)
        {
            return;
        }

        UpdateCurrentArea();

        GetFollowSettings(
            out Vector2 offset,
            out float followPercent);

        // 振り向きによる視野方向の切り替えを、専用の率でなめらかにする。
        float directionInterpolation =
            CalculateInterpolation(
                cameraData.directionChangePercent,
                Time.fixedDeltaTime);

        smoothedFacingDirection = Mathf.Lerp(
            smoothedFacingDirection,
            playerController.FacingDirection,
            directionInterpolation);

        Vector3 targetPosition =
            CalculateTargetPosition(offset);

        if (currentArea != null)
        {
            targetPosition =
                ClampToCurrentArea(targetPosition);
        }

        // 現在の状態に対応した追従率で、目標位置へ近づける。
        float positionInterpolation =
            CalculateInterpolation(
                followPercent,
                Time.fixedDeltaTime);

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            positionInterpolation);
    }


    // エリア番号に対応する、有効なColliderを取得する。
    public Collider GetRespawnArea(int areaNumber)
    {
        int areaIndex = areaNumber - 1;

        if (cameraAreas == null ||
            areaIndex < 0 ||
            areaIndex >= cameraAreas.Length)
        {
            return null;
        }

        Collider area = cameraAreas[areaIndex];

        if (area == null ||
            !area.enabled ||
            !area.gameObject.activeInHierarchy)
        {
            return null;
        }

        return area;
    }

    // 死亡した瞬間の位置でエリアを更新し、1始まりのエリア番号を返す。
    // エリア外では既存仕様と同じく直前のエリアを使用する。
    public int GetRespawnAreaNumber()
    {
        if (playerController == null)
            return 0;

        UpdateCurrentArea();

        for (int areaIndex = 0; areaIndex < AreaCount; areaIndex++)
        {
            Collider area = GetRespawnArea(areaIndex + 1);

            if (area != null && area == currentArea)
                return areaIndex + 1;
        }

        return 0;
    }

    // 復活先のエリアと向きを反映し、補間せずにカメラを即時配置する。
    public bool SnapToRespawnArea(int areaNumber)
    {
        Collider area = GetRespawnArea(areaNumber);

        if (area == null ||
            targetCamera == null ||
            playerController == null ||
            cameraData == null ||
            chainsawDigging == null)
        {
            return false;
        }

        currentArea = area;

        // 死亡前の視野方向を残さず、復活時の向きへ即座に合わせる。
        smoothedFacingDirection = playerController.FacingDirection;

        // 通常のカメラ更新と同じ条件でオフセットを取得する。
        GetFollowSettings(out Vector2 offset, out _);

        // このメソッド内で使用するローカル変数として宣言する。
        Vector3 targetPosition = CalculateTargetPosition(offset);
        targetPosition = ClampToCurrentArea(targetPosition);

        transform.position = targetPosition;

        return true;
    }

    // カメラとリスポーン処理が、同じプレイヤーを参照しているか確認する。
    public bool UsesPlayer(SensorPlayerController controller)
    {
        return playerController == controller;
    }

    // 食い込み状態を優先し、それ以外は接地状態からSOの設定を選ぶ。
    // 補間途中の値や実行中の状態はSOへ書き込まない。
    private void GetFollowSettings(
        out Vector2 offset,
        out float followPercent)
    {
        offset = cameraData.followOffset;
        followPercent = cameraData.followPercent;

        if (chainsawDigging.isActiveAndEnabled)
        {
            switch (chainsawDigging.Surface)
            {
                case ChainsawSurface.Floor:
                    offset = cameraData.floorOffset;
                    followPercent = cameraData.floorFollowPercent;
                    return;

                case ChainsawSurface.Wall:
                    offset = cameraData.wallOffset;
                    followPercent = cameraData.wallFollowPercent;
                    return;

                case ChainsawSurface.Ceiling:
                    offset = cameraData.ceilingOffset;
                    followPercent = cameraData.ceilingFollowPercent;
                    return;

                case ChainsawSurface.Enemy:
                    // 敵食い込み専用の指定がないため、通常設定を使用する。
                    return;
            }
        }

        // 食い込んでいない未接地状態は、ジャンプ・落下ともに空中設定を使う。
        if (!playerController.IsGroundedForCamera)
        {
            offset = cameraData.airOffset;
            followPercent = cameraData.airFollowPercent;
        }
    }

    // Xは左右の視野方向、Yは高さだけを使い、目標位置を計算する。
    private Vector3 CalculateTargetPosition(Vector2 offset)
    {
        Vector3 position =
            playerController.transform.position;

        position.x += offset.x * smoothedFacingDirection;

        // 左右の向きによって高さが変化しないよう、Yには向きを加算しない。
        position.y += offset.y;

        // カメラの奥行きは現在位置を維持する。
        position.z = transform.position.z;

        return position;
    }

    // 現在のエリアを維持し、外へ出た場合は次のエリアを探す。
    private void UpdateCurrentArea()
    {
        Vector3 playerPosition =
            playerController.transform.position;

        if (currentArea != null &&
            currentArea.enabled &&
            currentArea.gameObject.activeInHierarchy &&
            currentArea.bounds.Contains(playerPosition))
        {
            return;
        }

        Collider nextArea =
            FindPlayerArea(playerPosition);

        // 次のエリアが見つからない場合は、従来どおり直前のエリアを保持する。
        if (nextArea != null)
        {
            currentArea = nextArea;
        }
    }

    // プレイヤーを含む有効なカメラエリアを登録順に探す。
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
            Collider area = cameraAreas[areaIndex];

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

    // カメラの表示サイズを考慮し、目標位置を現在のエリア内へ制限する。
    private Vector3 ClampToCurrentArea(Vector3 position)
    {
        Bounds areaBounds = currentArea.bounds;

        float cameraHalfHeight =
            targetCamera.orthographicSize;

        float cameraHalfWidth =
            cameraHalfHeight * targetCamera.aspect;

        float minX =
            areaBounds.min.x + cameraHalfWidth;

        float maxX =
            areaBounds.max.x - cameraHalfWidth;

        float minY =
            areaBounds.min.y + cameraHalfHeight;

        float maxY =
            areaBounds.max.y - cameraHalfHeight;

        // 表示範囲がエリアより大きい軸は、エリア中央に固定する。
        if (minX > maxX)
        {
            minX = areaBounds.center.x;
            maxX = minX;
        }

        if (minY > maxY)
        {
            minY = areaBounds.center.y;
            maxY = minY;
        }

        position.x = Mathf.Clamp(
            position.x,
            minX,
            maxX);

        position.y = Mathf.Clamp(
            position.y,
            minY,
            maxY);

        return position;
    }

    // 60fps基準の追従率を、実際の更新間隔に対応する補間率へ変換する。
    private float CalculateInterpolation(
        float followPercent,
        float deltaTime)
    {
        float percent = Mathf.Clamp(
            followPercent,
            MIN_FOLLOW_PERCENT,
            MAX_FOLLOW_PERCENT);

        return 1f - Mathf.Pow(
            1f - percent,
            deltaTime * REFERENCE_FRAME_RATE);
    }
}
