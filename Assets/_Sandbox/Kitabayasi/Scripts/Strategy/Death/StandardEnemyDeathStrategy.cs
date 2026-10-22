using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通常Enemyで共通して使用する死亡処理を担当する。
/// </summary>
/// <remarks>
/// 死亡原因となった攻撃がEnemyMeshCutDeathEffectのTriggerAttackDataと一致する場合は、
/// 可変数Fragmentへ分割して吹き飛ばすMeshCut死亡演出を優先する。
/// 食い込み用MeshCut死亡演出の対象外で、かつ死亡原因AttackDataに対応する
/// ChainsawAttackTrajectoryProfileが設定されている場合は、
/// Bake済み軌跡をWorld空間へ復元してEnemyMeshCutterによるMesh分割死亡演出を行う。
///
/// 死亡開始時にはEnemy本体と子階層に存在していたColliderを無効化する。
/// Mesh分割アルゴリズム、Fragmentの具体的な物理挙動、State遷移、HP管理、
/// Enemy本体の最終破棄は担当しない。
/// </remarks>
[Serializable]
public sealed class StandardEnemyDeathStrategy :
    IEnemyDeathStrategy
{
    private const float COORDINATE_ROUND_TRIP_WARNING_THRESHOLD = 0.001f;
    private const float SCALE_DIFFERENCE_WARNING_THRESHOLD = 0.001f;

    [Header("通常攻撃Mesh分割")]
    [Tooltip(
        "Slash1 / Slash2 / Slash3など、通常攻撃ごとのBake済み軌跡Profile。" +
        "死亡原因のAttackDataとProfile内のAttackDataを参照一致で照合する。")]
    [SerializeField]
    private ChainsawAttackTrajectoryProfile[]
        chainsawAttackTrajectoryProfiles;

    [Header("診断ログ")]
    [Tooltip(
        "Bake済み軌跡の選択・World復元・EnemyMeshCutter・Shrink開始までを段階的にConsoleへ出力する。" +
        "原因特定後は無効化してよい。")]
    [SerializeField]
    private bool enableMeshCutDiagnosticLogs = true;

    [Tooltip(
        "Bake済みLocal座標→Runtime World座標→Local座標の往復誤差と、" +
        "Bake時/Runtime時の基準Transform、現在のChainsaw Tip / Rootとの距離を出力する。")]
    [SerializeField]
    private bool enableTrajectoryCoordinateValidation = true;

    [Tooltip(
        "Runtimeで復元したBake軌跡をDebug.DrawLineでScene Viewへ表示する。" +
        "Game Viewで見る場合はGizmos表示を有効にする。")]
    [SerializeField]
    private bool drawReconstructedTrajectory = true;

    [Tooltip("復元軌跡をScene Viewへ残す秒数。")]
    [Min(0.0f)]
    [SerializeField]
    private float reconstructedTrajectoryDrawDuration = 10.0f;

    private EnemyData enemyData;

    private EnemyAttackHitReceiver
        enemyAttackHitReceiver;

    private EnemyMeshCutDeathEffect
        enemyMeshCutDeathEffect;

    private EnemyMeshCutter
        enemyMeshCutter;

    private EnemyCutPieceBurst
        enemyCutPieceBurst;

    private EnemyCutPieceShrink
        enemyCutPieceShrink;

    private Collider[] enemyColliders;

    private bool isDeathActive;
    private bool isMeshCutDeathEffectActive;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject)
    {
        this.enemyData = enemyData
            ? enemyData
            : throw new ArgumentNullException(
                nameof(enemyData));

        if (!enemyObject)
        {
            throw new ArgumentNullException(
                nameof(enemyObject));
        }

        enemyAttackHitReceiver =
            enemyObject.GetComponent<
                EnemyAttackHitReceiver>();

        enemyMeshCutDeathEffect =
            enemyObject.GetComponent<
                EnemyMeshCutDeathEffect>();

        enemyMeshCutter =
            enemyObject.GetComponent<
                EnemyMeshCutter>();

        enemyCutPieceBurst =
            enemyObject.GetComponent<
                EnemyCutPieceBurst>();

        enemyCutPieceShrink =
            enemyObject.GetComponent<
                EnemyCutPieceShrink>();

        // 死亡時に生成される切断片は対象にせず、
        // 死亡前から存在するColliderだけを無効化する。
        enemyColliders =
            enemyObject.GetComponentsInChildren<
                Collider>(
                true);

        if (enemyAttackHitReceiver == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                $"{nameof(EnemyAttackHitReceiver)}が見つかりません。" +
                "死亡原因となった攻撃情報を取得できないため、" +
                "メッシュ分割死亡演出は実行されません。",
                enemyObject);
        }

        if (enemyMeshCutter == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                $"{nameof(EnemyMeshCutter)}が見つかりません。" +
                "既存のMesh分割死亡演出へのフォールバックは実行されません。",
                enemyObject);
        }

        if (enemyCutPieceBurst == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                $"{nameof(EnemyCutPieceBurst)}が見つかりません。" +
                "通常攻撃Mesh分割後の吹き飛ばしは実行されません。",
                enemyObject);
        }

        if (enemyCutPieceShrink == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                $"{nameof(EnemyCutPieceShrink)}が見つかりません。" +
                "既存メッシュ分割後の縮小は実行されません。",
                enemyObject);
        }
    }

    public void BeginDeath()
    {
        isDeathActive = true;

        isMeshCutDeathEffectActive =
            false;

        DisableEnemyColliders();

        if (TryBeginMeshCutDeathEffect())
        {
            return;
        }

        TryCutDeathMesh();
    }

    public bool UpdateDeath()
    {
        if (!isDeathActive)
        {
            return false;
        }

        if (isMeshCutDeathEffectActive)
        {
            return
                enemyMeshCutDeathEffect == null ||
                !enemyMeshCutDeathEffect.IsPlaying;
        }

        bool hasCompletedShrink =
            enemyCutPieceShrink == null ||
            !enemyCutPieceShrink.IsShrinkActive;

        return
            hasCompletedShrink;
    }

    public void EndDeath()
    {
        isDeathActive = false;

        if (isMeshCutDeathEffectActive)
        {
            enemyMeshCutDeathEffect
                ?.StopMeshCut();

            isMeshCutDeathEffectActive =
                false;

            return;
        }

        enemyCutPieceShrink?.StopShrink();
    }

    private void DisableEnemyColliders()
    {
        if (enemyColliders == null)
        {
            return;
        }

        foreach (
            Collider enemyCollider
            in enemyColliders)
        {
            if (enemyCollider != null)
            {
                enemyCollider.enabled =
                    false;
            }
        }
    }

    /// <summary>
    /// 死亡原因のAttackDataが設定済みTriggerAttackDataと一致する場合だけ
    /// MeshCut死亡演出を開始する。
    /// </summary>
    /// <remarks>
    /// EnemyAttackHitReceiverはTakeDamageより先にLastPlayerAttackHitDataを保存するため、
    /// HP0のDied通知から即座にDeath Stateへ入っても死亡原因となった攻撃を参照できる。
    /// </remarks>
    private bool TryBeginMeshCutDeathEffect()
    {
        if (enemyAttackHitReceiver == null ||
            enemyMeshCutDeathEffect == null)
        {
            return false;
        }

        PlayerAttackHitData
            lastPlayerAttackHitData =
                enemyAttackHitReceiver
                    .LastPlayerAttackHitData;

        if (lastPlayerAttackHitData == null)
        {
            return false;
        }

        AttackData triggerAttackData =
            enemyMeshCutDeathEffect
                .TriggerAttackData;

        if (triggerAttackData == null ||
            lastPlayerAttackHitData.AttackData !=
            triggerAttackData)
        {
            return false;
        }

        if (!enemyMeshCutDeathEffect
                .TryBeginMeshCut())
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                "MeshCut死亡演出を開始できなかったため、" +
                "既存のMesh分割死亡演出へフォールバックします。");

            return false;
        }

        isMeshCutDeathEffectActive =
            true;

        return true;
    }

    /// <summary>
    /// 死亡原因のAttackDataに対応するBake済みチェンソー軌跡をWorld空間へ復元し、
    /// 既存のMesh分割死亡演出を開始する。
    /// </summary>
    private void TryCutDeathMesh()
    {
        LogMeshCutDiagnostic(
            "Death-01 Start",
            "通常攻撃Mesh分割死亡演出の開始判定に入りました。");

        if (enemyAttackHitReceiver == null ||
            enemyMeshCutter == null)
        {
            LogMeshCutDiagnosticWarning(
                "Death-01 Start",
                $"必要Componentが不足しています。" +
                $" EnemyAttackHitReceiver={(enemyAttackHitReceiver != null)}," +
                $" EnemyMeshCutter={(enemyMeshCutter != null)}");
            return;
        }

        PlayerAttackHitData
            lastPlayerAttackHitData =
                enemyAttackHitReceiver
                    .LastPlayerAttackHitData;

        if (lastPlayerAttackHitData == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                "死亡原因となったPlayerAttackHitDataがありません。" +
                "メッシュ分割は実行されません。");

            LogMeshCutDiagnosticWarning(
                "Death-02 HitData",
                "LastPlayerAttackHitData=nullのため停止しました。");

            return;
        }

        ScriptableObject attackData =
            lastPlayerAttackHitData.AttackData;

        LogMeshCutDiagnostic(
            "Death-02 HitData",
            attackData == null
                ? "死亡原因AttackData=null"
                : $"死亡原因AttackData='{attackData.name}' ({attackData.GetType().Name})");

        ChainsawAttackTrajectoryProfile trajectoryProfile =
            FindTrajectoryProfile(
                attackData);

        if (trajectoryProfile == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                "死亡原因のAttackDataに対応する" +
                $"{nameof(ChainsawAttackTrajectoryProfile)}がありません。" +
                "通常攻撃のメッシュ切断は実行されません。");

            LogMeshCutDiagnosticWarning(
                "Death-03 Profile",
                "対応Profileを一意に解決できなかったため停止しました。");

            return;
        }

        LogMeshCutDiagnostic(
            "Death-03 Profile",
            $"Profile='{trajectoryProfile.name}', " +
            $"SourceClip='{(trajectoryProfile.SourceAnimationClip != null ? trajectoryProfile.SourceAnimationClip.name : "null")}', " +
            $"Samples={trajectoryProfile.SampleCount}, " +
            $"ReferencePath='{trajectoryProfile.TrajectoryReferencePath}'");

        if (!trajectoryProfile.HasValidTrajectory)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                $"{trajectoryProfile.name}に有効なBake済み軌跡がありません。" +
                "2サンプル以上の軌跡をBakeしてください。");

            LogMeshCutDiagnosticWarning(
                "Death-03 Profile",
                $"Bake済みSample数={trajectoryProfile.SampleCount}のため停止しました。");

            return;
        }

        if (!enemyAttackHitReceiver
                .TryGetPlayerTransform(
                    out Transform playerTransform))
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                "Player Transformを取得できないため、" +
                "Bake済み軌跡をWorld空間へ復元できません。");

            LogMeshCutDiagnosticWarning(
                "Death-04 Reference",
                "Player Transformを取得できなかったため停止しました。");

            return;
        }

        if (!TryResolveTrajectoryReferenceTransform(
                playerTransform,
                trajectoryProfile,
                out Transform trajectoryReferenceTransform))
        {
            LogMeshCutDiagnosticWarning(
                "Death-04 Reference",
                "Bake時のTrajectory Reference TransformをRuntime階層から解決できなかったため停止しました。");
            return;
        }

        LogMeshCutDiagnostic(
            "Death-04 Reference",
            $"Player='{playerTransform.name}', " +
            $"TrajectoryReference='{trajectoryReferenceTransform.name}', " +
            $"Position={FormatVector3(trajectoryReferenceTransform.position)}, " +
            $"RotationEuler={FormatVector3(trajectoryReferenceTransform.rotation.eulerAngles)}, " +
            $"LossyScale={FormatVector3(trajectoryReferenceTransform.lossyScale)}");

        ChainsawAttackTrajectory worldTrajectory =
            CreateWorldTrajectory(
                trajectoryProfile,
                trajectoryReferenceTransform);

        if (enableTrajectoryCoordinateValidation)
        {
            ValidateTrajectoryCoordinateConversion(
                playerTransform,
                trajectoryReferenceTransform,
                trajectoryProfile,
                worldTrajectory);
        }

        if (drawReconstructedTrajectory)
        {
            DrawTrajectoryCoordinateValidation(
                playerTransform,
                trajectoryProfile,
                worldTrajectory);
        }

        LogMeshCutDiagnostic(
            "Death-05 WorldTrajectory",
            BuildWorldTrajectorySummary(
                worldTrajectory));

        bool cutSucceeded =
            enemyMeshCutter.TryCut(
                worldTrajectory);

        if (!cutSucceeded)
        {
            LogMeshCutDiagnosticWarning(
                "Death-06 CutterResult",
                $"{nameof(EnemyMeshCutter)}.TryCutがfalseを返しました。" +
                " 詳細な停止地点は[Cutter-xx]ログとEnemyMeshCutterのfailureReasonを確認してください。");
            return;
        }

        LogMeshCutDiagnostic(
            "Death-06 CutterResult",
            "EnemyMeshCutter.TryCutが成功しました。切断片Effects開始へ進みます。");

        TryBeginCutPieceEffects();
    }

    private ChainsawAttackTrajectoryProfile FindTrajectoryProfile(
        ScriptableObject attackData)
    {
        if (attackData == null ||
            chainsawAttackTrajectoryProfiles == null)
        {
            return null;
        }

        ChainsawAttackTrajectoryProfile matchedProfile =
            null;

        for (int i = 0;
             i < chainsawAttackTrajectoryProfiles.Length;
             i++)
        {
            ChainsawAttackTrajectoryProfile profile =
                chainsawAttackTrajectoryProfiles[i];

            if (profile == null ||
                profile.AttackData != attackData)
            {
                continue;
            }

            if (matchedProfile == null ||
                matchedProfile == profile)
            {
                matchedProfile = profile;
                continue;
            }

            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                $"AttackData '{attackData.name}' に複数のTrajectory Profileが対応しています。" +
                "Slashを一意に判別できないため、メッシュ切断は実行しません。");

            return null;
        }

        return matchedProfile;
    }

    /// <summary>
    /// Bake時に保存したPlayer RootからのHierarchy Pathを使い、
    /// Runtimeの軌跡基準Transformを取得する。
    /// </summary>
    private bool TryResolveTrajectoryReferenceTransform(
        Transform playerTransform,
        ChainsawAttackTrajectoryProfile trajectoryProfile,
        out Transform trajectoryReferenceTransform)
    {
        string referencePath =
            trajectoryProfile.TrajectoryReferencePath;

        if (string.IsNullOrEmpty(referencePath))
        {
            trajectoryReferenceTransform =
                playerTransform;

            return true;
        }

        trajectoryReferenceTransform =
            playerTransform.Find(
                referencePath);

        if (trajectoryReferenceTransform != null)
        {
            return true;
        }

        Debug.LogWarning(
            $"{nameof(StandardEnemyDeathStrategy)}: " +
            $"Player階層から軌跡基準Transform '{referencePath}' を取得できません。" +
            "Player PrefabのHierarchyを変更した場合は軌跡を再Bakeしてください。");

        return false;
    }

    /// <summary>
    /// Profile内のローカル軌跡を、死亡時点のPlayer基準TransformからWorld空間へ復元する。
    /// </summary>
    private static ChainsawAttackTrajectory CreateWorldTrajectory(
        ChainsawAttackTrajectoryProfile trajectoryProfile,
        Transform trajectoryReferenceTransform)
    {
        var worldSamples =
            new List<ChainsawBladeTrajectorySample>(
                trajectoryProfile.SampleCount);

        IReadOnlyList<ChainsawBladeTrajectorySample> localSamples =
                trajectoryProfile.Samples;

        for (int i = 0; i < localSamples.Count; i++)
        {
            ChainsawBladeTrajectorySample localSample =
                localSamples[i];

            Vector3 worldTipPosition =
                trajectoryReferenceTransform.TransformPoint(
                    localSample.TipPosition);

            Vector3 worldRootPosition =
                trajectoryReferenceTransform.TransformPoint(
                    localSample.RootPosition);

            worldSamples.Add(
                new ChainsawBladeTrajectorySample(
                    worldTipPosition,
                    worldRootPosition,
                    localSample.SampleTime));
        }

        return new ChainsawAttackTrajectory(
            worldSamples);
    }

    /// <summary>
    /// Bake済みLocal座標をRuntimeのTrajectory ReferenceでWorldへ復元した結果を検証する。
    /// </summary>
    /// <remarks>
    /// 同じTransformでLocal→World→Localを往復した誤差だけでなく、
    /// Bake時に記録した基準情報とRuntimeの基準Transform、現在のChainsaw Tip / Root、
    /// Enemy位置に対する復元軌跡の距離も出力する。
    /// これにより「変換式そのもの」と「別Transformを基準にしている問題」を分けて確認できる。
    /// </remarks>
    private void ValidateTrajectoryCoordinateConversion(
        Transform playerTransform,
        Transform trajectoryReferenceTransform,
        ChainsawAttackTrajectoryProfile trajectoryProfile,
        ChainsawAttackTrajectory worldTrajectory)
    {
        if (playerTransform == null ||
            trajectoryReferenceTransform == null ||
            trajectoryProfile == null ||
            worldTrajectory == null ||
            trajectoryProfile.SampleCount == 0 ||
            worldTrajectory.SampleCount == 0)
        {
            LogMeshCutDiagnosticWarning(
                "CoordinateValidation",
                "座標変換検証に必要な参照またはサンプルが不足しています。");
            return;
        }

        bool hasCoordinateDiagnosticMetadata =
            trajectoryProfile.HasCoordinateDiagnosticMetadata;

        if (!hasCoordinateDiagnosticMetadata)
        {
            LogMeshCutDiagnosticWarning(
                "CoordinateValidation",
                "Profileに今回追加した座標変換診断用Bake情報がありません。" +
                "ChainsawAttackTrajectoryBakerでこのProfileを再Bakeしてください。" +
                "Local→World→Localの往復誤差は確認できますが、Bake/Runtime基準比較と現在のTip/Root比較は行いません。");
        }

        int sampleCount =
            Mathf.Min(
                trajectoryProfile.SampleCount,
                worldTrajectory.SampleCount);

        float maximumTipLocalRoundTripError = 0.0f;
        float maximumRootLocalRoundTripError = 0.0f;

        Vector3 worldMinimum =
            new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);

        Vector3 worldMaximum =
            new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);

        Vector3 enemyPosition =
            enemyMeshCutter != null
                ? enemyMeshCutter.transform.position
                : Vector3.zero;

        float nearestEnemyCenterDistance =
            float.PositiveInfinity;

        int nearestEnemySampleIndex = -1;

        for (int i = 0; i < sampleCount; i++)
        {
            ChainsawBladeTrajectorySample localSample =
                trajectoryProfile.Samples[i];

            ChainsawBladeTrajectorySample worldSample =
                worldTrajectory.Samples[i];

            Vector3 restoredTipLocalPosition =
                trajectoryReferenceTransform.InverseTransformPoint(
                    worldSample.TipPosition);

            Vector3 restoredRootLocalPosition =
                trajectoryReferenceTransform.InverseTransformPoint(
                    worldSample.RootPosition);

            maximumTipLocalRoundTripError =
                Mathf.Max(
                    maximumTipLocalRoundTripError,
                    Vector3.Distance(
                        localSample.TipPosition,
                        restoredTipLocalPosition));

            maximumRootLocalRoundTripError =
                Mathf.Max(
                    maximumRootLocalRoundTripError,
                    Vector3.Distance(
                        localSample.RootPosition,
                        restoredRootLocalPosition));

            worldMinimum =
                Vector3.Min(
                    worldMinimum,
                    worldSample.TipPosition);

            worldMinimum =
                Vector3.Min(
                    worldMinimum,
                    worldSample.RootPosition);

            worldMaximum =
                Vector3.Max(
                    worldMaximum,
                    worldSample.TipPosition);

            worldMaximum =
                Vector3.Max(
                    worldMaximum,
                    worldSample.RootPosition);

            Vector3 bladeCenter =
                GetBladeCenter(
                    worldSample.TipPosition,
                    worldSample.RootPosition);

            float enemyCenterDistance =
                Vector3.Distance(
                    enemyPosition,
                    bladeCenter);

            if (enemyCenterDistance <
                nearestEnemyCenterDistance)
            {
                nearestEnemyCenterDistance =
                    enemyCenterDistance;

                nearestEnemySampleIndex = i;
            }
        }

        bool liveTipResolved = false;
        bool liveRootResolved = false;
        Transform liveTipTransform = null;
        Transform liveRootTransform = null;

        if (hasCoordinateDiagnosticMetadata)
        {
            liveTipResolved =
                TryResolveRelativeTransform(
                    playerTransform,
                    trajectoryProfile.ChainsawTipPath,
                    out liveTipTransform);

            liveRootResolved =
                TryResolveRelativeTransform(
                    playerTransform,
                    trajectoryProfile.ChainsawRootPath,
                    out liveRootTransform);
        }

        string liveBladeSummary =
            BuildLiveBladeComparisonSummary(
                liveTipResolved
                    ? liveTipTransform
                    : null,
                liveRootResolved
                    ? liveRootTransform
                    : null,
                worldTrajectory);

        int middleIndex =
            sampleCount / 2;

        int lastIndex =
            sampleCount - 1;

        string message =
            "[TrajectoryCoordinateValidation][Runtime]\n" +
            $"Profile='{trajectoryProfile.name}', Samples={sampleCount}\n" +
            $"BakePlayerRoot='{trajectoryProfile.BakedPlayerRootName}', " +
            $"RuntimePlayerRoot='{playerTransform.name}', " +
            $"BakePlayerRootScale={FormatVector3(trajectoryProfile.BakedPlayerRootLossyScale)}, " +
            $"RuntimePlayerRootScale={FormatVector3(playerTransform.lossyScale)}\n" +
            $"BakeReference='{trajectoryProfile.BakedTrajectoryReferenceName}', " +
            $"RuntimeReference='{trajectoryReferenceTransform.name}', " +
            $"ReferencePath='{(string.IsNullOrEmpty(trajectoryProfile.TrajectoryReferencePath) ? "<Player Root>" : trajectoryProfile.TrajectoryReferencePath)}', " +
            $"BakeReferenceScale={FormatVector3(trajectoryProfile.BakedTrajectoryReferenceLossyScale)}, " +
            $"RuntimeReferenceScale={FormatVector3(trajectoryReferenceTransform.lossyScale)}\n" +
            $"RoundTripMaxLocalError: Tip={maximumTipLocalRoundTripError:F6}, " +
            $"Root={maximumRootLocalRoundTripError:F6}\n" +
            $"WorldBounds: Min={FormatVector3(worldMinimum)}, " +
            $"Max={FormatVector3(worldMaximum)}, " +
            $"Size={FormatVector3(worldMaximum - worldMinimum)}\n" +
            $"EnemyPosition={FormatVector3(enemyPosition)}, " +
            $"NearestTrajectoryCenterToEnemy: Index={nearestEnemySampleIndex}, " +
            $"Distance={nearestEnemyCenterDistance:F4}\n" +
            $"LiveTipPath='{trajectoryProfile.ChainsawTipPath}', Resolved={liveTipResolved}, " +
            $"LiveRootPath='{trajectoryProfile.ChainsawRootPath}', Resolved={liveRootResolved}\n" +
            $"{liveBladeSummary}\n" +
            $"First: {BuildRuntimeSampleDiagnostic(0, trajectoryProfile, worldTrajectory, trajectoryReferenceTransform)}\n" +
            $"Middle: {BuildRuntimeSampleDiagnostic(middleIndex, trajectoryProfile, worldTrajectory, trajectoryReferenceTransform)}\n" +
            $"Last: {BuildRuntimeSampleDiagnostic(lastIndex, trajectoryProfile, worldTrajectory, trajectoryReferenceTransform)}";

        Debug.Log(
            message);

        if (hasCoordinateDiagnosticMetadata &&
            !string.IsNullOrEmpty(
                trajectoryProfile.BakedPlayerRootName) &&
            trajectoryProfile.BakedPlayerRootName !=
                playerTransform.name)
        {
            LogMeshCutDiagnosticWarning(
                "CoordinateValidation",
                $"Bake時Player Root='{trajectoryProfile.BakedPlayerRootName}' と " +
                $"RuntimeのPlayer Tag取得結果='{playerTransform.name}' が一致していません。" +
                "別Transformを基準に座標復元している可能性があります。");
        }

        if (hasCoordinateDiagnosticMetadata &&
            !string.IsNullOrEmpty(
                trajectoryProfile.BakedTrajectoryReferenceName) &&
            trajectoryProfile.BakedTrajectoryReferenceName !=
                trajectoryReferenceTransform.name)
        {
            LogMeshCutDiagnosticWarning(
                "CoordinateValidation",
                $"Bake時Trajectory Reference='{trajectoryProfile.BakedTrajectoryReferenceName}' と " +
                $"Runtime解決結果='{trajectoryReferenceTransform.name}' が一致していません。");
        }

        if (hasCoordinateDiagnosticMetadata)
        {
            float playerRootScaleDifference =
                Vector3.Distance(
                    trajectoryProfile.BakedPlayerRootLossyScale,
                    playerTransform.lossyScale);

            float referenceScaleDifference =
                Vector3.Distance(
                    trajectoryProfile.BakedTrajectoryReferenceLossyScale,
                    trajectoryReferenceTransform.lossyScale);

            if (playerRootScaleDifference >
                    SCALE_DIFFERENCE_WARNING_THRESHOLD ||
                referenceScaleDifference >
                    SCALE_DIFFERENCE_WARNING_THRESHOLD)
            {
                LogMeshCutDiagnosticWarning(
                    "CoordinateValidation",
                    $"Bake時とRuntimeで基準TransformのLossy Scaleが異なります。" +
                    $" PlayerRoot差={playerRootScaleDifference:F6}, " +
                    $"Reference差={referenceScaleDifference:F6}。" +
                    "TransformPointはScaleの影響を受けるため、復元位置が変わります。");
            }
        }

        if (maximumTipLocalRoundTripError >
                COORDINATE_ROUND_TRIP_WARNING_THRESHOLD ||
            maximumRootLocalRoundTripError >
                COORDINATE_ROUND_TRIP_WARNING_THRESHOLD)
        {
            LogMeshCutDiagnosticWarning(
                "CoordinateValidation",
                "RuntimeのLocal→World→Local往復誤差が大きいです。" +
                "Transform階層またはScaleを確認してください。");
        }

        if (hasCoordinateDiagnosticMetadata &&
            (!liveTipResolved ||
             !liveRootResolved))
        {
            LogMeshCutDiagnosticWarning(
                "CoordinateValidation",
                "Bake時に保存したChainsaw Tip / Root PathをRuntime Player Rootから解決できません。" +
                "Bake時Player RootとRuntimeのPlayer Tag対象が異なる、またはPlayer階層が変更された可能性があります。" +
                "この診断項目はProfileを再Bakeした後に有効になります。");
        }
    }

    private static string BuildRuntimeSampleDiagnostic(
        int sampleIndex,
        ChainsawAttackTrajectoryProfile trajectoryProfile,
        ChainsawAttackTrajectory worldTrajectory,
        Transform trajectoryReferenceTransform)
    {
        ChainsawBladeTrajectorySample localSample =
            trajectoryProfile.Samples[sampleIndex];

        ChainsawBladeTrajectorySample worldSample =
            worldTrajectory.Samples[sampleIndex];

        Vector3 restoredTipLocalPosition =
            trajectoryReferenceTransform.InverseTransformPoint(
                worldSample.TipPosition);

        Vector3 restoredRootLocalPosition =
            trajectoryReferenceTransform.InverseTransformPoint(
                worldSample.RootPosition);

        return
            $"Index={sampleIndex}, Time={localSample.SampleTime:F4}, " +
            $"TipLocal={FormatVector3(localSample.TipPosition)}, " +
            $"TipWorld={FormatVector3(worldSample.TipPosition)}, " +
            $"TipRestoredLocal={FormatVector3(restoredTipLocalPosition)}, " +
            $"TipLocalError={Vector3.Distance(localSample.TipPosition, restoredTipLocalPosition):F6}, " +
            $"RootLocal={FormatVector3(localSample.RootPosition)}, " +
            $"RootWorld={FormatVector3(worldSample.RootPosition)}, " +
            $"RootRestoredLocal={FormatVector3(restoredRootLocalPosition)}, " +
            $"RootLocalError={Vector3.Distance(localSample.RootPosition, restoredRootLocalPosition):F6}";
    }

    private static string BuildLiveBladeComparisonSummary(
        Transform liveTipTransform,
        Transform liveRootTransform,
        ChainsawAttackTrajectory worldTrajectory)
    {
        if (liveTipTransform == null ||
            liveRootTransform == null ||
            worldTrajectory == null ||
            worldTrajectory.SampleCount == 0)
        {
            return
                "LiveBladeComparison=unavailable";
        }

        Vector3 liveTipPosition =
            liveTipTransform.position;

        Vector3 liveRootPosition =
            liveRootTransform.position;

        Vector3 liveBladeCenter =
            GetBladeCenter(
                liveTipPosition,
                liveRootPosition);

        float liveBladeLength =
            Vector3.Distance(
                liveTipPosition,
                liveRootPosition);

        float nearestDistance =
            float.PositiveInfinity;

        int nearestSampleIndex = -1;

        Vector3 nearestTrajectoryCenter =
            default;

        for (int i = 0;
             i < worldTrajectory.SampleCount;
             i++)
        {
            ChainsawBladeTrajectorySample sample =
                worldTrajectory.Samples[i];

            Vector3 sampleCenter =
                GetBladeCenter(
                    sample.TipPosition,
                    sample.RootPosition);

            float distance =
                Vector3.Distance(
                    liveBladeCenter,
                    sampleCenter);

            if (distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearestSampleIndex = i;
            nearestTrajectoryCenter = sampleCenter;
        }

        float distanceToBladeLengthRatio =
            liveBladeLength > Mathf.Epsilon
                ? nearestDistance / liveBladeLength
                : float.PositiveInfinity;

        return
            $"LiveBlade: Tip={FormatVector3(liveTipPosition)}, " +
            $"Root={FormatVector3(liveRootPosition)}, " +
            $"Center={FormatVector3(liveBladeCenter)}, " +
            $"Length={liveBladeLength:F4}, " +
            $"NearestTrajectorySample={nearestSampleIndex}, " +
            $"NearestCenter={FormatVector3(nearestTrajectoryCenter)}, " +
            $"Distance={nearestDistance:F4}, " +
            $"Distance/BladeLength={distanceToBladeLengthRatio:F4}";
    }

    private void DrawTrajectoryCoordinateValidation(
        Transform playerTransform,
        ChainsawAttackTrajectoryProfile trajectoryProfile,
        ChainsawAttackTrajectory worldTrajectory)
    {
        if (worldTrajectory == null ||
            worldTrajectory.SampleCount == 0)
        {
            return;
        }

        float duration =
            Mathf.Max(
                0.0f,
                reconstructedTrajectoryDrawDuration);

        ChainsawBladeTrajectorySample firstSample =
            worldTrajectory.Samples[0];

        Vector3 previousTip =
            firstSample.TipPosition;

        Vector3 previousRoot =
            firstSample.RootPosition;

        for (int i = 0;
             i < worldTrajectory.SampleCount;
             i++)
        {
            ChainsawBladeTrajectorySample sample =
                worldTrajectory.Samples[i];

            Vector3 currentTip =
                sample.TipPosition;

            Vector3 currentRoot =
                sample.RootPosition;

            if (i > 0)
            {
                Debug.DrawLine(
                    previousTip,
                    currentTip,
                    Color.cyan,
                    duration,
                    false);

                Debug.DrawLine(
                    previousRoot,
                    currentRoot,
                    Color.yellow,
                    duration,
                    false);
            }

            Debug.DrawLine(
                currentRoot,
                currentTip,
                Color.white,
                duration,
                false);

            previousTip =
                currentTip;

            previousRoot =
                currentRoot;
        }

        if (!trajectoryProfile.HasCoordinateDiagnosticMetadata ||
            !TryResolveRelativeTransform(
                playerTransform,
                trajectoryProfile.ChainsawTipPath,
                out Transform liveTipTransform) ||
            !TryResolveRelativeTransform(
                playerTransform,
                trajectoryProfile.ChainsawRootPath,
                out Transform liveRootTransform))
        {
            return;
        }

        Vector3 liveTipPosition =
            liveTipTransform.position;

        Vector3 liveRootPosition =
            liveRootTransform.position;

        Vector3 liveBladeCenter =
            GetBladeCenter(
                liveTipPosition,
                liveRootPosition);

        Debug.DrawLine(
            liveRootPosition,
            liveTipPosition,
            Color.red,
            duration,
            false);

        float nearestDistance =
            float.PositiveInfinity;

        Vector3 nearestTrajectoryCenter =
            liveBladeCenter;

        for (int i = 0;
             i < worldTrajectory.SampleCount;
             i++)
        {
            ChainsawBladeTrajectorySample sample =
                worldTrajectory.Samples[i];

            Vector3 sampleCenter =
                GetBladeCenter(
                    sample.TipPosition,
                    sample.RootPosition);

            float distance =
                Vector3.Distance(
                    liveBladeCenter,
                    sampleCenter);

            if (distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = distance;
            nearestTrajectoryCenter = sampleCenter;
        }

        Debug.DrawLine(
            liveBladeCenter,
            nearestTrajectoryCenter,
            Color.magenta,
            duration,
            false);
    }

    private static bool TryResolveRelativeTransform(
        Transform root,
        string relativePath,
        out Transform resolvedTransform)
    {
        if (root == null)
        {
            resolvedTransform = null;
            return false;
        }

        if (string.IsNullOrEmpty(relativePath))
        {
            resolvedTransform = root;
            return true;
        }

        resolvedTransform =
            root.Find(
                relativePath);

        return resolvedTransform != null;
    }

    private static Vector3 GetBladeCenter(
        Vector3 tipPosition,
        Vector3 rootPosition)
    {
        return
            (tipPosition + rootPosition) * 0.5f;
    }

    private void TryBeginCutPieceEffects()
    {
        var cutPieceTransforms =
            new List<Transform>();

        bool hasCutPieces =
            enemyMeshCutter
                .TryGetCutPieceTransforms(
                    cutPieceTransforms);

        if (!hasCutPieces)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                "Mesh分割には成功しましたが、" +
                "生成された切断片を取得できませんでした。");

            LogMeshCutDiagnosticWarning(
                "Death-07 PieceEffects",
                "TryGetCutPieceTransforms=falseのため切断片Effectsを開始できません。");

            return;
        }

        LogMeshCutDiagnostic(
            "Death-07 PieceEffects",
            $"切断片を{cutPieceTransforms.Count}個取得しました。");

        if (enemyCutPieceBurst == null)
        {
            LogMeshCutDiagnosticWarning(
                "Death-08 Burst",
                $"{nameof(EnemyCutPieceBurst)}がないため吹き飛ばしは開始されません。");
        }
        else
        {
            PlayerAttackHitData lastPlayerAttackHitData =
                enemyAttackHitReceiver
                    .LastPlayerAttackHitData;

            float chainsawRotationRatio =
                lastPlayerAttackHitData != null &&
                lastPlayerAttackHitData
                    .HasChainsawRotationRatio
                    ? lastPlayerAttackHitData
                        .ChainsawRotationRatio
                    : 0f;

            if (lastPlayerAttackHitData == null ||
                !lastPlayerAttackHitData
                    .HasChainsawRotationRatio)
            {
                Debug.LogWarning(
                    $"{nameof(StandardEnemyDeathStrategy)}: " +
                    "死亡攻撃時のチェンソー回転率が命中情報にないため、" +
                    "切断片は最低回転速度で回転させます。");
            }

            bool burstStarted =
                enemyCutPieceBurst.BeginBurst(
                    cutPieceTransforms,
                    enemyAttackHitReceiver
                        .LastResolvedKnockbackVelocity,
                    chainsawRotationRatio);

            LogMeshCutDiagnostic(
                "Death-08 Burst",
                $"BeginBurst result={burstStarted}, " +
                $"PieceCount={cutPieceTransforms.Count}, " +
                $"ResolvedKnockbackVelocity={enemyAttackHitReceiver.LastResolvedKnockbackVelocity}, " +
                $"ChainsawRotationRatio={chainsawRotationRatio:F3}");
        }

        if (enemyCutPieceShrink == null)
        {
            LogMeshCutDiagnosticWarning(
                "Death-09 Shrink",
                $"{nameof(EnemyCutPieceShrink)}がないため縮小処理は開始されません。");
            return;
        }

        bool shrinkStarted =
            enemyCutPieceShrink.BeginShrink(
                cutPieceTransforms,
                enemyData.DeathShrinkDelay,
                enemyData.DeathShrinkDuration);

        LogMeshCutDiagnostic(
            "Death-09 Shrink",
            $"BeginShrink result={shrinkStarted}, " +
            $"PieceCount={cutPieceTransforms.Count}, " +
            $"IsShrinkActive={enemyCutPieceShrink.IsShrinkActive}, " +
            $"Delay={enemyData.DeathShrinkDelay:F3}, Duration={enemyData.DeathShrinkDuration:F3}");
    }

    private static string BuildWorldTrajectorySummary(
        ChainsawAttackTrajectory trajectory)
    {
        if (trajectory == null ||
            trajectory.SampleCount == 0)
        {
            return "World軌跡にサンプルがありません。";
        }

        int lastIndex =
            trajectory.SampleCount - 1;

        ChainsawBladeTrajectorySample firstSample =
            trajectory.Samples[0];

        ChainsawBladeTrajectorySample lastSample =
            trajectory.Samples[lastIndex];

        Vector3 firstCenter =
            (firstSample.TipPosition +
             firstSample.RootPosition) * 0.5f;

        Vector3 lastCenter =
            (lastSample.TipPosition +
             lastSample.RootPosition) * 0.5f;

        return
            $"World軌跡: Samples={trajectory.SampleCount}, " +
            $"FirstCenter={FormatVector3(firstCenter)}, " +
            $"LastCenter={FormatVector3(lastCenter)}, " +
            $"LastTip={FormatVector3(lastSample.TipPosition)}, " +
            $"LastRoot={FormatVector3(lastSample.RootPosition)}, " +
            $"LastSampleTime={lastSample.SampleTime:F4}";
    }

    private static string FormatVector3(
        Vector3 value)
    {
        return
            $"({value.x:F4}, {value.y:F4}, {value.z:F4})";
    }

    private void LogMeshCutDiagnostic(
        string stage,
        string message)
    {
        if (!enableMeshCutDiagnosticLogs)
        {
            return;
        }

        Debug.Log(
            $"[MeshCutDiagnostic][{stage}] {message}");
    }

    private void LogMeshCutDiagnosticWarning(
        string stage,
        string message)
    {
        if (!enableMeshCutDiagnosticLogs)
        {
            return;
        }

        Debug.LogWarning(
            $"[MeshCutDiagnostic][{stage}] {message}");
    }
}
