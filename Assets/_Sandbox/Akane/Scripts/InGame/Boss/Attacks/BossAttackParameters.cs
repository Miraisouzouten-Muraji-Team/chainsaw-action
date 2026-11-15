using System;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 攻撃固有の設定を表す基底クラス。
    /// </summary>
    [Serializable]
    public abstract class BossAttackParameters
    {
        public abstract BossAttackType Type { get; }
        public abstract float GetPreparationTime(bool hasShield);
        public abstract float GetDamageMultiplier(bool hasShield);

        public static BossAttackParameters Create(BossAttackType type)
        {
            return type switch
            {
                BossAttackType.ShieldBash => new BossShieldBashParameters(),
                BossAttackType.ShieldThrow => new BossShieldThrowParameters(),
                BossAttackType.Thrust => new BossThrustParameters(),
                BossAttackType.VerticalSlash => new BossVerticalSlashParameters(),
                BossAttackType.Laser => new BossLaserParameters(),
                _ => throw new ArgumentOutOfRangeException(nameof(type))
            };
        }
    }

    /// <summary>
    /// 突き・縦切りに共通する1状態分の設定。
    /// </summary>
    [Serializable]
    public sealed class BossMeleeVariant
    {
        [SerializeField, Min(0f)] private float preparationTime;
        [SerializeField, Min(0f)] private float activeTime;
        [SerializeField, Min(0f)] private float damageMultiplier;
        [SerializeField, Min(0.01f)] private float animationSpeed;

        public float PreparationTime => preparationTime;
        public float ActiveTime => activeTime;
        public float DamageMultiplier => damageMultiplier;
        public float AnimationSpeed => animationSpeed;

        public BossMeleeVariant(
            float preparationTime,
            float activeTime,
            float damageMultiplier,
            float animationSpeed)
        {
            this.preparationTime = preparationTime;
            this.activeTime = activeTime;
            this.damageMultiplier = damageMultiplier;
            this.animationSpeed = animationSpeed;
        }
    }

    /// <summary>
    /// 盾を叩きつける攻撃の設定。
    /// </summary>
    [Serializable]
    public sealed class BossShieldBashParameters : BossAttackParameters
    {
        [SerializeField, Min(0f)] private float preparationTime = 0.5f;
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float ascentDuration = 1f;
        [SerializeField, Min(0f)] private float airHoldDuration = 0.5f;
        [SerializeField, Min(0.01f)] private float fallSpeed = 20f;
        [SerializeField, Min(0.01f)] private float shockwaveWidth = 8f;

        public override BossAttackType Type => BossAttackType.ShieldBash;
        public float AscentDuration => ascentDuration;
        public float AirHoldDuration => airHoldDuration;
        public float FallSpeed => fallSpeed;
        public float ShockwaveWidth => shockwaveWidth;

        public override float GetPreparationTime(bool hasShield) =>
            preparationTime;

        public override float GetDamageMultiplier(bool hasShield) =>
            damageMultiplier;
    }

    /// <summary>
    /// 盾投げの往路・待機・帰還設定。
    /// </summary>
    [Serializable]
    public sealed class BossShieldThrowParameters : BossAttackParameters
    {
        [SerializeField, Min(0f)] private float preparationTime = 0.5f;
        [SerializeField, Min(0f)] private float damageMultiplier = 0.4f;
        [SerializeField, Min(0.01f)] private float outboundDuration = 3f;
        [SerializeField, Min(0f)] private float returnDelay = 4f;
        [SerializeField, Min(0.01f)] private float returnMaxSpeed = 10f;
        [SerializeField, Min(0f)] private float returnDistance = 3f;
        [SerializeField, Min(0f)] private float arcHeight = 4f;
        [SerializeField, Min(0.01f)] private float accelerationDuration = 2f;
        [SerializeField]
        private AnimationCurve accelerationCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public override BossAttackType Type => BossAttackType.ShieldThrow;
        public float OutboundDuration => outboundDuration;
        public float ReturnDelay => returnDelay;
        public float ReturnMaxSpeed => returnMaxSpeed;
        public float ReturnDistance => returnDistance;
        public float ArcHeight => arcHeight;
        public float AccelerationDuration => accelerationDuration;
        public AnimationCurve AccelerationCurve => accelerationCurve;

        public override float GetPreparationTime(bool hasShield) =>
            preparationTime;

        public override float GetDamageMultiplier(bool hasShield) =>
            damageMultiplier;
    }

    /// <summary>
    /// チェーンソーによる突きの設定。
    /// </summary>
    [Serializable]
    public sealed class BossThrustParameters : BossAttackParameters
    {
        [Header("盾あり")]
        [SerializeField]
        private BossMeleeVariant shielded =
            new(1f, 2f, 0.5f, 1f);

        [Header("盾なし")]
        [SerializeField]
        private BossMeleeVariant unshielded =
            new(0.9f, 1f, 0.6f, 1.2f);

        public override BossAttackType Type => BossAttackType.Thrust;

        public BossMeleeVariant GetVariant(bool hasShield) =>
            hasShield ? shielded : unshielded;

        public override float GetPreparationTime(bool hasShield) =>
            GetVariant(hasShield).PreparationTime;

        public override float GetDamageMultiplier(bool hasShield) =>
            GetVariant(hasShield).DamageMultiplier;
    }

    /// <summary>
    /// チェーンソーによる縦切りの設定。
    /// </summary>
    [Serializable]
    public sealed class BossVerticalSlashParameters : BossAttackParameters
    {
        [Header("盾あり")]
        [SerializeField]
        private BossMeleeVariant shielded =
            new(0.75f, 1f, 0.8f, 1f);

        [Header("盾なし")]
        [SerializeField]
        private BossMeleeVariant unshielded =
            new(1f, 1f, 0.85f, 0.95f);

        [Header("武器モーション")]
        [SerializeField, Range(-180f, 180f)] private float raiseAngle = -90f;
        [SerializeField, Range(-180f, 180f)] private float finishAngle = 90f;

        public override BossAttackType Type => BossAttackType.VerticalSlash;
        public float RaiseAngle => raiseAngle;
        public float FinishAngle => finishAngle;

        public BossMeleeVariant GetVariant(bool hasShield) =>
            hasShield ? shielded : unshielded;

        public override float GetPreparationTime(bool hasShield) =>
            GetVariant(hasShield).PreparationTime;

        public override float GetDamageMultiplier(bool hasShield) =>
            GetVariant(hasShield).DamageMultiplier;
    }

    /// <summary>
    /// レーザーの1状態分の設定。
    /// </summary>
    [Serializable]
    public sealed class BossLaserVariant
    {
        [SerializeField, Min(0f)] private float preparationTime;
        [SerializeField, Min(1)] private int shotCount;
        [SerializeField, Min(0.01f)] private float speed;
        [SerializeField, Min(0f)] private float aimWidth;
        [SerializeField, Min(0f)] private float damageMultiplier;

        public float PreparationTime => preparationTime;
        public int ShotCount => shotCount;
        public float Speed => speed;
        public float AimWidth => aimWidth;
        public float DamageMultiplier => damageMultiplier;

        public BossLaserVariant(
            float preparationTime,
            int shotCount,
            float speed,
            float aimWidth,
            float damageMultiplier)
        {
            this.preparationTime = preparationTime;
            this.shotCount = shotCount;
            this.speed = speed;
            this.aimWidth = aimWidth;
            this.damageMultiplier = damageMultiplier;
        }
    }

    /// <summary>
    /// レーザー連射攻撃の設定。
    /// </summary>
    [Serializable]
    public sealed class BossLaserParameters : BossAttackParameters
    {
        [Header("共通")]
        [SerializeField, Min(0f)] private float shotInterval = 0.5f;

        [Header("盾あり")]
        [SerializeField]
        private BossLaserVariant shielded =
            new(1f, 5, 10f, 3f, 0.1f);

        [Header("盾なし")]
        [SerializeField]
        private BossLaserVariant unshielded =
            new(1f, 8, 12.5f, 5f, 0.1f);

        public override BossAttackType Type => BossAttackType.Laser;
        public float ShotInterval => shotInterval;

        public BossLaserVariant GetVariant(bool hasShield) =>
            hasShield ? shielded : unshielded;

        public override float GetPreparationTime(bool hasShield) =>
            GetVariant(hasShield).PreparationTime;

        public override float GetDamageMultiplier(bool hasShield) =>
            GetVariant(hasShield).DamageMultiplier;
    }
}
