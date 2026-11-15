using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 盾の投擲、地面待機、加速帰還、装備位置への復帰を管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossShieldMotionController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private Transform shieldRoot;
        [SerializeField] private Transform shieldSocket;
        [SerializeField] private Rigidbody shieldBody;
        [SerializeField] private Collider shieldBodyCollider;
        [SerializeField] private BossAttackHitbox attackHitbox;
        [SerializeField] private BossAttackVisualController attackVisuals;

        [Header("地形")]
        [SerializeField] private LayerMask stageMask;
        [SerializeField, Min(0f)] private float groundRayHeight = 20f;
        [SerializeField, Min(0f)] private float groundRayDistance = 50f;

        private CancellationTokenSource motionCancellation;

        private void Awake()
        {
            if (shieldBody == null && shieldRoot != null)
            {
                shieldBody = shieldRoot.GetComponent<Rigidbody>();
            }

            if (shieldBodyCollider == null && shieldRoot != null)
            {
                shieldBodyCollider = shieldRoot.GetComponent<Collider>();
            }

            attackVisuals ??= GetComponent<BossAttackVisualController>();
            SnapToSocket();
        }

        /// <summary>
        /// 往路と復路で命中履歴を分けて盾を投げる。
        /// </summary>
        public async UniTask ThrowAsync(
            Transform target,
            BossAttackDefinition definition,
            float damage,
            CancellationToken cancellationToken)
        {
            CancelMotion();

            if (shieldRoot == null || shieldSocket == null || shieldBody == null ||
                target == null || definition == null)
            {
                return;
            }

            motionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                destroyCancellationToken);
            CancellationToken token = motionCancellation.Token;

            shieldRoot.SetParent(null, true);
            if (!shieldBody.isKinematic)
            {
                shieldBody.linearVelocity = Vector3.zero;
                shieldBody.angularVelocity = Vector3.zero;
            }
            shieldBody.isKinematic = true;
            shieldBody.useGravity = false;

            try
            {
                Vector3 start = shieldBody.position;
                Vector3 destination = ResolveGroundDestination(target.position);
                Vector3 control = (start + destination) * 0.5f +
                    Vector3.up * definition.ShieldThrowArcHeight;

                attackHitbox?.OpenHitWindow(definition, damage);
                await MoveAlongArcAsync(
                    start,
                    control,
                    destination,
                    definition.ShieldOutboundDuration,
                    token);
                attackHitbox?.CloseHitWindow();

                float waitDuration = Mathf.Max(
                    0f,
                    definition.ShieldReturnDelay - definition.ShieldOutboundDuration);
                attackVisuals?.SetShieldReturnCue(true);
                await DelayAsync(waitDuration, token);
                attackVisuals?.SetShieldReturnCue(false);

                // 復路は往路とは別に1回命中できる。
                attackHitbox?.OpenHitWindow(definition, damage);
                await ReturnAlongGroundAsync(definition, token);
                attackHitbox?.CloseHitWindow();
                SnapToSocket();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 盾破壊、死亡、State終了による正常な中断。
            }
            finally
            {
                attackHitbox?.CloseHitWindow();
                attackVisuals?.SetShieldReturnCue(false);
            }
        }

        private async UniTask MoveAlongArcAsync(
            Vector3 start,
            Vector3 control,
            Vector3 destination,
            float duration,
            CancellationToken cancellationToken)
        {
            duration = Mathf.Max(Time.fixedDeltaTime, duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed = Mathf.Min(duration, elapsed + Time.fixedDeltaTime);
                float t = elapsed / duration;
                Vector3 position = Mathf.Pow(1f - t, 2f) * start +
                    2f * (1f - t) * t * control +
                    t * t * destination;
                shieldBody.MovePosition(position);
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, cancellationToken);
            }
        }

        private async UniTask ReturnAlongGroundAsync(
            BossAttackDefinition definition,
            CancellationToken cancellationToken)
        {
            float elapsed = 0f;

            while (Mathf.Abs(shieldSocket.position.x - shieldBody.position.x) >
                   definition.ShieldReturnDistance)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed += Time.fixedDeltaTime;
                float acceleration = definition.ShieldReturnAcceleration == null
                    ? 1f
                    : definition.ShieldReturnAcceleration.Evaluate(
                        Mathf.Clamp01(elapsed / 2f));
                float speed = definition.ShieldReturnMaxSpeed *
                    Mathf.Clamp01(acceleration);
                float nextX = Mathf.MoveTowards(
                    shieldBody.position.x,
                    shieldSocket.position.x,
                    speed * Time.fixedDeltaTime);
                shieldBody.MovePosition(new Vector3(
                    nextX,
                    shieldBody.position.y,
                    shieldBody.position.z));
                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, cancellationToken);
            }
        }

        private Vector3 ResolveGroundDestination(Vector3 targetPosition)
        {
            Vector3 rayOrigin = targetPosition + Vector3.up * groundRayHeight;

            if (Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    groundRayDistance,
                    stageMask,
                    QueryTriggerInteraction.Ignore))
            {
                float extent = shieldBodyCollider == null
                    ? 0f
                    : shieldBodyCollider.bounds.extents.y;
                return new Vector3(targetPosition.x, hit.point.y + extent, targetPosition.z);
            }

            return new Vector3(targetPosition.x, shieldBody.position.y, targetPosition.z);
        }

        private static async UniTask DelayAsync(
            float seconds,
            CancellationToken cancellationToken)
        {
            if (seconds <= 0f) { return; }

            await UniTask.Delay(
                TimeSpan.FromSeconds(seconds),
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 投擲を中断し、盾を装備位置へ戻す。
        /// </summary>
        public void CancelMotion()
        {
            motionCancellation?.Cancel();
            motionCancellation?.Dispose();
            motionCancellation = null;
            attackHitbox?.CloseHitWindow();
            attackVisuals?.SetShieldReturnCue(false);
            SnapToSocket();
        }

        public void SnapToSocket()
        {
            if (shieldRoot == null || shieldSocket == null) { return; }

            if (shieldBody != null)
            {
                if (!shieldBody.isKinematic)
                {
                    shieldBody.linearVelocity = Vector3.zero;
                    shieldBody.angularVelocity = Vector3.zero;
                }

                shieldBody.isKinematic = true;
                shieldBody.useGravity = false;
            }

            shieldRoot.SetParent(shieldSocket, false);
            shieldRoot.localPosition = Vector3.zero;
            shieldRoot.localRotation = Quaternion.identity;
        }

        private void OnDisable()
        {
            CancelMotion();
        }
    }
}
