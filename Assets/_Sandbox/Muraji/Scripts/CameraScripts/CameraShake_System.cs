using UnityEngine;
using System.Collections;

public class CameraShake_System : MonoBehaviour
{
    Vector3 basicPosition;
    Coroutine shakeCoroutine;

    void Awake()
    {
        basicPosition = transform.localPosition;
    }

    public void Shake(float duration, float magnitude)
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            transform.localPosition = basicPosition;
        }
        shakeCoroutine = StartCoroutine(ShakeCoroutine(duration, magnitude));
    }

    IEnumerator ShakeCoroutine(float duration, float magnitude)
    {
        float elapsed = 0.0f;
        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            transform.localPosition = new Vector3(basicPosition.x + x, basicPosition.y + y, basicPosition.z);
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.localPosition = basicPosition;
    }
}
