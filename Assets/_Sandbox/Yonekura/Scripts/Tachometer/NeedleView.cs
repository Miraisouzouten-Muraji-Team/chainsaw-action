using UnityEngine;

// ChainsawAcceleratorの回転速度に合わせて針を動かす
public class NeedleView : MonoBehaviour
{
    [Header("回転速度の参照")]
    [SerializeField] private ChainsawAccelerator accelerator;

    [Header("針の角度")]
    [SerializeField] private float minAngle = 40f;
    [SerializeField] private float maxAngle = -90f;

    private RectTransform needleTransform;

    private void Awake()
    {
        needleTransform = GetComponent<RectTransform>();
    }

    private void Update()
    {
        if (accelerator == null || needleTransform == null)
        {
            return;
        }

        // 現在の回転速度を、0～1の割合に変換する
        float speedRatio = accelerator.SpeedRatio;

        // 回転速度の割合に応じて針の角度を求める
        float angle = Mathf.Lerp(
            minAngle,
            maxAngle,
            speedRatio
        );

        // 針を回転させる
        needleTransform.localRotation =
            Quaternion.Euler(0f, 0f, angle);
    }
}
