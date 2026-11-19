using UnityEngine;

/// <summary>
/// Playerが切断片の側面へ近づいたとき、切断片のRigidbodyだけに疑似的な押し出しを与える。
/// </summary>
/// <remarks>
/// Playerと切断片本体の物理衝突は3D Physicsのレイヤー設定で無効化する。
/// 子Triggerは接近候補の検出専用とし、実際の押し出し可否は本体Colliderと
/// Player ColliderのBoundsから物理更新ごとに判断する。
/// </remarks>
public sealed class EnemyCutPiecePlayerPushReaction : MonoBehaviour
{
    private const string PIECE_LAYER_NAME = "EnemyCutPiece";
    private const string SENSOR_LAYER_NAME = "EnemyCutPieceSensor";
    private const float MINIMUM_PLAYER_SPEED = 0.1f;
    private const float MINIMUM_SEPARATION = 0.01f;
    private const float MAXIMUM_ADDITIONAL_ROLL_SPEED = 5f;

    private Rigidbody pieceRigidbody;
    private Collider physicalPieceCollider;
    private Transform playerRoot;
    private Collider activeTriggerCollider;

    private float contactDistance;
    private float pushSpeedMultiplier;
    private float maximumPushSpeed;
    private float maximumVelocityChangePerStep;
    private float minimumSideOverlapRatio;
    private float rollingLeverArmRatio;
    private float pushReadyFixedTime;
    private float lastPushFixedTime = float.NegativeInfinity;

    /// <summary>
    /// 非アクティブな切断片に接近検出用Triggerを事前生成する。
    /// </summary>
    public static void CreateForPiece(GameObject pieceObject)
    {
        var triggerObject = new GameObject("PlayerPushTrigger");
        triggerObject.transform.SetParent(pieceObject.transform, false);

        BoxCollider boxTrigger = triggerObject.AddComponent<BoxCollider>();
        boxTrigger.isTrigger = true;
        boxTrigger.enabled = false;

        CapsuleCollider capsuleTrigger = triggerObject.AddComponent<CapsuleCollider>();
        capsuleTrigger.isTrigger = true;
        capsuleTrigger.enabled = false;

        triggerObject.AddComponent<EnemyCutPiecePlayerPushReaction>();
    }

