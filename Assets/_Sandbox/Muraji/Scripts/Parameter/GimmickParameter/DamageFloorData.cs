using UnityEngine;

[CreateAssetMenu(fileName = "DamageFloorData", menuName = "Gimmick/ダメージ床設定")]
public sealed class DamageFloorData : ScriptableObject
{
    [Tooltip("最大HPから0になるまでの合計接触秒数です。直線カーブなら、HPが半分のときはこの半分の時間で0になります。")]
    [SerializeField, Min(0.01f)] private float secondsToZero = 3f;

    [Tooltip("Xは合計接触時間の割合、Yは最大HPから失う累計割合です。(0,0)から(1,1)への直線で等速になります。下向きの変化で回復はしません。")]
    [SerializeField] private AnimationCurve damageCurve =
        AnimationCurve.Linear(0f, 0f, 1f, 1f);

    public float SecondsToZero => Mathf.Max(0.01f, secondsToZero);

    // 接触時間の割合から累計減少割合を返し、開始点と終了点を保証する。
    public float EvaluateLoss(float normalizedTime)
    {
        if (normalizedTime <= 0f) return 0f;
        if (normalizedTime >= 1f) return 1f;

        float value = damageCurve != null && damageCurve.length > 0
            ? damageCurve.Evaluate(normalizedTime)
            : normalizedTime;

        // 不正なカーブ値でHPがNaNにならないよう、直線へ戻す。
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = normalizedTime;

        return Mathf.Clamp01(value);
    }
}
