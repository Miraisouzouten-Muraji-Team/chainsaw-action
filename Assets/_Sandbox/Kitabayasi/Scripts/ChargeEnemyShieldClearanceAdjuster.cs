using UnityEngine;

/// <summary>
/// トッシンエネミーの腕が盾へ近づいたとき、盾を一時的に前へ逃がす見た目の補正。
/// </summary>
/// <remarks>
/// 盾のMeshFilterのBoundsと前腕～手のボーン区間を使う近似判定であり、
/// Mesh同士の厳密な接触判定ではない。Colliderや攻撃判定、Animatorの再生は扱わない。
/// 補正は盾の親Transform基準で行い、VisualRootの向き変更にも追従する。
/// </remarks>
public sealed class ChargeEnemyShieldClearanceAdjuster : MonoBehaviour
{
    private const float MINIMUM_DISTANCE = 0.0001f;
    private const int SEGMENT_SAMPLE_COUNT = 5;

    [Header("盾")]
    [Tooltip("Shield_Piece_A / Bを含む盾の親Transform。腕ボーンの子にはしない。")]
    [SerializeField] private Transform shieldRoot;

    [Tooltip("盾の親Transformのローカル座標で指定する、盾を逃がす方向。モデルの正面方向に合わせて設定する。")]
    [SerializeField] private Vector3 pushDirectionInParentSpace = Vector3.forward;

    [Header("腕の参照")]
    [Tooltip("左前腕の開始位置に相当するボーン。")]
    [SerializeField] private Transform leftForearm;

    [Tooltip("左手首付近のボーン。")]
    [SerializeField] private Transform leftHand;

    [Tooltip("右前腕の開始位置に相当するボーン。")]
    [SerializeField] private Transform rightForearm;

    [Tooltip("右手首付近のボーン。")]
    [SerializeField] private Transform rightHand;

    [Header("距離・移動")]
    [Tooltip("腕と盾のBoundsがこの距離以内に近づくと前方への補正を開始する（ワールド単位）。")]
    [SerializeField, Min(0f)] private float detectionDistance = 0.25f;

    [Tooltip("盾を元の位置から前へ動かす最大距離（ワールド単位）。")]
    [SerializeField, Min(0f)] private float maximumPushDistance = 0.25f;

    [Tooltip("腕へ近づいたとき、盾が前進する速さ（ワールド単位/秒）。")]
    [SerializeField, Min(0f)] private float pushSpeed = 2.5f;

    [Tooltip("腕が離れたとき、盾が元の位置へ戻る速さ（ワールド単位/秒）。")]
    [SerializeField, Min(0f)] private float returnSpeed = 1.5f;

    private Transform shieldParent;
    private MeshFilter[] shieldMeshFilters;
    private Vector3 originalShieldLocalPosition;
    private float currentPushDistance;
    private bool isAdjustmentActive;
    private bool isInitialized;

