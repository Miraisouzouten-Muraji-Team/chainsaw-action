using UnityEngine;

/// <summary>
/// Enemyの攻撃予告処理の契約。Stateの保持・切り替えは担当しない。
/// </summary>
/// <remarks>
/// SearchStrategyと同じInitialize / Begin / Update / Endのライフサイクルを使う。
/// UpdateAlertの戻り値で完了を通知する。
/// </remarks>
public interface IEnemyAlertStrategy
{
    void Initialize(
        EnemyData enemyData,
        GameObject enemyObject);

    void BeginAlert();

    /// <returns>攻撃予告が完了した場合はtrue。</returns>
    bool UpdateAlert();

    /// <summary>
    /// Alert中の接触開始・継続通知を受け取る。
    /// 同じ接触の継続通知による多重ダメージはStrategy側で防止する。
    /// </summary>
    void HandleCollision(Collision collision);

    void EndAlert();
}
