using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

public class HitStop_System : MonoBehaviour
{
    private CancellationTokenSource hitStopCancellation;

    private float resumeTimeScale;
    private float hitStopEndTime;

    public void StopTime(float duration)
    {
        if (!isActiveAndEnabled || duration <= 0f)
        {
            return;
        }

        float requestedEndTime =
            Time.realtimeSinceStartup + duration;

        if (hitStopCancellation != null)
        {
            // 再命中は終了時刻を延長する。
            // 短い指定で、既存の長い停止を短縮しない。
            hitStopEndTime = Mathf.Max(
                hitStopEndTime,
                requestedEndTime
            );

            return;
        }

        // 他の処理によって停止中なら、その停止へ干渉しない。
        if (Time.timeScale <= 0f)
        {
            return;
        }

        resumeTimeScale = Time.timeScale;
        hitStopEndTime = requestedEndTime;

        CancellationTokenSource source =
            new CancellationTokenSource();

        hitStopCancellation = source;

        Time.timeScale = 0f;

        HitStopAsync(source).Forget();
    }

    private async UniTask HitStopAsync(
        CancellationTokenSource source)
    {
        CancellationToken token = source.Token;

        try
        {
            while (Time.realtimeSinceStartup < hitStopEndTime)
            {
                await UniTask.Yield(
                    PlayerLoopTiming.Update,
                    token
                );
            }
        }
        catch (System.OperationCanceledException)
            when (token.IsCancellationRequested)
        {
            // 無効化による通常のキャンセル。
        }
        finally
        {
            // 古い処理から新しい停止を解除しない。
            if (ReferenceEquals(hitStopCancellation, source))
            {
                hitStopCancellation = null;
                RestoreTimeScale();
            }

            source.Dispose();
        }
    }

    private void RestoreTimeScale()
    {
        // 別の処理が非0へ変更していたら、その値を維持する。
        if (Time.timeScale == 0f)
        {
            Time.timeScale = resumeTimeScale;
        }
    }

    private void OnDisable()
    {
        CancellationTokenSource source = hitStopCancellation;

        if (source == null)
        {
            return;
        }

        hitStopCancellation = null;
        source.Cancel();

        RestoreTimeScale();
    }
}
