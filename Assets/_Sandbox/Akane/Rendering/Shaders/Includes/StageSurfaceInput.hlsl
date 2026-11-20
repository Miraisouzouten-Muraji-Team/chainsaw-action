
#ifndef STAGE_SURFACE_INPUT_INCLUDED
#define STAGE_SURFACE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

// ステージに投影するテクスチャ。
TEXTURE2D(_StageSurfaceMap);
SAMPLER(sampler_StageSurfaceMap);

// マテリアル単位の値をまとめ、SRP Batcherへ対応する。
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _BaseColor;
half4 _EmissionColor;
half _Metallic;
half _Smoothness;
half _Surface;

half4 _StageSurfaceColor;
half4 _StageInnerColor;

float _StageEdgeWidth;
float _StageTileSize;
float _StageProjectionSharpness;

// 0: 地形本体、1: 縁用メッシュ。
float _StageEdgeOnly;

// 描画状態の切り替え。
float _StageZWrite;
float _StageDepthBiasFactor;
float _StageDepthBiasUnits;
CBUFFER_END

#endif
