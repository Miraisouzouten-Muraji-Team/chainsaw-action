
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃判定とプレイヤー接触を確認するテスト用コンポーネント。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossController))]
    public sealed class BossContactTestBridge : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private EnemyPlayerContactSensor contactSensor;

        [Header("ダメージ確認")]
        [SerializeField] private bool applyTestDamage;
        [SerializeField, Min(0f)] private float testDamage = 10f;

        private BossController boss;
        private BossAttackDefinition activeAttack;

        private void Awake()
        {
            boss = GetComponent<BossController>();

            if (contactSensor == null)
            {
                contactSensor = GetComponentInChildren<EnemyPlayerContactSensor>(true);
            }
        }

        private void OnEnable()
        {
            if (boss == null)
            {
                boss = GetComponent<BossController>();
            }

            if (contactSensor == null)
            {
                contactSensor = GetComponentInChildren<EnemyPlayerContactSensor>(true);
            }

            if (contactSensor == null)
            {
                Debug.LogError("BossContactTestBridge: ContactSensorがありません。", this);
                enabled = false;
                return;
            }

            boss.AttackWindowOpened += HandleAttackWindowOpened;
            boss.AttackWindowClosed += HandleAttackWindowClosed;
            contactSensor.PlayerTouched += HandlePlayerTouched;
        }

        /// <summary>
        /// ボスの攻撃開始に合わせてTrigger判定を有効にする。
        /// </summary>
        private void HandleAttackWindowOpened(BossAttackType attackType)
        {
            activeAttack = boss.SelectedAttack?.Definition;
            contactSensor.OpenHitWindow();
            Debug.Log($"[BossContact] 判定開始: {attackType}", this);
        }

        /// <summary>
        /// ボスの攻撃終了に合わせてTrigger判定を無効にする。
        /// </summary>
        private void HandleAttackWindowClosed(BossAttackType attackType)
        {
            contactSensor.CloseHitWindow();
            activeAttack = null;
            Debug.Log($"[BossContact] 判定終了: {attackType}", this);
        }

        /// <summary>
        /// プレイヤーへの接触を記録し、必要ならテストダメージを送る。
        /// </summary>
        private void HandlePlayerTouched(SensorPlayerController player)
        {
            Debug.Log($"[BossContact] Player接触: {player.name}", this);

            if (!applyTestDamage || activeAttack == null) { return; }

            var receiver = player.GetComponentInChildren<PlayerAttackHitReceiver>(true);

            if (receiver == null || !receiver.isActiveAndEnabled) { return; }

            receiver.ReceiveAttackHit(new EnemyAttackHitData(activeAttack, testDamage));
        }

        private void OnDisable()
        {
            if (boss != null)
            {
                boss.AttackWindowOpened -= HandleAttackWindowOpened;
                boss.AttackWindowClosed -= HandleAttackWindowClosed;
            }

            if (contactSensor != null)
            {
                contactSensor.PlayerTouched -= HandlePlayerTouched;
                contactSensor.CloseHitWindow();
            }

            activeAttack = null;
        }
    }
}
