using System;
using UnityEngine;

/// <summary>
/// 固定射撃EnemyのAttack State中に、照準予告と1回分のProjectile連射を実行する。
/// </summary>
/// <remarks>
/// Preparationでは照準を更新し、Preparation完了後にだけ1回分のBurstを開始する。
/// Burst完了後はUpdateAttackからtrueを返し、次の攻撃可否の再索敵をStateMachineへ委譲する。
/// </remarks>
[Serializable]
public sealed class TurretEnemyAttackStrategy :
    IEnemyAttackStrategy
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";
    private const string BASE_MODEL_NAME = "turret";
    private const string SHOOTING_MODEL_NAME = "foundation";
    private const float MINIMUM_AIM_DIRECTION_SQR_MAGNITUDE = 0.000001f;
    private const float MINIMUM_HORIZONTAL_DIRECTION = 0.001f;

    [Header("射撃")]
    [Tooltip("このEnemyが使用する共通Projectile Shooter。")]
    [SerializeField]
    private EnemyProjectileShooter projectileShooter;

    [Header("射撃部分の追従")]
    [Tooltip("回転させる射撃部分を指定。このモデルではfoundationが射撃部分、turretが土台。土台やそれを含む親は指定しない。未設定ならfoundationを一意に特定した場合だけ使用する。")]
    [SerializeField]
    private Transform turretRotationTarget;

    [Tooltip("射撃部分の左右旋回と上下仰角の変化速度（度/秒）。0なら追従しない。")]
    [SerializeField, Min(0f)]
    private float rotationSpeedDegreesPerSecond = 360f;

    [Tooltip("砲身を水平から上に向けられる最大角度（度）。90度未満に制限して上下反転を防ぐ。")]
    [SerializeField, Range(0f, 89f)]
    private float maximumUpwardAngle = 75f;

    [Tooltip("砲身を水平から下に向けられる最大角度（度）。90度未満に制限して上下反転を防ぐ。")]
    [SerializeField, Range(0f, 89f)]
    private float maximumDownwardAngle = 75f;

    private TurretEnemyData enemyData;
    private GameObject enemyObject;

    private EnemyPlayerReference playerReference;
    private Collider playerTargetCollider;

    private Vector3 lastAimTargetPosition;
    private float fallbackDirectionSign;
    private float preparationElapsedTime;

    private bool isPreparationActive;
    private bool isShooting;
    private bool hasAimTarget;

    private float lastHorizontalFacingSign;
    private Quaternion initialTurretWorldRotation;
    private bool canRotateTurret;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyData =
            enemyData as TurretEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(TurretEnemyAttackStrategy)}には" +
                $"{nameof(TurretEnemyData)}が必要です。");

        this.enemyObject =
            enemyObject
            ? enemyObject
            : throw new ArgumentNullException(
                nameof(enemyObject));

        if (projectileShooter == null)
        {
            throw new InvalidOperationException(
                $"{nameof(TurretEnemyAttackStrategy)}の" +
                $"{nameof(EnemyProjectileShooter)}を設定してください。");
        }

        playerReference =
            new EnemyPlayerReference();

        playerReference.TryGetPlayerTransform(
            out _);

        projectileShooter.Initialize(
            enemyObject,
            playerReference);

        InitializeTurretRotation(visualRoot);
    }

    public void BeginPreparation(
        float initialAttackDirectionSign)
    {
        ResetRuntimeState();

        isPreparationActive = true;
        fallbackDirectionSign =
            initialAttackDirectionSign < 0f
                ? -1f
                : 1f;

        RefreshAimTarget();

        if (!hasAimTarget)
        {
            lastAimTargetPosition =
                projectileShooter.FirePointPosition +
                Vector3.right *
                fallbackDirectionSign;

            hasAimTarget = true;
        }

        projectileShooter.ShowAim(
            lastAimTargetPosition);
    }

    public bool UpdatePreparation()
    {
        if (!isPreparationActive)
        {
            return true;
        }

        RefreshAimTarget();

        if (hasAimTarget)
        {
            projectileShooter.ShowAim(
                lastAimTargetPosition);
        }

        if (enemyData.AttackPreparationDuration <= 0f)
        {
            isPreparationActive = false;
            return true;
        }

        preparationElapsedTime +=
            Time.fixedDeltaTime;

        if (preparationElapsedTime <
            enemyData.AttackPreparationDuration)
        {
            return false;
        }

        isPreparationActive = false;
        return true;
    }

    public void BeginAttack()
    {
        RefreshAimTarget();

        projectileShooter.BeginBurst(
            lastAimTargetPosition,
            enemyData,
            enemyData.AttackPower);

        isShooting =
            projectileShooter.IsBurstActive;
    }

    public bool UpdateAttack()
    {
        if (!isShooting)
        {
            return true;
        }

        if (!projectileShooter.UpdateBurst())
        {
            return false;
        }

        isShooting = false;
        return true;
    }

    public void HandleCollisionEnter(
        Collision collision)
    {
        // Turret本体の接触ダメージは持たない。
    }

    public void HandleCollisionStay(
        Collision collision)
    {
        // Turret本体の接触ダメージは持たない。
    }

    public void EndAttack()
    {
        projectileShooter.CancelBurst();
        projectileShooter.HideAim();

        ResetRuntimeState();
    }

    /// <summary>
    /// 発見後のAlert/Attack State中に、Prefab上でワールド+Zへ合わせた砲身の正面をPlayerへ向ける。
    /// 発射方向のロックや連射処理は変更しない。
    /// </summary>
    public void UpdateTurretRotation()
    {
        if (!canRotateTurret ||
            !TryGetCurrentPlayerTargetPosition(out Vector3 targetPosition))
        {
            return;
        }

        // Prefabで砲身をワールド+Zへ合わせた姿勢を正面の基準とする。
        // プレイヤーとの奥行き差は照準に含めず、XYゲームプレイ平面内で方向を求める。
        Vector3 planarDirection = targetPosition - turretRotationTarget.position;
        planarDirection.z = 0f;

        if (planarDirection.sqrMagnitude <= MINIMUM_AIM_DIRECTION_SQR_MAGNITUDE)
        {
            return;
        }

        // プレイヤーが真上・真下に来た場合は直前の左右を保つ。
        // 左右反転時もモデルの上方向が逆さにならないよう、ワールド上方向を基準とする。
        if (Mathf.Abs(planarDirection.x) > MINIMUM_HORIZONTAL_DIRECTION)
        {
            lastHorizontalFacingSign = Mathf.Sign(planarDirection.x);
        }

        float elevationAngle = Mathf.Atan2(
                planarDirection.y,
                Mathf.Abs(planarDirection.x)) *
            Mathf.Rad2Deg;

        elevationAngle = Mathf.Clamp(
            elevationAngle,
            -maximumDownwardAngle,
            maximumUpwardAngle);

        float elevationRadians = elevationAngle * Mathf.Deg2Rad;
        Vector3 limitedDirection = new Vector3(
            lastHorizontalFacingSign * Mathf.Cos(elevationRadians),
            Mathf.Sin(elevationRadians),
            0f);

        // LookRotation単体ではTransformのローカル+Zが正面として扱われ、
        // Prefabでモデル固有の正面をワールド+Zへ合わせた初期回転が失われる。
        // その初期回転を後から合成し、実際の砲身を目標方向へ向ける。
        Quaternion targetRotation =
            Quaternion.LookRotation(limitedDirection, Vector3.up) *
            initialTurretWorldRotation;

        turretRotationTarget.rotation = Quaternion.RotateTowards(
            turretRotationTarget.rotation,
            targetRotation,
            rotationSpeedDegreesPerSecond * Time.fixedDeltaTime);
    }

    private void InitializeTurretRotation(Transform visualRoot)
    {
        canRotateTurret = false;

        // Inspector指定を優先し、未指定なら射撃部分foundationを一意に特定する。
        // 土台を含む親Transformを誤って回さない。
        if (turretRotationTarget == null && visualRoot != null)
        {
            turretRotationTarget = FindTurretRotationTarget(visualRoot);
        }

        if (!IsValidTurretRotationTarget(turretRotationTarget, visualRoot))
        {
            Debug.LogWarning(
                $"{nameof(TurretEnemyAttackStrategy)}: Turret Rotation Targetには射撃部分（このモデルではfoundation）を指定してください。" +
                "VisualRoot、土台turret、土台を含む親は回転対象にできません。射撃は従来どおり続行します。",
                enemyObject);
            return;
        }

        // この時点で砲身の見た目はワールド+Zを向いている前提。
        // モデル固有のローカル軸補正を初期ワールド回転として保持する。
        // 初期姿勢はXY平面の左右方向を表さないため、真上・真下の場合は右を仮の基準にする。
        initialTurretWorldRotation = turretRotationTarget.rotation;
        lastHorizontalFacingSign = 1f;

        canRotateTurret = true;
    }

    private static bool IsValidTurretRotationTarget(
        Transform candidate,
        Transform visualRoot)
    {
        return visualRoot != null &&
               candidate != null &&
               candidate != visualRoot &&
               candidate.IsChildOf(visualRoot) &&
               !ContainsBaseModel(candidate);
    }

    private static Transform FindTurretRotationTarget(Transform visualRoot)
    {
        Transform result = null;
        Transform[] descendants = visualRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform candidate in descendants)
        {
            if (candidate == visualRoot ||
                !string.Equals(
                    candidate.name,
                    SHOOTING_MODEL_NAME,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsValidTurretRotationTarget(candidate, visualRoot))
            {
                continue;
            }

            // 複数候補がある場合は、誤った部位を動かさないため自動選択しない。
            if (result != null)
            {
                return null;
            }

            result = candidate;
        }

        return result;
    }

    private static bool ContainsBaseModel(Transform rotationTarget)
    {
        // このPrefabでは名前turretが固定土台。自分自身も含めて検証する。
        // 土台を含む親を回転させると、射撃部分以外まで一緒に動く。
        foreach (Transform child in rotationTarget.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(
                    child.name,
                    BASE_MODEL_NAME,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshAimTarget()
    {
        if (!TryGetCurrentPlayerTargetPosition(
                out Vector3 targetWorldPosition))
        {
            return;
        }

        lastAimTargetPosition =
            targetWorldPosition;

        hasAimTarget = true;
    }

    /// <summary>
    /// 既存Droneと同じPlayer代表Colliderが存在する場合はその中心を狙い、
    /// 存在しない場合はPlayer Root位置へフォールバックする。
    /// </summary>
    private bool TryGetCurrentPlayerTargetPosition(
        out Vector3 targetWorldPosition)
    {
        if (!playerReference.TryGetPlayerTransform(
                out Transform playerTransform))
        {
            playerTargetCollider = null;
            targetWorldPosition = default;
            return false;
        }

        if (playerTargetCollider == null)
        {
            Transform targetTransform =
                playerTransform.Find(
                    PLAYER_TARGET_COLLIDER_NAME);

            if (targetTransform != null)
            {
                playerTargetCollider =
                    targetTransform.GetComponent<Collider>();
            }
        }

        if (playerTargetCollider != null &&
            playerTargetCollider.enabled &&
            playerTargetCollider.gameObject.activeInHierarchy)
        {
            targetWorldPosition =
                playerTargetCollider.bounds.center;

            return true;
        }

        targetWorldPosition =
            playerTransform.position;

        return true;
    }

    private void ResetRuntimeState()
    {
        isPreparationActive = false;
        isShooting = false;
        hasAimTarget = false;

        preparationElapsedTime = 0f;
        fallbackDirectionSign = 1f;
        lastAimTargetPosition = default;
    }
}
