using System.Collections.Generic;
using UnityEngine;

// Controllerから呼び出す接地照会専用クラス。自身では移動や物理更新を行わない。
public sealed class PlayerGroundProbe
{
    private const float SEPARATION_SPEED = 0.1f;
    private const float NORMAL_Z_LIMIT = 0.5f;
    private const float SCORE_EPSILON = 0.001f;
    private const float SWEEP_SKIN = 0.02f;
    private const float CONTACT_POINT_TOLERANCE = 0.002f;

    private readonly Rigidbody body;
    private readonly Transform feet;
    private readonly PlayerParameter settings;
    private readonly Dictionary<Collider, List<ContactPoint>> contacts =
        new Dictionary<Collider, List<ContactPoint>>();
    // 各地形の接触が最後に物理エンジンから届いた時刻を保存する。
    private readonly Dictionary<Collider, double> contactTimes = new Dictionary<Collider, double>();
    private readonly List<Collider> invalidContacts = new List<Collider>();

    private readonly List<Collider> bodyColliders = new List<Collider>();

    private SensorChainsawContact support;
    public bool IsGrounded { get; private set; }
    // 移動補助の先読みとは別に、本体が床付近にあると確認できた状態。
    public bool HasJumpSupport { get; private set; }
    public Vector3 Normal => IsGrounded ? support.Normal : Vector3.up;
    public string Source { get; private set; } = "未接地";
    public float NormalThreshold => Mathf.Clamp(settings.groundRayNormalThreshold, 0.1f, 1f);

    // 既存のRigidbody・足元基準・設定SOを参照する。SOへ状態を書き込まない。
    public PlayerGroundProbe(Rigidbody body, Transform feet, PlayerParameter settings)
    {
        this.body = body;
        this.feet = feet;
        this.settings = settings;
    }

    // 最新の物理接触点を地形ごとに保存する。面の選択はRefreshで行う。
    public void RecordContact(Collision collision)
    {
        Collider target = collision.collider;
        if (target == null) return;

        if (!contacts.TryGetValue(target, out List<ContactPoint> points))
        {
            points = new List<ContactPoint>();
            contacts.Add(target, points);
        }

        contactTimes[target] = Time.fixedTimeAsDouble;
        points.Clear();
        for (int index = 0; index < collision.contactCount; index++)
            points.Add(collision.GetContact(index));
    }

    // 接触が終了した地形の記録を解除する。
    public void RemoveContact(Collider target)
    {
        if (target == null) return;
        contacts.Remove(target);
        contactTimes.Remove(target);
    }

    // 無効化時などに接地状態と接触記録をすべて解除する。
    public void Clear()
    {
        contacts.Clear();
        contactTimes.Clear();
        invalidContacts.Clear();
        ResetSupport();
    }

    // 地面の照会結果だけを解除する。
    private void ResetSupport()
    {
        IsGrounded = false;
        HasJumpSupport = false;
        support = default;
        Source = "未接地";
    }

