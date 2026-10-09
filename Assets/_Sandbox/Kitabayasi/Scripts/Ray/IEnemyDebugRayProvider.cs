using System.Collections.Generic;

/// <summary>
/// Enemy処理が実際に使用したRay情報をデバッグ描画側へ提供する契約。
/// </summary>
/// <remarks>
/// Rayの判定や幾何計算は担当せず、
/// 各Strategy / Componentが判定時に記録した情報だけを提供する。
/// </remarks>
public interface IEnemyDebugRayProvider
{
    /// <summary>
    /// 現在提供可能なRay情報を呼び出し元のバッファへ追加する。
    /// </summary>
    void CollectDebugRays(
        List<EnemyDebugRay> debugRays);
}
