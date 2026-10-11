using UnityEngine;

/// <summary>
/// このEnemyが使用するEnemyDataへの参照を保持する。
/// </summary>
/// <remarks>
/// Enemyごとに使用するScriptableObjectをInspectorから設定し、
/// HP・State・攻撃・索敵などの各コンポーネントへ参照を提供する。
///
/// HP管理、State管理、攻撃処理などのゲームロジックは担当しない。
/// </remarks>
public sealed class EnemyDataReference : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("このEnemyが使用する設定データ。")]
    [SerializeField]
    private EnemyData enemyData;

    /// <summary>
    /// このEnemyが使用する設定データを取得する。
    /// </summary>
    public EnemyData Data => enemyData;
}
