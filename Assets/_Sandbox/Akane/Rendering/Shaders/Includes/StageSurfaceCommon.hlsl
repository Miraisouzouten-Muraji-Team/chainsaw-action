#ifndef STAGE_SURFACE_COMMON_INCLUDED
#define STAGE_SURFACE_COMMON_INCLUDED

// Scaleの大きさによらず、ワールド座標でテクスチャを繰り返す。
half3 SampleStageSurfaceTexture(float3 positionWS, float3 normalWS)
{
    float3 tilePosition = positionWS / max(_StageTileSize, 0.0001);
    float3 normalWeights = pow(abs(normalize(normalWS)), max(_StageProjectionSharpness, 1.0));
    normalWeights /= max(normalWeights.x + normalWeights.y + normalWeights.z, 0.0001);

    half3 xColor = SAMPLE_TEXTURE2D(_StageSurfaceMap, sampler_StageSurfaceMap, tilePosition.zy).rgb;
    half3 yColor = SAMPLE_TEXTURE2D(_StageSurfaceMap, sampler_StageSurfaceMap, tilePosition.xz).rgb;
    half3 zColor = SAMPLE_TEXTURE2D(_StageSurfaceMap, sampler_StageSurfaceMap, tilePosition.xy).rgb;

    return xColor * normalWeights.x + yColor * normalWeights.y + zColor * normalWeights.z;
}

// 画面上の地形群を合成した外周からの距離で淵を判定する。
// 距離画像はX/Y平面（サイドビュー）に沿って生成する。
bool IsStageEdgeArea(float3 positionWS)
{
    if (_StageMaskEnabled < 0.5 || _StageMaskMaxDistance <= 0.0)
    {
        return false;
    }

    float2 maskUv = (positionWS.xy - _StageMaskUvTransform.xy) * _StageMaskUvTransform.zw;
    if (any(maskUv < 0.0) || any(maskUv > 1.0))
    {
        return false;
    }

    float distanceNormalized = SAMPLE_TEXTURE2D(
        _StageEdgeDistanceTex, sampler_StageEdgeDistanceTex, maskUv).r;
    float distanceWS = distanceNormalized * _StageMaskMaxDistance;
    return distanceWS <= _StageEdgeWidth;
}

// Deferred / Forward / Metaで使う表面情報を共通化する。
void InitializeStageSurfaceData(
    float2 uv,
    float3 positionWS,
    float3 normalWS,
    out SurfaceData surfaceData)
{
    surfaceData = (SurfaceData)0;

    if (IsStageEdgeArea(positionWS))
    {
        half3 edgeTexture = SampleStageSurfaceTexture(positionWS, normalWS);
        surfaceData.albedo = edgeTexture * _StageSurfaceColor.rgb;
    }
    else
    {
        surfaceData.albedo = _StageInnerColor.rgb;
    }

    surfaceData.alpha = 1.0;
    surfaceData.metallic = _Metallic;
    surfaceData.specular = half3(0.0, 0.0, 0.0);
    surfaceData.smoothness = _Smoothness;
    surfaceData.normalTS = half3(0.0, 0.0, 1.0);
    surfaceData.occlusion = 1.0;
    surfaceData.emission = _EmissionColor.rgb;
}

#endif
