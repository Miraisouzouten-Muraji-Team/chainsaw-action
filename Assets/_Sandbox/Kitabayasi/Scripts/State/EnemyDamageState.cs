using System;
using UnityEngine;

/// <summary>
/// Enemyが攻撃を受けた後のやられ状態を管理する。
/// </summary>
/// <remarks>
/// 責務:
/// ・やられState開始時にダメージ点滅を開始する。
/// ・やられ中、攻撃を受けた側とは反対方向へ見た目用Transformを傾ける。
/// ・EnemyKnockbackMotorへ今回の被弾ノックバックを要求する。
/// ・ノックバック状態と接地状態に合わせて被弾姿勢を復帰する。
/// ・指定時間の経過を管理する。
/// ・やられ終了後に次のStateを決めるための遷移判定を要求する。
///
/// 担当しない責務:
/// ・HPの保持やダメージ計算。
/// ・Materialの具体的な切り替え処理。
/// ・Rigidbodyへの具体的なノックバック反映。
/// ・地面接触そのものの判定。
/// ・実際のState切り替え。
/// ・Collisionによる接触ダメージ処理。
/// </remarks>
public sealed class EnemyDamageState : IEnemyState
{
    private readonly EnemyDamageFlash enemyDamageFlash;
    private readonly EnemyKnockbackMotor enemyKnockbackMotor;
    private readonly Transform tiltTarget;
    private readonly float damageStateDuration;
    private readonly float tiltRecoveryDuration;
    private readonly float tiltRecoveryGroundDistance;
    private readonly float tiltAngle;
    private readonly Action requestDamageRecovery;

    private float elapsedTime;
    private float tiltRecoveryElapsedTime;
    private float hitSourceDirectionSign = 1f;
    private Vector2 attackVector;

    private Quaternion originalLocalRotation;
    private Quaternion tiltRecoveryStartLocalRotation;

    private bool hasCapturedOriginalRotation;
    private bool isTiltApplied;
    private bool isTiltRecovering;
    private bool hasRequestedDamageRecovery;

    public EnemyDamageState(
        EnemyDamageFlash enemyDamageFlash,
        EnemyKnockbackMotor enemyKnockbackMotor,
        Transform tiltTarget,
        float damageStateDuration,
        float tiltRecoveryDuration,
        float tiltRecoveryGroundDistance,
        float tiltAngle,
        Action requestDamageRecovery)
    {
        this.enemyDamageFlash = enemyDamageFlash
            ?? throw new ArgumentNullException(
                nameof(enemyDamageFlash));

        this.enemyKnockbackMotor = enemyKnockbackMotor
            ?? throw new ArgumentNullException(
                nameof(enemyKnockbackMotor));

        this.tiltTarget = tiltTarget;

        this.damageStateDuration =
            Mathf.Max(0f, damageStateDuration);

        this.tiltRecoveryDuration =
            Mathf.Max(
                0f,
                tiltRecoveryDuration);

        this.tiltRecoveryGroundDistance =
            Mathf.Max(
                0f,
                tiltRecoveryGroundDistance);

        this.tiltAngle = Mathf.Abs(tiltAngle);

        this.requestDamageRecovery = requestDamageRecovery
            ?? throw new ArgumentNullException(
                nameof(requestDamageRecovery));
    }

    public void Enter()
    {
        elapsedTime = 0f;
        hasRequestedDamageRecovery = false;

        ResetTiltRecovery();

        CaptureOriginalRotation();
        ApplyTilt();

        enemyKnockbackMotor.StartKnockback(
            attackVector,
            hitSourceDirectionSign);

        enemyDamageFlash.PlayDamageFlash();
    }

