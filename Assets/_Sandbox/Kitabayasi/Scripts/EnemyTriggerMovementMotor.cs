using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 本体Trigger Colliderを使ってXY平面上のステージとプレイヤーへの侵入を制限する。
/// </summary>
/// <remarks>
/// AIによる速度指示やState遷移は行わない。Rigidbodyが持つ要求速度を物理更新ごとに制限する。
/// 通常Colliderの接触ソルバーに頼れないため、開始時の重なり補正・移動経路Sweep・接地確認を行う。
/// 特定の突進中はPlayerへの侵入制限だけを解除し、接触通知は維持する。
/// 死亡後の分割片Colliderには適用しない。
/// </remarks>
public sealed class EnemyTriggerMovementMotor
{
    private const float MOVEMENT_SKIN = 0.003f;
    private const float GROUND_PROBE_DISTANCE = 0.025f;
    private const float MIN_MOVEMENT_DISTANCE = 0.00001f;
    private const float MIN_PLANAR_NORMAL = 0.0001f;
    private const int MAX_RECOVERY_ITERATIONS = 6;
    private const int QUERY_BUFFER_CAPACITY = 32;
    private const int GROUND_RAY_BUFFER_CAPACITY = 16;
    private const float GROUND_SURFACE_MIN_NORMAL_Y = 0.35f;
    private const float GROUND_SAMPLE_HORIZONTAL_RATIO = 0.6f;

    private readonly Rigidbody enemyRigidbody;
    private readonly Collider bodyCollider;
    private readonly int stageLayer;
    private readonly int playerLayer;
    private readonly EnemyPlayerReference playerReference = new EnemyPlayerReference();
    private readonly RaycastHit[] castBuffer = new RaycastHit[QUERY_BUFFER_CAPACITY];
    private readonly RaycastHit[] groundRayBuffer = new RaycastHit[GROUND_RAY_BUFFER_CAPACITY];
    private readonly Collider[] overlapBuffer = new Collider[QUERY_BUFFER_CAPACITY];
    private readonly Dictionary<Collider, EnemyContactInfo> previousContacts =
        new Dictionary<Collider, EnemyContactInfo>();
    private readonly Dictionary<Collider, EnemyContactInfo> currentContacts =
        new Dictionary<Collider, EnemyContactInfo>();
    private Transform previousContactPlayer;
    private EnemyContactInfo previousPlayerContact;
    private int contactHistoryVersion;

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

