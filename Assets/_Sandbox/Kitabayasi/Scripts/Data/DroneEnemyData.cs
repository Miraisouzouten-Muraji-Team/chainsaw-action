// 責務:
// ・ドローンエネミー固有の調整値を保持する。
// ・索敵Stateで使用する巡回速度、反転時間、接触ダメージ割合を保持する。
//
// 担当しない責務:
// ・巡回地点A/Bなど、Enemy個体ごとの配置情報。
// ・現在位置、現在State、現在の巡回方向などの実行時状態。

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

    public float PatrolMoveSpeed =>
        patrolMoveSpeed;

    public float PatrolTurnDuration =>
        patrolTurnDuration;

    public float PatrolContactDamagePercent =>
        patrolContactDamagePercent;
}
