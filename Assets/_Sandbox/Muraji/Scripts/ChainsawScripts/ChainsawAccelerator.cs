using UnityEngine;

// TickはPlayerController.FixedUpdateから1回だけ呼ぶ。
public class ChainsawAccelerator : MonoBehaviour
{
    private const float MIN_TRANSITION_DURATION = 0.01f;

    private const float DEFAULT_CHARGE_SECONDS = 2f;
    private const int CHARGE_TIME_SEARCH_STEPS = 16;

    [Header("アクセル値・食い込み加速・SE")]
    [Tooltip("アクセル値の範囲、食い込み加速と自動減速のカーブ、SEピッチを設定したアセットです。")]
    [SerializeField] private AccelerationParameter accelerationParameter;

    private float minSpeed => accelerationParameter.minSpeed;
    private float maxSpeed => accelerationParameter.maxSpeed;
    private float minPitch => accelerationParameter.minPitch;
    private float maxPitch => accelerationParameter.maxPitch;

    public float CurrentSpeed { get; private set; }

    public float MaxSpeed => Mathf.Max(0.01f, maxSpeed);

    // ボーナス判定には最大値に対する現在値の割合を使う。
    public float SpeedRatio =>
        Mathf.Clamp01(CurrentSpeed / MaxSpeed);

    public float PowerRatio =>
        Mathf.InverseLerp(minSpeed, MaxSpeed, CurrentSpeed);

    public float CurrentPitch =>
        Mathf.Lerp(minPitch, maxPitch, PowerRatio);