    private void Awake()
    {
        if (shieldRoot == null || shieldRoot.parent == null ||
            (leftForearm == null && leftHand == null &&
             rightForearm == null && rightHand == null))
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemyShieldClearanceAdjuster)}: " +
                "Shield Rootと少なくとも1つの腕ボーンを設定してください。",
                this);
            enabled = false;
            return;
        }

        shieldParent = shieldRoot.parent;
        originalShieldLocalPosition = shieldRoot.localPosition;
        shieldMeshFilters = shieldRoot.GetComponentsInChildren<MeshFilter>(true);

        bool hasValidShieldMesh = false;
        foreach (MeshFilter meshFilter in shieldMeshFilters)
        {
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                hasValidShieldMesh = true;
                break;
            }
        }

        if (!hasValidShieldMesh ||
            pushDirectionInParentSpace.sqrMagnitude <=
            MINIMUM_DISTANCE * MINIMUM_DISTANCE)
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemyShieldClearanceAdjuster)}: " +
                "盾のMeshFilterか前方移動方向が不正です。",
                this);
            enabled = false;
            return;
        }

        isInitialized = true;
    }

    private void LateUpdate()
    {
        if (!isInitialized || shieldRoot == null ||
            shieldRoot.parent != shieldParent)
        {
            return;
        }

        if (!isAdjustmentActive && currentPushDistance <= 0f)
        {
            return;
        }

        float desiredPushDistance = isAdjustmentActive
            ? CalculateDesiredPushDistance()
            : 0f;

        float moveSpeed = desiredPushDistance > currentPushDistance
            ? pushSpeed
            : returnSpeed;

        currentPushDistance = Mathf.MoveTowards(
            currentPushDistance,
            desiredPushDistance,
            Mathf.Max(0f, moveSpeed) * Time.deltaTime);

        ApplyPushDistance();
    }

    /// <summary>
    /// 予備動作の間のみ腕との距離による補正を有効にする。
    /// falseにすると現在の位置から滑らかに通常位置へ戻る。
    /// </summary>
    public void SetAdjustmentActive(bool active)
    {
        isAdjustmentActive = active;
    }

    /// <summary>
    /// 攻撃Stateから離れる場合は即座に元の位置へ戻す。
    /// 死亡時にShield PieceへRigidbodyが追加される前に補正を解除するために使用する。
    /// </summary>
    public void ResetAdjustment()
    {
        isAdjustmentActive = false;
        currentPushDistance = 0f;

        if (isInitialized && shieldRoot != null &&
            shieldRoot.parent == shieldParent)
        {
            shieldRoot.localPosition = originalShieldLocalPosition;
        }
    }

    private void OnDisable()
    {
        ResetAdjustment();
    }

    private float CalculateDesiredPushDistance()
    {
        if (detectionDistance <= MINIMUM_DISTANCE ||
            maximumPushDistance <= 0f)
        {
            return 0f;
        }

        // 現在の盾はすでに押し出されている可能性がある。
        // 距離判定だけは元の盾位置を基準にし、補正量が増減して振動するのを防ぐ。
        Vector3 currentShieldTranslation = shieldRoot.position -
            shieldParent.TransformPoint(originalShieldLocalPosition);

        float minimumSqrDistance = float.PositiveInfinity;
        AccumulateArmDistance(
            leftForearm, leftHand, currentShieldTranslation,
            ref minimumSqrDistance);
        AccumulateArmDistance(
            rightForearm, rightHand, currentShieldTranslation,
            ref minimumSqrDistance);

        float distance = Mathf.Sqrt(minimumSqrDistance);
        float proximity = Mathf.Clamp01(
            (detectionDistance - distance) / detectionDistance);

        return maximumPushDistance * proximity;
    }

    private void AccumulateArmDistance(
        Transform forearm,
        Transform hand,
        Vector3 currentShieldTranslation,
        ref float minimumSqrDistance)
    {
        if (forearm == null && hand == null)
        {
            return;
        }

        if (forearm == null || hand == null)
        {
            Vector3 samplePosition = forearm != null
                ? forearm.position
                : hand.position;

            AccumulateSampleDistance(
                samplePosition, currentShieldTranslation,
                ref minimumSqrDistance);
            return;
        }

        // ボーンの両端だけでは前腕中央が盾を横切る場合を見逃すため、区間もサンプリングする。
        for (int index = 0; index < SEGMENT_SAMPLE_COUNT; index++)
        {
            float t = (float)index / (SEGMENT_SAMPLE_COUNT - 1);
            Vector3 samplePosition = Vector3.Lerp(
                forearm.position, hand.position, t);

            AccumulateSampleDistance(
                samplePosition, currentShieldTranslation,
                ref minimumSqrDistance);
        }
    }

    private void AccumulateSampleDistance(
        Vector3 samplePosition,
        Vector3 currentShieldTranslation,
        ref float minimumSqrDistance)
    {
        // 移動中のMeshFilterを参照しつつ、盾が元位置にある状態の距離を測る。
        Vector3 sampleForOriginalShield =
            samplePosition + currentShieldTranslation;

        foreach (MeshFilter meshFilter in shieldMeshFilters)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null ||
                !meshFilter.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector3 meshLocalSample =
                meshFilter.transform.InverseTransformPoint(sampleForOriginalShield);
            Vector3 nearestMeshLocalPoint =
                meshFilter.sharedMesh.bounds.ClosestPoint(meshLocalSample);
            Vector3 nearestWorldPoint =
                meshFilter.transform.TransformPoint(nearestMeshLocalPoint);

            float sqrDistance =
                (sampleForOriginalShield - nearestWorldPoint).sqrMagnitude;

            if (sqrDistance < minimumSqrDistance)
            {
                minimumSqrDistance = sqrDistance;
            }
        }
    }

    private void ApplyPushDistance()
    {
        Vector3 worldPushDirection = shieldParent.TransformDirection(
            pushDirectionInParentSpace.normalized);
        Vector3 parentLocalOffset = shieldParent.InverseTransformVector(
            worldPushDirection * currentPushDistance);

        shieldRoot.localPosition =
            originalShieldLocalPosition + parentLocalOffset;
    }
}
