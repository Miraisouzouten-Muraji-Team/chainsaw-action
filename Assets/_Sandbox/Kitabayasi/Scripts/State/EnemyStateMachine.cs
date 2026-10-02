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
/// HPが0になった場合はDeath Stateへ遷移し、
/// 死亡処理完了後にEnemy本体を破棄する。
/// </remarks>
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(EnemyDataReference))]
[RequireComponent(typeof(EnemyDamageFlash))]
public class EnemyStateMachine : MonoBehaviour
{
    [Header("索敵")]
    [Tooltip("このEnemyが索敵Stateで使用するStrategy。")]
    [SerializeReference]
    private IEnemySearchStrategy searchStrategy;

    [Header("発見")]
    [Tooltip("このEnemyがAlert Stateで使用するStrategy。")]
    [SerializeReference]
    private IEnemyAlertStrategy alertStrategy;

    [Header("やられ")]
    [Tooltip(
        "やられ中に傾ける見た目用Transform。" +
        "RigidbodyやColliderを持つEnemy本体ではなく、" +
        "VisualRoot等の子Transformを設定する。")]
    [SerializeField]
    private Transform damageTiltTarget;

    [Header("死亡")]
    [Tooltip("このEnemyがDeath Stateで使用するStrategy。")]
    [SerializeReference]
    private IEnemyDeathStrategy deathStrategy =
        new StandardEnemyDeathStrategy();

    [Header("デバッグ")]
    [Tooltip("Sceneビューに索敵範囲と巡回範囲を表示する。")]
    [SerializeField]
    private bool showSearchDebugVisualization;

    private EnemyHealth enemyHealth;
    private EnemyDataReference enemyDataReference;
    private EnemyDamageFlash enemyDamageFlash;

    private EnemySearchState searchState;
    private EnemyAlertState alertState;
    private EnemyDamageState damageState;
    private EnemyDeathState deathState;

    private IEnemyState suspendedState;

    private bool isInitialized;

    public IEnemyState CurrentState { get; private set; }

    /// <summary>
    /// Alertを終了した後、一度だけ通知する攻撃開始要求。
    /// 購読側はOnEnable / OnDisable等で購読・解除する。
    /// 現段階ではAttack Stateは生成せず、
    /// 通知後のCurrentStateはnullとなる。
    /// </summary>
    public event Action AttackRequested;

