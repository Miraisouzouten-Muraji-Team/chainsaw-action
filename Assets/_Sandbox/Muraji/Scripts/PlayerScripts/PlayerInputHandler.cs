using UnityEngine;

/* Playerの入力を処理するクラス */

public class PlayerInputHandler : MonoBehaviour
{
    PlayerInputSystem inputSystem; // インスタンス保持する変数
    public float MoveInput { get; private set; }
    public bool JumpInput { get; private set; }
    public bool AttackInput { get; private set; }
    public bool SlashInput { get; private set; }
    public bool WedgieInput { get; private set; }

    /* InputSystemの初期化 */
    void Awake()
    {
        inputSystem = new PlayerInputSystem();
        inputSystem.Player.Move.performed += ctx => MoveInput = ctx.ReadValue<float>();
        inputSystem.Player.Move.canceled += ctx => MoveInput = 0.0f;
        inputSystem.Player.Jump.performed += ctx => JumpInput = true;
        inputSystem.Player.Attack.performed += ctx => AttackInput = true;
        inputSystem.Player.Slash.performed += ctx => SlashInput = true;
        inputSystem.Player.Wedgie.performed += ctx => WedgieInput = true;
    }

    /* InputSystemの有効化 */
    void OnEnable()
    {
        inputSystem.Enable();
    }

    /* InputSystemの無効化 */
    void OnDisable()
    {
        inputSystem.Disable();
    }

    /* 入力のリセット */
    public void ResetInput()
    {
        JumpInput = false;
        AttackInput = false;
        SlashInput = false;
        WedgieInput = false;
    }
}
