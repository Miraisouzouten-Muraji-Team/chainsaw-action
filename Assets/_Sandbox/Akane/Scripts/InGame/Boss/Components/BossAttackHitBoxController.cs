
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスが使用する攻撃用Colliderをまとめて管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAttackHitboxController : MonoBehaviour
    {
        [Header("攻撃判定")]
        [SerializeField] private BossAttackHitbox shieldBashHitbox;
        [SerializeField] private BossAttackHitbox shockwaveHitbox;
        [SerializeField] private BossAttackHitbox thrustHitbox;
        [SerializeField] private BossAttackHitbox slashHitbox;

        /// <summary>
        /// 指定した攻撃の命中判定を開始する。
        /// </summary>
        public void OpenHitbox(
            BossAttackType attackType,
            BossAttackDefinition definition,
            float damage)
        {
            GetHitbox(attackType)?.OpenHitWindow(definition, damage);
        }

        /// <summary>
        /// 指定した攻撃の命中判定を終了する。
        /// </summary>
        public void CloseHitbox(BossAttackType attackType)
        {
            GetHitbox(attackType)?.CloseHitWindow();
        }

        /// <summary>
        /// 盾打ち込みによる衝撃波の判定を開始する。
        /// </summary>
        public void OpenShockwave(BossAttackDefinition definition, float damage)
        {
            shockwaveHitbox?.OpenHitWindow(definition, damage);
        }

        public void CloseShockwave()
        {
            shockwaveHitbox?.CloseHitWindow();
        }

        /// <summary>
        /// ボスの死亡や攻撃中断時に全判定を無効にする。
        /// </summary>
        public void CloseAll()
        {
            shieldBashHitbox?.CloseHitWindow();
            shockwaveHitbox?.CloseHitWindow();
            thrustHitbox?.CloseHitWindow();
            slashHitbox?.CloseHitWindow();
        }

        private BossAttackHitbox GetHitbox(BossAttackType attackType)
        {
            return attackType switch
            {
                BossAttackType.ShieldBash => shieldBashHitbox,
                BossAttackType.Thrust => thrustHitbox,
                BossAttackType.VerticalSlash => slashHitbox,
                _ => null
            };
        }

        private void OnDisable()
        {
            CloseAll();
        }
    }
}
