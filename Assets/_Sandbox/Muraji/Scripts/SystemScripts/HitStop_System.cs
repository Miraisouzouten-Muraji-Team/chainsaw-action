using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

public class HitStop_System : MonoBehaviour
{
    CancellationTokenSource hitStopCancellation;

    /* ヒットストップ処理(引数には時間) */
    public void StopTime(float duration)
    {
        hitStopCancellation?.Cancel(); // 既存のヒットストップをキャンセル
        hitStopCancellation?.Dispose(); // 既存のヒットストップを破棄

        hitStopCancellation=new CancellationTokenSource();
        HitStopAsync(duration, hitStopCancellation.Token).Forget(); // 非同期でヒットストップを開始
    }
    private async UniTask HitStopAsync(float duration,CancellationToken token)
    {
        Time.timeScale=0.0f; // 時間を止める

        await UniTask.Delay(System.TimeSpan.FromSeconds(duration), ignoreTimeScale: true, cancellationToken: token); // 指定時間待機
        Time.timeScale=1.0f; // 時間を戻す
    }

    private void OnDisable()
    {
        hitStopCancellation?.Cancel(); // ヒットストップをキャンセル
        hitStopCancellation?.Dispose(); // ヒットストップを破棄
        Time.timeScale=1.0f; // 時間を戻す
    }
}
