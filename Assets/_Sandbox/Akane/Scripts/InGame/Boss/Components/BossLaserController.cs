using System.Collections.Generic;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// Playerを狙うボスレーザーの生成と中断時の破棄を管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossLaserController : MonoBehaviour
    {
        [SerializeField] private BossLaserProjectile projectilePrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private Transform player;
        [SerializeField] private LayerMask stageMask;

        private readonly List<BossLaserProjectile> activeProjectiles = new();
        private Collider[] ownerColliders;

        private void Awake()
        {
            ownerColliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>
        /// BossControllerが解決したPlayer参照を設定する。
        /// </summary>
        public void SetPlayer(Transform target)
        {
            player = target;
        }

        /// <summary>
        /// 現在のPlayer位置を中心とする照準幅内へ1発発射する。
        /// </summary>
        public void Fire(
            BossAttackDefinition definition,
            float damage,
            float aimWidth,
            float speed)
        {
            if (projectilePrefab == null || firePoint == null || player == null ||
                definition == null)
            {
                return;
            }

            activeProjectiles.RemoveAll(projectile => projectile == null);

            Vector3 aimPosition = player.position +
                Vector3.up * Random.Range(-aimWidth * 0.5f, aimWidth * 0.5f);
            Vector3 direction = aimPosition - firePoint.position;
            direction.z = 0f;

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = firePoint.right;
            }

            BossLaserProjectile projectile = Instantiate(
                projectilePrefab,
                firePoint.position,
                Quaternion.identity);
            float lifeTime = definition.Range / Mathf.Max(0.01f, speed) + 1f;
            projectile.Initialize(
                direction,
                definition,
                damage,
                speed,
                lifeTime,
                stageMask,
                ownerColliders);
            activeProjectiles.Add(projectile);
        }

        /// <summary>
        /// 死亡・無効化時に残っているレーザーをすべて破棄する。
        /// </summary>
        public void CancelAll()
        {
            foreach (BossLaserProjectile projectile in activeProjectiles)
            {
                if (projectile != null)
                {
                    Destroy(projectile.gameObject);
                }
            }

            activeProjectiles.Clear();
        }

        private void OnDisable()
        {
            CancelAll();
        }
    }
}
