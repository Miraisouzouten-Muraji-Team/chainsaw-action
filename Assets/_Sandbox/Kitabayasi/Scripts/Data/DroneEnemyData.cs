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

    [Tooltip("初回突進前の予備動作時間。")]
    [SerializeField]
    [Min(0f)]
    private float initialPreparationDuration = 0.75f;

    [Tooltip("予備動作中にVisualRootを横揺れさせる幅。")]
    [SerializeField]
    [Min(0f)]
    private float preparationShakeAmount = 0.1f;

    [Tooltip("予備動作中の横揺れ回数（1秒あたり）。")]
    [SerializeField]
    [Min(0f)]
    private float preparationShakeFrequency = 12f;

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

    public float PreparationShakeAmount =>
        preparationShakeAmount;

    public float PreparationShakeFrequency =>
        preparationShakeFrequency;

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
