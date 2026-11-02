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

    [SerializeField, Min(0.01f)]
    [Tooltip("食い込み開始時に上限を超えているアクセル値を、食い込み用の上限まで下げる秒数。途中で食い込みを解除しても減速を続けます。")]
    private float diggingDecelerationTime = 0.3f;

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

    // 食い込みを離しても、開始時に確定した減速目標を保持する。
    private bool isDiggingLimitPending;
    private float pendingDiggingLimit;

    private TransitionMode transitionMode;

    private bool isBonusDecelerating;
    private float bonusDecelerationElapsed;
    private float bonusDecelerationDuration;
    private float bonusDecelerationStartSpeed;
    private float bonusDecelerationTargetSpeed;
    private float lastBonusDecelerationFixedTime = float.NegativeInfinity;

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

    // 通常の加減速とボーナス減速を更新し、確定した減速目標への到達をアクセル入力より優先する。
    public void Tick(
        bool isAccelerating,
        float deltaTime,
        bool isDigging = false)
    {
        if (deltaTime <= 0f)
        {
            return;
        }

        // 食い込み側の更新が止まっても、Controllerの物理更新から専用減速を進める。
        if (isBonusDecelerating)
        {
            TickBonusDeceleration(deltaTime);
            wasDigging = isDigging;
            return;
        }

        float limit = isDigging
            ? Mathf.Clamp(diggingMaxSpeed, minSpeed, MaxSpeed)
            : MaxSpeed;

        // 専用減速APIを使わない既存の呼び出しでも、食い込み用上限までの減速を保持する。
        if (isDigging && CurrentSpeed > limit)
        {
            pendingDiggingLimit = isDiggingLimitPending
                ? Mathf.Min(pendingDiggingLimit, limit)
                : limit;
            isDiggingLimitPending = true;
        }

        if (isDiggingLimitPending && CurrentSpeed <= pendingDiggingLimit)
        {
            isDiggingLimitPending = false;
            transitionInitialized = false;
        }

        if (isDiggingLimitPending)
        {
            limit = pendingDiggingLimit;
        }

        bool enteredDigging = isDigging && !wasDigging;
        bool diggingChanged = isDigging != wasDigging;

        TransitionMode nextMode;

        if (isDiggingLimitPending)
        {
            // 食い込み解除・アクセルの押し直しより、確定した目標まで下げる処理を優先する。
            nextMode = TransitionMode.SettlingToDiggingLimit;
        }
        else if (!isAccelerating)
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
            (diggingChanged && !isDiggingLimitPending) ||
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

        // 上限超過の強制減速がない場合は、食い込み開始時の現在値を引き継ぐ。
        if (enteredDigging && !isDiggingLimitPending)
        {
            return;
        }

        bool increasing =
            transitionMode == TransitionMode.Accelerating;

        float duration = Mathf.Max(
    MIN_TRANSITION_DURATION,
    increasing
        ? accelerationTime
        : transitionMode == TransitionMode.SettlingToDiggingLimit
            ? diggingDecelerationTime
            : decelerationTime
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

    // 未設定の曲線は等速として扱い、設定された曲線の値を既存の範囲内で評価する。
    private static float Evaluate(AnimationCurve curve, float time)
    {
        return curve == null || curve.length == 0
            ? time
            : Mathf.Clamp01(curve.Evaluate(time));
    }

    // 現在値から指定値へ減速する準備を行う。現在値が低い場合は増やさない。
    public void BeginBonusDeceleration(float targetSpeed, float duration)
    {
        if (!isActiveAndEnabled || accelerationParameter == null)
        {
            return;
        }

        float target = Mathf.Min(CurrentSpeed, Mathf.Max(0f, targetSpeed));
        if (isBonusDecelerating)
        {
            // 連続した要求で減速時間を先延ばしにしない。新しい目標が低い場合だけ反映する。
            bonusDecelerationTargetSpeed = Mathf.Min(bonusDecelerationTargetSpeed, target);
            return;
        }

        bonusDecelerationStartSpeed = CurrentSpeed;
        bonusDecelerationTargetSpeed = target;
        bonusDecelerationDuration = Mathf.Max(MIN_TRANSITION_DURATION, duration);
        bonusDecelerationElapsed = 0f;
        isBonusDecelerating = CurrentSpeed > bonusDecelerationTargetSpeed;
        // 明示的に渡された目標がある場合は、通常の食い込み上限よりその設定を優先する。
        isDiggingLimitPending = false;
        transitionInitialized = false;
    }

    // 指定秒数で専用減速を進める。同じ物理更新で双方から呼ばれても二重に時間を進めない。
    public void TickBonusDeceleration(float deltaTime)
    {
        if (!isBonusDecelerating || deltaTime <= 0f ||
            lastBonusDecelerationFixedTime == Time.fixedTime)
        {
            return;
        }

        lastBonusDecelerationFixedTime = Time.fixedTime;

        bonusDecelerationElapsed = Mathf.Min(
            bonusDecelerationDuration, bonusDecelerationElapsed + deltaTime);
        float progress = bonusDecelerationElapsed / bonusDecelerationDuration;
        float blend = Mathf.SmoothStep(0f, 1f, progress);
        float speed = Mathf.Lerp(
            bonusDecelerationStartSpeed, bonusDecelerationTargetSpeed, blend);

        // 攻撃などによる別の消費があっても、その分を回復させない。
        CurrentSpeed = Mathf.Min(CurrentSpeed, speed);

        if (bonusDecelerationElapsed >= bonusDecelerationDuration)
        {
            CancelBonusDeceleration();
        }
    }

    // 目標到達後だけ専用減速を終了する。食い込み解除による途中キャンセルでは減速を残す。
    public void CancelBonusDeceleration()
    {
        if (isBonusDecelerating && CurrentSpeed > bonusDecelerationTargetSpeed)
        {
            return;
        }

        isBonusDecelerating = false;
        bonusDecelerationElapsed = 0f;
        transitionInitialized = false;
    }

    // 無効化時は未完了の減速目標まで下げて終了し、再有効化による減速回避を防ぐ。
    private void OnDisable()
    {
        if (isBonusDecelerating)
        {
            CurrentSpeed = Mathf.Min(CurrentSpeed, bonusDecelerationTargetSpeed);
        }
        if (isDiggingLimitPending)
        {
            CurrentSpeed = Mathf.Min(CurrentSpeed, pendingDiggingLimit);
            isDiggingLimitPending = false;
        }
        CancelBonusDeceleration();
    }

    // 指定量を消費する。残量不足の場合は消費せず失敗を返す。
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
