#ifndef DEFERRED_CEL_LIGHTING_INCLUDED
#define DEFERRED_CEL_LIGHTING_INCLUDED

// セルシェーディング共通パラメータ。
// C#側からグローバルShaderパラメータとして設定する。
float _CelEnabled;
float _CelStrength;

float _CelDarkThreshold;
float _CelLightThreshold;

float _CelDarkBrightness;
float _CelMiddleBrightness;
float _CelLightBrightness;

// 法線と光の角度から、直接光のDiffuseを3階調化する。
half GetCelDiffuseFactor(half ndotl)
{
    half darkThreshold = min(_CelDarkThreshold, _CelLightThreshold);
    half lightThreshold = max(_CelDarkThreshold, _CelLightThreshold);

    // 光が面の裏側にある場合は直接光を発生させない。
    if (ndotl <= 0.0001h)
    {
        return 0.0h;
    }

    if (ndotl >= lightThreshold)
    {
        return (half)_CelLightBrightness;
    }

    if (ndotl >= darkThreshold)
    {
        return (half)_CelMiddleBrightness;
    }

    return (half)_CelDarkBrightness;
}

// URP Litの直接光をセルシェーディング対応にする。
// Specularは元のPBR計算を維持する。
half3 LightingCelDeferred(
    BRDFData brdfData,
    Light light,
    half3 normalWS,
    half3 viewDirectionWS,
    bool specularHighlightsOff)
{
    // 既存のURP PBRライティング結果を取得する。
    half3 pbrColor = LightingPhysicallyBased(
        brdfData,
        light,
        normalWS,
        viewDirectionWS,
        specularHighlightsOff);

    if (_CelEnabled < 0.5 || _CelStrength <= 0.0001)
    {
        return pbrColor;
    }

    // 直接光の角度と減衰を取得する。
    half ndotl = saturate(dot(normalWS, light.direction));

    half attenuation =
        light.distanceAttenuation *
        light.shadowAttenuation;

    half3 diffuseBase =
        brdfData.diffuse *
        light.color *
        attenuation;

    // 元の滑らかなDiffuse。
    half3 originalDiffuse = diffuseBase * ndotl;

    // 3階調化したDiffuse。
    half celFactor = GetCelDiffuseFactor(ndotl);
    half3 celDiffuse = diffuseBase * celFactor;

    // Diffuseだけを差し替える。
    // 元のPBR Specularは変更しない。
    return pbrColor + (celDiffuse - originalDiffuse) *
        saturate(_CelStrength);
}

// URP SimpleLitのDiffuseにも同じ3階調を適用する。
half3 LightingCelSimpleDiffuse(
    half3 albedo,
    Light light,
    half3 normalWS)
{
    half ndotl = saturate(dot(normalWS, light.direction));

    half attenuation =
        light.distanceAttenuation *
        light.shadowAttenuation;

    half3 diffuseBase =
        albedo *
        light.color *
        attenuation;

    half3 originalDiffuse = diffuseBase * ndotl;

    if (_CelEnabled < 0.5 || _CelStrength <= 0.0001)
    {
        return originalDiffuse;
    }

    half3 celDiffuse =
        diffuseBase * GetCelDiffuseFactor(ndotl);

    return lerp(
        originalDiffuse,
        celDiffuse,
        saturate(_CelStrength));
}

#endif
