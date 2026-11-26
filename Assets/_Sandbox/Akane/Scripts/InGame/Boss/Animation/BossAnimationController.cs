using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスのアニメーション再生と攻撃イベントを管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAnimationController : MonoBehaviour
    {
        private const int ANIMATION_LAYER = 0;
        private const string STATE_PATH_PREFIX = "Base Layer.";

        [Header("制御方式")]
        [SerializeField]
        private BossAnimationMode animationMode = BossAnimationMode.Procedural;

        [Header("通常モーション")]
        [SerializeField] private string idleState = "Idle";
        [SerializeField] private string moveForwardState = "MoveForward";
        [SerializeField] private string deadState = "Dead";

        [Header("同期設定")]
        [SerializeField, Min(0.1f)]
        private float eventTimeout = 10f;

        [Tooltip("武器のボーンと攻撃判定の接続完了後に有効化する。")]
        [SerializeField] private bool weaponAnimationReady;

        private Animator animator;
        private int activeStateHash;
        private int attackGeneration;
        private bool attackActive;
        private bool requiresHitboxOff;
        private string eventError;

        private BossAnimationEventType lastEvent;

        public BossAnimationMode Mode => animationMode;

        public bool UsesAnimator =>
            animationMode == BossAnimationMode.Animator &&
            animator != null &&
            animator.isActiveAndEnabled &&
            animator.runtimeAnimatorController != null;

        /// <summary>
        /// BossControllerが保持するAnimatorを登録する。
        /// </summary>
        public void Initialize(Animator targetAnimator)
        {
            animator = targetAnimator;

            if (animator != null)
            {
                animator.applyRootMotion = false;
            }

            if (animationMode == BossAnimationMode.Animator &&
                !UsesAnimator)
            {
                Debug.LogWarning(
                    "BossAnimationController: Animatorが未設定です。仮モーションを使用します。",
                    this);
            }
        }

        /// <summary>
        /// 待機モーションを再生する。
        /// </summary>
        public void PlayIdle()
        {
            CancelAttack();
            TryPlayState(idleState);
        }

        /// <summary>
        /// 前進モーションを再生する。
        /// </summary>
        public void PlayMove()
        {
            CancelAttack();
            TryPlayState(moveForwardState);
        }

        /// <summary>
        /// 死亡モーションを再生する。
        /// </summary>
        public void PlayDeath()
        {
            CancelAttack();

            if (!UsesAnimator)
                return;

            // 死亡モーションが未実装の場合は現在の姿勢で停止する。
            if (!TryPlayState(deadState))
            {
                animator.speed = 0f;
            }
        }

        /// <summary>
        /// 指定された攻撃モーションを開始する。
        /// 戻り値はAnimation Event方式を使用するかどうか。
        /// </summary>
        public bool BeginAttack(
            BossAttackDefinition definition,
            bool hasShield)
        {
            CancelAttack();

            if (!UsesAnimator || definition == null)
                return false;

            string stateName = definition.GetAnimatorState(hasShield);

            // モーション未登録の攻撃は従来方式で実行する。
            if (string.IsNullOrWhiteSpace(stateName))
            {
                TryPlayState(idleState);
                return false;
            }

            bool isMelee =
                definition.Type == BossAttackType.Thrust ||
                definition.Type == BossAttackType.VerticalSlash;

            // 武器判定が未接続の状態で仮モーションを止めない。
            if (isMelee && !weaponAnimationReady)
            {
                TryPlayState(idleState);
                return false;
            }

            int stateHash = GetStateHash(stateName);

            if (!animator.HasState(ANIMATION_LAYER, stateHash))
            {
                throw new InvalidOperationException(
                    $"BossAnimationController: {stateName}が存在しません。");
            }

            activeStateHash = stateHash;
            requiresHitboxOff = isMelee;
            lastEvent = default;
            eventError = null;
            attackActive = true;

            animator.speed = 1f;
            animator.Play(stateHash, ANIMATION_LAYER, 0f);

            return true;
        }

        /// <summary>
        /// Animation Eventの通知を受け取る。
        /// 実行中の攻撃以外からの通知は無視する。
        /// </summary>
        public void ReceiveEvent(
            Animator source,
            AnimationEvent animationEvent)
        {
            if (!attackActive ||
                source != animator ||
                animationEvent == null ||
                !animationEvent.isFiredByAnimator)
            {
                return;
            }

            if (animationEvent.animatorStateInfo.fullPathHash !=
                activeStateHash)
            {
                return;
            }

            var eventType = (BossAnimationEventType)animationEvent.intParameter;

            // 同じイベントの重複通知は無視する。
            if ((int)eventType <= (int)lastEvent) { return; }

            BossAnimationEventType expectedEvent = GetNextExpectedEvent();

            if (eventType != expectedEvent)
            {
                eventError =
                    $"イベント順序が不正です。期待値: {expectedEvent}, 受信値: {eventType}";

                return;
            }

            lastEvent = eventType;
        }

        /// <summary>
        /// 次に受信すべきイベントを取得する。
        /// </summary>
        private BossAnimationEventType GetNextExpectedEvent()
        {
            if (lastEvent == default)
                return BossAnimationEventType.Action;

            if (lastEvent == BossAnimationEventType.Action &&
                requiresHitboxOff)
            {
                return BossAnimationEventType.HitboxOff;
            }

            return BossAnimationEventType.Finished;
        }

        /// <summary>
        /// 指定したイベントを受信するまで待機する。
        /// 通知済みの場合は即座に処理を再開する。
        /// </summary>
        public async UniTask WaitForEventAsync(
            BossAnimationEventType eventType,
            CancellationToken cancellationToken)
        {
            int generation = attackGeneration;
            float elapsedTime = 0f;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!attackActive ||
                    generation != attackGeneration ||
                    !UsesAnimator)
                {
                    throw new InvalidOperationException(
                        "BossAnimationController: 攻撃モーションが中断されました。");
                }

                if (!string.IsNullOrEmpty(eventError))
                {
                    throw new InvalidOperationException(eventError);
                }

                if ((int)lastEvent >= (int)eventType)
                {
                    // 判定開始前に次のイベントまで到達した場合は
                    // 遅れて攻撃を発生させない。
                    if (eventType == BossAnimationEventType.Action &&
                        lastEvent != BossAnimationEventType.Action)
                    {
                        throw new InvalidOperationException(
                            "BossAnimationController: 攻撃開始イベントを処理する前に次の区間へ進みました。");
                    }

                    return;
                }

                if (elapsedTime >= eventTimeout)
                {
                    throw new TimeoutException(
                        $"BossAnimationController: {eventType}の通知がありません。");
                }

                await UniTask.Yield(
                    PlayerLoopTiming.FixedUpdate,
                    cancellationToken);

                elapsedTime += Time.fixedDeltaTime;
            }
        }

        /// <summary>
        /// 実行中の攻撃イベント受付を終了する。
        /// </summary>
        public void CancelAttack()
        {
            attackGeneration++;

            attackActive = false;
            activeStateHash = 0;
            requiresHitboxOff = false;
            lastEvent = default;
            eventError = null;
        }

        /// <summary>
        /// Animatorのステートを直接再生する。
        /// </summary>
        private bool TryPlayState(string stateName)
        {
            if (!UsesAnimator ||
                string.IsNullOrWhiteSpace(stateName))
            {
                return false;
            }

            int stateHash = GetStateHash(stateName);

            if (!animator.HasState(ANIMATION_LAYER, stateHash))
            {
                Debug.LogWarning(
                    $"BossAnimationController: {stateName}が存在しません。",
                    this);

                return false;
            }

            animator.speed = 1f;
            animator.Play(stateHash, ANIMATION_LAYER, 0f);

            return true;
        }

        /// <summary>
        /// ステート名をAnimatorの完全パスへ変換する。
        /// </summary>
        private static int GetStateHash(string stateName)
        {
            string statePath =
                stateName.StartsWith(
                    STATE_PATH_PREFIX,
                    StringComparison.Ordinal)
                    ? stateName
                    : STATE_PATH_PREFIX + stateName;

            return Animator.StringToHash(statePath);
        }

        private void OnDisable()
        {
            CancelAttack();
        }
    }
}
