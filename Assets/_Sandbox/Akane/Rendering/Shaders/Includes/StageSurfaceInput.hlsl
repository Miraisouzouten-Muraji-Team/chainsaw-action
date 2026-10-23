
#ifndef STAGE_SURFACE_INPUT_INCLUDED
#define STAGE_SURFACE_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

// ステージ表面のテクスチャ。
TEXTURE2D(_StageSurfaceMap);
SAMPLER(sampler_StageSurfaceMap);

// Materialごとのパラメータ。
// SRP Batcherに対応するためUnityPerMaterialへまとめる。
CBUFFER_START(UnityPerMaterial)

// URP Litとの互換性に使用する値。
float4 _BaseMap_ST;
half4 _BaseColor;
half4 _EmissionColor;
half _Metallic;
half _Smoothness;
half _Surface;

// ステージ表面と内側の色。
half4 _StageSurfaceColor;
half4 _StageInnerColor;

// Object Spaceにおける表面の向き。
float4 _StageSurfaceAxisOS;

// Object Spaceにおける表面の位置。
float _StageTopOS;

// 表面から内側へ表示する幅（ワールド単位）。
float _StageBandWidth;

// テクスチャを繰り返す間隔（ワールド単位）。
float _StageTileSize;

// Triplanarの投影方向の切り替えの鋭さ。
float _StageProjectionSharpness;

// 面の向きを表面として判定する閾値。
float _StageNormalThreshold;

// 側面にも表面の帯を表示するか。
float _StageSideBandEnabled;

CBUFFER_END

#endif
