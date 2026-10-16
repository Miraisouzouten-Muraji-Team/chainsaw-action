using System;
using UnityEngine;

/// <summary>
/// チェンソーの刃を表す、ある1時点のTip / Root位置を保持する。
/// </summary>
/// <remarks>
/// Bake済みProfileでは軌跡基準Transformから見たローカル座標を保持し、
/// Runtimeの<see cref="ChainsawAttackTrajectory"/>ではWorld座標を保持する。
/// どの空間の値かは、このSampleを所有するデータ側の責務として明確にする。
/// </remarks>
[Serializable]
public struct ChainsawBladeTrajectorySample
{
    [SerializeField]
    private Vector3 tipPosition;

    [SerializeField]
    private Vector3 rootPosition;

    [SerializeField]
    private double sampleTime;

    public Vector3 TipPosition => tipPosition;
    public Vector3 RootPosition => rootPosition;
    public double SampleTime => sampleTime;

    public ChainsawBladeTrajectorySample(
        Vector3 tipPosition,
        Vector3 rootPosition,
        double sampleTime)
    {
        this.tipPosition = tipPosition;
        this.rootPosition = rootPosition;
        this.sampleTime = sampleTime;
    }
}