    private EnemyContactInfo CreatePlayerContact(Collider otherCollider)
    {
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
    /// Player本体の現在の接触と、突進の移動経路上の命中を個別に通知する。
    /// </summary>
    public void Step(
        Action<EnemyContactInfo, bool> notifyContact,
        Action<EnemyContactInfo> notifyExit,
        Action<EnemyContactInfo> notifyAttackHit,
        bool stageMovementHandledExternally,
        bool allowPlayerPassThrough,
        bool isStationary)
    {
        int historyVersion = contactHistoryVersion;
        currentContacts.Clear();
        if (!isStationary)
        {
            if (!stageMovementHandledExternally)
            {
                RecoverStagePenetrations();
            }

            ConstrainMovement(stageMovementHandledExternally, allowPlayerPassThrough);
        }

        // 通常移動は表面の手前で止まるため、移動停止用のSkinの分だけ照会形状を広げる。
        float contactMargin = isStationary || allowPlayerPassThrough
            ? 0f
            : MOVEMENT_SKIN + MIN_MOVEMENT_DISTANCE;
        bool hasPlayerContact =
            TryGetPlayerContact(contactMargin, out EnemyContactInfo playerContact, out Transform player);
        bool isNewPlayerContact = hasPlayerContact && player != previousContactPlayer;
        EnemyContactInfo endedPlayerContact = previousPlayerContact;
        previousContactPlayer = hasPlayerContact ? player : null;
        previousPlayerContact = playerContact;
        if (hasPlayerContact)
        {
            notifyContact?.Invoke(playerContact, isNewPlayerContact);
            if (historyVersion != contactHistoryVersion)
            {
                return;
            }
        }

        // Colliderが入れ替わった場合も、以前の接地候補をKnockbackMotorから取り除く。
        if (endedPlayerContact.IsValid && endedPlayerContact.OtherCollider != playerContact.OtherCollider)
        {
            notifyExit?.Invoke(endedPlayerContact);
            if (historyVersion != contactHistoryVersion)
            {
                return;
            }
        }

        foreach (KeyValuePair<Collider, EnemyContactInfo> contact in currentContacts)
        {
            notifyContact?.Invoke(contact.Value, !previousContacts.ContainsKey(contact.Key));
            if (historyVersion != contactHistoryVersion)
            {
                return;
            }
        }

        foreach (KeyValuePair<Collider, EnemyContactInfo> contact in previousContacts)
        {
            if (!currentContacts.ContainsKey(contact.Key))
            {
                notifyExit?.Invoke(contact.Value);
                if (historyVersion != contactHistoryVersion)
                {
                    return;
                }
            }
        }

        previousContacts.Clear();
        foreach (KeyValuePair<Collider, EnemyContactInfo> contact in currentContacts)
        {
            previousContacts.Add(contact.Key, contact.Value);
        }

        // Stageの接触処理で攻撃が停止した場合も考慮し、最終速度で届く範囲だけ照会する。
        // Sweepの命中を現在の接触履歴に入れると、次のStateで実接触のEnterを失う。
        if (allowPlayerPassThrough && !hasPlayerContact && bodyCollider != null &&
            bodyCollider.enabled && bodyCollider.gameObject.activeInHierarchy)
        {
            NotifySweptPlayerHit(notifyAttackHit);
        }
    }

    private void ConstrainMovement(bool stageMovementHandledExternally, bool allowPlayerPassThrough)
    {
        int stageMask = stageMovementHandledExternally ? 0 : 1 << stageLayer;
        int movementMask = allowPlayerPassThrough ? stageMask : stageMask | (1 << playerLayer);
        Vector3 velocity = enemyRigidbody.linearVelocity;
        // RigidbodyのGravityはこのFixedUpdateの後に反映されるため、移動予定量に含める。
        Vector3 gravityVelocityChange = enemyRigidbody.useGravity
            ? Physics.gravity * Time.fixedDeltaTime
            : Vector3.zero;
        Vector3 predictedVelocity = velocity + gravityVelocityChange;
        predictedVelocity.z = 0f;
        float distance = predictedVelocity.magnitude * Time.fixedDeltaTime;
        float stageAllowedFraction = 1f;
        float groundAllowedFraction = 1f;
        float playerAllowedFraction = 1f;

        if (movementMask != 0 && distance > MIN_MOVEMENT_DISTANCE)
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

                // 開始位置の重なりを表すCastのゼロ距離ヒットは、仮の法線を返す。
                // 微小移動だけでなく今回の移動予定全体で侵入するか確認し、
                // 床に沿って離れていく移動まで停止しない。
                if (hit.distance <= MIN_MOVEMENT_DISTANCE &&
                    !WouldPenetrateAfterMove(hit.collider,
                        predictedVelocity * Time.fixedDeltaTime))
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

                float fraction = Mathf.Clamp01(
                    (hit.distance - MOVEMENT_SKIN) / distance);
                if (hit.collider.gameObject.layer == stageLayer)
                {
                    RecordContact(new EnemyContactInfo(
                        hit.collider, hit.point, normal, predictedVelocity));
                    stageAllowedFraction = Mathf.Min(stageAllowedFraction, fraction);
                    if (normal.y > 0.35f)
                    {
                        groundAllowedFraction = Mathf.Min(groundAllowedFraction, fraction);
                    }
                }
                else
                {
                    playerAllowedFraction = Mathf.Min(playerAllowedFraction, fraction);
                }
            }
        }

        float allowedFraction = Mathf.Min(stageAllowedFraction, playerAllowedFraction);
        if (allowedFraction < 1f)
        {
            // StageはTriggerなので接触ソルバーが重力による落下を止めない。
            // 床に進路を制限された分の重力は次の物理シミュレーション前に相殺する。
            // 壁・天井・Playerだけの接触では落下を妨げない。
            float stageGravityCancellation = 1f - groundAllowedFraction;
            velocity.x = velocity.x * allowedFraction -
                gravityVelocityChange.x * stageGravityCancellation;
            velocity.y = velocity.y * allowedFraction -
                gravityVelocityChange.y * stageGravityCancellation;
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

        if (!stageMovementHandledExternally)
        {
            PreventPredictedStagePenetration(ref velocity, gravityVelocityChange);
        }

        if (!enemyRigidbody.isKinematic)
        {
            enemyRigidbody.linearVelocity = velocity;
        }
    }

