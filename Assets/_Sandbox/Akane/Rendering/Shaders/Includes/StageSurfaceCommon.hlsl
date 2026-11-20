
#ifndef STAGE_SURFACE_COMMON_INCLUDED
#define STAGE_SURFACE_COMMON_INCLUDED

// ステージテクスチャをワールド座標に沿って投影する。
half3 SampleStageSurfaceTexture(
    float3 positionWS,
    float3 normalWS)
{
    float3 tilePosition =
        positionWS / max(_StageTileSize, 0.0001);

    float3 normalWeights = pow(
        abs(normalize(normalWS)),
        max(_StageProjectionSharpness, 1.0));

    normalWeights /= max(
        normalWeights.x +
        normalWeights.y +
        normalWeights.z, 0.0001);

    half3 xColor = SAMPLE_TEXTURE2D(
        _StageSurfaceMap,
        sampler_StageSurfaceMap,
        tilePosition.zy).rgb;

    half3 yColor = SAMPLE_TEXTURE2D(
        _StageSurfaceMap,
        sampler_StageSurfaceMap,
        tilePosition.xz).rgb;

    half3 zColor = SAMPLE_TEXTURE2D(
        _StageSurfaceMap,
        sampler_StageSurfaceMap,
        tilePosition.xy).rgb;

    return
        xColor * normalWeights.x +
        yColor * normalWeights.y +
        zColor * normalWeights.z;
}

// 地形本体と縁メッシュの表面情報を共通化する。
void InitializeStageSurfaceData(
    float2 uv,
    float3 positionWS,
    float3 normalWS,
    out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;

    half3 albedo = _StageInnerColor.rgb;

    // 縁メッシュでは外周距離を計算する必要がない。
    if (_StageEdgeOnly > 0.5)
    {
        albedo = SampleStageSurfaceTexture(
            positionWS, normalWS) *
            _StageSurfaceColor.rgb;
    }

    surfaceData.albedo = albedo;
    surfaceData.alpha = 1.0;
    surfaceData.metallic = _Metallic;
    surfaceData.specular = half3(0.0, 0.0, 0.0);
    surfaceData.smoothness = _Smoothness;
    surfaceData.normalTS = half3(0.0, 0.0, 1.0);
    surfaceData.occlusion = 1.0;
    surfaceData.emission = _EmissionColor.rgb;
}

#endif
