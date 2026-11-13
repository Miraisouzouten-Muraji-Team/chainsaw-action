using UnityEngine;

/// <summary>
/// 分割後のEnemy MeshにPlayerが接近した際、分割片だけへ水平速度を加える。
/// </summary>
/// <remarks>
/// Playerの移動・速度・Colliderは変更しない。
/// Playerと分割片本体の物理衝突は生成元がIgnoreCollisionで無効化し、
/// このComponentが所有する子Triggerだけで接触を検出する。
/// </remarks>
public sealed class EnemyCutPiecePlayerPushReaction : MonoBehaviour
{
    private const float TRIGGER_PADDING = 0.05f;
    private const float MINIMUM_PLAYER_SPEED = 0.1f;
    private const float PUSH_SPEED_MULTIPLIER = 0.85f;
    private const float MAXIMUM_PUSH_SPEED = 4f;
    private const float MAXIMUM_VELOCITY_CHANGE_PER_STEP = 1.5f;
    private const float MINIMUM_SEPARATION = 0.01f;

    private Rigidbody pieceRigidbody;
    private Transform playerRoot;
    private Collider activeTriggerCollider;
    private float lastPushFixedTime = float.NegativeInfinity;

    /// <summary>
    /// 非アクティブな分割片の下に、接触検知用Triggerを事前生成する。
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
    /// 分割片を有効化する前に、物理形状とPlayer参照を確定する。
    /// </summary>
    public void Configure(Rigidbody targetRigidbody, Collider physicalCollider, Transform resolvedPlayerRoot)
    {
        pieceRigidbody = targetRigidbody;
        playerRoot = resolvedPlayerRoot;
        activeTriggerCollider = null;
        lastPushFixedTime = float.NegativeInfinity;

        // 子GameObjectのLayerは親から自動継承されない。
        // Piece本体と同じLayerでTriggerを判定させる。
        gameObject.layer = targetRigidbody.gameObject.layer;

        BoxCollider boxTrigger = GetComponent<BoxCollider>();
        CapsuleCollider capsuleTrigger = GetComponent<CapsuleCollider>();
        boxTrigger.enabled = false;
        capsuleTrigger.enabled = false;

        if (physicalCollider is CapsuleCollider physicalCapsule)
        {
            capsuleTrigger.center = physicalCapsule.center;
            capsuleTrigger.direction = physicalCapsule.direction;
            capsuleTrigger.radius = physicalCapsule.radius + TRIGGER_PADDING;
            capsuleTrigger.height = Mathf.Max(
                physicalCapsule.height + TRIGGER_PADDING * 2f,
                capsuleTrigger.radius * 2f);
            capsuleTrigger.enabled = true;
            activeTriggerCollider = capsuleTrigger;
        }
        else if (physicalCollider is BoxCollider physicalBox)
        {
            boxTrigger.center = physicalBox.center;
            boxTrigger.size = physicalBox.size + Vector3.one * (TRIGGER_PADDING * 2f);
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
        if (pieceRigidbody == null || pieceRigidbody.isKinematic ||
            playerRoot == null || activeTriggerCollider == null ||
            other == null || other.isTrigger ||
            (other.transform != playerRoot && !other.transform.IsChildOf(playerRoot)) ||
            lastPushFixedTime == Time.fixedTime)
        {
            return;
        }

        Rigidbody playerRigidbody = other.attachedRigidbody;
        if (playerRigidbody == null)
        {
            return;
        }

        // 実装済みの分割片は奥行き位置(Z)を固定しているため、
        // Playerの水平移動(X)が分割片に向かうときだけ押す。
        float playerHorizontalSpeed = playerRigidbody.linearVelocity.x;
        if (Mathf.Abs(playerHorizontalSpeed) < MINIMUM_PLAYER_SPEED)
        {
            return;
        }

        float separation = activeTriggerCollider.bounds.center.x - other.bounds.center.x;
        if (Mathf.Abs(separation) < MINIMUM_SEPARATION)
        {
            separation = pieceRigidbody.position.x - playerRigidbody.position.x;
        }

        if (Mathf.Abs(separation) < MINIMUM_SEPARATION ||
            playerHorizontalSpeed * separation <= 0f)
        {
            return;
        }

        float direction = Mathf.Sign(playerHorizontalSpeed);
        float desiredSpeed = Mathf.Min(
            MAXIMUM_PUSH_SPEED,
            Mathf.Abs(playerHorizontalSpeed) * PUSH_SPEED_MULTIPLIER);
        float currentSpeedInPushDirection = pieceRigidbody.linearVelocity.x * direction;
        float requiredSpeedChange = desiredSpeed - currentSpeedInPushDirection;
        if (requiredSpeedChange <= 0f)
        {
            return;
        }

        // 力を加える先はPieceのみ。Playerに作用する反作用は生成しない。
        pieceRigidbody.AddForce(
            Vector3.right * direction * Mathf.Min(requiredSpeedChange, MAXIMUM_VELOCITY_CHANGE_PER_STEP),
            ForceMode.VelocityChange);
        lastPushFixedTime = Time.fixedTime;
    }
}
