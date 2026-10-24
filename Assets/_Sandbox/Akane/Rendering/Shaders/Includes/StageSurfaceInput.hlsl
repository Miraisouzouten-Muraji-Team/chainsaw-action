#ifndef STAGE_SURFACE_INPUT_INCLUDED
#define STAGE_SURFACE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

// ステージに使用するテクスチャ。
TEXTURE2D(_StageSurfaceMap);
SAMPLER(sampler_StageSurfaceMap);

// Editorで作成した地形の外周距離画像。全地形で同じ値を参照する。
TEXTURE2D(_StageEdgeDistanceTex);
SAMPLER(sampler_StageEdgeDistanceTex);
float4 _StageMaskUvTransform; // (原点X, 原点Y, 1/幅, 1/高さ)
float _StageMaskMaxDistance;  // 距離画像のR=1に相当するワールド距離
float _StageMaskEnabled;      // 距離画像が設定されている場合のみ1

// SRP Batcher対応のため、マテリアルごとの値は同じCBUFFERにまとめる。
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
CBUFFER_END

#endif
