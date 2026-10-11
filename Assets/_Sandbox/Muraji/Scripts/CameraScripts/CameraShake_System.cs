using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

public class CameraShake_System : MonoBehaviour
{
    [Tooltip("追従処理が直接動かさない、揺れ専用の子Transform。未設定なら自身を使用")]
    [SerializeField] private Transform shakeTarget;

    private Vector3 basicPosition;
    private CancellationTokenSource shakeCancellation;

    private void Awake()
    {
        if (shakeTarget == null)
        {
            shakeTarget = transform;
        }

        basicPosition = shakeTarget.localPosition;
    }

    public void Shake(float duration, float magnitude)
    {
        if (!isActiveAndEnabled ||
            shakeTarget == null ||
            duration <= 0f ||
            magnitude <= 0f)
        {
            return;
        }

        CancelShake();

        // 前の揺れを戻した位置を基準にする。
        basicPosition = shakeTarget.localPosition;

        CancellationTokenSource source =
            new CancellationTokenSource();

        shakeCancellation = source;

        ShakeAsync(duration, magnitude, source).Forget();
    }

    private async UniTask ShakeAsync(
        float duration,
        float magnitude,
        CancellationTokenSource source)
    {
        CancellationToken token = source.Token;
        float endTime = Time.realtimeSinceStartup + duration;

        try
        {
            while (Time.realtimeSinceStartup < endTime)
            {
                token.ThrowIfCancellationRequested();

                if (shakeTarget == null)
                {
                    return;
                }

                float x = Random.Range(-1f, 1f) * magnitude;
                float y = Random.Range(-1f, 1f) * magnitude;

                shakeTarget.localPosition =
                    basicPosition + new Vector3(x, y, 0f);

                // ヒットストップ中も演出を進める。
                await UniTask.Yield(
                    PlayerLoopTiming.Update,
                    token
                );
            }
        }
        catch (System.OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // 再命中・無効化による通常のキャンセル。
        }
        finally
        {
            // 古い処理から新しい揺れの位置を戻さない。
            if (ReferenceEquals(shakeCancellation, source))
            {
                shakeCancellation = null;

                if (shakeTarget != null)
                {
                    shakeTarget.localPosition = basicPosition;
                }
            }

            source.Dispose();
        }
    }

    private void CancelShake()
    {
        CancellationTokenSource source = shakeCancellation;

        if (source == null)
        {
            return;
        }

        shakeCancellation = null;
        source.Cancel();

        if (shakeTarget != null)
        {
            shakeTarget.localPosition = basicPosition;
        }

        // Disposeは非同期処理のfinallyで行う。
    }

    private void OnDisable()
    {
        CancelShake();
    }
}
