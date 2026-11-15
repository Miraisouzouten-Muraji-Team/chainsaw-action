using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// 本体または盾のRendererだけを短時間点滅させる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossDamageFlash : MonoBehaviour
    {
        [SerializeField] private Renderer[] targetRenderers;
        [SerializeField, Min(0f)] private float duration = 0.1f;
        [SerializeField, Min(0.01f)] private float interval = 0.025f;

        private CancellationTokenSource flashCancellation;
        private bool[] originalEnabledStates;

        /// <summary>
        /// 現在の点滅を最初から再生する。
        /// </summary>
        public void Play()
        {
            Stop();

            if (!isActiveAndEnabled || targetRenderers == null ||
                targetRenderers.Length == 0 || duration <= 0f)
            {
                return;
            }

            originalEnabledStates = new bool[targetRenderers.Length];

            for (int i = 0; i < targetRenderers.Length; i++)
            {
                if (targetRenderers[i] != null)
                {
                    originalEnabledStates[i] = targetRenderers[i].enabled;
                }
            }

            flashCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                destroyCancellationToken);
            PlayAsync(flashCancellation.Token).Forget();
        }

        private async UniTaskVoid PlayAsync(CancellationToken cancellationToken)
        {
            try
            {
                float elapsed = 0f;
                bool visible = false;

                while (elapsed < duration)
                {
                    SetVisible(visible);
                    visible = !visible;
                    float waitTime = Mathf.Min(interval, duration - elapsed);
                    elapsed += waitTime;

                    await UniTask.Delay(
                        TimeSpan.FromSeconds(waitTime),
                        cancellationToken: cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 再被弾や無効化による正常な中断。
            }
            finally
            {
                Restore();
            }
        }

        private void SetVisible(bool visible)
        {
            for (int i = 0; i < targetRenderers.Length; i++)
            {
                Renderer target = targetRenderers[i];

                if (target != null && originalEnabledStates[i])
                {
                    target.enabled = visible;
                }
            }
        }

        public void Stop()
        {
            flashCancellation?.Cancel();
            flashCancellation?.Dispose();
            flashCancellation = null;
            Restore();
        }

        private void Restore()
        {
            if (targetRenderers == null || originalEnabledStates == null) { return; }

            int count = Mathf.Min(targetRenderers.Length, originalEnabledStates.Length);

            for (int i = 0; i < count; i++)
            {
                if (targetRenderers[i] != null)
                {
                    targetRenderers[i].enabled = originalEnabledStates[i];
                }
            }
        }

        private void OnDisable()
        {
            Stop();
        }
    }
}