    // 実接触を優先し、補助Ray・下向きSweep・前方Sweepで接地面を選ぶ。
    public void Refresh(int terrainLayers, float requestedSpeed, float deltaTime, bool allowGrounding)
    {
        bool wasGrounded = IsGrounded;
        Vector3 previousNormal = Normal;
        Collider previousCollider = support.Collider;
        ResetSupport();

        if (!allowGrounding || terrainLayers == 0 || body == null || feet == null ||
            !feet.gameObject.activeInHierarchy)
        {
            return;
        }

        // 実際に身体として衝突するColliderを確認する。Triggerや別Rigidbodyは使用しない。
        body.GetComponentsInChildren<Collider>(true, bodyColliders);
        Vector3 velocity = body.linearVelocity;
        float direction = Mathf.Abs(requestedSpeed) > SCORE_EPSILON
            ? Mathf.Sign(requestedSpeed) : 0f;
        bool continuing = wasGrounded &&
            Vector3.Dot(velocity, previousNormal) <= SEPARATION_SPEED;

        float bestRise = float.NegativeInfinity;
        float bestDistance = float.PositiveInfinity;
        invalidContacts.Clear();

        // 平地と坂が同時に接触した場合、移動を妨げる上り面を優先する。
        foreach (KeyValuePair<Collider, List<ContactPoint>> pair in contacts)
        {
            if (!IsTerrain(pair.Key, terrainLayers))
            {
                invalidContacts.Add(pair.Key);
                continue;
            }

            foreach (ContactPoint point in pair.Value)
            {
                // 上半身の接触を足元の支持面として扱わない。
                if (point.point.y > body.worldCenterOfMass.y ||
                    !TryGetFloorNormal(point.normal, out Vector3 normal)) continue;
                if (!continuing && Vector3.Dot(velocity, normal) > SEPARATION_SPEED) continue;
                // 歩行用の接触採用は従来どおりにする。
                // 本体との距離による追加制限は、後段のHasJumpSupportだけに適用する。

                // 歩行用に選ばれなかった面も、現在の本体を支えていればジャンプを許可する。
                if (Vector3.Dot(velocity, normal) <= SEPARATION_SPEED &&
                    IsCurrentPhysicalSupport(pair.Key, point))
                    HasJumpSupport = true;

                ConsiderSupport(pair.Key, point.point, normal, direction, previousCollider,
                    "物理接触", ref bestRise, ref bestDistance);
            }
        }

        foreach (Collider target in invalidContacts)
        {
            contacts.Remove(target);
            contactTimes.Remove(target);
        }

        // 実接触がない初期フレームや物理停止中は、既存の3本Rayを補助にする。
        if (!IsGrounded)
            CollectRaySupport(terrainLayers, velocity, continuing, direction, previousCollider,
                ref bestRise, ref bestDistance);

        // 現在の接触・足元Rayを、本体Colliderの実際の形状でも確認する。
        // この後の下り追従・前方の斜面への先読みでは、ジャンプ許可を新規に作らない。
        HasJumpSupport = HasJumpSupport ||
            (IsGrounded && HasCurrentFloorSupport(support.Collider, support.Normal, velocity)) ||
            HasNearbyFloorSupport(terrainLayers, velocity);

        // 下りの追従は直前まで接地していた場合だけ。空中から地面を吸い上げない。
        if (!IsGrounded && continuing && settings.slopeGroundSnapDistance > 0f &&
            TryGetNearestSweep(Vector3.down, settings.slopeGroundSnapDistance,
                terrainLayers, out RaycastHit below) &&
            TryGetFloorNormal(below.normal, out Vector3 belowNormal))
        {
            SetSupport(below.collider, below.point, belowNormal, "下り追従");
        }

        // 本体形状を1物理ステップ程度だけ前へ照会する。実際の位置は変更しない。
        if (IsGrounded && direction != 0f && settings.slopeGroundProbeDistance > 0f)
        {
            float distance = Mathf.Min(settings.slopeGroundProbeDistance,
                Mathf.Abs(requestedSpeed) * deltaTime + SWEEP_SKIN);

            if (TryGetNearestSweep(Vector3.right * direction, distance, terrainLayers,
                    out RaycastHit ahead) &&
                TryGetFloorNormal(ahead.normal, out Vector3 aheadNormal))
            {
                float currentRise = -Normal.x * direction / Normal.y;
                float aheadRise = -aheadNormal.x * direction / aheadNormal.y;

                // より上りになる面だけを先読みする。下りや平地へ先に切り替えない。
                if (aheadRise > Mathf.Max(0f, currentRise) + SCORE_EPSILON)
                    SetSupport(ahead.collider, ahead.point, aheadNormal, "前方の斜面");
            }
        }
    }

    // 補助Rayの開始高さと探索距離を求める。Gizmosにも同じ値を渡す。
    public void GetRayGeometry(out float sideOffset, out float startHeight, out float distance)
    {
        sideOffset = Mathf.Max(0f, settings.groundRaySideOffset);
        float threshold = NormalThreshold;
        float rise = sideOffset * Mathf.Sqrt(Mathf.Max(0f, 1f - threshold * threshold)) / threshold;
        startHeight = Mathf.Max(0.01f, settings.groundRayStartHeight) + rise;
        distance = Mathf.Max(settings.groundRayDistance,
            startHeight + Mathf.Max(0f, settings.groundRayContactTolerance));
    }

    // 足元とほぼ同じ高さにある面を3本のRayで探す。高い段差は接地にしない。
    private void CollectRaySupport(int layers, Vector3 velocity, bool continuing, float direction,
        Collider previousCollider, ref float bestRise, ref float bestDistance)
    {
        GetRayGeometry(out float sideOffset, out float startHeight, out float distance);
        float tolerance = Mathf.Max(0f, settings.groundRayContactTolerance);
        Vector3 center = feet.position + Vector3.up * startHeight;

        for (int index = -1; index <= 1; index++)
        {
            Vector3 origin = center + Vector3.right * (index * sideOffset);
            RaycastHit nearest = default;
            float nearestDistance = float.PositiveInfinity;

            // 上の面を無視して、その下の床を接地として選ばない。
            foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, distance,
                layers, QueryTriggerInteraction.Ignore))
            {
                if (!IsTerrain(hit.collider, layers) || hit.distance >= nearestDistance) continue;
                nearest = hit;
                nearestDistance = hit.distance;
            }

