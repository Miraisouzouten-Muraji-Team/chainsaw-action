using UnityEngine;
using System.Collections.Generic;

public class SensorPlayerController : MonoBehaviour
{
    PlayerInputHandler input;
    PlayerAnimator playerAnimator;

    [Header("食い込み")]
    [SerializeField] private SensorChainsawDigging chainsawDigging;
    [SerializeField] private ChainsawAccelerator chainsawAccelerator;
    [SerializeField, Min(0f)] private float surfaceStickSpeed = 1f;

    [Header("壁への食い込み移動")]
    [SerializeField, Min(0f)] private float wallClimbSpeed = 5f;

    public bool CanTakeDamage =>
        chainsawDigging == null || !chainsawDigging.IsEvading;

    private float facingDirection = 1f;
    public float FacingDirection => facingDirection;

    private bool jumpPending;
    private float pendingJumpPower;
    private float ignoreGroundUntil;
    private bool originalUseGravity;

    [Header("移動設定")]
    [SerializeField] float moveSpeed = 5.0f;
    [SerializeField] float acceleration = 20.0f;
    [SerializeField] float deceleration = 30.0f;

    float currentSpeed = 0.0f;

    [Header("ジャンプ設定")]
    [SerializeField] float jumpForce = 5.0f;

    [Tooltip("地面を離れてから、初段ジャンプを受け付ける秒数です。0にすると猶予を無効にします。")]
    [SerializeField, Min(0f)]
    private float coyoteTime = 0.15f;

    // 空中から開始した場合に猶予を与えないため、未接地で初期化する。
    private float lastGroundedTime = float.NegativeInfinity;

    private const float MIN_GROUND_NORMAL_Y = 0.7f;
    private const int MAX_JUMP_COUNT = 2;

    private Rigidbody playerRigidbody;

    private readonly HashSet<Collider> groundColliders =
        new HashSet<Collider>();

    [Header("重力設定")]
    [SerializeField, Min(0f)]
    private float gravityScale = 2f;

    [Tooltip("ジャンプ直後の重力倍率。頂点に近づくと1倍に戻ります。")]
    [SerializeField, Range(0.1f, 1f)]
    private float jumpStartGravityMultiplier = 0.7f;

    [Tooltip("落下中に到達する最大の重力倍率。")]
    [SerializeField, Min(1f)]
    private float maxFallGravityMultiplier = 2.5f;

    [Tooltip("落下開始から最大重力になるまでの秒数。")]
    [SerializeField, Min(0.01f)]
    private float fallGravityIncreaseTime = 0.2f;

    [Tooltip("落下速度の上限。")]
    [SerializeField, Min(0.1f)]
    private float maxFallSpeed = 20f;

    [Header("二段ジャンプ設定")]
    [SerializeField, Min(0f)]
    private float airJumpForce = 5f;

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

    [Header("壁に当たったときの押し戻し")]
    [SerializeField, Min(0f)]
    private float knockbackSpeed = 8f;

    [SerializeField, Min(0.01f)]
    private float knockbackDeceleration = 20f;

    private float knockbackVelocity;

    [Tooltip("跳ね返り時に上へ跳ぶ速さ。弧の高さになる")]
    [SerializeField, Min(0f)]
    private float knockbackUpSpeed = 7f;

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

    [Header("コンボ設定")]
    [Tooltip("次段へ移れる再生位置。1なら現在の攻撃を最後まで再生します。")]
    [Range(0.1f, 1f)]
    [SerializeField] float comboAdvanceTime = 1f;


[Header("食い込み中の強衝突：硬直とカメラシェイク")]
[SerializeField]
private bool enableDiggingImpact = true;

[Tooltip("NothingならDetectorのTerrain Layersを使用。指定する場合は敵を含めない")]
[SerializeField]
private LayerMask impactTerrainLayers;

[Tooltip("面へ向かう速度がこの値以上なら硬直。チェーンソーの回転数ではない")]
[SerializeField, Min(0.01f)]
private float minimumDiggingImpactSpeed = 4f;

[SerializeField, Min(0.01f)]
private float diggingImpactStunDuration = 0.2f;

[SerializeField]
private CameraShake_System impactCameraShake;

[SerializeField, Min(0f)]
private float impactShakeDuration = 0.15f;

[SerializeField, Min(0f)]
private float impactShakeMagnitude = 0.1f;

[Tooltip("DetectorのSurface Normal Thresholdと合わせる")]
[SerializeField, Range(0.1f, 0.95f)]
private float impactSurfaceNormalThreshold = 0.7f;

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
    enableDiggingImpact &&
    ResolvedImpactTerrainLayers != 0 &&
    diggingImpactStunDuration > 0f;

private bool wallAscentImpactArmed;
private bool floorTravelImpactArmed;
private float floorTravelImpactDirection;
private bool impactSettingsChecked;

private float impactStunRemaining;
private Vector3 lastDiggingPhysicsVelocity;
private ChainsawSurface lastDiggingPhysicsSurface;
private PlayeMidCollider playerMidCollider;

