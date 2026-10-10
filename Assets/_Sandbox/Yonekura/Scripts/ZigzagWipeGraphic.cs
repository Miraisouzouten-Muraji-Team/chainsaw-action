using UnityEngine;
using UnityEngine.UI;

// 斜めの帯4本で画面を閉じ、同じ方向に開く
public class ZigzagWipeGraphic : Graphic
{
    private readonly float[] progress = new float[4];

    // 帯の移動進行度を取得
    public float GetBarProgress(int index)
    {
        return progress[index];
    }

    // 帯の移動進行度を設定
    public void SetBarProgress(int index, float value)
    {
        if (index < 0 || index >= progress.Length)
        {
            return;
        }

        progress[index] = Mathf.Clamp(value, 0f, 2f);
        SetVerticesDirty();
    }

    // すべての帯を同じ進行度にする
    public void SetAllProgress(float value)
    {
        for (int i = 0; i < progress.Length; i++)
        {
            progress[i] = Mathf.Clamp(value, 0f, 2f);
        }

        SetVerticesDirty();
    }

    // 全帯を画面外の初期位置に戻す
    public void ResetProgress()
    {
        SetAllProgress(0f);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect rect = GetPixelAdjustedRect();
        Vector2 center = rect.center;

        float diagonal = Mathf.Sqrt(
            rect.width * rect.width +
            rect.height * rect.height
        );

        // 画面全体を覆える長さと太さ
        float barLength = diagonal * 3f;
        // 帯を太くして、隣同士が確実に重なるようにする
        float barThickness = diagonal * 0.5f;

        // 帯の太さの約2/3だけ間隔を空ける
        float spacing = barThickness * (2f / 3f);

        // 右肩上がり45度の方向
        Vector2 along = new Vector2(1f, 1f).normalized;

        // 帯の並び方向
        Vector2 across = new Vector2(-1f, 1f).normalized;

        // 4本を重ねながら配置して隙間を防ぐ
        float[] offsets =
        {
             spacing * 0.9f,
             spacing * 0.2f,
            -spacing * 0.5f,
            -spacing * 1.2f
        };

        // 1本目と3本目は左下方向、
        // 2本目と4本目は右上方向へ進む
        Vector2[] directions =
        {
            -along,
             along,
            -along,
             along
        };

        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;

        for (int i = 0; i < progress.Length; i++)
        {
            float t = progress[i];

            // 進行度0は画面外、1は画面を覆う位置、
            // 2は同じ方向に画面外まで通過した位置
            Vector2 start =
                center +
                across * offsets[i] -
                along * (barLength * 0.5f) +
                directions[i] * diagonal * 3f * (t - 1f);

            Vector2 end = start + along * barLength;
            Vector2 halfWidth = across * (barThickness * 0.5f);

            Vector2[] corners =
            {
                start - halfWidth,
                start + halfWidth,
                end + halfWidth,
                end - halfWidth
            };

            int first = vh.currentVertCount;

            for (int j = 0; j < corners.Length; j++)
            {
                vertex.position = corners[j];
                vh.AddVert(vertex);
            }

            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }
    }
}
