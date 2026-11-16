using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 本体Trigger Colliderを使ってXY平面上のステージ・プレイヤーへの侵入を抑止する。
/// </summary>
/// <remarks>
/// AIによる速度指示やState遷移は行わない。Rigidbodyが持つ要求速度を物理更新ごとに制限する。
/// 通常Colliderの接触ソルバーに頼れないため、開始時の重なり補正・移動経路Sweep・接地確認を行う。
/// 死亡後の分割片Colliderには適用しない。
/// </remarks>
public sealed class EnemyTriggerMovementMotor
{
    private const float MOVEMENT_SKIN = 0.003f;
    private const float ATTACK_START_CONTACT_MARGIN = 0.005f;
    private const float GROUND_PROBE_DISTANCE = 0.025f;
    private const float MIN_MOVEMENT_DISTANCE = 0.00001f;
    private const float MIN_PLANAR_NORMAL = 0.0001f;
    private const int MAX_RECOVERY_ITERATIONS = 6;
    private const int QUERY_BUFFER_CAPACITY = 32;

    private readonly Rigidbody enemyRigidbody;
    private readonly Collider bodyCollider;
    private readonly int stageLayer;
    private readonly int playerLayer;
    private readonly int blockingLayerMask;
    private readonly RaycastHit[] castBuffer = new RaycastHit[QUERY_BUFFER_CAPACITY];
    private readonly Collider[] overlapBuffer = new Collider[QUERY_BUFFER_CAPACITY];
    private readonly Dictionary<Collider, EnemyContactInfo> previousContacts =
        new Dictionary<Collider, EnemyContactInfo>();
    private readonly Dictionary<Collider, EnemyContactInfo> currentContacts =
        new Dictionary<Collider, EnemyContactInfo>();

    public EnemyTriggerMovementMotor(Rigidbody enemyRigidbody)
    {
        this.enemyRigidbody = enemyRigidbody != null
            ? enemyRigidbody
            : throw new ArgumentNullException(nameof(enemyRigidbody));

        stageLayer = LayerMask.NameToLayer("StageCollider");
        playerLayer = LayerMask.NameToLayer("PlayerBody");
        if (stageLayer < 0 || playerLayer < 0)
        {
            throw new InvalidOperationException(
                "EnemyTriggerMovementMotorにはStageColliderとPlayerBodyのLayerが必要です。");
        }

        blockingLayerMask = (1 << stageLayer) | (1 << playerLayer);

        Collider[] colliders = enemyRigidbody.GetComponentsInChildren<Collider>(true);
        foreach (Collider candidate in colliders)
        {
            if (!candidate.enabled || candidate.attachedRigidbody != enemyRigidbody)
            {
                continue;
            }

            if (!(candidate is BoxCollider) && !(candidate is CapsuleCollider))
            {
                continue;
            }

            // RigidbodyのルートColliderを優先する。盾などの死亡後Pieceは対象にしない。
            if (candidate.transform == enemyRigidbody.transform)
            {
                bodyCollider = candidate;
                break;
            }
        }

        if (bodyCollider == null)
        {
            throw new InvalidOperationException(
                "Enemy本体と同じGameObjectに有効なBoxColliderまたはCapsuleColliderが必要です。");
        }

        bodyCollider.isTrigger = true;
    }

    public bool IsContactTracked(Collider otherCollider)
    {
        return otherCollider != null && previousContacts.ContainsKey(otherCollider);
    }

    public EnemyContactInfo CreateTriggerContact(Collider otherCollider)
    {
        if (otherCollider == null)
        {
            return default;
        }

        Vector3 bodyPosition = enemyRigidbody.position;
        Vector3 point = otherCollider.ClosestPoint(bodyCollider.bounds.center);
        Vector3 normal = bodyCollider.bounds.center - point;

        if (Physics.ComputePenetration(
                bodyCollider, GetColliderWorldPosition(bodyPosition), bodyCollider.transform.rotation,
                otherCollider, otherCollider.transform.position, otherCollider.transform.rotation,
                out Vector3 separationDirection, out _))
        {
            normal = separationDirection;
        }

        normal.z = 0f;
        if (normal.sqrMagnitude > MIN_PLANAR_NORMAL * MIN_PLANAR_NORMAL)
        {
            normal.Normalize();
        }

        return new EnemyContactInfo(otherCollider, point, normal, enemyRigidbody.linearVelocity);
    }

