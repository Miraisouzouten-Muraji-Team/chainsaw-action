using UnityEngine;
using UnityEngine.InputSystem;

public class ChainsawAccelerator : MonoBehaviour
{
    private const float TRIGGER_THRESHOLD = 0.1f;
    private const float MIN_TRANSITION_DURATION = 0.01f;

    [Header("回転速度（回転 / 秒）")]
    [SerializeField] private float minSpeed = 5f;
    [SerializeField] private float maxSpeed = 50f;

    [Header("変化にかかる時間（秒）")]
    [SerializeField, Min(MIN_TRANSITION_DURATION)]
    private float accelerationTime = 2f;

    [SerializeField, Min(MIN_TRANSITION_DURATION)]
    private float decelerationTime = 2.5f;

    [Header("加速カーブ：左下(0,0) → 右上(1,1)")]
    [SerializeField]
    private AnimationCurve accelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.5f, 0.5f, 3f, 3f),
        new Keyframe(1f, 1f, 0f, 0f)
    );

    [Header("減速の進行カーブ：左下(0,0) → 右上(1,1)")]
    [SerializeField]
    private AnimationCurve decelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.7f, 0.3f, 1f, 1f),
        new Keyframe(1f, 1f, 5f, 5f)
    );

    [Header("食い込みのテスト設定")]
    [SerializeField] private bool isBiting;

    // 最高速度に掛ける倍率。
    [SerializeField, Range(0f, 1f)]
    private float resistance = 0.7f;

    [Header("SEピッチの計算用")]
    [SerializeField] private float minPitch = 0.8f;
    [SerializeField] private float maxPitch = 1.8f;

    [Header("Consoleへの表示間隔(秒)")]
    [SerializeField, Min(0.05f)]
    private float logInterval = 0.2f;

    public float CurrentSpeed { get; private set; }
    public float CurrentPitch { get; private set; }

    private bool previousAccelerating;
    private bool previousBiting;
    private float previousResistance;

    private float startSpeed;
    private float targetSpeed;
    private float elapsedTime;
    private float transitionDuration;
    private bool increasing;
    private float logTimer;

    private void Start()
    {
        CurrentSpeed = minSpeed;
        CurrentPitch = minPitch;

        previousBiting = isBiting;
        previousResistance = resistance;

        BeginTransition(false);
    }

    private void Update()
    {
        Gamepad gamepad = Gamepad.current;
        Keyboard keyboard = Keyboard.current;

        // RT、またはRキー長押しで加速。
        bool accelerating =
            (gamepad != null &&
             gamepad.rightTrigger.ReadValue() > TRIGGER_THRESHOLD) ||
            (keyboard != null && keyboard.rKey.isPressed);

        // Bボタン、またはEキーで食い込み状態を切り替える。
        if ((gamepad != null &&
             gamepad.buttonEast.wasPressedThisFrame) ||
            (keyboard != null && keyboard.eKey.wasPressedThisFrame))
        {
            isBiting = !isBiting;
        }

        // 入力・食い込み状態・抵抗力が変わったときに開始する。
        if (accelerating != previousAccelerating ||
            isBiting != previousBiting ||
            !Mathf.Approximately(resistance, previousResistance))
        {
            BeginTransition(accelerating);

            previousAccelerating = accelerating;
            previousBiting = isBiting;
            previousResistance = resistance;
        }

        elapsedTime += Time.deltaTime;

        float progress = Mathf.Clamp01(
            elapsedTime / transitionDuration
        );

        AnimationCurve curve = increasing
            ? accelerationCurve
            : decelerationCurve;

        float curveValue = Mathf.Clamp01(
            curve.Evaluate(progress)
        );

        CurrentSpeed = Mathf.Lerp(
            startSpeed,
            targetSpeed,
            curveValue
        );

        // 終了時は確実に目標速度へ合わせる。
        if (progress >= 1f)
        {
            CurrentSpeed = targetSpeed;
        }

        // 回転速度からSE用のピッチ値を計算する。
        float speedRatio = Mathf.InverseLerp(
            minSpeed,
            maxSpeed,
            CurrentSpeed
        );

        CurrentPitch = Mathf.Lerp(
            minPitch,
            maxPitch,
            speedRatio
        );

        logTimer += Time.unscaledDeltaTime;

        if (logTimer >= logInterval)
        {
            logTimer = 0f;

            //Debug.Log(
            //    $"[チェーンソー] " +
            //    $"RT:{(accelerating ? "ON" : "OFF")} | " +
            //    $"食い込み:{(isBiting ? "ON" : "OFF")} | " +
            //    $"抵抗力:{(isBiting ? resistance : 1f):F2} | " +
            //    $"回転速度:{CurrentSpeed:F2} 回転/秒 | " +
            //    $"目標:{targetSpeed:F2} | " +
            //    $"SEピッチ:{CurrentPitch:F2}",
            //    this
            //);
        }
    }

    private void BeginTransition(bool accelerating)
    {
        float multiplier = isBiting ? resistance : 1f;

        // 食い込み中でも最低速度は下回らない。
        float speedLimit = Mathf.Max(
            minSpeed,
            maxSpeed * multiplier
        );

        // テスト版では食い込んだ瞬間に速度上限を適用する。
        CurrentSpeed = Mathf.Clamp(
            CurrentSpeed,
            minSpeed,
            speedLimit
        );

        startSpeed = CurrentSpeed;
        targetSpeed = accelerating ? speedLimit : minSpeed;
        increasing = targetSpeed > startSpeed;

        // 食い込み中の減速時間は、通常の減速時間 × 抵抗力。
        transitionDuration = accelerating
            ? accelerationTime
            : decelerationTime * multiplier;

        transitionDuration = Mathf.Max(
            MIN_TRANSITION_DURATION,
            transitionDuration
        );

        elapsedTime = 0f;
    }
}