    private void Awake()
    {
        enemyHealth =
            GetComponent<EnemyHealth>();

        enemyDataReference =
            GetComponent<EnemyDataReference>();

        enemyDamageFlash =
            GetComponent<EnemyDamageFlash>();

        try
        {
            InitializeStates();
            isInitialized = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(
                exception,
                this);

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
            IEnemyState stateToResume =
                suspendedState;

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
    /// Collision系のみのため、
    /// このクラスをUnityイベントの入口として兼用する。
    ///
    /// 今後、Triggerやその他のMonoBehaviour依存イベントなど、
    /// Stateへ転送するUnityイベントが増えて
    /// StateMachineの責務が肥大化した場合は、
    /// Enemy専用のUnityイベント受信ハブとなるMonoBehaviourを別途作成し、
    /// イベント受信・配送責務をこのクラスから分離する。
    /// </remarks>
    private void OnCollisionEnter(
        Collision collision)
    {
        if (!isActiveAndEnabled ||
            !isInitialized ||
            enemyHealth.IsDead)
        {
            return;
        }

        if (CurrentState
            is IEnemyCollisionEnterReceiver receiver)
        {
            receiver.HandleCollisionEnter(
                collision);
        }
    }

    /// <summary>
    /// UnityからCollision継続通知を受信し、
    /// 対応可能なCurrentStateへそのまま転送する。
    /// </summary>
    private void OnCollisionStay(
        Collision collision)
    {
        if (!isActiveAndEnabled ||
            !isInitialized ||
            enemyHealth.IsDead)
        {
            return;
        }

        if (CurrentState
            is IEnemyCollisionStayReceiver receiver)
        {
            receiver.HandleCollisionStay(
                collision);
        }
    }

    private void OnDisable()
    {
        if (enemyHealth != null)
        {
            enemyHealth.Died -= HandleDied;
        }

        // 再有効化時は中断したStateへ入り直す。
        // State固有の経過時間等はEnter時に初期化される。
        suspendedState =
            enemyHealth != null &&
            !enemyHealth.IsDead
                ? CurrentState
                : null;

        ExitCurrentState();
    }

    /// <summary>
    /// 攻撃命中を受けたEnemyをDamage Stateへ遷移させる。
    /// </summary>
    /// <remarks>
    /// 既にDamage State中の場合はStateを切り替えず、
    /// やられ時間と点滅演出だけを最初からやり直す。
    /// </remarks>
    public void RequestDamageState()
    {
        if (!isActiveAndEnabled ||
            !isInitialized ||
            enemyHealth.IsDead ||
            damageState == null)
        {
            return;
        }

        if (ReferenceEquals(
                CurrentState,
                damageState))
        {
            damageState.Restart();
            return;
        }

        ChangeState(damageState);
    }

    private void InitializeStates()
    {
        EnemyData enemyData =
            enemyDataReference.Data;

        if (enemyData == null)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyDataReference)}に" +
                $"{nameof(EnemyData)}が設定されていません。");
        }

        if (damageTiltTarget == null)
        {
            Debug.LogWarning(
                $"{nameof(EnemyStateMachine)}: " +
                "Damage Tilt Targetが設定されていません。 " +
                "やられState中の傾き処理は実行されません。",
                this);
        }

        damageState =
            new EnemyDamageState(
                enemyDamageFlash,
                damageTiltTarget,
                enemyData.HitTiltDuration,
                enemyData.HitTiltAngle,
                RequestSearchState);

        if (deathStrategy == null)
        {
            throw new InvalidOperationException(
                $"{nameof(EnemyStateMachine)}の" +
                "Death Strategyを設定してください。");
        }

        deathStrategy.Initialize(
            enemyData,
            gameObject);

        deathState =
            new EnemyDeathState(
                deathStrategy,
                RequestDeathCompletion);

        // Searchを使わないEnemyの構成は従来通り許可する。
        // サンドバッグのようにDeath / Damageだけ必要なEnemyでも
        // StateMachineを使用できる。
        if (searchStrategy == null)
        {
            return;
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

        searchState =
            new EnemySearchState(
                searchStrategy,
                RequestAlertState);

        alertState =
            new EnemyAlertState(
                alertStrategy,
                RequestAttackState);
    }

    private void RequestAlertState()
    {
        if (isActiveAndEnabled &&
            !enemyHealth.IsDead &&
            ReferenceEquals(
                CurrentState,
                searchState))
        {
            ChangeState(alertState);
        }
    }

    private void RequestAttackState()
    {
        if (!isActiveAndEnabled ||
            enemyHealth.IsDead ||
            !ReferenceEquals(
                CurrentState,
                alertState))
        {
            return;
        }

        // 受信側が攻撃を開始する時点で
        // 赤色表示・接触ダメージは解除済みとする。
        ExitCurrentState();

        AttackRequested?.Invoke();
    }

    /// <summary>
    /// Damage State終了後、Search Stateへ戻す。
    /// </summary>
    private void RequestSearchState()
    {
        if (!isActiveAndEnabled ||
            enemyHealth.IsDead ||
            !ReferenceEquals(
                CurrentState,
                damageState))
        {
            return;
        }

        // Searchを持たないEnemyでは、
        // Damage Stateのみ終了してStateなしへ戻す。
        if (searchState == null)
        {
            ExitCurrentState();
            return;
        }

        ChangeState(searchState);
    }

    /// <summary>
    /// EnemyHealthから死亡通知を受け、
    /// 現在Stateに関係なくDeath Stateへ遷移する。
    /// </summary>
    private void HandleDied()
    {
        // 死亡後に再有効化された際、
        // 死亡前のStateへ戻らないように破棄する。
        suspendedState = null;

        if (!isActiveAndEnabled ||
            !isInitialized ||
            deathState == null)
        {
            ExitCurrentState();
            return;
        }

        ChangeState(deathState);
    }

    /// <summary>
    /// Death Strategyの共通死亡処理が完了した後、
    /// Enemy本体を破棄する。
    /// </summary>
    private void RequestDeathCompletion()
    {
        if (!isActiveAndEnabled ||
            !enemyHealth.IsDead ||
            !ReferenceEquals(
                CurrentState,
                deathState))
        {
            return;
        }

        ExitCurrentState();

        Destroy(gameObject);
    }

    /// <summary>
    /// 現在Stateを終了し、
    /// CurrentStateを空にする。
    /// </summary>
    private void ExitCurrentState()
    {
        IEnemyState previousState =
            CurrentState;

        CurrentState = null;

        previousState?.Exit();
    }

    /// <summary>
    /// 現在Stateを終了して、
    /// 指定されたStateへ遷移する。
    /// </summary>
    private void ChangeState(
        IEnemyState nextState)
    {
        if (nextState == null)
        {
            throw new ArgumentNullException(
                nameof(nextState));
        }

        if (ReferenceEquals(
                CurrentState,
                nextState))
        {
            return;
        }

        ExitCurrentState();

        CurrentState = nextState;

        CurrentState.Enter();
    }
}