    private RigidbodyConstraints constraintsBeforeImpact;
private bool impactConstraintsHeld;
private float impactBounceDirection;
public bool IsAttacking { get; private set; }

    int slashStep = 0;
    bool nextSlashReserved;
    int attackStartFrame;
    bool attackStateObserved;
    float stateWaitTime;

    void Awake()
    {
        input = GetComponent<PlayerInputHandler>();
        playerAnimator = GetComponent<PlayerAnimator>();
        playerRigidbody = GetComponent<Rigidbody>();

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
        if (IsImpactStunned)
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

    private void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;

        if (playerRigidbody == null || playerRigidbody.isKinematic)
        {
            ascentStartSpeed = 0f;
            fallElapsedTime = 0f;
            return;
        }

        groundColliders.RemoveWhere(collider =>
            collider == null ||
            !collider.enabled ||
            !collider.gameObject.activeInHierarchy);

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

        CheckDiggingImpactSettings();

        // 無効化・削除された壁の接触記録を更新する。
        playerMidCollider.RefreshContacts(ResolvedImpactTerrainLayers);

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
                ? Mathf.Max(0.01f, minimumDiggingImpactSpeed)
                : float.PositiveInfinity,
            currentSpeed,
            ResolvedImpactTerrainLayers
        );

        if (chainsawDigging.TryTakeWallBounce(out float bounceDirection))
        {
            knockbackVelocity = bounceDirection * knockbackSpeed;
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
                    : wallClimbSpeed;
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
                knockbackDeceleration * deltaTime
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
                        velocity = tangent * wallTravelSpeed - normal * surfaceStickSpeed;
                        groundColliders.Clear();
                        break;
                    }

                case ChainsawSurface.Floor:
                case ChainsawSurface.Ceiling:
                    {
                        Vector3 normal = chainsawDigging.SurfaceNormal;
                        Vector3 tangent = new Vector3(normal.y, -normal.x, 0f).normalized;
                        if (tangent.x < 0f) tangent = -tangent;
                        // currentSpeedは斜面に沿った速度として使う。
                        velocity = tangent * currentSpeed - normal * surfaceStickSpeed;
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

            groundColliders.Clear();
            ignoreGroundUntil = Time.time + 0.1f;
        }

        if (knockbackHopPending)
        {
            knockbackHopPending = false;

            velocity.y = knockbackUpSpeed;
            ascentStartSpeed = knockbackUpSpeed;
            fallElapsedTime = 0f;

            groundColliders.Clear();
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

    void Move()
    {
        bool isAutoMoving =
            chainsawDigging.Surface == ChainsawSurface.Floor ||
            chainsawDigging.Surface == ChainsawSurface.Ceiling;

        float moveInput = isAutoMoving
            ? facingDirection
            : Mathf.Clamp(input.MoveInput, -1f, 1f);

        float speedBonus = chainsawDigging.MoveSpeedBonus;
        float targetSpeed = moveInput * (moveSpeed + speedBonus);

        bool isReversing =
            (moveInput > 0f && currentSpeed < 0f) ||
            (moveInput < 0f && currentSpeed > 0f);

        if (isReversing)
        {
            currentSpeed = 0f;
        }

        float rate =
            Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed)
                ? acceleration
                : deceleration;

        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * Time.fixedDeltaTime
        );

