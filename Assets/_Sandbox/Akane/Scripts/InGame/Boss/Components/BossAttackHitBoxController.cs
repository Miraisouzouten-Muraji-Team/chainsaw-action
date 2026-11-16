using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの実攻撃判定と、縦切り範囲の設定を管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAttackHitboxController : MonoBehaviour
    {
        private readonly HashSet<SensorPlayerController> shieldBashHits = new();

        [Header("実際の攻撃判定")]
        [SerializeField] private BossAttackHitbox shieldBashHitbox;
        [SerializeField] private BossAttackHitbox shockwaveHitbox;

        [FormerlySerializedAs("thrustHitbox")]
        [SerializeField] private BossAttackHitbox weaponHitbox;

        [Header("縦切りの範囲")]
        [SerializeField] private BossVerticalSlashRange verticalSlashRange;

        [Header("刃の見た目")]
        [SerializeField] private Transform weaponRangeSource;

        public Transform WeaponRangeSource => weaponRangeSource;
        public BossVerticalSlashRange SlashRange => verticalSlashRange;

        /// <summary>
        /// 攻撃定義の射程を範囲表示と武器モーションへ反映する。
        /// </summary>
        public void PrepareVerticalSlash(BossAttackDefinition definition)
        {
            if (definition == null || verticalSlashRange == null)
                return;

            verticalSlashRange.SetDiameter(definition.Range);
        }

        private void Awake()
        {
            SynchronizeWeaponHitbox();
        }

        /// <summary>
        /// 刃の見た目と実Colliderを一致させる。
        /// </summary>
        public void SynchronizeWeaponHitbox()
        {
            if (weaponRangeSource == null || weaponHitbox == null)
                return;

            weaponHitbox.SynchronizeWithCube(weaponRangeSource);
        }


        /// <summary>
        /// 指定した攻撃のHitboxを開始する。
        /// </summary>
        public void OpenHitbox(BossAttackType attackType, BossAttackDefinition definition,
            float damage)
        {
            if (attackType == BossAttackType.VerticalSlash)
            {
                PrepareVerticalSlash(definition);
            }

            SynchronizeWeaponHitbox();

            BossAttackHitbox hitbox = GetHitbox(attackType);

            if (hitbox == null)
            {
                Debug.LogError(
                    $"BossAttackHitboxController: {attackType}の判定が未設定です。",
                    this);
                return;
            }

            hitbox.OpenHitWindow(definition, damage);

            if (attackType == BossAttackType.Thrust || attackType == BossAttackType.VerticalSlash)
            {
                weaponHitbox.GetComponent<BossWeaponSweepProbe>()?.CaptureCurrentPose();
            }
        }

        public void CloseHitbox(BossAttackType attackType)
        {
            GetHitbox(attackType)?.CloseHitWindow();
        }

        /// <summary>
        /// 盾打ち込みの落下判定を開始する。
        /// </summary>
        public void BeginShieldBash(BossAttackDefinition definition, float damage)
        {
            shieldBashHits.Clear();

            if (shieldBashHitbox == null)
            {
                Debug.LogError(
                    "BossAttackHitboxController: 盾打ち込み判定が未設定です。",
                    this);
                return;
            }

            shieldBashHitbox.SetSharedHitHistory(shieldBashHits);
            shieldBashHitbox.OpenHitWindow(definition, damage);
        }

        public void EndShieldBash()
        {
            shieldBashHitbox?.CloseHitWindow();
        }

        /// <summary>
        /// 落下判定と命中履歴を共有した衝撃波を発生する。
        /// </summary>
        public void HitShockwaveOnce(
            BossAttackDefinition definition,
            float damage,
            float width)
        {
            if (shockwaveHitbox == null)
            {
                ClearShieldBashHistory();
                return;
            }

            if (shockwaveHitbox.TryGetComponent(
                out BoxCollider boxCollider))
            {
                Vector3 size = boxCollider.size;
                size.x = Mathf.Max(0.01f, width);
                boxCollider.size = size;
            }

            shockwaveHitbox.SetSharedHitHistory(shieldBashHits);

            try
            {
                shockwaveHitbox.HitOnce(definition, damage);
            }
            finally
            {
                ClearShieldBashHistory();
            }
        }

        public void CloseShockwave()
        {
            shockwaveHitbox?.CloseHitWindow();
        }

        /// <summary>
        /// 中断・死亡時にすべての攻撃判定を停止する。
        /// </summary>
        public void CloseAll()
        {
            shieldBashHitbox?.CloseHitWindow();
            shockwaveHitbox?.CloseHitWindow();
            weaponHitbox?.CloseHitWindow();
            ClearShieldBashHistory();
        }

        private void ClearShieldBashHistory()
        {
            shieldBashHitbox?.SetSharedHitHistory(null);
            shockwaveHitbox?.SetSharedHitHistory(null);
            shieldBashHits.Clear();
        }

        private BossAttackHitbox GetHitbox(BossAttackType attackType)
        {
            return attackType switch
            {
                BossAttackType.ShieldBash => shieldBashHitbox,
                BossAttackType.Thrust => weaponHitbox,
                BossAttackType.VerticalSlash => weaponHitbox,
                _ => null
            };
        }

        private void OnDisable()
        {
            CloseAll();
        }
    }
}