    /// <summary>
    /// State更新で確定した速度を、次の物理シミュレーション前に安全な速度へ制限する。
    /// 接触通知は移動制限の副産物としてだけ生成し、通知先のStateをMotorは知らない。
    /// </summary>
    /// <param name="stageMovementHandledExternally">
    /// trueの場合はStageのSweep・重なり補正・接地補助を行わない。
    /// DroneのSearch/Attackは専用PlanarMotorがStage移動を解決済みだが、
    /// Playerとの侵入防止はこのMotorが引き続き担当する。
    /// </param>
    public void Step(
        Action<EnemyContactInfo, bool> notifyContact,
        Action<EnemyContactInfo> notifyExit,
        bool stageMovementHandledExternally = false)
    {
        // 外部MotorがStageを処理したフレームにはPlayerのみをQueryする。
        // 通常のDamage/Alert中はStageも含むため、接地・ノックバックを維持する。
        int movementMask = stageMovementHandledExternally
            ? 1 << playerLayer
            : blockingLayerMask;

        currentContacts.Clear();
        // プレイヤーとの重なりでは敵本体を押し戻さない。
        // 敵が静止中でもプレイヤーの接近だけで移動してしまうため、位置補正はStageに限定する。
        if (!stageMovementHandledExternally)
        {
            RecoverStagePenetrations();
        }

        // 位置補正しなくても接触ダメージ等の通知は必要なので、Player接触を別途記録する。
        RecordOverlappingPlayerContacts();

        Vector3 velocity = enemyRigidbody.linearVelocity;
        // RigidbodyのGravityはこのFixedUpdateの後に反映されるため、移動予定量に含める。
        Vector3 predictedVelocity = velocity +
            (enemyRigidbody.useGravity ? Physics.gravity * Time.fixedDeltaTime : Vector3.zero);
        predictedVelocity.z = 0f;
        float distance = predictedVelocity.magnitude * Time.fixedDeltaTime;
        float allowedFraction = 1f;

        if (distance > MIN_MOVEMENT_DISTANCE)
        {
            Vector3 direction = predictedVelocity.normalized;
            RaycastHit[] hits = CastBody(direction, distance + MOVEMENT_SKIN,
                movementMask, out int hitCount);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsBlocking(hit.collider))
                {
                    continue;
                }

                // Box/CapsuleCastは開始時の重なりに対して仮の逆方向法線を返す。
                // 床を横移動するだけの接触を壁と誤判定しないよう侵入を再検査する。
                if (hit.distance <= MIN_MOVEMENT_DISTANCE &&
                    !WouldPenetrateAfterMove(hit.collider, direction * 0.002f))
                {
                    continue;
                }

                Vector3 normal = hit.normal;
                normal.z = 0f;
                if (normal.sqrMagnitude < MIN_PLANAR_NORMAL * MIN_PLANAR_NORMAL)
                {
                    continue;
                }

                normal.Normalize();
                // 開始時の隣接床など、進行を塞いでいない接触では速度を落とさない。
                if (Vector3.Dot(direction, normal) >= -0.01f)
                {
                    continue;
                }

                RecordContact(new EnemyContactInfo(
                    hit.collider, hit.point, normal, predictedVelocity));

                float fraction = Mathf.Clamp01(
                    (hit.distance - MOVEMENT_SKIN) / distance);
                allowedFraction = Mathf.Min(allowedFraction, fraction);
            }
        }

        if (allowedFraction < 1f)
        {
            // 法線方向の物理反発は存在しないので、接触地点を越えない速度へ縮める。
            velocity.x *= allowedFraction;
            velocity.y *= allowedFraction;
        }

        if (!stageMovementHandledExternally &&
            ProbeGround(out EnemyContactInfo groundContact))
        {
            RecordContact(groundContact);
            // 平坦な地面上ではGravityが次ステップで再度床へ侵入させないよう相殺する。
            // 坂の下り方向速度は残し、坂道移動を止めない。
            if (enemyRigidbody.useGravity && groundContact.Normal.y > 0.95f &&
                velocity.y <= 0f)
            {
                velocity.y = -Physics.gravity.y * Time.fixedDeltaTime;
            }
        }

        if (!enemyRigidbody.isKinematic)
        {
            enemyRigidbody.linearVelocity = velocity;
        }

        foreach (KeyValuePair<Collider, EnemyContactInfo> contact in currentContacts)
        {
            notifyContact?.Invoke(contact.Value, !previousContacts.ContainsKey(contact.Key));
        }

        foreach (KeyValuePair<Collider, EnemyContactInfo> contact in previousContacts)
        {
            if (!currentContacts.ContainsKey(contact.Key))
            {
                notifyExit?.Invoke(contact.Value);
            }
        }

        previousContacts.Clear();
        foreach (KeyValuePair<Collider, EnemyContactInfo> contact in currentContacts)
        {
            previousContacts.Add(contact.Key, contact.Value);
        }
    }

    /// <summary>
    /// 攻撃開始時点で本体と接触しているPlayer Colliderを再照会する。
    /// </summary>
    /// <remarks>
    /// 予備動作中にMotorがPlayerとの重なりを解消すると、攻撃開始時には
    /// 新しいOnTriggerEnterが届かない場合がある。
    /// 実際の本体形状にごく小さな許容幅を加えて照会し、移動停止用のQueryとは独立して
    /// 攻撃開始時の接触だけを再通知する。距離が離れたPlayerへ攻撃を当てないため、
    /// 索敵距離や攻撃範囲による代用はしない。
    /// </remarks>
    public void NotifyPlayerContactsAtAttackStart(Action<EnemyContactInfo> notifyContact)
    {
        if (notifyContact == null || !bodyCollider.enabled)
        {
            return;
        }

        GetGeometry(enemyRigidbody.position,
            out Vector3 center, out Vector3 halfExtents, out Quaternion rotation,
            out Vector3 capsuleA, out Vector3 capsuleB, out float capsuleRadius);

        int playerMask = 1 << playerLayer;
        int count = bodyCollider is BoxCollider
            ? Physics.OverlapBoxNonAlloc(center,
                halfExtents + Vector3.one * ATTACK_START_CONTACT_MARGIN,
                overlapBuffer, rotation, playerMask, QueryTriggerInteraction.Collide)
            : Physics.OverlapCapsuleNonAlloc(capsuleA, capsuleB,
                capsuleRadius + ATTACK_START_CONTACT_MARGIN, overlapBuffer,
                playerMask, QueryTriggerInteraction.Collide);

        Collider[] colliders = overlapBuffer;
        if (count == overlapBuffer.Length)
        {
            // 他のQuery同様、バッファ満杯時のPlayer接触見落としを防ぐ。
            colliders = bodyCollider is BoxCollider
                ? Physics.OverlapBox(center,
                    halfExtents + Vector3.one * ATTACK_START_CONTACT_MARGIN,
                    rotation, playerMask, QueryTriggerInteraction.Collide)
                : Physics.OverlapCapsule(capsuleA, capsuleB,
                    capsuleRadius + ATTACK_START_CONTACT_MARGIN,
                    playerMask, QueryTriggerInteraction.Collide);
            count = colliders.Length;
        }

        for (int i = 0; i < count; i++)
        {
            Collider playerCollider = colliders[i];
            if (playerCollider == null || !playerCollider.enabled ||
                playerCollider.gameObject.layer != playerLayer ||
                playerCollider.attachedRigidbody == enemyRigidbody)
            {
                continue;
            }

            notifyContact(CreateTriggerContact(playerCollider));
        }
    }

    public void ClearContacts()
    {
        previousContacts.Clear();
        currentContacts.Clear();
    }

    private void RecordOverlappingPlayerContacts()
    {
        Collider[] colliders = OverlapBody(enemyRigidbody.position,
            1 << playerLayer, out int overlapCount);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider playerCollider = colliders[i];
            if (playerCollider == null || playerCollider.gameObject.layer != playerLayer ||
                !IsBlocking(playerCollider))
            {
                continue;
            }

            RecordContact(CreateTriggerContact(playerCollider));
        }
    }

    private void RecoverStagePenetrations()
    {
        Vector3 bodyPosition = enemyRigidbody.position;
        bool changed = false;
        for (int iteration = 0; iteration < MAX_RECOVERY_ITERATIONS; iteration++)
        {
            bool found = false;
            Collider[] colliders = OverlapBody(bodyPosition, 1 << stageLayer,
                out int overlapCount);
            for (int i = 0; i < overlapCount; i++)
            {
                Collider obstacle = colliders[i];
                if (!IsBlocking(obstacle) || obstacle.gameObject.layer != stageLayer ||
                    !Physics.ComputePenetration(
                        bodyCollider, GetColliderWorldPosition(bodyPosition), bodyCollider.transform.rotation,
                        obstacle, obstacle.transform.position, obstacle.transform.rotation,
                        out Vector3 separation, out float distance))
                {
                    continue;
                }

                separation.z = 0f;
                float magnitude = separation.magnitude;
                if (magnitude < MIN_PLANAR_NORMAL)
                {
                    continue;
                }

                separation /= magnitude;
                bodyPosition += separation * ((distance / magnitude) + MOVEMENT_SKIN);
                RecordContact(new EnemyContactInfo(
                    obstacle, obstacle.ClosestPoint(bodyCollider.bounds.center),
                    separation, enemyRigidbody.linearVelocity));
                found = true;
                changed = true;
            }

            if (!found)
            {
                break;
            }
        }

        if (changed)
        {
            bodyPosition.z = enemyRigidbody.position.z;
            enemyRigidbody.position = bodyPosition;
        }
    }

    private bool WouldPenetrateAfterMove(Collider obstacle, Vector3 movement)
    {
        return Physics.ComputePenetration(
            bodyCollider, GetColliderWorldPosition(enemyRigidbody.position + movement),
            bodyCollider.transform.rotation,
            obstacle, obstacle.transform.position, obstacle.transform.rotation,
            out _, out _);
    }

    private bool ProbeGround(out EnemyContactInfo contact)
    {
        contact = default;
        RaycastHit[] hits = CastBody(Vector3.down, GROUND_PROBE_DISTANCE,
            1 << stageLayer, out int hitCount);
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || !IsBlocking(hit.collider) ||
                hit.collider.gameObject.layer != stageLayer || hit.normal.y <= 0.35f)
            {
                continue;
            }

            contact = new EnemyContactInfo(
                hit.collider, hit.point, hit.normal, enemyRigidbody.linearVelocity);
            return true;
        }

        return false;
    }

    private void RecordContact(EnemyContactInfo contact)
    {
        if (!contact.IsValid)
        {
            return;
        }

        if (!currentContacts.TryGetValue(contact.OtherCollider, out EnemyContactInfo previous))
        {
            currentContacts[contact.OtherCollider] = contact;
            return;
        }

        if (previous.Normal.sqrMagnitude > 0f && contact.Normal.sqrMagnitude > 0f &&
            Vector3.Dot(previous.Normal.normalized, contact.Normal.normalized) < 0.9f)
        {
            currentContacts[contact.OtherCollider] =
                new EnemyContactInfo(previous.OtherCollider, previous.Point, previous.Normal,
                    previous.RelativeVelocity, contact.Normal);
        }
    }

    private bool IsBlocking(Collider candidate)
    {
        if (candidate == null || !candidate.enabled ||
            candidate.attachedRigidbody == enemyRigidbody ||
            candidate.transform.IsChildOf(enemyRigidbody.transform))
        {
            return false;
        }

        int layer = candidate.gameObject.layer;
        return (layer == stageLayer && !candidate.isTrigger) || layer == playerLayer;
    }

    private Vector3 GetColliderWorldPosition(Vector3 bodyPosition)
    {
        return bodyPosition;
    }

    private Collider[] OverlapBody(Vector3 bodyPosition, int movementMask, out int count)
    {
        GetGeometry(bodyPosition, out Vector3 center, out Vector3 halfExtents,
            out Quaternion rotation, out Vector3 capsuleA, out Vector3 capsuleB,
            out float capsuleRadius);

        count = bodyCollider is BoxCollider
            ? Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, rotation,
                movementMask, QueryTriggerInteraction.Collide)
            : Physics.OverlapCapsuleNonAlloc(capsuleA, capsuleB, capsuleRadius,
                overlapBuffer, movementMask, QueryTriggerInteraction.Collide);

        if (count < overlapBuffer.Length)
        {
            return overlapBuffer;
        }

        // 検出上限によって最寄り障害物を見逃さないよう、満杯のときだけ全件取得する。
        Collider[] all = bodyCollider is BoxCollider
            ? Physics.OverlapBox(center, halfExtents, rotation, movementMask,
                QueryTriggerInteraction.Collide)
            : Physics.OverlapCapsule(capsuleA, capsuleB, capsuleRadius,
                movementMask, QueryTriggerInteraction.Collide);
        count = all.Length;
        return all;
    }

    private RaycastHit[] CastBody(Vector3 direction, float distance,
        int movementMask, out int count)
    {
        GetGeometry(enemyRigidbody.position, out Vector3 center, out Vector3 halfExtents,
            out Quaternion rotation, out Vector3 capsuleA, out Vector3 capsuleB,
            out float capsuleRadius);

        count = bodyCollider is BoxCollider
            ? Physics.BoxCastNonAlloc(center, halfExtents, direction, castBuffer, rotation,
                distance, movementMask, QueryTriggerInteraction.Collide)
            : Physics.CapsuleCastNonAlloc(capsuleA, capsuleB, capsuleRadius, direction,
                castBuffer, distance, movementMask, QueryTriggerInteraction.Collide);

        if (count < castBuffer.Length)
        {
            return castBuffer;
        }

        RaycastHit[] all = bodyCollider is BoxCollider
            ? Physics.BoxCastAll(center, halfExtents, direction, rotation, distance,
                movementMask, QueryTriggerInteraction.Collide)
            : Physics.CapsuleCastAll(capsuleA, capsuleB, capsuleRadius, direction,
                distance, movementMask, QueryTriggerInteraction.Collide);
        count = all.Length;
        return all;
    }

    private void GetGeometry(
        Vector3 bodyPosition,
        out Vector3 center,
        out Vector3 halfExtents,
        out Quaternion rotation,
        out Vector3 capsuleA,
        out Vector3 capsuleB,
        out float capsuleRadius)
    {
        Transform colliderTransform = bodyCollider.transform;
        rotation = colliderTransform.rotation;
        Vector3 scale = colliderTransform.lossyScale;
        Vector3 absScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        // Body ColliderはRigidbodyと同じルートに置く契約。補正直後でも
        // Transformの遅延同期に依存しないようRigidbody位置からQuery形状を計算する。
        center = bodyPosition;
        halfExtents = Vector3.zero;
        capsuleRadius = 0f;
        capsuleA = center;
        capsuleB = center;

        if (bodyCollider is BoxCollider box)
        {
            center = bodyPosition + rotation * Vector3.Scale(box.center, scale);
            halfExtents = Vector3.Scale(box.size * 0.5f, absScale);
            return;
        }

        CapsuleCollider capsule = (CapsuleCollider)bodyCollider;
        center = bodyPosition + rotation * Vector3.Scale(capsule.center, scale);
        Vector3 localAxis = capsule.direction == 0 ? Vector3.right :
            capsule.direction == 1 ? Vector3.up : Vector3.forward;
        float heightScale = capsule.direction == 0 ? absScale.x :
            capsule.direction == 1 ? absScale.y : absScale.z;
        float radiusScale = capsule.direction == 0 ? Mathf.Max(absScale.y, absScale.z) :
            capsule.direction == 1 ? Mathf.Max(absScale.x, absScale.z) :
            Mathf.Max(absScale.x, absScale.y);
        capsuleRadius = capsule.radius * radiusScale;
        float halfAxis = Mathf.Max(0f, capsule.height * heightScale * 0.5f - capsuleRadius);
        Vector3 axis = colliderTransform.TransformDirection(localAxis);
        capsuleA = center + axis * halfAxis;
        capsuleB = center - axis * halfAxis;
    }
}
