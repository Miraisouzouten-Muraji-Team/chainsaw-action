using UnityEngine;

/// <summary>
/// チェーンソー食い込みの調整値をまとめて管理する設定アセット。
/// プレイヤーごとの進行状態は保持しない。
/// </summary>
[CreateAssetMenu(fileName = "DiggingParameter", menuName = "Player/Digging Parameter")]
public class DiggingParameter : ScriptableObject
{
    [Header("操作と開始ボーナス")]
    [Tooltip("Toggle：押すたび切替／Hold：長押し")]
    public SensorChainsawDigging.InputMode inputMode = SensorChainsawDigging.InputMode.Toggle;

    [Tooltip("開始ボーナスに必要な回転速度の割合")]
    [Range(0f, 1f)]
    public float bonusThreshold = 0.8f;

    [Header("床")]
    [Tooltip("速度加算＝回転速度÷この値")]
    [Min(0.01f)]
    public float floorSpeedDivisor = 5f;

    [Tooltip("ボーナス時のダッシュ加算速度")]
    [Min(0f)]
    public float floorDashPower = 10f;

    [Tooltip("ボーナス時の回避時間（秒）")]
    [Min(0f)]
    public float floorEvadeTime = 0.5f;

    [Header("壁")]
    [Tooltip("回転リソースの消費間隔（秒）")]
    [Min(0.01f)]
    public float wallConsumeInterval = 0.1f;

    [Tooltip("1回あたりの消費量")]
    [Min(0f)]
    public float wallConsumeAmount = 20f;

    [Tooltip("通常の壁ジャンプ速度")]
    [Min(0f)]
    public float wallJumpPower = 10f;

    [Tooltip("ボーナス時の壁ジャンプ速度")]
    [Min(0f)]
    public float bonusWallJumpPower = 15f;

    [Tooltip("ボーナス壁ジャンプの回避時間（秒）")]
    [Min(0f)]
    public float wallEvadeTime = 0.25f;

    [Tooltip("壁を見失っても上昇を続ける時間（秒）")]
    [Min(0f)]
    public float wallContactGraceTime = 2f;

    [Header("天井")]
    [Tooltip("速度加算＝回転速度÷この値")]
    [Min(0.01f)]
    public float ceilingSpeedDivisor = 7.5f;

    [Tooltip("ボーナス時のダッシュ加算速度")]
    [Min(0f)]
    public float ceilingDashPower = 7.5f;

    [Tooltip("ボーナス時の回避時間（秒）")]
    [Min(0f)]
    public float ceilingEvadeTime = 0.35f;

    [Header("敵：消費は攻撃1回ごと")]
    [Tooltip("連続攻撃の間隔（秒）")]
    [Min(0.01f)]
    public float enemyAttackInterval = 0.1f;

    [Tooltip("通常の攻撃1回の消費量")]
    [Min(0f)]
    public float enemyConsumeAmount = 1f;

    [Tooltip("ボーナス時の攻撃1回の消費量")]
    [Min(0f)]
    public float bonusEnemyConsumeAmount = 2f;

    [Tooltip("PowerRatioが0のときのダメージ倍率")]
    [Min(0f)]
    public float minDamageMultiplier = 0.9f;

    [Tooltip("PowerRatioが1のときのダメージ倍率")]
    [Min(0f)]
    public float maxDamageMultiplier = 1.25f;

    [Tooltip("ボーナス時に追加で掛ける倍率")]
    [Min(0f)]
    public float bonusDamageMultiplier = 1.5f;

    [Header("床から壁への切り替え")]
    [Tooltip("壁を検出する直前に押していれば、壁登りになる猶予時間（秒）")]
    [Min(0f)]
    public float wallClimbInputWindow = 0.2f;

    [Tooltip("床Colliderの切り替え時に一瞬だけ発生するWall判定を無視する時間")]
    [Min(0f)]
    public float floorTransitionWallGraceTime = 0.05f;

}
