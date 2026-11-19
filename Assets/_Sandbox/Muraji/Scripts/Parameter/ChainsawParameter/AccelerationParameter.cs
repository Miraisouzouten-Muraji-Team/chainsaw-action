using UnityEngine;

/// <summary>チェーンソーの回転速度・食い込み加速・ブレーキ・SEピッチの設定値を管理する。</summary>
[CreateAssetMenu(fileName = "AccelerationParameter", menuName = "Chanisaw/Acceleration Parameter")]
public class AccelerationParameter : ScriptableObject
{
    [Header("回転速度（回転 / 秒）")]
    [Tooltip("初期回転数と、自動減速・ブレーキで目指す最低値です。")]
    [SerializeField, Min(0f)]
    public float minSpeed = 5f;

    [Tooltip("アクセル値の上限です。食い込み加速とボーナスの判定に使います。")]
    [SerializeField, Min(0.01f)]
    public float maxSpeed = 50f;

    [Header("食い込み加速とブレーキ")]
    [Tooltip("食い込み中の加速カーブです。横軸は秒、縦軸は最低値から最大値までの割合です。0秒・0から始め、時間に沿って0～1で上げます。")]
    [SerializeField]
    public AnimationCurve diggingChargeBySeconds =
        AnimationCurve.Linear(0f, 0f, 2f, 1f);

    [Tooltip("食い込み中にブレーキを押している間の減速カーブです。横軸は秒、縦軸は最低値を0、最大値を1としたアクセル値です。0秒・1から始め、時間に沿って1～0に下げます。途中の値から押すと、対応するカーブの位置から減速します。")]
    [SerializeField]
    public AnimationCurve brakeBySeconds =
        AnimationCurve.Linear(0f, 1f, 1f, 0f);

    // 既存アセットの旧設定値は互換性のため保持する。
    [HideInInspector, SerializeField, Min(0.01f)]
    public float diggingMaxSpeed = 35f;

    [HideInInspector, SerializeField, Min(0.01f)]
    public float accelerationTime = 2f;

    [HideInInspector, SerializeField]
    public AnimationCurve accelerationCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 0.5f, 3f, 3f),
        new Keyframe(1f, 1f)
    );

    [Header("食い込んでいないときの自動減速")]
    [Tooltip("自動減速カーブの横軸1が何秒に相当するかを指定します。")]
    [SerializeField, Min(0.01f)]
    public float decelerationTime = 2.5f;

    [Tooltip("食い込み解除後の自動減速に使う従来のカーブです。横軸は減速時間内の割合、縦軸は減速の進み具合です。")]
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
