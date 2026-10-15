using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通常チェンソー攻撃1種類について、Editorで事前生成した完成軌跡を保持する。
/// </summary>
/// <remarks>
/// 軌跡は指定した基準Transformに対するローカル座標で保持する。
/// RuntimeではHit normalizedTime付近の保存済みサンプルから
/// BladeDirection / BladeMovementDirectionを解決する。
///
/// Hit判定、Damage、Runtime軌跡記録、現在のBladeCenter取得、
/// Cutting Plane生成、MeshCutは担当しない。
/// </remarks>
[CreateAssetMenu(
    fileName = "ChainsawAttackTrajectoryProfile",
    menuName = "ChainBlade/Chainsaw/Attack Trajectory Profile")]
public sealed class ChainsawAttackTrajectoryProfile : ScriptableObject
{
    private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.00000001f;

    [Header("生成元")]
    [Tooltip("このProfileを生成した通常攻撃Animation Clip。Runtimeの文字列検索には使用しない。")]
    [SerializeField]
    private AnimationClip sourceAnimationClip;

    [Header("完成軌跡")]
    [Tooltip("各サンプルのAnimation進行位置。0～1で昇順に保存する。")]
    [SerializeField]
    private List<float> normalizedTimes = new List<float>();

    [Tooltip("基準Transformに対する刃先Tipのローカル位置。")]
    [SerializeField]
    private List<Vector3> tipLocalPositions = new List<Vector3>();

    [Tooltip("基準Transformに対する刃元Rootのローカル位置。")]
    [SerializeField]
    private List<Vector3> rootLocalPositions = new List<Vector3>();

    public AnimationClip SourceAnimationClip => sourceAnimationClip;

    public int SampleCount => normalizedTimes != null
        ? normalizedTimes.Count
        : 0;

    /// <summary>
    /// Hit normalizedTime付近の完成軌跡から、World空間の刃方向と移動方向を解決する。
    /// </summary>
    /// <remarks>
    /// ローカルの方向ベクトルを単純に回転変換するのではなく、
    /// 保存した位置をTransformPointしてから差分を取る。
    /// これにより基準TransformのScaleや反転が位置へ反映される構成でも、
    /// 保存軌跡と同じ幾何関係からWorld方向を求める。
    ///
    /// trajectoryReferenceTransformにはBake時と同じ責務の基準Transformを渡すこと。
    /// PlayerのFacingがそのTransformより下位階層だけで行われる構成では、
    /// Player側でFacingを反映できる基準Transformを選定する必要がある。
    /// </remarks>
    public bool TryResolveWorldDirections(
        float normalizedTime,
        Transform trajectoryReferenceTransform,
        out Vector3 bladeDirectionWorld,
        out Vector3 bladeMovementDirectionWorld)
    {
        bladeDirectionWorld = Vector3.zero;
        bladeMovementDirectionWorld = Vector3.zero;

        if (trajectoryReferenceTransform == null ||
            !HasValidSampleLayout())
        {
            return false;
        }

        float clampedNormalizedTime =
            Mathf.Clamp01(normalizedTime);

        FindInterpolationIndices(
            clampedNormalizedTime,
            out int lowerIndex,
            out int upperIndex,
            out float interpolation);

        Vector3 tipLocal = Vector3.Lerp(
            tipLocalPositions[lowerIndex],
            tipLocalPositions[upperIndex],
            interpolation);

        Vector3 rootLocal = Vector3.Lerp(
            rootLocalPositions[lowerIndex],
            rootLocalPositions[upperIndex],
            interpolation);

        Vector3 tipWorld =
            trajectoryReferenceTransform.TransformPoint(
                tipLocal);

        Vector3 rootWorld =
            trajectoryReferenceTransform.TransformPoint(
                rootLocal);

        Vector3 bladeVectorWorld =
            tipWorld - rootWorld;

        if (bladeVectorWorld.sqrMagnitude <=
            MIN_DIRECTION_SQR_MAGNITUDE)
        {
            return false;
        }

        int nearestIndex = ResolveNearestSampleIndex(
            clampedNormalizedTime,
            lowerIndex,
            upperIndex);

        ResolveMovementSampleIndices(
            nearestIndex,
            out int previousIndex,
            out int nextIndex);

        Vector3 previousCenterLocal = GetBladeCenterLocal(
            previousIndex);

        Vector3 nextCenterLocal = GetBladeCenterLocal(
            nextIndex);

        Vector3 previousCenterWorld =
            trajectoryReferenceTransform.TransformPoint(
                previousCenterLocal);

        Vector3 nextCenterWorld =
            trajectoryReferenceTransform.TransformPoint(
                nextCenterLocal);

        Vector3 movementVectorWorld =
            nextCenterWorld - previousCenterWorld;

        if (movementVectorWorld.sqrMagnitude <=
            MIN_DIRECTION_SQR_MAGNITUDE)
        {
            return false;
        }

        bladeDirectionWorld =
            bladeVectorWorld.normalized;

        bladeMovementDirectionWorld =
            movementVectorWorld.normalized;

        return true;
    }

