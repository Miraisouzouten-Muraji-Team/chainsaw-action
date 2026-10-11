using UnityEngine;

/// <summary>
/// Enemyの索敵Stateで使用する、索敵方法の契約を定義する。
/// </summary>
/// <remarks>
/// 責務:
/// ・Enemyごとに異なる索敵処理のライフサイクルを定義する。
/// ・プレイヤーを発見したかどうかと、発見時の攻撃方向をEnemySearchStateへ返す。
/// ・索敵中に発生したCollisionを受け取る。
///
/// 担当しない責務:
/// ・Stateの保持やState遷移。
/// ・どのStrategyを使用するかの決定。
/// ・Enemyの設定値の保持。
/// </remarks>
public interface IEnemySearchStrategy
{
    /// <param name="visualRoot">
    /// Rigidbody / Colliderを回転させず見た目だけを制御するためのTransform。
    /// 使用しないStrategyは参照を保持する必要はない。
    /// </param>
    void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot);


    void BeginSearch();


    /// <param name="detectedPlayerDirectionSign">
    /// プレイヤーを発見した瞬間の左右方向。
    /// -1が-X方向、1が+X方向。
    /// </param>
    /// <returns>
    /// プレイヤーを発見した場合はtrue。
    /// </returns>
    bool UpdateSearch(
        out float detectedPlayerDirectionSign);


    void HandleCollisionEnter(Collision collision);


    void EndSearch();
}
