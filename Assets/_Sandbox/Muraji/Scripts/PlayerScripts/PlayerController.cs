using UnityEngine;
using System.Collections.Generic;
public class PlayerController : MonoBehaviour
{
    PlayerInputHandler input;
    PlayerAnimator playerAnimator;
    [Header("食い込み")]
    [SerializeField] private ChainsawDigging chainsawDigging;
    [SerializeField] private ChainsawAccelerator chainsawAccelerator;
    [SerializeField, Min(0f)] private float surfaceStickSpeed = 1f;
    [Header("壁への食い込み移動")]
    [SerializeField, Min(0f)] private float wallClimbSpeed = 5f;
    public bool CanTakeDamage => chainsawDigging == null || !chainsawDigging.IsEvading;
    private float facingDirection = 1f;
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

    private Rigidbody playerRigidbody;

    // 複数の床にまたがっていても接地を保持する。
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

    private const int MAX_JUMP_COUNT = 2;

    [Header("見た目の向き")]
    [Tooltip("モデルとチェーンソーを含む見た目の親")]
    [SerializeField] private Transform visualRoot;

    private Quaternion rightFacingRotation;

    // 実際に使用したジャンプ回数。
    private int jumpsUsed;

    // 次の物理更新で確定するジャンプ回数。
    private int pendingJumpCount;

    private float ascentStartSpeed;
    private float fallElapsedTime;

    [Header("食い込み動作確認（実行中に自動更新）")]
    [SerializeField] private float debugRotationSpeed;
    [SerializeField] private float debugSpeedBonus;
    [SerializeField] private float debugTargetSpeed;
    [SerializeField] private float debugCurrentSpeed;

    // 壁に入ったときの移動速度を保持する。
    private float wallTravelSpeed;

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

        // 地面移動の速度を、壁への切り替え前に保存する。
        float previousMoveSpeed = Mathf.Abs(currentSpeed);

        chainsawDigging.Tick(deltaTime, facingDirection);

        bool enteredWall =
            previousSurface != ChainsawSurface.Wall &&
            chainsawDigging.Surface == ChainsawSurface.Wall;

        if (enteredWall)
        {
            // 地面からなら横移動の速さを引き継ぐ。
            // 直接壁へ食い込んだ場合は設定した上昇速度を使う。
            wallTravelSpeed = previousSurface == ChainsawSurface.Floor
                ? previousMoveSpeed
                : wallClimbSpeed;
        }
        Move();

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
                        // 横方向の自動移動を止める。
                        currentSpeed = 0f;

                        // 壁との接触を維持するため、壁側へ軽く押す。
                        velocity.x =
                            -chainsawDigging.SurfaceNormal.x * surfaceStickSpeed;

                        // 地面での移動速度を上方向へ向ける。
                        velocity.y = wallTravelSpeed;

