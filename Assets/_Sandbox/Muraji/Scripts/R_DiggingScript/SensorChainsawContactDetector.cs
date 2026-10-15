using UnityEngine;

// ChainsawSurfaceは既存PlayerAnimatorと同じ型を共有する。
// ここでは再定義しない。

public struct SensorChainsawContact
{
    public Collider Collider;
    public EnemyAttackHitReceiver Enemy;
    public ChainsawSurface Surface;
    public Vector3 Point;
    public Vector3 Normal;
}

public class SensorChainsawContactDetector : MonoBehaviour
{
    private const int NORMAL_SAMPLE_COUNT = 7;
    private const float POINT_TOLERANCE = 0.002f;

    [Header("Player本体と、判定専用の親（モデルとは分ける）")]
    [SerializeField] private Transform ownerRoot;

    [Tooltip("Player直下、Local Position / Rotation = 0、Scale = 1。子を右向き用に配置")]
    [SerializeField] private Transform sensorRoot;

    [Header("正面の上・中央・下。すべて3D BoxCollider / Is Trigger ON")]

    [Tooltip("天井専用。床・壁・敵には使用しない")]
    [SerializeField] private BoxCollider upperSensor;

    [Tooltip("敵専用。壁はFront Rayで取得する")]
    [SerializeField] private BoxCollider middleSensor;

    [Tooltip("床専用。斜面も面の法線で床かどうか判定する")]
    [SerializeField] private BoxCollider lowerSensor;

    [Header("接触判定パラメータ")]
    [Tooltip("法線のしきい値とRayの長さを使用します。食い込み対象はTagで判定します。Terrain Layersは既存Controllerの強衝突判定への公開値としてのみ維持します。")]
    [SerializeField] private ContactDetectorParameter contactDetectorParameter;

    [Header("敵への食い込み判定")]
    [Tooltip("食い込み対象の敵のTag名です。敵のColliderからEnemyAttackHitReceiverがある親までのいずれかに、このTagを設定してください。敵のLayerは検出条件に使用しません。")]
    [SerializeField] private string enemyTag = "Enemy";

    [Tooltip("床・壁・天井・遮蔽物として扱う地形のTag名です。Collider自身または親に設定してください。敵Receiverのある物体は地形から除外します。")]
    [SerializeField] private string terrainTag = "Ground";

    [Header("壁専用の前方Ray")]

    [Tooltip("Player直下の胴体位置。モデルやアニメーションする骨には置かない")]
    [SerializeField] private Transform frontRayOrigin;

    [Header("直近の照会結果（再生中の確認用）")]
    [SerializeField] private ChainsawSurface detectedSurface;
    [SerializeField] private Collider detectedCollider;
    [SerializeField] private Vector3 detectedNormal;

    private Collider[] overlapBuffer = new Collider[32];

    private SensorPlayerController playerController;

    // 所有者のControllerから床・壁分類の共通値を取得する。
    private float ResolvedSurfaceNormalThreshold
    {
        get
        {
            if (playerController == null && ownerRoot != null)
                playerController = ownerRoot.GetComponent<SensorPlayerController>();
            return playerController != null
                ? playerController.GroundNormalThreshold
                : contactDetectorParameter.SurfaceNormalThreshold;
        }
    }

    private bool configured;
    private float facing = 1f;
    // 既存Controllerの強衝突判定との互換用。Detector内の対象選別には使用しない。
    public int TerrainLayerMask => contactDetectorParameter != null ? contactDetectorParameter.TerrainLayers.value : 0;

    // Receiverを変更せず、敵の有効状態と同じオブジェクトのHPから食い込み可能かを調べる。
    public static bool CanReceiveEnemyHit(EnemyAttackHitReceiver enemy)
    {
        if (enemy == null || !enemy.isActiveAndEnabled)
        {
            return false;
        }

        EnemyHealth health = enemy.GetComponent<EnemyHealth>();

        return health != null &&
            health.isActiveAndEnabled &&
            !health.IsDead;
    }

