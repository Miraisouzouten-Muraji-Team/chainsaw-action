using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 1つのチェンソー通常攻撃AnimationからEditorでBakeした軌跡を保持するAsset。
/// </summary>
/// <remarks>
/// Tip / Root位置はPlayer Rootそのものではなく、Bake時に指定した軌跡基準Transformから見た
/// ローカル座標として保存する。
/// RuntimeではTrajectory Reference Pathを使って現在のPlayer階層から同じ基準Transformを取得し、
/// World空間の<see cref="ChainsawAttackTrajectory"/>へ復元する。
///
/// Animationのサンプリング、Enemy Mesh切断、攻撃判定、Player制御は担当しない。
/// </remarks>
[CreateAssetMenu(
    fileName = "ChainsawAttackTrajectoryProfile",
    menuName = "Chainsaw/Attack Trajectory Profile")]
public sealed class ChainsawAttackTrajectoryProfile : ScriptableObject
{
    [Header("対応攻撃")]
    [Tooltip("この軌跡を使用する通常攻撃のAttackData。Enemy側は死亡原因のAttackDataと参照一致で照合する。")]
    [SerializeField]
    private AttackData attackData;

    [Tooltip("このProfileをBakeした元AnimationClip。Runtime判定には使用せず、対応関係の確認用に保持する。")]
    [SerializeField]
    private AnimationClip sourceAnimationClip;

    [Header("Bake基準")]
    [Tooltip(
        "RuntimeでPlayer Tagが付くPlayer Rootから、Bake時の軌跡基準Transformまでの相対Hierarchy Path。" +
        "空文字の場合はPlayer Root自体を基準にする。")]
    [SerializeField]
    private string trajectoryReferencePath = string.Empty;

    [Header("Bake済み軌跡")]
    [SerializeField]
    private List<ChainsawBladeTrajectorySample> samples =
        new List<ChainsawBladeTrajectorySample>();

    public AttackData AttackData => attackData;
    public AnimationClip SourceAnimationClip => sourceAnimationClip;
    public string TrajectoryReferencePath => trajectoryReferencePath;
    public IReadOnlyList<ChainsawBladeTrajectorySample> Samples => samples;
    public int SampleCount => samples?.Count ?? 0;
    public bool HasValidTrajectory => samples != null && samples.Count >= 2;

#if UNITY_EDITOR
    /// <summary>
    /// Editor Bake結果でProfileを更新する。
    /// Runtimeから軌跡を書き換えないようEditorビルド時だけ公開する。
    /// </summary>
    public void SetBakedData(
        AttackData sourceAttackData,
        AnimationClip sourceClip,
        string referencePath,
        IReadOnlyList<ChainsawBladeTrajectorySample> sourceSamples)
    {
        attackData = sourceAttackData;
        sourceAnimationClip = sourceClip;
        trajectoryReferencePath = referencePath ?? string.Empty;

        samples.Clear();

        if (sourceSamples == null)
        {
            return;
        }

        for (int i = 0; i < sourceSamples.Count; i++)
        {
            samples.Add(sourceSamples[i]);
        }
    }
#endif
}
