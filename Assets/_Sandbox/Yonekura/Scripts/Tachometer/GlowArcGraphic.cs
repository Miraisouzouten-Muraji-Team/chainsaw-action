
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;

// 指定した角度範囲の画像を描画する
public class GlowArcGraphic : MaskableGraphic
{
    [Header("速度設定")]
    [SerializeField] private ChainsawAccelerator accelerator;

    [Header("半円画像")]
    [SerializeField] private Sprite arcSprite;

    [Header("角度設定")]
    [SerializeField] private float startAngle = 126f;
    [SerializeField] private float endAngle = 0f;

    [Header("表示設定")]
    [SerializeField, Range(0f, 1f)]
    private float minSpeedRatio = 0f;

    [SerializeField, Range(0f, 1f)]
    private float maxSpeedRatio = 1f;

    [SerializeField, Range(10, 200)]
    private int segments = 100;

    private float fillRatio;

    public override Texture mainTexture
    {
        get
        {
            if (arcSprite != null)
                return arcSprite.texture;

            return s_WhiteTexture;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        fillRatio = 0f;
    }

    private void Update()
    {
        if (accelerator == null)
            return;

        // SpeedRatio が 0～1 の値であることを前提にする
        fillRatio = Mathf.Clamp01(accelerator.SpeedRatio);

        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        if (arcSprite == null || fillRatio <= 0f)
            return;

        Rect rect = GetPixelAdjustedRect();

        // 半円の中心をRectの下辺中央に設定
        Vector2 center = new Vector2(
            rect.center.x,
            rect.yMin
        );

        // 半円がRectからはみ出さない範囲で半径を設定
        float radius = Mathf.Min(
            rect.width * 0.5f,
            rect.height
        );

        Vector4 uv = DataUtility.GetOuterUV(arcSprite);

        float currentAngle = Mathf.Lerp(
            startAngle,
            endAngle,
            fillRatio
        );

        float totalAngle = startAngle - currentAngle;
        int count = Mathf.Max(
            1,
            Mathf.CeilToInt(segments * fillRatio)
        );

        AddArcVertex(vh, center, rect, uv);

        for (int i = 0; i <= count; i++)
        {
            float t = (float)i / count;
            float angle = startAngle - totalAngle * t;
            float rad = angle * Mathf.Deg2Rad;

            Vector2 position = center + new Vector2(
                Mathf.Cos(rad),
                Mathf.Sin(rad)
            ) * radius;

            AddArcVertex(vh, position, rect, uv);

            if (i > 0)
                vh.AddTriangle(0, i, i + 1);
        }
    }

    private void AddArcVertex(
        VertexHelper vh,
        Vector2 position,
        Rect rect,
        Vector4 uv
    )
    {
        float x = Mathf.Clamp01(
            (position.x - rect.xMin) / rect.width
        );

        float y = Mathf.Clamp01(
            (position.y - rect.yMin) / rect.height
        );

        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = color;
        vertex.uv0 = new Vector2(
            Mathf.Lerp(uv.x, uv.z, x),
            Mathf.Lerp(uv.y, uv.w, y)
        );

        vh.AddVert(vertex);
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}