            if (nearest.collider == null ||
                !TryGetFloorNormal(nearest.normal, out Vector3 normal)) continue;
            float gap = nearest.distance - startHeight;
            if (Mathf.Abs(gap) > tolerance) continue;
            if (!continuing && Vector3.Dot(velocity, normal) > SEPARATION_SPEED) continue;

            ConsiderSupport(nearest.collider, nearest.point, normal, direction, previousCollider,
                "足元Ray", ref bestRise, ref bestDistance);
        }
    }

    // 地面の候補点付近に、現在の本体Colliderがあるかを形状で確認する。
    // Ground Check Transformの高さでは判定せず、物理接触の余白だけを許可する。
    private bool IsBodyNearSupport(Collider target, Vector3 point)
    {
        if (target == null || point.y > body.worldCenterOfMass.y) return false;

        foreach (Collider bodyCollider in bodyColliders)
        {
            if (bodyCollider == null || !bodyCollider.enabled ||
                !bodyCollider.gameObject.activeInHierarchy || bodyCollider.isTrigger ||
                bodyCollider.attachedRigidbody != body)
            {
                continue;
            }

            // ClosestPointで形状を検証できる、身体用のColliderだけを使う。
            bool supportedShape = bodyCollider is BoxCollider ||
                bodyCollider is SphereCollider || bodyCollider is CapsuleCollider ||
                (bodyCollider is MeshCollider mesh && mesh.convex);
            if (!supportedShape) continue;

            float tolerance = Mathf.Max(CONTACT_POINT_TOLERANCE,
                bodyCollider.contactOffset + target.contactOffset);
            Vector3 closestPoint = bodyCollider.ClosestPoint(point);
            if ((closestPoint - point).sqrMagnitude <= tolerance * tolerance)
                return true;
        }

        return false;
    }

    // 新しい物理接触は追加Rayや接触点との距離で否定せず、その接触自体を支持とする。
    // 移動中の古い履歴は採用しない。睡眠中は接触通知が省略されるため静止接触を維持する。
    private bool IsCurrentPhysicalSupport(Collider target, ContactPoint point)
    {
        Collider playerCollider = point.thisCollider;
        if (playerCollider == null || !playerCollider.enabled || playerCollider.isTrigger ||
            !playerCollider.gameObject.activeInHierarchy || playerCollider.attachedRigidbody != body)
            return false;

        if (!contactTimes.TryGetValue(target, out double lastContactTime))
            return false;

        double age = Time.fixedTimeAsDouble - lastContactTime;
        return body.IsSleeping() ||
            (age >= 0d && age <= Time.fixedDeltaTime * 1.5d);
    }

    // 保存済み接触点ではなく、現在の身体の中心から地形面を取り直す。
    // 斜面に沿って移動しても過去の接触点との横ずれで初段を失わないようにする。
    private bool HasCurrentFloorSupport(Collider target, Vector3 normal, Vector3 velocity)
    {
        if (target == null || Vector3.Dot(velocity, normal) > SEPARATION_SPEED)
            return false;

        foreach (Collider bodyCollider in bodyColliders)
        {
            if (bodyCollider == null || !bodyCollider.enabled ||
                !bodyCollider.gameObject.activeInHierarchy || bodyCollider.isTrigger ||
                bodyCollider.attachedRigidbody != body)
                continue;

            bool supportedShape = bodyCollider is BoxCollider ||
                bodyCollider is SphereCollider || bodyCollider is CapsuleCollider ||
                (bodyCollider is MeshCollider mesh && mesh.convex);
            if (!supportedShape) continue;

            float tolerance = Mathf.Max(CONTACT_POINT_TOLERANCE,
                bodyCollider.contactOffset + target.contactOffset);
            Bounds bounds = bodyCollider.bounds;
            Ray ray = new Ray(bounds.center, -normal);

            // 地形自身へ照会するため、先読み面が遠くても現在の支持としては採用しない。
            if (!target.Raycast(ray, out RaycastHit hit, bounds.extents.magnitude + tolerance) ||
                !TryGetFloorNormal(hit.normal, out Vector3 hitNormal) ||
                hit.point.y > body.worldCenterOfMass.y ||
                Vector3.Dot(velocity, hitNormal) > SEPARATION_SPEED)
                continue;

            Vector3 closestPoint = bodyCollider.ClosestPoint(hit.point);
            if ((closestPoint - hit.point).sqrMagnitude <= tolerance * tolerance)
                return true;
        }

        return false;
    }

    // 古い接触点や足元Rayの位置に依存せず、現在の本体直下に支持面があるか確認する。
    // 長い斜面追従距離は使わず、Colliderの接触余白までに限定する。
    private bool HasNearbyFloorSupport(int layers, Vector3 velocity)
    {
        float bodyOffset = 0f;
        foreach (Collider bodyCollider in bodyColliders)
        {
            if (bodyCollider == null || !bodyCollider.enabled ||
                !bodyCollider.gameObject.activeInHierarchy || bodyCollider.isTrigger ||
                bodyCollider.attachedRigidbody != body)
                continue;

            bodyOffset = Mathf.Max(bodyOffset, bodyCollider.contactOffset);
        }

        // 探索後も相手側の実際のcontactOffsetで距離を検証する。
        float searchDistance = Mathf.Max(CONTACT_POINT_TOLERANCE,
            bodyOffset + Physics.defaultContactOffset);
        foreach (RaycastHit hit in body.SweepTestAll(
            Vector3.down, searchDistance, QueryTriggerInteraction.Ignore))
        {
            if (!IsTerrain(hit.collider, layers) || hit.distance < 0f ||
                !TryGetFloorNormal(hit.normal, out Vector3 normal) ||
                hit.point.y > body.worldCenterOfMass.y ||
                Vector3.Dot(velocity, normal) > SEPARATION_SPEED)
                continue;

            // ジャンプ中に床が近いだけで回数を回復させない。
            // 斜面に沿う上下移動は、鉛直速度ではなく面から離れる速度で区別する。
            if (IsBodyNearSupport(hit.collider, hit.point))
            {
                // Controller側の接地条件にも、現在位置で確認した支持面を渡す。
                if (!IsGrounded)
                    SetSupport(hit.collider, hit.point, normal, "本体直下の支持");
                return true;
            }
        }

        return false;
    }

    // プレイヤーの物理形状で照会し、最も手前の地形だけを返す。
    private bool TryGetNearestSweep(Vector3 direction, float distance, int layers, out RaycastHit nearest)
    {
        nearest = default;
        float nearestDistance = float.PositiveInfinity;
        foreach (RaycastHit hit in body.SweepTestAll(direction, distance, QueryTriggerInteraction.Ignore))
        {
            if (!IsTerrain(hit.collider, layers) || hit.distance < 0f || hit.distance >= nearestDistance)
                continue;

            nearest = hit;
            nearestDistance = hit.distance;
        }
        return nearest.collider != null;
    }

    // 移動方向に対して上りになる支持面を優先し、同等なら近い接触点を選ぶ。
    private void ConsiderSupport(Collider target, Vector3 point, Vector3 normal, float direction,
        Collider previousCollider, string source, ref float bestRise, ref float bestDistance)
    {
        float rise = direction == 0f ? 0f : -normal.x * direction / normal.y;
        float distance = (point - feet.position).sqrMagnitude;
        bool sameRise = Mathf.Abs(rise - bestRise) <= SCORE_EPSILON;
        bool keepPrevious = direction == 0f && support.Collider == previousCollider &&
            target != previousCollider;

        if (IsGrounded && (rise < bestRise - SCORE_EPSILON ||
            (sameRise && (keepPrevious || distance >= bestDistance)))) return;

        bestRise = rise;
        bestDistance = distance;
        SetSupport(target, point, normal, source);
    }

    // 選択した支持面を保存する。
    private void SetSupport(Collider target, Vector3 point, Vector3 normal, string source)
    {
        support = new SensorChainsawContact
        {
            Collider = target,
            Enemy = null,
            Surface = ChainsawSurface.Floor,
            Point = point,
            Normal = normal
        };
        IsGrounded = true;
        Source = source;
    }

    // XY平面で歩ける上向きの面かを判定する。
    private bool TryGetFloorNormal(Vector3 rawNormal, out Vector3 normal)
    {
        normal = new Vector3(rawNormal.x, rawNormal.y, 0f).normalized;
        return Mathf.Abs(rawNormal.z) <= NORMAL_Z_LIMIT && normal.y >= NormalThreshold;
    }

    // 自分自身・Trigger・対象外レイヤー・無効な地形を除外する。
    private bool IsTerrain(Collider target, int layers)
    {
        return target != null && target.enabled && target.gameObject.activeInHierarchy &&
            !target.isTrigger && target.attachedRigidbody != body &&
            !target.transform.IsChildOf(body.transform) &&
            (layers & (1 << target.gameObject.layer)) != 0;
    }

    // 現在採用している接地面を、食い込み判定などの読み取り用に返す。
    public bool TryGetSupport(out SensorChainsawContact contact)
    {
        contact = support;
        return IsGrounded && support.Collider != null && support.Collider.enabled &&
            support.Collider.gameObject.activeInHierarchy;
    }
}
