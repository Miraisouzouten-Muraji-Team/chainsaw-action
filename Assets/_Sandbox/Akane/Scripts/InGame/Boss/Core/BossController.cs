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
        private readonly List<Collider> playerColliders = new();

        [Header("参照")]
        [SerializeField] private Transform player;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Animator animator;
        [SerializeField] private BossAttackHitboxController attackHitboxes;
        [SerializeField] private BossAttackVisualController attackVisuals;
        [SerializeField] private BossPhysicsMotor physicsMotor;
        [SerializeField] private BossShieldMotionController shieldMotion;
        [SerializeField] private BossLaserController laserController;

        public BossAttackHitboxController AttackHitboxes => attackHitboxes;
        public BossAttackVisualController AttackVisuals => attackVisuals;
        public BossPhysicsMotor PhysicsMotor => physicsMotor;
        public BossShieldMotionController ShieldMotion => shieldMotion;
        public BossLaserController LaserController => laserController;

        [Header("行動設定")]
        [SerializeField, Min(0f)] private float idleDuration = 2f;
        [SerializeField, Min(0f)] private float moveSpeed = 4f;

        [Header("盾なし時の行動設定")]
        [SerializeField, Min(0f)] private float noShieldIdleDuration = 1.5f;
        [SerializeField, Min(0f)] private float noShieldMoveSpeed = 6f;

        [Header("向きの設定")]
        [SerializeField] private float rightFacingAngle = 0f;
        [SerializeField] private float leftFacingAngle = -180f;
        [SerializeField, Min(0f)] private float rotationSpeed = 360f;

        [Header("攻撃設定")]
        [SerializeField] private List<BossAttackDefinition> attackDefinitions = new();

        [Header("アニメーション")]
        [SerializeField]
        private BossAnimationController animationController;

        public BossAnimationController Animation => animationController;

        [SerializeField] private BossAttackTelegraph attackTelegraph;
        [Header("攻撃力")]
        [SerializeField, Min(0f)] private float attackPower = 50f;

        /// <summary>
        /// ボスの基本攻撃力。
        /// </summary>
        public float AttackPower => attackPower;

        /// <summary>
        /// 現在の盾状態で攻撃ダメージを計算する。
        /// </summary>
        public float CalculateAttackDamage(BossAttackDefinition definition)
        {
            return CalculateAttackDamage(definition, Shield != null && Shield.IsActive);
        }

        /// <summary>
        /// 攻撃開始時の盾状態からダメージを確定する。
        /// </summary>
        public float CalculateAttackDamage(BossAttackDefinition definition, bool hasShield)
        {
            if (definition == null || !definition.IsConfigured)
            {
                return 0f;
            }

            return attackPower * definition.GetDamageMultiplier(hasShield);
        }

        private float lastPlayerDirection = 1f;
        private bool started;
        private EnemyPlayerReference playerReference;
        private Collider outerCollider;

        public BossAttackTelegraph Telegraph => attackTelegraph;
        public Transform Player => player;
        public Animator Animator => animator;
        public BossHealth Health { get; private set; }
        public BossShieldController Shield { get; private set; }
        public BossStateMachine StateMachine { get; private set; }
        public BossAttackSelector AttackSelector { get; private set; }
        public IBossAttackStrategy SelectedAttack { get; private set; }
        public float IdleDuration => Shield != null && Shield.IsActive ? idleDuration : noShieldIdleDuration;
        public float DistanceToPlayer => CalculateHorizontalSurfaceDistance();

        /// <summary>
        /// Playerの非Trigger Colliderを統合した中心座標を返す。
        /// </summary>
        public Vector3 GetPlayerAimPosition()
        {
            if (TryGetPlayerBounds(out Bounds playerBounds))
            {
                return playerBounds.center;
            }

            return player != null ? player.position : transform.position;
        }

        // 攻撃判定などの実装時に購読するイベント。
        public event Action<BossAttackType> AttackWindowOpened;
        public event Action<BossAttackType> AttackWindowClosed;

        private void Awake()
        {
            playerReference = new EnemyPlayerReference();
            outerCollider = GetComponent<Collider>();

            Health = GetComponent<BossHealth>();
            Shield = GetComponent<BossShieldController>();

            attackHitboxes ??= GetComponent<BossAttackHitboxController>();
            attackVisuals ??= GetComponent<BossAttackVisualController>();
            physicsMotor ??= GetComponent<BossPhysicsMotor>();
            shieldMotion ??= GetComponent<BossShieldMotionController>();
            laserController ??= GetComponent<BossLaserController>();

            // アニメーション制御の初期化。
            animationController ??= GetComponent<BossAnimationController>();
            animationController?.Initialize(animator);

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
            Shield.ShieldBroken += HandleShieldBroken;
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
            StopAllRuntimeActions();
        }

        private void OnDestroy()
        {
            if (Health != null)
            {
                Health.Died -= HandleDeath;
            }

            if (Shield != null)
            {
                Shield.ShieldBroken -= HandleShieldBroken;
            }

            StateMachine?.Dispose();
        }

        /// <summary>
        /// 盾を使う攻撃中に盾が壊れた場合、攻撃を終了する。
        /// </summary>
        private void HandleShieldBroken()
        {
            if (StateMachine == null || Health == null || Health.IsDead)
            {
                return;
            }

            if (StateMachine.CurrentType != BossStateType.Attack)
            {
                return;
            }

            if (SelectedAttack?.Definition.RequiresShield != true)
            {
                return;
            }

            StateMachine.ChangeState(BossStateType.Idle);
        }

        /// <summary>
        /// 参照を検証し、初期状態へ遷移する。
        /// </summary>
        private void BeginBoss()
        {
            if (!TryResolvePlayer())
            {
                Debug.LogError("BossController: Playerを解決できません。", this);
                enabled = false;
                return;
            }

            IgnoreOuterCollisionWithPlayer();
            laserController?.SetPlayer(player);

            if (AttackSelector.AttackCount == 0)
            {
                Debug.LogError("BossController: 攻撃設定がありません。", this);
                enabled = false;
                return;
            }

            if (attackHitboxes == null || attackVisuals == null ||
                physicsMotor == null || shieldMotion == null ||
                laserController == null)
            {
                Debug.LogError("BossController: 攻撃制御コンポーネントが不足しています。", this);
                enabled = false;
                return;
            }

            var initialState = Health.IsDead ? BossStateType.Dead : BossStateType.Idle;

            StateMachine.ChangeState(initialState);
        }

        /// <summary>
        /// Inspector参照がない場合は共通Player参照から再解決する。
        /// </summary>
        private bool TryResolvePlayer()
        {
            if (player != null) { return true; }

            return playerReference != null &&
                playerReference.TryGetPlayerTransform(out player);
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

            foreach (BossAttackDefinition definition in attackDefinitions)
            {
                if (definition == null)
                {
                    continue;
                }

                if (!definition.IsConfigured)
                {
                    Debug.LogError(
                        $"BossController: {definition.name}の攻撃設定が未完成です。",
                        definition);
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
        /// プレイヤーの方向へモデルの向きを変更する。
        /// </summary>
        public void FacePlayer(float deltaTime)
        {
            if (visualRoot == null || player == null) { return; }

            Quaternion targetRotation = GetPlayerFacingRotation();

            visualRoot.rotation = Quaternion.RotateTowards(
                visualRoot.rotation,
                targetRotation,
                rotationSpeed * deltaTime);
        }

        /// <summary>
        /// 攻撃開始時にPlayerのいる方向へ即座に向きを確定する。
        /// </summary>
        public void FacePlayerImmediately()
        {
            if (visualRoot == null || player == null) { return; }

            visualRoot.rotation = GetPlayerFacingRotation();
        }

        private Quaternion GetPlayerFacingRotation()
        {
            float distanceX = player.position.x - transform.position.x;

            if (Mathf.Abs(distanceX) > 0.001f)
            {
                lastPlayerDirection = Mathf.Sign(distanceX);
            }

            float targetAngle = lastPlayerDirection > 0f
                ? rightFacingAngle
                : leftFacingAngle;
            return Quaternion.Euler(0f, targetAngle, 0f);
        }

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

            float safeTargetDistance = Mathf.Max(0f, targetDistance);
            float targetX;

            if (outerCollider != null && TryGetPlayerBounds(out Bounds playerBounds))
            {
                if (lastPlayerDirection > 0f)
                {
                    float rootToRight = outerCollider.bounds.max.x - currentPosition.x;
                    targetX = playerBounds.min.x - safeTargetDistance - rootToRight;
                }
                else
                {
                    float rootToLeft = currentPosition.x - outerCollider.bounds.min.x;
                    targetX = playerBounds.max.x + safeTargetDistance + rootToLeft;
                }
            }
            else
            {
                targetX = player.position.x -
                    lastPlayerDirection * safeTargetDistance;
            }

            float currentMoveSpeed = Shield != null && Shield.IsActive ? moveSpeed : noShieldMoveSpeed;

            physicsMotor.SetHorizontalTarget(targetX, currentMoveSpeed);
        }

        /// <summary>
        /// 外周Collider同士のX軸方向の表面間距離を返す。
        /// </summary>
        private float CalculateHorizontalSurfaceDistance()
        {
            if (player == null) { return float.PositiveInfinity; }

            if (outerCollider == null || !TryGetPlayerBounds(out Bounds playerBounds))
            {
                return Mathf.Abs(player.position.x - transform.position.x);
            }

            Bounds bossBounds = outerCollider.bounds;

            if (bossBounds.max.x < playerBounds.min.x)
            {
                return playerBounds.min.x - bossBounds.max.x;
            }

            if (playerBounds.max.x < bossBounds.min.x)
            {
                return bossBounds.min.x - playerBounds.max.x;
            }

            return 0f;
        }

        private bool TryGetPlayerBounds(out Bounds bounds)
        {
            bounds = default;

            if (player == null) { return false; }

            playerColliders.Clear();
            player.GetComponentsInChildren(true, playerColliders);
            bool found = false;

            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider == null || playerCollider.isTrigger ||
                    !playerCollider.enabled)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = playerCollider.bounds;
                    found = true;
                    continue;
                }

                bounds.Encapsulate(playerCollider.bounds);
            }

            return found;
        }

        /// <summary>
        /// 地形用外周ColliderとPlayerの物理衝突だけを無効化する。
        /// </summary>
        private void IgnoreOuterCollisionWithPlayer()
        {
            if (outerCollider == null || player == null) { return; }

            playerColliders.Clear();
            player.GetComponentsInChildren(true, playerColliders);

            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider != null)
                {
                    Physics.IgnoreCollision(outerCollider, playerCollider, true);
                }
            }
        }

        /// <summary>
        /// 死亡・無効化時に実行中の攻撃と再生成を停止する。
        /// </summary>
        public void StopAllRuntimeActions()
        {
            animationController?.CancelAttack();

            attackHitboxes?.CloseAll();
            attackVisuals?.ResetPose();
            shieldMotion?.CancelMotion();
            laserController?.CancelAll();

            Shield?.CancelRestoration();
            physicsMotor?.EndScriptedMotion();
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
            animationController?.PlayDeath();
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
