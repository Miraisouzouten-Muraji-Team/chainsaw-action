using UnityEngine;

public class SensorPlayerController : MonoBehaviour
{
    [Header("プレイヤーの共通設定")]
    [Tooltip("移動・ジャンプ・食い込み・角補正の設定を読み取るPlayerParameterアセットを指定してください。実行中の速度や残り時間はこのアセットへ書き込みません。")]
    [SerializeField] private PlayerParameter playerParameter;

    PlayerInputHandler input;
    PlayerAnimator playerAnimator;

    [Header("食い込み")]
    [SerializeField] private SensorChainsawDigging chainsawDigging;
    [SerializeField] private ChainsawAccelerator chainsawAccelerator;

    public bool CanTakeDamage =>
        chainsawDigging == null || !chainsawDigging.IsEvading;

    private float facingDirection = 1f;
    public float FacingDirection => facingDirection;

    private bool jumpPending;
    private float pendingJumpPower;

    private float wallJumpStunRemaining;
    private float wallJumpDirectionHoldRemaining;
    private Vector3 pendingWallJumpVelocity;
    private float wallJumpHorizontalVelocity;
    private bool wallJumpLaunchPending;
    private float ignoreGroundUntil;
    private bool originalUseGravity;

    float currentSpeed = 0.0f;

    // 空中から開始した場合に猶予を与えないため、未接地で初期化する。
    private float lastGroundedTime = float.NegativeInfinity;


    [Header("3本Rayによる接地判定")]
    [Tooltip("足元中央に配置した空のGameObjectのTransform。ここを基準に中央・前・後ろへRayを飛ばします。")]
    [SerializeField] private Transform groundCheckTransform;

    // Rayによる接地判定は実行時に更新する。SOには状態を書き込まない。
    private bool isGroundedByRay;
    private const int MAX_JUMP_COUNT = 2;

    private Rigidbody playerRigidbody;


    [Header("見た目の向き")]
    [Tooltip("モデルとチェーンソーを含む見た目の親")]
    [SerializeField] private Transform visualRoot;

    private Quaternion rightFacingRotation;

    // 空中で開始した攻撃は、コンボ終了までその場に留まる。
    private bool attackGravityOff;

    private int jumpsUsed;
    private int pendingJumpCount;
    private float ascentStartSpeed;
    private float fallElapsedTime;

    [Header("食い込み動作確認（実行中に自動更新）")]
    [SerializeField] private float debugRotationSpeed;
    [SerializeField] private float debugSpeedBonus;
    [SerializeField] private float debugTargetSpeed;
    [SerializeField] private float debugCurrentSpeed;

    private float wallTravelSpeed;

    private float knockbackVelocity;

    private bool knockbackHopPending;

    // 各段の地上攻撃の踏み込み。
    private float attackMoveTimeRemaining;
    private float attackMoveSpeed;
    private float attackMoveDirection;

    [Header("攻撃データ")]
    [SerializeField] AttackData slash1;
    [SerializeField] AttackData slash2;
    [SerializeField] AttackData slash3;

    public AttackData CurrentAttackData { get; private set; }

    // コンボの次段へ移る再生位置はPlayerParameterから取得する。
    private float ComboAdvanceTime => playerParameter.comboAdvanceTime;

    [Header("食い込み中の強衝突：硬直とカメラシェイク")]
    [SerializeField]
    private bool enableDiggingImpact = true;

    [Tooltip("NothingならDetectorのTerrain Layersを使用。指定する場合は敵を含めない")]
    [SerializeField]
    private LayerMask impactTerrainLayers;

    [SerializeField]
    private CameraShake_System impactCameraShake;

    [SerializeField]
    private bool logDiggingImpact = true;

    public bool IsImpactStunned => impactStunRemaining > 0f;

    private int ResolvedImpactTerrainLayers =>
        impactTerrainLayers.value != 0
            ? impactTerrainLayers.value
            : chainsawDigging != null
                ? chainsawDigging.TerrainLayerMask
                : 0;

    private bool CanUseDiggingImpact =>
        playerParameter != null &&
        enableDiggingImpact &&
        ResolvedImpactTerrainLayers != 0 &&
        playerParameter.diggingImpactStunDuration > 0f;

    private bool wallAscentImpactArmed;
    private bool floorTravelImpactArmed;
    private float floorTravelImpactDirection;
    private bool impactSettingsChecked;

    private float impactStunRemaining;
    private Vector3 lastDiggingPhysicsVelocity;
    private ChainsawSurface lastDiggingPhysicsSurface;
    private PlayeMidCollider playerMidCollider;
    [Header("角の補正")]
    [Tooltip("ONにすると、ステージの角に軽く触れたときに止まらず、ずらして通過させます。OFFなら従来どおりの動きになります。")]
    [SerializeField]
    private bool enableCornerCorrection = true;

    // 次の物理更新で適用する補正量。x＝天井の角の横ずらし、y＝床の角の持ち上げ。
    private Vector3 pendingCornerCorrection;

    [Tooltip("頭部の物理Colliderを指定。ここが天井角に接触したときだけ横補正します。")]
    [SerializeField] private Collider headCollider;

    // 補正直後に天井の強衝突が発火するのを防ぐ短い猶予。
    private float ceilingCornerGraceUntil;

    // 衝突イベントから次のFixedUpdateにジャンプを予約する。
    private bool ceilingCornerJumpPending;

    // これより小さい重なりは、床の継ぎ目などの誤差として補正しない。
    private const float MIN_CORNER_OVERLAP = 0.02f;

    // 角から完全に外れるための、わずかな余白。
    private const float CORNER_EXTRA_MARGIN = 0.005f;

    private RigidbodyConstraints constraintsBeforeImpact;
    private bool impactConstraintsHeld;
    private float impactBounceDirection;
    public bool IsAttacking { get; private set; }

    int slashStep = 0;
    bool nextSlashReserved;
    int attackStartFrame;
    bool attackStateObserved;
    float stateWaitTime;

