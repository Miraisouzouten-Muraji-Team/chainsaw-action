
#ifndef STAGE_SURFACE_COMMON_INCLUDED
#define STAGE_SURFACE_COMMON_INCLUDED

// Object Spaceの位置を、Scaleを反映した座標へ変換する。
float3 GetStageScaledPositionOS(float3 positionWS)
{
    float3 positionOS = TransformWorldToObject(positionWS);
    float3x3 objectToWorld = (float3x3)unity_ObjectToWorld;
    float3 scaleWS = float3(
        length(mul(objectToWorld, float3(1.0, 0.0, 0.0))),
        length(mul(objectToWorld, float3(0.0, 1.0, 0.0))),
        length(mul(objectToWorld, float3(0.0, 0.0, 1.0)))
    );

    return positionOS * scaleWS;
}

// Cubeの向きに合わせ、3方向からテクスチャを投影する。
half3 SampleStageSurfaceTexture(float3 positionWS, float3 normalWS)
{
    float tileSize = max(_StageTileSize, 0.0001);
    float3 position = GetStageScaledPositionOS(positionWS) / tileSize;
    float3 normalOS = abs(normalize(TransformWorldToObjectNormal(normalWS)));

    // 面の法線に応じて3方向の投影比率を決定する。
    float sharpness = max(_StageProjectionSharpness, 1.0);
    float3 weights = pow(normalOS, sharpness);
    weights /= max(weights.x + weights.y + weights.z, 0.0001);

    half3 xColor = SAMPLE_TEXTURE2D(
        _StageSurfaceMap, sampler_StageSurfaceMap, position.zy).rgb;
    half3 yColor = SAMPLE_TEXTURE2D(
        _StageSurfaceMap, sampler_StageSurfaceMap, position.xz).rgb;
    half3 zColor = SAMPLE_TEXTURE2D(
        _StageSurfaceMap, sampler_StageSurfaceMap, position.xy).rgb;

    return xColor * weights.x +
           yColor * weights.y +
           zColor * weights.z;
}

// 指定された表面からの距離をワールド単位で計算する。
float GetStageSurfaceDepth(float3 positionWS)
{
    float3 positionOS = TransformWorldToObject(positionWS);
    float3 axisOS = normalize(_StageSurfaceAxisOS.xyz);
    float3 axisWS = mul((float3x3)unity_ObjectToWorld, axisOS);

    float depthOS = _StageTopOS - dot(positionOS, axisOS);
    return depthOS * length(axisWS);
}

// 現在の位置がテクスチャを表示する領域か判定する。
bool IsStageSurfaceArea(float3 positionWS, float3 normalWS)
{
    float3 axisOS = normalize(_StageSurfaceAxisOS.xyz);
    float3 axisWS = normalize(TransformObjectToWorldNormal(axisOS));
    float normalDot = dot(normalize(normalWS), axisWS);
    float threshold = saturate(_StageNormalThreshold);

    // 表面方向を向いている面は、全面をテクスチャ表示する。
    if (normalDot >= threshold)
        return true;

    // 側面の帯が無効な場合は、内部色を使用する。
    if (_StageSideBandEnabled < 0.5)
        return false;

    // 上面・下面以外の面を側面として扱う。
    if (abs(normalDot) >= threshold)
        return false;

    float depth = GetStageSurfaceDepth(positionWS);
    return depth >= -0.0001 && depth <= _StageBandWidth;
}

// GBuffer・Forward・Metaで共通の表面情報を生成する。
void InitializeStageSurfaceData(
    float2 uv,
    float3 positionWS,
    float3 normalWS,
    out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;

    // 表面と内部で使用する色を切り替える。
    if (IsStageSurfaceArea(positionWS, normalWS))
    {
        half3 textureColor = SampleStageSurfaceTexture(positionWS, normalWS);
        surfaceData.albedo = textureColor * _StageSurfaceColor.rgb;
    }
    else
    {
        surfaceData.albedo = _StageInnerColor.rgb;
    }

    // URP Litで使用する表面情報。
    surfaceData.alpha = 1.0;
    surfaceData.metallic = _Metallic;
    surfaceData.specular = half3(0.0, 0.0, 0.0);
    surfaceData.smoothness = _Smoothness;
    surfaceData.normalTS = half3(0.0, 0.0, 1.0);
    surfaceData.occlusion = 1.0;
    surfaceData.emission = _EmissionColor.rgb;
}

#endif
