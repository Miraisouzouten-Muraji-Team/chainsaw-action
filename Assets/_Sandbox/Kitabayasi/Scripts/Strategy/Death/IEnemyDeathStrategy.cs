using UnityEngine;

/// <summary>
/// Enemyの死亡Stateで使用する、死亡処理の契約を定義する。
/// </summary>
/// <remarks>
/// 責務:
/// ・Enemyごとに異なる死亡処理のライフサイクルを定義する。
/// ・死亡処理が完了したかどうかをEnemyDeathStateへ返す。
///
/// 担当しない責務:
/// ・HPの保持や死亡判定。
/// ・Stateの保持やState遷移。
/// ・どのStrategyを使用するかの決定。
/// </remarks>
public interface IEnemyDeathStrategy
{
    void Initialize(
        EnemyData enemyData,
        GameObject enemyObject);

    void BeginDeath();

    /// <returns>
    /// 死亡処理が完了した場合はtrue。
    /// </returns>
    bool UpdateDeath();

    void EndDeath();
}
