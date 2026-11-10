#ifndef PIXEL_OPAQUE_SELECTION_INCLUDED
#define PIXEL_OPAQUE_SELECTION_INCLUDED

// 背景や描画対象のないDepthを判定する。
bool IsPixelSkyDepth(float rawDepth)
{
    #if UNITY_REVERSED_Z
        return rawDepth <= 0.0001;
    #else
        return rawDepth >= 0.9999;
    #endif
}

// 候補が現在のDepthより手前にあるか判定する。
bool IsPixelDepthCloser(float candidateDepth, float currentDepth)
{
    #if UNITY_REVERSED_Z
        return candidateDepth > currentDepth;
    #else
        return candidateDepth < currentDepth;
    #endif
}

// Depthを比較して最も手前の描画グループを更新する。
void TrySelectOpaque(float candidateDepth, int candidateIndex, inout float selectedDepth, inout int selectedIndex)
{
    if (IsPixelSkyDepth(candidateDepth)) return;

    if (selectedIndex < 0 || IsPixelDepthCloser(candidateDepth, selectedDepth))
    {
        selectedDepth = candidateDepth;
        selectedIndex = candidateIndex;
    }
}

// Near=0、Middle=1、Far=2、描画対象なし=-1を返す。
int SelectOpaqueLayer(float nearDepth, float middleDepth, float farDepth, out float selectedDepth)
{
    #if UNITY_REVERSED_Z
        selectedDepth = 0.0;
    #else
        selectedDepth = 1.0;
    #endif

    int selectedIndex = -1;

    // FarからNearへDepthを比較する。
    TrySelectOpaque(farDepth, 2, selectedDepth, selectedIndex);
    TrySelectOpaque(middleDepth, 1, selectedDepth, selectedIndex);
    TrySelectOpaque(nearDepth, 0, selectedDepth, selectedIndex);

    return selectedIndex;
}

#endif