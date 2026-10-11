using UnityEngine;

public class PlayerInputHandler : MonoBehaviour
{
    private PlayerInputSystem inputSystem;

    public float MoveInput { get; private set; }
    public bool JumpInput { get; private set; }
    public bool AttackInput { get; private set; }
    public bool SlashInput { get; private set; }
    public bool WedgieInput { get; private set; }

    public bool WedgieHeld =>
        inputSystem != null && inputSystem.Player.Wedgie.IsPressed();

    // Input Actionsで設定したアクセルの長押し状態。
    public bool AccelerateHeld =>
        inputSystem != null && inputSystem.Player.Accelerate.IsPressed();

    private void Awake()
    {
        inputSystem = new PlayerInputSystem();

        inputSystem.Player.Move.performed += ctx =>
            MoveInput = ctx.ReadValue<float>();

        inputSystem.Player.Move.canceled += ctx =>
            MoveInput = 0f;

        inputSystem.Player.Jump.performed += ctx =>
            JumpInput = true;

        inputSystem.Player.Attack.performed += ctx =>
            AttackInput = true;

        inputSystem.Player.Slash.performed += ctx =>
            SlashInput = true;

        inputSystem.Player.Wedgie.performed += ctx =>
            WedgieInput = true;
    }

    private void OnEnable()
    {
        inputSystem.Enable();
    }

    private void OnDisable()
    {
        inputSystem.Disable();

        MoveInput = 0f;
        ResetInput();
    }

    private void OnDestroy()
    {
        inputSystem?.Dispose();
    }

    public void ResetInput()
    {
        JumpInput = false;
        AttackInput = false;
        SlashInput = false;
        WedgieInput = false;

        // 長押し状態はIsPressedで取得するので、
        // ここではリセットしない。
    }
}
