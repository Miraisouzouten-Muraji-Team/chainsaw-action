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
        [Tooltip("盾あり時の攻撃力に掛ける倍率。100%なら1.0")]
        [SerializeField, Min(0f)] private float damageMultiplier = 1f;

        [Tooltip("盾なし時の攻撃力に掛ける倍率")]
        [SerializeField, Min(0f)] private float noShieldDamageMultiplier = 1f;

        [Header("盾なし時の攻撃設定")]
        [SerializeField, Min(0f)] private float noShieldPreparationTime = 1f;
        [SerializeField, Min(0f)] private float noShieldActiveTime = 1f;

        [Header("モーション速度")]
        [SerializeField, Min(0.01f)] private float animationSpeed = 1f;
        [SerializeField, Min(0.01f)] private float noShieldAnimationSpeed = 1f;

        /// <summary>
        /// 盾の有無に応じた攻撃倍率を取得する。
        /// </summary>
        public float GetDamageMultiplier(bool hasShield)
        {
            return hasShield ? damageMultiplier : noShieldDamageMultiplier;
        }

        public float GetPreparationTime(bool hasShield)
        {
            return hasShield ? PreparationTime : noShieldPreparationTime;
        }

        public float GetActiveTime(bool hasShield)
        {
            return hasShield ? ActiveTime : noShieldActiveTime;
        }

        public float GetAnimationSpeed(bool hasShield)
        {
            return hasShield ? animationSpeed : noShieldAnimationSpeed;
        }


        public BossAttackType Type => type;
        public float Range => range;
        public float TargetDistanceRatio => targetDistanceRatio;
        public float PreparationTime => preparationTime;
        public float ActiveTime => activeTime;
        public float RecoveryTime => recoveryTime;
        public string AnimatorTrigger => animatorTrigger;

        // 盾を使う攻撃は盾がある場合に限り選択できる。
        public bool RequiresShield =>
            type == BossAttackType.ShieldBash ||
            type == BossAttackType.ShieldThrow;
    }
}
