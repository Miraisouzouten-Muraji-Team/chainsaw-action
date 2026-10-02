using UnityEngine;

/// <summary>
/// Enemyの索敵Stateで使用する、索敵方法の契約を定義する。
/// </summary>
/// <remarks>
/// 責務:
/// ・Enemyごとに異なる索敵処理のライフサイクルを定義する。
/// ・プレイヤーを発見したかどうかをEnemySearchStateへ返す。
/// ・索敵中に発生したCollisionを受け取る。
///
/// 担当しない責務:
/// ・Stateの保持やState遷移。
/// ・どのStrategyを使用するかの決定。
/// ・Enemyの設定値の保持。
/// </remarks>
public interface IEnemySearchStrategy
{
    /// <summary>
    /// Strategyで使用するEnemyDataと、
    /// Strategyを所有するEnemyを設定する。
    /// </summary>
    void Initialize(
        EnemyData enemyData,
        GameObject enemyObject);

    /// <summary>
    /// 索敵Stateへ入った際の処理を行う。
    /// </summary>
    void BeginSearch();

    /// <summary>
    /// 索敵中の処理を更新する。
    /// </summary>
    /// <returns>
    /// プレイヤーを発見した場合はtrue。
    /// </returns>
    bool UpdateSearch();

    /// <summary>
    /// 索敵中に発生したCollisionを受け取る。
    /// </summary>
    void HandleCollisionEnter(Collision collision);

    /// <summary>
    /// 索敵Stateから抜ける際の処理を行う。
    /// </summary>
    void EndSearch();
}
