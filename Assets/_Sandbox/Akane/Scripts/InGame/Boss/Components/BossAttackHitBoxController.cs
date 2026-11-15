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

        [Header("武器範囲の同期")]
        [Tooltip("突きの判定範囲に使用するCube")]
        [SerializeField] private Transform weaponRangeSource;

        [Header("Editorデバッグ")]
        [Tooltip("Scene/Gameビューへ攻撃判定Gizmoを表示する")]
        [SerializeField] private bool showHitboxGizmos = true;

        public bool ShowHitboxGizmos => showHitboxGizmos;
        public Transform VerticalSlashRangeSource =>
            slashHitbox != null ? slashHitbox.transform : null;
        public Transform WeaponRangeSource => weaponRangeSource;

        /// <summary>溜め動作の開始前に縦切りの寸法を攻撃データへ合わせる。</summary>
        public void PrepareVerticalSlash(BossAttackDefinition definition)
        {
            if (definition != null) { slashHitbox?.SetDomeDiameter(definition.Range); }
        }

        private void Awake()
        {
            SynchronizeThrustHitbox();
        }

        /// <summary>
        /// 指定した攻撃の命中判定を開始する。
        /// </summary>
        public void OpenHitbox(
            BossAttackType attackType,
            BossAttackDefinition definition,
            float damage)
        {
            if (attackType == BossAttackType.Thrust)
            {
                SynchronizeThrustHitbox();
            }
            else if (attackType == BossAttackType.VerticalSlash &&
                definition != null)
            {
                slashHitbox?.SetDomeDiameter(definition.Range);
            }

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

        /// <summary>
        /// 着地点を中心とする指定横幅の衝撃波を一度だけ判定する。
        /// </summary>
        public void HitShockwaveOnce(
            BossAttackDefinition definition,
            float damage,
            float width)
        {
            if (shockwaveHitbox == null) { return; }

            if (shockwaveHitbox.TryGetComponent(out BoxCollider boxCollider))
            {
                Vector3 size = boxCollider.size;
                size.x = Mathf.Max(0.01f, width);
                boxCollider.size = size;
            }

            shockwaveHitbox.HitOnce(definition, damage);
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

        /// <summary>
        /// 武器Cubeの位置・回転・大きさを突き判定へ反映する。
        /// </summary>
        private void SynchronizeThrustHitbox()
        {
            if (weaponRangeSource == null) { return; }

            thrustHitbox?.SynchronizeWithCube(weaponRangeSource);
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
