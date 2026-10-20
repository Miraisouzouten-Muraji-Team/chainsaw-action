using System;
using UnityEngine;

/// <summary>
/// Enemyが攻撃を受けた後のやられ状態を管理する。
/// </summary>
/// <remarks>
/// 責務:
/// ・やられState開始時にダメージ点滅を開始する。
/// ・やられ中、攻撃を受けた側とは反対方向へ見た目用Transformを傾ける。
/// ・指定時間の経過を管理する。
/// ・やられ終了後に次のStateを決めるための遷移判定を要求する。
///
/// 担当しない責務:
/// ・HPの保持やダメージ計算。
/// ・Materialの具体的な切り替え処理。
/// ・実際のState切り替え。
/// ・Collisionによる接触ダメージ処理。
///
/// Collision受信interfaceを実装しないことで、
/// このState中はStateMachineからCollision通知を受け取らない。
/// </remarks>
public sealed class EnemyDamageState : IEnemyState
{
    private readonly EnemyDamageFlash enemyDamageFlash;
    private readonly Transform tiltTarget;
    private readonly float tiltDuration;
    private readonly float tiltAngle;
    private readonly Action requestDamageRecovery;

    private float elapsedTime;
    private float hitSourceDirectionSign = 1f;

    private Quaternion originalLocalRotation;

    private bool hasCapturedOriginalRotation;
    private bool hasRequestedDamageRecovery;

    public EnemyDamageState(
        EnemyDamageFlash enemyDamageFlash,
        Transform tiltTarget,
        float tiltDuration,
        float tiltAngle,
        Action requestDamageRecovery)
    {
        this.enemyDamageFlash = enemyDamageFlash
            ?? throw new ArgumentNullException(
                nameof(enemyDamageFlash));

        this.tiltTarget = tiltTarget;

        this.tiltDuration =
            Mathf.Max(0f, tiltDuration);

        this.tiltAngle = Mathf.Abs(tiltAngle);

        this.requestDamageRecovery = requestDamageRecovery
            ?? throw new ArgumentNullException(
                nameof(requestDamageRecovery));
    }

    public void Enter()
    {
        elapsedTime = 0f;
        hasRequestedDamageRecovery = false;

        CaptureOriginalRotation();
        ApplyTilt();

        enemyDamageFlash.PlayDamageFlash();
    }

    public void Update()
    {
        if (hasRequestedDamageRecovery)
        {
            return;
        }

        elapsedTime += Time.deltaTime;

        if (elapsedTime < tiltDuration)
        {
            return;
        }

        hasRequestedDamageRecovery = true;

        requestDamageRecovery.Invoke();
    }

    public void Exit()
    {
        RestoreOriginalRotation();

        elapsedTime = 0f;
        hasRequestedDamageRecovery = false;
    }

    /// <summary>
    /// 今回の攻撃がEnemyの左右どちら側から来たかを設定する。
    /// </summary>
    /// <remarks>
    /// X/Yゲームプレイ平面を前提とし、
    /// -1はEnemyの左側、+1は右側からの攻撃を表す。
    /// 0は方向不明として従来と同じ+1へ補正する。
    /// </remarks>
    public void SetHitSourceDirectionSign(
        float directionSign)
    {
        hitSourceDirectionSign =
            directionSign < 0f
                ? -1f
                : 1f;
    }

    /// <summary>
    /// やられState中に再度攻撃を受けた場合、
    /// やられ時間と点滅演出を最初からやり直す。
    /// </summary>
    public void Restart()
    {
        elapsedTime = 0f;
        hasRequestedDamageRecovery = false;

        ApplyTilt();

        enemyDamageFlash.PlayDamageFlash();
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
    }

    /// <summary>
    /// やられ開始前の見た目へ戻す。
    /// </summary>
    private void RestoreOriginalRotation()
    {
        if (tiltTarget == null ||
            !hasCapturedOriginalRotation)
        {
            return;
        }

        tiltTarget.localRotation =
            originalLocalRotation;

        hasCapturedOriginalRotation = false;
    }
}
