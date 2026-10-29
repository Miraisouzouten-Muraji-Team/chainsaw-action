#ifndef TACHOMETER_FUNCTION_INCLUDED
#define TACHOMETER_FUNCTION_INCLUDED

void TachometerFunction_float(
    float2 UV,
    float StartAngle,
    float EndAngle,
    float Thickness,
    out float Progress,
    out float Alpha)
{
    // UVの中心を円弧の中心にする
    float2 p = UV - float2(0.5, 0.5);

    // 中心からの距離を求める
    float radius = length(p);

    // 外側の半径と内側の半径からリングを作る
    float outerRadius = 0.5;
    float innerRadius = max(0.0, outerRadius - Thickness);

    float outerMask =
        1.0 - smoothstep(outerRadius - 0.002,
                         outerRadius + 0.002, radius);

    float innerMask =
        smoothstep(innerRadius - 0.002,
                   innerRadius + 0.002, radius);

    float ringMask = outerMask * innerMask;

     // 真下を0度として、時計回りに角度を増やす
    float angle = atan2(-p.x, -p.y) * 57.2957795;

    // 角度を0～360度に正規化する
    angle = angle < 0.0 ? angle + 360.0 : angle;

    // 開始角度から終了角度までの時計回りの角度範囲
float rawRange = EndAngle - StartAngle;
float angleRange = frac(rawRange / 360.0) * 360.0;

// 360度の全周を指定した場合は360度として扱う
if (abs(rawRange) >= 359.999)
    angleRange = 360.0;

float angleProgress = 0.0;

if (angleRange > 0.001)
{
    // 開始角度からの時計回りの差を求める
    float angleOffset = frac((angle - StartAngle) / 360.0) * 360.0;
    angleProgress = angleOffset / angleRange;
}

// 指定範囲の外は非表示
float angleMask =
    step(0.0, angleProgress) * step(angleProgress, 1.0);

Progress = saturate(angleProgress);
Alpha = ringMask * angleMask;
}

#endif