    // 壁の優先順位に隠されないように、Upperだけで天井を調べる。
    public bool TryGetCeilingContact(out SensorChainsawContact contact)
    {
        contact = default;

        if (!isActiveAndEnabled || !configured)
        {
            return false;
        }

        Physics.SyncTransforms();

        float bestScore = float.PositiveInfinity;

        CollectContacts(
            upperSensor,
            null,
            ChainsawSurface.None,
            ref contact,
            ref bestScore
        );

        return contact.Collider != null &&
            contact.Surface == ChainsawSurface.Ceiling;
    }
    // 必須コンポーネントと設定アセットの参照が揃っているかを確認する。
    private void Awake()
    {
        if (contactDetectorParameter == null)
        {
            Debug.LogError("食い込み判定：Contact Detector Parameterを設定してください。", this);
            enabled = false;
            return;
        }

        if (ownerRoot == null)
        {
            ownerRoot = transform;
        }

        configured =
            sensorRoot != null &&
            sensorRoot != ownerRoot &&
            sensorRoot.IsChildOf(ownerRoot) &&
            IsValidSensor(upperSensor) &&
            IsValidSensor(middleSensor) &&
            IsValidSensor(lowerSensor) &&
            upperSensor != middleSensor &&
            upperSensor != lowerSensor &&
            middleSensor != lowerSensor;

        if (!configured)
        {
            Debug.LogError(
                "食い込み判定：Sensor Rootと、異なる3つの子BoxCollider" +
                "（Is Trigger ON）を設定してください。",
                this
            );

            enabled = false;
        }

        if (frontRayOrigin == null)
        {
            Debug.LogWarning(
                "壁判定用のFront Ray OriginをPlayerの胴体位置に設定してください。" +
                "未設定では壁を検出しません。",
                this
            );
        }
    }

    private bool IsValidSensor(BoxCollider sensor)
    {
        return
            sensor != null &&
            sensor.isTrigger &&
            sensor.transform != sensorRoot &&
            sensor.transform.IsChildOf(sensorRoot);
    }

    public void SetFacingDirection(float direction)
    {
        if (!configured || !isActiveAndEnabled)
        {
            return;
        }

        if (Mathf.Abs(direction) > 0.01f)
        {
            facing = Mathf.Sign(direction);
        }

        if (sensorRoot != null)
        {
            sensorRoot.localRotation = Quaternion.Euler(
                0f,
                facing < 0f ? 180f : 0f,
                0f
            );
        }
    }

    public bool TryGetContact(
        Collider preferred,
        out SensorChainsawContact contact,
        ChainsawSurface currentSurface = ChainsawSurface.None,
        float facingDirection = 0f)
    {
        contact = default;

        detectedSurface = ChainsawSurface.None;
        detectedCollider = null;
        detectedNormal = Vector3.zero;

        if (!isActiveAndEnabled || !configured)
        {
            return false;
        }

        SetFacingDirection(facingDirection);

        // 向き変更直後のColliderも入力時に正しく照会する。
        Physics.SyncTransforms();

        float bestScore = float.PositiveInfinity;

        CollectContacts(
            upperSensor,
            preferred,
            currentSurface,
            ref contact,
            ref bestScore
        );

        CollectContacts(
            middleSensor,
            preferred,
            currentSurface,
            ref contact,
            ref bestScore
        );

        CollectContacts(
            lowerSensor,
            preferred,
            currentSurface,
            ref contact,
            ref bestScore
        );

        CollectWallContact(
            preferred,
            currentSurface,
            ref contact,
            ref bestScore
        );

        // 床候補は通常移動と同じ支持面へそろえる。
        // 敵・壁・天井の優先順位は既存どおり維持する。
        ApplyPlayerGroundSupport(currentSurface, ref contact);

        detectedSurface = contact.Surface;
        detectedCollider = contact.Collider;
        detectedNormal = contact.Normal;

        return contact.Collider != null;
    }

    // 床を検出した場合、または床食い込み継続中だけ、実際の支持面を共有する。
    private void ApplyPlayerGroundSupport(
        ChainsawSurface currentSurface,
        ref SensorChainsawContact contact)
    {
        if (playerController == null && ownerRoot != null)
            playerController = ownerRoot.GetComponent<SensorPlayerController>();

        bool canReplaceFloor = contact.Surface == ChainsawSurface.Floor ||
            (contact.Collider == null && currentSurface == ChainsawSurface.Floor);

        if (!canReplaceFloor || playerController == null ||
            !playerController.TryGetGroundSupport(out SensorChainsawContact ground) ||
            !IsTerrainTarget(ground.Collider) ||
            ground.Normal.y < ResolvedSurfaceNormalThreshold)
        {
            return;
        }

        contact = ground;
    }