    // 必要なコンポーネントと設定アセットを確認し、物理移動を初期化する。
    void Awake()
    {
        input = GetComponent<PlayerInputHandler>();
        playerAnimator = GetComponent<PlayerAnimator>();
        playerRigidbody = GetComponent<Rigidbody>();

        // Rayの発射位置は専用Transformのみで決める。物理Colliderは参照しない。
        if (groundCheckTransform == null)
        {
            Debug.LogError("3本Ray接地判定用のGround Check Transform（足元中央）を設定してください。", this);
            enabled = false;
            return;
        }

        // 壁の接触記録用コンポーネントを取得する。
        // 未配置なら同じGameObjectへ追加する。物理Colliderは追加しない。
        playerMidCollider = GetComponent<PlayeMidCollider>();

        if (playerMidCollider == null)
        {
            playerMidCollider = gameObject.AddComponent<PlayeMidCollider>();
        }

        if (chainsawDigging == null)
        {
            chainsawDigging = GetComponent<SensorChainsawDigging>();
        }

        if (chainsawAccelerator == null)
        {
            chainsawAccelerator =
                GetComponentInChildren<ChainsawAccelerator>();
        }

        if (input == null ||
            playerAnimator == null ||
            playerRigidbody == null ||
            chainsawDigging == null ||
            chainsawAccelerator == null)
        {
            Debug.LogError(
                "PlayerInputHandler・PlayerAnimator・Rigidbodyを" +
                "同じGameObjectに配置し、SensorChainsawDiggingと" +
                "ChainsawAcceleratorも設定してください。",
                this
            );

            enabled = false;
            return;
        }

        originalUseGravity = playerRigidbody.useGravity;

        // 設定アセットがない場合は、参照エラーになる前にControllerを停止する。
        // useGravityの元の値を保存した後に止めるため、OnDisableでも正しく復元できる。
        if (playerParameter == null)
        {
            Debug.LogError("SensorPlayerControllerのPlayer Parameterに、PlayerParameterアセットを設定してください。", this);
            enabled = false;
            return;
        }

        playerRigidbody.useGravity = false;

        if (playerRigidbody.isKinematic)
        {
            Debug.LogError(
                "PlayerのRigidbodyのIs KinematicをOFFにしてください。",
                this
            );
        }

        if (visualRoot != null)
        {
            rightFacingRotation = visualRoot.localRotation;
        }
    }

    void OnEnable()
    {
        if (playerRigidbody != null)
        {
            playerRigidbody.useGravity = false;
        }
    }

    void Update()
    {
        if (IsImpactStunned || wallJumpStunRemaining > 0f)
        {
            input.ResetInput();
            return;
        }
        // ヒットストップ中はコンボ予約だけ受け付ける。
        if (Time.timeScale <= 0f)
        {
            if (IsAttacking && input.SlashInput)
            {
                Slash();
            }

            input.ResetInput();
            return;
        }

        if (chainsawDigging.Surface != ChainsawSurface.Floor &&
            chainsawDigging.Surface != ChainsawSurface.Ceiling &&
            input.MoveInput != 0f)
        {
            facingDirection = Mathf.Sign(input.MoveInput);
            ApplyFacingRotation();
        }

        chainsawDigging.SetFacingDirection(facingDirection);
        chainsawDigging.HandleInput(
            input.WedgieInput,
            input.WedgieHeld,
            IsAttacking,
            facingDirection
        );

        // 同時押しの優先順位：弱攻撃 > ジャンプ > 食い込み。
        if (input.SlashInput)
        {
            Slash();
        }
        else if (!IsAttacking && input.JumpInput)
        {
            Jump();
        }

        UpdateSlash();
        input.ResetInput();
    }

    // PlayerParameterの設定を読み、食い込み・ジャンプ・移動を物理更新する。
    private void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;

        if (playerRigidbody == null || playerRigidbody.isKinematic)
        {
            ascentStartSpeed = 0f;
            fallElapsedTime = 0f;
            return;
        }

        // 3本のRayで着地を確認し、空中ジャンプ回数を回復する。
        RefreshGroundRayState();

        if (chainsawAccelerator.isActiveAndEnabled)
        {
            chainsawAccelerator.Tick(
                input.AccelerateHeld,
                deltaTime,
                chainsawDigging.IsDigging
            );
        }

        if (TickDiggingImpactStun(deltaTime))
        {
            return;
        }

        // 壁ジャンプ前の硬直中は操作による移動を停止する。
        if (TickWallJumpStun(deltaTime))
        {
            return;
        }

        CheckDiggingImpactSettings();

        // 無効化・削除された壁の接触記録を更新する。
        playerMidCollider.RefreshContacts(ResolvedImpactTerrainLayers);

        // 前の物理ステップで検出した頭部の角接触を先に補正する。
        ApplyPendingCornerCorrection(deltaTime);

        // 食い込み状態の更新前に、前回の移動で天井に触れたか確認する。
        if (TryBeginUpperSensorImpact())
        {
            return;
        }

        ChainsawSurface previousSurface = chainsawDigging.Surface;
        float previousMoveSpeed = Mathf.Abs(currentSpeed);

        chainsawDigging.Tick(
            deltaTime,
            facingDirection,
            CanUseDiggingImpact
                ? Mathf.Max(0.01f, playerParameter.surfaceStickCollisionSpeed)
                : float.PositiveInfinity,
            currentSpeed,
            ResolvedImpactTerrainLayers
        );

        if (chainsawDigging.TryTakeWallBounce(out float bounceDirection))
        {
            knockbackVelocity = bounceDirection * playerParameter.wallKnockbackSpeed;
            knockbackHopPending = true;
        }

        bool enteredWall =
            previousSurface != ChainsawSurface.Wall &&
            chainsawDigging.Surface == ChainsawSurface.Wall;

        if (enteredWall)
        {
            wallTravelSpeed =
                previousSurface == ChainsawSurface.Floor
                    ? previousMoveSpeed
                    : playerParameter.wallClimbSpeed;
        }

        // 食い込み状態と押し戻しの予約が確定してから猶予を更新する。
        UpdateCoyoteTime();

        Move();
        bool isKnockbackActive =
            knockbackVelocity != 0f || knockbackHopPending;

        // 押し戻し中は移動入力より押し戻し速度を優先。
        if (knockbackVelocity != 0f)
        {
            currentSpeed = knockbackVelocity;

            knockbackVelocity = Mathf.MoveTowards(
                knockbackVelocity,
                0f,
                playerParameter.wallKnockbackDeceleration * deltaTime
            );
        }

        if (attackGravityOff && !chainsawDigging.IsDigging)
        {
            currentSpeed = 0f;
        }

        Vector3 velocity = playerRigidbody.linearVelocity;
        velocity.x = currentSpeed;

        if (chainsawDigging.TryTakeDash(out float power))
        {
            currentSpeed += facingDirection * power;
            velocity.x = currentSpeed;
        }

        bool suppressGravity = chainsawDigging.SuppressGravity;

