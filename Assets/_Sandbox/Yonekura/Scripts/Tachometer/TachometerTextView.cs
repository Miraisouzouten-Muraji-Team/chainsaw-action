using TMPro;
using UnityEngine;

// 回転速度をテキストで表示する
public class TachometerTextView : MonoBehaviour
{
    [Header("メーター用テキスト")]
    [SerializeField] private TMP_Text speedText;

    [Header("回転速度の参照")]
    [SerializeField] private ChainsawAccelerator accelerator;

    private void Update()
    {
        // 参照先が設定されていなければ処理しない
        if (speedText == null || accelerator == null)
        {
            return;
        }

        // 現在の回転速度を取得する
        float speed = accelerator.CurrentSpeed;

        // 回転速度から整数部分を取り出す
        int integerPart = Mathf.FloorToInt(speed);
        // 小数部分の1桁目を取り出す
        int decimalPart = Mathf.FloorToInt((speed - integerPart) * 10);

        // 現在の回転速度をテキストに表示する
        speedText.text = $"{integerPart}<size=70%>.{decimalPart}</size>";
    }
}
