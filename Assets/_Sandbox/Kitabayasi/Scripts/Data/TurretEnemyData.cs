using UnityEngine;

/// <summary>
/// 固定射撃Enemy固有の調整値を保持するScriptableObject。
/// </summary>
/// <remarks>
/// 射撃前の予備動作時間など、TurretEnemy固有の設定値を保持する。
///
/// 最大HP、攻撃力、共通の攻撃予告、被ダメージ演出、死亡演出など、
/// 全Enemy共通の設定値はEnemyDataから継承する。
///
/// 索敵半径はEnemy個体ごとに調整する仕様のため、
/// TurretEnemySearchStrategyのDetection Radiusを使用する。
/// Projectile Prefab、射撃精度、最大ブレ角度、発射数、発射間隔は
/// 他Enemyと共有するEnemyProjectileShooter側で保持する。
///
/// 現在HP、現在State、現在の照準位置など、実行中に変化する状態は保持しない。
/// </remarks>
[CreateAssetMenu(
    fileName = "TurretEnemyData",
    menuName = "Enemy/Turret Enemy Data")]
public sealed class TurretEnemyData : EnemyData
{
    [Header("攻撃")]
    [Tooltip("射撃開始前に照準を表示して待機する時間（秒）。")]
    [SerializeField]
    [Min(0f)]
    private float attackPreparationDuration = 0.75f;

    public float AttackPreparationDuration =>
        attackPreparationDuration;
}
