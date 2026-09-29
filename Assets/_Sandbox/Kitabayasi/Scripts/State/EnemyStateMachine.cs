using System;
using UnityEngine;


/// <summary>
/// EnemyのStateを保持し、Stateの遷移と実行を管理する。
/// </summary>
/// <remarks>
/// 責務:
/// ・Stateの保持と切り替えを管理する。
/// ・現在のStateを実行する。
/// ・死亡通知を受けてDeadStateへ遷移する。
///
/// 担当しない責務:
/// ・各State固有の行動処理。
/// ・HPや移動、攻撃などの個別機能。
/// </remarks>



// EnemyHealthが無ければ自動で追加される。
[RequireComponent(typeof(EnemyHealth))]
public class EnemyStateMachine : MonoBehaviour
{
    private EnemyHealth enemyHealth;

    private EnemyIdleState idleState;
    private EnemyDeadState deadState;

    public IEnemyState CurrentState { get; private set; }

    private void Awake()
    {
        enemyHealth = GetComponent<EnemyHealth>();

        // Enemy間でStateを共有しないように、StateMachineごとに生成する。
        idleState = new EnemyIdleState();
        deadState = new EnemyDeadState();
    }

    private void OnEnable()
    {
        // EnemyHealthの死亡通知を受け、DeadStateへ遷移する。
        enemyHealth.Died += HandleDied;

        // 無効化中やイベント購読前に死亡していた場合でも、
        // StateとHealthの状態が食い違わないようにする。
        if (enemyHealth.IsDead)
        {
            ChangeState(deadState);
        }
    }

    private void Start()
    {
        // 通常はIdleから開始する。
        // Start以前に死亡が成立している特殊ケースではDeadを優先する。
        ChangeState(enemyHealth.IsDead ? deadState : idleState);
    }

    private void Update()
    {
        CurrentState?.Update();
    }

    private void OnDisable()
    {
        enemyHealth.Died -= HandleDied;
    }

    private void HandleDied()
    {
        ChangeState(deadState);
    }

    private void ChangeState(IEnemyState nextState)
    {
        if (nextState == null)
        {
            // 引数がnullの場合は例外をスローする。
            throw new ArgumentNullException(nameof(nextState));
        }

        //同じStateへの遷移を防ぐ
        if (ReferenceEquals(CurrentState, nextState))
        {
            return;
        }

        CurrentState?.Exit();

        CurrentState = nextState;

        CurrentState.Enter();
    }
}
