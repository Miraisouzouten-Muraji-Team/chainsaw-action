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
    [Min(0f)]
    [SerializeField] private float gravityScale = 2f;

    [Header("食い込み動作確認（実行中に自動更新）")]
    [SerializeField] private float debugRotationSpeed;
    [SerializeField] private float debugSpeedBonus;
    [SerializeField] private float debugTargetSpeed;
    [SerializeField] private float debugCurrentSpeed;

    private void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;
        if (playerRigidbody == null || playerRigidbody.isKinematic) return;
        if (chainsawAccelerator.isActiveAndEnabled)
            chainsawAccelerator.Tick(input.AccelerateHeld, deltaTime);
        chainsawDigging.Tick(deltaTime);
        Move();
        Vector3 velocity = playerRigidbody.linearVelocity;
        velocity.x = currentSpeed;
        if (chainsawDigging.TryTakeDash(out float power))
        {
            // 「パワー」は横速度への加算として実装。次フレーム以降は通常速度へ補間。
            currentSpeed += facingDirection * power;
            velocity.x = currentSpeed;
        }
        if (chainsawDigging.SuppressGravity)
        {
            velocity.y = 0f;
            if (chainsawDigging.Surface == ChainsawSurface.Wall ||
                chainsawDigging.Surface == ChainsawSurface.Enemy)
            {
                currentSpeed = 0f;
                velocity.x = 0f;
            }
            if (chainsawDigging.Surface == ChainsawSurface.Ceiling ||
                chainsawDigging.Surface == ChainsawSurface.Wall)
                velocity -= chainsawDigging.SurfaceNormal * surfaceStickSpeed;
        }
        if (jumpPending)
        {
            jumpPending = false;
            velocity.y = pendingJumpPower;
            groundColliders.Clear();
            ignoreGroundUntil = Time.time + 0.1f;
        }
        velocity.z = 0f;
        playerRigidbody.linearVelocity = velocity;
        debugCurrentSpeed = velocity.x;        // useGravityは無効にし、重力の適用箇所を1か所にする。
        if (!chainsawDigging.SuppressGravity)
            playerRigidbody.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
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
        if (input.MoveInput != 0f) facingDirection = Mathf.Sign(input.MoveInput);
        chainsawDigging.HandleInput(input.WedgieInput, input.WedgieHeld, IsAttacking);
        // 同時押しの優先順位：弱攻撃 > ジャンプ > 食い込み。
        if (input.SlashInput) Slash();
        else if (!IsAttacking && input.JumpInput) Jump();
        UpdateSlash();
        input.ResetInput();
    }

    void Move()
    {
        float moveInput = Mathf.Clamp(input.MoveInput, -1f, 1f);

        // 床への食い込み中は「回転速度 ÷ 5」が加算される。
        // 食い込んでいないときは0。
        float speedBonus = chainsawDigging.MoveSpeedBonus;

        // 左右入力に応じて移動方向と目標速度を決定する。
        float targetSpeed = moveInput * (moveSpeed + speedBonus);

        float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(currentSpeed)
            ? acceleration
            : deceleration;

        // 目標速度へ滑らかに近づける。
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            rate * Time.fixedDeltaTime
        );

        // Inspectorで確認するための値。
        debugRotationSpeed = chainsawAccelerator.CurrentSpeed;
        debugSpeedBonus = speedBonus;
        debugTargetSpeed = targetSpeed;
    }

    void Jump()
    {
        if (jumpPending) return;
        bool wallJump = chainsawDigging.TryGetWallJump(out float wallPower);
        // 接地していなくても、ジャンプ入力で食い込みは解除する。
        chainsawDigging.Cancel(true);
        groundColliders.RemoveWhere(collider => collider == null ||
            !collider.enabled || !collider.gameObject.activeInHierarchy);
        if (!wallJump && (groundColliders.Count == 0 || Time.time < ignoreGroundUntil)) return;
        pendingJumpPower = wallJump ? wallPower : jumpForce;
        jumpPending = true;
        groundColliders.Clear();
        playerAnimator.PlayJump();
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
                break;
            }
        }
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
