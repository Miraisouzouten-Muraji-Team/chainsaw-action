using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 突進エネミーの各種設定値を保持するScriptableObject。
/// </summary>
/// <remarks>
/// 索敵・攻撃Stateで使用する速度・時間・ダメージ割合、
/// 平坦面移動制限など、突進エネミー固有の調整値を保持する。
///
/// 最大HP、攻撃力、索敵半径、共通の攻撃予告、被ダメージ演出、死亡演出など、
/// 全Enemy共通の設定値はEnemyDataから継承する。
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
    [Tooltip("巡回開始地点からの片道距離。")]
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
    [Tooltip("Player命中後、再突進前に後退する予備動作時間。")]
    [SerializeField]
    [Min(0f)]
    private float attackPreparationDuration = 0.75f;

    [Tooltip("Player命中後の予備動作で後退する距離。")]
    [FormerlySerializedAs("attackRetreatDistance")]
    [SerializeField]
    [Min(0f)]
    private float attackPreparationRetreatDistance;

    [Tooltip("突進時の移動速度。")]
    [SerializeField]
    [Min(0f)]
    private float attackMoveSpeed = 15f;

    [Tooltip("突進中の接触ダメージ割合（%）。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float attackContactDamagePercent = 100f;

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

    public float AttackPreparationRetreatDistance =>
        attackPreparationRetreatDistance;

    public float AttackMoveSpeed =>
        attackMoveSpeed;

    public float AttackContactDamagePercent =>
        attackContactDamagePercent;

    public float GroundCheckForwardOffset =>
        groundCheckForwardOffset;

    public float GroundCheckDownDistance =>
        groundCheckDownDistance;

    public float MaxGroundHeightDifference =>
        maxGroundHeightDifference;

    public float MaxGroundSlopeAngle =>
        maxGroundSlopeAngle;
}
