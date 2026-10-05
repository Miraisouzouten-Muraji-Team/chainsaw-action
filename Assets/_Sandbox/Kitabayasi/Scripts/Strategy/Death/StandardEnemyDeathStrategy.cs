using System;
using UnityEngine;

/// <summary>
/// 通常Enemyで共通して使用する死亡処理を担当する。
/// </summary>
/// <remarks>
/// 死亡開始時に、死亡原因となったPlayerの攻撃情報から
/// チェンソー軌跡を取得し、EnemyMeshCutterへメッシュ分割を要求する。
///
/// Mesh分割成功後は、生成された切断片を
/// EnemyCutPieceBurstへ渡して吹き飛ばしを開始し、
/// EnemyCutPieceShrinkへ渡して縮小シーケンスを開始する。
///
/// Mesh分割アルゴリズム、切断片の具体的な移動・縮小処理、
/// State切り替え、HPの保持・死亡判定、
/// Enemy本体の最終破棄は担当しない。
/// </remarks>
[Serializable]
public sealed class StandardEnemyDeathStrategy :
    IEnemyDeathStrategy
{
    private EnemyData enemyData;

    private EnemyAttackHitReceiver
        enemyAttackHitReceiver;

    private EnemyMeshCutter
        enemyMeshCutter;

    private EnemyCutPieceBurst
        enemyCutPieceBurst;

    private EnemyCutPieceShrink
        enemyCutPieceShrink;

    private bool isDeathActive;

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

        enemyMeshCutter =
            enemyObject.GetComponent<
                EnemyMeshCutter>();

        enemyCutPieceBurst =
            enemyObject.GetComponent<
                EnemyCutPieceBurst>();

        enemyCutPieceShrink =
            enemyObject.GetComponent<
                EnemyCutPieceShrink>();

        if (enemyAttackHitReceiver == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: "
                + $"{nameof(EnemyAttackHitReceiver)}"
                + "が見つかりません。 "
                + "死亡時の攻撃情報を取得できないため、"
                + "メッシュ分割は実行されません。",
                enemyObject);
        }

        if (enemyMeshCutter == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: "
                + $"{nameof(EnemyMeshCutter)}"
                + "が見つかりません。 "
                + "死亡時のメッシュ分割は実行されません。",
                enemyObject);
        }

        if (enemyCutPieceBurst == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: "
                + $"{nameof(EnemyCutPieceBurst)}"
                + "が見つかりません。 "
                + "メッシュ分割後の吹き飛ばしは"
                + "実行されません。",
                enemyObject);
        }

        if (enemyCutPieceShrink == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: "
                + $"{nameof(EnemyCutPieceShrink)}"
                + "が見つかりません。 "
                + "メッシュ分割後の縮小は"
                + "実行されません。",
                enemyObject);
        }
    }

    public void BeginDeath()
    {
        isDeathActive = true;

        TryCutDeathMesh();
    }

    public bool UpdateDeath()
    {
        if (!isDeathActive)
        {
            return false;
        }

        bool hasCompletedBurst =
            enemyCutPieceBurst == null ||
            !enemyCutPieceBurst.IsBursting;

        bool hasCompletedShrink =
            enemyCutPieceShrink == null ||
            !enemyCutPieceShrink.IsShrinkActive;

        return hasCompletedBurst &&
               hasCompletedShrink;
    }

    public void EndDeath()
    {
        isDeathActive = false;

        enemyCutPieceBurst?.StopBurst();
        enemyCutPieceShrink?.StopShrink();
    }

    /// <summary>
    /// 死亡原因となったPlayerの攻撃情報から
    /// チェンソー軌跡を取得し、
    /// メッシュ分割と切断片演出の開始を要求する。
    /// </summary>
    private void TryCutDeathMesh()
    {
        if (enemyAttackHitReceiver == null ||
            enemyMeshCutter == null)
        {
            return;
        }

        PlayerAttackHitData lastPlayerAttackHitData =
            enemyAttackHitReceiver
                .LastPlayerAttackHitData;

        if (lastPlayerAttackHitData == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: "
                + "死亡原因となった"
                + "PlayerAttackHitDataがありません。 "
                + "メッシュ分割は実行されません.");

            return;
        }

        ChainsawAttackTrajectory chainsawTrail =
            lastPlayerAttackHitData
                .ChainsawTrail;

        if (chainsawTrail == null)
        {
            Debug.LogWarning(
                $"{nameof(StandardEnemyDeathStrategy)}: "
                + "死亡原因となった攻撃に"
                + "ChainsawTrailがありません。 "
                + "メッシュ分割は実行されません.");

            return;
        }

        bool cutSucceeded =
            enemyMeshCutter.TryCut(
                chainsawTrail);

        if (!cutSucceeded)
        {
            return;
        }

        TryBeginCutPieceEffects();
    }

    /// <summary>
    /// Mesh分割によって生成された2つの切断片を取得し、
    /// 吹き飛ばしと縮小の開始を要求する。
    /// </summary>
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
                $"{nameof(StandardEnemyDeathStrategy)}: "
                + "Mesh分割には成功しましたが、"
                + "生成された切断片を取得できませんでした.");

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
