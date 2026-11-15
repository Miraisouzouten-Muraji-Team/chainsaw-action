
using System;
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
    /// 攻撃の共通設定と、選択された種類の専用設定を保持する。
    /// </summary>
    [CreateAssetMenu(
        fileName = "BossAttack",
        menuName = "Project/Boss/Attack Definition")]
    public sealed class BossAttackDefinition : ScriptableObject
    {
        [Header("攻撃種類")]
        [SerializeField] private BossAttackType type;

        [Header("射程と移動")]
        [SerializeField, Min(0.01f)] private float range = 5f;
        [SerializeField, Range(0.05f, 1f)]
        private float targetDistanceRatio = 0.5f;

        [Header("共通")]
        [SerializeField, Min(0f)] private float recoveryTime = 1f;
        [SerializeField] private string animatorTrigger;

        [SerializeReference] private BossAttackParameters parameters;

        public BossAttackType Type => type;
        public float Range => range;
        public float TargetDistanceRatio => targetDistanceRatio;
        public float RecoveryTime => recoveryTime;
        public string AnimatorTrigger => animatorTrigger;

        public bool RequiresShield =>
            type == BossAttackType.ShieldBash ||
            type == BossAttackType.ShieldThrow;

        public bool IsConfigured =>
            parameters != null && parameters.Type == type;

        /// <summary>
        /// 指定した攻撃専用データを取得する。
        /// </summary>
        public T GetParameters<T>() where T : BossAttackParameters
        {
            if (!IsConfigured || parameters is not T typedParameters)
            {
                throw new InvalidOperationException(
                    $"{name}: 攻撃種類と設定データが一致していません。");
            }

            return typedParameters;
        }

        /// <summary>
        /// 盾の有無に応じた準備時間を返す。
        /// </summary>
        public float GetPreparationTime(bool hasShield)
        {
            return IsConfigured
                ? parameters.GetPreparationTime(hasShield)
                : 0f;
        }

        public float GetDamageMultiplier(bool hasShield)
        {
            return IsConfigured
                ? parameters.GetDamageMultiplier(hasShield)
                : 0f;
        }
    }
}
