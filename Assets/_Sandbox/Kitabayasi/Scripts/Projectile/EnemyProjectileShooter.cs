using System;
using UnityEngine;

/// <summary>
/// Enemy共通のProjectile発射処理を担当する。
/// Projectile生成、射撃精度、ブレ角度、発射数、発射間隔、射撃前の照準線を管理する。
/// </summary>
/// <remarks>
/// Playerの索敵可否やEnemyのState遷移は担当しない。
/// 現在のゲームプレイ平面であるXY平面上で照準・ブレ角度を計算する。
/// </remarks>
public sealed class EnemyProjectileShooter : MonoBehaviour
{
    private const float DIRECTION_EPSILON = 0.001f;
    private const float MIN_AIM_LINE_LENGTH = 0.1f;

    [Header("Projectile")]
    [Tooltip("発射する共通Projectile Prefab。")]
    [SerializeField]
    private EnemyProjectile projectilePrefab;

    [Tooltip("Projectileを生成する位置。")]
    [SerializeField]
    private Transform firePoint;

    [Header("射撃")]
    [Tooltip("狙った方向へ正確に発射する確率（%）。残りの確率では最大ブレ角度内へランダムにずらす。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float accuracyPercent = 70f;

    [Tooltip("命中精度判定に失敗した弾がずれる最大角度。XYゲームプレイ平面内で±この角度までブレる。")]
    [SerializeField]
    [Range(0f, 180f)]
    private float maximumSpreadAngle = 10f;

    [Tooltip("1回の射撃サイクルで発射する弾数。")]
    [SerializeField]
    [Min(1)]
    private int shotCount = 3;

    [Tooltip("同一射撃サイクル内の弾同士の発射間隔（秒）。最初の1発は即時発射する。")]
    [SerializeField]
    [Min(0f)]
    private float shotInterval = 0.25f;

    [Header("照準表示")]
    [Tooltip("射撃精度100%時に使用する中央の照準線。")]
    [SerializeField]
    private LineRenderer centerAimLine;

    [Tooltip("射撃精度100%未満時に使用する最大ブレ範囲の上側照準線。")]
    [SerializeField]
    private LineRenderer upperSpreadAimLine;

    [Tooltip("射撃精度100%未満時に使用する最大ブレ範囲の下側照準線。")]
    [SerializeField]
    private LineRenderer lowerSpreadAimLine;

    [Header("Scene確認")]
    [Tooltip("Sceneビューで射撃方向を確認する対象。Scene上のPlayer Transformを設定する。未設定時はFire Pointの右方向を使用する。")]
    [SerializeField]
    private Transform scenePreviewTarget;

    [Tooltip("Scene Preview Target未設定時に使用するGizmoの長さ。実際のProjectile射程には影響しない。")]
    [SerializeField]
    [Min(MIN_AIM_LINE_LENGTH)]
    private float aimGizmoLength = 10f;

    private Collider[] ownerColliders;
    private EnemyPlayerReference playerReference;

    private ScriptableObject currentAttackData;
    private Vector3 lockedBaseDirection;

    private float currentDamage;
    private float shotIntervalElapsedTime;
    private int firedShotCount;

    private bool isInitialized;
    private bool isBurstActive;

    public Vector3 FirePointPosition =>
        firePoint != null
            ? firePoint.position
            : transform.position;

    public bool IsBurstActive =>
        isBurstActive;

    private void Awake()
    {
        HideAim();
    }

    private void OnDisable()
    {
        CancelBurst();
        HideAim();
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin =
            firePoint != null
                ? firePoint.position
                : transform.position;

        Vector3 referenceDirection;
        float previewLength;

        if (scenePreviewTarget != null)
        {
            referenceDirection =
                GetPlanarDirectionToTarget(
                    scenePreviewTarget.position,
                    out previewLength);
        }
        else
        {
            referenceDirection =
                GetGizmoReferenceDirection();

            previewLength =
                Mathf.Max(
                    MIN_AIM_LINE_LENGTH,
                    aimGizmoLength);
        }

        Gizmos.color = Color.yellow;

        if (accuracyPercent >= 100f ||
            maximumSpreadAngle <= 0f)
        {
            Gizmos.DrawLine(
                origin,
                origin +
                referenceDirection * previewLength);

            return;
        }

        Vector3 upperDirection =
            RotateInGameplayPlane(
                referenceDirection,
                maximumSpreadAngle);

        Vector3 lowerDirection =
            RotateInGameplayPlane(
                referenceDirection,
                -maximumSpreadAngle);

        Gizmos.DrawLine(
            origin,
            origin +
            upperDirection * previewLength);

        Gizmos.DrawLine(
            origin,
            origin +
            lowerDirection * previewLength);
    }

    /// <summary>
    /// 発射元Enemyを確定し、自身とのCollisionを無効化するためのCollider一覧を保持する。
    /// </summary>
    public void Initialize(
        GameObject ownerObject,
        EnemyPlayerReference playerReference)
    {
        if (ownerObject == null)
        {
            throw new ArgumentNullException(
                nameof(ownerObject));
        }

        this.playerReference =
            playerReference
            ?? throw new ArgumentNullException(
                nameof(playerReference));

        if (projectilePrefab == null)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyProjectileShooter)}のProjectile Prefabを設定してください。");
        }

