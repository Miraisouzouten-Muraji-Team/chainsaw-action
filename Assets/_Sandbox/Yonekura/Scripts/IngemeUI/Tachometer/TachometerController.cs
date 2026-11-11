using UnityEngine;

/// <summary>
/// ChainsawAcceleratorの速度を取得し、タコメーターの針を制御する。
/// </summary>
public class TachometerController : MonoBehaviour
{
    [Header("針の参照")]
    [Tooltip("針全体を回転させる親オブジェクト")]
    [SerializeField] private RectTransform needlePivot;

    [Header("速度管理")]
    [Tooltip("チェーンソーの速度を管理するコンポーネント")]
    [SerializeField] private ChainsawAccelerator accelerator;

    [Header("回転設定")]
    [Tooltip("速度0のときの針の角度")]
    [SerializeField] private float minAngle = 120f;

    [Tooltip("最大速度のときの針の角度")]
    [SerializeField] private float maxAngle = -120f;

    private void Update()
    {
        if (accelerator == null || needlePivot == null)
        {
            return;
        }

        SetSpeedRatio(accelerator.SpeedRatio);
    }

    /// <summary>
    /// 速度の割合に応じて針の角度を更新する。
    /// </summary>
    /// <param name="speedRatio">最大速度に対する現在速度の割合（0～1）</param>
    public void SetSpeedRatio(float speedRatio)
    {
        if (needlePivot == null)
        {
            return;
        }

        float normalizedSpeed = Mathf.Clamp01(speedRatio);
        float angle = Mathf.Lerp(minAngle, maxAngle, normalizedSpeed);

        needlePivot.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}
