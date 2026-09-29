/// <summary>
/// Enemyの各Stateが共通して持つライフサイクルを定義する。
/// </summary>
/// <remarks>
/// 責務:
/// ・各StateにEnter / Update / Exitの実装を要求する。
/// ・EnemyStateMachineから各Stateを共通の型として扱えるようにする。
/// </remarks>


public interface IEnemyState
{
    void Enter();

    void Update();

    void Exit();
}
