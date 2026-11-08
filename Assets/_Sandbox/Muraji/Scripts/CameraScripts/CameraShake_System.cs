using UnityEngine;

// 通常のカメラ追従が終わってから、そのフレームの位置へ揺れだけを加える。
[DefaultExecutionOrder(20000)]
public class CameraShake_System : MonoBehaviour
{
    [Tooltip("揺らすカメラ、または揺れ専用の親・子オブジェクトです。未設定なら自身を使用します。複数カメラを重ねる場合は、それらが同じ揺れを共有できる構成で指定してください。")]
    [SerializeField] private Transform shakeTarget;

    private float shakeEndTime;
    private float shakeMagnitude;
    private bool isShaking;

    // 実際に加算した揺れだけを記録し、追従位置を開始時の座標へ戻さない。
    private Transform appliedTarget;
    private Vector3 appliedOffset;
    private Vector3 lastWrittenLocalPosition;

    // 揺れの対象を取得する。追従の基準位置は固定しない。
    private void Awake()
    {
        if (shakeTarget == null)
        {
            shakeTarget = transform;
        }
    }

    // 指定時間の揺れを予約する。再被弾時も座標を書き戻さず、時間と強さだけを更新する。
    public void Shake(float duration, float magnitude)
    {
        if (!isActiveAndEnabled || shakeTarget == null ||
            duration <= 0f || magnitude <= 0f ||
            float.IsNaN(duration) || float.IsInfinity(duration) ||
            float.IsNaN(magnitude) || float.IsInfinity(magnitude))
        {
            return;
        }

        shakeEndTime = Time.unscaledTime + duration;
        shakeMagnitude = magnitude;
        isShaking = true;
    }

    // LateUpdateの追従が前フレームの揺れを読み込まないよう、描画用の加算分を先に取り除く。
    // 物理更新では描画フレームと回数が一致しないため、この除去にはUpdateが必要。
    private void Update()
    {
        RemoveAppliedOffset();
    }

    // 追従後の最新位置に揺れを加える。ヒットストップ中も実時間で演出を進める。
    private void LateUpdate()
    {
        RemoveAppliedOffset();

        if (!isShaking || shakeTarget == null)
        {
            return;
        }

        if (Time.unscaledTime >= shakeEndTime)
        {
            isShaking = false;
            return;
        }

        appliedTarget = shakeTarget;
        appliedOffset = new Vector3(
            Random.Range(-1f, 1f) * shakeMagnitude,
            Random.Range(-1f, 1f) * shakeMagnitude,
            0f);

        lastWrittenLocalPosition = appliedTarget.localPosition + appliedOffset;
        appliedTarget.localPosition = lastWrittenLocalPosition;
    }

    // 自分の加算結果が残っている場合だけ揺れを除去し、外部の追従・転送による書き換えを保護する。
    private void RemoveAppliedOffset()
    {
        if (appliedTarget != null &&
            (appliedTarget.localPosition - lastWrittenLocalPosition).sqrMagnitude <= 0.0000000001f)
        {
            appliedTarget.localPosition -= appliedOffset;
        }

        appliedTarget = null;
        appliedOffset = Vector3.zero;
    }

    // 揺れを終了し、このコンポーネントが加えた位置差分だけを取り除く。
    private void CancelShake()
    {
        isShaking = false;
        shakeMagnitude = 0f;
        RemoveAppliedOffset();
    }

    // 無効化時に揺れを残さず、追従位置を維持する。
    private void OnDisable()
    {
        CancelShake();
    }
}
