using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ChainsawAcceleratorの速度を数字スプライトで表示する。
/// </summary>
public class SpeedNumberController : MonoBehaviour
{
    [Header("速度設定")]
    [Tooltip("チェーンソーの速度を管理するコンポーネント")]
    [SerializeField] private ChainsawAccelerator accelerator;

    [Tooltip("チェーンソーの最大速度")]
    [SerializeField] private float maxSpeed = 50f;

    [Header("数字Image")]
    [Tooltip("十の位を表示するImage")]
    [SerializeField] private Image tensDigit;

    [Tooltip("一の位を表示するImage")]
    [SerializeField] private Image onesDigit;

    [Tooltip("小数点以下を表示するImage")]
    [SerializeField] private Image decimalDigit;

    [Header("数字スプライト")]
    [Tooltip("0～9の数字スプライト。配列の0番目に0、9番目に9を設定")]
    [SerializeField] private Sprite[] numberSprites = new Sprite[10];

    [Tooltip("小数点のスプライト")]
    [SerializeField] private Sprite decimalPointSprite;

    private int previousValue = -1;

    private void Awake()
    {
        if (accelerator == null ||
            tensDigit == null ||
            onesDigit == null ||
            decimalDigit == null ||
            numberSprites == null ||
            numberSprites.Length < 10 ||
            decimalPointSprite == null)
        {
            Debug.LogError("SpeedNumberController：必要な参照を設定してください。", this);
            enabled = false;
            return;
        }

        decimalDigit.sprite = decimalPointSprite;
    }

    private void Update()
    {
        if (accelerator == null) return;

        // 速度の割合から実際の速度を計算する。
        float speed = accelerator.SpeedRatio * maxSpeed;
        speed = Mathf.Clamp(speed, 0f, maxSpeed);

        // 小数点以下1桁に変換する。
        int value = Mathf.RoundToInt(speed * 10f);

        // 数字が変化していない場合は更新しない。
        if (value == previousValue) return;
        previousValue = value;

        int tens = value / 100;
        int ones = (value / 10) % 10;
        int decimalNumber = value % 10;

        tensDigit.sprite = numberSprites[tens];
        onesDigit.sprite = numberSprites[ones];
        decimalDigit.sprite = numberSprites[decimalNumber];
    }
}
