using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Enemyが被弾した際のノックバック初速度と、ノックバック中の接地状態を管理する。
/// </summary>
/// <remarks>
/// AttackVectorのX成分はノックバック強度として扱い、
/// 実際の左右方向は攻撃元とEnemyの位置関係から決定する。
/// Y成分はAttackVectorの符号をそのまま使用する。
///
/// ノックバック速度は開始時または再被弾時に一度だけX/Yへ設定する。
/// Gravityを使用するEnemyが一度でも空中へ出た場合は、
/// 以降の軌道をRigidbodyのGravity・衝突・Damping等の物理結果に任せ、着地時にX/Y速度を停止する。
/// 一度も空中へ出ない場合とGravityを使用しないEnemyは、既存のノックバック時間で終了する。
///
/// 接地確定はCollisionの接触法線から追跡する。
/// 着地直前の距離判定だけは、Enemy本体の下端から3D Physicsの下向きRayで確認する。
/// どちらも上向き法線を持たない面は地面として扱わない。
///
/// 通常移動AI、Damage State遷移、HP変更、見た目の傾斜そのものは担当しない。
/// </remarks>
public sealed class EnemyKnockbackMotor
{
    private const float KNOCKBACK_DURATION = 0.15f;
    private const float DIRECTION_EPSILON = 0.001f;
    private const float GROUND_NORMAL_EPSILON = 0.001f;
    private const int GROUND_DISTANCE_HIT_BUFFER_SIZE = 16;

    private readonly Rigidbody enemyRigidbody;
    private readonly Collider[] enemyColliders;
    private readonly RaycastHit[] groundDistanceHitBuffer =
        new RaycastHit[GROUND_DISTANCE_HIT_BUFFER_SIZE];
    private readonly HashSet<Collider> groundContactColliders =
        new HashSet<Collider>();

    private float remainingDuration;
    private bool isKnockbackActive;
    private bool shouldTrackLandingForCurrentKnockback;
    private bool hasBecomeAirborneSinceKnockbackStart;

    public bool IsKnockbackActive =>
        isKnockbackActive;

    /// <summary>
    /// 現在、上向き法線を持つ接触面に接地しているか。
    /// </summary>
    public bool IsGrounded =>
        groundContactColliders.Count > 0;

    /// <summary>
    /// 最新のノックバック開始後、一度でも非接地になったか。
    /// </summary>
    public bool HasBecomeAirborneSinceKnockbackStart =>
        hasBecomeAirborneSinceKnockbackStart;

    /// <summary>
    /// 現在のゲームプレイ上の上下方向速度。
    /// </summary>
    /// <remarks>
    /// 現在のEnemyノックバックはX/Y平面を使用しているため、
    /// Rigidbody.linearVelocity.yを返す。
    /// </remarks>
    public float VerticalVelocity =>
        enemyRigidbody.linearVelocity.y;

