using System;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボス本体のHPと死亡判定を管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossHealth : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float maxHealth = 500f;

        private float currentHealth;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public bool IsDead => currentHealth <= 0f;

        public event Action<float, float> HealthChanged;
        public event Action Died;

        private void Awake()
        {
            currentHealth = maxHealth;
        }

        /// <summary>
        /// ボス本体にダメージを適用する。
        /// </summary>
        public bool TakeDamage(float damage)
        {
            if (IsDead || damage <= 0f ||
                float.IsNaN(damage) || float.IsInfinity(damage))
                return false;

            currentHealth = Mathf.Max(0f, currentHealth - damage);
            HealthChanged?.Invoke(currentHealth, maxHealth);

            if (IsDead)
                Died?.Invoke();

            return true;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 動作確認用。ボス本体のHPを0にする。
        /// </summary>
        [ContextMenu("Debug/Kill Boss")]
        private void DebugKillBoss()
        {
            TakeDamage(currentHealth);
        }
#endif
    }
}