    /// <summary>
    /// EditorのBakerから、完成済み軌跡をこのProfileへ置き換える。
    /// </summary>
    /// <remarks>
    /// Runtime gameplayから継続記録するためのAPIではない。
    /// 3つの配列は同数かつnormalizedTime昇順で渡すこと。
    /// </remarks>
    public void SetBakedSamples(
        AnimationClip animationClip,
        IReadOnlyList<float> bakedNormalizedTimes,
        IReadOnlyList<Vector3> bakedTipLocalPositions,
        IReadOnlyList<Vector3> bakedRootLocalPositions)
    {
        if (animationClip == null)
        {
            throw new ArgumentNullException(
                nameof(animationClip));
        }

        if (bakedNormalizedTimes == null)
        {
            throw new ArgumentNullException(
                nameof(bakedNormalizedTimes));
        }

        if (bakedTipLocalPositions == null)
        {
            throw new ArgumentNullException(
                nameof(bakedTipLocalPositions));
        }

        if (bakedRootLocalPositions == null)
        {
            throw new ArgumentNullException(
                nameof(bakedRootLocalPositions));
        }

        int sampleCount =
            bakedNormalizedTimes.Count;

        if (sampleCount < 2 ||
            bakedTipLocalPositions.Count != sampleCount ||
            bakedRootLocalPositions.Count != sampleCount)
        {
            throw new ArgumentException(
                "完成軌跡は2サンプル以上かつ、NormalizedTime / Tip / Rootが同数である必要があります。");
        }

        var copiedNormalizedTimes =
            new List<float>(sampleCount);

        var copiedTipLocalPositions =
            new List<Vector3>(sampleCount);

        var copiedRootLocalPositions =
            new List<Vector3>(sampleCount);

        float previousNormalizedTime = -1.0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float sampleNormalizedTime =
                bakedNormalizedTimes[i];

            if (sampleNormalizedTime < 0.0f ||
                sampleNormalizedTime > 1.0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bakedNormalizedTimes),
                    "NormalizedTimeは0～1の範囲である必要があります。");
            }

            if (i > 0 &&
                sampleNormalizedTime <= previousNormalizedTime)
            {
                throw new ArgumentException(
                    "NormalizedTimeは重複せず昇順である必要があります。",
                    nameof(bakedNormalizedTimes));
            }

            copiedNormalizedTimes.Add(
                sampleNormalizedTime);

            copiedTipLocalPositions.Add(
                bakedTipLocalPositions[i]);

            copiedRootLocalPositions.Add(
                bakedRootLocalPositions[i]);

            previousNormalizedTime =
                sampleNormalizedTime;
        }

        sourceAnimationClip = animationClip;
        normalizedTimes = copiedNormalizedTimes;
        tipLocalPositions = copiedTipLocalPositions;
        rootLocalPositions = copiedRootLocalPositions;
    }

    private bool HasValidSampleLayout()
    {
        if (normalizedTimes == null ||
            tipLocalPositions == null ||
            rootLocalPositions == null ||
            normalizedTimes.Count < 2 ||
            tipLocalPositions.Count != normalizedTimes.Count ||
            rootLocalPositions.Count != normalizedTimes.Count)
        {
            return false;
        }

        float previousNormalizedTime = -1.0f;

        for (int i = 0; i < normalizedTimes.Count; i++)
        {
            float currentNormalizedTime =
                normalizedTimes[i];

            if (currentNormalizedTime < 0.0f ||
                currentNormalizedTime > 1.0f ||
                (i > 0 &&
                 currentNormalizedTime <= previousNormalizedTime))
            {
                return false;
            }

            previousNormalizedTime =
                currentNormalizedTime;
        }

        return true;
    }

    private void FindInterpolationIndices(
        float normalizedTime,
        out int lowerIndex,
        out int upperIndex,
        out float interpolation)
    {
        int lastIndex =
            normalizedTimes.Count - 1;

        if (normalizedTime <= normalizedTimes[0])
        {
            lowerIndex = 0;
            upperIndex = 0;
            interpolation = 0.0f;
            return;
        }

        if (normalizedTime >= normalizedTimes[lastIndex])
        {
            lowerIndex = lastIndex;
            upperIndex = lastIndex;
            interpolation = 0.0f;
            return;
        }

        upperIndex = 1;

        while (upperIndex < normalizedTimes.Count &&
               normalizedTimes[upperIndex] < normalizedTime)
        {
            upperIndex++;
        }

        lowerIndex = upperIndex - 1;

        float lowerTime =
            normalizedTimes[lowerIndex];

        float upperTime =
            normalizedTimes[upperIndex];

        interpolation = Mathf.InverseLerp(
            lowerTime,
            upperTime,
            normalizedTime);
    }

    private int ResolveNearestSampleIndex(
        float normalizedTime,
        int lowerIndex,
        int upperIndex)
    {
        if (lowerIndex == upperIndex)
        {
            return lowerIndex;
        }

        float lowerDistance = Mathf.Abs(
            normalizedTime - normalizedTimes[lowerIndex]);

        float upperDistance = Mathf.Abs(
            normalizedTimes[upperIndex] - normalizedTime);

        return lowerDistance <= upperDistance
            ? lowerIndex
            : upperIndex;
    }

    private void ResolveMovementSampleIndices(
        int currentIndex,
        out int previousIndex,
        out int nextIndex)
    {
        int lastIndex =
            normalizedTimes.Count - 1;

        if (currentIndex <= 0)
        {
            previousIndex = 0;
            nextIndex = 1;
            return;
        }

        if (currentIndex >= lastIndex)
        {
            previousIndex = lastIndex - 1;
            nextIndex = lastIndex;
            return;
        }

        previousIndex = currentIndex - 1;
        nextIndex = currentIndex + 1;
    }

    private Vector3 GetBladeCenterLocal(
        int sampleIndex)
    {
        return (
            tipLocalPositions[sampleIndex] +
            rootLocalPositions[sampleIndex]) * 0.5f;
    }
}
