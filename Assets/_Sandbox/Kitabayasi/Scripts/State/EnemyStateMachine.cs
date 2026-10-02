using System;
using UnityEngine;

/// <summary>
/// EnemyのStateを保持し、遷移・実行とStrategyの初期構成を管理する。
/// </summary>
/// <remarks>
/// 各State固有の行動・演出・HP処理は担当しない。
///
/// UnityからStateに関係するイベントを受信した場合は、
/// 具体的なStateやStrategyを判定せず、
/// 対応する受信interfaceを実装しているCurrentStateへ通知する。
///
/// Alert完了後はStateを終了し、通常攻撃側へAttackRequestedを通知する。
/// </remarks>
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(EnemyDataReference))]
public class EnemyStateMachine : MonoBehaviour
{
    [Header("索敵")]
    [Tooltip("このEnemyが索敵Stateで使用するStrategy。")]
    [SerializeReference]
    private IEnemySearchStrategy searchStrategy;

    [Header("攻撃予告")]
    [Tooltip("このEnemyがAlert Stateで使用するStrategy。")]
    [SerializeReference]
    private IEnemyAlertStrategy alertStrategy;

    [Header("デバッグ")]
    [Tooltip("Sceneビューに索敵範囲と巡回範囲を表示する。")]
    [SerializeField]
    private bool showSearchDebugVisualization;

    private EnemyHealth enemyHealth;
    private EnemyDataReference enemyDataReference;
    private EnemySearchState searchState;
    private EnemyAlertState alertState;
    private IEnemyState suspendedState;
    private bool isInitialized;

    public IEnemyState CurrentState { get; private set; }

    /// <summary>
    /// Alertを終了した後、一度だけ通知する攻撃開始要求。
    /// 購読側はOnEnable / OnDisable等で購読・解除する。
    /// 現段階ではAttack Stateは生成せず、通知後のCurrentStateはnullとなる。
    /// </summary>
    public event Action AttackRequested;

    private void Awake()
    {
        enemyHealth = GetComponent<EnemyHealth>();
        enemyDataReference = GetComponent<EnemyDataReference>();

        try
        {
            InitializeStates();
            isInitialized = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (!isInitialized)
        {
            return;
        }

        enemyHealth.Died += HandleDied;

        if (enemyHealth.IsDead)
        {
            HandleDied();
            return;
        }

        if (suspendedState != null)
        {
            IEnemyState stateToResume = suspendedState;

            suspendedState = null;

            ChangeState(stateToResume);
        }
    }

    private void Start()
    {
        if (isInitialized &&
            !enemyHealth.IsDead &&
            CurrentState == null &&
            searchState != null)
        {
            ChangeState(searchState);
        }
    }

    /// <summary>
    /// 既存の物理更新周期で現在のStateを実行する。
    /// </summary>
    private void FixedUpdate()
    {
        CurrentState?.Update();
    }

    /// <summary>
    /// UnityからCollision開始通知を受信し、
    /// 対応可能なCurrentStateへそのまま転送する。
    /// </summary>
    /// <remarks>
    /// EnemyStateMachine自身はCollisionの意味や、
    /// どのStrategyが使用されているかを判断しない。
    ///
    /// 現時点ではMonoBehaviourでしか直接受信できないUnityイベントが
    /// Collision系のみのため、このクラスをUnityイベントの入口として兼用する。
    ///
    /// 今後、Triggerやその他のMonoBehaviour依存イベントなど、
    /// Stateへ転送するUnityイベントが増えてStateMachineの責務が肥大化した場合は、
    /// Enemy専用のUnityイベント受信ハブとなるMonoBehaviourを別途作成し、
    /// イベント受信・配送責務をこのクラスから分離する。
    /// </remarks>
    private void OnCollisionEnter(Collision collision)
    {
        // Unityは無効なMonoBehaviourにもCollisionを送るため、
        // 無効中・初期化前・死亡後の通知は処理しない。
        if (!isActiveAndEnabled ||
            !isInitialized ||
            enemyHealth.IsDead)
        {
            return;
        }

        if (CurrentState is IEnemyCollisionEnterReceiver receiver)
        {
            receiver.HandleCollisionEnter(collision);
        }
    }

    /// <summary>
    /// UnityからCollision継続通知を受信し、
    /// 対応可能なCurrentStateへそのまま転送する。
    /// </summary>
    private void OnCollisionStay(Collision collision)
    {
        if (!isActiveAndEnabled ||
            !isInitialized ||
            enemyHealth.IsDead)
        {
            return;
        }

        if (CurrentState is IEnemyCollisionStayReceiver receiver)
        {
            receiver.HandleCollisionStay(collision);
        }
    }

    private void OnDisable()
    {
        if (enemyHealth != null)
        {
            enemyHealth.Died -= HandleDied;
        }

        // 再有効化時は中断したStateへ入り直す。
        // Alertの時間・接触記録も初期化される。
        suspendedState =
            enemyHealth != null && !enemyHealth.IsDead
                ? CurrentState
                : null;

        ExitCurrentState();
    }

    private void InitializeStates()
    {
        // Searchを使わないEnemyの構成は従来通り許可する。
        if (searchStrategy == null)
        {
            return;
        }

        EnemyData enemyData = enemyDataReference.Data;

        if (enemyData == null)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyDataReference)}に" +
                $"{nameof(EnemyData)}が設定されていません。");
        }

        if (alertStrategy == null)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyStateMachine)}の" +
                "Alert Strategyを設定してください。");
        }

        searchStrategy.Initialize(
            enemyData,
            gameObject);

        alertStrategy.Initialize(
            enemyData,
            gameObject);

        searchState = new EnemySearchState(
            searchStrategy,
            RequestAlertState);

        alertState = new EnemyAlertState(
            alertStrategy,
            RequestAttackState);
    }

    private void RequestAlertState()
    {
        if (isActiveAndEnabled &&
            !enemyHealth.IsDead &&
            ReferenceEquals(CurrentState, searchState))
        {
            ChangeState(alertState);
        }
    }

    private void RequestAttackState()
    {
        if (!isActiveAndEnabled ||
            enemyHealth.IsDead ||
            !ReferenceEquals(CurrentState, alertState))
        {
            return;
        }

        // 受信側が攻撃を開始する時点で
        // 赤色表示・接触ダメージは解除済みとする。
        ExitCurrentState();

        AttackRequested?.Invoke();
    }

    private void HandleDied()
    {
        suspendedState = null;

        ExitCurrentState();
    }

    private void ExitCurrentState()
    {
        IEnemyState previousState = CurrentState;

        CurrentState = null;

        previousState?.Exit();
    }

    private void ChangeState(IEnemyState nextState)
    {
        if (nextState == null)
        {
            throw new ArgumentNullException(nameof(nextState));
        }

        if (ReferenceEquals(CurrentState, nextState))
        {
            return;
        }

        ExitCurrentState();

        CurrentState = nextState;

        CurrentState.Enter();
    }
}
