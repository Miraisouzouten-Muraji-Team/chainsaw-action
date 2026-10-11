using UnityEngine;

/// <summary>
/// チェンソーの刃を表す、ある1時点の実測位置を保持する。
/// </summary>
public readonly struct ChainsawBladeTrajectorySample
{
    public Vector3 TipPosition { get; }
    public Vector3 RootPosition { get; }
    public double SampleTime { get; }

    public ChainsawBladeTrajectorySample(
        Vector3 tipPosition,
        Vector3 rootPosition,
        double sampleTime)
    {
        TipPosition = tipPosition;
        RootPosition = rootPosition;
        SampleTime = sampleTime;
    }
}
