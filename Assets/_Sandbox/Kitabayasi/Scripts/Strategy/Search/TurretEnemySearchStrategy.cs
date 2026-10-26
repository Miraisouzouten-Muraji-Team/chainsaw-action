using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 固定射撃Enemyの索敵を担当する。
/// 移動は行わず、個体ごとの索敵半径と既存のLOS判定でPlayerを検知する。
/// </summary>
/// <remarks>
/// Search State中の通常索敵だけでなく、1回の射撃完了後に同じ条件で
/// Playerがまだ攻撃対象か再確認するためにもTryDetectPlayerを使用する。
/// </remarks>
[Serializable]
public sealed class TurretEnemySearchStrategy :
    IEnemySearchStrategy,
    IEnemyDebugRayProvider
{
    private const string PLAYER_TARGET_COLLIDER_NAME = "Mid_Collider";
    private const float DIRECTION_EPSILON = 0.001f;

    [Header("個体設定")]
    [Tooltip("このTurretEnemyがPlayerを検知する半径。EnemyDataのDetectionRadiusは使用しない。")]
    [SerializeField]
    [Min(0f)]
    private float detectionRadius = 10f;

    private GameObject enemyObject;
    private Transform enemyTransform;
    private Collider enemyCollider;

    private EnemyPlayerReference playerReference;
    private EnemyPlayerDetectionSensor playerDetectionSensor;
    private Collider playerTargetCollider;

    private bool isSearchActive;

    /// <summary>
    /// このTurretEnemy個体に設定された索敵半径。
    /// </summary>
    public float DetectionRadius => detectionRadius;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot)
    {
        this.enemyObject =
            enemyObject
            ? enemyObject
            : throw new ArgumentNullException(
                nameof(enemyObject));

        enemyTransform =
            enemyObject.transform;

        enemyCollider =
            enemyObject.GetComponentInChildren<Collider>();

        if (enemyCollider == null)
        {
            throw new InvalidOperationException(
                $"{nameof(TurretEnemySearchStrategy)}を使用するEnemyには" +
                $"{nameof(Collider)}が必要です。");
        }

        playerReference =
            new EnemyPlayerReference();

        playerDetectionSensor =
            new EnemyPlayerDetectionSensor(
                enemyTransform,
                playerReference);

        playerReference.TryGetPlayerTransform(
            out _);
    }

    public void BeginSearch()
    {
        isSearchActive = true;
        playerDetectionSensor.ClearDebugRay();
        playerReference.TryGetPlayerTransform(
            out _);
    }

    public bool UpdateSearch(
        out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;

        if (!isSearchActive ||
            !TryDetectPlayer(
                out detectedPlayerDirectionSign))
        {
            return false;
        }

        // State遷移要求後にSearchが一時的に残っても、
        // 同じ発見通知を繰り返さない。
        isSearchActive = false;
        return true;
    }

    public bool TryDetectPlayer(
        out float detectedPlayerDirectionSign)
    {
        detectedPlayerDirectionSign = 0f;

        if (!TryGetCurrentPlayerTargetPosition(
                out Vector3 playerTargetPosition))
        {
            playerDetectionSensor.ClearDebugRay();
            return false;
        }

        Vector3 searchOrigin =
            GetSearchOrigin();

        if (!playerDetectionSensor.CanDetectPlayer(
                searchOrigin,
                playerTargetPosition,
                detectionRadius,
                playerTargetPosition,
                playerColliderEndsLineOfSight: true))
        {
            return false;
        }

        float deltaX =
            playerTargetPosition.x -
            searchOrigin.x;

        detectedPlayerDirectionSign =
            Mathf.Abs(deltaX) > DIRECTION_EPSILON
                ? Mathf.Sign(deltaX)
                : 1f;

        return true;
    }

    public void HandleCollisionEnter(
        Collision collision)
    {
        // TurretEnemyは固定砲台であり、Search中の接触攻撃を持たない。
    }

    public void EndSearch()
    {
        isSearchActive = false;
        playerDetectionSensor.ClearDebugRay();
    }

    public void CollectDebugRays(
        List<EnemyDebugRay> debugRays)
    {
        if (debugRays == null)
        {
            throw new ArgumentNullException(
                nameof(debugRays));
        }

        if (playerDetectionSensor.TryGetDebugRay(
                out EnemyDebugRay detectionDebugRay))
        {
            debugRays.Add(
                detectionDebugRay);
        }
    }

    private Vector3 GetSearchOrigin()
    {
        return enemyCollider.bounds.center;
    }

    /// <summary>
    /// 既存Droneと同じPlayer代表Colliderが存在する場合はその中心を使用し、
    /// 存在しない場合だけPlayer Root位置へフォールバックする。
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
}
