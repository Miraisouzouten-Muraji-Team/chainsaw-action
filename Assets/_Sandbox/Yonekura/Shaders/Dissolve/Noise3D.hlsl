#ifndef NOISE_3D_INCLUDED
#define NOISE_3D_INCLUDED

float Hash3D(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float ValueNoise3D(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);

    f = f * f * (3.0 - 2.0 * f);

    float n000 = Hash3D(i + float3(0, 0, 0));
    float n100 = Hash3D(i + float3(1, 0, 0));
    float n010 = Hash3D(i + float3(0, 1, 0));
    float n110 = Hash3D(i + float3(1, 1, 0));
    float n001 = Hash3D(i + float3(0, 0, 1));
    float n101 = Hash3D(i + float3(1, 0, 1));
    float n011 = Hash3D(i + float3(0, 1, 1));
    float n111 = Hash3D(i + float3(1, 1, 1));

    float x00 = lerp(n000, n100, f.x);
    float x10 = lerp(n010, n110, f.x);
    float x01 = lerp(n001, n101, f.x);
    float x11 = lerp(n011, n111, f.x);

    float y0 = lerp(x00, x10, f.y);
    float y1 = lerp(x01, x11, f.y);

    return lerp(y0, y1, f.z);
}

void Noise3D_float(
    float3 Position,
    float Scale,
    out float Out)
{
    Out = ValueNoise3D(Position * Scale);
}

#endif