        if (firePoint == null)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyProjectileShooter)}のFire Pointを設定してください。");
        }

        ValidateAimLineConfiguration();

        ownerColliders =
            ownerObject.GetComponentsInChildren<Collider>(
                true);

        isInitialized = true;
        HideAim();
    }

    /// <summary>
    /// 現在狙っている範囲を表示する。
    /// 100%精度では中央1本、それ未満では最大ブレ角度の上限・下限2本を表示する。
    /// </summary>
    public void ShowAim(
        Vector3 targetWorldPosition)
    {
        EnsureInitialized();

        Vector3 baseDirection =
            GetPlanarDirectionToTarget(
                targetWorldPosition,
                out float aimLineLength);

        if (accuracyPercent >= 100f)
        {
            SetAimLine(
                centerAimLine,
                baseDirection,
                aimLineLength,
                true);

            SetLineEnabled(
                upperSpreadAimLine,
                false);

            SetLineEnabled(
                lowerSpreadAimLine,
                false);

            return;
        }

        SetLineEnabled(
            centerAimLine,
            false);

        Vector3 upperDirection =
            RotateInGameplayPlane(
                baseDirection,
                maximumSpreadAngle);

        Vector3 lowerDirection =
            RotateInGameplayPlane(
                baseDirection,
                -maximumSpreadAngle);

        SetAimLine(
            upperSpreadAimLine,
            upperDirection,
            aimLineLength,
            true);

        SetAimLine(
            lowerSpreadAimLine,
            lowerDirection,
            aimLineLength,
            true);
    }

    public void HideAim()
    {
        SetLineEnabled(
            centerAimLine,
            false);

        SetLineEnabled(
            upperSpreadAimLine,
            false);

        SetLineEnabled(
            lowerSpreadAimLine,
            false);
    }

    /// <summary>
    /// 現在の照準位置をロックして1回分の連射を開始する。
    /// </summary>
    public void BeginBurst(
        Vector3 targetWorldPosition,
        ScriptableObject attackData,
        float damage)
    {
        EnsureInitialized();

        currentAttackData =
            attackData
            ? attackData
            : throw new ArgumentNullException(
                nameof(attackData));

        currentDamage =
            Mathf.Max(
                0f,
                damage);

        lockedBaseDirection =
            GetPlanarDirectionToTarget(
                targetWorldPosition,
                out _);

        firedShotCount = 0;
        shotIntervalElapsedTime = 0f;
        isBurstActive = true;

        HideAim();

        FireNextProjectile();

        if (!isBurstActive ||
            shotInterval > 0f)
        {
            return;
        }

        // 間隔0は同一タイミングで残弾をすべて発射する。
        while (isBurstActive)
        {
            FireNextProjectile();
        }
    }

    /// <summary>
    /// 進行中の連射を1物理更新分進める。
    /// </summary>
    /// <returns>今回の連射が完了している場合はtrue。</returns>
    public bool UpdateBurst()
    {
        if (!isBurstActive)
        {
            return true;
        }

        if (shotInterval <= 0f)
        {
            return true;
        }

        shotIntervalElapsedTime +=
            Time.fixedDeltaTime;

        while (isBurstActive &&
               shotIntervalElapsedTime >= shotInterval)
        {
            shotIntervalElapsedTime -=
                shotInterval;

            FireNextProjectile();
        }

        return !isBurstActive;
    }

    public void CancelBurst()
    {
        isBurstActive = false;
        firedShotCount = 0;
        shotIntervalElapsedTime = 0f;
        currentAttackData = null;
    }

    private void FireNextProjectile()
    {
        if (!isBurstActive)
        {
            return;
        }

        Vector3 shotDirection =
            GetShotDirection();

        EnemyProjectile projectile =
            Instantiate(
                projectilePrefab,
                firePoint.position,
                projectilePrefab.transform.rotation);

        projectile.Initialize(
            shotDirection,
            currentAttackData,
            currentDamage,
            playerReference,
            ownerColliders);

        firedShotCount++;

        if (firedShotCount <
            Mathf.Max(
                1,
                shotCount))
        {
            return;
        }

        isBurstActive = false;
    }

    private Vector3 GetShotDirection()
    {
        float clampedAccuracy =
            Mathf.Clamp(
                accuracyPercent,
                0f,
                100f);

        bool firesAccurately =
            UnityEngine.Random.Range(
                0f,
                100f) < clampedAccuracy;

        if (firesAccurately ||
            maximumSpreadAngle <= 0f)
        {
            return lockedBaseDirection;
        }

        float spreadAngle =
            UnityEngine.Random.Range(
                -maximumSpreadAngle,
                maximumSpreadAngle);

        return RotateInGameplayPlane(
            lockedBaseDirection,
            spreadAngle);
    }

    private Vector3 GetPlanarDirectionToTarget(
        Vector3 targetWorldPosition,
        out float distance)
    {
        Vector3 firePosition =
            FirePointPosition;

        // 現行Enemy実装と同じXYゲームプレイ平面へ固定し、
        // モデル階層のZ差で弾が奥行き方向へ逸れないようにする。
        Vector3 planarTargetPosition =
            new Vector3(
                targetWorldPosition.x,
                targetWorldPosition.y,
                firePosition.z);

        Vector3 direction =
            planarTargetPosition -
            firePosition;

        distance =
            Mathf.Max(
                MIN_AIM_LINE_LENGTH,
                direction.magnitude);

        if (direction.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return Vector3.right;
        }

        return direction.normalized;
    }

    private Vector3 GetGizmoReferenceDirection()
    {
        Vector3 sourceDirection =
            firePoint != null
                ? firePoint.right
                : transform.right;

        // 実際の射撃と同じXYゲームプレイ平面へ投影し、
        // Scene確認用Gizmoだけが奥行き方向へ傾かないようにする。
        Vector3 planarDirection =
            new Vector3(
                sourceDirection.x,
                sourceDirection.y,
                0f);

        if (planarDirection.sqrMagnitude <=
            DIRECTION_EPSILON * DIRECTION_EPSILON)
        {
            return Vector3.right;
        }

        return planarDirection.normalized;
    }

    private static Vector3 RotateInGameplayPlane(
        Vector3 direction,
        float angleDegrees)
    {
        return Quaternion.AngleAxis(
                angleDegrees,
                Vector3.forward) *
            direction;
    }

    private void SetAimLine(
        LineRenderer lineRenderer,
        Vector3 direction,
        float lineLength,
        bool enabled)
    {
        if (lineRenderer == null)
        {
            return;
        }

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(
            0,
            FirePointPosition);
        lineRenderer.SetPosition(
            1,
            FirePointPosition +
            direction.normalized *
            lineLength);
        lineRenderer.enabled = enabled;
    }

    private static void SetLineEnabled(
        LineRenderer lineRenderer,
        bool enabled)
    {
        if (lineRenderer != null)
        {
            lineRenderer.enabled = enabled;
        }
    }

    private void ValidateAimLineConfiguration()
    {
        if (accuracyPercent >= 100f)
        {
            if (centerAimLine == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(EnemyProjectileShooter)}: " +
                    "Accuracyが100%の場合はCenter Aim Lineを設定してください。");
            }

            return;
        }

        if (upperSpreadAimLine == null ||
            lowerSpreadAimLine == null)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyProjectileShooter)}: " +
                "Accuracyが100%未満の場合はUpper / Lower Spread Aim Lineを設定してください。");
        }
    }

    private void EnsureInitialized()
    {
        if (!isInitialized)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyProjectileShooter)}をInitializeしてから使用してください。");
        }
    }
}
