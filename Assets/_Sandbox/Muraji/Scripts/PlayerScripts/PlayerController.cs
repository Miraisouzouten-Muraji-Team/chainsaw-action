using UnityEngine;
using System.Collections.Generic;
public class PlayerController : MonoBehaviour
{
    PlayerInputHandler input;
    PlayerAnimator playerAnimator;

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

    private void FixedUpdate()
    {
        // Use Gravityで通常の重力がかかるため、
        // 指定倍率との差分だけ追加する。
        playerRigidbody.AddForce(
            Physics.gravity * (gravityScale - 1f),
            ForceMode.Acceleration
        );
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

        if (input == null ||
            playerAnimator == null ||
            playerRigidbody == null)
        {
            Debug.LogError(
                "PlayerInputHandler・PlayerAnimator・Rigidbodyを" +
                "同じGameObjectに配置してください。",
                this
            );

            enabled = false;
            return;
        }
    }
    void Update()
    {
        Move();

        // 攻撃中の追加入力は、次の1段の予約として扱う。
        if (input.SlashInput)
        {
            Slash();
        }

        // 攻撃中のジャンプ・食い込みによる中断を一旦禁止。
        if (!IsAttacking)
        {
            if (input.JumpInput)
            {
                Jump();
            }
            else if (input.WedgieInput)
            {
                //playerAnimator.PlayWedgie();
                if (!playerAnimator.IsDiggingAnimationActive)
                {
                    // 食い込みアニメーションを開始する。
                    playerAnimator.StartDiggingAnimation();
                }
                else
                {
                    // 停止を解除し、続きから再生する。
                    playerAnimator.ReleaseDiggingAnimation();
                }
            }
        }

        UpdateSlash();

        playerAnimator.SetSpeed(currentSpeed);

        input.ResetInput();
    }

    void Move()
    {
        float moveInput = input.MoveInput;

        if (moveInput != 0)
        {
            currentSpeed +=
                moveInput * acceleration * Time.deltaTime;

            currentSpeed = Mathf.Clamp(
                currentSpeed,
                -moveSpeed,
                moveSpeed
            );
        }
        else
        {
            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                0,
                deceleration * Time.deltaTime
            );
        }

        transform.position +=
            Vector3.right * currentSpeed * Time.deltaTime;
    }

    void Jump()
    {
        // 削除・無効化された床を接地対象から外す。
        groundColliders.RemoveWhere(
            groundCollider =>
                groundCollider == null ||
                !groundCollider.enabled ||
                !groundCollider.gameObject.activeInHierarchy
        );

        if (groundColliders.Count == 0 || Time.timeScale <= 0f)
        {
            return;
        }

        // 食い込みで停止していたアニメーションを解除。
        playerAnimator.CancelDiggingAnimation();

        // Rigidbodyに上向きの速度を設定する。
        Vector3 velocity = playerRigidbody.linearVelocity;
        velocity.y = jumpForce;
        playerRigidbody.linearVelocity = velocity;

        // 着地するまで再ジャンプできないようにする。
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
        if (playerRigidbody.linearVelocity.y > 0.1f)
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