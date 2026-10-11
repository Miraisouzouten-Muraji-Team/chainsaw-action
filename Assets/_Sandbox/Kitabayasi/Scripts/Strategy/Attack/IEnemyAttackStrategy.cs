using UnityEngine;

/// <summary>
/// Enemyの攻撃Stateで使用する、攻撃方法の契約を定義する。
/// </summary>
/// <remarks>
/// 責務:
/// ・Enemyごとに異なる攻撃処理のライフサイクルを定義する。
/// ・攻撃中に発生したCollision開始・継続通知を受け取る。
///
/// 担当しない責務:
/// ・Stateの保持やState遷移。
/// ・どのStrategyを使用するかの決定。
/// ・Enemyの設定値そのものの保持。
/// </remarks>
public interface IEnemyAttackStrategy
{
    /// <param name="visualRoot">
    /// Rigidbody / Colliderを回転させず見た目だけを制御するためのTransform。
    /// 使用しないStrategyは参照を保持する必要はない。
    /// </param>
    void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot);

    void BeginAttack(
        float initialAttackDirectionSign);

    void UpdateAttack();

    void HandleCollisionEnter(
        Collision collision);

    void HandleCollisionStay(
        Collision collision);

    void EndAttack();
}
