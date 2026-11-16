using System.Collections.Generic;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの攻撃判定を管理する。
    /// 判定表示はBossHitboxDebugViewへ委譲する。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BossAttackHitbox : MonoBehaviour
    {
        private readonly HashSet<SensorPlayerController> hitPlayers = new();
        private HashSet<SensorPlayerController> sharedHitPlayers;

        private BoxCollider hitCollider;
        private EnemyPlayerReference playerReference;
        private BossAttackDefinition activeDefinition;
        private float activeDamage;

        public bool IsWindowOpen { get; private set; }
        public float LastOpenedAt { get; private set; } = -1f;
        public float LastHitAt { get; private set; } = -1f;
        public BoxCollider HitCollider => hitCollider != null
            ? hitCollider
            : GetComponent<BoxCollider>();

        private void Awake()
        {
            hitCollider = GetComponent<BoxCollider>();
            playerReference = new EnemyPlayerReference();

            if (!hitCollider.isTrigger)
            {
                Debug.LogError(
                    "BossAttackHitbox: Is TriggerをONにしてください。",
                    this);
                enabled = false;
                return;
            }

            hitCollider.enabled = false;
        }

        /// <summary>
        /// 同一攻撃内の別Colliderと命中済み情報を共有する。
        /// </summary>
        public void SetSharedHitHistory(
            HashSet<SensorPlayerController> sharedHits)
        {
            sharedHitPlayers = sharedHits;
        }

        /// <summary>
        /// 攻撃判定を開始し、開始時点で重なっている対象も確認する。
        /// </summary>
        public void OpenHitWindow(
            BossAttackDefinition definition,
            float damage)
        {
            if (!isActiveAndEnabled || hitCollider == null ||
                definition == null)
            {
                return;
            }

            CloseHitWindow();
            activeDefinition = definition;
            activeDamage = Mathf.Max(0f, damage);
            IsWindowOpen = true;
            LastOpenedAt = Time.realtimeSinceStartup;
            hitCollider.enabled = true;

            Physics.SyncTransforms();
            CheckCurrentOverlaps();
        }

        /// <summary>
        /// 攻撃判定を停止する。
        /// </summary>
        public void CloseHitWindow()
        {
            IsWindowOpen = false;
            activeDefinition = null;
            activeDamage = 0f;
            hitPlayers.Clear();

            if (hitCollider != null)
                hitCollider.enabled = false;
        }

        /// <summary>
        /// 同じ親のCubeとBoxColliderの位置・寸法を一致させる。
        /// </summary>
        public void SynchronizeWithCube(Transform sourceCube)
        {
            if (sourceCube == null || HitCollider == null)
                return;

            if (transform.parent != sourceCube.parent)
            {
                Debug.LogError(
                    "BossAttackHitbox: 刃とColliderは同じ親に配置してください。",
                    this);
                return;
            }

            transform.localPosition = sourceCube.localPosition;
            transform.localRotation = sourceCube.localRotation;
            transform.localScale = sourceCube.localScale;
            HitCollider.center = Vector3.zero;
            HitCollider.size = Vector3.one;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryHitPlayer(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryHitPlayer(other);
        }

        private void TryHitPlayer(Collider other)
        {
            if (!IsWindowOpen || activeDefinition == null ||
                activeDamage <= 0f || other == null || other.isTrigger)
            {
                return;
            }

            if (!playerReference.IsPlayerCollider(other))
                return;

            SensorPlayerController player =
                other.GetComponentInParent<SensorPlayerController>();

            if (player == null || hitPlayers.Contains(player) ||
                (sharedHitPlayers != null &&
                 sharedHitPlayers.Contains(player)))
            {
                return;
            }

            if (!playerReference.TryGetAttackHitReceiver(
                true, out IAttackHitReceiver receiver, out _))
            {
                return;
            }

            hitPlayers.Add(player);
            sharedHitPlayers?.Add(player);
            LastHitAt = Time.realtimeSinceStartup;

            receiver.ReceiveAttackHit(
                new EnemyAttackHitData(activeDefinition, activeDamage));
        }

        /// <summary>
        /// 補間判定から検出したColliderを通常の命中処理へ渡す。
        /// </summary>
        public void TryHitCollider(Collider other)
        {
            TryHitPlayer(other);
        }

        /// <summary>
        /// 現在のBoxColliderと重なっているプレイヤーを調べる。
        /// </summary>
        private void CheckCurrentOverlaps()
        {
            BoxCollider boxCollider = HitCollider;
            if (boxCollider == null)
                return;

            Vector3 scale = boxCollider.transform.lossyScale;
            Vector3 size = Vector3.Scale(
                boxCollider.size,
                new Vector3(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y),
                    Mathf.Abs(scale.z)));

            Vector3 center =
                boxCollider.transform.TransformPoint(boxCollider.center);

            Collider[] overlaps = Physics.OverlapBox(
                center,
                size * 0.5f,
                boxCollider.transform.rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            foreach (Collider other in overlaps)
                TryHitPlayer(other);
        }

        /// <summary>
        /// 現在の範囲で一度だけ攻撃する。衝撃波などに使用。
        /// </summary>
        public void HitOnce(BossAttackDefinition definition, float damage)
        {
            if (!isActiveAndEnabled || definition == null)
                return;

            OpenHitWindow(definition, damage);
            CloseHitWindow();
        }

        private void OnDisable()
        {
            CloseHitWindow();
        }
    }
}
