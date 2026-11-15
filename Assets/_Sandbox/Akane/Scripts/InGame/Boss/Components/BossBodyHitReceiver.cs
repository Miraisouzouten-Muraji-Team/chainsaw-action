using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボス本体への攻撃命中を受信し、HPへ反映する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossBodyHitReceiver : MonoBehaviour, IAttackHitReceiver
    {
        [SerializeField] private BossDamageFlash damageFlash;
        private BossHealth health;

        private void Awake()
        {
            health = GetComponentInParent<BossHealth>();

            if (health == null)
            {
                Debug.LogError("BossBodyHitReceiver: BossHealthが見つかりません。", this);
                enabled = false;
            }
        }

        /// <summary>
        /// プレイヤーからの攻撃命中情報を処理する。
        /// </summary>
        public void ReceiveAttackHit(AttackHitData attackHitData)
        {
            if (!isActiveAndEnabled || health.IsDead) { return; }

            if (attackHitData is not PlayerAttackHitData playerHit) { return; }

            if (playerHit.AttackData is not AttackData attackData) { return; }

            if (attackData.damage <= 0) { return; }

            // 本体のHPにダメージを与える。
            if (health.TakeDamage(attackData.damage))
            {
                damageFlash?.Play();
            }
        }
    }
}
