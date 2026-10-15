using UnityEngine;

/// <summary>
/// 1回の通常チェンソー命中について、MeshCutに必要なWorld空間の刃情報を保持する。
/// </summary>
/// <remarks>
/// Damage、Animation進行度、Trajectory Profile、切断Plane生成は担当しない。
/// BladeCenterWorldは実際の命中時点のTip / Rootから取得し、
/// BladeDirectionWorldとBladeMovementDirectionWorldは保存済みProfileから解決する。
/// </remarks>
public readonly struct ChainsawCutKinematics
{
    private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.00000001f;

    public Vector3 BladeCenterWorld { get; }
    public Vector3 BladeDirectionWorld { get; }
    public Vector3 BladeMovementDirectionWorld { get; }

    public bool HasValidDirections =>
        BladeDirectionWorld.sqrMagnitude > MIN_DIRECTION_SQR_MAGNITUDE &&
        BladeMovementDirectionWorld.sqrMagnitude > MIN_DIRECTION_SQR_MAGNITUDE;

    public ChainsawCutKinematics(
        Vector3 bladeCenterWorld,
        Vector3 bladeDirectionWorld,
        Vector3 bladeMovementDirectionWorld)
    {
        BladeCenterWorld = bladeCenterWorld;
        BladeDirectionWorld = bladeDirectionWorld;
        BladeMovementDirectionWorld = bladeMovementDirectionWorld;
    }
}
