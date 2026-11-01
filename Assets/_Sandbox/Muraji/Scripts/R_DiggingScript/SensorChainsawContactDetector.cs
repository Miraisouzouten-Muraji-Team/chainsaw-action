using System.Collections.Generic;
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

    [Tooltip("上・中央・下の判定をまとめる親です。実行開始前に右向き用の位置・角度へ配置してください。Player本体を中心に、初期位置と初期角度の両方を左右反転します。アニメーションで動かさない専用の親を指定し、Scaleは1にしてください。")]
    [SerializeField] private Transform sensorRoot;

    // Player本体の現在のY回転を重ねないよう、右向き配置をワールド基準で保存する。
    private Quaternion rightSensorWorldRotation;
    private Vector3 rightSensorWorldOffset;

    [Header("正面の上・中央・下。すべて3D BoxCollider / Is Trigger ON")]

    [Tooltip("天井専用。床・壁・敵には使用しない")]
    [SerializeField] private BoxCollider upperSensor;

    [Tooltip("敵専用。壁はFront Rayで取得する")]
    [SerializeField] private BoxCollider middleSensor;

    [Tooltip("床専用。斜面も面の法線で床かどうか判定する")]
    [SerializeField] private BoxCollider lowerSensor;

    [Header("接触判定パラメータ")]
    [Tooltip("地形と敵のレイヤー、法線のしきい値、Rayの長さを管理するScriptableObject")]
    [SerializeField] private ContactDetectorParameter contactDetectorParameter;

    [Header("壁専用の前方Ray")]

    [Tooltip("Player直下の胴体位置。モデルやアニメーションする骨には置かない")]
    [SerializeField] private Transform frontRayOrigin;

    [Header("直近の照会結果（再生中の確認用）")]
    [SerializeField] private ChainsawSurface detectedSurface;
    [SerializeField] private Collider detectedCollider;
    [SerializeField] private Vector3 detectedNormal;

    private Collider[] overlapBuffer = new Collider[32];
    private RaycastHit[] movementHitBuffer = new RaycastHit[32];

    private SensorPlayerController playerController;

    // 敵専用センサーの重なりと今回の移動経路を調べ、固定食い込みとは別に命中候補を集める。
    public void CollectMovementEnemyContacts(
        List<SensorChainsawContact> destination, Vector3 displacement, Vector3 surfaceNormal)
    {
        destination.Clear();
        if (!isActiveAndEnabled || !configured ||
            !middleSensor.enabled || !middleSensor.gameObject.activeInHierarchy)
            return;

        Physics.SyncTransforms();
        Vector3 center = middleSensor.transform.TransformPoint(middleSensor.center);
        Vector3 scale = middleSensor.transform.lossyScale;
        Vector3 half = Vector3.Scale(middleSensor.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Quaternion rotation = middleSensor.transform.rotation;
        int count;
        do
        {
            count = Physics.OverlapBoxNonAlloc(center, half, overlapBuffer, rotation,
                contactDetectorParameter.EnemyLayers.value, QueryTriggerInteraction.Collide);
            if (count < overlapBuffer.Length) break;
            System.Array.Resize(ref overlapBuffer, overlapBuffer.Length * 2);
        } while (true);

        for (int i = 0; i < count; i++)
        {
            Collider target = overlapBuffer[i];
            if (target != null)
                AddMovementEnemyContact(destination, target, center, target.ClosestPoint(center));
        }

        float distance = displacement.magnitude;
        if (distance <= POINT_TOLERANCE) return;
        Vector3 direction = displacement / distance;

        // 地形を越えた先へ先読み攻撃しないよう、進行方向を塞ぐ面までに限定する。
        do
        {
            count = Physics.BoxCastNonAlloc(center, half, direction, movementHitBuffer,
                rotation, distance, contactDetectorParameter.TerrainLayers.value,
                QueryTriggerInteraction.Ignore);
            if (count < movementHitBuffer.Length) break;
            System.Array.Resize(ref movementHitBuffer, movementHitBuffer.Length * 2);
        } while (true);
        float allowedDistance = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = movementHitBuffer[i];
            // 張り付き用速度で支持面へ押していても、その面で経路を0に切り詰めない。
            if (hit.collider != null && !IsOwner(hit.collider) &&
                Vector3.Dot(hit.normal, surfaceNormal) < 0.99f &&
                Vector3.Dot(direction, hit.normal) < -0.01f)
                allowedDistance = Mathf.Min(allowedDistance, hit.distance);
        }
        if (allowedDistance <= POINT_TOLERANCE) return;

        do
        {
            count = Physics.BoxCastNonAlloc(center, half, direction, movementHitBuffer,
                rotation, allowedDistance, contactDetectorParameter.EnemyLayers.value,
                QueryTriggerInteraction.Collide);
            if (count < movementHitBuffer.Length) break;
            System.Array.Resize(ref movementHitBuffer, movementHitBuffer.Length * 2);
        } while (true);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = movementHitBuffer[i];
            if (hit.collider != null)
                AddMovementEnemyContact(destination, hit.collider,
                    center + direction * hit.distance, hit.point);
        }
    }

    // 無効な敵・壁の裏側・同じ敵の複数Colliderを除き、移動接触の命中候補を追加する。
    private void AddMovementEnemyContact(List<SensorChainsawContact> destination,
        Collider target, Vector3 sensorCenter, Vector3 point)
    {
        if (IsOwner(target)) return;
        EnemyAttackHitReceiver enemy = target.GetComponentInParent<EnemyAttackHitReceiver>();
        if (!CanReceiveEnemyHit(enemy)) return;

        Vector3 sightOrigin = new Vector3(
            ownerRoot.position.x, sensorCenter.y, sensorCenter.z);
        if (IsBlocked(sightOrigin, point, target)) return;
        for (int i = 0; i < destination.Count; i++)
            if (destination[i].Enemy == enemy) return;

        destination.Add(new SensorChainsawContact
        {
            Collider = target,
            Enemy = enemy,
            Surface = ChainsawSurface.Enemy,
            Point = point,
            Normal = Vector3.zero
        });
    }

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
    /// <summary>敵接触攻撃の検索に使用する、既存設定の敵レイヤー。</summary>
    public int EnemyLayerMask => contactDetectorParameter != null ? contactDetectorParameter.EnemyLayers.value : 0;

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

        if (configured)
        {
            // 起動時の右向き配置を保存する。以降のPlayer本体の左右反転は基準へ加えない。
            rightSensorWorldRotation = sensorRoot.rotation;
            rightSensorWorldOffset = sensorRoot.position - ownerRoot.position;
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

    // Playerの位置だけに追従し、向きは初期配置から一度だけ反転して二重回転を防ぐ。
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
            Quaternion facingRotation = Quaternion.Euler(
                0f,
                facing < 0f ? 180f : 0f,
                0f
            );
            // Player本体も180度回る構成なので、現在のownerRoot.rotationを掛けると
            // 左向きの180度が二重に入り、ワールドで右向きへ戻ってしまう。
            Vector3 position = ownerRoot.position + facingRotation * rightSensorWorldOffset;
            Quaternion rotation = facingRotation * rightSensorWorldRotation;
            // 最終ワールド姿勢を指定し、親の回転分はUnityのTransform変換で相殺する。
            sensorRoot.SetPositionAndRotation(position, rotation);
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

        // 地形食い込み中は敵を「食い込み先」として再選択しない。
        // 未食い込み時は従来どおり敵への直接食い込みを許可する。
        if (currentSurface == ChainsawSurface.None)
        {
            CollectContacts(
                middleSensor,
                preferred,
                currentSurface,
                ref contact,
                ref bestScore
            );
        }

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
            (TerrainLayerMask & (1 << ground.Collider.gameObject.layer)) == 0 ||
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
                sensor == middleSensor
                    ? contactDetectorParameter.EnemyLayers.value
                    : contactDetectorParameter.TerrainLayers.value,
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

            bool isEnemyLayer =
                (contactDetectorParameter.EnemyLayers.value & (1 << target.gameObject.layer)) != 0;

            // 敵はMiddleだけで検出する。
            if (sensor == middleSensor)
            {
                if (!isEnemyLayer)
                {
                    continue;
                }

                EnemyAttackHitReceiver enemy =
                    target.GetComponentInParent<EnemyAttackHitReceiver>();

                if (!CanReceiveEnemyHit(enemy))
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

            if (target.isTrigger ||
                (contactDetectorParameter.TerrainLayers.value & (1 << target.gameObject.layer)) == 0)
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
            contactDetectorParameter.TerrainLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || IsOwner(hit.collider))
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
            contactDetectorParameter.TerrainLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != target && !IsOwner(hit.collider))
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
