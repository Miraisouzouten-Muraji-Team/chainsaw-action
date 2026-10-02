using UnityEngine;

/// <summary>
/// 突進エネミーの各種設定値を保持するScriptableObject。
/// </summary>
/// <remarks>
/// 索敵範囲、索敵・攻撃Stateで使用する速度・時間・
/// ダメージ割合など、突進エネミー固有の調整値を保持する。
///
/// 最大HP、攻撃力、共通の攻撃予告、被ダメージ演出、死亡演出など、
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
    [Tooltip("プレイヤーを検知する範囲の半径。")]
    [SerializeField]
    [Min(0f)]
    private float detectionRadius = 2f;

    [Tooltip("巡回開始地点から折り返し地点までの片道距離。")]
    [SerializeField]
    [Min(0f)]
    private float patrolDistance;

    [Tooltip("索敵中の移動速度。")]
    [SerializeField]
    [Min(0f)]
    private float patrolMoveSpeed = 3f;

    [Tooltip("巡回範囲の端で左右反転するまでにかける時間。")]
    [SerializeField]
    [Min(0f)]
    private float patrolTurnDuration = 0.5f;

    [Tooltip("索敵中にプレイヤーへ接触した際の、基礎攻撃力に対するダメージ割合（%）。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float patrolContactDamagePercent = 50f;

    [Header("攻撃")]
    [Tooltip("突進を開始する前の予備動作時間。")]
    [SerializeField]
    [Min(0f)]
    private float attackPreparationDuration = 0.75f;

    [Tooltip("突進中の移動速度。")]
    [SerializeField]
    [Min(0f)]
    private float attackMoveSpeed = 15f;

    [Tooltip("突進中にプレイヤーへ接触した際の、基礎攻撃力に対するダメージ割合（%）。")]
    [SerializeField]
    [Range(0f, 100f)]
    private float attackContactDamagePercent = 100f;

    public float DetectionRadius => detectionRadius;
    public float PatrolDistance => patrolDistance;
    public float PatrolMoveSpeed => patrolMoveSpeed;
    public float PatrolTurnDuration => patrolTurnDuration;
    public float PatrolContactDamagePercent => patrolContactDamagePercent;

    // 既存の呼び出し元との互換性を保ち、設定値は共通Dataを参照する。
    public float DiscoveryDuration => AlertDuration;
    public float DiscoveryContactDamagePercent => AlertContactDamagePercent;

    public float AttackPreparationDuration => attackPreparationDuration;
    public float AttackMoveSpeed => attackMoveSpeed;
    public float AttackContactDamagePercent => attackContactDamagePercent;
}