    /// <summary>
    /// 切断片の使用前に、本体とSensorのレイヤー、物理形状、押し出し設定を確定する。
    /// 再使用時にも前回の押し出し判定状態を引き継がない。
    /// </summary>
    public void Configure(
        Rigidbody targetRigidbody,
        Collider physicalCollider,
        Transform resolvedPlayerRoot,
        float reactionDistance,
        float speedMultiplier,
        float maximumSpeed,
        float maximumSpeedChangePerStep,
        float requiredSideOverlapRatio,
        float pushRollingLeverArmRatio,
        float initialPushDelay)
    {
        pieceRigidbody = targetRigidbody;
        physicalPieceCollider = physicalCollider;
        playerRoot = resolvedPlayerRoot;
        activeTriggerCollider = null;

        if (physicalCollider != null)
        {
            physicalCollider.gameObject.layer = LayerMask.NameToLayer(PIECE_LAYER_NAME);
        }

        gameObject.layer = LayerMask.NameToLayer(SENSOR_LAYER_NAME);

        contactDistance = Mathf.Max(0f, reactionDistance);
        pushSpeedMultiplier = Mathf.Max(0f, speedMultiplier);
        maximumPushSpeed = Mathf.Max(0f, maximumSpeed);
        maximumVelocityChangePerStep = Mathf.Max(0f, maximumSpeedChangePerStep);
        minimumSideOverlapRatio = Mathf.Clamp01(requiredSideOverlapRatio);
        rollingLeverArmRatio = Mathf.Clamp(pushRollingLeverArmRatio, 0f, 0.5f);
        pushReadyFixedTime = Time.fixedTime + Mathf.Max(0f, initialPushDelay);
        lastPushFixedTime = float.NegativeInfinity;

        BoxCollider boxTrigger = GetComponent<BoxCollider>();
        CapsuleCollider capsuleTrigger = GetComponent<CapsuleCollider>();
        boxTrigger.enabled = false;
        capsuleTrigger.enabled = false;

        if (targetRigidbody == null || physicalCollider == null ||
            maximumPushSpeed <= 0f || maximumVelocityChangePerStep <= 0f ||
            pushSpeedMultiplier <= 0f)
        {
            return;
        }

        // Triggerだけを拡張して候補を受け取り、本体Colliderの大きさは変更しない。
        if (physicalCollider is CapsuleCollider physicalCapsule)
        {
            capsuleTrigger.center = physicalCapsule.center;
            capsuleTrigger.direction = physicalCapsule.direction;
            capsuleTrigger.radius = physicalCapsule.radius + contactDistance;
            capsuleTrigger.height = Mathf.Max(
                physicalCapsule.height + contactDistance * 2f,
                capsuleTrigger.radius * 2f);
            capsuleTrigger.enabled = true;
            activeTriggerCollider = capsuleTrigger;
        }
        else if (physicalCollider is BoxCollider physicalBox)
        {
            boxTrigger.center = physicalBox.center;
            boxTrigger.size = physicalBox.size + Vector3.one * (contactDistance * 2f);
            boxTrigger.enabled = true;
            activeTriggerCollider = boxTrigger;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryPushPiece(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryPushPiece(other);
    }

    private void TryPushPiece(Collider other)
    {
        if (pieceRigidbody == null || !pieceRigidbody.gameObject.activeInHierarchy ||
            pieceRigidbody.isKinematic || physicalPieceCollider == null ||
            !physicalPieceCollider.enabled || playerRoot == null ||
            activeTriggerCollider == null || !activeTriggerCollider.enabled ||
            other == null || !other.enabled || other.isTrigger ||
            Time.fixedTime < pushReadyFixedTime ||
            lastPushFixedTime == Time.fixedTime ||
            (other.transform != playerRoot && !other.transform.IsChildOf(playerRoot)))
        {
            return;
        }

        Rigidbody playerRigidbody = other.attachedRigidbody;
        if (playerRigidbody == null)
        {
            return;
        }

        // 両分割方式の移動平面はXY。奥行き位置Zは既存のConstraintに従う。
        float playerHorizontalSpeed = playerRigidbody.linearVelocity.x;
        if (Mathf.Abs(playerHorizontalSpeed) < MINIMUM_PLAYER_SPEED)
        {
            return;
        }

        Bounds pieceBounds = physicalPieceCollider.bounds;
        Bounds playerBounds = other.bounds;
        float pushDirection = Mathf.Sign(playerHorizontalSpeed);

        // 側面に十分重なった場合だけ反応させる。
        // 上からかすめるだけの接近や、ジャンプで上を通る動きを除外する。
        float verticalOverlap = Mathf.Min(pieceBounds.max.y, playerBounds.max.y) -
                                Mathf.Max(pieceBounds.min.y, playerBounds.min.y);
        float requiredVerticalOverlap =
            Mathf.Min(pieceBounds.size.y, playerBounds.size.y) * minimumSideOverlapRatio;
        if (verticalOverlap <= 0f || verticalOverlap < requiredVerticalOverlap)
        {
            return;
        }

        // PlayerがPieceの上にいて、Colliderの下端が上半分に触れた場合は側面接触に含めない。
        if (playerBounds.center.y >= pieceBounds.max.y && playerBounds.min.y >= pieceBounds.center.y)
        {
            return;
        }

        float centerSeparation = (pieceBounds.center.x - playerBounds.center.x) * pushDirection;
        if (centerSeparation < MINIMUM_SEPARATION)
        {
            return;
        }

        // 中心間距離ではなく、進行方向側のPlayer表面とPiece表面の間隔を測る。
        // 高速移動でも1物理ステップ分の食い込みは許容し、完全にすり抜けた後の反応は防ぐ。
        float surfaceGap = pushDirection > 0f
            ? pieceBounds.min.x - playerBounds.max.x
            : playerBounds.min.x - pieceBounds.max.x;
        float maximumAllowedPenetration =
            Mathf.Min(pieceBounds.extents.x, playerBounds.extents.x) +
            Mathf.Abs(playerHorizontalSpeed) * Time.fixedDeltaTime;
        if (surfaceGap > contactDistance || surfaceGap < -maximumAllowedPenetration)
        {
            return;
        }

        float playerSpeedInPushDirection = Mathf.Abs(playerHorizontalSpeed);
        float currentSpeedInPushDirection = pieceRigidbody.linearVelocity.x * pushDirection;
        if (currentSpeedInPushDirection >= playerSpeedInPushDirection)
        {
            return;
        }

        float desiredPushSpeed = Mathf.Min(maximumPushSpeed, playerSpeedInPushDirection * pushSpeedMultiplier);
        float requestedSpeedChange = desiredPushSpeed - currentSpeedInPushDirection;
        if (requestedSpeedChange <= 0f)
        {
            return;
        }

        float appliedSpeedChange = Mathf.Min(requestedSpeedChange, maximumVelocityChangePerStep);

        // Pieceだけへ速度変化を加える。押す向きの現在速度と上限を見ているため、
        // 連続Stayでも過剰加速しない。Playerの速度・座標は変更しない。
        Vector3 pushVelocityChange = Vector3.right * (pushDirection * appliedSpeedChange);

        // 質量中心の少し上から押すと、移動方向へ転がる小さなTorqueも発生する。
        // 既に高速回転しているPieceにはTorqueを追加せず、既存の飛散回転を妨げない。
        if (rollingLeverArmRatio > 0f &&
            Mathf.Abs(pieceRigidbody.angularVelocity.z) < MAXIMUM_ADDITIONAL_ROLL_SPEED)
        {
            float rollingLeverArm = pieceBounds.extents.y * rollingLeverArmRatio;
            Vector3 pushPosition = pieceRigidbody.worldCenterOfMass + Vector3.up * rollingLeverArm;
            pieceRigidbody.AddForceAtPosition(
                pushVelocityChange,
                pushPosition,
                ForceMode.VelocityChange);
        }
        else
        {
            pieceRigidbody.AddForce(pushVelocityChange, ForceMode.VelocityChange);
        }

        lastPushFixedTime = Time.fixedTime;
    }
}
