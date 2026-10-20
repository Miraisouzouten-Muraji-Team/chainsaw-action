// 責務:
// ・ドローンエネミー固有の調整値を保持する。
// ・Searchで使用する巡回設定と、Attackで使用するホーミング・ブレーキ・後退設定を保持する。
//
// 担当しない責務:
// ・巡回地点A/Bなど、Enemy個体ごとの配置情報。
// ・現在位置、現在State、ロック位置、現在の攻撃方向などの実行時状態。

using UnityEngine;

[CreateAssetMenu(
    fileName = "DroneEnemyData",
    menuName = "Enemy/Drone Enemy Data")]
public sealed class DroneEnemyData : EnemyData
{
    [Header("索敵")]
    [Tooltip("巡回時の移動速度。")]
    [SerializeField]
    [Min(0f)]
    private float patrolMoveSpeed = 5f;

    [Tooltip("巡回端での振り返り時間。")]
    [SerializeField]
    [Min(0f)]
    private float patrolTurnDuration = 0.5f;

    [Tooltip("巡回中の接触ダメージ割合（%）。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float patrolContactDamagePercent = 50f;

    [Header("経路探索")]
    [Tooltip("A*経路追従時の移動速度。")]
    [SerializeField]
    [Min(0f)]
    private float navigationMoveSpeed = 5f;

    [Tooltip("実BoxCollider相当の通行判定Boxに加える経路探索用の安全余白。")]
    [SerializeField]
    [Min(0f)]
    private float pathSafetyMargin = 0.05f;

    [Tooltip("StartとGoalを囲む探索矩形へ追加する余白。")]
    [SerializeField]
    [Min(0f)]
    private float pathSearchPadding = 5f;

    [Tooltip("1回のA*探索で展開するNode数の上限。")]
    [SerializeField]
    [Min(1)]
    private int maximumExpandedPathNodes = 4096;

    [Tooltip("経路Waypointへ到着したとみなすXY距離。")]
    [SerializeField]
    [Min(0f)]
    private float waypointArrivalTolerance = 0.1f;

    [Tooltip("Attack中にPlayer移動を理由としてA*を再探索できる最短間隔。")]
    [SerializeField]
    [Min(0f)]
    private float attackRepathInterval = 0.25f;

    [Tooltip("前回探索時のPlayer位置からこの距離以上動いた場合、再探索候補にする。")]
    [SerializeField]
    [Min(0f)]
    private float attackRepathDistance = 0.75f;

    [Tooltip("A*経路追従中、この速度未満の実移動を進行不能として扱う。0で無効化。")]
    [SerializeField]
    [Min(0f)]
    private float navigationStallMinimumMoveSpeed = 0.1f;

    [Tooltip("A*経路追従中の進行不能が続いた場合、経路を破棄するまでの時間。0で無効化。")]
    [SerializeField]
    [Min(0f)]
    private float navigationStallDetectionDuration = 0.5f;

    [Tooltip("A*と経路通行判定で障害物として扱うLayer。暫定ではPixelNear、最終的にはStageColliderを想定。")]
    [SerializeField]
    private LayerMask navigationObstacleMask = 1 << 9;

    [Header("攻撃")]
    [Tooltip("ホーミング突進時の基本移動速度。")]
    [SerializeField]
    [Min(0f)]
    private float attackMoveSpeed = 15f;

    [Tooltip("基準攻撃方向から現在Player方向へ寄せる割合（%）。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float homingStrength = 50f;

    [Tooltip("ホーミング時の最大旋回速度（度/秒）。")]
    [SerializeField]
    [Min(0f)]
    private float maximumTurnSpeed = 180f;

    [Tooltip("突進中、この速度未満の実移動を進行不能として扱う。0で時間判定を無効化。")]
    [SerializeField]
    [Min(0f)]
    private float attackStallMinimumMoveSpeed = 0.1f;

    [Tooltip("進行不能が続いた場合、突進失敗とみなすまでの時間。0で時間判定を無効化。")]
    [SerializeField]
    [Min(0f)]
    private float attackStallDetectionDuration = 0.25f;

    [Tooltip("Attack中にPlayerへ接触した際のダメージ割合（%）。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float attackContactDamagePercent = 100f;

    [Tooltip("初回突進前に停止して待機する時間。")]
    [SerializeField]
    [Min(0f)]
    private float initialPreparationDuration = 0.75f;

    [Tooltip("ロック地点の判定ライン手前を到達扱いにする距離。")]
    [SerializeField]
    [Min(0f)]
    private float homingEndThreshold = 0.25f;

    [Tooltip("ホーミング終了後の減速度。")]
    [SerializeField]
    [Min(0f)]
    private float brakingDeceleration = 60f;

    [Tooltip("急ブレーキ完了後、その場で停止する時間。")]
    [SerializeField]
    [Min(0f)]
    private float brakePauseDuration = 0.15f;

    [Tooltip("次回突進前に後退する最大速度。")]
    [SerializeField]
    [Min(0f)]
    private float retreatSpeed = 5f;

    [Tooltip("後退の開始・終了を滑らかにする時間。0でイージングなし。")]
    [SerializeField]
    [Min(0f)]
    private float retreatEaseDuration = 0.1f;

    [Tooltip("次回突進前に後退する距離。")]
    [SerializeField]
    [Min(0f)]
    private float retreatDistance = 1.5f;

    public float PatrolMoveSpeed =>
        patrolMoveSpeed;

    public float PatrolTurnDuration =>
        patrolTurnDuration;

    public float PatrolContactDamagePercent =>
        patrolContactDamagePercent;

    public float NavigationMoveSpeed =>
        navigationMoveSpeed;

    public float PathSafetyMargin =>
        pathSafetyMargin;

    public float PathSearchPadding =>
        pathSearchPadding;

    public int MaximumExpandedPathNodes =>
        maximumExpandedPathNodes;

    public float WaypointArrivalTolerance =>
        waypointArrivalTolerance;

    public float AttackRepathInterval =>
        attackRepathInterval;

    public float AttackRepathDistance =>
        attackRepathDistance;

    public float NavigationStallMinimumMoveSpeed =>
        navigationStallMinimumMoveSpeed;

    public float NavigationStallDetectionDuration =>
        navigationStallDetectionDuration;

    public LayerMask NavigationObstacleMask =>
        navigationObstacleMask;

    public float AttackMoveSpeed =>
        attackMoveSpeed;

    public float HomingStrength =>
        homingStrength;

    public float MaximumTurnSpeed =>
        maximumTurnSpeed;

    public float AttackStallMinimumMoveSpeed =>
        attackStallMinimumMoveSpeed;

    public float AttackStallDetectionDuration =>
        attackStallDetectionDuration;

    public float AttackContactDamagePercent =>
        attackContactDamagePercent;

    public float InitialPreparationDuration =>
        initialPreparationDuration;

    public float HomingEndThreshold =>
        homingEndThreshold;

    public float BrakingDeceleration =>
        brakingDeceleration;

    public float BrakePauseDuration =>
        brakePauseDuration;

    public float RetreatSpeed =>
        retreatSpeed;

    public float RetreatEaseDuration =>
        retreatEaseDuration;

    public float RetreatDistance =>
        retreatDistance;
}
