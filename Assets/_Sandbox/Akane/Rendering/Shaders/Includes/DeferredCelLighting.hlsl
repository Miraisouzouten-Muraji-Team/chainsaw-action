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
int _CelStepCount;


// 光の入射角を、既存のDark / Middle / Light境界に合わせて再配置する。
// 3階調では従来のThreshold設定をそのまま使用する。
float RemapCelLightAmount(float ndotl)
{
    float darkThreshold = saturate(min(_CelDarkThreshold, _CelLightThreshold));
    float lightThreshold = saturate(max(_CelDarkThreshold, _CelLightThreshold));

    // 暗部の区間。
    if (ndotl < darkThreshold)
    {
        return (ndotl / max(darkThreshold, 0.0001)) / 3.0;
    }

    // 中間部の区間。
    if (ndotl < lightThreshold)
    {
        float t = (ndotl - darkThreshold) /
            max(lightThreshold - darkThreshold, 0.0001);

        return (1.0 + t) / 3.0;
    }

    // 明部の区間。
    float t = (ndotl - lightThreshold) /
        max(1.0 - lightThreshold, 0.0001);

    return (2.0 + t) / 3.0;
}

// 指定された階調数に応じて直接光のDiffuseを量子化する。
half GetCelDiffuseFactor(half ndotl)
{
    // 光が裏側から当たる面には直接Diffuseを発生させない。
    if (ndotl <= 0.0001h)
    {
        return 0.0h;
    }

    // 階調数を3〜12に制限する。
    int stepCount = clamp(_CelStepCount, 3, 12);

    // 既存のThresholdを考慮して光量を0〜1へ再配置する。
    float lightAmount = RemapCelLightAmount(saturate(ndotl));

    // 光量を指定した階調数に量子化する。
    float stepIndex = min(
        floor(lightAmount * stepCount),
        stepCount - 1.0);

    float stepPosition = stepIndex / (stepCount - 1.0);

    // Dark → Middle → Lightの明るさを階調数に応じて補間する。
    float brightness;

    if (stepPosition <= 0.5)
    {
        brightness = lerp(
            _CelDarkBrightness,
            _CelMiddleBrightness,
            stepPosition * 2.0);
    }
    else
    {
        brightness = lerp(
            _CelMiddleBrightness,
            _CelLightBrightness,
            (stepPosition - 0.5) * 2.0);
    }

    return (half)brightness;
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
