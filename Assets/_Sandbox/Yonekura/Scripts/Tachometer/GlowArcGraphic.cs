using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Sprites;

// 回転速度に応じて、指定した角度範囲の扇形を描画する
public class GlowArcGraphic : MaskableGraphic
{
    [Header("回転速度の参照")]
    [SerializeField] private ChainsawAccelerator accelerator;

    [Header("半円画像")]
    [SerializeField] private Sprite arcSprite;

    [Header("角度設定")]
    [SerializeField] private float startAngle = 130f;
    [SerializeField] private float endAngle = 0f;

    [Header("表示設定")]
    [SerializeField, Range(0f, 1f)] private float minSpeedRatio = 0f;
    [SerializeField, Range(0f, 1f)] private float maxSpeedRatio = 1f;
    [SerializeField, Range(10, 200)] private int segments = 100;

    // 現在のゲージ表示割合
    private float fillRatio;

    // 描画に使用するテクスチャ
    public override Texture mainTexture =>
        arcSprite != null ? arcSprite.texture : s_WhiteTexture;

    protected override void Awake()
    {
        base.Awake();
        // 初期状態ではゲージを空にする
        fillRatio = 0f;
    }

    private void Update()
    {
        // 回転速度の参照先が設定されていない場合は処理しない
        if (accelerator == null)
            return;

        // 速度を0～1の表示割合に変換する
        fillRatio = Mathf.InverseLerp(
            minSpeedRatio,
            maxSpeedRatio,
            Mathf.Clamp01(accelerator.SpeedRatio)
        );

        // ゲージの再描画を要求する
        SetVerticesDirty();
    }

    // 扇形の頂点と三角形を生成する
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        // 前回の描画で生成した頂点・三角形を削除する
        vh.Clear();

        if (arcSprite == null || fillRatio <= 0f)
            return;

        // UIの表示領域を取得する
        Rect rect = GetPixelAdjustedRect();

        // 描画領域の幅・高さが 0 の場合は計算できないため終了する
        if (rect.width <= 0f || rect.height <= 0f)
            return;

        // 扇形の中心と半径を決める
        Vector2 center = new Vector2(rect.center.x, rect.yMin);
        float radius = Mathf.Min(rect.width * 0.5f, rect.height);
        Vector4 uv = DataUtility.GetOuterUV(arcSprite);

        // 表示割合に応じて終端角度を決める
        float currentAngle = Mathf.Lerp(startAngle, endAngle, fillRatio);
        float totalAngle = startAngle - currentAngle;
        int count = Mathf.Max(1, Mathf.CeilToInt(segments * fillRatio));

        // 頂点0を扇形の中心にする
        AddVertex(vh, center, rect, uv);

        // 円弧上の頂点を追加し、三角形で中心と結ぶ
        for (int i = 0; i <= count; i++)
        {
            // 0～1の割合に変換し、円弧上の位置を求める
            float t = (float)i / count;
            float angle = (startAngle - totalAngle * t) * Mathf.Deg2Rad;

            // 角度から円弧上の座標を計算する
            Vector2 position = center + new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)
            ) * radius;

            // 計算した位置に頂点を追加
            AddVertex(vh, position, rect, uv);

            // 中心・前の頂点・現在の頂点を結び、三角形を作る
            if (i > 0)
                vh.AddTriangle(0, i, i + 1);
        }
    }

    // 頂点の位置・色・UV座標を設定する
    private void AddVertex(
        VertexHelper vh,
        Vector2 position,
        Rect rect,
        Vector4 uv
    )
    {
        // 頂点の位置を0～1の割合に変換する(テクスチャのUV座標を求めるために使用)
        float x = Mathf.Clamp01((position.x - rect.xMin) / rect.width);
        float y = Mathf.Clamp01((position.y - rect.yMin) / rect.height);

        // UI用の頂点データを作成する
        UIVertex vertex = UIVertex.simpleVert;

        // 頂点の位置と色を設定する
        vertex.position = position;
        vertex.color = color;

        // 頂点の位置に対応するテクスチャのUV座標を設定する
        vertex.uv0 = new Vector2(
            Mathf.Lerp(uv.x, uv.z, x),
            Mathf.Lerp(uv.y, uv.w, y)
        );

        // 作成した頂点をメッシュに追加する
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
