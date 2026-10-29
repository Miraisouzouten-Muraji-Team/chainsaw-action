#ifndef HP_BAR_SHAPE_INCLUDED
#define HP_BAR_SHAPE_INCLUDED

void HPBarShape_float(
    float2 UV,
    float AngleA,
    float AngleB,
    float AspectRatio,
    out float Mask,
    out float ShapeUVX
)
{
    float angleA = clamp(AngleA, 1.0, 179.0);
    float angleB = clamp(AngleB, 1.0, 179.0);

    float angle = (angleA + (180.0 - angleB)) * 0.5;
    angle = clamp(angle, 1.0, 179.0);

    float radians = angle * 0.01745329252;
    float tangent = tan(radians);

    float safeTangent = (abs(tangent) < 0.0001)
        ? (tangent < 0.0 ? -0.0001 : 0.0001)
        : tangent;

    float skew = 1.0 /
        (max(AspectRatio, 0.001) * safeTangent);

    skew = clamp(skew, -0.95, 0.95);

    float leftEdge =
        max(skew, 0.0) * (1.0 - UV.y)
        + max(-skew, 0.0) * UV.y;

    float shapeWidth = 1.0 - abs(skew);
    float rightEdge = leftEdge + shapeWidth;

    float insideX = step(leftEdge, UV.x)
                  * step(UV.x, rightEdge);

    float insideY = step(0.0, UV.y)
                  * step(UV.y, 1.0);

    Mask = insideX * insideY;

    // 平行四辺形の内側を、0～1の横方向座標に変換する
    ShapeUVX = (UV.x - leftEdge) / max(shapeWidth, 0.001);
}

#endif
