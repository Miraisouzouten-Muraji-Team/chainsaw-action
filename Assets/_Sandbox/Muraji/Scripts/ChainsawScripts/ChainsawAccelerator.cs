using UnityEngine;

// TickはPlayerController.FixedUpdateから1回だけ呼ぶ。
public class ChainsawAccelerator : MonoBehaviour
{
    private const float MIN_TRANSITION_DURATION = 0.01f;
    [Header("回転速度（回転 / 秒）")]
    [SerializeField, Min(0f)] private float minSpeed = 5f;
    [SerializeField, Min(0.01f)] private float maxSpeed = 50f;
    [Header("最低→最高／最高→最低にかかる時間")]
    [SerializeField, Min(MIN_TRANSITION_DURATION)] private float accelerationTime = 2f;
    [SerializeField, Min(MIN_TRANSITION_DURATION)] private float decelerationTime = 2.5f;
    [SerializeField]
    private AnimationCurve accelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.5f, 0.5f, 3f, 3f), new Keyframe(1f, 1f));
    [SerializeField]
    private AnimationCurve decelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.7f, 0.3f, 1f, 1f), new Keyframe(1f, 1f));
    [Header("SEピッチ")]
    [SerializeField] private float minPitch = 0.8f;
    [SerializeField] private float maxPitch = 1.8f;
    public float CurrentSpeed { get; private set; }
    public float CurrentPitch => Mathf.Lerp(minPitch, maxPitch, Mathf.InverseLerp(minSpeed, maxSpeed, CurrentSpeed));
    public float MaxSpeed => Mathf.Max(0.01f, maxSpeed);
    public float SpeedRatio => Mathf.Clamp01(CurrentSpeed / MaxSpeed);
    public float PowerRatio => Mathf.InverseLerp(minSpeed, maxSpeed, CurrentSpeed);
    private float transitionTime;
    private bool accelerating;

    private void Awake() { CurrentSpeed = minSpeed; }

    public void Tick(bool isAccelerating, float deltaTime)
    {
        if (isAccelerating != accelerating)
        {
            accelerating = isAccelerating;
            transitionTime = 0f;
        }
        float duration = Mathf.Max(MIN_TRANSITION_DURATION, accelerating ? accelerationTime : decelerationTime);
        AnimationCurve curve = accelerating ? accelerationCurve : decelerationCurve;
        float previous = Mathf.Clamp01(transitionTime / duration);
        transitionTime += deltaTime;
        float next = Mathf.Clamp01(transitionTime / duration);
        // カーブの差分を加算する。消費した回転数をLerpで上書きしない。
        float difference = Mathf.Max(0f, Evaluate(curve, next) - Evaluate(curve, previous));
        // カーブ終了後もアクセルを踏んでいれば、消費分を回復できる。
        if (previous >= 1f) difference = deltaTime / duration;
        float amount = difference * Mathf.Max(0.01f, maxSpeed - minSpeed);
        float target = accelerating ? maxSpeed : minSpeed;
        CurrentSpeed = Mathf.MoveTowards(CurrentSpeed, target, amount);
        CurrentSpeed = Mathf.Clamp(CurrentSpeed, 0f, MaxSpeed);
    }

    private static float Evaluate(AnimationCurve curve, float time)
    {
        return curve == null || curve.length == 0 ? time : Mathf.Clamp01(curve.Evaluate(time));
    }

    public bool TryConsume(float amount)
    {
        amount = Mathf.Max(0f, amount);
        if (CurrentSpeed + 0.0001f < amount) return false;
        CurrentSpeed = Mathf.Max(0f, CurrentSpeed - amount);
        return true;
    }

    private void OnValidate()
    {
        minSpeed = Mathf.Max(0f, minSpeed);
        maxSpeed = Mathf.Max(minSpeed + 0.01f, maxSpeed);
    }
}
