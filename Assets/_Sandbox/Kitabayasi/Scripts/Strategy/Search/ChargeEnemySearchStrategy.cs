using System;
using UnityEngine;

/// <summary>
/// 突進エネミーの索敵Stateで使用する索敵Strategy。
/// </summary>
/// <remarks>
/// 責務:
/// ・ゲーム開始時の位置を基準に巡回する。
/// ・初期Facing方向へPatrolDistanceだけ移動して往復する。
/// ・巡回端で停止し、指定時間をかけて180度反転する。
/// ・XY平面上でプレイヤーとの距離を判定する。
/// ・EnemyとPlayer間のColliderによる視線遮蔽を判定する。
/// ・索敵中にPlayerへ接触した際の攻撃情報を送信する。
///
/// 担当しない責務:
/// ・Stateの保持やState遷移。
/// ・PlayerのHP変更。
/// ・設定値そのものの保持。
/// </remarks>
[Serializable]
public sealed class ChargeEnemySearchStrategy : IEnemySearchStrategy
{
    private const string PLAYER_TAG = "Player";

    private const int RAYCAST_HIT_BUFFER_SIZE = 16;

    private const float ARRIVAL_TOLERANCE = 0.05f;
    private const float DIRECTION_EPSILON = 0.001f;

    private ChargeEnemyData enemyData;

    private GameObject enemyObject;
    private Transform enemyTransform;
    private Rigidbody enemyRigidbody;
    private Collider enemyCollider;

    private Transform playerTransform;
    private Collider playerCollider;
    private IAttackHitReceiver playerAttackHitReceiver;

    private RaycastHit[] raycastHitBuffer;

    private Vector3 patrolStartPosition;

    private float initialPatrolDirectionSign;
    private float currentMoveDirectionSign;

    private bool isMovingToPatrolEnd;
    private bool isTurning;
    private bool isSearchActive;

    private float turnElapsedTime;

    private Quaternion turnStartRotation;
    private Quaternion turnTargetRotation;

