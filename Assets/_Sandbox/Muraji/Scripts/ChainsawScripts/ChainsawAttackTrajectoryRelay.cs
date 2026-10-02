using UnityEngine;

/// <summary>
/// 既存の攻撃処理とChainsawBladeTrajectoryRecorderの間を中継し、
/// 命中時にPlayerAttackHitDataを生成してIAttackHitReceiverへ送る。
/// 軌跡と命中情報の確認用ログも出力する。
/// </summary>
public class ChainsawAttackTrajectoryRelay : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("軌跡を記録するRecorder。")]
    [SerializeField]
    private ChainsawBladeTrajectoryRecorder recorder;

    [Header("デバッグ")]
    [Tooltip("ONにすると、軌跡記録と命中送信の状況をConsoleへ出力する。")]
    [SerializeField]
    private bool enableDebugLog = true;

    [Tooltip("RecordSampleのログを何サンプルごとに出すか。1なら毎回出力する。")]
    [SerializeField, Min(1)]
    private int sampleLogInterval = 10;

    // 今回の攻撃で使用する攻撃データ。
    private ScriptableObject currentAttackData;

    // 今回の攻撃中に呼んだRecordSampleの回数(ログ間引き用)。
    private int fixedSampleCallCount;

    private const string LogPrefix = "[ChainsawTrail] ";

    private void Awake()
    {
        if (recorder == null)
        {
            recorder = GetComponent<ChainsawBladeTrajectoryRecorder>();
        }

        if (recorder == null)
        {
            Debug.LogError(
                LogPrefix + "ChainsawBladeTrajectoryRecorderが見つかりません。",
                this);
        }
    }

    private void FixedUpdate()
    {
        // 攻撃中のみサンプルを記録する。
        if (recorder == null || !recorder.IsRecording)
        {
            return;
        }

        recorder.RecordSample();
        fixedSampleCallCount++;

        if (enableDebugLog && fixedSampleCallCount % sampleLogInterval == 0)
        {
            Debug.Log(
                LogPrefix +
                $"記録中: サンプル数={recorder.RecordedSampleCount}",
                this);
        }
    }

    /// <summary>
    /// 攻撃開始時に呼ぶ。
    /// </summary>
    /// <param name="attackData">今回使用する攻撃のScriptableObject。</param>
    public void OnAttackBegin(ScriptableObject attackData)
    {
        if (recorder == null)
        {
            return;
        }

        currentAttackData = attackData;
        fixedSampleCallCount = 0;

        bool started = recorder.BeginRecording();

        if (enableDebugLog)
        {
            Debug.Log(
                LogPrefix +
                $"攻撃開始: 記録開始={(started ? "成功" : "失敗")}, " +
                $"攻撃データ={(attackData != null ? attackData.name : "null")}",
                this);
        }
    }

    /// <summary>
    /// Enemyへの命中が成立した時に呼ぶ。
    /// </summary>
    /// <param name="hitObject">命中したGameObject。</param>
    public void OnAttackHit(GameObject hitObject)
    {
        if (recorder == null || hitObject == null)
        {
            return;
        }

        // 命中時点までの軌跡を取得する(記録は継続する)。
        if (!recorder.TryCreateTrajectorySnapshot(out ChainsawAttackTrajectory trajectory))
        {
            if (enableDebugLog)
            {
                Debug.LogWarning(
                    LogPrefix +
                    $"命中したが軌跡サンプルが0件のため送信しません: 対象={hitObject.name}",
                    hitObject);
            }

            return;
        }

        // Collider等が子オブジェクトにある場合に備えて親方向も探す。
        IAttackHitReceiver receiver =
            hitObject.GetComponentInParent<IAttackHitReceiver>();

        if (receiver == null)
        {
            if (enableDebugLog)
            {
                Debug.LogWarning(
                    LogPrefix +
                    $"命中対象にIAttackHitReceiverがありません: 対象={hitObject.name}",
                    hitObject);
            }

            return;
        }

        var hitData = new PlayerAttackHitData(currentAttackData, trajectory);

        receiver.ReceiveAttackHit(hitData);

        if (enableDebugLog)
        {
            Debug.Log(
                LogPrefix +
                $"命中情報を送信: 対象={hitObject.name}, " +
                $"攻撃データ={(currentAttackData != null ? currentAttackData.name : "null")}, " +
                $"軌跡サンプル数={trajectory.SampleCount}",
                hitObject);
        }
    }

    /// <summary>
    /// 攻撃終了時に呼ぶ。
    /// </summary>
    public void OnAttackEnd()
    {
        if (recorder == null)
        {
            return;
        }

        ChainsawAttackTrajectory finalTrajectory = recorder.EndRecording();

        if (enableDebugLog)
        {
            Debug.Log(
                LogPrefix +
                (finalTrajectory != null
                    ? $"攻撃終了: 最終サンプル数={finalTrajectory.SampleCount}"
                    : "攻撃終了: 軌跡サンプルなし(または記録していませんでした)"),
                this);
        }

        currentAttackData = null;
    }
}
