using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの盾への攻撃命中を受信する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossShieldHitReceiver : MonoBehaviour, IAttackHitReceiver
    {
        [SerializeField] private BossShieldController shield;

        private void Awake()
        {
            if (shield == null)
            {
                shield = GetComponentInParent<BossShieldController>();
            }
        }

        /// <summary>
        /// プレイヤーからの攻撃を盾のHPへ反映する。
        /// </summary>
        public void ReceiveAttackHit(AttackHitData attackHitData)
        {
            if (!isActiveAndEnabled || shield == null || !shield.IsActive) { return; }

            if (attackHitData is not PlayerAttackHitData playerHit) { return; }

            if (playerHit.AttackData is not AttackData attackData) { return; }

            if (attackData.damage <= 0) { return; }

            // 本体とは独立した盾HPにダメージを与える。
            shield.TakeDamage(attackData.damage);
        }
    }
}