    private void CollectContacts(
        BoxCollider sensor,
        Collider preferred,
        ChainsawSurface currentSurface,
        ref SensorChainsawContact contact,
        ref float bestScore)
    {
        if (!sensor.enabled || !sensor.gameObject.activeInHierarchy)
        {
            return;
        }

        Vector3 center = sensor.transform.TransformPoint(sensor.center);
        Vector3 scale = sensor.transform.lossyScale;

        Vector3 half = Vector3.Scale(
            sensor.size * 0.5f,
            new Vector3(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)
            )
        );

        int count;

        // バッファ満杯時に候補を黙って捨てない。
        while (true)
        {
            count = Physics.OverlapBoxNonAlloc(
                center,
                half,
                overlapBuffer,
                sensor.transform.rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide
            );

            if (count < overlapBuffer.Length)
            {
                break;
            }

            System.Array.Resize(
                ref overlapBuffer,
                overlapBuffer.Length * 2
            );
        }

        for (int i = 0; i < count; i++)
        {
            Collider target = overlapBuffer[i];

            if (target == null || IsOwner(target))
            {
                continue;
            }

            // 敵はMiddleだけで検出する。
            if (sensor == middleSensor)
            {
                EnemyAttackHitReceiver enemy =
                    target.GetComponentInParent<EnemyAttackHitReceiver>();

                if (!CanReceiveEnemyHit(enemy) || !HasEnemyTag(target, enemy))
                {
                    continue;
                }

                Vector3 point = target.ClosestPoint(center);

                Vector3 sightOrigin = new Vector3(
                    ownerRoot.position.x,
                    center.y,
                    center.z
                );

                if (IsBlocked(sightOrigin, point, target))
                {
                    continue;
                }

                Consider(
                    target,
                    enemy,
                    ChainsawSurface.Enemy,
                    point,
                    Vector3.zero,
                    preferred,
                    currentSurface,
                    ref contact,
                    ref bestScore
                );

                continue;
            }

            if (target.isTrigger || !IsTerrainTarget(target))
            {
                continue;
            }

            // 床はLowerだけ、天井はUpperだけで検出する。
            // 壁は別の前方Rayで検出する。
            if (sensor == lowerSensor)
            {
                SampleSurface(
                    sensor,
                    target,
                    Vector3.down,
                    ChainsawSurface.Floor,
                    preferred,
                    currentSurface,
                    ref contact,
                    ref bestScore
                );
            }
            else if (sensor == upperSensor)
            {
                SampleSurface(
                    sensor,
                    target,
                    Vector3.up,
                    ChainsawSurface.Ceiling,
                    preferred,
                    currentSurface,
                    ref contact,
                    ref bestScore
                );
            }
        }
    }

    // 地形Tagを階層から確認し、敵自身が壁や遮蔽物として採用されることを防ぐ。
    private bool IsTerrainTarget(Collider target)
    {
        if (target == null || string.IsNullOrWhiteSpace(terrainTag) ||
            target.GetComponentInParent<EnemyAttackHitReceiver>() != null)
        {
            return false;
        }

        Transform current = target.transform;
        while (current != null)
        {
            if (current.gameObject.tag == terrainTag)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    // Colliderから受信先までの階層で敵Tagを確認する。敵の子Colliderにも対応する。
    private bool HasEnemyTag(Collider target, EnemyAttackHitReceiver enemy)
    {
        if (target == null || enemy == null || string.IsNullOrWhiteSpace(enemyTag))
        {
            return false;
        }

        Transform receiverTransform = enemy.transform;
        Transform current = target.transform;

        while (current != null)
        {
            // Tag文字列を比較し、未作成のTag名でも例外を発生させない。
            if (current.gameObject.tag == enemyTag)
            {
                return true;
            }

            // 敵より外側の親に付いたTagを誤って採用しない。
            if (current == receiverTransform)
            {
                break;
            }

            current = current.parent;
        }

        return false;
    }

    private void SampleSurface(
        BoxCollider sensor,
        Collider target,
        Vector3 direction,
        ChainsawSurface expectedSurface,
        Collider preferred,
        ChainsawSurface currentSurface,
        ref SensorChainsawContact contact,
        ref float bestScore)
    {
        Bounds bounds = sensor.bounds;

        bool vertical = Mathf.Abs(direction.y) > 0.5f;
        float backoff = Mathf.Max(0.01f, contactDetectorParameter.NormalRayBackoff);

        for (int i = 0; i < NORMAL_SAMPLE_COUNT; i++)
        {
            float fraction = Mathf.Lerp(
                0.001f,
                0.999f,
                i / (float)(NORMAL_SAMPLE_COUNT - 1)
            );

            Vector3 origin = bounds.center;
            float distance;

            if (vertical)
            {
                origin.x = Mathf.Lerp(
                    bounds.min.x,
                    bounds.max.x,
                    fraction
                );

                origin.y = direction.y < 0f
                    ? bounds.max.y + backoff
                    : bounds.min.y - backoff;

                distance = bounds.size.y + backoff * 2f;
            }
            else
            {
                origin.y = Mathf.Lerp(
                    bounds.min.y,
                    bounds.max.y,
                    fraction
                );

                origin.x = direction.x > 0f
                    ? bounds.min.x - backoff
                    : bounds.max.x + backoff;

                distance = bounds.size.x + backoff * 2f;
            }

            // センサーが斜面に深く重なっていても、
            // Ray始点が対象Collider内部にならないようにする。
            Vector3 visibilityOrigin = origin;
            Bounds targetBounds = target.bounds;
            float padding = Mathf.Max(0.001f, backoff);

            if (vertical)
            {
                origin.y = direction.y < 0f
                    ? Mathf.Max(origin.y, targetBounds.max.y + padding)
                    : Mathf.Min(origin.y, targetBounds.min.y - padding);

                distance = direction.y < 0f
                    ? origin.y - bounds.min.y + POINT_TOLERANCE
                    : bounds.max.y - origin.y + POINT_TOLERANCE;
            }
            else
            {
                origin.x = direction.x > 0f
                    ? Mathf.Min(origin.x, targetBounds.min.x - padding)
                    : Mathf.Max(origin.x, targetBounds.max.x + padding);

                distance = direction.x > 0f
                    ? bounds.max.x - origin.x + POINT_TOLERANCE
                    : origin.x - bounds.min.x + POINT_TOLERANCE;
            }

            if (!target.Raycast(
                    new Ray(origin, direction),
                    out RaycastHit hit,
                    distance))
            {
                continue;
            }

            // Rayは法線取得用。
            // 担当センサーの範囲外にある面は採用しない。
            if (!ContainsPoint(sensor, hit.point) ||
                Mathf.Abs(hit.normal.z) > 0.5f)
            {
                continue;
            }

            Vector3 normal = new Vector3(
                hit.normal.x,
                hit.normal.y,
                0f
            ).normalized;

            ChainsawSurface surface =
                normal.y >= ResolvedSurfaceNormalThreshold
                    ? ChainsawSurface.Floor
                    : normal.y <= -ResolvedSurfaceNormalThreshold
                        ? ChainsawSurface.Ceiling
                        : ChainsawSurface.Wall;

            if (surface != expectedSurface)
            {
                continue;
            }

            if (IsBlocked(visibilityOrigin, hit.point, target))
            {
                continue;
            }

            Consider(
                target,
                null,
                surface,
                hit.point,
                normal,
                preferred,
                currentSurface,
                ref contact,
                ref bestScore
            );
        }
    }

    private void CollectWallContact(
        Collider preferred,
        ChainsawSurface currentSurface,
        ref SensorChainsawContact contact,
        ref float bestScore)
    {
        if (frontRayOrigin == null)
        {
            return;
        }

        Vector3 origin = frontRayOrigin.position;
        Vector3 direction = Vector3.right * facing;
        float distance = Mathf.Max(0.01f, contactDetectorParameter.FrontRayDistance);

        Debug.DrawRay(
            origin,
            direction * distance,
            Color.red
        );

        // 手前の地形が坂や床なら、その奥の壁を拾わない。
        RaycastHit nearestHit = default;
        float nearestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in Physics.RaycastAll(
            origin,
            direction,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (!IsTerrainTarget(hit.collider) || IsOwner(hit.collider))
            {
                continue;
            }

            if (hit.distance > nearestDistance)
            {
                continue;
            }

            if (hit.distance == nearestDistance &&
                nearestHit.collider != null &&
                hit.collider.GetInstanceID() >=
                nearestHit.collider.GetInstanceID())
            {
                continue;
            }

            nearestHit = hit;
            nearestDistance = hit.distance;
        }

        if (nearestHit.collider == null ||
            Mathf.Abs(nearestHit.normal.z) > 0.5f)
        {
            return;
        }

        Vector3 normal = new Vector3(
            nearestHit.normal.x,
            nearestHit.normal.y,
            0f
        ).normalized;

        // 床・天井に分類される面は、壁として採用しない。
        if (Mathf.Abs(normal.y) >= ResolvedSurfaceNormalThreshold)
        {
            return;
        }

        // プレイヤー正面に向いた壁だけを採用する。
        if (normal.x * facing >= -0.1f)
        {
            return;
        }

        Consider(
            nearestHit.collider,
            null,
            ChainsawSurface.Wall,
            nearestHit.point,
            normal,
            preferred,
            currentSurface,
            ref contact,
            ref bestScore
        );
    }

    private bool ContainsPoint(
        BoxCollider sensor,
        Vector3 point)
    {
        Vector3 local =
            sensor.transform.InverseTransformPoint(point) -
            sensor.center;

        Vector3 half = sensor.size * 0.5f;

        return
            Mathf.Abs(local.x) <= half.x + POINT_TOLERANCE &&
            Mathf.Abs(local.y) <= half.y + POINT_TOLERANCE &&
            Mathf.Abs(local.z) <= half.z + POINT_TOLERANCE;
    }

    private bool IsBlocked(
        Vector3 origin,
        Vector3 point,
        Collider target)
    {
        Vector3 delta = point - origin;
        float distance = delta.magnitude;

        if (distance <= POINT_TOLERANCE)
        {
            return false;
        }

        foreach (RaycastHit hit in Physics.RaycastAll(
            origin,
            delta / distance,
            distance - POINT_TOLERANCE,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != target &&
                IsTerrainTarget(hit.collider) &&
                !IsOwner(hit.collider))
            {
                return true;
            }
        }

        return false;
    }

    private void Consider(
        Collider target,
        EnemyAttackHitReceiver enemy,
        ChainsawSurface surface,
        Vector3 point,
        Vector3 normal,
        Collider preferred,
        ChainsawSurface currentSurface,
        ref SensorChainsawContact contact,
        ref float bestScore)
    {
        float distance = Vector3.Distance(
            ownerRoot.position,
            point
        );

        float score = distance / (1f + distance);

        if (surface == ChainsawSurface.Enemy)
        {
            score -= target == preferred ? 400f : 300f;
        }
        else if (surface == ChainsawSurface.Wall &&
                 currentSurface != ChainsawSurface.Ceiling)
        {
            score -= 200f;
        }
        else if (surface == currentSurface)
        {
            score -= 100f;
        }

        if (surface != ChainsawSurface.Enemy &&
            target == preferred)
        {
            score -= 0.25f;
        }

        if (score > bestScore)
        {
            return;
        }

        if (score == bestScore &&
            contact.Collider != null &&
            target.GetInstanceID() >= contact.Collider.GetInstanceID())
        {
            return;
        }

        bestScore = score;

        contact = new SensorChainsawContact
        {
            Collider = target,
            Enemy = enemy,
            Surface = surface,
            Point = point,
            Normal = normal
        };
    }

    private bool IsOwner(Collider target)
    {
        return
            target.transform == ownerRoot ||
            target.transform.IsChildOf(ownerRoot);
    }

    private void OnDrawGizmosSelected()
    {
        DrawSensor(upperSensor, Color.cyan);
        DrawSensor(middleSensor, Color.yellow);
        DrawSensor(lowerSensor, Color.green);

        if (frontRayOrigin != null)
        {
            Color previousColor = Gizmos.color;
            Gizmos.color = Color.red;

            Vector3 direction =
                Vector3.right *
                (Application.isPlaying ? facing : 1f);

            Gizmos.DrawLine(
                frontRayOrigin.position,
                frontRayOrigin.position +
                direction * Mathf.Max(0.01f, contactDetectorParameter.FrontRayDistance)
            );

            Gizmos.color = previousColor;
        }
    }

    private static void DrawSensor(
        BoxCollider sensor,
        Color color)
    {
        if (sensor == null)
        {
            return;
        }

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = sensor.transform.localToWorldMatrix;
        Gizmos.color = color;

        Gizmos.DrawWireCube(
            sensor.center,
            sensor.size
        );

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
