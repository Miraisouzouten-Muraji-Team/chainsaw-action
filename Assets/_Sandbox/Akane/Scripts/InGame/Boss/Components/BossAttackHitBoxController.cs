using UnityEngine.Serialization;
using System.Collections.Generic;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃判定と確認用の範囲表示を管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAttackHitboxController : MonoBehaviour
    {
        private readonly HashSet<SensorPlayerController> shieldBashHits = new();

        [Header("攻撃判定")]
        [SerializeField] private BossAttackHitbox shieldBashHitbox;
        [SerializeField] private BossAttackHitbox shockwaveHitbox;

        [Tooltip("突き・縦切り共通のチェーンソー刃の攻撃判定")]
        [FormerlySerializedAs("thrustHitbox")]
        [SerializeField] private BossAttackHitbox weaponHitbox;

        [Tooltip("縦切りの範囲を可視化するためのコンポーネント")]
        [SerializeField] private BossAttackHitbox slashHitbox;

        [Header("武器範囲の同期")]
        [SerializeField] private Transform weaponRangeSource;

        [Header("Editorデバッグ")]
        [SerializeField] private bool showHitboxGizmos = true;

        public bool ShowHitboxGizmos => showHitboxGizmos;
        public Transform WeaponRangeSource => weaponRangeSource;

        public Transform VerticalSlashRangeSource =>
            slashHitbox != null ? slashHitbox.transform : null;

        /// <summary>
        /// 縦振りの表示範囲を攻撃データに合わせる。
        /// </summary>
        public void PrepareVerticalSlash(BossAttackDefinition definition)
        {
            if (definition != null)
            {
                slashHitbox?.SetDomeDiameter(definition.Range);
            }
        }

        private void Awake()
        {
            SynchronizeWeaponHitbox();
        }


        /// <summary>
        /// どちらの近接攻撃でも同じ刃のColliderを使う。
        /// </summary>
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

        /// <summary>
        /// 仮の刃モデルから命中Colliderの寸法・位置を更新する。
        /// </summary>
        public void SynchronizeWeaponHitbox()
        {
            if (weaponRangeSource == null || weaponHitbox == null)
            {
                return;
            }

            weaponHitbox.SynchronizeWithCube(weaponRangeSource);
        }

        /// <summary>
        /// 突き・縦振りの攻撃判定を開始する。
        /// </summary>
        public void OpenHitbox(
            BossAttackType attackType,
            BossAttackDefinition definition,
            float damage)
        {
            if (attackType == BossAttackType.VerticalSlash)
            {
                PrepareVerticalSlash(definition);
            }

            SynchronizeWeaponHitbox();

            GetHitbox(attackType)?.OpenHitWindow(definition, damage);
        }

        public void CloseHitbox(BossAttackType attackType)
        {
            GetHitbox(attackType)?.CloseHitWindow();
        }

        /// <summary>
        /// 盾打ち込みの落下判定を開始する。
        /// </summary>
        public void BeginShieldBash(
            BossAttackDefinition definition,
            float damage)
        {
            shieldBashHits.Clear();

            shieldBashHitbox?.SetSharedHitHistory(shieldBashHits);
            shieldBashHitbox?.OpenHitWindow(definition, damage);
        }

        /// <summary>
        /// 落下判定だけ終了する。命中履歴は衝撃波まで保持する。
        /// </summary>
        public void EndShieldBash()
        {
            shieldBashHitbox?.CloseHitWindow();
        }

        /// <summary>
        /// 落下判定の命中履歴を引き継ぎ、着地時の衝撃波を1回判定する。
        /// </summary>
        public void HitShockwaveOnce(
            BossAttackDefinition definition,
            float damage,
            float width)
        {
            if (shockwaveHitbox == null)
            {
                shieldBashHits.Clear();
                shieldBashHitbox?.SetSharedHitHistory(null);
                return;
            }

            if (shockwaveHitbox.TryGetComponent(out BoxCollider boxCollider))
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
                shockwaveHitbox.SetSharedHitHistory(null);
                shieldBashHitbox?.SetSharedHitHistory(null);
                shieldBashHits.Clear();
            }
        }

        public void CloseShockwave()
        {
            shockwaveHitbox?.CloseHitWindow();
        }

        /// <summary>
        /// 死亡・中断時にすべての攻撃判定を無効にする。
        /// </summary>
        public void CloseAll()
        {
            shieldBashHitbox?.CloseHitWindow();
            shockwaveHitbox?.CloseHitWindow();
            weaponHitbox?.CloseHitWindow();

            shieldBashHitbox?.SetSharedHitHistory(null);
            shockwaveHitbox?.SetSharedHitHistory(null);
            shieldBashHits.Clear();

            // 上半円の範囲表示側は常にダメージ判定OFF。
            slashHitbox?.CloseHitWindow();
        }

        private void OnDisable()
        {
            CloseAll();
        }
    }
}
