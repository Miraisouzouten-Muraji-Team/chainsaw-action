using UnityEngine;
using System.Collections;

public class HitStop_System : MonoBehaviour
{
    Coroutine hitStopCoroutine;

    /* ヒットストップ処理(引数には時間) */
    public void StopTime(float duration)
    {
        if (hitStopCoroutine != null)
        {
            StopCoroutine(hitStopCoroutine);
        }
        hitStopCoroutine = StartCoroutine(HitStopCoroutine(duration));
    }
    IEnumerator HitStopCoroutine(float duration)
    {
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration); // 現実の時間ベースで待ち
        Time.timeScale = 1f; // 時間が動き出す
        hitStopCoroutine = null;
    }
}
