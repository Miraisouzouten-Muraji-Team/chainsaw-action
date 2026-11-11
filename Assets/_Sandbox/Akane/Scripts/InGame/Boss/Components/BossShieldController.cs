using System;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの盾のHP、破壊、再生成を管理する。
    /// </summary>
    public sealed class BossShieldController : MonoBehaviour
    {
        [Header("盾の設定")]
        [SerializeField, Min(1f)] private float maxHealth = 50f;
        [SerializeField, Min(0f)] private float respawnTime = 120f;

        [Header("盾オブジェクト")]
        [SerializeField] private GameObject shieldVisual;
        [SerializeField] private Collider[] shieldColliders;

        private float currentHealth;
        private float respawnElapsedTime;

        public bool IsActive { get; private set; }
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;

        public event Action ShieldBroken;
        public event Action ShieldRestored;

        private void Awake()
        {
            currentHealth = maxHealth;
            respawnElapsedTime = 0f;
            IsActive = true;

            SetShieldVisible(true);
        }

        /// <summary>
        /// 盾が破壊されている場合、再生成までの時間を進める。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (IsActive)
                return;

            respawnElapsedTime += deltaTime;
        }

        /// <summary>
        /// 盾にダメージを与える。
        /// </summary>
        public void TakeDamage(float damage)
        {
            if (!IsActive || damage <= 0f)
                return;

            currentHealth = Mathf.Max(0f, currentHealth - damage);

            if (currentHealth <= 0f)
                BreakShield();
        }

        /// <summary>
        /// 盾を破壊する。
        /// </summary>
        private void BreakShield()
        {
            if (!IsActive)
                return;

            IsActive = false;
            currentHealth = 0f;
            respawnElapsedTime = 0f;

            SetShieldVisible(false);
            ShieldBroken?.Invoke();
        }

        /// <summary>
        /// 再生成時間が経過していれば盾を復元する。
        /// 行動の区切りから呼び出す。
        /// </summary>
        public bool TryRestore()
        {
            if (IsActive || respawnElapsedTime < respawnTime)
                return false;

            IsActive = true;
            currentHealth = maxHealth;
            respawnElapsedTime = 0f;

            SetShieldVisible(true);
            ShieldRestored?.Invoke();

            return true;
        }

        /// <summary>
        /// 盾の表示と当たり判定を切り替える。
        /// </summary>
        private void SetShieldVisible(bool visible)
        {
            if (shieldVisual != null)
                shieldVisual.SetActive(visible);

            if (shieldColliders == null)
                return;

            foreach (var shieldCollider in shieldColliders)
            {
                if (shieldCollider != null)
                    shieldCollider.enabled = visible;
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// 動作確認用。盾を破壊する。
        /// </summary>
        [ContextMenu("Debug/Break Shield")]
        private void DebugBreakShield()
        {
            TakeDamage(currentHealth);
        }
#endif
    }
}