    public void Update()
    {
        enemyKnockbackMotor.UpdateKnockback();

        UpdateHitTilt();

        if (hasRequestedDamageRecovery)
        {
            return;
        }

        elapsedTime += Time.deltaTime;

        if (elapsedTime < damageStateDuration)
        {
            return;
        }

        // Damage Stateが先に終了してノックバックを途中キャンセルしたり、
        // 空中のまま通常AIへ制御が戻ったりしないよう、物理的な被弾処理の完了を待つ。
        if (enemyKnockbackMotor.IsKnockbackActive ||
            IsWaitingForLanding())
        {
            return;
        }

        hasRequestedDamageRecovery = true;

        requestDamageRecovery.Invoke();
    }

    public void Exit()
    {
        enemyKnockbackMotor.StopKnockback();

        RestoreOriginalRotation();

        hasCapturedOriginalRotation = false;
        elapsedTime = 0f;
        hasRequestedDamageRecovery = false;

        ResetTiltRecovery();
    }

    /// <summary>
    /// 次に開始または再開始するDamage Stateで使用する被弾情報を設定する。
    /// </summary>
    /// <param name="directionSign">
    /// X/Yゲームプレイ平面上で見た攻撃元の左右方向。
    /// -1はEnemyの左側、+1は右側。0は方向不明として+1へ補正する。
    /// </param>
    /// <param name="newAttackVector">
    /// 今回命中したAttackDataのAttackVector。
    /// </param>
    public void SetHitData(
        float directionSign,
        Vector2 newAttackVector)
    {
        hitSourceDirectionSign =
            directionSign < 0f
                ? -1f
                : 1f;

        attackVector =
            newAttackVector;
    }

    /// <summary>
    /// やられState中に再度攻撃を受けた場合、
    /// 最新の被弾情報でノックバック・やられ時間・点滅演出を最初からやり直す。
    /// </summary>
    public void Restart()
    {
        elapsedTime = 0f;
        hasRequestedDamageRecovery = false;

        ResetTiltRecovery();

        if (!hasCapturedOriginalRotation)
        {
            CaptureOriginalRotation();
        }

        ApplyTilt();

        enemyKnockbackMotor.StartKnockback(
            attackVector,
            hitSourceDirectionSign);

        enemyDamageFlash.PlayDamageFlash();
    }

    /// <summary>
    /// ノックバック軌道に合わせて被弾姿勢を維持・復帰する。
    /// </summary>
    /// <remarks>
    /// Gravityを使用して実際に離地した場合は、上昇中と通常の落下中は傾きを維持する。
    /// 落下中かつ地面までの距離が指定値以内になった時点でだけ復帰を開始し、
    /// 着地時は補間途中でも通常姿勢へ確定する。
    ///
    /// 一度も離地しなかった場合とGravityを使用しないEnemyは、
    /// 従来通りノックバック終了時に通常姿勢へ戻す。
    /// </remarks>
    private void UpdateHitTilt()
    {
        if (!isTiltApplied)
        {
            return;
        }

        if (!enemyKnockbackMotor
                .HasBecomeAirborneSinceKnockbackStart)
        {
            if (!enemyKnockbackMotor.IsKnockbackActive)
            {
                RestoreOriginalRotation();
            }

            return;
        }

        if (enemyKnockbackMotor.IsGrounded)
        {
            // 距離判定や補間の進行状況に関係なく、
            // 通常Stateへ被弾姿勢を持ち越さないよう着地時に確定する。
            RestoreOriginalRotation();
            return;
        }

        // 「落下中」が条件なので、頂点の速度0付近ではまだ復帰を開始しない。
        if (enemyKnockbackMotor.VerticalVelocity >= 0f)
        {
            return;
        }

        if (!isTiltRecovering)
        {
            if (!enemyKnockbackMotor.IsGroundWithinDistance(
                    tiltRecoveryGroundDistance))
            {
                return;
            }

            BeginTiltRecovery();
        }

        RecoverTiltTowardsOriginalRotation();
    }