                        groundColliders.Clear();
                        break;
                    }
                case ChainsawSurface.Ceiling:
                    {
                        velocity.y = 0f;

                        velocity -=
                            chainsawDigging.SurfaceNormal * surfaceStickSpeed;
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

            // 落下中でも、上向きの速度に置き換えて跳び直す。
            velocity.y = pendingJumpPower;

            // 実際に跳んだタイミングで回数を確定する。
            jumpsUsed = pendingJumpCount;

            // 2回目も、弱い上昇重力から開始する。
            ascentStartSpeed = Mathf.Max(pendingJumpPower, 0f);
            fallElapsedTime = 0f;

            groundColliders.Clear();
            ignoreGroundUntil = Time.time + 0.1f;
        }

        if (!suppressGravity)
        {
            ApplyJumpGravity(ref velocity, deltaTime);
        }

        velocity.z = 0f;

        playerRigidbody.linearVelocity = velocity;

        debugCurrentSpeed = velocity.x;
        playerAnimator.SetSpeed(currentSpeed);
    }
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
        if (chainsawDigging == null) chainsawDigging = GetComponent<ChainsawDigging>();
        if (chainsawAccelerator == null) chainsawAccelerator = GetComponentInChildren<ChainsawAccelerator>();

        if (input == null ||
            playerAnimator == null ||
            playerRigidbody == null || chainsawDigging == null || chainsawAccelerator == null)
        {
            Debug.LogError(
                "PlayerInputHandler・PlayerAnimator・Rigidbodyを" +
                "同じGameObjectに配置し、ChainsawDiggingとChainsawAcceleratorも設定してください。",
                this
            );

            enabled = false;
            return;
        }
        originalUseGravity = playerRigidbody.useGravity;
        playerRigidbody.useGravity = false;
        if (playerRigidbody.isKinematic)
            Debug.LogError("PlayerのRigidbodyのIs KinematicをOFFにしてください。", this);
        if (visualRoot != null)
        {
            // 初期状態を右向きとして保存。
            rightFacingRotation = visualRoot.localRotation;
        }
    }

    void OnEnable()
    {
        if (playerRigidbody != null) playerRigidbody.useGravity = false;
    }

    void Update()
    {
        // ヒットストップ中はコンボ予約だけ受け付け、食い込み／ジャンプは変更しない。
        if (Time.timeScale <= 0f)
        {
            if (IsAttacking && input.SlashInput) Slash();
            input.ResetInput();
            return;
        }
        // 地面食い込み中以外は、入力方向を向く。
        if (chainsawDigging.Surface != ChainsawSurface.Floor &&
            input.MoveInput != 0f)
        {
            facingDirection = Mathf.Sign(input.MoveInput);
            ApplyFacingRotation();
        }

        chainsawDigging.HandleInput(input.WedgieInput, input.WedgieHeld, IsAttacking);
        // 同時押しの優先順位：弱攻撃 > ジャンプ > 食い込み。
        if (input.SlashInput) Slash();
        else if (!IsAttacking && input.JumpInput) Jump();
        UpdateSlash();
        input.ResetInput();
    }

    void Move()
    {
        bool isFloorDigging =
            chainsawDigging.Surface == ChainsawSurface.Floor;

        // 地面食い込み中は向いている方向へ自動移動。
        float moveInput = isFloorDigging
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

        float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed)
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
        if (jumpPending) return;

        bool wallJump = chainsawDigging.TryGetWallJump(out float wallPower);

        // ジャンプ入力で食い込みを解除する。
        chainsawDigging.Cancel(true);

        groundColliders.RemoveWhere(collider =>
            collider == null ||
            !collider.enabled ||
            !collider.gameObject.activeInHierarchy);

        bool isGrounded =
            groundColliders.Count > 0 &&
            Time.time >= ignoreGroundUntil;

        if (wallJump)
        {
            // 壁ジャンプを1回目として扱い、空中ジャンプを回復する。
            pendingJumpPower = wallPower;
            pendingJumpCount = 1;
        }
        else if (isGrounded)
        {
            // 地上からの1回目。
            pendingJumpPower = jumpForce;
            pendingJumpCount = 1;
        }
        else
        {
            // 歩いて落ちた場合も、空中で使えるのは残り1回。
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
    private void ApplyJumpGravity(ref Vector3 velocity, float deltaTime)
    {
        bool isGrounded =
            groundColliders.Count > 0 &&
            Time.time >= ignoreGroundUntil &&
            velocity.y <= 0.1f;

        float gravityMultiplier;

        if (isGrounded)
        {
            // 着地したら次のジャンプに備えてリセット。
            ascentStartSpeed = 0f;
            fallElapsedTime = 0f;

            gravityMultiplier = 1f;
        }
        else if (velocity.y > 0f)
        {
            // 上昇中。
            fallElapsedTime = 0f;

            // ジャンプ以外の力で上昇した場合にも対応する。
            ascentStartSpeed = Mathf.Max(ascentStartSpeed, velocity.y);

            // 飛び出し直後は0、頂点に近づくほど1になる。
            float ascentProgress = 1f - Mathf.Clamp01(
                velocity.y / Mathf.Max(ascentStartSpeed, 0.001f)
            );

            gravityMultiplier = Mathf.SmoothStep(
                jumpStartGravityMultiplier,
                1f,
                ascentProgress
            );
        }
        else
        {
            // 落下中。時間とともに重力を強める。
            ascentStartSpeed = 0f;

            float fallProgress = Mathf.Clamp01(
                fallElapsedTime / Mathf.Max(fallGravityIncreaseTime, 0.01f)
            );

            gravityMultiplier = Mathf.SmoothStep(
                1f,
                maxFallGravityMultiplier,
                fallProgress
            );

            fallElapsedTime += deltaTime;
        }

        // 重力はここだけで適用する。
        velocity += Physics.gravity
            * gravityScale
            * gravityMultiplier
            * deltaTime;

        // 下向きの速度に上限を設ける。
        velocity.y = Mathf.Max(velocity.y, -maxFallSpeed);
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

        // ジャンプ直後の接触を着地と誤認しない。
        if (jumpPending || Time.time < ignoreGroundUntil || playerRigidbody.linearVelocity.y > 0.1f)
        {
            return;
        }

        for (int contactIndex = 0;
             contactIndex < collision.contactCount;
             contactIndex++)
        {
            ContactPoint contact = collision.GetContact(contactIndex);

            // 上向きの面に乗ったときだけ接地扱い。
            // 壁や天井に触れただけでは接地扱いにしない。
            if (contact.normal.y >= MIN_GROUND_NORMAL_Y)
            {
                groundColliders.Add(otherCollider);

                // 地面に着地したらジャンプ回数を回復。
                jumpsUsed = 0;

                break;
            }
        }
    }

    private void ApplyFacingRotation()
    {
        if (visualRoot == null) return;

        float angle = facingDirection < 0f ? 180f : 0f;
        visualRoot.localRotation =
            rightFacingRotation * Quaternion.Euler(0f, angle, 0f);
    }

    // 攻撃ボタンを押したときに呼ぶ。
    void Slash()
    {
        if (!IsAttacking)
        {
            jumpPending = false;
            chainsawDigging.Cancel(true);
            StartSlash(1);
        }
        else if (slashStep < 3)
        {
            // 連打されても、次の1段だけを予約する。
            // ここでは攻撃段数・攻撃データを変更しない。
            nextSlashReserved = true;
        }

        // 3段目中は、新しいコンボを予約しない。
    }

    // アニメーションの進行に合わせて次段・終了を判断。
    void UpdateSlash()
    {
        if (!IsAttacking || Time.frameCount == attackStartFrame)
        {
            return;
        }

        // ヒットストップ中もSlash()で予約は受け付ける。
        // ただし、停止中には次段へ進めない。
        if (Time.timeScale <= 0f)
        {
            return;
        }

        if (!playerAnimator.TryGetAttackProgress(out float progress))
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

    // 実際に攻撃を開始するときだけ、段数とデータを更新。
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

        IsAttacking = true;
        nextSlashReserved = false;

        attackStartFrame = Time.frameCount;
        attackStateObserved = false;
        stateWaitTime = 0f;
    }

    void FinishSlash()
    {
        playerAnimator.EndSlash(true);
        ClearSlashState();
    }

    // ムラジmemo:被ダメージ・死亡を追加するときは、別モーションの再生前にこの関数を呼ぶ。
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
        IsAttacking = false;
        nextSlashReserved = false;
        slashStep = 0;
        CurrentAttackData = null;
    }

    void OnDisable()
    {
        jumpsUsed = 0;
        pendingJumpCount = 0;
        jumpPending = false;
        currentSpeed = 0f;
        if (chainsawDigging != null) chainsawDigging.Cancel(true);
        if (playerRigidbody != null) playerRigidbody.useGravity = originalUseGravity;
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