    private bool TryGetPlayerContact(
        float margin, out EnemyContactInfo contact, out Transform player)
    {
        contact = default;
        player = null;
        if (bodyCollider == null || !bodyCollider.enabled ||
            !bodyCollider.gameObject.activeInHierarchy ||
            !playerReference.TryGetPlayerTransform(out player))
        {
            return false;
        }

        Collider[] colliders = OverlapBody(enemyRigidbody.position, 1 << playerLayer,
            out int overlapCount, margin);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider playerCollider = colliders[i];
            if (IsBlocking(playerCollider) && playerReference.IsPlayerCollider(playerCollider))
            {
                // 同じPlayerの複数の本体Colliderが重なっても、通知はPlayerごとに一つ。
                contact = CreatePlayerContact(playerCollider);
                return true;
            }
        }

        return false;
    }

    public void ClearContacts()
    {
        contactHistoryVersion++;
        previousContacts.Clear();
        currentContacts.Clear();
        previousContactPlayer = null;
        previousPlayerContact = default;
        playerReference.Clear();
    }

    private void NotifySweptPlayerHit(Action<EnemyContactInfo> notifyAttackHit)
    {
        Vector3 velocity = enemyRigidbody.linearVelocity;
        if (enemyRigidbody.useGravity)
        {
            velocity += Physics.gravity * Time.fixedDeltaTime;
        }

        velocity.z = 0f;
        float distance = velocity.magnitude * Time.fixedDeltaTime;
        if (distance <= MIN_MOVEMENT_DISTANCE)
        {
            return;
        }

        RaycastHit[] hits = CastBody(velocity.normalized, distance, 1 << playerLayer, out int hitCount);
        RaycastHit closestHit = default;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hits[i];
            // 初期重なりは現在位置のOverlapで処理済み。距離0の仮のCastヒットは使わない。
            if (hit.distance > 0f && hit.distance < closestDistance &&
                IsBlocking(hit.collider) && playerReference.IsPlayerCollider(hit.collider))
            {
                closestHit = hit;
                closestDistance = hit.distance;
            }
        }

        if (closestHit.collider != null)
        {
            notifyAttackHit?.Invoke(new EnemyContactInfo(
                closestHit.collider, closestHit.point, closestHit.normal, velocity));
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

        // MeshColliderでは深く侵入した場合にComputePenetrationが床面を検出できないことがある。
        // 下向きRayで実際の床面を調べ、すでに下端が越えていた場合だけ持ち上げる。
        if (enemyRigidbody.linearVelocity.y <= 0f &&
            TryGetStageGroundSurface(bodyPosition, bodyPosition,
                out RaycastHit groundHit, out float bottomHeight) &&
            groundHit.point.y - bottomHeight > MOVEMENT_SKIN * 0.5f)
        {
            bodyPosition.y += groundHit.point.y - bottomHeight + MOVEMENT_SKIN;
            RecordContact(new EnemyContactInfo(
                groundHit.collider, groundHit.point, groundHit.normal,
                enemyRigidbody.linearVelocity));
            changed = true;
        }

        if (changed)
        {
            bodyPosition.z = enemyRigidbody.position.z;
            enemyRigidbody.position = bodyPosition;
        }
    }

    /// <summary>
    /// Sweepを抜けた移動を重力込みで再検査し、床面を通り抜ける速度を制限する。
    /// Triggerは接触ソルバーで止まらないため、重なり判定を通過した場合も床面Rayで補完する。
    /// </summary>
    private void PreventPredictedStagePenetration(
        ref Vector3 velocity, Vector3 gravityVelocityChange)
    {
        Vector3 predictedVelocity = velocity + gravityVelocityChange;
        predictedVelocity.z = 0f;
        Vector3 movement = predictedVelocity * Time.fixedDeltaTime;
        if (movement.sqrMagnitude > MIN_MOVEMENT_DISTANCE * MIN_MOVEMENT_DISTANCE &&
            HasStagePenetrationAtPosition(enemyRigidbody.position + movement))
        {
            // 重なりを検出できた場合は、最初の侵入位置まで移動割合を縮める。
            float safeFraction = 0f;
            float blockedFraction = 1f;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                float fraction = (safeFraction + blockedFraction) * 0.5f;
                if (HasStagePenetrationAtPosition(
                        enemyRigidbody.position + movement * fraction))
                {
                    blockedFraction = fraction;
                }
                else
                {
                    safeFraction = fraction;
                }
            }

            velocity.x = predictedVelocity.x * safeFraction - gravityVelocityChange.x;
            velocity.y = predictedVelocity.y * safeFraction - gravityVelocityChange.y;
        }

        // ComputePenetrationはMeshColliderの裏面三角形を無視する。
        // すでに床面を通り抜けていると重なりが返らない場合もあるため、
        // 接地可能な面を上からRayで調べ、落下中に床の下へ進む速度だけを制限する。
        predictedVelocity = velocity + gravityVelocityChange;
        if (predictedVelocity.y > 0f ||
            predictedVelocity.sqrMagnitude * Time.fixedDeltaTime * Time.fixedDeltaTime <=
            MIN_MOVEMENT_DISTANCE * MIN_MOVEMENT_DISTANCE)
        {
            return;
        }

        Vector3 predictedPosition = enemyRigidbody.position +
            new Vector3(predictedVelocity.x, predictedVelocity.y, 0f) * Time.fixedDeltaTime;
        if (!TryGetStageGroundSurface(enemyRigidbody.position, predictedPosition,
                out RaycastHit groundHit, out float predictedBottomHeight))
        {
            return;
        }

        float penetrationDepth = groundHit.point.y + MOVEMENT_SKIN - predictedBottomHeight;
        if (penetrationDepth <= 0f)
        {
            return;
        }

        // X方向や上昇速度は変更しない。物理更新で加算される重力を含む
        // 予測位置の下端だけが床上にとどまるよう、Y速度を引き上げる。
        velocity.y += penetrationDepth / Time.fixedDeltaTime;
        RecordContact(new EnemyContactInfo(
            groundHit.collider, groundHit.point, groundHit.normal,
            predictedVelocity));
    }

    /// <summary>
    /// 本体の予定位置から、足元を横切るStageの上面を検出する。
    /// Overlap/ComputePenetrationが検出できない深い侵入を補うための床専用判定。
    /// </summary>
    private bool TryGetStageGroundSurface(
        Vector3 startBodyPosition, Vector3 targetBodyPosition,
        out RaycastHit groundHit, out float targetBottomHeight)
    {
        groundHit = default;
        targetBottomHeight = float.NegativeInfinity;
        GetBodyBoundsForGroundRay(startBodyPosition,
            out Vector3 startCenter, out float startTopHeight,
            out Vector2 startExtents);
        GetBodyBoundsForGroundRay(targetBodyPosition,
            out Vector3 targetCenter, out float targetTopHeight,
            out Vector2 horizontalAndVerticalExtents);

        targetBottomHeight = targetCenter.y - horizontalAndVerticalExtents.y;
        float rayStartHeight = Mathf.Max(startTopHeight, targetTopHeight) + MOVEMENT_SKIN;
        float rayDistance = rayStartHeight -
            Mathf.Min(targetBottomHeight, startCenter.y - startExtents.y) +
            GROUND_PROBE_DISTANCE;
        if (rayDistance <= 0f)
        {
            return false;
        }

        float sampleOffset = horizontalAndVerticalExtents.x *
            GROUND_SAMPLE_HORIZONTAL_RATIO;
        bool foundGround = false;
        for (int sample = -1; sample <= 1; sample++)
        {
            Vector3 rayOrigin = new Vector3(
                targetCenter.x + sampleOffset * sample,
                rayStartHeight, targetCenter.z);
            int count = Physics.RaycastNonAlloc(
                rayOrigin, Vector3.down, groundRayBuffer, rayDistance,
                1 << stageLayer, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = groundRayBuffer;
            if (count == groundRayBuffer.Length)
            {
                // バッファ満杯でも実際の地面を見落とさない。
                hits = Physics.RaycastAll(
                    rayOrigin, Vector3.down, rayDistance,
                    1 << stageLayer, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (!IsBlocking(hit.collider) ||
                    hit.collider.gameObject.layer != stageLayer ||
                    hit.normal.y <= GROUND_SURFACE_MIN_NORMAL_Y)
                {
                    continue;
                }

                if (!foundGround || hit.point.y > groundHit.point.y)
                {
                    groundHit = hit;
                    foundGround = true;
                }
            }
        }

        return foundGround;
    }

    private void GetBodyBoundsForGroundRay(
        Vector3 bodyPosition, out Vector3 center,
        out float topHeight, out Vector2 horizontalAndVerticalExtents)
    {
        GetGeometry(bodyPosition, out center, out Vector3 halfExtents,
            out Quaternion rotation, out Vector3 capsuleA,
            out Vector3 capsuleB, out float capsuleRadius);

        if (bodyCollider is BoxCollider)
        {
            Vector3 axisX = rotation * Vector3.right;
            Vector3 axisY = rotation * Vector3.up;
            Vector3 axisZ = rotation * Vector3.forward;
            horizontalAndVerticalExtents = new Vector2(
                Mathf.Abs(axisX.x) * halfExtents.x +
                Mathf.Abs(axisY.x) * halfExtents.y +
                Mathf.Abs(axisZ.x) * halfExtents.z,
                Mathf.Abs(axisX.y) * halfExtents.x +
                Mathf.Abs(axisY.y) * halfExtents.y +
                Mathf.Abs(axisZ.y) * halfExtents.z);
        }
        else
        {
            horizontalAndVerticalExtents = new Vector2(
                Mathf.Max(Mathf.Abs(capsuleA.x - center.x),
                    Mathf.Abs(capsuleB.x - center.x)) + capsuleRadius,
                Mathf.Max(Mathf.Abs(capsuleA.y - center.y),
                    Mathf.Abs(capsuleB.y - center.y)) + capsuleRadius);
        }

        topHeight = center.y + horizontalAndVerticalExtents.y;
    }

    private bool HasStagePenetrationAtPosition(Vector3 bodyPosition)
    {
        Collider[] colliders = OverlapBody(bodyPosition, 1 << stageLayer,
            out int overlapCount);
        for (int i = 0; i < overlapCount; i++)
        {
            Collider obstacle = colliders[i];
            if (!IsBlocking(obstacle) ||
                !Physics.ComputePenetration(
                    bodyCollider, GetColliderWorldPosition(bodyPosition),
                    bodyCollider.transform.rotation,
                    obstacle, obstacle.transform.position, obstacle.transform.rotation,
                    out Vector3 direction, out float distance))
            {
                continue;
            }

            direction.z = 0f;
            if (direction.sqrMagnitude > MIN_PLANAR_NORMAL * MIN_PLANAR_NORMAL &&
                distance > MOVEMENT_SKIN * 0.5f)
            {
                return true;
            }
        }

        return false;
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
        // PlayerBodyレイヤーにはプレイヤーのチェーンソー等のTriggerも入る。
        if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy ||
            candidate.isTrigger ||
            candidate.attachedRigidbody == enemyRigidbody ||
            candidate.transform.IsChildOf(enemyRigidbody.transform))
        {
            return false;
        }

        int layer = candidate.gameObject.layer;
        return layer == stageLayer || layer == playerLayer;
    }

    private Vector3 GetColliderWorldPosition(Vector3 bodyPosition)
    {
        return bodyPosition;
    }

    private Collider[] OverlapBody(Vector3 bodyPosition, int movementMask, out int count,
        float margin = 0f)
    {
        GetGeometry(bodyPosition, out Vector3 center, out Vector3 halfExtents,
            out Quaternion rotation, out Vector3 capsuleA, out Vector3 capsuleB,
            out float capsuleRadius);

        halfExtents += Vector3.one * margin;
        capsuleRadius += margin;
        count = bodyCollider is BoxCollider
            ? Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, rotation,
                movementMask, QueryTriggerInteraction.Ignore)
            : Physics.OverlapCapsuleNonAlloc(capsuleA, capsuleB, capsuleRadius,
                overlapBuffer, movementMask, QueryTriggerInteraction.Ignore);

        if (count < overlapBuffer.Length)
        {
            return overlapBuffer;
        }

        // 検出上限によって最寄り障害物を見逃さないよう、満杯のときだけ全件取得する。
        Collider[] all = bodyCollider is BoxCollider
            ? Physics.OverlapBox(center, halfExtents, rotation, movementMask,
                QueryTriggerInteraction.Ignore)
            : Physics.OverlapCapsule(capsuleA, capsuleB, capsuleRadius,
                movementMask, QueryTriggerInteraction.Ignore);
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
                distance, movementMask, QueryTriggerInteraction.Ignore)
            : Physics.CapsuleCastNonAlloc(capsuleA, capsuleB, capsuleRadius, direction,
                castBuffer, distance, movementMask, QueryTriggerInteraction.Ignore);

        if (count < castBuffer.Length)
        {
            return castBuffer;
        }

        RaycastHit[] all = bodyCollider is BoxCollider
            ? Physics.BoxCastAll(center, halfExtents, direction, rotation, distance,
                movementMask, QueryTriggerInteraction.Ignore)
            : Physics.CapsuleCastAll(capsuleA, capsuleB, capsuleRadius, direction,
                distance, movementMask, QueryTriggerInteraction.Ignore);
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