    /// <summary>
    /// 着地直前の姿勢復帰を、現在の被弾姿勢を始点として開始する。
    /// </summary>
    private void BeginTiltRecovery()
    {
        if (tiltTarget == null ||
            !hasCapturedOriginalRotation)
        {
            return;
        }

        tiltRecoveryStartLocalRotation =
            tiltTarget.localRotation;

        tiltRecoveryElapsedTime = 0f;
        isTiltRecovering = true;
    }

    /// <summary>
    /// 復帰開始直後はゆっくり、後半ほど速くなる二乗Ease-Inで通常姿勢へ戻す。
    /// </summary>
    private void RecoverTiltTowardsOriginalRotation()
    {
        if (tiltTarget == null ||
            !hasCapturedOriginalRotation ||
            !isTiltRecovering)
        {
            return;
        }

        if (tiltRecoveryDuration <= 0f)
        {
            RestoreOriginalRotation();
            return;
        }

        tiltRecoveryElapsedTime +=
            Time.fixedDeltaTime;

        float progress =
            Mathf.Clamp01(
                tiltRecoveryElapsedTime /
                tiltRecoveryDuration);

        float easedProgress =
            progress *
            progress;

        tiltTarget.localRotation =
            Quaternion.Slerp(
                tiltRecoveryStartLocalRotation,
                originalLocalRotation,
                easedProgress);

        if (progress >= 1f)
        {
            RestoreOriginalRotation();
        }
    }

    /// <summary>
    /// 再被弾やState終了時に、進行中の着地直前復帰だけを破棄する。
    /// </summary>
    private void ResetTiltRecovery()
    {
        tiltRecoveryElapsedTime = 0f;
        tiltRecoveryStartLocalRotation = default;
        isTiltRecovering = false;
    }

    private bool IsWaitingForLanding()
    {
        return enemyKnockbackMotor
                   .HasBecomeAirborneSinceKnockbackStart &&
               !enemyKnockbackMotor.IsGrounded;
    }

    /// <summary>
    /// やられ開始前の見た目のローカル回転を保存する。
    /// </summary>
    private void CaptureOriginalRotation()
    {
        if (tiltTarget == null)
        {
            return;
        }

        originalLocalRotation =
            tiltTarget.localRotation;

        hasCapturedOriginalRotation = true;
    }

    /// <summary>
    /// X/Yゲームプレイ平面上で、攻撃を受けた側とは反対へ身体が倒れるように
    /// ゲームプレイ平面の法線であるワールドZ軸周りに見た目を傾ける。
    /// </summary>
    /// <remarks>
    /// 親Transformやモデル固有の初期向きに傾斜方向を依存させない。
    /// 元姿勢はlocalRotationで保持し、復帰時は親Transformに対する相対姿勢を復元する。
    /// </remarks>
    private void ApplyTilt()
    {
        if (tiltTarget == null ||
            !hasCapturedOriginalRotation)
        {
            return;
        }

        Quaternion baseWorldRotation =
            tiltTarget.parent != null
                ? tiltTarget.parent.rotation *
                  originalLocalRotation
                : originalLocalRotation;

        Quaternion tiltRotation =
            Quaternion.AngleAxis(
                tiltAngle * hitSourceDirectionSign,
                Vector3.forward);

        tiltTarget.rotation =
            tiltRotation *
            baseWorldRotation;

        isTiltApplied = true;
    }

    /// <summary>
    /// やられ開始前の見た目へ戻す。
    /// </summary>
    /// <remarks>
    /// Damage State中の再被弾で再度傾けられるよう、
    /// 元姿勢の保存自体はState終了まで維持する。
    /// </remarks>
    private void RestoreOriginalRotation()
    {
        if (tiltTarget == null ||
            !hasCapturedOriginalRotation ||
            !isTiltApplied)
        {
            return;
        }

        tiltTarget.localRotation =
            originalLocalRotation;

        isTiltApplied = false;
        ResetTiltRecovery();
    }
}
