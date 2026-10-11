using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 全Enemyに共通する設定値を保持するScriptableObject。
/// </summary>
/// <remarks>
/// 最大HP、攻撃力、索敵半径、共通の攻撃予告、被ダメージ演出、死亡演出など、
/// Enemyの種類に関係なく共通して必要な設定値を保持する。
///
/// Enemy固有の設定が不要な場合は、この型を直接使用できる。
/// 固有設定が必要なEnemyDataの基底クラスとしても使用する。
///
/// 現在HP、現在State、現在位置など、
/// 実行中に変化する状態は保持しない。
/// </remarks>
[CreateAssetMenu(
    fileName = "EnemyData",
    menuName = "Enemy/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("基本")]
    [Tooltip("最大HP（1以上）。")]
    [SerializeField]
    [Min(1)]
    private int maxHealth = 20;

    [Tooltip("基礎攻撃力。")]
    [SerializeField, Min(0)]
    private int attackPower = 10;

    [Header("索敵")]
    [Tooltip("プレイヤーの検知半径。")]
    [SerializeField]
    [Min(0f)]
    private float detectionRadius = 2f;

    [Header("攻撃予告")]
    [Tooltip("攻撃予告の継続時間。")]
    [FormerlySerializedAs("discoveryDuration")]
    [SerializeField, Min(0f)]
    private float alertDuration = 1.0f;

    [Tooltip("攻撃予告中の接触ダメージ割合（%）。")]
    [FormerlySerializedAs("discoveryContactDamagePercent")]
    [SerializeField, Range(0f, 100f)]
    private float alertContactDamagePercent = 60f;

    [Header("やられ")]
    [Tooltip("被ダメージ時の点滅時間。")]
    [SerializeField]
    [Min(0f)]
    private float damageFlashDuration = 0.1f;

    [Tooltip("被ダメージ時の傾き継続時間。")]
    [SerializeField]
    [Min(0f)]
    private float hitTiltDuration = 0.5f;

    [Tooltip("被ダメージ時のZ軸回転角度。")]
    [SerializeField]
    private float hitTiltAngle;

    [Header("死亡")]

    [Tooltip("切断後、縮小開始までの待機時間。")]
    [SerializeField]
    [Min(0f)]
    private float deathShrinkDelay = 3f;

    [Tooltip("切断片の縮小時間。")]
    [SerializeField]
    [Min(0f)]
    private float deathShrinkDuration = 3f;

    public int MaxHealth =>
        maxHealth;

    public int AttackPower =>
        attackPower;

    public float DetectionRadius =>
        detectionRadius;

    public float AlertDuration =>
        alertDuration;

    public float AlertContactDamagePercent =>
        alertContactDamagePercent;

    public float DamageFlashDuration =>
        damageFlashDuration;

    public float HitTiltDuration =>
        hitTiltDuration;

    public float HitTiltAngle =>
        hitTiltAngle;

    public float DeathShrinkDelay =>
        deathShrinkDelay;

    public float DeathShrinkDuration =>
        deathShrinkDuration;
}
