using UnityEngine;
using UnityEngine.UI;

// 指定した範囲を弧として描画するUI
public class ArcHPBarGraphic : MaskableGraphic
{
    [Header("弧の設定")]
    [SerializeField] private float startAngle = 130f;
    [SerializeField] private float totalAngle = -130f;
    [SerializeField] private float thickness = 30f;

    [Header("表示範囲（0～1）")]
    [SerializeField, Range(0f, 1f)] private float startFill = 0f;
    [SerializeField, Range(0f, 1f)] private float endFill = 1f;

    // 外部から表示範囲を変更する
    public void SetRange(float start, float end)
    {
        startFill = Mathf.Clamp01(start);
        endFill = Mathf.Clamp01(end);

        // UIを再描画する
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float from = Mathf.Clamp01(startFill);
        float to = Mathf.Clamp01(endFill);

        if (to <= from)
            return;

        Rect rect = GetPixelAdjustedRect();

        // UIの中央を円弧の中心にする
        Vector2 center = rect.center;

        // 円の半径をRectTransformのサイズから決める
        float radius = Mathf.Min(rect.width, rect.height) * 0.5f
                       - thickness * 0.5f;

        if (radius <= 0f)
            return;

        // 1度あたりの分割数を決め、滑らかな弧にする
        float angleSpan = Mathf.Abs(totalAngle * (to - from));
        int segments = Mathf.Max(1, Mathf.CeilToInt(angleSpan / 2f));

        for (int i = 0; i < segments; i++)
        {
            float t0 = Mathf.Lerp(from, to, i / (float)segments);
            float t1 = Mathf.Lerp(from, to, (i + 1f) / segments);

            float a0 = (startAngle + totalAngle * t0) * Mathf.Deg2Rad;
            float a1 = (startAngle + totalAngle * t1) * Mathf.Deg2Rad;

            Vector2 dir0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            Vector2 dir1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

            Vector2 outer0 = center + dir0 * (radius + thickness * 0.5f);
            Vector2 inner0 = center + dir0 * (radius - thickness * 0.5f);
            Vector2 outer1 = center + dir1 * (radius + thickness * 0.5f);
            Vector2 inner1 = center + dir1 * (radius - thickness * 0.5f);

            int index = vh.currentVertCount;

            vh.AddVert(outer0, color, Vector2.zero);
            vh.AddVert(inner0, color, Vector2.zero);
            vh.AddVert(inner1, color, Vector2.zero);
            vh.AddVert(outer1, color, Vector2.zero);

            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