        debugRotationSpeed = chainsawAccelerator.CurrentSpeed;
        debugSpeedBonus = speedBonus;
        debugTargetSpeed = targetSpeed;
    }

    // 壁ジャンプ、接地・猶予中の初段、二段目の順でジャンプを予約する。
    void Jump()
    {
        if (jumpPending)
        {
            return;
        }

        // 食い込み解除前の状態を保存する。
        ChainsawSurface surfaceBeforeJump = chainsawDigging.Surface;

        bool wasDiggingFloor =
            surfaceBeforeJump == ChainsawSurface.Floor;

        bool wallJump =
            chainsawDigging.TryGetWallJump(out float wallPower);

        chainsawDigging.Cancel(true);

        groundColliders.RemoveWhere(collider =>
            collider == null ||
            !collider.enabled ||
            !collider.gameObject.activeInHierarchy);

        bool isGrounded =
            wasDiggingFloor ||
            (
                groundColliders.Count > 0 &&
                Time.time >= ignoreGroundUntil
            );

        // 初段をまだ使っておらず、最後の接地から設定時間以内なら許可する。
        // 壁・天井・敵への食い込み中や押し戻し中には適用しない。
        bool canUseCoyoteTime =
            surfaceBeforeJump == ChainsawSurface.None &&
            coyoteTime > 0f &&
            jumpsUsed == 0 &&
            !IsImpactStunned &&
            !knockbackHopPending &&
            knockbackVelocity == 0f &&
            Time.time - lastGroundedTime <= coyoteTime;

        if (wallJump)
        {
            // 既存の壁ジャンプを優先する。
            pendingJumpPower = wallPower;
            pendingJumpCount = 1;
        }
        else if (isGrounded || canUseCoyoteTime)
        {
            // 猶予中も、通常の初段と同じ強さ・回数でジャンプする。
            pendingJumpPower = jumpForce;
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

            pendingJumpPower = airJumpForce;
            pendingJumpCount = effectiveJumpCount + 1;
        }

        jumpPending = true;

        // 予約時点で猶予を消費し、初段を連続使用できないようにする。
        lastGroundedTime = float.NegativeInfinity;

        groundColliders.Clear();

        playerAnimator.PlayJump();
    }
    private void ApplyJumpGravity(
        ref Vector3 velocity,
        float deltaTime)
    {
        bool isGrounded =
            groundColliders.Count > 0 &&
            Time.time >= ignoreGroundUntil &&
            velocity.y <= 0.1f;

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
                jumpStartGravityMultiplier,
                1f,
                ascentProgress
            );
        }
        else
        {
            ascentStartSpeed = 0f;

            float fallProgress = Mathf.Clamp01(
                fallElapsedTime /
                Mathf.Max(fallGravityIncreaseTime, 0.01f)
            );

            gravityMultiplier = Mathf.SmoothStep(
                1f,
                maxFallGravityMultiplier,
                fallProgress
            );

            fallElapsedTime += deltaTime;
        }

        velocity +=
            Physics.gravity *
            gravityScale *
            gravityMultiplier *
            deltaTime;

        velocity.y = Mathf.Max(velocity.y, -maxFallSpeed);
    }

    private bool IsGrounded()
    {
        if (chainsawDigging != null && chainsawDigging.Surface == ChainsawSurface.Floor)
            return true;

        groundColliders.RemoveWhere(collider =>
            collider == null ||
            !collider.enabled ||
            !collider.gameObject.activeInHierarchy);

        return
            groundColliders.Count > 0 &&
            Time.time >= ignoreGroundUntil;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        // 速度を止める前に、既存の硬直・シェイクを判定する。
        TryBeginDiggingImpact(collision);

        UpdateWallContact(collision);
        UpdateGroundContact(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        // 同じ地形内で接触面が変化した場合も更新する。
        TryBeginDiggingImpact(collision);

        UpdateWallContact(collision);
        UpdateGroundContact(collision);
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

        groundColliders.Remove(collision.collider);
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

    void UpdateGroundContact(Collision collision)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        Collider otherCollider = collision.collider;
        groundColliders.Remove(otherCollider);

        if (jumpPending ||
            Time.time < ignoreGroundUntil ||
            (playerRigidbody.linearVelocity.y > 0.1f &&
             chainsawDigging.Surface != ChainsawSurface.Floor))
        {
            return;
        }

        for (int contactIndex = 0;
             contactIndex < collision.contactCount;
             contactIndex++)
        {
            ContactPoint contact =
                collision.GetContact(contactIndex);

            if (contact.normal.y >= MIN_GROUND_NORMAL_Y)
            {
                groundColliders.Add(otherCollider);
                jumpsUsed = 0;

                // 通常状態で着地したら、空中移動の記録を解除する。
                // 床に食い込んでいる最中は記録を維持する。
                if (chainsawDigging.Surface != ChainsawSurface.Floor)
                {
                    ResetFloorTravelImpact();
                }

                break;
            }
        }
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
            progress >= comboAdvanceTime)
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
    private bool TryBeginUpperSensorImpact()
    {
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

        if (normal.y > -impactSurfaceNormalThreshold)
        {
            return false;
        }

        // 天井の面へ向かう速度で判定する。
        float approachSpeed =
            -Vector3.Dot(lastDiggingPhysicsVelocity, normal);

        if (approachSpeed <
            Mathf.Max(0.01f, minimumDiggingImpactSpeed))
        {
            return false;
        }

        BeginDiggingImpactStun(0f);
        return true;
    }
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
                normal.y <= -impactSurfaceNormalThreshold &&
                lastDiggingPhysicsVelocity.y > 0f;

            bool hitsWall =
                wasSliding &&
                Mathf.Abs(normal.y) < impactSurfaceNormalThreshold;

            if (!hitsCeiling && !hitsWall)
            {
                continue;
            }

            float approachSpeed =
                -Vector3.Dot(lastDiggingPhysicsVelocity, normal);

            if (approachSpeed <
                Mathf.Max(0.01f, minimumDiggingImpactSpeed))
            {
                continue;
            }

            BeginDiggingImpactStun(
                hitsWall ? Mathf.Sign(normal.x) : 0f
            );

            return;
        }
    }
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
            Mathf.Max(0.01f, diggingImpactStunDuration);

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
                impactShakeDuration,
                impactShakeMagnitude
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
                    impactBounceDirection * knockbackSpeed;

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
        // 再有効化時に、古い壁の接触情報を持ち越さない。
        if (playerMidCollider != null)
        {
            playerMidCollider.ClearContacts();
        }
        ResetFloorTravelImpact();
        wallAscentImpactArmed = false;
        impactSettingsChecked = false;
        RestoreImpactConstraints();

        impactStunRemaining = 0f;
        impactBounceDirection = 0f;
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

        groundColliders.Clear();

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