    private bool isCharging;
    private float chargeElapsed;
    private float autoDecelerationElapsed;

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
        ResetForRespawn();
    }

    // 復活用に起動時の最低回転数へ戻し、前回の加減速・ボーナス減速の途中状態もすべて破棄する。
    public void ResetForRespawn()
    {
        if (accelerationParameter == null) return;
        CurrentSpeed = minSpeed;
        isCharging = false;
        chargeElapsed = 0f;
        autoDecelerationElapsed = 0f;
        isBonusDecelerating = false;
        bonusDecelerationElapsed = 0f;
        bonusDecelerationDuration = 0f;
        bonusDecelerationStartSpeed = 0f;
        bonusDecelerationTargetSpeed = 0f;
        lastBonusDecelerationFixedTime = float.NegativeInfinity;
    }

    // 食い込み中は回転数を上げ、解除後は自動で減速する。
    public void Tick(float deltaTime, bool isDigging)
    {
        if (accelerationParameter == null || deltaTime <= 0f) return;

        bool settlingBonus = isBonusDecelerating;
        if (settlingBonus)
            TickBonusDeceleration(deltaTime);

        if (!isDigging)
        {
            isCharging = false;
            if (settlingBonus)
                autoDecelerationElapsed = 0f;
            else
                TickAutoDeceleration(deltaTime);
            return;
        }

        autoDecelerationElapsed = 0f;
        if (!settlingBonus)
            TickDiggingCharge(deltaTime);
    }

    // 従来の0～1の減速カーブを設定秒数に伸ばし、食い込み解除時の現在値から最低値まで下げる。
    private void TickAutoDeceleration(float deltaTime)
    {
        if (CurrentSpeed <= minSpeed) return;

        AnimationCurve curve = accelerationParameter.decelerationCurve;
        float duration = Mathf.Max(MIN_TRANSITION_DURATION, accelerationParameter.decelerationTime);
        float previous = Mathf.Clamp01(autoDecelerationElapsed / duration);
        autoDecelerationElapsed = Mathf.Min(duration, autoDecelerationElapsed + deltaTime);
        float next = Mathf.Clamp01(autoDecelerationElapsed / duration);

        // 旧カーブは値が増えるほど減速が進む。曲線終了後も残りがあれば従来どおり減速する。
        float progress = previous >= 1f
            ? deltaTime / duration
            : Mathf.Max(0f, EvaluateProgress(curve, next, 1f) -
                             EvaluateProgress(curve, previous, 1f));
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, minSpeed, progress * (MaxSpeed - minSpeed));
    }

    // 食い込み中は現在値に対応する時点から加速カーブを進め、前回との差分を加える。
    private void TickDiggingCharge(float deltaTime)
    {
        AnimationCurve curve = accelerationParameter.diggingChargeBySeconds;
        float duration = CurveDuration(curve, DEFAULT_CHARGE_SECONDS);
        if (!isCharging)
        {
            chargeElapsed = FindCurveTime(curve, duration, PowerRatio);
            isCharging = true;
        }

        float previous = EvaluateProgress(curve, chargeElapsed, duration);
        chargeElapsed = Mathf.Min(duration, chargeElapsed + deltaTime);
        float next = EvaluateProgress(curve, chargeElapsed, duration);
        float increase = Mathf.Max(0f, next - previous) * (MaxSpeed - minSpeed);
        CurrentSpeed = Mathf.Min(MaxSpeed, CurrentSpeed + increase);
        if (chargeElapsed >= duration && next > 0f)
        {
            float curveTarget = Mathf.Lerp(minSpeed, MaxSpeed, next);
            if (CurrentSpeed < curveTarget)
            {
                if (curveTarget - CurrentSpeed <= 0.001f)
                    CurrentSpeed = curveTarget;
                else
                    isCharging = false; // 消費で最低値を下回っていた分も、続く食い込みで回復する。
            }
        }
    }

    // 加速に入ったときに、現在値に対応するカーブ上の時刻を探す。
    private static float FindCurveTime(AnimationCurve curve, float duration, float ratio)
    {
        if (ratio <= EvaluateProgress(curve, 0f, duration)) return 0f;
        if (ratio >= EvaluateProgress(curve, duration, duration)) return duration;

        float low = 0f;
        float high = duration;
        for (int i = 0; i < CHARGE_TIME_SEARCH_STEPS; i++)
        {
            float middle = (low + high) * 0.5f;
            if (EvaluateProgress(curve, middle, duration) < ratio)
                low = middle;
            else
                high = middle;
        }
        return high;
    }

    // カーブの最後のキーフレームまでを、実際に経過する秒数として使用する。
    private static float CurveDuration(AnimationCurve curve, float fallback)
    {
        return curve == null || curve.length == 0
            ? fallback
            : Mathf.Max(MIN_TRANSITION_DURATION, curve[curve.length - 1].time);
    }

    // カーブを0～1の進行量として読む。
    private static float EvaluateProgress(AnimationCurve curve, float elapsed, float duration)
    {
        if (curve == null || curve.length == 0)
            return Mathf.Clamp01(elapsed / duration);

        return Mathf.Clamp01(curve.Evaluate(elapsed));
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
        isCharging = false;
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
        isCharging = false;
    }

    // 無効化時は未完了の減速目標まで下げて終了し、再有効化による減速回避を防ぐ。
    private void OnDisable()
    {
        if (isBonusDecelerating)
        {
            CurrentSpeed = Mathf.Min(CurrentSpeed, bonusDecelerationTargetSpeed);
        }
        isCharging = false;
        autoDecelerationElapsed = 0f;
        CancelBonusDeceleration();
    }

    // 命中などで得た回転数を上限内で加算し、加速カーブを現在値に合わせ直す。
    public void AddSpeed(float amount)
    {
        if (!isActiveAndEnabled || accelerationParameter == null || !(amount > 0f)) return;

        float nextSpeed = Mathf.Min(MaxSpeed, CurrentSpeed + amount);
        if (nextSpeed <= CurrentSpeed) return;

        CurrentSpeed = nextSpeed;
        isCharging = false;
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
        // 消費後は実際の残量に対応する時点から、食い込み加速を再開する。
        if (amount > 0f) isCharging = false;
        return true;
    }
}
