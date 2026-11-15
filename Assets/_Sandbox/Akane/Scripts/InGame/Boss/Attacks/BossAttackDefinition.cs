using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスが使用する攻撃の種類。
    /// </summary>
    public enum BossAttackType
    {
        ShieldBash,
        ShieldThrow,
        Thrust,
        VerticalSlash,
        Laser
    }

    /// <summary>
    /// 攻撃ごとの設定データ。
    /// </summary>
    [CreateAssetMenu(
        fileName = "BossAttack",
        menuName = "Project/Boss/Attack Definition")]
    public sealed class BossAttackDefinition : ScriptableObject
    {
        [Header("攻撃の種類")]
        [Tooltip("攻撃の種類を指定する。")]
        [SerializeField] private BossAttackType type;

        [Header("射程と移動")]
        [Tooltip("攻撃が有効な距離")]
        [SerializeField, Min(0.01f)] private float range = 5f;
        [Tooltip("攻撃時にターゲットまでの距離を何割まで詰めるか")]
        [SerializeField, Range(0.05f, 1f)] private float targetDistanceRatio = 0.5f;

        [Header("攻撃時間")]
        [Tooltip("攻撃の準備時間")]
        [SerializeField, Min(0f)] private float preparationTime = 0.5f;
        [Tooltip("攻撃が有効な時間")]
        [SerializeField, Min(0f)] private float activeTime = 0.2f;
        [Tooltip("攻撃後の回復時間")]
        [SerializeField, Min(0f)] private float recoveryTime = 1f;

        [Header("アニメーション")]
        [SerializeField] private string animatorTrigger;

        [Header("ダメージ")]
        [Tooltip("本体の攻撃力に掛ける倍率。100%なら1.0")]
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;

        [Header("モーション速度")]
        [SerializeField, Min(0.01f)] private float animationSpeed = 1f;

        [Header("盾打ち込み")]
        [SerializeField, Min(0f)] private float bashAscentDuration = 1f;
        [SerializeField, Min(0f)] private float bashAirHoldDuration = 0.5f;
        [SerializeField, Min(0f)] private float bashFallSpeed = 20f;
        [SerializeField, Min(0.01f)] private float shockwaveWidth = 8f;

        [Header("盾投げ")]
        [SerializeField, Min(0f)] private float shieldOutboundDuration = 3f;
        [SerializeField, Min(0f)] private float shieldReturnDelay = 4f;
        [SerializeField, Min(0f)] private float shieldReturnMaxSpeed = 10f;
        [SerializeField, Min(0f)] private float shieldReturnDistance = 3f;
        [SerializeField, Min(0f)] private float shieldThrowArcHeight = 4f;
        [SerializeField] private AnimationCurve shieldReturnAcceleration =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("レーザー")]
        [SerializeField, Min(1)] private int laserShotCount = 5;
        [SerializeField, Min(0f)] private float laserShotInterval = 0.5f;
        [SerializeField, Min(0f)] private float laserSpeed = 10f;
        [SerializeField, Min(0f)] private float laserAimWidth = 3f;

        public BossAttackType Type => type;
        public float Range => range;
        public float TargetDistanceRatio => targetDistanceRatio;
        public float PreparationTime => preparationTime;
        public float ActiveTime => activeTime;
        public float RecoveryTime => recoveryTime;
        public string AnimatorTrigger => animatorTrigger;
        public float DamageMultiplier => damageMultiplier;
        public float AnimationSpeed => animationSpeed;
        public float BashAscentDuration => bashAscentDuration;
        public float BashAirHoldDuration => bashAirHoldDuration;
        public float BashFallSpeed => bashFallSpeed;
        public float ShockwaveWidth => shockwaveWidth;
        public float ShieldOutboundDuration => shieldOutboundDuration;
        public float ShieldReturnDelay => shieldReturnDelay;
        public float ShieldReturnMaxSpeed => shieldReturnMaxSpeed;
        public float ShieldReturnDistance => shieldReturnDistance;
        public float ShieldThrowArcHeight => shieldThrowArcHeight;
        public AnimationCurve ShieldReturnAcceleration => shieldReturnAcceleration;
        public int LaserShotCount => laserShotCount;
        public float LaserShotInterval => laserShotInterval;
        public float LaserSpeed => laserSpeed;
        public float LaserAimWidth => laserAimWidth;

        // 盾を使う攻撃は盾がある場合に限り選択できる。
        public bool RequiresShield =>
            type == BossAttackType.ShieldBash ||
            type == BossAttackType.ShieldThrow;
    }
}
