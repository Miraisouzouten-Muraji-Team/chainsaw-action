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


        public async UniTask ThrowAsync(
            Transform target,
            BossAttackDefinition definition,
            BossShieldThrowParameters parameters,
            float damage,
            CancellationToken cancellationToken)
        {
            // 前の投擲が残っていれば停止する。
            CancelMotion();

            if (shieldRoot == null || shieldSocket == null ||
                shieldBody == null || target == null ||
                definition == null || parameters == null)
            {
                Debug.LogError(
                    "BossShieldMotionController: 投擲用の参照が不足しています。",
                    this);
                return;
            }

            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, destroyCancellationToken);

            motionCancellation = source;
            CancellationToken token = source.Token;

            try
            {
                token.ThrowIfCancellationRequested();

                // 盾をボスから分離する。
                shieldRoot.SetParent(null, true);

                if (!shieldBody.isKinematic)
                {
                    shieldBody.linearVelocity = Vector3.zero;
                    shieldBody.angularVelocity = Vector3.zero;
                }

                shieldBody.isKinematic = true;
                shieldBody.useGravity = false;

                Vector3 start = shieldBody.position;
                Vector3 destination = ResolveGroundDestination(target.position);
                Vector3 control = (start + destination) * 0.5f + Vector3.up * parameters.ArcHeight;

                // 往路は1回分の命中履歴を持つ。
                attackHitbox?.OpenHitWindow(definition, damage);

                await MoveAlongArcAsync(
                    start,
                    control,
                    destination,
                    parameters.OutboundDuration,
                    token);

                attackHitbox?.CloseHitWindow();
                token.ThrowIfCancellationRequested();

                // 投擲開始から指定秒数経過後に帰還する。
                float waitDuration = Mathf.Max(0f, parameters.ReturnDelay - parameters.OutboundDuration);

                attackVisuals?.SetShieldReturnCue(true);
                await DelayAsync(waitDuration, token);
                attackVisuals?.SetShieldReturnCue(false);

                token.ThrowIfCancellationRequested();

                // 復路は往路と独立した命中履歴を持つ。
                attackHitbox?.OpenHitWindow(definition, damage);

                await ReturnAlongGroundAsync(parameters, token);

                attackHitbox?.CloseHitWindow();
            }
            catch (OperationCanceledException)
                when (token.IsCancellationRequested)
            {
                // 盾破壊・死亡・状態終了による正常なキャンセル。
            }
            finally
            {
                // 新しい投擲が始まっていた場合は、その状態を変更しない。
                if (ReferenceEquals(motionCancellation, source))
                {
                    motionCancellation = null;
                    attackHitbox?.CloseHitWindow();
                    attackVisuals?.SetShieldReturnCue(false);
                    SnapToSocket();
                }

                source.Dispose();
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


        /// <summary>
        /// 地形の高さに追従しながら、徐々に加速して盾を戻す。
        /// </summary>
        private async UniTask ReturnAlongGroundAsync(BossShieldThrowParameters parameters, CancellationToken cancellationToken)
        {
            float elapsed = 0f;

            while (Mathf.Abs(shieldSocket.position.x - shieldBody.position.x) > parameters.ReturnDistance)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float deltaTime = Time.fixedDeltaTime;
                elapsed += deltaTime;

                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, parameters.AccelerationDuration));

                AnimationCurve curve = parameters.AccelerationCurve;
                float acceleration = curve != null ? Mathf.Clamp01(curve.Evaluate(t)) : t;

                // 初速を少し持たせ、カーブの初期値が0でも帰還が始まるようにする。
                float maxSpeed = parameters.ReturnMaxSpeed;
                float speed = Mathf.Lerp(Mathf.Min(0.2f, maxSpeed), maxSpeed, acceleration);

                float nextX = Mathf.MoveTowards(shieldBody.position.x, shieldSocket.position.x, speed * deltaTime);

                float nextY = ResolveGroundHeight(nextX);

                Vector3 nextPosition = new Vector3(nextX, nextY, shieldBody.position.z);

                shieldBody.MovePosition(nextPosition);

                await UniTask.Yield(PlayerLoopTiming.FixedUpdate, cancellationToken);
            }
        }

        /// <summary>
        /// 指定したX座標の地面の高さを取得し、盾の底面を合わせる。
        /// </summary>
        private float ResolveGroundHeight(float x)
        {
            float currentY = shieldBody.position.y;

            Vector3 rayOrigin = new Vector3(
                x,
                Mathf.Max(currentY, shieldSocket.position.y) + groundRayHeight,
                shieldBody.position.z);

            if (!Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                groundRayDistance,
                stageMask,
                QueryTriggerInteraction.Ignore))
            {
                return currentY;
            }

            float extent = shieldBodyCollider != null
                ? shieldBodyCollider.bounds.extents.y
                : 0f;

            return hit.point.y + extent;
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
        /// 盾の投擲へ中断を通知し、直ちに装備位置へ戻す。
        /// </summary>
        public void CancelMotion()
        {
            CancellationTokenSource source = motionCancellation;
            motionCancellation = null;

            if (source != null && !source.IsCancellationRequested)
            {
                source.Cancel();
            }

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
