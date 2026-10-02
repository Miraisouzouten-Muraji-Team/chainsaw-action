using System;
using UnityEngine;

/// <summary>
/// 通常Enemyで共通して使用する死亡処理を担当する。
/// </summary>
/// <remarks>
/// 現段階では死亡演出時間の進行だけを実装する。
/// メッシュ分割・分割パーツの吹き飛ばし・縮小処理は、
/// メッシュ分割機能のAPI確定後にこのStrategyへ接続する。
///
/// State切り替え、HPの保持・死亡判定、
/// Enemy本体の最終破棄は担当しない。
/// </remarks>
[Serializable]
public sealed class StandardEnemyDeathStrategy : IEnemyDeathStrategy
{
    private EnemyData enemyData;
    private GameObject enemyObject;

    private float elapsedTime;
    private bool isDeathActive;

    public void Initialize(
        EnemyData enemyData,
        GameObject enemyObject)
    {
        this.enemyData = enemyData
            ? enemyData
            : throw new ArgumentNullException(nameof(enemyData));

        this.enemyObject = enemyObject
            ? enemyObject
            : throw new ArgumentNullException(nameof(enemyObject));
    }

    public void BeginDeath()
    {
        isDeathActive = true;
        elapsedTime = 0f;

        // TODO: メッシュ分割機能のAPIが確定したら、ここから死亡演出を開始する。
        // 想定する処理:
        // 1. 必要であればEnemyAttackHitReceiver.LastPlayerAttackHitDataから
        //    死亡の原因となった攻撃情報・チェンソー軌跡を取得する。
        // 2. メッシュ分割関数を呼び出す。
        // 3. 生成された分割パーツを吹き飛ばす。
        // 4. 分割パーツと各パーツの初期Scaleを保持し、
        //    UpdateDeath()でDeathShrinkDurationに合わせて徐々に縮小する。
        //
        // 現時点ではメッシュ分割APIが未接続のため、
        // Enemy本体の見た目は変更せず死亡時間だけを進行させる。
    }

    public bool UpdateDeath()
    {
        if (!isDeathActive)
        {
            return false;
        }

        float shrinkDuration =
            enemyData.DeathShrinkDuration;

        if (shrinkDuration <= 0f)
        {
            return true;
        }

        elapsedTime += Time.fixedDeltaTime;

        float shrinkProgress =
            Mathf.Clamp01(
                elapsedTime /
                shrinkDuration);

        // TODO: メッシュ分割機能を接続したら、
        // BeginDeath()で保持した各分割パーツを
        // shrinkProgressに合わせて初期ScaleからVector3.zeroへ縮小する。
        //
        // 例:
        // part.localScale =
        //     Vector3.Lerp(
        //         initialScale,
        //         Vector3.zero,
        //         shrinkProgress);

        return shrinkProgress >= 1f;
    }

    public void EndDeath()
    {
        isDeathActive = false;
        elapsedTime = 0f;
    }
}
