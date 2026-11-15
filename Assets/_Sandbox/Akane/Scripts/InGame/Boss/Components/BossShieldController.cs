using System;
using Cysharp.Threading.Tasks;
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
        [SerializeField] private BossShieldMotionController motionController;

        [Header("再生成演出")]
        [SerializeField] private Material dissolveMaterial;
        [SerializeField, Min(0f)] private float dissolveDuration = 1f;

        private float currentHealth;
        private float respawnElapsedTime;

        public bool IsActive { get; private set; }
        public bool IsRestoring { get; private set; }
        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;

        public event Action ShieldBroken;
        public event Action ShieldRestored;

        private void Awake()
        {
            currentHealth = maxHealth;
            respawnElapsedTime = 0f;
            IsActive = true;
            IsRestoring = false;

            motionController ??= GetComponent<BossShieldMotionController>();

            SetShieldVisible(true);
        }

        /// <summary>
        /// 盾が破壊されている場合、再生成までの時間を進める。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (IsActive || IsRestoring)
                return;

            respawnElapsedTime += deltaTime;
        }

        /// <summary>
        /// 盾にダメージを与える。
        /// </summary>
        public bool TakeDamage(float damage)
        {
            if (!IsActive || damage <= 0f)
                return false;

            currentHealth = Mathf.Max(0f, currentHealth - damage);

            if (currentHealth <= 0f)
                BreakShield();

            return true;
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

            motionController?.CancelMotion();
            SetShieldVisible(false);
            ShieldBroken?.Invoke();
        }

        /// <summary>
        /// 再生成時間が経過していれば盾を復元する。
        /// 行動の区切りから呼び出す。
        /// </summary>
        public bool TryRestore()
        {
            if (IsActive || IsRestoring || respawnElapsedTime < respawnTime)
                return false;

            RestoreAsync().Forget();

            return true;
        }

        private async UniTaskVoid RestoreAsync()
        {
            IsRestoring = true;
            respawnElapsedTime = 0f;
            SetShieldVisible(true, enableColliders: false);
            motionController?.SnapToSocket();

            if (EnemyDissolveManager.Instance != null && dissolveMaterial != null)
            {
                await EnemyDissolveManager.Instance.PlayAppearAsync(
                    shieldVisual,
                    dissolveMaterial,
                    dissolveDuration);
            }

            if (this == null || !isActiveAndEnabled)
            {
                return;
            }

            currentHealth = maxHealth;
            IsActive = true;
            IsRestoring = false;
            SetShieldVisible(true);
            ShieldRestored?.Invoke();
        }

        /// <summary>
        /// 盾の表示と当たり判定を切り替える。
        /// </summary>
        private void SetShieldVisible(bool visible, bool enableColliders = true)
        {
            if (shieldVisual != null)
                shieldVisual.SetActive(visible);

            if (shieldColliders == null)
                return;

            foreach (var shieldCollider in shieldColliders)
            {
                if (shieldCollider != null)
                    shieldCollider.enabled = visible && enableColliders;
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
