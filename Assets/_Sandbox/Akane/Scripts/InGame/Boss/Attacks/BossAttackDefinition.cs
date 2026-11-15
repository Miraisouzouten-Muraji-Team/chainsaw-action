
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃種類。
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
    /// 盾あり・盾なしの攻撃設定を保持する。
    /// </summary>
    [CreateAssetMenu(
        fileName = "BossAttack",
        menuName = "Project/Boss/Attack Definition")]
    public sealed class BossAttackDefinition : ScriptableObject
    {
        [Header("攻撃の種類")]
        [SerializeField] private BossAttackType type;

        [Header("射程と移動")]
        [SerializeField, Min(0.01f)] private float range = 5f;
        [SerializeField, Range(0.05f, 1f)] private float targetDistanceRatio = 0.5f;

        [Header("盾あり時の攻撃")]
        [SerializeField, Min(0f)] private float preparationTime = 0.5f;
        [SerializeField, Min(0f)] private float activeTime = 0.2f;
        [SerializeField, Min(0f)] private float recoveryTime = 1f;
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float animationSpeed = 1f;

        [Header("盾なし時の攻撃")]
        [SerializeField, Min(0f)] private float noShieldPreparationTime = 1f;
        [SerializeField, Min(0f)] private float noShieldActiveTime = 1f;
        [SerializeField, Min(0f)] private float noShieldDamageMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float noShieldAnimationSpeed = 1f;

        [Header("アニメーション")]
        [SerializeField] private string animatorTrigger;

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
        [SerializeField, Min(0.01f)] private float shieldReturnAccelerationDuration = 2f;
        [SerializeField]
        private AnimationCurve shieldReturnAcceleration =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("レーザー：盾あり")]
        [SerializeField, Min(1)] private int laserShotCount = 5;
        [SerializeField, Min(0f)] private float laserShotInterval = 0.5f;
        [SerializeField, Min(0f)] private float laserSpeed = 10f;
        [SerializeField, Min(0f)] private float laserAimWidth = 3f;

        [Header("レーザー：盾なし")]
        [SerializeField, Min(1)] private int noShieldLaserShotCount = 8;
        [SerializeField, Min(0f)] private float noShieldLaserSpeed = 12.5f;
        [SerializeField, Min(0f)] private float noShieldLaserAimWidth = 5f;

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
        public float ShieldReturnAccelerationDuration => shieldReturnAccelerationDuration;
        public AnimationCurve ShieldReturnAcceleration => shieldReturnAcceleration;

        public float LaserShotInterval => laserShotInterval;

        public bool RequiresShield =>
            type == BossAttackType.ShieldBash ||
            type == BossAttackType.ShieldThrow;

        /// <summary>
        /// 攻撃開始時の盾状態に応じて溜め時間を返す。
        /// </summary>
        public float GetPreparationTime(bool hasShield)
        {
            return hasShield ? preparationTime : noShieldPreparationTime;
        }

        public float GetActiveTime(bool hasShield)
        {
            return hasShield ? activeTime : noShieldActiveTime;
        }

        public float GetDamageMultiplier(bool hasShield)
        {
            return hasShield ? damageMultiplier : noShieldDamageMultiplier;
        }

        public float GetAnimationSpeed(bool hasShield)
        {
            return hasShield ? animationSpeed : noShieldAnimationSpeed;
        }

        public int GetLaserShotCount(bool hasShield)
        {
            return hasShield ? laserShotCount : noShieldLaserShotCount;
        }

        public float GetLaserSpeed(bool hasShield)
        {
            return hasShield ? laserSpeed : noShieldLaserSpeed;
        }

        public float GetLaserAimWidth(bool hasShield)
        {
            return hasShield ? laserAimWidth : noShieldLaserAimWidth;
        }
    }
}
