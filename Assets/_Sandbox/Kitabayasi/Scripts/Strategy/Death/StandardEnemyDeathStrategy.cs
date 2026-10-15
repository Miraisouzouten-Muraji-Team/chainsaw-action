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
/// Bake済み軌跡をWorld空間へ復元してEnemyMeshCutterによる2分割死亡演出を行う。
///
/// 死亡開始時にはEnemy本体と子階層に存在していたColliderを無効化する。
/// Mesh分割アルゴリズム、Fragmentの具体的な物理挙動、State遷移、HP管理、
/// Enemy本体の最終破棄は担当しない。
/// </remarks>
[Serializable]
public sealed class StandardEnemyDeathStrategy :
    IEnemyDeathStrategy
{
    [Header("通常攻撃2分割")]
    [Tooltip(
        "Slash1 / Slash2 / Slash3など、通常攻撃ごとのBake済み軌跡Profile。" +
        "死亡原因のAttackDataとProfile内のAttackDataを参照一致で照合する。")]
    [SerializeField]
    private ChainsawAttackTrajectoryProfile[]
        chainsawAttackTrajectoryProfiles;

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
                "既存の2分割死亡演出へのフォールバックは実行されません。",
                enemyObject);
        }

        if (enemyCutPieceBurst == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                $"{nameof(EnemyCutPieceBurst)}が見つかりません。" +
                "既存メッシュ分割後の吹き飛ばしは実行されません。",
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

        bool hasCompletedBurst =
            enemyCutPieceBurst == null ||
            !enemyCutPieceBurst.IsBursting;

        bool hasCompletedShrink =
            enemyCutPieceShrink == null ||
            !enemyCutPieceShrink.IsShrinkActive;

        return
            hasCompletedBurst &&
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

        enemyCutPieceBurst?.StopBurst();
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
                "既存の2分割死亡演出へフォールバックします。");

            return false;
        }

        isMeshCutDeathEffectActive =
            true;

        return true;
    }

    /// <summary>
    /// 死亡原因のAttackDataに対応するBake済みチェンソー軌跡をWorld空間へ復元し、
    /// 既存の2分割死亡演出を開始する。
    /// </summary>
    private void TryCutDeathMesh()
    {
        if (enemyAttackHitReceiver == null ||
            enemyMeshCutter == null)
        {
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

            return;
        }

        ChainsawAttackTrajectoryProfile trajectoryProfile =
            FindTrajectoryProfile(
                lastPlayerAttackHitData.AttackData);

        if (trajectoryProfile == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                "死亡原因のAttackDataに対応する" +
                $"{nameof(ChainsawAttackTrajectoryProfile)}がありません。" +
                "通常攻撃の2分割メッシュ切断は実行されません。");

            return;
        }

        if (!trajectoryProfile.HasValidTrajectory)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                $"{trajectoryProfile.name}に有効なBake済み軌跡がありません。" +
                "2サンプル以上の軌跡をBakeしてください。");

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

            return;
        }

        if (!TryResolveTrajectoryReferenceTransform(
                playerTransform,
                trajectoryProfile,
                out Transform trajectoryReferenceTransform))
        {
            return;
        }

        ChainsawAttackTrajectory worldTrajectory =
            CreateWorldTrajectory(
                trajectoryProfile,
                trajectoryReferenceTransform);

        if (!enemyMeshCutter.TryCut(
                worldTrajectory))
        {
            return;
        }

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
                "Slashを一意に判別できないため、2分割メッシュ切断は実行しません。");

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

    private void TryBeginCutPieceEffects()
    {
        bool hasCutPieces =
            enemyMeshCutter
                .TryGetCutPieceTransforms(
                    out Transform firstPiece,
                    out Transform secondPiece);

        if (!hasCutPieces)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                "Mesh分割には成功しましたが、" +
                "生成された切断片を取得できませんでした。");

            return;
        }

        enemyCutPieceBurst?.BeginBurst(
            firstPiece,
            secondPiece);

        enemyCutPieceShrink?.BeginShrink(
            firstPiece,
            secondPiece,
            enemyData.DeathShrinkDelay,
            enemyData.DeathShrinkDuration);
    }
}