    public EnemyKnockbackMotor(
        Rigidbody enemyRigidbody)
    {
        this.enemyRigidbody = enemyRigidbody
            ? enemyRigidbody
            : throw new ArgumentNullException(
                nameof(enemyRigidbody));

        if (enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyKnockbackMotor)}には" +
                "非KinematicのRigidbodyが必要です。");
        }

        enemyColliders =
            enemyRigidbody.GetComponentsInChildren<Collider>(
                true);
    }

    /// <summary>
    /// 現在位置から下方向へ、Enemy本体の下端を基準に指定距離以内へ
    /// 接地候補となる面があるか判定する。
    /// </summary>
    /// <remarks>
    /// 着地そのものの確定には既存のCollision接触法線を使用する。
    /// この判定はDamage Stateが「着地直前」を判断するためだけに使用する。
    ///
    /// Queryは現在のノックバックと同じ3D Physicsを使用し、
    /// Enemy自身のColliderとTriggerは対象外にする。
    /// </remarks>
    public bool IsGroundWithinDistance(
        float maxGroundDistance)
    {
        if (!enemyRigidbody.useGravity ||
            maxGroundDistance < 0f ||
            !TryGetEnemyBounds(out Bounds enemyBounds))
        {
            return false;
        }

        if (IsGrounded)
        {
            return true;
        }

        float rayDistance =
            enemyBounds.extents.y +
            maxGroundDistance;

        if (rayDistance <= 0f)
        {
            return false;
        }

        int hitCount =
            Physics.RaycastNonAlloc(
                enemyBounds.center,
                Vector3.down,
                groundDistanceHitBuffer,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

        float nearestGroundDistance =
            float.PositiveInfinity;

        for (int i = 0;
             i < hitCount;
             i++)
        {
            RaycastHit hit =
                groundDistanceHitBuffer[i];

            Collider hitCollider =
                hit.collider;

            if (hitCollider == null ||
                hitCollider.attachedRigidbody == enemyRigidbody ||
                hit.normal.y <= GROUND_NORMAL_EPSILON)
            {
                continue;
            }

            float groundDistance =
                Mathf.Max(
                    0f,
                    hit.distance -
                    enemyBounds.extents.y);

            if (groundDistance <
                nearestGroundDistance)
            {
                nearestGroundDistance =
                    groundDistance;
            }
        }

        return nearestGroundDistance <=
               maxGroundDistance;
    }

    /// <summary>
    /// 最新の攻撃内容でノックバックを開始、または上書きする。
    /// </summary>
    /// <remarks>
    /// 再被弾時は既存ノックバックを一度停止せず、
    /// 最新のAttackVectorによる初速度へ直接置き換えて継続条件をリスタートする。
    ///
    /// 「一度でも非接地になったか」は最新のノックバック単位で判定し直す。
    /// 再被弾時点ですでに空中なら、その時点から非接地として扱う。
    /// </remarks>
    /// <param name="attackVector">
    /// Xは横方向の強さ、Yは縦方向の向きと強さを表す。
    /// </param>
    /// <param name="hitSourceDirectionSign">
    /// X/Yゲームプレイ平面上で見た攻撃元の左右方向。
    /// -1はEnemyの左側、+1は右側。
    /// </param>
    public void StartKnockback(
        Vector2 attackVector,
        float hitSourceDirectionSign)
    {
        float normalizedHitSourceDirectionSign =
            hitSourceDirectionSign < 0f
                ? -1f
                : 1f;

        float horizontalKnockback =
            Mathf.Abs(attackVector.x) *
            -normalizedHitSourceDirectionSign;

        Vector2 initialKnockbackVelocity =
            new Vector2(
                horizontalKnockback,
                attackVector.y);

        // Gravityを使用しない飛行Enemyでは「着地待ち」が成立しないため、
        // そのノックバックでは空中→着地を終了条件にしない。
        // Gravityを使用するEnemyが空中で再被弾した場合は、再被弾時点から着地待ち対象にする。
        shouldTrackLandingForCurrentKnockback =
            enemyRigidbody.useGravity;

        hasBecomeAirborneSinceKnockbackStart =
            shouldTrackLandingForCurrentKnockback &&
            !IsGrounded;

        if (initialKnockbackVelocity.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            FinishKnockbackVelocity();
            return;
        }

        remainingDuration =
            KNOCKBACK_DURATION;

        isKnockbackActive = true;

        // 初速度だけを設定し、以降は毎物理更新で再設定しない。
        // これによりGravityや衝突による速度変化をそのままノックバック軌道へ反映する。
        ApplyKnockbackVelocity(
            initialKnockbackVelocity);
    }

    /// <summary>
    /// Damage Stateの物理更新から呼び出し、ノックバックの終了条件を管理する。
    /// </summary>
    public void UpdateKnockback()
    {
        if (!isKnockbackActive)
        {
            return;
        }

        UpdateAirborneAndLandingState();

        if (!isKnockbackActive)
        {
            return;
        }

        // Gravityを使用するEnemyが一度でも空中へ出た後は、
        // 時間では止めず着地までRigidbodyの物理結果を使用する。
        if (shouldTrackLandingForCurrentKnockback &&
            hasBecomeAirborneSinceKnockbackStart)
        {
            return;
        }

        remainingDuration -=
            Time.fixedDeltaTime;

        if (remainingDuration <= 0f)
        {
            FinishKnockbackVelocity();
        }
    }

    /// <summary>
    /// Collision開始時の接触情報から接地状態を更新する。
    /// </summary>
    public void HandleCollisionEnter(
        Collision collision)
    {
        UpdateGroundContact(
            collision);

        UpdateAirborneAndLandingState();
    }

    /// <summary>
    /// Collision継続中の接触情報から接地状態を更新する。
    /// </summary>
    /// <remarks>
    /// 同じColliderとの接触でも、地面接触から壁面接触だけへ変化する場合があるため、
    /// Stayでも現在の接触法線を再評価する。
    /// </remarks>
    public void HandleCollisionStay(
        Collision collision)
    {
        UpdateGroundContact(
            collision);

        UpdateAirborneAndLandingState();
    }

    /// <summary>
    /// Collision終了時に、そのColliderを接地候補から除外する。
    /// </summary>
    public void HandleCollisionExit(
        Collision collision)
    {
        if (collision == null ||
            collision.collider == null)
        {
            return;
        }

        groundContactColliders.Remove(
            collision.collider);

        UpdateAirborneAndLandingState();
    }

    /// <summary>
    /// State中断や死亡遷移などでノックバックを強制終了する。
    /// </summary>
    /// <remarks>
    /// 通常の時間切れ・着地終了とは異なり、着地待ち用の履歴も破棄する。
    /// </remarks>
    public void StopKnockback()
    {
        if (isKnockbackActive)
        {
            StopPlanarVelocity();
        }

        remainingDuration = 0f;
        isKnockbackActive = false;
        shouldTrackLandingForCurrentKnockback = false;
        hasBecomeAirborneSinceKnockbackStart = false;
    }

    /// <summary>
    /// Disable中はCollision Exitを受け取れない場合があるため、
    /// 再有効化前に保持中の接地参照を破棄する。
    /// </summary>
    public void ClearGroundContactTracking()
    {
        groundContactColliders.Clear();
    }

    /// <summary>
    /// 通常のノックバック終了処理。
    /// </summary>
    /// <remarks>
    /// X/Y速度を停止する。
    /// 空中へ出た履歴はDamage Stateの姿勢復帰判定に必要なため保持する。
    /// </remarks>
    private void FinishKnockbackVelocity()
    {
        if (isKnockbackActive)
        {
            StopPlanarVelocity();
        }

        remainingDuration = 0f;
        isKnockbackActive = false;
    }

    /// <summary>
    /// ノックバック開始時または再被弾時のX/Y初速度をRigidbodyへ反映する。
    /// </summary>
    private void ApplyKnockbackVelocity(
        Vector2 initialKnockbackVelocity)
    {
        if (enemyRigidbody == null ||
            enemyRigidbody.isKinematic)
        {
            return;
        }

        Vector3 velocity =
            enemyRigidbody.linearVelocity;

        velocity.x =
            initialKnockbackVelocity.x;

        velocity.y =
            initialKnockbackVelocity.y;

        enemyRigidbody.linearVelocity =
            velocity;
    }

    /// <summary>
    /// RigidbodyのX/Y速度だけを停止する。
    /// </summary>
    private void StopPlanarVelocity()
    {
        if (enemyRigidbody == null ||
            enemyRigidbody.isKinematic)
        {
            return;
        }

        Vector3 velocity =
            enemyRigidbody.linearVelocity;

        velocity.x = 0f;
        velocity.y = 0f;

        enemyRigidbody.linearVelocity =
            velocity;
    }

    /// <summary>
    /// Gravityを使用するノックバックについて、離地履歴と着地終了を更新する。
    /// </summary>
    /// <remarks>
    /// 一度離地するまでは接地したままの時間切れノックバックとして扱う。
    /// 離地後に再接地した場合は、その瞬間をノックバック終了としてX/Y速度を停止する。
    /// </remarks>
    private void UpdateAirborneAndLandingState()
    {
        if (!isKnockbackActive ||
            !shouldTrackLandingForCurrentKnockback)
        {
            return;
        }

        if (!IsGrounded)
        {
            hasBecomeAirborneSinceKnockbackStart = true;
            return;
        }

        if (hasBecomeAirborneSinceKnockbackStart)
        {
            FinishKnockbackVelocity();
        }
    }

    private void UpdateGroundContact(
        Collision collision)
    {
        if (collision == null ||
            collision.collider == null)
        {
            return;
        }

        Collider otherCollider =
            collision.collider;

        if (HasGroundFacingContact(collision))
        {
            groundContactColliders.Add(
                otherCollider);
            return;
        }

        groundContactColliders.Remove(
            otherCollider);
    }

    /// <summary>
    /// Rigidbodyに属する有効な非Trigger ColliderをまとめたワールドBoundsを取得する。
    /// </summary>
    private bool TryGetEnemyBounds(
        out Bounds enemyBounds)
    {
        enemyBounds = default;

        bool hasBounds = false;

        for (int i = 0;
             i < enemyColliders.Length;
             i++)
        {
            Collider enemyCollider =
                enemyColliders[i];

            if (enemyCollider == null ||
                !enemyCollider.enabled ||
                enemyCollider.isTrigger ||
                enemyCollider.attachedRigidbody != enemyRigidbody)
            {
                continue;
            }

            if (!hasBounds)
            {
                enemyBounds =
                    enemyCollider.bounds;

                hasBounds = true;
                continue;
            }

            enemyBounds.Encapsulate(
                enemyCollider.bounds);
        }

        return hasBounds;
    }

    /// <summary>
    /// 壁ではなく、Enemyを下側から支える向きの接触が含まれるか判定する。
    /// </summary>
    private static bool HasGroundFacingContact(
        Collision collision)
    {
        int contactCount =
            collision.contactCount;

        for (int i = 0;
             i < contactCount;
             i++)
        {
            ContactPoint contact =
                collision.GetContact(i);

            if (contact.normal.y >
                GROUND_NORMAL_EPSILON)
            {
                return true;
            }
        }

        return false;
    }
}
