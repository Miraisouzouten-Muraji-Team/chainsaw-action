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

        ChainsawSurface previousSurface = chainsawDigging.Surface;
        float previousMoveSpeed = Mathf.Abs(currentSpeed);

        chainsawDigging.Tick(deltaTime, facingDirection);

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
        playerRigidbody.linearVelocity = velocity;

        debugCurrentSpeed = velocity.x;

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

    void Jump()
    {
        if (jumpPending)
        {
            return;
        }
        
        bool wasDiggingFloor = chainsawDigging.Surface == ChainsawSurface.Floor;
        bool wallJump =
            chainsawDigging.TryGetWallJump(out float wallPower);

        chainsawDigging.Cancel(true);

        groundColliders.RemoveWhere(collider =>
            collider == null ||
            !collider.enabled ||
            !collider.gameObject.activeInHierarchy);

        bool isGrounded = wasDiggingFloor ||
            (groundColliders.Count > 0 && Time.time >= ignoreGroundUntil);

        if (wallJump)
        {
            pendingJumpPower = wallPower;
            pendingJumpCount = 1;
        }
        else if (isGrounded)
        {
            pendingJumpPower = jumpForce;
            pendingJumpCount = 1;
        }
        else
        {
            int effectiveJumpCount = Mathf.Max(jumpsUsed, 1);

            if (effectiveJumpCount >= MAX_JUMP_COUNT)
            {
                return;
            }

            pendingJumpPower = airJumpForce;
            pendingJumpCount = effectiveJumpCount + 1;
        }

        jumpPending = true;
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
        UpdateGroundContact(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        UpdateGroundContact(collision);
    }

    void OnCollisionExit(Collision collision)
    {
        groundColliders.Remove(collision.collider);
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

    void OnDisable()
    {
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
