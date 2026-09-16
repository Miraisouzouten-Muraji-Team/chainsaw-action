using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    PlayerInputSystem input;

    Animator playerAnimator;

    float moveInput;

    [SerializeField] float moveSpeed = 5.0f; // 移動速度
    [SerializeField] float acceleration = 20.0f; // 加速度
    [SerializeField] float deceleration = 30.0f; // 減速度
    float currentSpeed = 0.0f; // 現在の速度

    [SerializeField] float jumpForce = 5.0f; // ジャンプ力
    // [SerializeField] float gravity = -20.0f; // 重力
    float verticalSpeed = 0.0f; // 垂直方向の速度
    bool isGrounded = true; // 地面判定


    void Update()
    {
        // 入力がある場合加速
        if (moveInput != 0)
        {
            currentSpeed += moveInput * acceleration * Time.deltaTime;

            // 最大速度制限
            currentSpeed = Mathf.Clamp(
                currentSpeed,
                -moveSpeed,
                moveSpeed
            );
        }
        else
        {
            // 入力がない場合減速
            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                0,
                deceleration * Time.deltaTime
            );
        }


        // 移動
        transform.position +=
            Vector3.right * currentSpeed * Time.deltaTime;


        // Animator
        playerAnimator.SetFloat(
            "Speed",
            Mathf.Abs(currentSpeed)
        );

        // verticalSpeed+= gravity * Time.deltaTime; // 重力
        transform.position += Vector3.up * verticalSpeed * Time.deltaTime; // ジャンプ
    }

    void Awake()
    {
        input = new PlayerInputSystem();
        playerAnimator = GetComponent<Animator>();
        Debug.Log(playerAnimator);

        // 移動の入力
        input.Player.Move.performed += ctx =>
        {
            moveInput = ctx.ReadValue<float>();
        };

        // 移動の入力がキャンセル
        input.Player.Move.canceled += ctx =>
        {
            moveInput = 0;
        };

        // 食い込みの入力
        input.Player.Wedgie.performed += ctx =>
        {
            playerAnimator.SetTrigger("Wedgie");
        };

        input.Player.Wedgie.canceled += ctx =>
        {
            playerAnimator.ResetTrigger("Wedgie");
        };

        // 弱攻撃の入力
        input.Player.Attack.performed += ctx =>
        {
            playerAnimator.SetTrigger("Attack");
        };

        // ジャンプの入力
        input.Player.Jump.performed += ctx =>
        {
            Jump();
        };
    }


    private void OnEnable()
    {
        input.Enable();
    }


    private void OnDisable()
    {
        input.Disable();
    }

    void Jump()
    {
        if(isGrounded)
        {
            verticalSpeed = jumpForce;
            isGrounded = false;
            // playerAnimator.SetTrigger("Jump");
        }
    }
}