    /// <summary>
    /// Strategyで使用する実行時参照を初期化する。
    /// ゲーム開始時の巡回開始位置もここで固定する。
    /// </summary>
    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject)
    {
        this.enemyData = enemyData as ChargeEnemyData
            ?? throw new InvalidOperationException(
                $"{nameof(ChargeEnemySearchStrategy)}には" +
                $"{nameof(ChargeEnemyData)}が必要です。");

        this.enemyObject = enemyObject
            ?? throw new ArgumentNullException(nameof(enemyObject));

        enemyTransform = enemyObject.transform;

        enemyRigidbody =
            enemyObject.GetComponent<Rigidbody>();

        if (enemyRigidbody == null)
        {
            throw new InvalidOperationException(
                $"{nameof(ChargeEnemySearchStrategy)}を使用するEnemyには" +
                $"{nameof(Rigidbody)}が必要です。");
        }

        if (enemyRigidbody.isKinematic)
        {
            throw new InvalidOperationException(
                $"{nameof(ChargeEnemySearchStrategy)}は" +
                "非KinematicのRigidbodyを使用する前提です。");
        }

        enemyCollider =
            enemyObject.GetComponentInChildren<Collider>();

        raycastHitBuffer =
            new RaycastHit[RAYCAST_HIT_BUFFER_SIZE];

        // 巡回開始位置はSearchStateへ入るたびではなく、
        // ゲーム開始時の位置として固定する。
        patrolStartPosition = enemyRigidbody.position;

        initialPatrolDirectionSign =
            GetInitialHorizontalDirectionSign(
                enemyTransform);

        ResolvePlayerReferences();
    }

    /// <summary>
    /// 索敵Stateを開始する。
    /// </summary>
    public void BeginSearch()
    {
        isSearchActive = true;
        isTurning = false;
        isMovingToPatrolEnd = true;

        currentMoveDirectionSign =
            initialPatrolDirectionSign;

        turnElapsedTime = 0f;

        ResolvePlayerReferences();
    }

    /// <summary>
    /// 巡回とプレイヤー検知を更新する。
    /// </summary>
    public bool UpdateSearch()
    {
        if (!isSearchActive)
        {
            return false;
        }

        if (CanDetectPlayer())
        {
            StopHorizontalMovement();

            // State遷移要求後にSearchStateが一時的に残っても、
            // 巡回処理や接触攻撃を継続しないよう停止する。
            isSearchActive = false;

            return true;
        }

        UpdatePatrol();

        return false;
    }

    /// <summary>
    /// SearchState中にPlayerへ接触した場合、
    /// 索敵中の接触ダメージ情報を送信する。
    /// </summary>
    public void HandleCollisionEnter(Collision collision)
    {
        if (!isSearchActive)
        {
            return;
        }

        if (collision == null)
        {
            return;
        }

        ResolvePlayerReferences();

        if (playerTransform == null)
        {
            return;
        }

        Collider collidedCollider = collision.collider;

        if (!IsPlayerCollider(collidedCollider))
        {
            return;
        }

        if (playerAttackHitReceiver == null)
        {
            Debug.LogWarning(
                $"{nameof(ChargeEnemySearchStrategy)}: " +
                "PlayerにIAttackHitReceiverの実装が見つかりません。",
                enemyObject);

            return;
        }

        float damage =
            enemyData.AttackPower *
            (enemyData.PatrolContactDamagePercent / 100f);

        EnemyAttackHitData attackHitData =
            new EnemyAttackHitData(
                enemyData,
                damage);

        playerAttackHitReceiver.ReceiveAttackHit(
            attackHitData);
    }

    /// <summary>
    /// 索敵Stateを終了する。
    /// </summary>
    public void EndSearch()
    {
        isSearchActive = false;
        isTurning = false;

        StopHorizontalMovement();
    }

    /// <summary>
    /// 巡回移動または反転処理を更新する。
    /// </summary>
    private void UpdatePatrol()
    {
        if (enemyData.PatrolDistance <= 0f)
        {
            StopHorizontalMovement();
            return;
        }

        if (isTurning)
        {
            UpdateTurn();
            return;
        }

        float targetPositionX =
            GetCurrentPatrolTargetPositionX();

        if (HasReachedPatrolTarget(
                targetPositionX))
        {
            BeginTurn();
            return;
        }

        float targetVelocityX =
            currentMoveDirectionSign *
            enemyData.PatrolMoveSpeed;

        SetHorizontalVelocity(
            targetVelocityX);
    }

    /// <summary>
    /// 現在向かっている巡回地点のX座標を取得する。
    /// </summary>
    private float GetCurrentPatrolTargetPositionX()
    {
        if (!isMovingToPatrolEnd)
        {
            return patrolStartPosition.x;
        }

        return patrolStartPosition.x +
               initialPatrolDirectionSign *
               enemyData.PatrolDistance;
    }

    /// <summary>
    /// 現在向かっている巡回地点へ到達したか判定する。
    /// </summary>
    private bool HasReachedPatrolTarget(
        float targetPositionX)
    {
        float remainingDistance =
            (targetPositionX -
             enemyRigidbody.position.x) *
            currentMoveDirectionSign;

        return remainingDistance <=
               ARRIVAL_TOLERANCE;
    }

    /// <summary>
    /// 巡回端での180度反転を開始する。
    /// </summary>
    private void BeginTurn()
    {
        StopHorizontalMovement();

        turnElapsedTime = 0f;

        turnStartRotation =
            enemyRigidbody.rotation;

        // X/Yゲームプレイ平面における左右反転なので、
        // 世界Y軸を中心に180度回転する。
        turnTargetRotation =
            Quaternion.AngleAxis(
                180f,
                Vector3.up) *
            turnStartRotation;

        isTurning = true;

        if (enemyData.PatrolTurnDuration <= 0f)
        {
            CompleteTurnImmediately();
        }
    }

    /// <summary>
    /// 反転処理を更新する。
    /// </summary>
    private void UpdateTurn()
    {
        StopHorizontalMovement();

        float turnDuration =
            enemyData.PatrolTurnDuration;

        if (turnDuration <= 0f)
        {
            CompleteTurnImmediately();
            return;
        }

        turnElapsedTime +=
            Time.fixedDeltaTime;

        float progress =
            Mathf.Clamp01(
                turnElapsedTime /
                turnDuration);

        Quaternion nextRotation =
            Quaternion.Slerp(
                turnStartRotation,
                turnTargetRotation,
                progress);

        enemyRigidbody.MoveRotation(
            nextRotation);

        if (progress < 1f)
        {
            return;
        }

        CompleteTurn();
    }

    /// <summary>
    /// 反転時間が0の場合に即座に反転を完了する。
    /// </summary>
    private void CompleteTurnImmediately()
    {
        enemyRigidbody.MoveRotation(
            turnTargetRotation);

        CompleteTurn();
    }

    /// <summary>
    /// 反転完了後、次の巡回地点へ向かう状態へ切り替える。
    /// </summary>
    private void CompleteTurn()
    {
        isTurning = false;

        isMovingToPatrolEnd =
            !isMovingToPatrolEnd;

        currentMoveDirectionSign *= -1f;
    }

    /// <summary>
    /// RigidbodyのY方向速度を維持しながら、
    /// X方向を指定速度へ補正する。
    /// </summary>
    private void SetHorizontalVelocity(
        float targetVelocityX)
    {
        float currentVelocityX =
            enemyRigidbody.linearVelocity.x;

        float velocityDifference =
            targetVelocityX -
            currentVelocityX;

        if (Mathf.Abs(velocityDifference) <=
            DIRECTION_EPSILON)
        {
            return;
        }

        enemyRigidbody.AddForce(
            new Vector3(
                velocityDifference,
                0f,
                0f),
            ForceMode.VelocityChange);
    }

    /// <summary>
    /// X方向の移動速度のみ停止する。
    /// 重力などによるY方向速度は維持する。
    /// </summary>
    private void StopHorizontalMovement()
    {
        if (enemyRigidbody == null)
        {
            return;
        }

        float currentVelocityX =
            enemyRigidbody.linearVelocity.x;

        if (Mathf.Abs(currentVelocityX) <=
            DIRECTION_EPSILON)
        {
            return;
        }

        enemyRigidbody.AddForce(
            new Vector3(
                -currentVelocityX,
                0f,
                0f),
            ForceMode.VelocityChange);
    }

    /// <summary>
    /// Playerが検知範囲内に存在し、
    /// かつ間に別Colliderが存在しないか判定する。
    /// </summary>
    private bool CanDetectPlayer()
    {
        ResolvePlayerReferences();

        if (playerTransform == null)
        {
            return false;
        }

        Vector3 playerPosition =
            GetPlayerRayTargetPosition();

        float deltaX =
            playerPosition.x -
            enemyRigidbody.position.x;

        float deltaY =
            playerPosition.y -
            enemyRigidbody.position.y;

        float squaredDistance =
            deltaX * deltaX +
            deltaY * deltaY;

        float detectionRadius =
            enemyData.DetectionRadius;

        if (squaredDistance >
            detectionRadius *
            detectionRadius)
        {
            return false;
        }

        return HasClearLineOfSight(
            playerPosition);
    }

    /// <summary>
    /// EnemyからPlayerまでの間に、
    /// Player以外の通常Colliderが存在しないか判定する。
    /// </summary>
    private bool HasClearLineOfSight(
        Vector3 playerPosition)
    {
        Vector3 rayOrigin =
            GetRayOrigin();

        Vector3 rayVector =
            playerPosition -
            rayOrigin;

        float rayDistance =
            rayVector.magnitude;

        if (rayDistance <=
            DIRECTION_EPSILON)
        {
            return true;
        }

        Vector3 rayDirection =
            rayVector /
            rayDistance;

        int hitCount =
            Physics.RaycastNonAlloc(
                rayOrigin,
                rayDirection,
                raycastHitBuffer,
                rayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

        Collider nearestCollider = null;

        float nearestDistance =
            float.PositiveInfinity;

        for (int i = 0;
             i < hitCount;
             i++)
        {
            RaycastHit hit =
                raycastHitBuffer[i];

            Collider hitCollider =
                hit.collider;

            if (hitCollider == null)
            {
                continue;
            }

            // Enemy自身のColliderは
            // 視線を遮る対象にしない。
            if (IsEnemyOwnedCollider(
                    hitCollider))
            {
                continue;
            }

            if (hit.distance >=
                nearestDistance)
            {
                continue;
            }

            nearestDistance =
                hit.distance;

            nearestCollider =
                hitCollider;
        }

        // Playerまでの間に何も無ければ
        // 視線が通っている。
        if (nearestCollider == null)
        {
            return true;
        }

        // 最初の有効ColliderがPlayerなら発見可能。
        // それ以外はEnemy・ステージ・オブジェクトを問わず
        // 障害物として扱う。
        return IsPlayerCollider(
            nearestCollider);
    }

    /// <summary>
    /// Rayの開始位置を取得する。
    /// Colliderが存在する場合はその中心を使用する。
    /// </summary>
    private Vector3 GetRayOrigin()
    {
        if (enemyCollider != null)
        {
            return enemyCollider.bounds.center;
        }

        return enemyRigidbody.worldCenterOfMass;
    }

    /// <summary>
    /// Player側のRay終点を取得する。
    /// </summary>
    private Vector3 GetPlayerRayTargetPosition()
    {
        if (playerCollider != null)
        {
            return playerCollider.bounds.center;
        }

        return playerTransform.position;
    }

    /// <summary>
    /// 指定ColliderがこのEnemy自身のものか判定する。
    /// </summary>
    private bool IsEnemyOwnedCollider(
        Collider targetCollider)
    {
        Transform targetTransform =
            targetCollider.transform;

        return
            targetTransform == enemyTransform ||
            targetTransform.IsChildOf(
                enemyTransform);
    }

    /// <summary>
    /// 指定ColliderがPlayerのものか判定する。
    /// </summary>
    private bool IsPlayerCollider(
        Collider targetCollider)
    {
        if (targetCollider == null ||
            playerTransform == null)
        {
            return false;
        }

        Transform targetTransform =
            targetCollider.transform;

        return
            targetTransform == playerTransform ||
            targetTransform.IsChildOf(
                playerTransform);
    }

    /// <summary>
    /// Player TagからPlayerの参照を取得する。
    /// 一度取得した後は再検索しない。
    /// </summary>
    private void ResolvePlayerReferences()
    {
        if (playerTransform != null)
        {
            return;
        }

        GameObject playerObject =
            GameObject.FindGameObjectWithTag(
                PLAYER_TAG);

        if (playerObject == null)
        {
            return;
        }

        playerTransform =
            playerObject.transform;

        ResolvePlayerCollider(
            playerObject);

        ResolvePlayerAttackHitReceiver(
            playerObject);
    }

    /// <summary>
    /// Playerの通常Colliderを取得する。
    /// </summary>
    private void ResolvePlayerCollider(
        GameObject playerObject)
    {
        Collider[] colliders =
            playerObject.GetComponentsInChildren<Collider>(
                true);

        playerCollider = null;

        for (int i = 0;
             i < colliders.Length;
             i++)
        {
            Collider collider =
                colliders[i];

            if (collider == null)
            {
                continue;
            }

            if (collider.isTrigger)
            {
                continue;
            }

            playerCollider = collider;
            return;
        }

        if (colliders.Length > 0)
        {
            playerCollider = colliders[0];
        }
    }

    /// <summary>
    /// Player階層からIAttackHitReceiverの実装を取得する。
    /// </summary>
    private void ResolvePlayerAttackHitReceiver(
        GameObject playerObject)
    {
        MonoBehaviour[] behaviours =
            playerObject.GetComponentsInChildren<MonoBehaviour>(
                true);

        playerAttackHitReceiver = null;

        for (int i = 0;
             i < behaviours.Length;
             i++)
        {
            if (behaviours[i] is
                IAttackHitReceiver receiver)
            {
                playerAttackHitReceiver =
                    receiver;

                return;
            }
        }
    }

    /// <summary>
    /// Enemyがゲーム開始時に向いている方向から、
    /// X方向の巡回符号を取得する。
    /// </summary>
    private static float GetInitialHorizontalDirectionSign(
        Transform targetTransform)
    {
        float forwardX =
            targetTransform.forward.x;

        if (Mathf.Abs(forwardX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(forwardX);
        }

        float rightX =
            targetTransform.right.x;

        if (Mathf.Abs(rightX) >
            DIRECTION_EPSILON)
        {
            return Mathf.Sign(rightX);
        }

        Debug.LogWarning(
            $"{nameof(ChargeEnemySearchStrategy)}: " +
            "Enemyの初期向きからX方向を判定できなかったため、" +
            "+X方向を使用します。",
            targetTransform);

        return 1f;
    }
}
