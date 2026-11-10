using System.Collections.Generic;
using UnityEngine;

// 食い込み先の種別は、プロジェクト内の既存ChainsawSurfaceを使用する。

public struct SensorChainsawContact
{
    public Collider Collider;
    public EnemyAttackHitReceiver Enemy;
    public ChainsawSurface Surface;
    public Vector3 Point;
    public Vector3 Normal;
    // 天井継続の本体BoxCastが求めた安全な中心位置。通常の床・壁・敵判定では使用しない。
    public bool HasCeilingBodyTarget;
    public Vector3 CeilingBodyTarget;
    // 床継続中の本体BoxCastで求めた、次の横位置に対応する安全な中心位置。
    public bool HasFloorBodyTarget;
    public Vector3 FloorBodyTarget;
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
    private Rigidbody ownerBody;
    private readonly List<Collider> ceilingBodyColliders = new List<Collider>();
    private float ceilingBodyContactOffset;
    private const float CEILING_FOLLOW_SKIN = 0.005f;
    private const int CEILING_PATH_MAX_PASSES = 4;

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
        if (IsOwner(target) || !ChainsawContactZone.AllowsAttack(target)) return;
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

    // 食い込み開始は各センサーで調べ、天井継続中は今回の移動先を支える本体幅の照会で面を確定する。
    public bool TryGetContact(
        Collider preferred,
        out SensorChainsawContact contact,
        ChainsawSurface currentSurface = ChainsawSurface.None,
        float facingDirection = 0f,
        float ceilingMoveSpeed = 0f,
        float ceilingDeltaTime = 0f,
        Vector3 ceilingPreviousNormal = default)
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

        // 中央センサーは壁用Triggerの継続確認にも使用する。
        // 敵への新規食い込みはCollectContacts内で未食い込み時だけに制限する。
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

        // 開始時はUpper、継続中は本体の次の移動位置を支える天井で判定する。
        // 前方の遠い面や、後ろに残った斜面の法線だけで移動方向を決めない。
        if (currentSurface == ChainsawSurface.Ceiling)
        {
            if (contact.Surface != ChainsawSurface.Wall &&
                TryGetCeilingBodySupport(ceilingMoveSpeed, ceilingDeltaTime,
                ceilingPreviousNormal, out SensorChainsawContact support))
                contact = support;
            else if (contact.Surface == ChainsawSurface.Ceiling)
                contact = default;
        }

        // 床候補は通常移動と同じ支持面へそろえる。
        // 敵・壁・天井の優先順位は既存どおり維持する。
        ApplyPlayerGroundSupport(currentSurface, ref contact);

        // 床の開始条件は変えず、継続時だけ移動先の本体幅で実際の床高さを確定する。
        // 正面の壁は既存の床→壁遷移を優先する。
        if (currentSurface == ChainsawSurface.Floor &&
            (contact.Collider == null || contact.Surface == ChainsawSurface.Floor))
        {
            if (TryGetFloorBodySupport(ceilingMoveSpeed, ceilingDeltaTime,
                ceilingPreviousNormal, out SensorChainsawContact floorSupport))
                contact = floorSupport;
            else
                contact = default;
        }

        detectedSurface = contact.Surface;
        detectedCollider = contact.Collider;
        detectedNormal = contact.Normal;

