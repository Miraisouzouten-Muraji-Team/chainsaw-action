using UnityEngine;

/// <summary>XY平面の床を検知し、指定オブジェクトのローカルX軸を坂道に沿わせます。</summary>
[DefaultExecutionOrder(11000)]
public sealed class SlopeRotationFollower : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("坂道に合わせて回転させる表示用オブジェクトです。物理判定を持つプレイヤー本体ではなく、モデルや表示用の親を指定してください。")]
    [SerializeField] private Transform rotationTarget;

    [Tooltip("床を調べる基準位置です。足元や刃先付近の空オブジェクトを指定してください。未指定なら、このスクリプトの位置を使います。")]
    [SerializeField] private Transform probePoint;

    [Tooltip("検知から除外するプレイヤーの親です。未指定なら、このスクリプトが付いたオブジェクトの最上位の親を使います。")]
    [SerializeField] private Transform ignoredRoot;

    [Header("坂道の検知")]
    [Tooltip("床と坂道に設定しているLayerを選択してください。")]
    [SerializeField] private LayerMask groundLayers;

    [Tooltip("基準位置から、レイの開始位置を上にずらす距離です。")]
    [SerializeField, Min(0f)] private float probeHeight = 0.5f;

    [Tooltip("基準位置より下を調べる距離です。空中でも床を拾う場合は小さくしてください。")]
    [SerializeField, Min(0.01f)] private float probeDepth = 0.8f;

    [Tooltip("床として扱う坂道の最大角度です。壁と天井は対象にしません。")]
    [SerializeField, Range(0f, 89f)] private float maximumSlopeAngle = 75f;

    [Tooltip("床のTriggerも検知する場合にオンにしてください。")]
    [SerializeField] private bool includeTriggers;

    [Header("回転の補間")]
    [Tooltip("坂道の角度に近づくまでの目安の秒数です。小さいほど素早く、大きいほどゆっくり変化します。")]
    [SerializeField, Min(0.01f)] private float smoothTime = 0.12f;

    [Tooltip("床が見つからないとき、開始時のローカル回転へ滑らかに戻します。オフなら最後の補正角度を維持します。")]
    [SerializeField] private bool returnWhenNoGround = true;

    private const int HIT_BUFFER_SIZE = 32;
    private const float MIN_DIRECTION_SQR = 0.000001f;
    private readonly RaycastHit[] hits = new RaycastHit[HIT_BUFFER_SIZE];
    private Quaternion initialLocalRotation;
    private Vector3 groundNormal;
    private float currentAngle;
    private float angleVelocity;
    private bool hasGround;
    private bool initialized;

    /// <summary>参照を確認し、補正の基準となる回転を保存します。</summary>
    private void Awake()
    {
        if (rotationTarget == null)
        {
            Debug.LogError("SlopeRotationFollower：回転対象を設定してください。", this);
            enabled = false;
            return;
        }

        if (ignoredRoot == null) ignoredRoot = transform.root;
        initialLocalRotation = rotationTarget.localRotation;
        initialized = true;
        DetectGround();
    }

    /// <summary>物理更新に合わせて、足元の床と坂道を検知します。</summary>
    private void FixedUpdate()
    {
        DetectGround();
    }

    /// <summary>自分のColliderと壁を除外し、最も近い床の法線を保存します。</summary>
    private void DetectGround()
    {
        Vector3 point = probePoint != null ? probePoint.position : transform.position;
        Vector3 origin = point + Vector3.up * probeHeight;
        float distance = probeHeight + probeDepth;
        QueryTriggerInteraction triggerMode = includeTriggers
            ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;
        int count = Physics.RaycastNonAlloc(
            origin, Vector3.down, hits, distance, groundLayers, triggerMode);
        RaycastHit[] results = hits;

        // 検知数が上限に達した場合も、近い床を取りこぼさないよう再取得します。
        if (count == hits.Length)
        {
            results = Physics.RaycastAll(
                origin, Vector3.down, distance, groundLayers, triggerMode);
            count = results.Length;
        }

        hasGround = false;
        float nearestDistance = float.PositiveInfinity;
        float minimumNormalY = Mathf.Cos(maximumSlopeAngle * Mathf.Deg2Rad);
        for (int index = 0; index < count; index++)
        {
            RaycastHit hit = results[index];
            if (hit.collider == null || hit.distance >= nearestDistance) continue;
            Transform hitTransform = hit.collider.transform;
            if (ignoredRoot != null &&
                (hitTransform == ignoredRoot || hitTransform.IsChildOf(ignoredRoot))) continue;

            Vector3 normal = hit.normal;
            normal.z = 0f;
            if (normal.sqrMagnitude < MIN_DIRECTION_SQR) continue;
            normal.Normalize();
            if (normal.y < minimumNormalY) continue;

            nearestDistance = hit.distance;
            groundNormal = normal;
            hasGround = true;
        }
    }

    /// <summary>描画直前に回転を補間し、物理更新間の表示も滑らかにつなぎます。</summary>
    private void LateUpdate()
    {
        if (!initialized || rotationTarget == null || Time.deltaTime <= 0f) return;

        float targetAngle = returnWhenNoGround ? 0f : currentAngle;
        if (hasGround && TryGetSlopeAngle(out float slopeAngle)) targetAngle = slopeAngle;

        currentAngle = Mathf.SmoothDampAngle(
            currentAngle, targetAngle, ref angleVelocity,
            Mathf.Max(0.01f, smoothTime), Mathf.Infinity, Time.deltaTime);
        rotationTarget.localRotation = initialLocalRotation *
            Quaternion.AngleAxis(currentAngle, Vector3.forward);
    }

    /// <summary>親の回転と左右反転を考慮して、床に沿うローカル補正角度を求めます。</summary>
    private bool TryGetSlopeAngle(out float slopeAngle)
    {
        slopeAngle = 0f;
        Transform parent = rotationTarget.parent;
        Vector3 baseAxis = initialLocalRotation * Vector3.right;
        Vector3 worldAxis = parent != null ? parent.TransformVector(baseAxis) : baseAxis;
        worldAxis.z = 0f;
        if (worldAxis.sqrMagnitude < MIN_DIRECTION_SQR) return false;

        Vector3 tangent = new Vector3(groundNormal.y, -groundNormal.x, 0f);
        // 左右反転しても180度回転せず、開始時のX軸に近い接線を選びます。
        if (Vector3.Dot(tangent, worldAxis) < 0f) tangent = -tangent;
        Vector3 localTangent = parent != null ? parent.InverseTransformVector(tangent) : tangent;
        localTangent = Quaternion.Inverse(initialLocalRotation) * localTangent;
        if (localTangent.x * localTangent.x + localTangent.y * localTangent.y < MIN_DIRECTION_SQR)
            return false;

        slopeAngle = Mathf.Atan2(localTangent.y, localTangent.x) * Mathf.Rad2Deg;
        return true;
    }

    /// <summary>無効化時に、このスクリプトによる回転補正を解除します。</summary>
    private void OnDisable()
    {
        if (initialized && rotationTarget != null)
            rotationTarget.localRotation = initialLocalRotation;
        currentAngle = 0f;
        angleVelocity = 0f;
        hasGround = false;
    }

    /// <summary>選択時に、床を検知するレイの範囲を表示します。</summary>
    private void OnDrawGizmosSelected()
    {
        Vector3 point = probePoint != null ? probePoint.position : transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(point + Vector3.up * probeHeight, point - Vector3.up * probeDepth);
    }
}
