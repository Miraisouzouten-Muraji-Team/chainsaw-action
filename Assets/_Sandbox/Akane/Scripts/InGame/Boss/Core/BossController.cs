using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.InGame.Boss
{
    /// <summary>
    /// ボスの行動管理と各システムの連携を行う。
    /// </summary>
    [RequireComponent(typeof(BossHealth), typeof(BossShieldController))]
    public sealed class BossController : MonoBehaviour
    {
        [Header("参照")]
        [SerializeField] private Transform player;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;

        [Header("行動設定")]
        [SerializeField, Min(0f)] private float idleDuration = 2f;
        [SerializeField, Min(0f)] private float moveSpeed = 4f;

        [Header("向きの設定")]
        [SerializeField] private float rightFacingAngle = 0f;
        [SerializeField] private float leftFacingAngle = -180f;
        [SerializeField, Min(0f)] private float rotationSpeed = 360f;

        [Header("攻撃設定")]
        [SerializeField] private List<BossAttackDefinition> attackDefinitions = new();

        [Header("アニメーション")]
        [SerializeField] private string deathTrigger = "Dead";

        [SerializeField] private BossAttackTelegraph attackTelegraph;

        private float lastPlayerDirection = 1f;
        private bool started;

        public BossAttackTelegraph Telegraph => attackTelegraph;
        public Transform Player => player;
        public Animator Animator => animator;
        public BossHealth Health { get; private set; }
        public BossShieldController Shield { get; private set; }
        public BossStateMachine StateMachine { get; private set; }
        public BossAttackSelector AttackSelector { get; private set; }
        public IBossAttackStrategy SelectedAttack { get; private set; }

        public float IdleDuration => idleDuration;
        public float DistanceToPlayer => player == null
            ? float.PositiveInfinity
            : Mathf.Abs(player.position.x - transform.position.x);

        // 攻撃判定などの実装時に購読するイベント。
        public event Action<BossAttackType> AttackWindowOpened;
        public event Action<BossAttackType> AttackWindowClosed;

        private void Awake()
        {
            Health = GetComponent<BossHealth>();
            Shield = GetComponent<BossShieldController>();

            var context = new BossContext(this);
            var strategies = CreateAttackStrategies();

            AttackSelector = new BossAttackSelector(strategies);
            StateMachine = new BossStateMachine();

            // 各行動状態を登録する。
            StateMachine.Register(new BossIdleState(context));
            StateMachine.Register(new BossMoveState(context));
            StateMachine.Register(new BossAttackState(context));
            StateMachine.Register(new BossDeadState(context));

            Health.Died += HandleDeath;
        }

        private void Start()
        {
            started = true;
            BeginBoss();
        }

        private void OnEnable()
        {
            // Start実行後に再有効化された場合のみ再開する。
            if (started)
            {
                BeginBoss();
            }
        }

        private void OnDisable()
        {
            // Stateを終了し、実行中の攻撃を中断する。
            StateMachine?.Stop();
            attackTelegraph?.Hide();
        }

        private void OnDestroy()
        {
            if (Health != null)
            {
                Health.Died -= HandleDeath;
            }
            StateMachine?.Dispose();
        }

        /// <summary>
        /// 参照を検証し、初期状態へ遷移する。
        /// </summary>
        private void BeginBoss()
        {
            if (player == null)
            {
                Debug.LogError("BossController: Playerが設定されていません。", this);
                enabled = false;
                return;
            }

            if (AttackSelector.AttackCount == 0)
            {
                Debug.LogError("BossController: 攻撃設定がありません。", this);
                enabled = false;
                return;
            }

            var initialState = Health.IsDead ? BossStateType.Dead : BossStateType.Idle;

            StateMachine.ChangeState(initialState);
        }

        private void Update()
        {
            if (Health.IsDead) { return; }

            Shield.Tick(Time.deltaTime);
            StateMachine.Tick(Time.deltaTime);
        }

        /// <summary>
        /// 設定データから攻撃Strategyを生成する。
        /// </summary>
        private List<IBossAttackStrategy> CreateAttackStrategies()
        {
            var strategies = new List<IBossAttackStrategy>();
            var registeredTypes = new HashSet<BossAttackType>();

            foreach (var definition in attackDefinitions)
            {
                if (definition == null)
                {
                    continue;
                }

                // 同じ種類の攻撃が重複登録されないようにする。
                if (!registeredTypes.Add(definition.Type))
                {
                    Debug.LogWarning($"攻撃が重複しています: {definition.Type}", this);
                    continue;
                }

                IBossAttackStrategy strategy = definition.Type switch
                {
                    BossAttackType.ShieldBash => new ShieldBashAttack(definition),
                    BossAttackType.ShieldThrow => new ShieldThrowAttack(definition),
                    BossAttackType.Thrust => new ThrustAttack(definition),
                    BossAttackType.VerticalSlash => new VerticalSlashAttack(definition),
                    BossAttackType.Laser => new LaserAttack(definition),
                    _ => throw new ArgumentOutOfRangeException()
                };

                strategies.Add(strategy);
            }

            return strategies;
        }

        /// <summary>
        /// 次に使用する攻撃を抽選する。
        /// </summary>
        public void SelectNextAttack()
        {
            SelectedAttack = AttackSelector.DrawNext(Shield.IsActive);
        }

        /// <summary>
        /// 選択中の攻撃を解除する。
        /// </summary>
        public void ClearSelectedAttack()
        {
            SelectedAttack = null;
        }

        /// <summary>
        /// プレイヤーの方向へ向きを変える。
        /// </summary>
        /// <summary>
        /// プレイヤーの方向へモデルの向きを変更する。
        /// </summary>
        public void FacePlayer(float deltaTime)
        {
            if (visualRoot == null || player == null) { return; }

            var distanceX = player.position.x - transform.position.x;

            if (Mathf.Abs(distanceX) > 0.001f)
            {
                lastPlayerDirection = Mathf.Sign(distanceX);
            }

            var targetAngle = lastPlayerDirection > 0f ? rightFacingAngle : leftFacingAngle;

            var targetRotation = Quaternion.Euler(0f, targetAngle, 0f);

            visualRoot.rotation = Quaternion.RotateTowards(visualRoot.rotation, targetRotation, rotationSpeed * deltaTime);
        }

        /// <summary>
        /// プレイヤーとの距離が指定値になるまでX軸方向に移動する。
        /// </summary>
        /// <summary>
        /// プレイヤーとの距離が指定値になるよう、X軸方向に移動する。
        /// </summary>
        public void MoveToTargetDistance(float targetDistance, float deltaTime)
        {
            if (player == null) { return; }

            var currentPosition = transform.position;
            var distanceX = player.position.x - currentPosition.x;

            if (Mathf.Abs(distanceX) > 0.001f)
            {
                lastPlayerDirection = Mathf.Sign(distanceX);
            }

            var targetX = player.position.x - lastPlayerDirection * Mathf.Max(0f, targetDistance);

            var nextX = Mathf.MoveTowards(currentPosition.x, targetX, moveSpeed * deltaTime);

            transform.position = new Vector3(nextX, currentPosition.y, currentPosition.z);
        }

        /// <summary>
        /// 攻撃の有効判定開始を通知する。
        /// </summary>
        public void NotifyAttackWindowOpened(BossAttackType attackType)
        {
            AttackWindowOpened?.Invoke(attackType);
        }

        /// <summary>
        /// 攻撃の有効判定終了を通知する。
        /// </summary>
        public void NotifyAttackWindowClosed(BossAttackType attackType)
        {
            AttackWindowClosed?.Invoke(attackType);
        }

        /// <summary>
        /// 死亡アニメーションを再生する。
        /// </summary>
        public void PlayDeathAnimation()
        {
            if (animator != null && !string.IsNullOrEmpty(deathTrigger))
            {
                animator.SetTrigger(deathTrigger);
            }
        }

        /// <summary>
        /// 本体のHPが0になった際に死亡状態へ遷移する。
        /// </summary>
        private void HandleDeath()
        {
            StateMachine.ChangeState(BossStateType.Dead);
        }
    }
}
