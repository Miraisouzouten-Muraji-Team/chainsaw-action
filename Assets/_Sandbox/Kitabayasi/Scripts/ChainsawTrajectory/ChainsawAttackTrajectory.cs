using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// 1回のチェンソー攻撃について確定した軌跡サンプルを保持する。
/// PlayerAttackHitDataで送るため
/// </summary>
/// /// <remarks>
/// Recorderの記録中Listから生成される確定済みスナップショット。
/// 生成時にサンプルを別のListへコピーするため、
/// 生成後にRecorder側の記録内容が変更されてもこの軌跡は変更されない。
/// Enemy側のメッシュ切断処理へ渡すデータとして使用する。
/// </remarks>
public sealed class ChainsawAttackTrajectory
{
    // 外部から変更されないように保持する、読み取り専用の軌跡サンプル一覧。
    private readonly ReadOnlyCollection<ChainsawBladeTrajectorySample> samples;

    /// <summary>
    /// 記録された軌跡サンプルを取得する。
    /// </summary>
    public IReadOnlyList<ChainsawBladeTrajectorySample> Samples => samples;

    /// <summary>
    /// 記録されている軌跡サンプル数を取得する。
    /// </summary>
    public int SampleCount => samples.Count;

    /// <summary>
    /// 軌跡サンプルから、変更できない攻撃軌跡データを作成する。
    /// </summary>
    /// <param name="sourceSamples">
    /// 元となる軌跡サンプル一覧。
    /// </param>
    public ChainsawAttackTrajectory(
        IReadOnlyList<ChainsawBladeTrajectorySample> sourceSamples)
    {
        // nullが渡された場合は軌跡を作れないためエラーにする。
        if (sourceSamples == null)
        {
            throw new ArgumentNullException(nameof(sourceSamples));
        }

        // 渡されたリストをそのまま保持せず、
        // このクラス専用のリストとしてコピーする。
        var copiedSamples =
            new List<ChainsawBladeTrajectorySample>(sourceSamples.Count);

        // すべての軌跡サンプルを新しいリストへコピーする。
        for (int i = 0; i < sourceSamples.Count; i++)
        {
            copiedSamples.Add(sourceSamples[i]);
        }

        // コピーしたリストを読み取り専用にして保持する。
        samples = copiedSamples.AsReadOnly();
    }
}