        if (suppressGravity)
        {
            ascentStartSpeed = 0f;
            fallElapsedTime = 0f;

            switch (chainsawDigging.Surface)
            {
                case ChainsawSurface.Wall:
                    {
                        currentSpeed = 0f;
                        Vector3 normal = chainsawDigging.SurfaceNormal;
                        Vector3 tangent = new Vector3(normal.y, -normal.x, 0f).normalized;
                        if (tangent.y < 0f) tangent = -tangent;
                        velocity = tangent * wallTravelSpeed - normal * playerParameter.surfaceStickSpeed;
                        break;
                    }

                case ChainsawSurface.Floor:
                case ChainsawSurface.Ceiling:
                    {
                        Vector3 normal = chainsawDigging.SurfaceNormal;
                        Vector3 tangent = new Vector3(normal.y, -normal.x, 0f).normalized;
                        if (tangent.x < 0f) tangent = -tangent;
                        // currentSpeedは斜面に沿った速度として使う。
                        velocity = tangent * currentSpeed - normal * playerParameter.surfaceStickSpeed;
                        if (chainsawDigging.Surface == ChainsawSurface.Floor)
                            jumpsUsed = 0;
                        break;
                    }

                case ChainsawSurface.Enemy:
                    {
                        currentSpeed = 0f;
                        velocity.x = 0f;
                        velocity.y = 0f;
                        break;
                    }
            }
        }

        if (jumpPending)
        {
            jumpPending = false;
            velocity.y = pendingJumpPower;
            jumpsUsed = pendingJumpCount;

            ascentStartSpeed = Mathf.Max(pendingJumpPower, 0f);
            fallElapsedTime = 0f;
            ignoreGroundUntil = Time.time + 0.1f;
        }

        if (knockbackHopPending)
        {
            knockbackHopPending = false;

            velocity.y = playerParameter.wallKnockbackUpwardForce;
            ascentStartSpeed = playerParameter.wallKnockbackUpwardForce;
            fallElapsedTime = 0f;
            ignoreGroundUntil = Time.time + 0.1f;
        }

        if (attackGravityOff)
        {
            velocity.y = 0f;
            ascentStartSpeed = 0f;
            fallElapsedTime = 0f;
        }
        else if (!suppressGravity)
        {
            ApplyJumpGravity(ref velocity, deltaTime);
        }

        // 踏み込みは通常移動とは別に加算する。
        if (isKnockbackActive ||
            attackGravityOff ||
            chainsawDigging.IsDigging)
        {
            ClearAttackMovement();
        }
        else
        {
            velocity.x += TakeAttackMoveSpeed(deltaTime);
        }

        // 壁ジャンプの初速は通常移動・食い込み速度の計算後に上書きする。
        if (wallJumpLaunchPending)
        {
            wallJumpLaunchPending = false;
            jumpPending = false;
            velocity.x = pendingWallJumpVelocity.x;
            velocity.y = pendingWallJumpVelocity.y;
            wallJumpHorizontalVelocity = velocity.x;
            wallJumpDirectionHoldRemaining = playerParameter.surfaceStickJumpNoOverrideTime;
            currentSpeed = velocity.x;
            ascentStartSpeed = Mathf.Max(velocity.y, 0f);
            fallElapsedTime = 0f;
            ignoreGroundUntil = Time.time + 0.1f;
        }
        else if (wallJumpDirectionHoldRemaining > 0f)
        {
            // 壁から離れる初速を維持し、通常移動による即時上書きを防止する。
            velocity.x = wallJumpHorizontalVelocity;
            currentSpeed = velocity.x;
            wallJumpDirectionHoldRemaining = Mathf.Max(
                0f, wallJumpDirectionHoldRemaining - deltaTime);
        }

        // 角に接触して横補正したときだけ、上向きのジャンプも加える。
        // 速度決定の最後で適用し、重力計算や食い込み速度で上書きされないようにする。
        if (ceilingCornerJumpPending)
        {
            ceilingCornerJumpPending = false;
            velocity.y = Mathf.Max(velocity.y, playerParameter.cornerJumpForce);
            ascentStartSpeed = Mathf.Max(ascentStartSpeed, velocity.y);
            fallElapsedTime = 0f;
            ignoreGroundUntil = Time.time + 0.1f;
        }

        velocity.z = 0f;

        // 床の食い込みから続く移動を記録する。
        UpdateFloorTravelImpact(velocity);
        // 物理衝突によって速度が0になる前の移動速度。
        lastDiggingPhysicsVelocity = velocity;
        lastDiggingPhysicsSurface = chainsawDigging.Surface;

        // 壁登りによる上昇を記録する。
        if (lastDiggingPhysicsSurface == ChainsawSurface.Wall &&
            velocity.y > 0f)
        {
            wallAscentImpactArmed = true;
        }
        else if (
            velocity.y <= 0f ||
            lastDiggingPhysicsSurface != ChainsawSurface.None)
        {
            // 下降開始、または別の面への食い込みで解除する。
            wallAscentImpactArmed = false;
        }

        // SurfaceがNoneでも上昇中なら、直前の壁登りの記録を維持する。
        debugCurrentSpeed = velocity.x;

        // 硬直・シェイクの判定には制限前の速度を残す。
        // Rigidbodyへ渡す実際の移動速度だけを、最後に制限する。
        ApplyWallMovementBlock(ref velocity);
        playerRigidbody.linearVelocity = velocity;

