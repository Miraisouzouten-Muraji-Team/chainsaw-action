using System;
using UnityEngine;

/// <summary>
/// 通常Enemyで共通して使用する死亡処理を担当する。
/// </summary>
/// <remarks>
/// 死亡原因となった攻撃がEnemyMeshCutDeathEffectのTriggerAttackDataと一致する場合は、
/// 可変数Fragmentへ分割して吹き飛ばすMeshCut死亡演出を優先する。
/// 対象攻撃ではない場合、またはMeshCut死亡演出を開始できなかった場合は、
/// 既存のEnemyMeshCutterによる2分割死亡演出へフォールバックする。
///
/// 死亡開始時にはEnemy本体と子階層に存在していたColliderを無効化する。
/// Mesh分割アルゴリズム、Fragmentの具体的な物理挙動、State遷移、HP管理、
/// Enemy本体の最終破棄は担当しない。
/// </remarks>
[Serializable]
public sealed class StandardEnemyDeathStrategy :
    IEnemyDeathStrategy
{
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
    /// 死亡原因となったPlayer攻撃のチェンソー軌跡を使い、
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

        ChainsawAttackTrajectory chainsawTrail =
            lastPlayerAttackHitData
                .ChainsawTrail;

        if (chainsawTrail == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: " +
                "死亡原因となった攻撃にChainsawTrailがありません。" +
                "メッシュ分割は実行されません。");

            return;
        }

        if (!enemyMeshCutter.TryCut(
                chainsawTrail))
        {
            return;
        }

        TryBeginCutPieceEffects();
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
