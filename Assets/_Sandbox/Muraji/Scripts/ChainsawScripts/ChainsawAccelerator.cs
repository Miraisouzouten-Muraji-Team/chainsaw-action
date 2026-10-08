using UnityEngine;

// TickはPlayerController.FixedUpdateから1回だけ呼ぶ。
public class ChainsawAccelerator : MonoBehaviour
{
    private const float MIN_TRANSITION_DURATION = 0.01f;

    private enum TransitionMode
    {
        Accelerating,
        Decelerating,
        SettlingToDiggingLimit
    }

    [Header("回転速度・加減速・SE設定")]
    [Tooltip("回転速度、食い込み時の上限、加減速曲線、SEピッチをまとめた設定データ")]
    [SerializeField] private AccelerationParameter accelerationParameter;

    private float minSpeed
    {
        get => accelerationParameter.minSpeed;
        set => accelerationParameter.minSpeed = value;
    }

    private float maxSpeed
    {
        get => accelerationParameter.maxSpeed;
        set => accelerationParameter.maxSpeed = value;
    }

    private float diggingMaxSpeed
    {
        get => accelerationParameter.diggingMaxSpeed;
        set => accelerationParameter.diggingMaxSpeed = value;
    }

    private float accelerationTime
    {
        get => accelerationParameter.accelerationTime;
        set => accelerationParameter.accelerationTime = value;
    }

    private float decelerationTime
    {
        get => accelerationParameter.decelerationTime;
        set => accelerationParameter.decelerationTime = value;
    }

    private AnimationCurve accelerationCurve
    {
        get => accelerationParameter.accelerationCurve;
        set => accelerationParameter.accelerationCurve = value;
    }

    private AnimationCurve decelerationCurve
    {
        get => accelerationParameter.decelerationCurve;
        set => accelerationParameter.decelerationCurve = value;
    }

    private float minPitch
    {
        get => accelerationParameter.minPitch;
        set => accelerationParameter.minPitch = value;
    }

    private float maxPitch
    {
        get => accelerationParameter.maxPitch;
        set => accelerationParameter.maxPitch = value;
    }

    public float CurrentSpeed { get; private set; }

    public float MaxSpeed => Mathf.Max(0.01f, maxSpeed);

    // ボーナス・威力・SEの基準は通常上限の50を維持する。
    public float SpeedRatio =>
        Mathf.Clamp01(CurrentSpeed / MaxSpeed);

    public float PowerRatio =>
        Mathf.InverseLerp(minSpeed, MaxSpeed, CurrentSpeed);

    public float CurrentPitch =>
        Mathf.Lerp(minPitch, maxPitch, PowerRatio);

    private float transitionTime;
    private float transitionRange;
    private float previousLimit;

    private bool wasDigging;
    private bool transitionInitialized;

    private TransitionMode transitionMode;

    // SOの割り当てを確認してから、従来通り初期回転数を設定する。
    private void Awake()
    {
        if (accelerationParameter == null)
        {
            Debug.LogError("Acceleration Parameterを設定してください。", this);
            enabled = false;
            return;
        }
        CurrentSpeed = minSpeed;
    }

    public void Tick(
        bool isAccelerating,
        float deltaTime,
        bool isDigging = false)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        float limit = isDigging
            ? Mathf.Clamp(diggingMaxSpeed, minSpeed, MaxSpeed)
            : MaxSpeed;

        bool enteredDigging = isDigging && !wasDigging;
        bool diggingChanged = isDigging != wasDigging;

        TransitionMode nextMode;

        if (!isAccelerating)
        {
            // アクセルを離した場合は最低速度へ減速。
            nextMode = TransitionMode.Decelerating;
        }
        else if (isDigging && CurrentSpeed > limit)
        {
            // アクセルを踏んでいても、上限超過分は徐々に減速。
            nextMode = TransitionMode.SettlingToDiggingLimit;
        }
        else
        {
            nextMode = TransitionMode.Accelerating;
        }

        bool restartTransition =
            !transitionInitialized ||
            diggingChanged ||
            nextMode != transitionMode ||
            !Mathf.Approximately(limit, previousLimit);

        if (restartTransition)
        {
            transitionMode = nextMode;
            transitionTime = 0f;

            // 50→35の場合は、差の15を曲線に沿って減らす。
            transitionRange =
                transitionMode == TransitionMode.SettlingToDiggingLimit
                    ? Mathf.Max(0f, CurrentSpeed - limit)
                    : Mathf.Max(0.01f, limit - minSpeed);

            previousLimit = limit;
            transitionInitialized = true;
        }

        wasDigging = isDigging;

        // 食い込み開始時は現在の値をそのまま引き継ぐ。
        if (enteredDigging)
        {
            return;
        }

        bool increasing =
            transitionMode == TransitionMode.Accelerating;

        float duration = Mathf.Max(
            MIN_TRANSITION_DURATION,
            increasing ? accelerationTime : decelerationTime
        );

        AnimationCurve curve = increasing
            ? accelerationCurve
            : decelerationCurve;

        float previous = Mathf.Clamp01(transitionTime / duration);

        transitionTime += deltaTime;

        float next = Mathf.Clamp01(transitionTime / duration);

        float difference = Mathf.Max(
            0f,
            Evaluate(curve, next) - Evaluate(curve, previous)
        );

        // 曲線終了後も、消費した回転数を回復できるようにする。
        if (previous >= 1f)
        {
            difference = deltaTime / duration;
        }

        float amount = difference * transitionRange;

        float target =
            transitionMode == TransitionMode.Decelerating
                ? minSpeed
                : limit;

        // 差分で更新し、攻撃などで消費した値を上書きしない。
        CurrentSpeed = Mathf.MoveTowards(
            CurrentSpeed,
            target,
            amount
        );

        // 35ではClampしない。移行中は35を超える値を許可する。
        CurrentSpeed = Mathf.Clamp(CurrentSpeed, 0f, MaxSpeed);
    }

    private static float Evaluate(AnimationCurve curve, float time)
    {
        return curve == null || curve.length == 0
            ? time
            : Mathf.Clamp01(curve.Evaluate(time));
    }

    public bool TryConsume(float amount)
    {
        amount = Mathf.Max(0f, amount);

        if (CurrentSpeed + 0.0001f < amount)
        {
            return false;
        }

        CurrentSpeed = Mathf.Max(0f, CurrentSpeed - amount);
        return true;
    }

    // 既存の値域補正を維持する。SO未設定時は補正しない。
    private void OnValidate()
    {
        if (accelerationParameter == null) return;
        minSpeed = Mathf.Max(0f, minSpeed);
        maxSpeed = Mathf.Max(minSpeed + 0.01f, maxSpeed);

        diggingMaxSpeed = Mathf.Clamp(
            diggingMaxSpeed,
            minSpeed,
            maxSpeed
        );
    }
}
