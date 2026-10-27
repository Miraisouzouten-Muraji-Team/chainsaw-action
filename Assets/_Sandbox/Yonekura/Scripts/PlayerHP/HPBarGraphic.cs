using UnityEngine;

/// <summary>
/// HPバーの色とダメージ表示を管理する
/// </summary>
[ExecuteAlways]
public class HPBarGraphic : BaseBarGraphic
{
    private static readonly int DamageFillId =
        Shader.PropertyToID("_DamageFill");
    private static readonly int HPColorId =
        Shader.PropertyToID("_HPColor");
    private static readonly int DamageColorId =
        Shader.PropertyToID("_DamageColor");

    [InspectorName("HP Color")]
    [SerializeField] private Color hpColor = Color.cyan;

    [InspectorName("Damage Color")]
    [SerializeField] private Color damageColor = Color.red;

    /// <summary>
    /// HPバー固有の色設定をマテリアルに反映する
    /// </summary>
    protected override void ApplySpecificSettings()
    {
        SetColorIfAvailable(HPColorId, hpColor);
        SetColorIfAvailable(DamageColorId, damageColor);
    }

    /// <summary>
    /// ダメージ表示の終端位置を設定する
    /// </summary>
    /// <param name="ratio">ダメージ表示の割合。0から1の範囲</param>
    public void SetDamageFill(float ratio)
    {
        SetMaterialFloat(DamageFillId, ratio);
    }
}