        return contact.Collider != null;
    }

    // 同じRigidbodyに属する有効な非Triggerの本体形状だけを集め、ワールド範囲を求める。
    private bool TryGetCeilingBodyBounds(out Bounds bounds)
    {
        bounds = default;
        ceilingBodyContactOffset = 0f;
        if (ownerBody == null) ownerBody = ownerRoot.GetComponent<Rigidbody>();
        if (ownerBody == null) return false;
        ownerBody.GetComponentsInChildren<Collider>(true, ceilingBodyColliders);
        bool found = false;
        foreach (Collider bodyCollider in ceilingBodyColliders)
        {
            if (bodyCollider == null || !bodyCollider.enabled || bodyCollider.isTrigger ||
                !bodyCollider.gameObject.activeInHierarchy || bodyCollider.attachedRigidbody != ownerBody)
                continue;
            if (!found) bounds = bodyCollider.bounds;
            else bounds.Encapsulate(bodyCollider.bounds);
            ceilingBodyContactOffset = Mathf.Max(ceilingBodyContactOffset, bodyCollider.contactOffset);
            found = true;
        }
        return found;
    }

    // 本体中央の真上に実在する下面を確認し、BoxCastが角だけを拾う支持を除外する。
    private bool TryGetCeilingCenterFace(Collider target, Bounds bodyBounds, float centerX,
        float extraHeight, out RaycastHit face)
    {
        face = default;
        if (target == null || target.isTrigger || IsOwner(target)) return false;
        float backoff = Mathf.Max(0.01f, contactDetectorParameter.NormalRayBackoff);
        Vector3 origin = new Vector3(centerX, bodyBounds.min.y - backoff, bodyBounds.center.z);
        float distance = bodyBounds.size.y + backoff * 2f + Mathf.Max(0f, extraHeight);
        if (!target.Raycast(new Ray(origin, Vector3.up), out face, distance)) return false;
        if (face.point.y < bodyBounds.center.y || Mathf.Abs(face.normal.z) > 0.5f) return false;
        Vector3 normal = new Vector3(face.normal.x, face.normal.y, 0f).normalized;
        return normal.y <= -ResolvedSurfaceNormalThreshold;
    }

    // 次の横位置の本体幅を上向きにBoxCastし、頭が通れる高さを決める実際の天井を取得する。
    // 平面・斜面・凸の継ぎ目を同じ方法で調べ、Upperから一瞬外れても本体の支持を維持する。
    public bool TryGetCeilingBodySupport(float moveSpeed, float deltaTime,
        Vector3 previousNormal, out SensorChainsawContact contact)
    {
        contact = default;
        if (!isActiveAndEnabled || !configured) return false;
        if (!TryGetCeilingBodyBounds(out Bounds bounds)) return false;
        float travel = Mathf.Abs(moveSpeed) * Mathf.Max(0f, deltaTime);
        float probeDistance = Mathf.Max(bounds.extents.y,
            Mathf.Max(0.01f, contactDetectorParameter.NormalRayBackoff)) + travel + CEILING_FOLLOW_SKIN;
        Vector3 half = new Vector3(
            Mathf.Max(0.001f, bounds.extents.x),
            Mathf.Max(0.001f, bounds.extents.y),
            Mathf.Max(0.001f, bounds.extents.z));
        // 接線上の移動距離がtravelなので、横方向の先読みも最大travelに限定する。
        float horizontalRatio = previousNormal.sqrMagnitude > 0.01f
            ? Mathf.Abs(previousNormal.normalized.y) : 1f;
        Vector3 origin = bounds.center + Vector3.right * (moveSpeed * Mathf.Max(0f, deltaTime) * horizontalRatio)
            - Vector3.up * probeDistance;
        float nearestDistance = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.BoxCastAll(origin, half, Vector3.up,
            Quaternion.identity, probeDistance * 2f,
            contactDetectorParameter.TerrainLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || IsOwner(hit.collider) || hit.distance <= POINT_TOLERANCE ||
                hit.point.y < bounds.center.y || Mathf.Abs(hit.normal.z) > 0.5f)
                continue;
            Vector3 normal = new Vector3(hit.normal.x, hit.normal.y, 0f).normalized;
            if (normal.y > -ResolvedSurfaceNormalThreshold) continue;
            // 次の本体中央が天井の外へ出ているなら、幅の端だけを支えに接続し続けない。
            if (!TryGetCeilingCenterFace(hit.collider, bounds, origin.x,
                probeDistance * 2f, out RaycastHit centerFace)) continue;
            normal = new Vector3(centerFace.normal.x, centerFace.normal.y, 0f).normalized;
            if (hit.distance > nearestDistance) continue;
            if (hit.distance == nearestDistance && contact.Collider != null &&
                hit.collider.GetInstanceID() >= contact.Collider.GetInstanceID()) continue;
            nearestDistance = hit.distance;
            contact = new SensorChainsawContact
            {
                Collider = hit.collider,
                Surface = ChainsawSurface.Ceiling,
                Point = hit.point,
                Normal = normal,
                HasCeilingBodyTarget = true,
                // 本体を縮めずに調べ、両Colliderの接触余白も確保する。
                // 角で法線が真下になっても、許容する斜面側の余白を失わない高さへ下げる。
                CeilingBodyTarget = new Vector3(origin.x,
                    origin.y + hit.distance - GetCeilingClearance(hit.collider) /
                    Mathf.Max(0.01f, ResolvedSurfaceNormalThreshold), bounds.center.z)
            };
        }
        detectedSurface = contact.Surface;
        detectedCollider = contact.Collider;
        detectedNormal = contact.Normal;
        return contact.Collider != null;
    }

    // 本体と天井の接触生成範囲に入らないよう、双方の接触余白と小さな安全距離を合計する。
    private float GetCeilingClearance(Collider ceilingCollider)
    {
        return ceilingBodyContactOffset + Mathf.Max(0f, ceilingCollider.contactOffset) + CEILING_FOLLOW_SKIN;
    }

    // 移動経路を本体サイズで掃引し、途中で当たる天井の面から離れる高さへ補正する。
    // 移動先だけが安全でも角を横切る途中で接触するため、Xを維持したままYを下げて再検査する。
    private void ResolveCeilingPath(Bounds bounds, float pathClearance, ref Vector3 displacement)
    {
        // 実体がめり込む手前の接触生成範囲も掃引する。支持位置側には残り半分のskinを残す。
        Vector3 castHalf = bounds.extents;
        castHalf.y += Mathf.Max(0f, pathClearance - CEILING_FOLLOW_SKIN * 0.5f) /
            Mathf.Max(0.01f, ResolvedSurfaceNormalThreshold);
        for (int pass = 0; pass < CEILING_PATH_MAX_PASSES; pass++)
        {
            float distance = displacement.magnitude;
            if (distance <= POINT_TOLERANCE) return;
            float safeY = displacement.y;
            foreach (RaycastHit hit in Physics.BoxCastAll(bounds.center, castHalf,
                displacement / distance, Quaternion.identity, distance,
                contactDetectorParameter.TerrainLayers, QueryTriggerInteraction.Ignore))
            {
                // 開始時の重なりは支持面から求めた目標高さで解消する。
                if (hit.collider == null || IsOwner(hit.collider) || hit.distance <= 0f ||
                    hit.normal.y > -ResolvedSurfaceNormalThreshold || Mathf.Abs(hit.normal.z) > 0.5f)
                    continue;
                Vector3 normal = hit.normal.normalized;
                float radius = Mathf.Abs(normal.x) * bounds.extents.x +
                    Mathf.Abs(normal.y) * bounds.extents.y + Mathf.Abs(normal.z) * bounds.extents.z;
                float gap = Vector3.Dot(bounds.center - hit.point, normal) - radius;
                // 実際に経路へ当たった面に対し、本体全体が接触余白の外側に残るYを求める。
                float requiredY = (GetCeilingClearance(hit.collider) - gap -
                    normal.x * displacement.x - normal.z * displacement.z) / normal.y;
                safeY = Mathf.Min(safeY, requiredY);
            }
            if (safeY >= displacement.y - 0.00001f) return;
            displacement.y = safeY;
        }
    }

    // 天井に沿う移動速度を求め、経路上の角を避ける上下補正だけを加える。
    public bool TryGetCeilingFollowVelocity(SensorChainsawContact support, float moveSpeed,
        float stickSpeed, float deltaTime, out Vector3 velocity)
    {
        velocity = default;
        if (deltaTime <= 0f || support.Collider == null ||
            support.Surface != ChainsawSurface.Ceiling || !support.HasCeilingBodyTarget ||
            !TryGetCeilingBodyBounds(out Bounds bounds)) return false;
        Vector3 displacement = support.CeilingBodyTarget - bounds.center;
        // Xは直前の接線から求めた移動先をそのまま使い、押し付け速度のX成分を加算しない。
        float correctionLimit = Mathf.Max(Mathf.Abs(moveSpeed), Mathf.Max(0f, stickSpeed));
        displacement.y = Mathf.Min(displacement.y, correctionLimit * deltaTime);
        ResolveCeilingPath(bounds, GetCeilingClearance(support.Collider), ref displacement);
        velocity.x = displacement.x / deltaTime;
        // 角を避ける下向き補正まで制限すると、本体が角に残り続けるため再制限しない。
        velocity.y = displacement.y / deltaTime;
        // 一定の上向き押し付けを重ねない。角で接触反力が横移動を阻害することを防ぐ。
        return true;
    }

    // 床食い込みの移動先を本体サイズで下向きに掃引し、坂の継ぎ目でも支持位置を取得する。
    public bool TryGetFloorBodySupport(float moveSpeed, float deltaTime,
        Vector3 previousNormal, out SensorChainsawContact contact)
    {
        contact = default;
        if (!isActiveAndEnabled || !configured || deltaTime <= 0f ||
            !TryGetCeilingBodyBounds(out Bounds bounds)) return false;

        float travel = Mathf.Abs(moveSpeed) * deltaTime;
        float threshold = Mathf.Clamp(ResolvedSurfaceNormalThreshold, 0.01f, 1f);
        float maxSlope = Mathf.Sqrt(Mathf.Max(0f, 1f - threshold * threshold)) / threshold;
        float backoff = Mathf.Max(0.01f, contactDetectorParameter.NormalRayBackoff);
        float verticalRange = travel * maxSlope + backoff +
            (ceilingBodyContactOffset + CEILING_FOLLOW_SKIN) * 2f / threshold;
        float horizontalRatio = previousNormal.sqrMagnitude > 0.01f
            ? Mathf.Abs(previousNormal.normalized.y) : 1f;
        Vector3 origin = bounds.center + Vector3.right * (moveSpeed * deltaTime * horizontalRatio)
            + Vector3.up * verticalRange;
        float nearestDistance = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.BoxCastAll(origin, bounds.extents, Vector3.down,
            Quaternion.identity, verticalRange * 2f,
            contactDetectorParameter.TerrainLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == null || IsOwner(hit.collider) || hit.distance <= POINT_TOLERANCE ||
                Mathf.Abs(hit.normal.z) > 0.5f) continue;
            Vector3 normal = new Vector3(hit.normal.x, hit.normal.y, 0f).normalized;
            if (normal.y < threshold || hit.distance > nearestDistance) continue;
            if (hit.distance == nearestDistance && contact.Collider != null &&
                hit.collider.GetInstanceID() >= contact.Collider.GetInstanceID()) continue;
            // 本体を縮めずに照会し、接触余白の分だけ床から上へ離す。
            float targetY = origin.y - hit.distance + GetCeilingClearance(hit.collider) / threshold;
            if (Mathf.Abs(targetY - bounds.center.y) > verticalRange) continue;
            nearestDistance = hit.distance;
            contact = new SensorChainsawContact
            {
                Collider = hit.collider,
                Surface = ChainsawSurface.Floor,
                Point = hit.point,
                Normal = normal,
                HasFloorBodyTarget = true,
                FloorBodyTarget = new Vector3(origin.x, targetY, bounds.center.z)
            };
        }
        return contact.Collider != null;
    }

    // 床へ向かう経路上の角も掃引し、横移動を打ち消さずに必要な上方向の補正を行う。
    private void ResolveFloorPath(Bounds bounds, float pathClearance, ref Vector3 displacement)
    {
        float threshold = Mathf.Max(0.01f, ResolvedSurfaceNormalThreshold);
        Vector3 castHalf = bounds.extents;
        castHalf.y += Mathf.Max(0f, pathClearance - CEILING_FOLLOW_SKIN * 0.5f) / threshold;
        for (int pass = 0; pass < CEILING_PATH_MAX_PASSES; pass++)
        {
            float distance = displacement.magnitude;
            if (distance <= POINT_TOLERANCE) return;
            float safeY = displacement.y;
            foreach (RaycastHit hit in Physics.BoxCastAll(bounds.center, castHalf,
                displacement / distance, Quaternion.identity, distance,
                contactDetectorParameter.TerrainLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null || IsOwner(hit.collider) || hit.distance <= 0f ||
                    hit.normal.y < ResolvedSurfaceNormalThreshold || Mathf.Abs(hit.normal.z) > 0.5f)
                    continue;
                Vector3 normal = hit.normal.normalized;
                float radius = Mathf.Abs(normal.x) * bounds.extents.x +
                    Mathf.Abs(normal.y) * bounds.extents.y + Mathf.Abs(normal.z) * bounds.extents.z;
                float gap = Vector3.Dot(bounds.center - hit.point, normal) - radius;
                float requiredY = (GetCeilingClearance(hit.collider) - gap -
                    normal.x * displacement.x - normal.z * displacement.z) / normal.y;
                safeY = Mathf.Max(safeY, requiredY);
            }
            if (safeY <= displacement.y + 0.00001f) return;
            displacement.y = safeY;
        }
    }

    // 床の支持高さへ到達する速度を返し、頂点での浮き上がりと下り坂での離脱を防ぐ。
    public bool TryGetFloorFollowVelocity(SensorChainsawContact support,
        float deltaTime, out Vector3 velocity)
    {
        velocity = default;
        if (deltaTime <= 0f || support.Collider == null ||
            support.Surface != ChainsawSurface.Floor || !support.HasFloorBodyTarget ||
            !TryGetCeilingBodyBounds(out Bounds bounds)) return false;
        Vector3 displacement = support.FloorBodyTarget - bounds.center;
        ResolveFloorPath(bounds, GetCeilingClearance(support.Collider), ref displacement);
        velocity = displacement / deltaTime;
        // 法線方向の押し付け速度は重ねない。斜面でXが減速し、継ぎ目で衝突するのを防ぐ。
        return true;
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
                    ? contactDetectorParameter.EnemyLayers.value | contactDetectorParameter.TerrainLayers.value
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

            // 盾などの壁専用Triggerは、敵本体の受信コンポーネントより先に用途を確認する。
            if (sensor == middleSensor && TryConsiderWallZone(target, center,
                preferred, currentSurface, ref contact, ref bestScore))
                continue;

            bool isEnemyLayer =
                (contactDetectorParameter.EnemyLayers.value & (1 << target.gameObject.layer)) != 0;

            // 敵はMiddleだけで検出する。
            if (sensor == middleSensor)
            {
                if (!isEnemyLayer || currentSurface != ChainsawSurface.None ||
                    !ChainsawContactZone.AllowsEnemyDigging(target))
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

            // 初回接触でも、センサーだけが角に届いている候補を天井として採用しない。
            if (surface == ChainsawSurface.Ceiling)
            {
                if (!TryGetCeilingBodyBounds(out Bounds bodyBounds)) continue;
                float threshold = Mathf.Clamp(ResolvedSurfaceNormalThreshold, 0.01f, 1f);
                float maxSlope = Mathf.Sqrt(Mathf.Max(0f, 1f - threshold * threshold)) / threshold;
                float extraHeight = Mathf.Abs(hit.point.y - bodyBounds.max.y) + bodyBounds.extents.x * maxSlope;
                if (!TryGetCeilingCenterFace(target, bodyBounds, bodyBounds.center.x,
                    extraHeight, out RaycastHit centerFace)) continue;
                normal = new Vector3(centerFace.normal.x, centerFace.normal.y, 0f).normalized;
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

    // 中央センサーと重なる壁用Triggerを、指定した面の向きで壁候補へ追加する。
    private bool TryConsiderWallZone(Collider target, Vector3 sensorCenter,
        Collider preferred, ChainsawSurface currentSurface,
        ref SensorChainsawContact contact, ref float bestScore)
    {
        ChainsawContactZone zone = ChainsawContactZone.GetZone(target);
        if (zone == null || !zone.TryGetWallNormal(target, out Vector3 normal)) return false;
        if (normal.x * facing >= -0.1f) return true;
        Vector3 point = target.ClosestPoint(sensorCenter);
        Vector3 origin = new Vector3(ownerRoot.position.x, sensorCenter.y, sensorCenter.z);
        if (!IsBlocked(origin, point, target))
            Consider(target, null, ChainsawSurface.Wall, point, normal,
                preferred, currentSurface, ref contact, ref bestScore);
        return true;
    }

    // 壁用Triggerへの食い込み開始時だけ、設定された本体の進入位置へ合わせる。
    public void ApplyWallEntryPosition(Collider target)
    {
        ChainsawContactZone zone = ChainsawContactZone.GetZone(target);
        if (zone == null || ownerRoot == null || !zone.TryGetWallNormal(target, out Vector3 normal)) return;
        Vector3 currentPosition = ownerBody != null ? ownerBody.position : ownerRoot.position;
        if (!zone.TryGetEntryPosition(currentPosition, out Vector3 position))
        {
            // メッシュ原点ではなく、盾とプレイヤー本体の実際のCollider範囲から外側の位置を求める。
            // 移動先が未設定・盾自身・プレイヤー自身でも、盾の中心へ埋まらせない。
            if (!TryGetCeilingBodyBounds(out Bounds bodyBounds)) return;
            Bounds wallBounds = target.bounds;
            float wallFaceX = normal.x < 0f ? wallBounds.min.x : wallBounds.max.x;
            float bodyFaceX = bodyBounds.center.x - normal.x * bodyBounds.extents.x;
            float clearance = Mathf.Max(0.005f, ceilingBodyContactOffset);
            position = currentPosition;
            position.x += wallFaceX - bodyFaceX + normal.x * clearance;
        }
        if (ownerBody != null) ownerBody.position = position;
        else ownerRoot.position = position;
        Physics.SyncTransforms();
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
            contactDetectorParameter.TerrainLayers.value | contactDetectorParameter.EnemyLayers.value,
            QueryTriggerInteraction.Collide))
        {
            if (hit.collider == null || IsOwner(hit.collider))
            {
                continue;
            }

            ChainsawContactZone zone = ChainsawContactZone.GetZone(hit.collider);
            bool isWallZone = zone != null &&
                zone.TryGetWallNormal(hit.collider, out _);
            if (hit.collider.isTrigger ? !isWallZone :
                (contactDetectorParameter.TerrainLayers.value & (1 << hit.collider.gameObject.layer)) == 0)
                continue;

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
        ChainsawContactZone wallZone = ChainsawContactZone.GetZone(nearestHit.collider);
        if (wallZone != null && wallZone.TryGetWallNormal(nearestHit.collider, out Vector3 zoneNormal))
            normal = zoneNormal;


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

        // 同時に盾と胴体へ届いても、明示した壁用Triggerを優先する。
        ChainsawContactZone zone = ChainsawContactZone.GetZone(target);
        if (surface == ChainsawSurface.Wall && zone != null &&
            zone.TryGetWallNormal(target, out _))
            score -= 500f;

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
