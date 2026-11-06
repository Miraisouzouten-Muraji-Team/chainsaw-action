using UnityEngine;

/// <summary>
/// 新型ドローンの巡回・帰還・射撃位置探索・射撃追従に固有の調整値を保持する。
/// </summary>
/// <remarks>
/// HP、攻撃力、Alert、被弾、死亡、攻撃後待機はEnemyDataに委譲する。
/// 索敵半径と巡回地点は個体ごとのSearch Strategy、弾数・精度・間隔はShooterで設定する。
/// 現在位置や攻撃中の追従オフセットなどの実行時状態は保持しない。
/// </remarks>
[CreateAssetMenu(
    fileName = "NewDroneEnemyData",
    menuName = "Enemy/New Drone Enemy Data")]
public sealed class NewDroneEnemyData : EnemyData
{
    [Header("巡回")]
    [SerializeField, Min(0f)]
    [Tooltip("2点間の巡回速度。")]
    private float patrolMoveSpeed = 5f;

    [SerializeField, Min(0f)]
    [Tooltip("巡回端での反転にかける時間。")]
    private float patrolTurnDuration = 0.5f;

    [Header("初期位置への帰還")]
    [SerializeField, Min(0f)]
    private float navigationMoveSpeed = 5f;

    [SerializeField, Min(0f)]
    private float pathSafetyMargin = 0.05f;

    [SerializeField, Min(0f)]
    private float pathSearchPadding = 5f;

    [SerializeField, Min(1)]
    private int maximumExpandedPathNodes = 4096;

    [SerializeField, Min(0f)]
    private float waypointArrivalTolerance = 0.1f;

    [SerializeField, Min(0f)]
    private float navigationStallMinimumMoveSpeed = 0.1f;

    [SerializeField, Min(0f)]
    private float navigationStallDetectionDuration = 0.5f;

    [Tooltip("巡回・帰還・射撃追従で共通使用するStage障害物のLayer。必ず設定する。")]
    [SerializeField]
    private LayerMask navigationObstacleMask;

    [Header("射撃")]
    [SerializeField, Min(0f)]
    [Tooltip("移動を止め、Playerへ照準を更新する予備動作時間。")]
    private float attackPreparationDuration = 0.75f;

    [SerializeField, Min(0f)]
    [Tooltip("射撃中にPlayerとの相対位置を維持しようとする最大移動速度。")]
    private float shootingFollowSpeed = 4f;

    public float PatrolMoveSpeed => patrolMoveSpeed;
    public float PatrolTurnDuration => patrolTurnDuration;
    public float NavigationMoveSpeed => navigationMoveSpeed;
    public float PathSafetyMargin => pathSafetyMargin;
    public float PathSearchPadding => pathSearchPadding;
    public int MaximumExpandedPathNodes => maximumExpandedPathNodes;
    public float WaypointArrivalTolerance => waypointArrivalTolerance;
    public float NavigationStallMinimumMoveSpeed => navigationStallMinimumMoveSpeed;
    public float NavigationStallDetectionDuration => navigationStallDetectionDuration;
    public LayerMask NavigationObstacleMask => navigationObstacleMask;
    public float AttackPreparationDuration => attackPreparationDuration;
    [SerializeField, Min(0f)]
    [Tooltip("射撃可能地点を見つけられない状態で再探索を続ける最大秒数。0なら最初の失敗で帰還する。")]
    private float shootingPositionSearchTimeout = 3f;

    public float ShootingFollowSpeed => shootingFollowSpeed;
    public float ShootingPositionSearchTimeout => shootingPositionSearchTimeout;
}
