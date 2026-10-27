using UnityEngine;

/// <summary>
/// 自動回復タイマーバーの色を管理する。
/// </summary>
[ExecuteAlways]
public class AutoRecoveryBarGraphic : BaseBarGraphic
{
    private static readonly int HPColorId =
        Shader.PropertyToID("_HPColor");

    [InspectorName("Recovery Color")]
    [SerializeField] private Color recoveryColor = Color.green;

    /// <summary>
    /// 回復バー固有の色設定をマテリアルに反映する。
    /// </summary>
    protected override void ApplySpecificSettings()
    {
        SetColorIfAvailable(HPColorId, recoveryColor);
    }
}
