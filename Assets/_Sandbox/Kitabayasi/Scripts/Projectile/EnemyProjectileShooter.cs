using System;
using UnityEngine;

/// <summary>
/// Enemy共通のProjectile発射処理を担当する。
/// Projectile生成、射撃精度、ブレ角度、発射数、発射間隔、攻撃予告表示を管理する。
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

    [Header("旧照準表示（ゲーム中は使用しない）")]
    [Tooltip("旧照準用。互換性のため参照を保持し、ゲーム中は常に非表示にする。")]
    [SerializeField]
    private LineRenderer centerAimLine;

    [Tooltip("旧照準用。ゲーム中は表示せず、Scene Gizmoで拡散範囲を確認する。")]
    [SerializeField]
    private LineRenderer upperSpreadAimLine;

    [Tooltip("旧照準用。ゲーム中は表示せず、Scene Gizmoで拡散範囲を確認する。")]
    [SerializeField]
    private LineRenderer lowerSpreadAimLine;

    [Header("攻撃予告")]
    [Tooltip("予備動作中だけ表示するLineRenderer。旧照準線とは別のものを指定する。")]
    [SerializeField]
    private LineRenderer attackWarningLine;

    [Tooltip("攻撃予告線の色。LineRendererのMaterialは頂点カラーに対応するものを使用する。")]
    [SerializeField]
    private Color attackWarningColor = Color.red;

    [Tooltip("攻撃予告線の太さ（ワールド単位）。")]
    [SerializeField, Min(0f)]
    private float attackWarningWidth = 0.035f;

    [Tooltip("攻撃予告線の点滅回数（1秒あたり）。0なら点灯したままにする。")]
    [SerializeField, Min(0f)]
    private float attackWarningBlinkFrequency = 4f;

    [Header("Scene確認")]
    [Tooltip("Sceneビューで射撃方向を確認する対象。Scene上のPlayer Transformを設定する。未設定時はFire Pointの右方向を使用する。")]
    [SerializeField]
    private Transform scenePreviewTarget;

    [Tooltip("Scene Preview Target未設定時に使用するGizmoの長さ。実際のProjectile射程には影響しない。")]
    [SerializeField]
    [Min(MIN_AIM_LINE_LENGTH)]
    private float aimGizmoLength = 10f;

    private Collider[] ownerColliders;
    private Collider[] projectilePrefabColliders;
    private EnemyPlayerReference playerReference;

    private ScriptableObject currentAttackData;
    private Vector3 lockedBaseDirection;

    private float currentDamage;
    private float shotIntervalElapsedTime;
    private int firedShotCount;

    private bool isInitialized;
    private bool isBurstActive;
    private bool isAttackWarningActive;
    private bool hasRuntimeAimTarget;
    private float attackWarningStartTime;
    private Vector3 runtimeAimTargetPosition;
    private Vector3 attackWarningTargetPosition;
    private Collider attackWarningTargetCollider;

    public Vector3 FirePointPosition =>
        firePoint != null
            ? firePoint.position
            : transform.position;

    public bool IsBurstActive =>
        isBurstActive;

    public bool HasAttackWarningLine => attackWarningLine != null;

    public Transform FirePointTransform => firePoint;

    // 発射時の弾Collider形状を射撃地点判定へ渡す。発射仕様自体は変更しない。
    public Transform ProjectilePrefabRoot => projectilePrefab != null ? projectilePrefab.transform : null;
    public Collider[] ProjectilePrefabColliders => projectilePrefabColliders;

    public LayerMask ProjectileStageCollisionMask =>
        projectilePrefab != null ? projectilePrefab.StageCollisionMask : default;

    private void Awake()
    {
        HideAim();
        HideAttackWarning();
    }

    private void OnDisable()
    {
        CancelBurst();
        HideAim();
        HideAttackWarning();
    }

    private void LateUpdate()
    {
        // Playerと銃口の表示位置は描画フレームごとに更新し、物理更新間のカクつきを減らす。
        if (isAttackWarningActive)
        {
            RefreshAttackWarningLine();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Gizmosを有効にしたGameビューでも旧紫線が出ないよう、Scene描画時だけ実行する。
        if (UnityEditor.SceneView.currentDrawingSceneView == null)
        {
            return;
        }

        Vector3 origin =
            firePoint != null
                ? firePoint.position
                : transform.position;

        Vector3 referenceDirection;
        float previewLength;

        if (hasRuntimeAimTarget && Application.isPlaying)
        {
            referenceDirection =
                GetPlanarDirectionToTarget(
                    runtimeAimTargetPosition,
                    out previewLength);
        }
        else if (scenePreviewTarget != null)
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

        Gizmos.color = new Color(0.65f, 0.15f, 1f, 1f);

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
#endif

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

        ownerColliders =
            ownerObject.GetComponentsInChildren<Collider>(
                true);

        // A*探索中の各候補でGetComponentsInChildrenを呼び直さない。
        projectilePrefabColliders =
            projectilePrefab.GetComponentsInChildren<Collider>(true);

        isInitialized = true;
        HideAim();
        HideAttackWarning();
    }

    /// <summary>
    /// Sceneビュー用に照準位置を保持する。旧LineRendererはゲーム中に描画しない。
    /// </summary>
    public void ShowAim(Vector3 targetWorldPosition)
    {
        EnsureInitialized();
        runtimeAimTargetPosition = targetWorldPosition;
        hasRuntimeAimTarget = true;
        DisableLegacyAimLines();
    }

    public void HideAim()
    {
        hasRuntimeAimTarget = false;
        DisableLegacyAimLines();
    }

    /// <summary>
    /// 予備動作中の警告線を更新する。射撃精度や発射方向には使用しない。
    /// </summary>
    public void ShowAttackWarning(
        Vector3 targetWorldPosition,
        Collider targetCollider = null)
    {
        EnsureInitialized();

        if (attackWarningLine == null)
        {
            return;
        }

        if (!isAttackWarningActive)
        {
            isAttackWarningActive = true;
            attackWarningStartTime = Time.time;
        }

        attackWarningTargetPosition = targetWorldPosition;
        attackWarningTargetCollider = targetCollider;

        attackWarningLine.useWorldSpace = true;
        attackWarningLine.positionCount = 2;
        attackWarningLine.startColor = attackWarningColor;
        attackWarningLine.endColor = attackWarningColor;
        attackWarningLine.startWidth = attackWarningWidth;
        attackWarningLine.endWidth = attackWarningWidth;

        RefreshAttackWarningLine();
    }

    public void HideAttackWarning()
    {
        isAttackWarningActive = false;
        attackWarningTargetCollider = null;
        SetLineEnabled(attackWarningLine, false);
    }

    private void RefreshAttackWarningLine()
    {
        if (attackWarningLine == null)
        {
            return;
        }

        Vector3 origin = FirePointPosition;
        Vector3 target = attackWarningTargetPosition;

        if (attackWarningTargetCollider != null &&
            attackWarningTargetCollider.enabled &&
            attackWarningTargetCollider.gameObject.activeInHierarchy)
        {
            target = attackWarningTargetCollider.bounds.center;
        }

        // 弾と同じXYゲームプレイ平面上で描画する。照準計算への反映はしない。
        target.z = origin.z;
        attackWarningLine.SetPosition(0, origin);
        attackWarningLine.SetPosition(1, target);

        float elapsedTime = Mathf.Max(0f, Time.time - attackWarningStartTime);
        bool isLit = attackWarningBlinkFrequency <= 0f ||
            Mathf.Repeat(elapsedTime * attackWarningBlinkFrequency, 1f) < 0.5f;
        attackWarningLine.enabled = isLit;
    }

    private void DisableLegacyAimLines()
    {
        SetLineEnabled(centerAimLine, false);
        SetLineEnabled(upperSpreadAimLine, false);
        SetLineEnabled(lowerSpreadAimLine, false);
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
        HideAttackWarning();

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

    private static void SetLineEnabled(
        LineRenderer lineRenderer,
        bool enabled)
    {
        if (lineRenderer != null)
        {
            lineRenderer.enabled = enabled;
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
