using UnityEngine;

/// <summary>
/// Enemyの攻撃Stateで使用する、攻撃方法の契約を定義する。
/// </summary>
/// <remarks>
/// 責務:
/// ・Enemyごとに異なる攻撃予備動作と1回分の攻撃処理を定義する。
/// ・攻撃中に発生したCollision開始・継続通知を受け取る。
///
/// 担当しない責務:
/// ・Stateの保持やState遷移。
/// ・Preparationを省略して攻撃本体を直接開始するかどうかの判断。
/// ・次の攻撃可否を再判定すること。
/// ・どのStrategyを使用するかの決定。
/// ・Enemyの設定値そのものの保持。
/// </remarks>
public interface IEnemyAttackStrategy
{
    /// <param name="visualRoot">
    /// Rigidbody / Colliderを回転させず見た目だけを制御するためのTransform。
    /// Prefab側ではローカル+Zがモデル正面になるよう統一する。
    /// 使用しないStrategyは参照を保持する必要はない。
    /// </param>
    void Initialize(
        EnemyData enemyData,
        GameObject enemyObject,
        Transform visualRoot);

    /// <summary>
    /// 1回分の攻撃に先立つ予備動作を開始する。
    /// EnemyAttackStateは、この処理とUpdatePreparationの完了を必ず経由してからBeginAttackを呼ぶ。
    /// </summary>
    void BeginPreparation(
        float initialAttackDirectionSign);

    /// <summary>
    /// 攻撃予備動作を1物理更新分進める。
    /// </summary>
    /// <returns>
    /// 予備動作が完了し、攻撃本体を開始してよい場合はtrue。
    /// </returns>
    bool UpdatePreparation();

    /// <summary>
    /// Preparation完了後に、1回分の攻撃本体を開始する。
    /// </summary>
    void BeginAttack();

    /// <summary>
    /// 攻撃本体を1物理更新分進める。
    /// </summary>
    /// <returns>
    /// 現在の1攻撃が完了し、次の攻撃可否を再判定してよい場合はtrue。
    /// </returns>
    bool UpdateAttack();

    void HandleCollisionEnter(
        Collision collision);

    void HandleCollisionStay(
        Collision collision);

    void EndAttack();
}
