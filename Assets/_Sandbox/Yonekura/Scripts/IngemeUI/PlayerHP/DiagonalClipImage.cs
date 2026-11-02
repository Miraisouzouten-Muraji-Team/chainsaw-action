using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// UI画像を斜めの切断線で右側から非表示にするクラス。
/// 画像のサイズや元のUV座標を維持して描画する。
/// </summary>
[AddComponentMenu("UI/Diagonal Clip Image")]
public class DiagonalClipImage : Image
{
    [Header("斜め設定")]
    [Tooltip("切断線の傾き。右端の斜め部分に合わせて調整してください。")]
    [SerializeField] private float edgeSlope = 0.2f;

    [Tooltip("表示する割合。1で全表示、0で全非表示になります。")]
    [SerializeField, Range(0f, 1f)] private float visibleRatio = 1f;

    //private readonly List<UIVertex> vertices = new();
    //private readonly List<int> indices = new();

    /// <summary>
    /// 表示割合を設定する。
    /// </summary>
    public void SetVisibleRatio(float ratio)
    {
        visibleRatio = Mathf.Clamp01(ratio);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (sprite == null || visibleRatio <= 0f)
            return;

        // 元画像のメッシュを取得する。
        var original = new VertexHelper();
        base.OnPopulateMesh(original);

        var sourceVertices = new List<UIVertex>();
        var sourceIndices = new List<int>();

        original.GetUIVertexStream(sourceVertices);

        // Imageの三角形メッシュを取得する。
        for (int i = 0; i < sourceVertices.Count; i += 3)
        {
            if (i + 2 >= sourceVertices.Count)
                break;

            ClipTriangle(
                sourceVertices[i],
                sourceVertices[i + 1],
                sourceVertices[i + 2],
                vh
            );
        }

        original.Dispose();
    }

    private void ClipTriangle(
        UIVertex a,
        UIVertex b,
        UIVertex c,
        VertexHelper vh)
    {
        var polygon = new List<UIVertex> { a, b, c };
        var clipped = new List<UIVertex>();

        float width = rectTransform.rect.width;
        float height = rectTransform.rect.height;

        float cutX = rectTransform.rect.xMin + width * visibleRatio;

        // 右端の斜め形状に合わせた切断線を作る。
        float centerY = rectTransform.rect.center.y;

        for (int i = 0; i < polygon.Count; i++)
        {
            UIVertex current = polygon[i];
            UIVertex next = polygon[(i + 1) % polygon.Count];

            float currentLimit = cutX + (current.position.y - centerY) * edgeSlope;
            float nextLimit = cutX + (next.position.y - centerY) * edgeSlope;

            bool currentInside = current.position.x <= currentLimit;
            bool nextInside = next.position.x <= nextLimit;

            if (currentInside)
                clipped.Add(current);

            if (currentInside != nextInside)
            {
                float t = (currentLimit - current.position.x) /
                          ((next.position.x - current.position.x) -
                           (next.position.y - current.position.y) * edgeSlope);

                t = Mathf.Clamp01(t);

                UIVertex intersection = current;

                // 頂点の位置を補間する。
                intersection.position = Vector3.Lerp(
                    current.position,
                    next.position,
                    t
                );

                // UV座標を補間する。
                intersection.uv0 = Vector4.Lerp(
                    current.uv0,
                    next.uv0,
                    t
                );

                // 色を補間する。
                intersection.color = Color32.Lerp(
                    current.color,
                    next.color,
                    t
                );

                clipped.Add(intersection);
            }
        }

        if (clipped.Count < 3)
            return;

        int startIndex = vh.currentVertCount;

        foreach (var vertex in clipped)
            vh.AddVert(vertex);

        for (int i = 1; i < clipped.Count - 1; i++)
            vh.AddTriangle(startIndex, startIndex + i, startIndex + i + 1);
    }
}
