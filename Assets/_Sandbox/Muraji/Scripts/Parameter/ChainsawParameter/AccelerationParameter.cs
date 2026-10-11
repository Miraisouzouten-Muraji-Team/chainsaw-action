using UnityEngine;

/// <summary>チェーンソーの回転速度・加減速曲線・SEピッチの設定値を管理する。</summary>
[CreateAssetMenu(fileName = "AccelerationParameter", menuName = "Chanisaw/Acceleration Parameter")]
public class AccelerationParameter : ScriptableObject
{
    [Header("回転速度（回転 / 秒）")]
    [Tooltip("アクセルを離したときの目標値")]
    [SerializeField, Min(0f)]
    public float minSpeed = 5f;

    [Tooltip("食い込んでいないときの上限")]
    [SerializeField, Min(0.01f)]
    public float maxSpeed = 50f;

    [Tooltip("食い込み中の目標上限")]
    [SerializeField, Min(0.01f)]
    public float diggingMaxSpeed = 35f;

    [Header("加速／減速にかかる時間")]
    [Tooltip("加速曲線の再生時間（秒）")]
    [SerializeField, Min(0.01f)]
    public float accelerationTime = 2f;

    [Tooltip("減速曲線の再生時間（秒）。50→35にも使用")]
    [SerializeField, Min(0.01f)]
    public float decelerationTime = 2.5f;

    [Tooltip("横軸：時間、縦軸：加速の進み具合（0→1）")]
    [SerializeField]
    public AnimationCurve accelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 0.5f, 3f, 3f),
        new Keyframe(1f, 1f)
    );

    [Tooltip("横軸：時間、縦軸：減速の進み具合（0→1）")]
    [SerializeField]
    public AnimationCurve decelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.7f, 0.3f, 1f, 1f),
        new Keyframe(1f, 1f)
    );

    [Header("SEピッチ")]
    [SerializeField] public float minPitch = 0.8f;
    [SerializeField] public float maxPitch = 1.8f;

    /// <summary>元のChainsawAccelerator.OnValidateと同じ範囲制約を維持する。</summary>
    private void OnValidate()
    {
        minSpeed = Mathf.Max(0f, minSpeed);
        maxSpeed = Mathf.Max(minSpeed + 0.01f, maxSpeed);
        diggingMaxSpeed = Mathf.Clamp(diggingMaxSpeed, minSpeed, maxSpeed);
    }
}