        playerAnimator.SetSpeed(
            knockbackVelocity != 0f ? 0f : currentSpeed
        );
    }

    // 設定アセットの移動速度と加減速を使い、現在速度を更新する。
    void Move()
    {
        bool isAutoMoving =
            chainsawDigging.Surface == ChainsawSurface.Floor ||
            chainsawDigging.Surface == ChainsawSurface.Ceiling;

        float moveInput = isAutoMoving
            ? facingDirection
            : Mathf.Clamp(input.MoveInput, -1f, 1f);

        float speedBonus = chainsawDigging.MoveSpeedBonus;
        float targetSpeed = moveInput * (playerParameter.moveSpeed + speedBonus);

        bool isReversing =
            (moveInput > 0f && currentSpeed < 0f) ||
            (moveInput < 0f && currentSpeed > 0f);

        if (isReversing)
        {
            currentSpeed = 0f;
        }

        float rate =
            Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed)
                ? playerParameter.acceleration
                : playerParameter.deceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * Time.fixedDeltaTime
        );

        debugRotationSpeed = chainsawAccelerator.CurrentSpeed;
        debugSpeedBonus = speedBonus;
        debugTargetSpeed = targetSpeed;
    }

    // 壁ジャンプの硬直時間をFixedUpdateで進め、終了したらジャンプを予約する。
    private bool TickWallJumpStun(float deltaTime)
    {
        if (wallJumpStunRemaining <= 0f)
            return false;

        wallJumpStunRemaining = Mathf.Max(0f, wallJumpStunRemaining - deltaTime);
        playerRigidbody.linearVelocity = Vector3.zero;
        currentSpeed = 0f;
        input.ResetInput();
        playerAnimator.SetSpeed(0f);

        if (wallJumpStunRemaining <= 0f)
            wallJumpLaunchPending = true;

        // 硬直終了した同フレームも通常移動を行わず、次の物理更新で飛ぶ。
        return true;
    }

    // 壁ジャンプ、接地・猶予中の初段、二段目の順でジャンプを予約する。
    void Jump()
    {
        if (jumpPending)
        {
            return;
        }

        // 壁ジャンプ待機中は二重入力を受け付けない。
        if (wallJumpStunRemaining > 0f || wallJumpLaunchPending)
            return;

        // 食い込み解除前の状態と法線を保存する。
        ChainsawSurface surfaceBeforeJump = chainsawDigging.Surface;
        Vector3 wallNormal = chainsawDigging.SurfaceNormal;

        bool wasDiggingFloor =
            surfaceBeforeJump == ChainsawSurface.Floor;

        bool wallJump =
            chainsawDigging.TryGetWallJump(out _);

        // 壁食い込み中は独立した壁ジャンプ速度と角度から初速を計算する。
        if (wallJump && surfaceBeforeJump == ChainsawSurface.Wall &&
            Mathf.Abs(wallNormal.x) > 0.01f)
        {
            float horizontalDirection = Mathf.Sign(wallNormal.x);
            float angleRadians = playerParameter.surfaceStickJumpAngle * Mathf.Deg2Rad;
            pendingWallJumpVelocity = new Vector3(
                horizontalDirection * Mathf.Cos(angleRadians) * playerParameter.surfaceStickJumpForce,
                Mathf.Sin(angleRadians) * playerParameter.surfaceStickJumpForce,
                0f);

            chainsawDigging.Cancel(true);
            pendingJumpCount = 1;
            jumpsUsed = 1;
            jumpPending = false;
            currentSpeed = 0f;
            knockbackVelocity = 0f;
            knockbackHopPending = false;
            lastGroundedTime = float.NegativeInfinity;
            wallJumpStunRemaining = playerParameter.surfaceStickJumpStunTime;
            wallJumpDirectionHoldRemaining = 0f;
            wallJumpLaunchPending = playerParameter.surfaceStickJumpStunTime <= 0f;
            playerRigidbody.linearVelocity = Vector3.zero;
            // 壁ジャンプ専用アニメーションを再生する。硬直と跳躍処理は変更しない。
            playerAnimator.PlayWallJump();
            return;
        }

        chainsawDigging.Cancel(true);

        // ジャンプ入力の瞬間にも更新し、直前の物理フレームの判定ズレを防ぐ。
        RefreshGroundRayState();
        bool isGrounded = wasDiggingFloor || isGroundedByRay;

        // 初段をまだ使っておらず、最後の接地から設定時間以内なら許可する。
        // 壁・天井・敵への食い込み中や押し戻し中には適用しない。
        bool canUseCoyoteTime =
            surfaceBeforeJump == ChainsawSurface.None &&
            playerParameter.coyoteTime > 0f &&
            jumpsUsed == 0 &&
            !IsImpactStunned &&
            !knockbackHopPending &&
            knockbackVelocity == 0f &&
            Time.time - lastGroundedTime <= playerParameter.coyoteTime;

        // 壁ジャンプの判定が成立しているのに法線が取れない場合、
        // 通常ジャンプへ置き換えず、真上に飛ぶ旧挙動を防ぐ。
        if (wallJump)
        {
            Debug.LogWarning("壁ジャンプの方向を確定できませんでした。壁のSurfaceNormalを確認してください。", this);
            return;
        }

        if (isGrounded || canUseCoyoteTime)
        {
            // 猶予中も、通常の初段と同じ強さ・回数でジャンプする。
            pendingJumpPower = playerParameter.jumpForce;
            pendingJumpCount = 1;
        }
        else
        {
            // 猶予を過ぎた場合は、従来どおり二段目として扱う。
            int effectiveJumpCount = Mathf.Max(jumpsUsed, 1);

            if (effectiveJumpCount >= MAX_JUMP_COUNT)
            {
                return;
            }

            pendingJumpPower = playerParameter.airJumpForce;
            pendingJumpCount = effectiveJumpCount + 1;
        }

        jumpPending = true;

        // 予約時点で猶予を消費し、初段を連続使用できないようにする。
        lastGroundedTime = float.NegativeInfinity;

        // 確定したジャンプ回数に応じて、初段と二段目のアニメーションを切り替える。
        if (pendingJumpCount >= MAX_JUMP_COUNT)
        {
            playerAnimator.PlayDoubleJump();
        }
        else
        {
            playerAnimator.PlayJump();
        }
    }

    // 足元の中央・前・後ろの3本Rayで床／斜面を調べ、着地時にジャンプ回数を戻す。
    private void RefreshGroundRayState()
    {
        isGroundedByRay = false;

        if (groundCheckTransform == null || playerParameter == null ||
            playerRigidbody == null || !groundCheckTransform.gameObject.activeInHierarchy ||
            jumpPending || wallJumpLaunchPending || wallJumpStunRemaining > 0f ||
            Time.time < ignoreGroundUntil ||
            playerRigidbody.linearVelocity.y > 0.1f)
        {
            return;
        }

        int groundMask = playerParameter.groundRayLayers.value != 0
            ? playerParameter.groundRayLayers.value
            : ResolvedImpactTerrainLayers;
        if (groundMask == 0)
        {
            return;
        }

        float sideOffset = Mathf.Max(0f, playerParameter.groundRaySideOffset);
        float startHeight = Mathf.Max(0.01f, playerParameter.groundRayStartHeight);
        float distance = Mathf.Max(0.01f, playerParameter.groundRayDistance);
        float normalThreshold = Mathf.Clamp01(playerParameter.groundRayNormalThreshold);
        // 足元中央のTransformを原点として、上に開始位置をずらす。
        Vector3 center = groundCheckTransform.position + Vector3.up * startHeight;

        // 真ん中・前・後ろ。向きが変わっても同じ3か所を検出する。
        for (int index = 0; index < 3; index++)
        {
            float offset = index == 0 ? 0f :
                index == 1 ? sideOffset * facingDirection :
                -sideOffset * facingDirection;
            Vector3 origin = center + Vector3.right * offset;
            RaycastHit[] hits = Physics.RaycastAll(
                origin, Vector3.down, distance,
                groundMask, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null ||
                    hit.collider.transform == transform ||
                    hit.collider.transform.IsChildOf(transform) ||
                    hit.normal.y < normalThreshold)
                {
                    continue;
                }

                // Rayが遠方の床を発見しただけでは接地にしない。
                // GroundCheckが足元にある前提で、実際の足元と床との隙間を評価する。
                // 探索用Rayの距離とは別に、接地確定の許容距離を設定する。
                float groundGap = hit.distance - startHeight;
                float contactTolerance = Mathf.Max(0f, playerParameter.groundRayContactTolerance);

                // 足元より上の面を「着地」として採用しない。
                // 側面の段差・壁際で横のRayが上段の床を拾うと、
                // 旧実装ではgroundGapが負でも接地が成立していた。
                // 床の探索距離は変えず、接地できる高さの範囲だけ制限する。
                if (groundGap < -contactTolerance || groundGap > contactTolerance)
                {
                    continue;
                }

                isGroundedByRay = true;
                jumpsUsed = 0;
                return;
            }
        }
    }

    // Sceneビューで3本の接地Rayの位置と長さを表示する。
    private void OnDrawGizmosSelected()
    {
        if (groundCheckTransform == null || playerParameter == null)
            return;

        float offset = Mathf.Max(0f, playerParameter.groundRaySideOffset);
        float height = Mathf.Max(0.01f, playerParameter.groundRayStartHeight);
        float distance = Mathf.Max(0.01f, playerParameter.groundRayDistance);
        Vector3 center = groundCheckTransform.position + Vector3.up * height;
        Gizmos.color = Application.isPlaying && isGroundedByRay ? Color.green : Color.yellow;
        for (int i = -1; i <= 1; i++)
        {
            Vector3 origin = center + Vector3.right * offset * i;
            Gizmos.DrawLine(origin, origin + Vector3.down * distance);
        }
    }

    // 設定アセットの重力倍率と落下上限を使い、上下速度を更新する。
    private void ApplyJumpGravity(
        ref Vector3 velocity,
        float deltaTime)
    {
        bool isGrounded = isGroundedByRay && velocity.y <= 0.1f;

        float gravityMultiplier;

        if (isGrounded)
        {
            ascentStartSpeed = 0f;
            fallElapsedTime = 0f;
            gravityMultiplier = 1f;
        }
        else if (velocity.y > 0f)
        {
            fallElapsedTime = 0f;
            ascentStartSpeed = Mathf.Max(
                ascentStartSpeed,
                velocity.y
            );

            float ascentProgress = 1f - Mathf.Clamp01(
                velocity.y /
                Mathf.Max(ascentStartSpeed, 0.001f)
            );

            gravityMultiplier = Mathf.SmoothStep(
                playerParameter.jumpStartGravityMultiplier,
                1f,
                ascentProgress
            );
        }
        else
        {
            ascentStartSpeed = 0f;

            float fallProgress = Mathf.Clamp01(
                fallElapsedTime /
                Mathf.Max(playerParameter.fallGravityIncreaseTime, 0.01f)
            );

            gravityMultiplier = Mathf.SmoothStep(
                1f,
                playerParameter.maxFallGravityMultiplier,
                fallProgress
            );

            fallElapsedTime += deltaTime;
        }

        velocity +=
            Physics.gravity *
            playerParameter.gravityScale *
            gravityMultiplier *
            deltaTime;

        velocity.y = Mathf.Max(velocity.y, -playerParameter.maxFallSpeed);
    }

    private bool IsGrounded()
    {
        if (chainsawDigging != null && chainsawDigging.Surface == ChainsawSurface.Floor)
            return true;

        return isGroundedByRay;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        // 頭部が天井の端に軽く当たったときだけ、横移動を予約する。
        bool correctingCeilingCorner = TryQueueCeilingCornerCorrection(collision);
        if (!correctingCeilingCorner)
            TryBeginDiggingImpact(collision);

        UpdateWallContact(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        // 同じ地形への接触中も、角の状態を判定し直す。
        bool correctingCeilingCorner = TryQueueCeilingCornerCorrection(collision);
        if (!correctingCeilingCorner)
            TryBeginDiggingImpact(collision);

        UpdateWallContact(collision);
    }

    void OnCollisionExit(Collision collision)
    {
        if (playerMidCollider != null)
        {
            playerMidCollider.RemoveContact(
                collision.collider,
                ResolvedImpactTerrainLayers
            );
        }
    }

    [Header("天井角補正の確認")]
    [SerializeField] private bool logCornerCorrection = false;

    // BoxColliderの天井端を使って、横方向の食い込み量を算出する。
    // Raycastは角で失敗しやすいため、BoxColliderでは使わない。
    private bool TryQueueCeilingCornerCorrection(Collision collision)
    {
        if (!enableCornerCorrection || headCollider == null ||
            !headCollider.enabled || headCollider.isTrigger ||
            playerRigidbody == null || IsImpactStunned ||
            Time.time < ceilingCornerGraceUntil)
            return false;

        float upwardSpeed = Mathf.Max(playerRigidbody.linearVelocity.y,
                                      lastDiggingPhysicsVelocity.y);
        if (upwardSpeed <= 0.05f)
            return false;

        Collider terrain = collision.collider;
        if (terrain == null || terrain.isTrigger ||
            (ResolvedImpactTerrainLayers & (1 << terrain.gameObject.layer)) == 0)
            return false;

        bool headTouched = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.thisCollider == headCollider)
            {
                headTouched = true;
                break;
            }
        }
        if (!headTouched)
            return false;

        Bounds head = headCollider.bounds;
        Bounds ceiling = terrain.bounds;

        // 現在の角補正は、回転していないBoxCollider地形に限定する。
        // MeshColliderや斜面はboundsだけでは誤検出するため補正しない。
        BoxCollider box = terrain as BoxCollider;
        if (box == null ||
            Mathf.Abs(Vector3.Dot(terrain.transform.right, Vector3.right)) < 0.999f ||
            Mathf.Abs(Vector3.Dot(terrain.transform.up, Vector3.up)) < 0.999f)
        {
            if (logCornerCorrection)
                Debug.Log("[天井角補正] 地形が軸平行BoxColliderではないため未対応: " + terrain.name, this);
            return false;
        }

        // 頭が天井下面に近い場合のみ。真横の壁接触は対象外。
        float underside = ceiling.min.y;
        if (underside < head.center.y - 0.02f ||
            underside > head.max.y + 0.06f)
            return false;

        // 頭の中央まで天井に入っていたら平面衝突として扱う。
        float overlap;
        float direction;
        if (head.center.x < ceiling.min.x && head.max.x > ceiling.min.x)
        {
            overlap = head.max.x - ceiling.min.x;
            direction = -1f;
        }
        else if (head.center.x > ceiling.max.x && head.min.x < ceiling.max.x)
        {
            overlap = ceiling.max.x - head.min.x;
            direction = 1f;
        }
        else
            return false;

        if (overlap < MIN_CORNER_OVERLAP || overlap > playerParameter.cornerTolerance)
        {
            if (logCornerCorrection)
                Debug.Log($"[天井角補正] 重なりが範囲外: {overlap:F3} / 許容 {playerParameter.cornerTolerance:F3}", this);
            return false;
        }

        float correctionX = direction * (overlap + CORNER_EXTRA_MARGIN);
        if (Mathf.Abs(correctionX) > Mathf.Abs(pendingCornerCorrection.x))
            pendingCornerCorrection.x = correctionX;

        ceilingCornerJumpPending = true;
        ceilingCornerGraceUntil = Time.time + 0.12f;
        if (logCornerCorrection)
            Debug.Log($"[天井角補正] 成功: {terrain.name} 横補正 {correctionX:F3}", this);
        return true;
    }

    // 設定アセットの押し出し速度で、予約済みの天井角補正を進める。
    private void ApplyPendingCornerCorrection(float deltaTime)
    {
        if (pendingCornerCorrection == Vector3.zero)
            return;

        float amount = Mathf.MoveTowards(0f, pendingCornerCorrection.x,
                                        playerParameter.cornerPushSpeed * deltaTime);
        pendingCornerCorrection.x -= amount;
        // 補正はプレイヤーのRigidbody全体に適用する。
        playerRigidbody.MovePosition(playerRigidbody.position + Vector3.right * amount);

        if (Mathf.Abs(pendingCornerCorrection.x) < 0.0001f)
            pendingCornerCorrection = Vector3.zero;
    }

    private void UpdateWallContact(Collision collision)
    {
        if (playerMidCollider == null)
        {
            return;
        }

        playerMidCollider.RecordContact(
            collision,
            ResolvedImpactTerrainLayers
        );

        // 衝突したフレームにも、壁方向の移動と歩行速度を止める。
        Vector3 velocity = playerRigidbody.linearVelocity;

        if (ApplyWallMovementBlock(ref velocity))
        {
            playerRigidbody.linearVelocity = velocity;
            playerAnimator.SetSpeed(0f);
            debugCurrentSpeed = velocity.x;
        }
    }

    private bool ApplyWallMovementBlock(ref Vector3 velocity)
    {
        // 硬直・壁登り・敵への食い込みは既存の専用処理を優先する。
        if (playerMidCollider == null ||
            IsImpactStunned ||
            chainsawDigging.Surface == ChainsawSurface.Wall ||
            chainsawDigging.Surface == ChainsawSurface.Enemy)
        {
            return false;
        }

        // 壁へ向かう横移動だけを止める。
        // 上下移動と、壁から離れる方向への移動は残す。
        bool blocked = playerMidCollider.IsBlocked(velocity.x);

        if (blocked)
        {
            velocity.x = 0f;
        }

        // 歩行アニメーションに渡す速度も止める。
        if (playerMidCollider.IsBlocked(currentSpeed))
        {
            currentSpeed = 0f;
            blocked = true;
        }

        // 押し戻された先にも壁がある場合は、その方向への移動を止める。
        if (playerMidCollider.IsBlocked(knockbackVelocity))
        {
            knockbackVelocity = 0f;
        }

        // 攻撃自体は継続し、壁方向の踏み込みだけを終了する。
        if (playerMidCollider.IsBlocked(attackMoveDirection))
        {
            ClearAttackMovement();
            blocked = true;
        }

        return blocked;
    }

    private void ApplyFacingRotation()
    {
        if (visualRoot == null)
        {
            return;
        }

        float angle = facingDirection < 0f ? 180f : 0f;

        visualRoot.localRotation =
            rightFacingRotation *
            Quaternion.Euler(0f, angle, 0f);
    }

    void Slash()
    {
        if (!IsAttacking)
        {
            jumpPending = false;
            bool groundedBeforeAttack = IsGrounded();
            chainsawDigging.Cancel(true);

            attackGravityOff = !groundedBeforeAttack;
            StartSlash(1);
        }
        else if (slashStep < 3)
        {
            nextSlashReserved = true;
        }
    }

    void UpdateSlash()
    {
        if (!IsAttacking ||
            Time.frameCount == attackStartFrame)
        {
            return;
        }

        if (Time.timeScale <= 0f)
        {
            return;
        }

        if (!playerAnimator.TryGetAttackProgress(
                out float progress))
        {
            stateWaitTime += Time.deltaTime;

            if (attackStateObserved || stateWaitTime > 0.5f)
            {
                Debug.LogWarning(
                    "攻撃が中断されたか、攻撃ステートを再生できませんでした。" +
                    "Animator設定を確認してください。",
                    this
                );

                CancelAttack();
            }

            return;
        }

        attackStateObserved = true;

        if (slashStep < 3 &&
            nextSlashReserved &&
            progress >= ComboAdvanceTime)
        {
            StartSlash(slashStep + 1);
        }
        else if (progress >= 1f)
        {
            FinishSlash();
        }
    }

    void StartSlash(int step)
    {
        AttackData data =
            step == 1 ? slash1 :
            step == 2 ? slash2 :
            slash3;

        if (!playerAnimator.StartSlash(step, data))
        {
            CancelAttack();
            return;
        }

        slashStep = step;
        CurrentAttackData = data;

        BeginAttackMovement(data);

        IsAttacking = true;
        nextSlashReserved = false;

        attackStartFrame = Time.frameCount;
        attackStateObserved = false;
        stateWaitTime = 0f;
    }

    private void BeginAttackMovement(AttackData data)
    {
        ClearAttackMovement();

        if (data == null ||
            attackGravityOff ||
            !IsGrounded())
        {
            return;
        }

        float distance = Mathf.Max(0f, data.attackMoveRange);

        if (distance <= 0f)
        {
            return;
        }

        float duration = Mathf.Max(
            0.01f,
            data.attackMoveDuration
        );

        attackMoveTimeRemaining = duration;
        attackMoveSpeed = distance / duration;
        attackMoveDirection = facingDirection < 0f ? -1f : 1f;
    }

    private float TakeAttackMoveSpeed(float deltaTime)
    {
        if (!IsAttacking || !IsGrounded())
        {
            ClearAttackMovement();
            return 0f;
        }

        if (deltaTime <= 0f ||
            attackMoveTimeRemaining <= 0f)
        {
            return 0f;
        }

        // 最後のステップは残り時間分だけ移動させる。
        float stepTime = Mathf.Min(
            deltaTime,
            attackMoveTimeRemaining
        );

        float speed =
            attackMoveDirection *
            attackMoveSpeed *
            stepTime /
            deltaTime;

        attackMoveTimeRemaining = Mathf.Max(
            0f,
            attackMoveTimeRemaining - stepTime
        );

        return speed;
    }

    private void ClearAttackMovement()
    {
        attackMoveTimeRemaining = 0f;
        attackMoveSpeed = 0f;
        attackMoveDirection = 0f;
    }

    void FinishSlash()
    {
        playerAnimator.EndSlash(true);
        ClearSlashState();
    }

    public void CancelAttack()
    {
        if (playerAnimator != null)
        {
            playerAnimator.EndSlash(false);
        }

        ClearSlashState();
    }

    void ClearSlashState()
    {
        ClearAttackMovement();

        IsAttacking = false;
        nextSlashReserved = false;
        slashStep = 0;
        CurrentAttackData = null;
        attackGravityOff = false;
    }

    private void CheckDiggingImpactSettings()
    {
        if (impactSettingsChecked || !enableDiggingImpact)
        {
            return;
        }

        impactSettingsChecked = true;

        ResolveImpactCameraShake();

        if (ResolvedImpactTerrainLayers == 0)
        {
            Debug.LogWarning(
                "食い込み硬直：DetectorのTerrain Layers、または" +
                "Impact Terrain Layersに地形Layerを設定してください。",
                this
            );
        }

        if (impactCameraShake == null ||
            !impactCameraShake.isActiveAndEnabled)
        {
            Debug.LogWarning(
                "食い込み硬直：有効なCameraShake_Systemを" +
                "Impact Camera Shakeに設定してください。" +
                "硬直だけは実行できます。",
                this
            );
        }
    }

    private void ResolveImpactCameraShake()
    {
        if (impactCameraShake != null)
        {
            return;
        }

        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return;
        }

        impactCameraShake =
            mainCamera.GetComponentInParent<CameraShake_System>();

        if (impactCameraShake == null)
        {
            impactCameraShake =
                mainCamera.GetComponentInChildren<CameraShake_System>();
        }
    }

    private void UpdateFloorTravelImpact(Vector3 velocity)
    {
        ChainsawSurface surface = chainsawDigging.Surface;

        if (surface == ChainsawSurface.Floor)
        {
            // 床の食い込みで移動していた方向を記録する。
            floorTravelImpactArmed =
                Mathf.Abs(velocity.x) > 0.01f;

            floorTravelImpactDirection =
                floorTravelImpactArmed
                    ? Mathf.Sign(velocity.x)
                    : 0f;

            return;
        }

        // 床を離れた後も、元の方向へ移動していれば維持する。
        // 落下に移っても解除しない。
        //
        // 別の面への食い込み・停止・反転で解除する。
        if (surface != ChainsawSurface.None ||
            velocity.x * floorTravelImpactDirection <= 0.01f)
        {
            ResetFloorTravelImpact();
        }
    }

    private void ResetFloorTravelImpact()
    {
        floorTravelImpactArmed = false;
        floorTravelImpactDirection = 0f;
    }
    // 設定アセットの法線閾値と衝突速度を使い、上センサーから硬直を判定する。
    private bool TryBeginUpperSensorImpact()
    {
        if (Time.time < ceilingCornerGraceUntil)
            return false;

        if (!CanUseDiggingImpact ||
            IsImpactStunned ||
            !wallAscentImpactArmed ||
            lastDiggingPhysicsVelocity.y <= 0f)
        {
            return false;
        }

        if (!chainsawDigging.TryGetCeilingContact(
            out SensorChainsawContact ceiling))
        {
            return false;
        }

        int targetLayer = 1 << ceiling.Collider.gameObject.layer;

        if ((ResolvedImpactTerrainLayers & targetLayer) == 0)
        {
            return false;
        }

        Vector3 normal = ceiling.Normal;

        if (Mathf.Abs(normal.z) > 0.5f)
        {
            return false;
        }

        normal = new Vector3(
            normal.x,
            normal.y,
            0f
        ).normalized;

        if (normal.y > -playerParameter.impactSurfaceNormalThreshold)
        {
            return false;
        }

        // 天井の面へ向かう速度で判定する。
        float approachSpeed =
            -Vector3.Dot(lastDiggingPhysicsVelocity, normal);

        if (approachSpeed <
            Mathf.Max(0.01f, playerParameter.surfaceStickCollisionSpeed))
        {
            return false;
        }

        BeginDiggingImpactStun(0f);
        return true;
    }
    // 設定アセットの閾値を使い、壁・天井への物理衝突で硬直を判定する。
    private void TryBeginDiggingImpact(Collision collision)
    {
        if (!isActiveAndEnabled ||
            !CanUseDiggingImpact ||
            IsImpactStunned ||
            playerRigidbody == null ||
            playerRigidbody.isKinematic ||
            chainsawDigging == null ||
            !chainsawDigging.isActiveAndEnabled)
        {
            return;
        }

        bool wasClimbing =
            wallAscentImpactArmed ||
            lastDiggingPhysicsSurface == ChainsawSurface.Wall;

        bool wasSliding =
            floorTravelImpactArmed ||
            lastDiggingPhysicsSurface == ChainsawSurface.Floor;

        if (!wasClimbing && !wasSliding)
        {
            return;
        }

        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            Collider target = contact.otherCollider;

            if (target == null || target.isTrigger)
            {
                continue;
            }

            int targetLayer = 1 << target.gameObject.layer;

            if ((ResolvedImpactTerrainLayers & targetLayer) == 0)
            {
                continue;
            }

            if (target.transform == transform ||
                target.transform.IsChildOf(transform))
            {
                continue;
            }

            Vector3 normal = contact.normal;

            if (Mathf.Abs(normal.z) > 0.5f)
            {
                continue;
            }

            normal = new Vector3(
                normal.x,
                normal.y,
                0f
            ).normalized;

            bool hitsCeiling =
                wasClimbing &&
                normal.y <= -playerParameter.impactSurfaceNormalThreshold &&
                lastDiggingPhysicsVelocity.y > 0f;

            bool hitsWall =
                wasSliding &&
                Mathf.Abs(normal.y) < playerParameter.impactSurfaceNormalThreshold;

            // 頭部の角を通過している間は、天井硬直だけ抑制する。
            if (hitsCeiling && Time.time < ceilingCornerGraceUntil)
                continue;

            if (!hitsCeiling && !hitsWall)
            {
                continue;
            }

            float approachSpeed =
                -Vector3.Dot(lastDiggingPhysicsVelocity, normal);

            if (approachSpeed <
                Mathf.Max(0.01f, playerParameter.surfaceStickCollisionSpeed))
            {
                continue;
            }

            BeginDiggingImpactStun(
                hitsWall ? Mathf.Sign(normal.x) : 0f
            );

            return;
        }
    }
    // 設定アセットの時間と揺れの値を使い、硬直とカメラシェイクを開始する。
    private void BeginDiggingImpactStun(float bounceDirection)
    {
        // 硬直終了後に衝突前のジャンプ猶予を持ち越さない。
        lastGroundedTime = float.NegativeInfinity;
        ResetFloorTravelImpact();
        wallAscentImpactArmed = false;

        if (logDiggingImpact)
        {
            Debug.Log(
                "食い込み強衝突：硬直開始（" +
                (bounceDirection == 0f ? "天井" : "壁") +
                "）",
                this
            );
        }

        impactStunRemaining =
            Mathf.Max(0.01f, playerParameter.diggingImpactStunDuration);

        // 床→壁の場合は、硬直終了後に既存ののけぞりを行う。
        impactBounceDirection = bounceDirection;

        chainsawDigging.Cancel(true);
        chainsawDigging.TryTakeWallBounce(out _);

        CancelAttack();

        jumpPending = false;
        pendingJumpPower = 0f;
        pendingJumpCount = 0;

        knockbackVelocity = 0f;
        knockbackHopPending = false;

        currentSpeed = 0f;
        wallTravelSpeed = 0f;
        ascentStartSpeed = 0f;
        fallElapsedTime = 0f;

        lastDiggingPhysicsVelocity = Vector3.zero;
        lastDiggingPhysicsSurface = ChainsawSurface.None;

        input.ResetInput();

        // 硬直前の制約を保存してから固定する。
        constraintsBeforeImpact = playerRigidbody.constraints;
        impactConstraintsHeld = true;

        playerRigidbody.linearVelocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;
        playerRigidbody.constraints = RigidbodyConstraints.FreezeAll;

        playerAnimator.SetSpeed(0f);

        debugCurrentSpeed = 0f;
        debugSpeedBonus = 0f;
        debugTargetSpeed = 0f;

        ResolveImpactCameraShake();

        if (impactCameraShake != null &&
            impactCameraShake.isActiveAndEnabled)
        {
            impactCameraShake.Shake(
                playerParameter.impactShakeDuration,
                playerParameter.impactShakeMagnitude
            );
        }
    }

    // 初段ジャンプ用の最終接地時刻を記録し、使用済みの猶予を解除する。
    private void UpdateCoyoteTime()
    {
        ChainsawSurface surface = chainsawDigging.Surface;

        if (jumpPending ||
            jumpsUsed > 0 ||
            IsImpactStunned ||
            knockbackHopPending ||
            knockbackVelocity != 0f ||
            surface == ChainsawSurface.Wall ||
            surface == ChainsawSurface.Ceiling ||
            surface == ChainsawSurface.Enemy)
        {
            lastGroundedTime = float.NegativeInfinity;
            return;
        }

        // 空中では時刻を更新しない。
        // IsGrounded自体の判定内容は変更しない。
        if (IsGrounded())
        {
            lastGroundedTime = Time.time;
        }
    }
    // 実行中の硬直残り時間を減らし、終了時に設定アセットの速度で押し戻す。
    private bool TickDiggingImpactStun(float deltaTime)
    {
        if (!IsImpactStunned)
        {
            return false;
        }

        playerRigidbody.linearVelocity = Vector3.zero;
        playerRigidbody.angularVelocity = Vector3.zero;

        lastDiggingPhysicsVelocity = Vector3.zero;
        lastDiggingPhysicsSurface = ChainsawSurface.None;

        debugRotationSpeed = chainsawAccelerator.CurrentSpeed;

        input.ResetInput();

        impactStunRemaining = Mathf.Max(
            0f,
            impactStunRemaining - deltaTime
        );

        if (!IsImpactStunned)
        {
            RestoreImpactConstraints();

            if (impactBounceDirection != 0f)
            {
                knockbackVelocity =
                    impactBounceDirection * playerParameter.wallKnockbackSpeed;

                knockbackHopPending = true;
            }

            impactBounceDirection = 0f;
        }

        return true;
    }

    private void RestoreImpactConstraints()
    {
        if (!impactConstraintsHeld)
        {
            return;
        }

        if (playerRigidbody != null)
        {
            playerRigidbody.constraints = constraintsBeforeImpact;
        }

        impactConstraintsHeld = false;
    }

    void OnDisable()
    {
        // 再有効化時に以前の接地時刻を持ち越さない。
        lastGroundedTime = float.NegativeInfinity;
        isGroundedByRay = false;
        // 再有効化時に、古い壁の接触情報を持ち越さない。
        if (playerMidCollider != null)
        {
            playerMidCollider.ClearContacts();
        }
        ResetFloorTravelImpact();
        wallAscentImpactArmed = false;
        pendingCornerCorrection = Vector3.zero;
        ceilingCornerJumpPending = false;
        ceilingCornerGraceUntil = 0f;
        impactSettingsChecked = false;
        RestoreImpactConstraints();

        impactStunRemaining = 0f;
        impactBounceDirection = 0f;
        wallJumpStunRemaining = 0f;
        wallJumpDirectionHoldRemaining = 0f;
        wallJumpLaunchPending = false;
        pendingWallJumpVelocity = Vector3.zero;
        wallJumpHorizontalVelocity = 0f;
        lastDiggingPhysicsVelocity = Vector3.zero;
        lastDiggingPhysicsSurface = ChainsawSurface.None;
        jumpsUsed = 0;
        pendingJumpCount = 0;
        jumpPending = false;
        currentSpeed = 0f;

        if (chainsawDigging != null)
        {
            chainsawDigging.Cancel(true);
        }

        if (playerRigidbody != null)
        {
            playerRigidbody.useGravity = originalUseGravity;
        }

        if (playerAnimator != null)
        {
            playerAnimator.EndSlash(true);
        }

        ClearSlashState();

        if (input != null)
        {
            input.ResetInput();
        }
    }
}
