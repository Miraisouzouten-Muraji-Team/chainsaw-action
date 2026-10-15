using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>
/// 1回のチェンソー通常攻撃でMesh切断へ使用する、
/// World空間上の確定済み軌跡を保持する。
/// </summary>
/// <remarks>
/// Bake済みの<see cref="ChainsawAttackTrajectoryProfile"/>を
/// 現在のPlayer基準TransformからWorld空間へ復元した後のデータとして使用する。
/// Mesh切断側は軌跡がどのように生成されたかを知らず、この確定済み軌跡だけを受け取る。
/// </remarks>
public sealed class ChainsawAttackTrajectory
{
    private readonly ReadOnlyCollection<ChainsawBladeTrajectorySample> samples;

    /// <summary>
    /// World空間上の確定済み軌跡サンプルを取得する。
    /// </summary>
    public IReadOnlyList<ChainsawBladeTrajectorySample> Samples => samples;

    public int SampleCount => samples.Count;

    public ChainsawAttackTrajectory(
        IReadOnlyList<ChainsawBladeTrajectorySample> sourceSamples)
    {
        if (sourceSamples == null)
        {
            throw new ArgumentNullException(nameof(sourceSamples));
        }

        var copiedSamples =
            new List<ChainsawBladeTrajectorySample>(sourceSamples.Count);

        for (int i = 0; i < sourceSamples.Count; i++)
        {
            copiedSamples.Add(sourceSamples[i]);
        }

        samples = copiedSamples.AsReadOnly();
    }
}
