using UnityEngine;

/* Playerの移動や行動を管理するクラス */

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

    float verticalSpeed = 0.0f;
    bool isGrounded = true;
    private int slashStep = 0;



    void Awake()
    {
        input = GetComponent<PlayerInputHandler>();
        playerAnimator = GetComponent<PlayerAnimator>();
    }



    void Update()
    {
        // 移動処理
        Move();


        // ジャンプ
        if (input.JumpInput)
        {
            Jump();
        }


        //// 攻撃
        //if (input.AttackInput)
        //{
        //    playerAnimator.PlayAttack();
        //}


        // スラッシュ
        if (input.SlashInput)
        {
            Slash();
        }


        // 食い込み
        if (input.WedgieInput)
        {
            playerAnimator.PlayWedgie();
        }


        // 移動速度をAnimatorへ渡す
        playerAnimator.SetSpeed(currentSpeed);


        // 一回入力をリセット
        input.ResetInput();
    }



    /*
     * 移動処理
     */
    void Move()
    {
        float moveInput = input.MoveInput;


        // 加速
        if (moveInput != 0)
        {
            currentSpeed +=
                moveInput *
                acceleration *
                Time.deltaTime;


            currentSpeed = Mathf.Clamp(
                currentSpeed,
                -moveSpeed,
                moveSpeed
            );
        }
        else
        {
            // 減速
            currentSpeed = Mathf.MoveTowards(
                currentSpeed,
                0,
                deceleration *
                Time.deltaTime
            );
        }



        transform.position +=
            Vector3.right *
            currentSpeed *
            Time.deltaTime;
    }

    /* ジャンプ処理 */
    void Jump()
    {
        if (isGrounded)
        {
            verticalSpeed = jumpForce;

            isGrounded = false;

            playerAnimator.PlayJump();
        }
    }

    /* 多段攻撃処理 */
    void Slash()
    {
        // カウントリセット
        if(slashStep >= 3)
        {
            slashStep = 0;
        }

        if (slashStep == 0)
        {
            slashStep = 1;
            playerAnimator.PlaySlash();
        }
        else if (slashStep == 1)
        {
            slashStep = 2;
            playerAnimator.PlaySlash2();
        }
        else if (slashStep == 2)
        {
            slashStep = 3;
            playerAnimator.PlaySlash3();
        }
    }
}