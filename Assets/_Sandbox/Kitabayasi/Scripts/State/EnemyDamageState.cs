using System;
using UnityEngine;

/// <summary>
/// Enemyが攻撃を受けた後のやられ状態を管理する。
/// </summary>
/// <remarks>
/// 責務:
/// ・やられState開始時にダメージ点滅を開始する。
/// ・やられ中、見た目用TransformをZ軸方向へ傾ける。
/// ・指定時間の経過を管理する。
/// ・やられ終了後にSearch Stateへの遷移を要求する。
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
    private readonly Action requestSearchState;

    private float elapsedTime;

    private Quaternion originalLocalRotation;

    private bool hasCapturedOriginalRotation;
    private bool hasRequestedSearchState;

    public EnemyDamageState(
        EnemyDamageFlash enemyDamageFlash,
        Transform tiltTarget,
        float tiltDuration,
        float tiltAngle,
        Action requestSearchState)
    {
        this.enemyDamageFlash = enemyDamageFlash
            ?? throw new ArgumentNullException(
                nameof(enemyDamageFlash));

        this.tiltTarget = tiltTarget;

        this.tiltDuration =
            Mathf.Max(0f, tiltDuration);

        this.tiltAngle = tiltAngle;

        this.requestSearchState = requestSearchState
            ?? throw new ArgumentNullException(
                nameof(requestSearchState));
    }

    public void Enter()
    {
        elapsedTime = 0f;
        hasRequestedSearchState = false;

        CaptureOriginalRotation();
        ApplyTilt();

        enemyDamageFlash.PlayDamageFlash();
    }

    public void Update()
    {
        if (hasRequestedSearchState)
        {
            return;
        }

        elapsedTime += Time.deltaTime;

        if (elapsedTime < tiltDuration)
        {
            return;
        }

        hasRequestedSearchState = true;

        requestSearchState.Invoke();
    }

    public void Exit()
    {
        RestoreOriginalRotation();

        elapsedTime = 0f;
        hasRequestedSearchState = false;
    }

    /// <summary>
    /// やられState中に再度攻撃を受けた場合、
    /// やられ時間と点滅演出を最初からやり直す。
    /// </summary>
    public void Restart()
    {
        elapsedTime = 0f;
        hasRequestedSearchState = false;

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
    /// X/Yゲームプレイ平面上で身体を傾けるため、
    /// 見た目用TransformをローカルZ軸周りに回転させる。
    /// </summary>
    private void ApplyTilt()
    {
        if (tiltTarget == null ||
            !hasCapturedOriginalRotation)
        {
            return;
        }

        Quaternion tiltRotation =
            Quaternion.AngleAxis(
                tiltAngle,
                Vector3.forward);

        tiltTarget.localRotation =
            originalLocalRotation *
            tiltRotation;
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
