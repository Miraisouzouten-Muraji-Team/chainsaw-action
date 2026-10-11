using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 攻撃中のチェンソー刃のTip / Root位置を時系列で記録する。
/// </summary>
/// <remarks>
/// チェンソー側へアタッチして使用する軌跡記録コンポーネント。
///
/// Inspector設定:
/// ・Tip Transform:
///   チェンソーの刃先位置を表すTransformを設定する。
/// ・Root Transform:
///   チェンソーの刃の根元位置を表すTransformを設定する。
///
/// 呼び出し側が行う処理:
///
/// 攻撃開始:
///     BeginRecording()
///
/// 攻撃中:
///     RecordSample()
///
/// Enemyへの命中時:
///     TryCreateTrajectorySnapshot(out ChainsawAttackTrajectory trajectory)
///
/// 攻撃終了:
///     EndRecording()
///
/// 重要:
/// このコンポーネント自身はUpdate / FixedUpdate等から自動でRecordSample()を呼ばない。
/// Player / 武器側で、チェンソーのTransformが更新された適切なタイミングに
/// RecordSample()を呼ぶこと。
///
/// 命中時は原則としてEndRecording()ではなくTryCreateTrajectorySnapshot()を使用する。
/// TryCreateTrajectorySnapshot()はその時点までの軌跡を取得するだけで、
/// 現在の攻撃の記録は継続する。
///
/// 生成されたChainsawAttackTrajectoryは記録中のListとは独立した
/// スナップショットであるため、その後RecordSample()が追加されたり、
/// 次のBeginRecording()で内部記録がクリアされたりしても内容は変更されない。
///
/// サンプリング頻度やUpdate / FixedUpdate等のどこから呼ぶかは、
/// 武器を実際に動かしている処理との実行順を考慮してPlayer側で決定すること。
/// </remarks>
///

public class ChainsawBladeTrajectoryRecorder : MonoBehaviour
{
    [Header("刃の位置")]
    [Tooltip("チェンソーの刃先側を表すTransform。")]
    [SerializeField]
    private Transform tipTransform;

    [Tooltip("チェンソーの刃の根元側を表すTransform。")]
    [SerializeField]
    private Transform rootTransform;

    // 現在の攻撃中に記録した軌跡サンプルを順番に保持する。
    private readonly List<ChainsawBladeTrajectorySample> recordingSamples =
        new List<ChainsawBladeTrajectorySample>();

    /// <summary>
    /// 現在、軌跡を記録中かどうか。
    /// </summary>
    public bool IsRecording { get; private set; }

    /// <summary>
    /// 現在記録されているサンプル数。
    /// </summary>
    public int RecordedSampleCount => recordingSamples.Count;

    /// <summary>
    /// 新しい攻撃の軌跡記録を開始する。
    /// </summary>
    /// <returns>
    /// 記録を開始できた場合はtrue。
    /// </returns>
    public bool BeginRecording()
    {
        // Tip / Rootがなければ刃の位置を記録できない。
        if (!HasValidBladeReferences())
        {
            Debug.LogError(
                $"{nameof(ChainsawBladeTrajectoryRecorder)}: " +
                "TipまたはRootのTransformが設定されていません。",
                this);

            return false;
        }

        // 二重に記録開始されることを防ぐ。
        if (IsRecording)
        {
            Debug.LogWarning(
                $"{nameof(ChainsawBladeTrajectoryRecorder)}: " +
                "既に軌跡を記録中です。",
                this);

            return false;
        }

        // 前回の攻撃の軌跡が混ざらないように削除する。
        recordingSamples.Clear();

        // ここからRecordSampleで軌跡を記録できる状態にする。
        IsRecording = true;

        return true;
    }

    /// <summary>
    /// 現在のTip / Root位置を1サンプルとして記録する。
    /// </summary>
    public void RecordSample()
    {
        // 記録期間外なら何もしない。
        if (!IsRecording)
        {
            return;
        }

        // 記録中にTip / Rootの参照が失われた場合は記録を停止する。
        if (!HasValidBladeReferences())
        {
            StopRecordingBecauseReferencesAreMissing();
            return;
        }

        // 現在の刃先と根元のワールド座標を取得する。
        Vector3 tipPosition = tipTransform.position;
        Vector3 rootPosition = rootTransform.position;

        // このサンプルが記録された時刻を保存する。
        double sampleTime = Time.timeAsDouble;

        // 同じ時点のTip / Root / 時刻を1つのサンプルにまとめる。
        var sample = new ChainsawBladeTrajectorySample(
            tipPosition,
            rootPosition,
            sampleTime);

        // 時系列データとして末尾に追加する。
        recordingSamples.Add(sample);
    }

    /// <summary>
    /// 現在までに記録された軌跡のスナップショットを生成する。
    /// Enemyへの命中時など、攻撃途中の現在までの軌跡が必要な場合に使用する。
    /// </summary>
    /// <param name="trajectory">
    /// 生成された軌跡。
    /// </param>
    /// <returns>
    /// 1件以上のサンプルから軌跡を生成できた場合はtrue。
    /// </returns>
    public bool TryCreateTrajectorySnapshot(
        out ChainsawAttackTrajectory trajectory)
    {
        // まだ1件も記録されていなければ軌跡を作れない。
        if (recordingSamples.Count == 0)
        {
            trajectory = null;
            return false;
        }

        // 現時点までのサンプルから独立した軌跡データを作成する。
        // この後もrecordingSamplesへの記録自体は続けられる。
        trajectory = new ChainsawAttackTrajectory(recordingSamples);

        return true;
    }

    /// <summary>
    /// 現在の攻撃の軌跡記録を終了する。
    /// </summary>
    /// <returns>
    /// 攻撃終了時点までに記録された軌跡。
    /// サンプルが存在しない場合はnull。
    /// </returns>
    public ChainsawAttackTrajectory EndRecording()
    {
        // 記録していない場合は終了するものがない。
        if (!IsRecording)
        {
            return null;
        }

        // これ以降RecordSampleでサンプルが追加されないようにする。
        IsRecording = false;

        // サンプルが1件もなければ軌跡は生成しない。
        if (recordingSamples.Count == 0)
        {
            return null;
        }

        // 攻撃終了時点までのサンプルから最終的な軌跡を作成する。
        return new ChainsawAttackTrajectory(recordingSamples);
    }

    /// <summary>
    /// Tip / Rootの両方が設定されているか確認する。
    /// </summary>
    private bool HasValidBladeReferences()
    {
        return tipTransform != null && rootTransform != null;
    }

    /// <summary>
    /// 記録中にTip / Rootの参照が失われた場合、安全のため記録を停止する。
    /// </summary>
    private void StopRecordingBecauseReferencesAreMissing()
    {
        IsRecording = false;

        Debug.LogError(
            $"{nameof(ChainsawBladeTrajectoryRecorder)}: " +
            "記録中にTipまたはRootのTransform参照が失われたため、" +
            "軌跡記録を停止しました。",
            this);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector設定時に、TipとRootへ同じTransformを設定していないか確認する。
    /// </summary>
    private void OnValidate()
    {
        // 同じTransformでは刃を表す2点にならないため設定ミスとして警告する。
        if (tipTransform != null &&
            rootTransform != null &&
            tipTransform == rootTransform)
        {
            Debug.LogWarning(
                $"{nameof(ChainsawBladeTrajectoryRecorder)}: " +
                "TipとRootに同じTransformが設定されています。",
                this);
        }
    }
#endif
}
