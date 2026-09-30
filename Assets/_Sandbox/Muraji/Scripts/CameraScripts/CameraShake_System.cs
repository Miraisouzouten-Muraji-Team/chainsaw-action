using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Threading;

public class CameraShake_System : MonoBehaviour
{
    Vector3 basicPosition;
    CancellationTokenSource shakeCancellation;

    void Awake()
    {
        basicPosition = transform.localPosition; // カメラの基本位置を保存
    }

    public void Shake(float duration,float magnitude)
    {
        shakeCancellation?.Cancel(); // 既存のカメラシェイクをキャンセル
        shakeCancellation?.Dispose(); // 既存のカメラシェイクを破棄
        shakeCancellation = new CancellationTokenSource();
        transform.localPosition=basicPosition; // カメラの位置を基本位置に戻す

        ShakeAsync(duration, magnitude, shakeCancellation.Token).Forget(); // 非同期でカメラシェイクを開始
    }

    private async UniTask ShakeAsync(float duration,float magnitude,CancellationToken token)
    {
        float elapsed = 0.0f;
        while (elapsed < duration)
        {
            token.ThrowIfCancellationRequested(); // キャンセルが要求された場合、例外をスローして処理を中断

            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            transform.localPosition = new Vector3(x+x, y+y, basicPosition.z); // カメラの位置をランダムに変更
            elapsed += Time.deltaTime;
            await UniTask.Yield(PlayerLoopTiming.Update, token); // 次のフレームまで待機
        }
        transform.localPosition = basicPosition; // カメラの位置を基本位置に戻す
    }

    private void OnDisable()
    {
        shakeCancellation?.Cancel(); // カメラシェイクをキャンセル
        shakeCancellation?.Dispose(); // カメラシェイクを破棄
        transform.localPosition = basicPosition; // カメラの位置を基本位置に戻
    }
}
