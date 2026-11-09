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
        [SerializeField] private BossAttackType type;

        [Header("射程と移動")]
        [SerializeField, Min(0.01f)] private float range = 5f;
        [SerializeField, Range(0.05f, 1f)] private float targetDistanceRatio = 0.5f;

        [Header("攻撃時間")]
        [SerializeField, Min(0f)] private float preparationTime = 0.5f;
        [SerializeField, Min(0f)] private float activeTime = 0.2f;
        [SerializeField, Min(0f)] private float recoveryTime = 1f;

        [Header("アニメーション")]
        [SerializeField] private string animatorTrigger;

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
