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

    [Header("回転速度（回転 / 秒）")]
    [Tooltip("アクセルを離したときの目標値")]
    [SerializeField, Min(0f)]
    private float minSpeed = 5f;

    [Tooltip("食い込んでいないときの上限")]
    [SerializeField, Min(0.01f)]
    private float maxSpeed = 50f;

    [Tooltip("食い込み中の目標上限")]
    [SerializeField, Min(0.01f)]
    private float diggingMaxSpeed = 35f;

    [Header("加速／減速にかかる時間")]
    [Tooltip("加速曲線の再生時間（秒）")]
    [SerializeField, Min(MIN_TRANSITION_DURATION)]
    private float accelerationTime = 2f;

    [Tooltip("減速曲線の再生時間（秒）。50→35にも使用")]
    [SerializeField, Min(MIN_TRANSITION_DURATION)]
    private float decelerationTime = 2.5f;

    [Tooltip("横軸：時間、縦軸：加速の進み具合（0→1）")]
    [SerializeField]
    private AnimationCurve accelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 0.5f, 3f, 3f),
        new Keyframe(1f, 1f)
    );

    [Tooltip("横軸：時間、縦軸：減速の進み具合（0→1）")]
    [SerializeField]
    private AnimationCurve decelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.7f, 0.3f, 1f, 1f),
        new Keyframe(1f, 1f)
    );

    [Header("SEピッチ")]
    [SerializeField] private float minPitch = 0.8f;
    [SerializeField] private float maxPitch = 1.8f;

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

    private void Awake()
    {
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

    private void OnValidate()
    {
        minSpeed = Mathf.Max(0f, minSpeed);
        maxSpeed = Mathf.Max(minSpeed + 0.01f, maxSpeed);

        diggingMaxSpeed = Mathf.Clamp(
            diggingMaxSpeed,
            minSpeed,
            maxSpeed
        );
    }
}
