using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 突進エネミーの各種設定値を保持するScriptableObject。
/// </summary>
/// <remarks>
/// 索敵・攻撃Stateで使用する速度・時間・ダメージ割合、
/// 平坦面移動制限など、突進エネミー固有の調整値を保持する。
///
/// 最大HP、攻撃力、共通の攻撃予告、被ダメージ演出、死亡演出など、
/// 全Enemy共通の設定値はEnemyDataから継承する。
/// 索敵半径と巡回距離はChargeEnemySearchStrategyの個体設定を使用する。
///
/// 現在HP、現在State、現在位置など、
/// 実行中に変化する状態は保持しない。
/// </remarks>
[CreateAssetMenu(
    fileName = "ChargeEnemyData",
    menuName = "Enemy/Charge Enemy Data")]
public sealed class ChargeEnemyData : EnemyData
{
    [Header("索敵")]
    [Tooltip("互換性のため保持している旧巡回距離。現在のChargeEnemySearchStrategyでは使用せず、個体側のPatrol Distanceを使用します。")]
    [SerializeField]
    [Min(0f)]
    private float patrolDistance;

    [Tooltip("巡回時の移動速度。")]
    [SerializeField]
    [Min(0f)]
    private float patrolMoveSpeed = 3f;

    [Tooltip("巡回端での振り返り時間。")]
    [SerializeField]
    [Min(0f)]
    private float patrolTurnDuration = 0.5f;

    [Tooltip("巡回中の接触ダメージ割合（%）。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float patrolContactDamagePercent = 50f;

    [Header("攻撃")]
    [Tooltip("突進開始前に停止して待機する予備動作時間。")]
    [SerializeField]
    [Min(0f)]
    private float attackPreparationDuration = 0.75f;

    [Tooltip("予備動作モーションの再生速度。1で通常速度、0で停止します。予備動作時間には影響しません。")]
    [SerializeField]
    [Min(0f)]
    private float preparationAnimationSpeed = 1f;

    [Tooltip("ChargeEnemy個体のCharge Distanceへ最初にコピーする初期値。個体側を初期化した後は、その個体の値が使用されます。")]
    [SerializeField]
    [Min(0f)]
    private float chargeDistance = float.MaxValue;

    [Tooltip("突進時の移動速度。")]
    [SerializeField]
    [Min(0f)]
    private float attackMoveSpeed = 15f;

    [Tooltip("突進中の接触ダメージ割合（%）。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float attackContactDamagePercent = 100f;

    [Header("Stage判定")]
    [Tooltip("Ground判定と突進中の壁判定でStage Geometryとして扱うLayer。Projectの現在のStage Layerへ設定してください。")]
    [SerializeField]
    private LayerMask stageLayerMask = Physics.DefaultRaycastLayers;

    [Header("平坦面移動制限")]
    [Tooltip("地面判定をCollider前端より先へ出す最低距離。")]
    [FormerlySerializedAs("attackCliffCheckForwardOffset")]
    [SerializeField]
    [Min(0f)]
    private float groundCheckForwardOffset = 0.1f;

    [Tooltip("地面判定Rayの下方向距離。")]
    [FormerlySerializedAs("attackCliffCheckDownDistance")]
    [SerializeField]
    [Min(0f)]
    private float groundCheckDownDistance = 0.25f;

    [Tooltip("移動可能な地面の最大高さ差。")]
    [SerializeField]
    [Min(0f)]
    private float maxGroundHeightDifference = 0.1f;

    [Tooltip("移動可能な地面の最大傾斜角。")]
    [SerializeField]
    [Range(0f, 90f)]
    private float maxGroundSlopeAngle = 5f;

    [Header("坂道・地形の継ぎ目補正")]
    [Tooltip("別Colliderの地面へ移る際に許す、斜面の自然な高低差を除いた追加段差(m)。Max Ground Height Differenceを超えません。")]
    [SerializeField, Min(0f)]
    private float groundConnectionHeightTolerance = 0.08f;

    [Tooltip("前方の地面Rayが欠けたとき、次の支持面を探してよい最大の水平隙間(m)。")]
    [SerializeField, Min(0f)]
    private float groundConnectionGapTolerance = 0.1f;

    [Tooltip("軽微な地面めり込みに対する、1回の上方向位置補正の最大量(m)。")]
    [SerializeField, Min(0f)]
    private float maxGroundPenetrationRecovery = 0.1f;

    public float GroundConnectionHeightTolerance => groundConnectionHeightTolerance;
    public float GroundConnectionGapTolerance => groundConnectionGapTolerance;
    public float MaxGroundPenetrationRecovery => maxGroundPenetrationRecovery;

    public float PatrolDistance => patrolDistance;
    public float PatrolMoveSpeed => patrolMoveSpeed;
    public float PatrolTurnDuration => patrolTurnDuration;
    public float PatrolContactDamagePercent => patrolContactDamagePercent;

    // 既存の呼び出し元との互換性を保ち、
    // 攻撃予告の設定値は共通EnemyDataを参照する。
    public float DiscoveryDuration => AlertDuration;
    public float DiscoveryContactDamagePercent =>
        AlertContactDamagePercent;

    public float AttackPreparationDuration =>
        attackPreparationDuration;

    public float PreparationAnimationSpeed =>
        preparationAnimationSpeed;

    /// <summary>
    /// ChargeEnemy個体へ最初に設定する突進距離の初期値。
    /// </summary>
    public float ChargeDistance =>
        chargeDistance;

    public float AttackMoveSpeed =>
        attackMoveSpeed;

    public float AttackContactDamagePercent =>
        attackContactDamagePercent;

    public LayerMask StageLayerMask =>
        stageLayerMask;

    public float GroundCheckForwardOffset =>
        groundCheckForwardOffset;

    public float GroundCheckDownDistance =>
        groundCheckDownDistance;

    public float MaxGroundHeightDifference =>
        maxGroundHeightDifference;

    public float MaxGroundSlopeAngle =>
        maxGroundSlopeAngle;
}
